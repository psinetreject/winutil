using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinUtil.Core.Abstractions;
using WinUtil.Core.Configuration;
using WinUtil.Core.Models;

namespace WinUtil.App.ViewModels;

/// <summary>
/// Install tab: applications grouped by category, rendered as selectable cards, filtered by the shared
/// search and by the top "Filters" chip row. Install/Uninstall route through <see cref="IPackageInstaller"/>;
/// Upgrade-All routes through the <see cref="IPackageManager"/> matching the active preference.
/// </summary>
public partial class InstallViewModel : TabViewModelBase
{
    private readonly IPackageInstaller _installer;
    private readonly IReadOnlyList<IPackageManager> _packageManagers;
    private readonly List<AppItemViewModel> _apps;

    // Active category filter set by the chip row. Null = "All" (no category restriction).
    private string? _categoryFilter;

    public InstallViewModel(
        WinUtilConfig config,
        IPackageInstaller installer,
        IEnumerable<IPackageManager> packageManagers,
        ShellState shell)
        : base(shell)
    {
        _installer = installer;
        _packageManagers = packageManagers.ToList();

        _apps = config.Applications
            .Select(kvp => new AppItemViewModel(kvp.Key, kvp.Value))
            .ToList();

        foreach (var app in _apps)
        {
            app.PropertyChanged += OnAppPropertyChanged;
        }

        Categories = new ObservableCollection<FilteredCategoryViewModel>(
            _apps.GroupBy(a => a.Category)
                 .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                 .Select(g => new FilteredCategoryViewModel(
                     g.Key,
                     new ObservableCollection<AppItemViewModel>(g.OrderBy(a => a.Content, StringComparer.OrdinalIgnoreCase)),
                     FilterApp)));

        // Categories are expanded by default on the Install tab (the original tool renders them open).
        foreach (var category in Categories)
        {
            category.IsExpanded = true;
        }

        // "All" chip first, then one chip per category present in the data.
        Chips = new ObservableCollection<CategoryChipViewModel>(
            new[] { new CategoryChipViewModel("All", string.Empty) { IsActive = true } }
                .Concat(Categories.Select(c => new CategoryChipViewModel(c.Name, c.Name))));
    }

    public ObservableCollection<FilteredCategoryViewModel> Categories { get; }

    /// <summary>Category filter chips shown across the top of the tab ("All" + one per category).</summary>
    public ObservableCollection<CategoryChipViewModel> Chips { get; }

    /// <summary>Live count of selected applications (drives the "Selected Apps: N" sidebar button).</summary>
    [ObservableProperty]
    private int _selectedCount;

    /// <summary>Two-way radio binding for the WinGet package-manager preference (shared via the shell).</summary>
    public bool IsWinget
    {
        get => Shell.PackageManager == PackageManagerKind.Winget;
        set
        {
            if (value)
            {
                Shell.PackageManager = PackageManagerKind.Winget;
            }
        }
    }

    /// <summary>Two-way radio binding for the Chocolatey package-manager preference (shared via the shell).</summary>
    public bool IsChoco
    {
        get => Shell.PackageManager == PackageManagerKind.Choco;
        set
        {
            if (value)
            {
                Shell.PackageManager = PackageManagerKind.Choco;
            }
        }
    }

    private bool FilterApp(object item)
        => item is AppItemViewModel app
           && (_categoryFilter is null || string.Equals(app.Category, _categoryFilter, StringComparison.OrdinalIgnoreCase))
           && app.Matches(Shell.SearchText);

    private void OnAppPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppItemViewModel.IsSelected))
        {
            SelectedCount = _apps.Count(a => a.IsSelected);
        }
    }

    protected override void OnShellPropertyChanged(string? propertyName)
    {
        base.OnShellPropertyChanged(propertyName);
        switch (propertyName)
        {
            case nameof(ShellState.IsBusy):
                InstallCommand.NotifyCanExecuteChanged();
                UninstallCommand.NotifyCanExecuteChanged();
                UpgradeAllCommand.NotifyCanExecuteChanged();
                break;
            case nameof(ShellState.SearchText):
                var expand = !string.IsNullOrWhiteSpace(Shell.SearchText);
                foreach (var category in Categories)
                {
                    category.Refresh(expand);
                }

                break;
            case nameof(ShellState.PackageManager):
                OnPropertyChanged(nameof(IsWinget));
                OnPropertyChanged(nameof(IsChoco));
                break;
        }
    }

    private bool CanRun() => !Shell.IsBusy;

    private IReadOnlyList<AppEntry> SelectedEntries()
        => _apps.Where(a => a.IsSelected).Select(a => a.Entry).ToList();

    /// <summary>Applies a chip filter. Empty/"All" clears the filter (shows every category).</summary>
    [RelayCommand]
    private void FilterByCategory(string? category)
    {
        _categoryFilter = string.IsNullOrWhiteSpace(category) || string.Equals(category, "All", StringComparison.OrdinalIgnoreCase)
            ? null
            : category;

        foreach (var chip in Chips)
        {
            chip.IsActive = string.Equals(chip.Category, _categoryFilter ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        foreach (var cat in Categories)
        {
            cat.Refresh(expandMatches: false);
        }
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var app in _apps)
        {
            app.IsSelected = false;
        }
    }

    [RelayCommand]
    private void CollapseAll()
    {
        foreach (var category in Categories)
        {
            category.IsExpanded = false;
        }
    }

    [RelayCommand]
    private void ExpandAll()
    {
        foreach (var category in Categories)
        {
            category.IsExpanded = true;
        }
    }

    [RelayCommand]
    private void ShowInstalled()
    {
        // TODO M4: no "installed apps" detection backend exists yet (the PowerShell tool queries
        // winget/choco to check-mark already-installed apps). Wire the command; report a placeholder.
        Shell.Report(TaskProgress.Failed("Showing installed applications is not yet available."));
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task InstallAsync()
    {
        var apps = SelectedEntries();
        if (apps.Count == 0)
        {
            Shell.Report(TaskProgress.Failed("No applications selected."));
            return;
        }

        await RunGuardedAsync(
            () => _installer.InstallAppsAsync(apps, Shell.PackageManager, Shell.Progress),
            $"Installing {apps.Count} application(s) via {Shell.PackageManager}…");
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task UninstallAsync()
    {
        var apps = SelectedEntries();
        if (apps.Count == 0)
        {
            Shell.Report(TaskProgress.Failed("No applications selected."));
            return;
        }

        await RunGuardedAsync(
            () => _installer.UninstallAppsAsync(apps, Shell.PackageManager, Shell.Progress),
            $"Uninstalling {apps.Count} application(s)…");
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task UpgradeAllAsync()
    {
        if (_packageManagers.FirstOrDefault(m => m.Kind == Shell.PackageManager) is not { } manager)
        {
            // TODO M4: no IPackageManager backend is registered for this preference yet.
            Shell.Report(TaskProgress.Failed($"Upgrade-all is not yet available for {Shell.PackageManager}."));
            return;
        }

        await RunGuardedAsync(
            () => manager.UpgradeAllAsync(Shell.Progress),
            "Upgrading all installed packages…");
    }
}

/// <summary>A single "Filters" chip. <see cref="Category"/> empty means the "All" chip (no filter).</summary>
public partial class CategoryChipViewModel : ObservableObject
{
    public CategoryChipViewModel(string label, string category)
    {
        Label = label;
        Category = category;
    }

    public string Label { get; }

    /// <summary>Category this chip filters to; empty string for the "All" chip.</summary>
    public string Category { get; }

    /// <summary>True when this is the active filter — dimming/weighting the chips is driven off this.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChipOpacity))]
    private bool _isActive;

    /// <summary>Inactive chips are dimmed so the active filter reads clearly, whatever FilterChipStyle does.</summary>
    public double ChipOpacity => IsActive ? 1.0 : 0.6;
}
