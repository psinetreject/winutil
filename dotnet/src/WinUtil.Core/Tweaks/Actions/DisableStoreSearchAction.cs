using WinUtil.Core.Abstractions;

namespace WinUtil.Core.Tweaks.Actions;

/// <summary>
/// Stops the Start-menu search surfacing recommended Microsoft Store apps by denying Everyone full
/// control of the Store's <c>store.db</c> (the local app catalog it reads to build suggestions).
/// Trivial. Undo restores the grant.
/// </summary>
public sealed class DisableStoreSearchAction : ICustomTweakAction
{
    public string Id => "WPFTweaksDisableStoreSearch";
    public bool SupportsUndo => true;

    // %LocalAppData%\Packages\Microsoft.WindowsStore_8wekyb3d8bbwe\LocalState\store.db — the Store's
    // local catalog database whose ACL the tweak flips.
    private static string StoreDbPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Packages",
        "Microsoft.WindowsStore_8wekyb3d8bbwe",
        "LocalState",
        "store.db");

    public async Task<OperationResult> ApplyAsync(TweakActionContext context)
    {
        var result = await context.Process.RunAsync(
            "icacls.exe", [StoreDbPath, "/deny", "Everyone:F"], context.CancellationToken);

        // PARITY: the PowerShell tool does not inspect icacls's exit code, so an absent store.db is
        // silently tolerated there; here a non-zero result (e.g. the file is missing) is surfaced as a
        // failure so the user knows the deny did not take effect.
        return result.Succeeded
            ? OperationResult.Ok("Store search recommendations disabled")
            : OperationResult.Fail($"Failed to deny access to store.db: {result.StandardError}");
    }

    public async Task<OperationResult> UndoAsync(TweakActionContext context)
    {
        var result = await context.Process.RunAsync(
            "icacls.exe", [StoreDbPath, "/grant", "Everyone:F"], context.CancellationToken);

        return result.Succeeded
            ? OperationResult.Ok("Store search recommendations restored")
            : OperationResult.Fail($"Failed to grant access to store.db: {result.StandardError}");
    }
}
