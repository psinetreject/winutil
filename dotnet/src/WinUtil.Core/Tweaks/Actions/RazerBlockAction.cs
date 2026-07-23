using WinUtil.Core.Abstractions;

namespace WinUtil.Core.Tweaks.Actions;

/// <summary>
/// Blocks the Razer bloatware auto-installer: ensure an empty <c>%SystemRoot%\Installer\Razer</c> folder
/// exists, then deny Everyone write access so the Razer installer cannot repopulate it. Moderate (DEL).
/// Undo removes the deny ACE.
/// </summary>
public sealed class RazerBlockAction : ICustomTweakAction
{
    public string Id => "WPFTweaksRazerBlock";
    public bool SupportsUndo => true;

    private static string RazerPath => Path.Combine(ActionHelpers.SystemRoot, "Installer", "Razer");

    public async Task<OperationResult> ApplyAsync(TweakActionContext context)
    {
        var path = RazerPath;

        if (Directory.Exists(path))
        {
            ActionHelpers.TryDeleteContents(path);
        }
        else
        {
            try
            {
                Directory.CreateDirectory(path);
            }
            catch (IOException ex)
            {
                return OperationResult.Fail($"Failed to create {path}: {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                return OperationResult.Fail($"Failed to create {path}: {ex.Message}");
            }
        }

        var result = await context.Process.RunAsync("icacls.exe", [path, "/deny", "Everyone:(W)"], context.CancellationToken);
        return result.Succeeded
            ? OperationResult.Ok("Razer installer folder locked")
            : OperationResult.Fail($"icacls deny exited {result.ExitCode}: {result.StandardError}");
    }

    public async Task<OperationResult> UndoAsync(TweakActionContext context)
    {
        var result = await context.Process.RunAsync("icacls.exe", [RazerPath, "/remove:d", "Everyone"], context.CancellationToken);
        return result.Succeeded
            ? OperationResult.Ok("Razer installer folder unlocked")
            : OperationResult.Fail($"icacls remove exited {result.ExitCode}: {result.StandardError}");
    }
}
