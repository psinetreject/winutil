using System.Diagnostics;
using Microsoft.Extensions.Logging;
using WinUtil.Core.Abstractions;

namespace WinUtil.Platform.System;

/// <summary>
/// Async wrapper over <see cref="Process"/> for running external command-line tools
/// (winget, choco, dism.exe, oscdimg, ...). Standard output and error are captured concurrently
/// (deadlock-safe), and the caller's <see cref="CancellationToken"/> kills the process tree and
/// propagates cancellation. Arguments pass verbatim through
/// <see cref="ProcessStartInfo.ArgumentList"/> — no manual quoting.
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
    private readonly ILogger<ProcessRunner> _logger;

    public ProcessRunner(ILogger<ProcessRunner> logger) => _logger = logger;

    public async Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };

        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to start process {FileName}", fileName);
            return new ProcessResult(-1, string.Empty, ex.Message);
        }

        // Read both streams concurrently to avoid a full-pipe deadlock.
        Task<string> outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            await ObserveAsync(outputTask).ConfigureAwait(false);
            await ObserveAsync(errorTask).ConfigureAwait(false);
            throw;
        }

        string standardOutput = await outputTask.ConfigureAwait(false);
        string standardError = await errorTask.ConfigureAwait(false);
        return new ProcessResult(process.ExitCode, standardOutput, standardError);
    }

    private void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to kill process {FileName} after cancellation", process.StartInfo.FileName);
        }
    }

    /// <summary>Awaits a stream-reader task purely to observe its outcome so it is never unobserved.</summary>
    private static async Task ObserveAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The process was killed; a faulted/canceled read is expected and irrelevant here.
        }
    }
}
