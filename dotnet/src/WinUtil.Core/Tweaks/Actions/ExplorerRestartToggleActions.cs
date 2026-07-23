using WinUtil.Core.Abstractions;

namespace WinUtil.Core.Tweaks.Actions;

/// <summary>
/// Shared base for the "Customize Preferences" toggles whose registry values are written by the
/// declarative <c>registry</c> array (in both directions) and whose only imperative step is restarting
/// Explorer so the change takes effect. Ports <c>Invoke-WinUtilExplorerUpdate -action "restart"</c>,
/// which the PowerShell tool runs identically in both InvokeScript and UndoScript — hence Apply and
/// Undo do the same thing here.
/// </summary>
public abstract class ExplorerRestartToggleActionBase : ICustomTweakAction
{
    public abstract string Id { get; }
    public bool SupportsUndo => true;

    public Task<OperationResult> ApplyAsync(TweakActionContext context) => RestartAsync(context);

    public Task<OperationResult> UndoAsync(TweakActionContext context) => RestartAsync(context);

    private static Task<OperationResult> RestartAsync(TweakActionContext context)
    {
        // taskkill /F /IM explorer.exe + Start-Process explorer.exe.
        context.Explorer.RestartExplorer();
        return Task.FromResult(OperationResult.Ok("Explorer restarted to apply the setting"));
    }
}

/// <summary>Reveals hidden files in Explorer (registry: Advanced\Hidden). Toggle.</summary>
public sealed class HiddenFilesAction : ExplorerRestartToggleActionBase
{
    public override string Id => "WPFToggleHiddenFiles";
}

/// <summary>Shows known file extensions in Explorer (registry: Advanced\HideFileExt). Toggle.</summary>
public sealed class ShowExtAction : ExplorerRestartToggleActionBase
{
    public override string Id => "WPFToggleShowExt";
}

/// <summary>Toggles the Start-menu recommendations section (PolicyManager / Explorer policy keys). Toggle.</summary>
public sealed class StartMenuRecommendationsAction : ExplorerRestartToggleActionBase
{
    public override string Id => "WPFToggleStartMenuRecommendations";
}

/// <summary>Toggles taskbar icon alignment left/center (registry: Advanced\TaskbarAl). Toggle.</summary>
public sealed class TaskbarAlignmentAction : ExplorerRestartToggleActionBase
{
    public override string Id => "WPFToggleTaskbarAlignment";
}
