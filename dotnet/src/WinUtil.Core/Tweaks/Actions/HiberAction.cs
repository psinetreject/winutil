using WinUtil.Core.Abstractions;

namespace WinUtil.Core.Tweaks.Actions;

/// <summary>
/// Disables hibernation. The declarative <c>registry</c> array clears the HibernateEnabled /
/// ShowHibernateOption values; this action runs <c>powercfg /hibernate off</c> to release the
/// hiberfil.sys reservation. Trivial. Undo turns hibernation back on.
/// </summary>
public sealed class HiberAction : ICustomTweakAction
{
    public string Id => "WPFTweaksHiber";
    public bool SupportsUndo => true;

    public async Task<OperationResult> ApplyAsync(TweakActionContext context)
    {
        var result = await context.Process.RunAsync(
            "powercfg.exe", ["/hibernate", "off"], context.CancellationToken);

        return result.Succeeded
            ? OperationResult.Ok("Hibernation disabled")
            : OperationResult.Fail($"Failed to disable hibernation: {result.StandardError}");
    }

    public async Task<OperationResult> UndoAsync(TweakActionContext context)
    {
        var result = await context.Process.RunAsync(
            "powercfg.exe", ["/hibernate", "on"], context.CancellationToken);

        return result.Succeeded
            ? OperationResult.Ok("Hibernation enabled")
            : OperationResult.Fail($"Failed to enable hibernation: {result.StandardError}");
    }
}
