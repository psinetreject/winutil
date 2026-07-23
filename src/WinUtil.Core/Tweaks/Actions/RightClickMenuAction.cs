using WinUtil.Core.Abstractions;

namespace WinUtil.Core.Tweaks.Actions;

/// <summary>
/// Restores the classic (Windows 10) File Explorer context menu by registering an empty
/// InprocServer32 default value under the "new context menu" CLSID, then restarting Explorer so the
/// change is visible. Moderate. Undo removes the CLSID key tree.
/// </summary>
public sealed class RightClickMenuAction : ICustomTweakAction
{
    public string Id => "WPFTweaksRightClickMenu";
    public bool SupportsUndo => true;

    // The CLSID whose presence (with a blank InprocServer32) suppresses the Windows 11 command bar and
    // brings back the legacy right-click menu.
    private const string ClsidPath =
        "HKCU:\\Software\\Classes\\CLSID\\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}";
    private const string InprocServer32Path = ClsidPath + "\\InprocServer32";

    public Task<OperationResult> ApplyAsync(TweakActionContext context)
    {
        // Equivalent to `New-Item ... -Name InprocServer32 -Value "" -Force`: create the sub-key and set
        // its (default) value to an empty string.
        var result = context.Registry.SetValue(InprocServer32Path, string.Empty, string.Empty, RegistryValueKind.String);
        if (!result.Success)
        {
            return Task.FromResult(result);
        }

        // PS runs `Stop-Process -Name explorer` (the shell auto-respawns); RestartExplorer both stops and
        // relaunches it for the same visible effect.
        context.Explorer.RestartExplorer();

        return Task.FromResult(OperationResult.Ok("Classic right-click menu enabled"));
    }

    public async Task<OperationResult> UndoAsync(TweakActionContext context)
    {
        // Mirrors `Remove-Item ... -Recurse`. IRegistryService has no key-tree delete, so reg.exe is used
        // (its `delete /f` removes the key and all sub-keys). An absent key returns non-zero and is
        // ignored, keeping the undo idempotent.
        await context.Process.RunAsync(
            "reg.exe", ["delete", ActionHelpers.ToRegExePath(ClsidPath), "/f"], context.CancellationToken);

        // PARITY: the PowerShell UndoScript only removes the key and does not restart Explorer (the
        // classic menu reverts on the next shell restart). This restarts Explorer so the revert is
        // immediately visible — a deliberate, benign enhancement that matches the apply direction.
        context.Explorer.RestartExplorer();

        return OperationResult.Ok("Classic right-click menu disabled");
    }
}
