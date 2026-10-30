using System.Buffers.Binary;
using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;

namespace Nami.Tests;

/// <summary>Retains a private process group/job until the command and all its descendants are terminated.</summary>
internal sealed partial class OwnedProcess : IAsyncDisposable
{
    private readonly NamedPipeServerStream _control;
    private readonly Task<int> _exit;
    private readonly Lazy<Task> _gracefulTermination;
    private readonly Process _host;
    private readonly Lazy<Task> _termination;
    private readonly TimeProvider _time;
    private int _disposed;
    private int _forced;

    private OwnedProcess(Process host, NamedPipeServerStream control, int id, TimeProvider time)
    {
        _host = host;
        _control = control;
        Id = id;
        _time = time;
        _exit = ReadExitAsync();
        _termination = new Lazy<Task>(KillCoreAsync);
        _gracefulTermination = new Lazy<Task>(TerminateCoreAsync);
    }

    internal int Id { get; }
    internal bool HasExited => _exit.IsCompletedSuccessfully || Volatile.Read(ref _forced) != 0;

    internal int ExitCode => _exit.IsCompletedSuccessfully ? _exit.Result
        : Volatile.Read(ref _forced) != 0 ? -1 : throw new InvalidOperationException("The command is still running.");

    internal StreamWriter StandardInput => _host.StandardInput;
    internal StreamReader StandardOutput => _host.StandardOutput;
    internal StreamReader StandardError => _host.StandardError;

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try
        {
            await _termination.Value.ConfigureAwait(false);
        }
        finally
        {
            await _control.DisposeAsync().ConfigureAwait(false);
            _host.Dispose();
        }
    }

    internal static async Task<OwnedProcess> StartAsync(ProcessStartInfo start, CancellationToken cancellationToken,
        TimeProvider? timeProvider = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var time = timeProvider ?? TimeProvider.System;
        var assembly = Path.Combine(AppContext.BaseDirectory, "process-host", "Nami.ProcessHost.dll");
        if (!File.Exists(assembly))
            throw new FileNotFoundException("The process ownership host was not staged.", assembly);
        var pipeName = CreatePipeName(Path.GetTempPath());
        NamedPipeServerStream? control = null;
        Process? host = null;
        var connected = false;
        try
        {
            control = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            var hostStart = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = start.WorkingDirectory,
                RedirectStandardInput = start.RedirectStandardInput,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = start.StandardOutputEncoding,
                StandardErrorEncoding = start.StandardErrorEncoding
            };
            hostStart.ArgumentList.Add(assembly);
            hostStart.ArgumentList.Add(pipeName);
            hostStart.Environment.Clear();
            foreach (var pair in start.Environment) hostStart.Environment.Add(pair);
            host = Process.Start(hostStart) ??
                   throw new InvalidOperationException("The process ownership host did not start.");
            using var startup = new CancellationTokenSource(TimeSpan.FromSeconds(10), time);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, startup.Token);
            await control.WaitForConnectionAsync(deadline.Token).ConfigureAwait(false);
            connected = true;
            await using var request = new MemoryStream();
            await using (var writer = new BinaryWriter(request, Encoding.UTF8, true))
            {
                writer.Write(start.FileName);
                writer.Write(start.Arguments);
                writer.Write(start.ArgumentList.Count);
                foreach (var argument in start.ArgumentList) writer.Write(argument);
            }

            var bytes = request.ToArray();
            var length = new byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
            await control.WriteAsync(length, deadline.Token).ConfigureAwait(false);
            await control.WriteAsync(bytes, deadline.Token).ConfigureAwait(false);
            await control.FlushAsync(deadline.Token).ConfigureAwait(false);
            var id = await ReadIntAsync(control, deadline.Token).ConfigureAwait(false);
            if (id <= 0) throw new InvalidOperationException($"Could not start {start.FileName}.");
            var owned = new OwnedProcess(host, control, id, time);
            host = null;
            control = null;
            return owned;
        }
        catch (Exception startupFailure)
        {
            var failures = new List<Exception> { startupFailure };
            try
            {
                if (host is not null)
                {
                    Terminate(host, connected);
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10), time);
                    await host.WaitForExitAsync(cleanup.Token).ConfigureAwait(false);
                    var output = host.StandardOutput.ReadToEndAsync(cleanup.Token);
                    var error = host.StandardError.ReadToEndAsync(cleanup.Token);
                    await Task.WhenAll(output, error).ConfigureAwait(false);
                    startupFailure.Data["Output"] = output.Result;
                    startupFailure.Data["Error"] = error.Result;
                }
            }
            catch (Exception cleanupFailure)
            {
                failures.Add(cleanupFailure);
            }

            if (failures.Count > 1) throw new AggregateException("Process startup and cleanup failed.", failures);
            throw;
        }
        finally
        {
            await using (control)
            {
                host?.Dispose();
            }
        }
    }

    internal static string CreatePipeName(string temporaryDirectory)
    {
        var name = Guid.NewGuid().ToString("N");
        if (OperatingSystem.IsWindows()
            || Encoding.UTF8.GetByteCount(Path.Combine(temporaryDirectory, "CoreFxPipe_" + name)) < 104)
            return name;

        // .NET supports absolute Unix pipe paths for temporary directories exceeding sockaddr_un.sun_path.
        return Path.Combine("/tmp", "nami-" + name);
    }

    internal async Task WaitForExitAsync(CancellationToken cancellationToken)
    {
        _ = await _exit.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    internal Task KillAsync(CancellationToken cancellationToken)
    {
        return _termination.Value.WaitAsync(cancellationToken);
    }

    /// <summary>Requests command shutdown and waits for exit; disposal still owns descendant cleanup.</summary>
    internal Task TerminateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _gracefulTermination.Value.WaitAsync(cancellationToken);
    }

    private async Task TerminateCoreAsync()
    {
        // Windows console processes have no equivalent to SIGTERM. Keep their existing job cleanup.
        if (OperatingSystem.IsWindows())
        {
            await _termination.Value.ConfigureAwait(false);
            return;
        }

        // Signal only the command: the ownership host must remain alive to report its exit and
        // retain descendants until KillAsync/DisposeAsync releases the complete process group.
        if (!_exit.IsCompleted && Kill(Id, 15) != 0)
        {
            var error = Marshal.GetLastPInvokeError();
            if (error != 3) throw new Win32Exception(error); // ESRCH: the command exited concurrently.
        }

        _ = await _exit.ConfigureAwait(false);
    }

    private async Task KillCoreAsync()
    {
        if (!_exit.IsCompletedSuccessfully) Interlocked.Exchange(ref _forced, 1);
        Terminate(_host, true);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10), _time);
        await _host.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
        // Observe the expected control-pipe closure after forced termination.
        try
        {
            _ = await _exit.WaitAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (IOException) when (Volatile.Read(ref _forced) != 0)
        {
        }
    }

    private async Task<int> ReadExitAsync()
    {
        return await ReadIntAsync(_control, CancellationToken.None).ConfigureAwait(false);
    }

    private static async Task<int> ReadIntAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[sizeof(int)];
        await stream.ReadExactlyAsync(buffer, cancellationToken).ConfigureAwait(false);
        return BinaryPrimitives.ReadInt32LittleEndian(buffer);
    }

    private static void Terminate(Process host, bool groupEstablished)
    {
        if (!OperatingSystem.IsWindows() && groupEstablished)
        {
            if (Kill(-host.Id, 9) != 0 && !host.HasExited)
                throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
        else if (!host.HasExited)
        {
            try
            {
                host.Kill();
            }
            catch (InvalidOperationException) when (host.HasExited)
            {
            }
        }
    }

    [LibraryImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static partial int Kill(int process, int signal);
}
