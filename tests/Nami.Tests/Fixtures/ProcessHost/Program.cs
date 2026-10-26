using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

// This executable is only an ownership anchor. The caller supplies a command over a private pipe.
// It remains alive after that command exits, so its process group/job cannot disappear before cleanup.
using var ownership = ProcessOwnership.Create();
await using var control = new NamedPipeClientStream(".", args.Single(), PipeDirection.InOut, PipeOptions.Asynchronous);
await control.ConnectAsync(10_000);
var length = await ReadIntAsync(control);
if (length is < 0 or > 1_048_576) throw new InvalidDataException("Invalid process launch request.");
var bytes = new byte[length];
await control.ReadExactlyAsync(bytes);
using var request = new BinaryReader(new MemoryStream(bytes));
var start = new ProcessStartInfo(request.ReadString())
{
    Arguments = request.ReadString(),
    UseShellExecute = false,
    CreateNoWindow = true
};
var count = request.ReadInt32();
for (var index = 0; index < count; index++) start.ArgumentList.Add(request.ReadString());
Process child;
try
{
    child = Process.Start(start) ?? throw new InvalidOperationException("The command did not start.");
}
catch (Exception error)
{
    await Console.Error.WriteLineAsync(error.ToString());
    await WriteIntAsync(control, 0);
    await WriteIntAsync(control, 125);
    await WaitForDisconnectAsync(control);
    return;
}

using (child)
{
    await WriteIntAsync(control, child.Id);
    // Observe the parent concurrently with the target, including when the target never exits.
    var disconnected = WaitForDisconnectAsync(control);
    var exited = child.WaitForExitAsync();
    if (await Task.WhenAny(exited, disconnected) == disconnected) return;
    await exited;
    await WriteIntAsync(control, child.ExitCode);
    await disconnected;
}

static async Task WaitForDisconnectAsync(Stream stream)
{
    if (await stream.ReadAsync(new byte[1]) != 0)
        throw new InvalidDataException("Unexpected message after process launch.");
}

static async Task<int> ReadIntAsync(Stream stream)
{
    var buffer = new byte[sizeof(int)];
    await stream.ReadExactlyAsync(buffer);
    return System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(buffer);
}

static async Task WriteIntAsync(Stream stream, int value)
{
    var buffer = new byte[sizeof(int)];
    System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
    await stream.WriteAsync(buffer);
    await stream.FlushAsync();
}

internal sealed partial class ProcessOwnership : IDisposable
{
    private const uint KillOnJobClose = 0x2000;
    private const int ExtendedLimitInformation = 9;
    private readonly SafeFileHandle? _job;

    private ProcessOwnership(SafeFileHandle? job) => _job = job;

    internal static ProcessOwnership Create()
    {
        if (!OperatingSystem.IsWindows())
        {
            if (SetProcessGroup(0, 0) != 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
            return new ProcessOwnership(null);
        }

        SafeFileHandle? job = CreateJobObject(IntPtr.Zero, null);
        try
        {
            if (job.IsInvalid) throw new Win32Exception(Marshal.GetLastPInvokeError());
            var limits = new ExtendedLimits { BasicLimitInformation = new BasicLimits { LimitFlags = KillOnJobClose } };
            using var self = Process.GetCurrentProcess();
            if (!SetInformationJobObject(job, ExtendedLimitInformation, in limits, (uint)Marshal.SizeOf<ExtendedLimits>())
                || !AssignProcessToJobObject(job, self.SafeHandle))
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            var ownership = new ProcessOwnership(job);
            job = null;
            return ownership;
        }
        finally
        {
            job?.Dispose();
        }
    }

    public void Dispose()
    {
        if (_job is not null) _job.Dispose();
        else _ = Kill(-Environment.ProcessId, 9);
    }

    // Layouts: https://learn.microsoft.com/windows/win32/api/winnt/ns-winnt-jobobject_extended_limit_information
    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimits
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimits
    {
        public BasicLimits BasicLimitInformation;
        public IoCounters IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    [LibraryImport("libc", EntryPoint = "setpgid", SetLastError = true)]
    private static partial int SetProcessGroup(int process, int group);

    [LibraryImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static partial int Kill(int process, int signal);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateJobObjectW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial SafeFileHandle CreateJobObject(IntPtr attributes, string? name);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetInformationJobObject(SafeFileHandle job, int informationClass,
        in ExtendedLimits information, uint length);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AssignProcessToJobObject(SafeFileHandle job, SafeProcessHandle process);
}
