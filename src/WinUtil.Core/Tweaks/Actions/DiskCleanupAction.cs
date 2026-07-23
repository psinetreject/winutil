using WinUtil.Core.Abstractions;

namespace WinUtil.Core.Tweaks.Actions;

/// <summary>
/// Runs the aggressive disk cleanup: <c>cleanmgr /VERYLOWDISK</c> followed by a DISM component-store
/// cleanup with <c>/ResetBase</c> (permanently removes superseded update components).
/// Complex (IRR · NOUNDO).
/// </summary>
public sealed class DiskCleanupAction : ICustomTweakAction
{
    public string Id => "WPFTweaksDiskCleanup";
    public bool SupportsUndo => false;

    public async Task<OperationResult> ApplyAsync(TweakActionContext context)
    {
        var ct = context.CancellationToken;

        // cleanmgr /VERYLOWDISK runs every handler silently on C:.
        await context.Process.RunAsync("cleanmgr.exe", ["/d", "C:", "/VERYLOWDISK"], ct);

        // DISM component cleanup + ResetBase — no managed equivalent, so DISM is invoked directly.
        var dism = await context.Process.RunAsync(
            "Dism.exe", ["/online", "/Cleanup-Image", "/StartComponentCleanup", "/ResetBase"], ct);

        return dism.Succeeded
            ? OperationResult.Ok("Disk cleanup complete")
            : OperationResult.Fail($"DISM component cleanup exited {dism.ExitCode}: {dism.StandardError}");
    }

    public Task<OperationResult> UndoAsync(TweakActionContext context)
        => Task.FromResult(OperationResult.Ok("Disk cleanup has no undo"));
}
