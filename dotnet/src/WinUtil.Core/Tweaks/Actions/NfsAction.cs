using WinUtil.Core.Abstractions;

namespace WinUtil.Core.Tweaks.Actions;

/// <summary>
/// Configures the NFS client for anonymous UID/GID 0 access and default file permissions. Moderate
/// (NOUNDO). The NFS optional features themselves are enabled by the shared feature-array processor.
/// </summary>
public sealed class NfsAction : ICustomTweakAction
{
    public string Id => "WPFFeaturenfs";
    public bool SupportsUndo => false;

    private const string NfsDefaultPath =
        "HKLM:\\SOFTWARE\\Microsoft\\ClientForNFS\\CurrentVersion\\Default";

    public async Task<OperationResult> ApplyAsync(TweakActionContext context)
    {
        var ct = context.CancellationToken;

        await context.Process.RunAsync("nfsadmin.exe", ["client", "stop"], ct);

        context.Registry.SetValue(NfsDefaultPath, "AnonymousUID", "0", RegistryValueKind.DWord);
        context.Registry.SetValue(NfsDefaultPath, "AnonymousGID", "0", RegistryValueKind.DWord);

        await context.Process.RunAsync("nfsadmin.exe", ["client", "start"], ct);
        await context.Process.RunAsync(
            "nfsadmin.exe",
            ["client", "localhost", "config", "fileaccess=755", "SecFlavors=+sys", "-krb5", "-krb5i"],
            ct);

        return OperationResult.Ok("NFS client configured");
    }

    public Task<OperationResult> UndoAsync(TweakActionContext context)
        => Task.FromResult(OperationResult.Ok("NFS configuration has no undo"));
}
