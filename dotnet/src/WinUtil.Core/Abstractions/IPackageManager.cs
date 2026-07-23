using WinUtil.Core.Models;

namespace WinUtil.Core.Abstractions;

public enum PackageManagerKind
{
    Winget,
    Choco,
}

/// <summary>A package-manager backend (winget or Chocolatey).</summary>
public interface IPackageManager
{
    PackageManagerKind Kind { get; }

    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);

    Task<OperationResult> EnsureInstalledAsync(
        IProgress<TaskProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<OperationResult> InstallAsync(string packageId, IProgress<TaskProgress>? progress = null, CancellationToken cancellationToken = default);
    Task<OperationResult> UninstallAsync(string packageId, IProgress<TaskProgress>? progress = null, CancellationToken cancellationToken = default);
    Task<OperationResult> UpgradeAllAsync(IProgress<TaskProgress>? progress = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Resolves the active package manager from the user's preference and routes app install/uninstall,
/// including the "choco == na → fall back to winget" rule from the PowerShell tool.
/// </summary>
public interface IPackageInstaller
{
    Task<OperationResult> InstallAppsAsync(
        IReadOnlyList<AppEntry> apps,
        PackageManagerKind preferred,
        IProgress<TaskProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<OperationResult> UninstallAppsAsync(
        IReadOnlyList<AppEntry> apps,
        PackageManagerKind preferred,
        IProgress<TaskProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
