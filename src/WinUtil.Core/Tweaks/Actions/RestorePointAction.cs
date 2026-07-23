using Microsoft.Extensions.DependencyInjection;
using WinUtil.Core.Abstractions;

namespace WinUtil.Core.Tweaks.Actions;

/// <summary>
/// Creates a System Restore checkpoint. Complex (NOUNDO). Delegates to <see cref="ISystemRestoreService"/>,
/// which owns enabling System Restore on the system drive and calling the restore-point API.
/// </summary>
public sealed class RestorePointAction : ICustomTweakAction
{
    public string Id => "WPFTweaksRestorePoint";
    public bool SupportsUndo => false;

    public async Task<OperationResult> ApplyAsync(TweakActionContext context)
    {
        var restore = context.ServiceProvider.GetService<ISystemRestoreService>();
        if (restore is null)
        {
            return OperationResult.Fail("System restore service is unavailable");
        }

        // PARITY: PS first runs `Enable-ComputerRestore -Drive $SystemDrive` when no restore point
        // exists, then `Checkpoint-Computer -RestorePointType MODIFY_SETTINGS`. That enable-if-needed
        // step is expected to live inside ISystemRestoreService.CreateRestorePointAsync.
        return await restore.CreateRestorePointAsync(
            "System Restore Point created by WinUtil", context.CancellationToken);
    }

    public Task<OperationResult> UndoAsync(TweakActionContext context)
        => Task.FromResult(OperationResult.Ok("Restore point creation has no undo"));
}
