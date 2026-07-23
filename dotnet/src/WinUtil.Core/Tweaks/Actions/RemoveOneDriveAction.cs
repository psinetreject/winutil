using WinUtil.Core.Abstractions;

namespace WinUtil.Core.Tweaks.Actions;

/// <summary>
/// Removes OneDrive: run the built-in uninstaller, kill the processes that hold its files open, delete
/// the leftover data directories, drop the empty user folder + env var, and disable OneSyncSvc.
/// Complex (DEL · DL · IRR). Undo reinstalls via winget and re-enables the service.
/// </summary>
public sealed class RemoveOneDriveAction : ICustomTweakAction
{
    public string Id => "WPFTweaksRemoveOneDrive";
    public bool SupportsUndo => true;

    public async Task<OperationResult> ApplyAsync(TweakActionContext context)
    {
        var ct = context.CancellationToken;
        var oneDrive = Environment.GetEnvironmentVariable("OneDrive");
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

        // Deny delete on the OneDrive folder so nothing races the uninstaller into it.
        if (!string.IsNullOrWhiteSpace(oneDrive) && Directory.Exists(oneDrive))
        {
            await context.Process.RunAsync("icacls.exe", [oneDrive, "/deny", "Administrators:(D,DC)"], ct);
        }

        // Built-in uninstaller (System32\OneDriveSetup.exe /uninstall), waited on.
        var uninstaller = Path.Combine(ActionHelpers.SystemRoot, "System32", "OneDriveSetup.exe");
        if (File.Exists(uninstaller))
        {
            await context.Process.RunAsync(uninstaller, ["/uninstall"], ct);
        }

        // Release handles: OneDrive files are held by FileCoAuth and Explorer.
        await ActionHelpers.KillProcessAsync(context.Process, "FileCoAuth.exe", ct);
        await ActionHelpers.KillProcessAsync(context.Process, "explorer.exe", ct);

        ActionHelpers.TryDeleteDirectory(Path.Combine(localAppData, "Microsoft", "OneDrive"));
        ActionHelpers.TryDeleteDirectory(Path.Combine(programData, "Microsoft OneDrive"));

        if (!string.IsNullOrWhiteSpace(oneDrive) && Directory.Exists(oneDrive))
        {
            await context.Process.RunAsync("icacls.exe", [oneDrive, "/grant", "Administrators:(D,DC)"], ct);

            if (!Directory.EnumerateFileSystemEntries(oneDrive).Any())
            {
                ActionHelpers.TryDeleteDirectory(oneDrive);
                // Clear the per-user OneDrive env var (HKCU\Environment) — equivalent to
                // [Environment]::SetEnvironmentVariable('OneDrive', $null, 'User').
                context.Registry.DeleteValue("HKCU:\\Environment", "OneDrive");
            }
        }

        context.Services.SetStartupType("OneSyncSvc", ServiceStartupType.Disabled);

        // Bring the shell back after killing Explorer above.
        context.Explorer.RestartExplorer();

        return OperationResult.Ok("OneDrive removed");
    }

    public async Task<OperationResult> UndoAsync(TweakActionContext context)
    {
        var ct = context.CancellationToken;

        // PARITY: PS runs `winget install Microsoft.Onedrive --source winget`; the accept-* flags are
        // added so the reinstall does not stall on a source-agreement prompt in a non-interactive run.
        var result = await context.Process.RunAsync(
            "winget.exe",
            ["install", "Microsoft.Onedrive", "--source", "winget", "--accept-source-agreements", "--accept-package-agreements"],
            ct);

        context.Services.SetStartupType("OneSyncSvc", ServiceStartupType.Automatic);

        return result.Succeeded
            ? OperationResult.Ok("OneDrive reinstalled")
            : OperationResult.Fail($"OneDrive reinstall failed: {result.StandardError}");
    }
}
