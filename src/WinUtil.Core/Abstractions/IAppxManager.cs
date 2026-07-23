namespace WinUtil.Core.Abstractions;

/// <summary>Installs/removes Appx (Store) packages, including provisioned packages.</summary>
public interface IAppxManager
{
    Task<OperationResult> RemoveAsync(
        string packageId,
        bool removeProvisioned = true,
        CancellationToken cancellationToken = default);

    Task<OperationResult> InstallAsync(
        string packageId,
        string? storeId,
        CancellationToken cancellationToken = default);

    Task<bool> IsInstalledAsync(string packageId, CancellationToken cancellationToken = default);
}
