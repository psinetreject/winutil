using WinUtil.Core.Abstractions;

namespace WinUtil.Core.Tweaks.Actions;

/// <summary>
/// Disables Teredo IPv6 tunneling. The declarative <c>registry</c> array sets the Tcpip6
/// DisabledComponents flag; this action issues <c>netsh interface teredo set state disabled</c> so the
/// change takes effect without a reboot. Moderate. Undo returns Teredo to its default state.
/// </summary>
public sealed class TeredoAction : ICustomTweakAction
{
    public string Id => "WPFTweaksTeredo";
    public bool SupportsUndo => true;

    public async Task<OperationResult> ApplyAsync(TweakActionContext context)
    {
        var result = await context.Process.RunAsync(
            "netsh.exe", ["interface", "teredo", "set", "state", "disabled"], context.CancellationToken);

        return result.Succeeded
            ? OperationResult.Ok("Teredo disabled")
            : OperationResult.Fail($"Failed to disable Teredo: {result.StandardError}");
    }

    public async Task<OperationResult> UndoAsync(TweakActionContext context)
    {
        var result = await context.Process.RunAsync(
            "netsh.exe", ["interface", "teredo", "set", "state", "default"], context.CancellationToken);

        return result.Succeeded
            ? OperationResult.Ok("Teredo restored to default")
            : OperationResult.Fail($"Failed to restore Teredo: {result.StandardError}");
    }
}
