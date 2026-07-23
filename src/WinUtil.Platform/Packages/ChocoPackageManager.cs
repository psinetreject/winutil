using Microsoft.Extensions.Logging;
using WinUtil.Core.Abstractions;

namespace WinUtil.Platform.Packages;

/// <summary>
/// <see cref="IPackageManager"/> backed by the Chocolatey CLI, driven through the injected
/// <see cref="IProcessRunner"/>.
/// </summary>
public sealed class ChocoPackageManager(
    IProcessRunner processRunner,
    ILogger<ChocoPackageManager> logger) : IPackageManager
{
    private const string Executable = "choco";
    private const string InstallScriptUri = "https://community.chocolatey.org/install.ps1";
    private static readonly string[] VersionArgs = ["--version"];
    private static readonly string[] UpgradeAllArgs = ["upgrade", "all", "-y"];

    public PackageManagerKind Kind => PackageManagerKind.Choco;

    /// <summary>Probes <c>choco --version</c>; a zero exit code means Chocolatey is usable.</summary>
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await processRunner.RunAsync(Executable, VersionArgs, cancellationToken).ConfigureAwait(false);
            return result.Succeeded;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "choco availability probe failed.");
            return false;
        }
    }

    // DOCUMENTED EXCEPTION: Chocolatey has no non-PowerShell bootstrap. Its only supported install path
    // is the official install.ps1 script, so we shell PowerShell *here and only here*. Every other member
    // of this package layer deliberately avoids PowerShell in favour of native APIs and plain CLIs.
    public async Task<OperationResult> EnsureInstalledAsync(
        IProgress<TaskProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (await IsAvailableAsync(cancellationToken).ConfigureAwait(false))
        {
            return OperationResult.Ok("Chocolatey is available.");
        }

        progress?.Report(TaskProgress.Indeterminate("Installing Chocolatey..."));
        logger.LogInformation("Bootstrapping Chocolatey via {InstallScriptUri}", InstallScriptUri);

        // Canonical Chocolatey bootstrap one-liner (download + execute the official install script).
        var script =
            "Set-ExecutionPolicy Bypass -Scope Process -Force; " +
            "[System.Net.ServicePointManager]::SecurityProtocol = " +
            "[System.Net.ServicePointManager]::SecurityProtocol -bor 3072; " +
            $"iex ((New-Object System.Net.WebClient).DownloadString('{InstallScriptUri}'))";

        string[] args = ["-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", script];

        try
        {
            var result = await processRunner.RunAsync("powershell", args, cancellationToken).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                var detail = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
                return OperationResult.Fail(
                    $"Chocolatey bootstrap failed (exit {result.ExitCode}). {detail}".Trim() +
                    " Install Chocolatey manually from https://chocolatey.org/install and retry.");
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Chocolatey bootstrap threw.");
            return OperationResult.Fail(
                "Chocolatey could not be installed automatically. " +
                "Install it manually from https://chocolatey.org/install and retry.");
        }

        // A fresh install often needs the process PATH refreshed before `choco` resolves, so treat a
        // still-missing choco as a soft failure with guidance rather than reporting a false success.
        return await IsAvailableAsync(cancellationToken).ConfigureAwait(false)
            ? OperationResult.Ok("Chocolatey installed.")
            : OperationResult.Fail(
                "Chocolatey was bootstrapped but is not yet on PATH. Restart the app (or your shell) and retry.");
    }

    public async Task<OperationResult> InstallAsync(string packageId, IProgress<TaskProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var ids = SplitIds(packageId);
        if (ids.Length == 0)
        {
            return OperationResult.Ok("No Chocolatey packages to install.");
        }

        progress?.Report(TaskProgress.Indeterminate($"Installing {ids.Length} Chocolatey package(s)"));
        string[] args = ["install", .. ids, "-y"];
        logger.LogInformation("choco install {Packages}", string.Join(' ', ids));
        var result = await processRunner.RunAsync(Executable, args, cancellationToken).ConfigureAwait(false);
        return ToResult(result, $"install {string.Join(' ', ids)}");
    }

    public async Task<OperationResult> UninstallAsync(string packageId, IProgress<TaskProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var ids = SplitIds(packageId);
        if (ids.Length == 0)
        {
            return OperationResult.Ok("No Chocolatey packages to uninstall.");
        }

        progress?.Report(TaskProgress.Indeterminate($"Uninstalling {ids.Length} Chocolatey package(s)"));
        string[] args = ["uninstall", .. ids, "-y"];
        logger.LogInformation("choco uninstall {Packages}", string.Join(' ', ids));
        var result = await processRunner.RunAsync(Executable, args, cancellationToken).ConfigureAwait(false);
        return ToResult(result, $"uninstall {string.Join(' ', ids)}");
    }

    public async Task<OperationResult> UpgradeAllAsync(IProgress<TaskProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        progress?.Report(TaskProgress.Indeterminate("Upgrading all Chocolatey packages"));
        logger.LogInformation("choco upgrade all -y");
        var result = await processRunner.RunAsync(Executable, UpgradeAllArgs, cancellationToken).ConfigureAwait(false);
        return ToResult(result, "upgrade all");
    }

    // The IPackageManager surface is single-id, but Chocolatey installs are batched (one CLI invocation
    // for many packages). PackageInstaller passes a whitespace-joined id list which we split back out
    // here; Chocolatey package ids never contain whitespace, so this round-trip is lossless.
    private static string[] SplitIds(string packageId) =>
        packageId.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                 .Where(id => !string.Equals(id, "na", StringComparison.OrdinalIgnoreCase))
                 .ToArray();

    private OperationResult ToResult(ProcessResult result, string action)
    {
        if (result.Succeeded)
        {
            return OperationResult.Ok($"choco {action} succeeded.");
        }

        logger.LogWarning("choco {Action} failed (exit {ExitCode}): {Error}", action, result.ExitCode, result.StandardError);
        var detail = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
        return OperationResult.Fail($"choco {action} failed (exit {result.ExitCode}). {detail}".Trim());
    }
}
