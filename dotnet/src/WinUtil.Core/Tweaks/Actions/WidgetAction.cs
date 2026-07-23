using WinUtil.Core.Abstractions;

namespace WinUtil.Core.Tweaks.Actions;

/// <summary>
/// Removes the Windows Widgets: stop the Widgets processes (so removal can't be blocked), remove the
/// WidgetsPlatformRuntime and WebExperience Appx packages for all users, then restart Explorer.
/// Complex (DEL · IRR · NOUNDO).
/// </summary>
public sealed class WidgetAction : ICustomTweakAction
{
    public string Id => "WPFTweaksWidget";
    public bool SupportsUndo => false;

    public async Task<OperationResult> ApplyAsync(TweakActionContext context)
    {
        var ct = context.CancellationToken;

        // "Get-Process *Widget* | Stop-Process" — both the host and service processes.
        await ActionHelpers.KillProcessAsync(context.Process, "Widgets.exe", ct);
        await ActionHelpers.KillProcessAsync(context.Process, "WidgetService.exe", ct);

        await context.Appx.RemoveAsync("Microsoft.WidgetsPlatformRuntime", removeProvisioned: true, ct);
        await context.Appx.RemoveAsync("MicrosoftWindows.Client.WebExperience", removeProvisioned: true, ct);

        context.Explorer.RestartExplorer();

        return OperationResult.Ok("Removed widgets");
    }

    public Task<OperationResult> UndoAsync(TweakActionContext context)
        => Task.FromResult(OperationResult.Ok("Widgets tweak has no undo"));
}
