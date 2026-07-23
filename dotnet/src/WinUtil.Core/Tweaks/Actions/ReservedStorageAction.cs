using WinUtil.Core.Abstractions;

namespace WinUtil.Core.Tweaks.Actions;

/// <summary>
/// Toggles Windows Reserved Storage via <c>DISM /Online /Set-ReservedStorageState</c>. Complex.
/// Apply disables it; Undo re-enables it.
/// </summary>
public sealed class ReservedStorageAction : ICustomTweakAction
{
    public string Id => "WPFTweaksReservedStorage";
    public bool SupportsUndo => true;

    public Task<OperationResult> ApplyAsync(TweakActionContext context) => SetStateAsync(context, "Disabled");

    public Task<OperationResult> UndoAsync(TweakActionContext context) => SetStateAsync(context, "Enabled");

    // No managed reserved-storage API; DISM is the documented tool path.
    private static async Task<OperationResult> SetStateAsync(TweakActionContext context, string state)
    {
        var result = await context.Process.RunAsync(
            "DISM.exe", ["/Online", "/Set-ReservedStorageState", $"/State:{state}"], context.CancellationToken);

        return result.Succeeded
            ? OperationResult.Ok($"Reserved storage {state.ToLowerInvariant()}")
            : OperationResult.Fail($"DISM reserved-storage state exited {result.ExitCode}: {result.StandardError}");
    }
}
