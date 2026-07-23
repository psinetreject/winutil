using Microsoft.Extensions.Logging;
using Windows.ApplicationModel;
using Windows.Management.Deployment;
using WinUtil.Core.Abstractions;

using WinRtPackageManager = Windows.Management.Deployment.PackageManager;

namespace WinUtil.Platform.Packages;

/// <summary>
/// Installs and removes Appx (Store) packages using the WinRT
/// <see cref="Windows.Management.Deployment.PackageManager"/> for the pure-API paths, and shells
/// <c>dism.exe</c> (via the injected <see cref="IProcessRunner"/>) only for provisioned-package removal,
/// which has no reliable WinRT equivalent. Store installs fall back to WinGet's <c>msstore</c> source.
/// </summary>
public sealed class AppxManager : IAppxManager
{
    private readonly IProcessRunner _processRunner;
    private readonly ILogger<AppxManager> _logger;
    private readonly WinRtPackageManager _packageManager = new();

    private static readonly string[] DismListArgs = ["/Online", "/Get-ProvisionedAppxPackages"];

    public AppxManager(IProcessRunner processRunner, ILogger<AppxManager> logger)
    {
        _processRunner = processRunner;
        _logger = logger;
    }

    public async Task<OperationResult> RemoveAsync(
        string packageId,
        bool removeProvisioned = true,
        CancellationToken cancellationToken = default)
    {
        var failures = new List<string>();
        var removedAny = false;

        // Installed packages for the current (elevated) user. FindPackagesForUser can return per-profile
        // duplicates, so de-dupe by full name; each removal targets all user profiles.
        IReadOnlyList<Package> matches;
        try
        {
            matches = _packageManager.FindPackagesForUser(string.Empty)
                .Where(p => Matches(p, packageId))
                .GroupBy(p => p.Id.FullName)
                .Select(g => g.First())
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Enumerating installed packages for {PackageId} failed.", packageId);
            return OperationResult.Fail($"Could not enumerate installed packages for '{packageId}': {ex.Message}");
        }

        foreach (var package in matches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                _logger.LogInformation("Removing AppX package {FullName}", package.Id.FullName);
                var operation = _packageManager.RemovePackageAsync(package.Id.FullName, RemovalOptions.RemoveForAllUsers);
                var result = await operation.AsTask(cancellationToken).ConfigureAwait(false);
                if (result.ExtendedErrorCode is not null)
                {
                    failures.Add($"{package.Id.FullName}: {result.ErrorText}");
                }
                else
                {
                    removedAny = true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Removing AppX package {FullName} threw.", package.Id.FullName);
                failures.Add($"{package.Id.FullName}: {ex.Message}");
            }
        }

        if (removeProvisioned)
        {
            var provisioned = await RemoveProvisionedAsync(packageId, cancellationToken).ConfigureAwait(false);
            if (!provisioned.Success)
            {
                failures.Add(provisioned.Message ?? "Provisioned removal failed.");
            }
        }

        if (failures.Count > 0)
        {
            return OperationResult.Fail($"AppX removal for '{packageId}' had errors: {string.Join(" | ", failures)}");
        }

        return OperationResult.Ok(removedAny
            ? $"Removed AppX package(s) matching '{packageId}'."
            : $"No installed AppX package matched '{packageId}'.");
    }

    public async Task<OperationResult> InstallAsync(
        string packageId,
        string? storeId,
        CancellationToken cancellationToken = default)
    {
        // 1) Pure-API path: if the package is already staged (provisioned / installed for other users),
        //    register it for the current user by its package family name.
        string? familyName = null;
        try
        {
            familyName = _packageManager.FindPackages() // all users; requires elevation
                .Where(p => Matches(p, packageId))
                .Select(p => p.Id.FamilyName)
                .FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Enumerating staged packages for {PackageId} failed; considering Store fallback.", packageId);
        }

        if (familyName is not null)
        {
            try
            {
                _logger.LogInformation("Registering AppX package by family name {FamilyName}", familyName);
                var operation = _packageManager.RegisterPackageByFamilyNameAsync(
                    familyName, Array.Empty<string>(), DeploymentOptions.None,
                    appDataVolume: null!, optionalPackageFamilyNames: Array.Empty<string>());
                var result = await operation.AsTask(cancellationToken).ConfigureAwait(false);
                if (result.ExtendedErrorCode is null)
                {
                    return OperationResult.Ok($"Registered AppX package '{familyName}'.");
                }

                _logger.LogWarning("Registration of {FamilyName} failed: {Error}", familyName, result.ErrorText);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Registration of {FamilyName} threw; considering Store fallback.", familyName);
            }
        }

        // 2) Fallback: install from the Microsoft Store via WinGet's msstore source.
        if (string.IsNullOrWhiteSpace(storeId))
        {
            return OperationResult.Fail(
                $"Could not install '{packageId}': no staged package to register and no Microsoft Store id provided.");
        }

        _logger.LogInformation(
            "Installing {PackageId} from Microsoft Store id {StoreId} via winget msstore source.", packageId, storeId);

        // Mirrors WingetPackageManager's msstore path (winget install --id <storeId> --source msstore ...).
        string[] args =
        [
            "install", "--id", storeId, "--source", "msstore",
            "--accept-package-agreements", "--accept-source-agreements", "--silent",
        ];

        try
        {
            var pr = await _processRunner.RunAsync("winget", args, cancellationToken).ConfigureAwait(false);
            if (pr.Succeeded)
            {
                return OperationResult.Ok($"Installed '{packageId}' from the Microsoft Store ({storeId}).");
            }

            var detail = string.IsNullOrWhiteSpace(pr.StandardError) ? pr.StandardOutput : pr.StandardError;
            return OperationResult.Fail($"winget msstore install of '{storeId}' failed (exit {pr.ExitCode}). {detail}".Trim());
        }
        catch (Exception ex)
        {
            return OperationResult.Fail($"winget msstore install of '{storeId}' failed: {ex.Message}");
        }
    }

    public Task<bool> IsInstalledAsync(string packageId, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            try
            {
                return _packageManager.FindPackagesForUser(string.Empty).Any(p => Matches(p, packageId));
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "IsInstalled probe for {PackageId} failed.", packageId);
                return false;
            }
        }, cancellationToken);

    // No reliable WinRT API exists for enumerating/removing *provisioned* packages, so we shell dism.exe.
    // Mirrors the PS behaviour of matching the provisioned DisplayName with a "*name*" pattern.
    private async Task<OperationResult> RemoveProvisionedAsync(string packageId, CancellationToken cancellationToken)
    {
        ProcessResult listResult;
        try
        {
            listResult = await _processRunner.RunAsync("dism", DismListArgs, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DISM enumeration of provisioned packages failed.");
            return OperationResult.Fail($"Could not query provisioned packages via DISM: {ex.Message}");
        }

        if (!listResult.Succeeded)
        {
            return OperationResult.Fail($"DISM provisioned-package query failed (exit {listResult.ExitCode}).");
        }

        var toRemove = ParseProvisionedPackageNames(listResult.StandardOutput, packageId);
        if (toRemove.Count == 0)
        {
            return OperationResult.Ok($"No provisioned AppX package matched '{packageId}'.");
        }

        var failures = new List<string>();
        foreach (var name in toRemove)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _logger.LogInformation("Removing provisioned AppX package {PackageName}", name);
            string[] args = ["/Online", "/Remove-ProvisionedAppxPackage", $"/PackageName:{name}"];
            var result = await _processRunner.RunAsync("dism", args, cancellationToken).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                failures.Add($"{name} (exit {result.ExitCode})");
            }
        }

        return failures.Count == 0
            ? OperationResult.Ok($"Removed {toRemove.Count} provisioned package(s) matching '{packageId}'.")
            : OperationResult.Fail($"DISM failed to remove provisioned package(s): {string.Join(", ", failures)}");
    }

    private static bool Matches(Package package, string pattern) =>
        package.Id.Name.Contains(pattern, StringComparison.OrdinalIgnoreCase) ||
        package.Id.FamilyName.Contains(pattern, StringComparison.OrdinalIgnoreCase);

    // DISM prints repeating "Key : Value" blocks. We collect each block's PackageName when its
    // DisplayName contains the requested pattern (matching the PS DisplayName -Like "*name*" behaviour).
    private static List<string> ParseProvisionedPackageNames(string dismOutput, string pattern)
    {
        var names = new List<string>();
        string? displayName = null;

        foreach (var raw in dismOutput.Split('\n'))
        {
            var line = raw.Trim();
            var separator = line.IndexOf(':', StringComparison.Ordinal);
            if (separator < 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();

            if (key.Equals("DisplayName", StringComparison.OrdinalIgnoreCase))
            {
                displayName = value;
            }
            else if (key.Equals("PackageName", StringComparison.OrdinalIgnoreCase))
            {
                if (value.Length > 0 &&
                    displayName is not null &&
                    displayName.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    names.Add(value);
                }

                displayName = null;
            }
        }

        return names;
    }
}
