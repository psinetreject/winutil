using Microsoft.Extensions.Logging;
using WinUtil.Core.Abstractions;

namespace WinUtil.Platform.Packages;

/// <summary>
/// <see cref="IPackageManager"/> backed by the WinGet CLI, driven through the injected
/// <see cref="IProcessRunner"/> (no PowerShell involved).
/// </summary>
/// <remarks>
/// This shells out to <c>winget.exe</c>. The future pure-API route is the native WinGet COM surface
/// (<c>Microsoft.Management.Deployment</c> — the in-proc/out-of-proc <c>PackageManager</c> COM server),
/// which removes the CLI dependency entirely and lets us install, repair, and query packages directly.
/// </remarks>
public sealed class WingetPackageManager(
    IProcessRunner processRunner,
    ILogger<WingetPackageManager> logger) : IPackageManager
{
    private const string Executable = "winget";
    private static readonly string[] VersionArgs = ["--version"];
    private static readonly string[] UpgradeAllArgs = ["upgrade", "--all", "--include-unknown", "--silent"];

    public PackageManagerKind Kind => PackageManagerKind.Winget;

    /// <summary>Probes <c>winget --version</c>; a zero exit code means WinGet is usable.</summary>
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await processRunner.RunAsync(Executable, VersionArgs, cancellationToken).ConfigureAwait(false);
            return result.Succeeded;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "winget availability probe failed.");
            return false;
        }
    }

    // We only verify the CLI is present here. Bootstrapping WinGet itself (the App Installer MSIX) is
    // out of scope for this layer, so a missing WinGet yields actionable guidance rather than a silent
    // failure. The future pure-API path is the native WinGet COM surface (Microsoft.Management.Deployment),
    // which can install/repair WinGet without any CLI or PowerShell dependency.
    public async Task<OperationResult> EnsureInstalledAsync(
        IProgress<TaskProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (await IsAvailableAsync(cancellationToken).ConfigureAwait(false))
        {
            return OperationResult.Ok("WinGet is available.");
        }

        progress?.Report(TaskProgress.Failed("WinGet is not available."));
        return OperationResult.Fail(
            "WinGet (App Installer) is not available. Install 'App Installer' from the Microsoft Store, " +
            "or run Repair-WinGetPackageManager, then retry.");
    }

    public async Task<OperationResult> InstallAsync(string packageId, IProgress<TaskProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var (id, source) = ResolveSource(packageId);
        progress?.Report(TaskProgress.Indeterminate($"Installing {id} via winget ({source})"));

        // install --id <id> --accept-package-agreements --accept-source-agreements --silent --source <winget|msstore>
        string[] args =
        [
            "install", "--id", id,
            "--accept-package-agreements", "--accept-source-agreements",
            "--silent", "--source", source,
        ];

        logger.LogInformation("winget install {PackageId} (source {Source})", id, source);
        var result = await processRunner.RunAsync(Executable, args, cancellationToken).ConfigureAwait(false);
        return ToResult(result, $"install {id}");
    }

    public async Task<OperationResult> UninstallAsync(string packageId, IProgress<TaskProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var (id, source) = ResolveSource(packageId);
        progress?.Report(TaskProgress.Indeterminate($"Uninstalling {id} via winget"));

        string[] args = ["uninstall", "--id", id, "--silent", "--source", source];

        logger.LogInformation("winget uninstall {PackageId} (source {Source})", id, source);
        var result = await processRunner.RunAsync(Executable, args, cancellationToken).ConfigureAwait(false);
        return ToResult(result, $"uninstall {id}");
    }

    public async Task<OperationResult> UpgradeAllAsync(IProgress<TaskProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        progress?.Report(TaskProgress.Indeterminate("Upgrading all winget packages"));
        logger.LogInformation("winget upgrade --all --include-unknown --silent");
        var result = await processRunner.RunAsync(Executable, UpgradeAllArgs, cancellationToken).ConfigureAwait(false);
        return ToResult(result, "upgrade --all");
    }

    // A leading "msstore:" prefix selects the Microsoft Store source instead of the winget community source.
    private static (string Id, string Source) ResolveSource(string packageId)
    {
        const string prefix = "msstore:";
        return packageId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? (packageId[prefix.Length..], "msstore")
            : (packageId, "winget");
    }

    private OperationResult ToResult(ProcessResult result, string action)
    {
        if (result.Succeeded)
        {
            return OperationResult.Ok($"winget {action} succeeded.");
        }

        logger.LogWarning("winget {Action} failed (exit {ExitCode}): {Error}", action, result.ExitCode, result.StandardError);
        var detail = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
        return OperationResult.Fail($"winget {action} failed (exit {result.ExitCode}). {detail}".Trim());
    }
}
