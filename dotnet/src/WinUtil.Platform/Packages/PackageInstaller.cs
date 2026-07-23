using Microsoft.Extensions.Logging;
using WinUtil.Core.Abstractions;
using WinUtil.Core.Models;

namespace WinUtil.Platform.Packages;

/// <summary>
/// Routes app install/uninstall across the WinGet and Chocolatey backends according to the user's
/// preference, reproducing the PowerShell tool's rules: WinGet preference always uses <c>app.Winget</c>;
/// Chocolatey preference uses <c>app.Choco</c> but falls back to <c>app.Winget</c> when the app has no
/// real Chocolatey package (<see cref="AppEntry.HasChoco"/> is false). WinGet operations run one package
/// at a time; Chocolatey operations are batched into a single CLI call.
/// </summary>
public sealed class PackageInstaller : IPackageInstaller
{
    private readonly IPackageManager _winget;
    private readonly IPackageManager _choco;
    private readonly ILogger<PackageInstaller> _logger;

    public PackageInstaller(IEnumerable<IPackageManager> managers, ILogger<PackageInstaller> logger)
    {
        ArgumentNullException.ThrowIfNull(managers);
        var list = managers.ToList();
        _winget = list.FirstOrDefault(m => m.Kind == PackageManagerKind.Winget)
            ?? throw new InvalidOperationException("No WinGet IPackageManager was registered.");
        _choco = list.FirstOrDefault(m => m.Kind == PackageManagerKind.Choco)
            ?? throw new InvalidOperationException("No Chocolatey IPackageManager was registered.");
        _logger = logger;
    }

    public Task<OperationResult> InstallAppsAsync(
        IReadOnlyList<AppEntry> apps,
        PackageManagerKind preferred,
        IProgress<TaskProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(apps, preferred, install: true, progress, cancellationToken);

    public Task<OperationResult> UninstallAppsAsync(
        IReadOnlyList<AppEntry> apps,
        PackageManagerKind preferred,
        IProgress<TaskProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(apps, preferred, install: false, progress, cancellationToken);

    private async Task<OperationResult> ExecuteAsync(
        IReadOnlyList<AppEntry> apps,
        PackageManagerKind preferred,
        bool install,
        IProgress<TaskProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(apps);

        var (wingetIds, chocoIds) = Route(apps, preferred);
        var total = wingetIds.Count + chocoIds.Count;
        if (total == 0)
        {
            return OperationResult.Ok("No packages selected.");
        }

        var verb = install ? "install" : "uninstall";
        _logger.LogInformation(
            "Package {Verb} split (preference {Preference}): winget={WingetCount}, choco={ChocoCount}",
            verb, preferred, wingetIds.Count, chocoIds.Count);

        var failures = new List<string>();
        var completed = 0;

        // --- WinGet: one package at a time -------------------------------------------------------
        if (wingetIds.Count > 0)
        {
            var wingetReady = true;
            if (install)
            {
                var ensure = await _winget.EnsureInstalledAsync(progress, cancellationToken).ConfigureAwait(false);
                if (!ensure.Success)
                {
                    wingetReady = false;
                    failures.Add(ensure.Message ?? "WinGet is unavailable.");
                }
            }

            if (wingetReady)
            {
                foreach (var id in wingetIds)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var position = completed + 1;
                    progress?.Report(new TaskProgress(Percent(completed, total), $"{Gerund(verb)} {id} ({position}/{total})"));

                    var result = install
                        ? await _winget.InstallAsync(id, progress, cancellationToken).ConfigureAwait(false)
                        : await _winget.UninstallAsync(id, progress, cancellationToken).ConfigureAwait(false);

                    if (!result.Success)
                    {
                        failures.Add(result.Message ?? $"winget {verb} {id} failed.");
                    }

                    completed++;
                    progress?.Report(new TaskProgress(Percent(completed, total), $"{id} done ({completed}/{total})"));
                }
            }
            else
            {
                completed += wingetIds.Count; // count skipped packages so the choco percentage stays sane
            }
        }

        // --- Chocolatey: batched -----------------------------------------------------------------
        if (chocoIds.Count > 0)
        {
            var chocoReady = true;
            if (install)
            {
                var ensure = await _choco.EnsureInstalledAsync(progress, cancellationToken).ConfigureAwait(false);
                if (!ensure.Success)
                {
                    chocoReady = false;
                    failures.Add(ensure.Message ?? "Chocolatey is unavailable.");
                }
            }

            if (chocoReady)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var position = completed + 1;
                progress?.Report(new TaskProgress(
                    Percent(completed, total),
                    $"{Gerund(verb)} {chocoIds.Count} Chocolatey package(s) ({position}/{total})"));

                var batched = string.Join(' ', chocoIds);
                var result = install
                    ? await _choco.InstallAsync(batched, progress, cancellationToken).ConfigureAwait(false)
                    : await _choco.UninstallAsync(batched, progress, cancellationToken).ConfigureAwait(false);

                if (!result.Success)
                {
                    failures.Add(result.Message ?? $"choco {verb} failed.");
                }
            }

            completed += chocoIds.Count;
        }

        // --- Aggregate ---------------------------------------------------------------------------
        if (failures.Count > 0)
        {
            progress?.Report(TaskProgress.Failed($"{Capitalize(verb)} finished with {failures.Count} error(s)."));
            return OperationResult.Fail(
                $"{failures.Count} of {total} package operation(s) failed: {string.Join(" | ", failures)}");
        }

        progress?.Report(TaskProgress.Completed($"{Capitalize(verb)} finished ({total} package(s))."));
        return OperationResult.Ok($"{Past(verb)} {total} package(s).");
    }

    // Mirrors Get-WinUtilSelectedPackages: winget preference always uses app.Winget; choco preference
    // uses app.Choco but falls back to app.Winget when the app has no real choco package (choco == na).
    private static (List<string> Winget, List<string> Choco) Route(IReadOnlyList<AppEntry> apps, PackageManagerKind preferred)
    {
        var winget = new List<string>();
        var choco = new List<string>();

        foreach (var app in apps)
        {
            if (preferred == PackageManagerKind.Choco && app.HasChoco)
            {
                AddUnique(choco, app.Choco);
            }
            else
            {
                AddUnique(winget, app.Winget);
            }
        }

        return (winget, choco);
    }

    private static void AddUnique(List<string> target, string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || string.Equals(id, "na", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!target.Contains(id))
        {
            target.Add(id);
        }
    }

    private static int Percent(int completed, int total) => total == 0 ? 0 : (int)(completed / (double)total * 100);

    private static string Capitalize(string verb) => char.ToUpperInvariant(verb[0]) + verb[1..];

    private static string Gerund(string verb) => Capitalize(verb) + "ing"; // install -> Installing

    private static string Past(string verb) => Capitalize(verb) + "ed";    // install -> Installed
}
