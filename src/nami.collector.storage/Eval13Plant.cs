using System.Diagnostics;

namespace Nami.Collector.Storage;

// evaluator-13 check only, never merged: which platform-analyzer findings remain with Windows removed from SupportedPlatform.
internal static class Eval13Plant
{
    // [UnsupportedOSPlatform("windows")]: expected to compile without CA1416.
    internal static void UnixOnly(string path) => File.SetUnixFileMode(path, UnixFileMode.UserRead);

    // [SupportedOSPlatform("windows"), SupportedOSPlatform("linux")]: expected CA1416, macOS reaches this call.
    internal static void LinuxAndWindowsOnly(Process process) => process.ProcessorAffinity = 1;

    // [SupportedOSPlatform("windows")]: expected CA1416.
    internal static void WindowsOnly() => Console.Beep(440, 100);
}
