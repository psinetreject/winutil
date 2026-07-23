using WinUtil.Core.Abstractions;

namespace WinUtil.Core.Tweaks.Actions;

/// <summary>
/// Removes Microsoft Edge: resolve the versioned <c>setup.exe</c>, plant a dummy legacy
/// <c>MicrosoftEdge.exe</c> to unlock the uninstaller, then run the system-level uninstall.
/// Complex (DEL · DL · IRR). Undo reinstalls Edge via winget.
/// </summary>
public sealed class RemoveEdgeAction : ICustomTweakAction
{
    public string Id => "WPFTweaksRemoveEdge";
    public bool SupportsUndo => true;

    public async Task<OperationResult> ApplyAsync(TweakActionContext context)
    {
        var ct = context.CancellationToken;

        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var applicationDir = Path.Combine(programFilesX86, "Microsoft", "Edge", "Application");
        if (!Directory.Exists(applicationDir))
        {
            return OperationResult.Ok("Microsoft Edge is not installed");
        }

        // Resolve ...\Application\<version>\Installer\setup.exe, taking the last (highest) version.
        // PARITY: PS uses `Resolve-Path ... | Select-Object -Last 1`; ordinal sort matches for the
        // fixed-width version folders Edge produces.
        var setup = Directory.EnumerateDirectories(applicationDir)
            .Select(dir => Path.Combine(dir, "Installer", "setup.exe"))
            .Where(File.Exists)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .LastOrDefault();

        if (setup is null)
        {
            return OperationResult.Ok("Microsoft Edge is not installed");
        }

        // Plant a dummy legacy Edge exe — its presence lets the uninstaller proceed.
        var dummy = Path.Combine(
            ActionHelpers.SystemRoot, "SystemApps", "Microsoft.MicrosoftEdge_8wekyb3d8bbwe", "MicrosoftEdge.exe");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dummy)!);
            if (!File.Exists(dummy))
            {
                // Create an empty placeholder file (mirrors PowerShell New-Item -Force).
                using (File.Create(dummy))
                {
                }
            }
        }
        catch (IOException)
        {
            // Non-fatal: the uninstall may still succeed.
        }
        catch (UnauthorizedAccessException)
        {
        }

        var result = await context.Process.RunAsync(
            setup, ["--uninstall", "--system-level", "--force-uninstall", "--delete-profile"], ct);

        return result.Succeeded
            ? OperationResult.Ok("Microsoft Edge was removed")
            : OperationResult.Fail($"Edge uninstall exited {result.ExitCode}: {result.StandardError}");
    }

    public async Task<OperationResult> UndoAsync(TweakActionContext context)
    {
        // PARITY: PS runs `winget install Microsoft.Edge --source winget`; accept-* flags added for
        // non-interactive reinstall.
        var result = await context.Process.RunAsync(
            "winget.exe",
            ["install", "Microsoft.Edge", "--source", "winget", "--accept-source-agreements", "--accept-package-agreements"],
            context.CancellationToken);

        return result.Succeeded
            ? OperationResult.Ok("Microsoft Edge reinstalled")
            : OperationResult.Fail($"Edge reinstall failed: {result.StandardError}");
    }
}
