using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinUtil.App.Theming;
using WinUtil.Core.Abstractions;

namespace WinUtil.App.ViewModels;

/// <summary>
/// Root view-model. Hosts the tab view-models, owns the top-nav selection, and re-exposes the shared
/// <see cref="ShellState"/> (progress/status, package-manager preference, search string). Applies the
/// dark theme on construction.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly ThemeService _themeService;

    public MainViewModel(
        ShellState shell,
        ThemeService themeService,
        InstallViewModel install,
        TweaksViewModel tweaks,
        ConfigViewModel config,
        UpdatesViewModel updates)
    {
        Shell = shell;
        _themeService = themeService;
        Install = install;
        Tweaks = tweaks;
        Config = config;
        Updates = updates;

        Tabs =
        [
            new TabViewModel("Install", install),
            new TabViewModel("Tweaks", tweaks),
            new TabViewModel("Config", config),
            new TabViewModel("Updates", updates),
            new TabViewModel("Win11 ISO", new PlaceholderViewModel("Windows 11 ISO", "MicroWin ISO tooling lands in a later milestone.")),
            new TabViewModel("AppX", new PlaceholderViewModel("AppX Packages", "Store / Appx management lands in a later milestone.")),
        ];

        _selectedTab = Tabs[0];
        _selectedTab.IsSelected = true;

        // Re-raise the shared shell properties this VM re-exposes for binding convenience.
        Shell.PropertyChanged += OnShellChanged;

        // Dark theme by default (see ThemeService / themes.json tokens).
        _themeService.ApplyTheme(_themeMode);
    }

    public ShellState Shell { get; }

    public InstallViewModel Install { get; }
    public TweaksViewModel Tweaks { get; }
    public ConfigViewModel Config { get; }
    public UpdatesViewModel Updates { get; }

    public IReadOnlyList<TabViewModel> Tabs { get; }

    [ObservableProperty]
    private TabViewModel _selectedTab;

    public IReadOnlyList<string> ThemeOptions { get; } = ["Auto", "Light", "Dark"];

    [ObservableProperty]
    private string _themeMode = "Dark";

    partial void OnThemeModeChanged(string value) => _themeService.ApplyTheme(value);

    // Contract pass-throughs over the shared shell state.
    public PackageManagerKind PackageManagerPreference
    {
        get => Shell.PackageManager;
        set => Shell.PackageManager = value;
    }

    public string SearchText
    {
        get => Shell.SearchText;
        set => Shell.SearchText = value;
    }

    public TaskProgress Status => Shell.Status;

    [RelayCommand]
    private void SelectTab(TabViewModel tab)
    {
        foreach (var candidate in Tabs)
        {
            candidate.IsSelected = ReferenceEquals(candidate, tab);
        }

        SelectedTab = tab;
    }

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ShellState.PackageManager):
                OnPropertyChanged(nameof(PackageManagerPreference));
                break;
            case nameof(ShellState.SearchText):
                OnPropertyChanged(nameof(SearchText));
                break;
            case nameof(ShellState.Status):
                OnPropertyChanged(nameof(Status));
                break;
        }
    }
}
