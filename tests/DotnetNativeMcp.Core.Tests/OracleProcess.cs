using System.ComponentModel;
using System.Diagnostics;

namespace DotnetNativeMcp.Core.Tests;

/// <summary>
/// Shared, safe process runner for the differential-test oracles (<see cref="ReadelfOracle"/>,
/// <see cref="ObjdumpOracle"/>). Captures stdout, draining both pipes concurrently with the wait,
/// and returns <c>null</c> when the tool is missing, hangs, or exits non-zero — so the harnesses
/// skip cleanly on hosts without binutils. See docs/differential-testing.md.
/// </summary>
internal static class OracleProcess
{
    /// <summary>
    /// Runs <paramref name="fileName"/> with <paramref name="args"/> and returns its stdout, or
    /// <c>null</c> if the tool is unavailable, times out, or exits non-zero.
    /// </summary>
    public static string? Run(string fileName, params string[] args)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        try
        {
            using var proc = Process.Start(psi);
            if (proc is null) return null;

            // Drain both streams concurrently with the wait: reading stdout to EOF *before*
            // waiting would defeat the timeout if the tool hangs, and an undrained stderr can
            // deadlock once its pipe buffer fills.
            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();

            if (!proc.WaitForExit(30_000))
            {
                try { proc.Kill(entireProcessTree: true); } catch { /* best effort */ }
                return null;
            }

            // Process has exited; the reads should complete promptly. Bound the join anyway.
            if (!Task.WaitAll([stdoutTask, stderrTask], 5_000))
                return null;

            return proc.ExitCode == 0 ? stdoutTask.Result : null;
        }
        catch (Win32Exception)
        {
            return null; // tool not installed
        }
    }
}
