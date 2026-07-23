using WinUtil.Core.Abstractions;

namespace WinUtil.Core.Tweaks.Actions;

/// <summary>
/// Deletes the contents of the user and system TEMP folders. Moderate (DEL · IRR · NOUNDO).
/// </summary>
public sealed class DeleteTempFilesAction : ICustomTweakAction
{
    public string Id => "WPFTweaksDeleteTempFiles";
    public bool SupportsUndo => false;

    public Task<OperationResult> ApplyAsync(TweakActionContext context)
    {
        // %Temp%\* — the current user's temp directory.
        ActionHelpers.TryDeleteContents(Path.GetTempPath());

        // %SystemRoot%\Temp\* — the machine temp directory.
        ActionHelpers.TryDeleteContents(Path.Combine(ActionHelpers.SystemRoot, "Temp"));

        return Task.FromResult(OperationResult.Ok("Temp files deleted"));
    }

    public Task<OperationResult> UndoAsync(TweakActionContext context)
        => Task.FromResult(OperationResult.Ok("Temp-file deletion has no undo"));
}
