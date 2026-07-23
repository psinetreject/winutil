using WinUtil.Core.Abstractions;

namespace WinUtil.Core.Tweaks.Actions;

/// <summary>
/// Toggles Windows dark mode. Both theme keys (AppsUseLightTheme + SystemUsesLightTheme) are written by
/// the declarative <c>registry</c> array in either direction; this action just broadcasts the settings
/// change so the running shell re-reads the theme without a restart. Toggle.
/// </summary>
public sealed class DarkModeAction : ICustomTweakAction
{
    public string Id => "WPFToggleDarkMode";
    public bool SupportsUndo => true;

    // PARITY: the PowerShell InvokeScript/UndoScript are identical — call Invoke-WinUtilExplorerUpdate
    // (default "refresh" ⇒ WM_SETTINGCHANGE broadcast) and, only inside the WPF UI, flip the app's own
    // theme button to "Auto". That button is UI-only and has no equivalent in Core, so only the
    // broadcast is ported. Both theme registry values are handled by the declarative registry array.

    public Task<OperationResult> ApplyAsync(TweakActionContext context)
    {
        context.Explorer.BroadcastSettingChange();
        return Task.FromResult(OperationResult.Ok("Dark mode enabled"));
    }

    public Task<OperationResult> UndoAsync(TweakActionContext context)
    {
        context.Explorer.BroadcastSettingChange();
        return Task.FromResult(OperationResult.Ok("Dark mode disabled"));
    }
}
