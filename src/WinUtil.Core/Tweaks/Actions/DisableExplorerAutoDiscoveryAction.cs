using WinUtil.Core.Abstractions;

namespace WinUtil.Core.Tweaks.Actions;

/// <summary>
/// Stops Explorer auto-detecting folder types: flush the per-folder view database (Bags / BagMRU) and,
/// on apply, pin every folder to the generic "NotSpecified" view. Moderate. Undo just flushes the
/// database.
/// </summary>
public sealed class DisableExplorerAutoDiscoveryAction : ICustomTweakAction
{
    public string Id => "WPFTweaksDisableExplorerAutoDiscovery";
    public bool SupportsUndo => true;

    private const string BagsPath =
        "HKCU:\\Software\\Classes\\Local Settings\\Software\\Microsoft\\Windows\\Shell\\Bags";
    private const string BagMruPath =
        "HKCU:\\Software\\Classes\\Local Settings\\Software\\Microsoft\\Windows\\Shell\\BagMRU";
    private const string AllFoldersShellPath =
        "HKCU:\\Software\\Classes\\Local Settings\\Software\\Microsoft\\Windows\\Shell\\Bags\\AllFolders\\Shell";

    public async Task<OperationResult> ApplyAsync(TweakActionContext context)
    {
        await FlushViewDatabaseAsync(context);

        // Set a generic folder view for every folder. SetValue creates the key path as needed.
        context.Registry.SetValue(AllFoldersShellPath, "FolderType", "NotSpecified", RegistryValueKind.String);

        return OperationResult.Ok("Explorer auto-discovery disabled (sign out to apply)");
    }

    public async Task<OperationResult> UndoAsync(TweakActionContext context)
    {
        await FlushViewDatabaseAsync(context);
        return OperationResult.Ok("Explorer view database reset (sign out to apply)");
    }

    // Recursively removes the Bags/BagMRU trees. IRegistryService has no key-tree delete, so reg.exe is
    // used (its `delete /f` removes the key and all subkeys). Absent keys return non-zero and are ignored.
    private static async Task FlushViewDatabaseAsync(TweakActionContext context)
    {
        var ct = context.CancellationToken;
        await context.Process.RunAsync("reg.exe", ["delete", ActionHelpers.ToRegExePath(BagsPath), "/f"], ct);
        await context.Process.RunAsync("reg.exe", ["delete", ActionHelpers.ToRegExePath(BagMruPath), "/f"], ct);
    }
}
