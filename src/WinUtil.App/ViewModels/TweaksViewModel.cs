using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinUtil.Core.Abstractions;
using WinUtil.Core.Configuration;
using WinUtil.Core.Models;
using WinUtil.Core.Tweaks;

namespace WinUtil.App.ViewModels;

/// <summary>
/// Tweaks tab, laid out to match the original winutil: a "Recommended Selections" button row, a
/// two-column body (Essential + Advanced checkbox tweaks and DNS/OOSU on the left; Customize Preferences
/// toggles and Performance Plans on the right), and a Run/Undo action bar. Checkbox tweaks batch-apply
/// through <see cref="ITweakEngine"/>; toggles apply/undo immediately (<see cref="ToggleTweakViewModel"/>);
/// DNS and power-plan actions call the platform services directly (mirroring <c>ConfigViewModel</c>).
/// </summary>
public partial class TweaksViewModel : TabViewModelBase
{
    private const string EssentialCategory = "Essential Tweaks";

    private readonly WinUtilConfig _config;
    private readonly ITweakEngine _engine;
    private readonly IDnsService? _dns;
    private readonly IPowerPlanService? _power;
    private readonly List<TweakItemViewModel> _checkboxes;
    private bool _dnsReady;

    public TweaksViewModel(
        WinUtilConfig config,
        ITweakEngine engine,
        IEnumerable<IDnsService> dnsServices,
        IEnumerable<IPowerPlanService> powerServices,
        ShellState shell)
        : base(shell)
    {
        _config = config;
        _engine = engine;
        _dns = dnsServices.FirstOrDefault();
        _power = powerServices.FirstOrDefault();

        // Checkbox tweaks, kept in config (JSON) order to mirror the original panel layout.
        _checkboxes = config.Tweaks.Values
            .Where(t => t.Kind == TweakKind.Checkbox)
            .Select(t => new TweakItemViewModel(t))
            .ToList();

        EssentialTweaks = new ObservableCollection<TweakItemViewModel>(
            _checkboxes.Where(t => IsEssential(t.RawCategory)));

        AdvancedTweaks = new ObservableCollection<TweakItemViewModel>(
            _checkboxes.Where(t => IsAdvanced(t.RawCategory)));

        Toggles = new ObservableCollection<ToggleTweakViewModel>(
            config.Tweaks.Values
                  .Where(t => t.Kind == TweakKind.Toggle)
                  .Select(t => new ToggleTweakViewModel(t, engine, shell)));

        // Default views back the ItemsControls so the shared search string can filter them in place.
        EssentialView = CollectionViewSource.GetDefaultView(EssentialTweaks);
        EssentialView.Filter = FilterCheckbox;
        AdvancedView = CollectionViewSource.GetDefaultView(AdvancedTweaks);
        AdvancedView.Filter = FilterCheckbox;
        TogglesView = CollectionViewSource.GetDefaultView(Toggles);
        TogglesView.Filter = FilterToggle;

        // DNS: "Default" (DHCP) plus every provider from config/dns.json.
        var dnsOptions = new List<DnsOptionViewModel> { new("Default", null) };
        dnsOptions.AddRange(config.Dns
            .Select(kvp => new DnsOptionViewModel(kvp.Key.Replace('_', ' '), kvp.Value)));
        DnsOptions = new ObservableCollection<DnsOptionViewModel>(dnsOptions);
        _selectedDns = DnsOptions[0];
        _dnsReady = true;
    }

    // --- Left column: checkbox tweaks --------------------------------------------------------------
    public ObservableCollection<TweakItemViewModel> EssentialTweaks { get; }
    public ObservableCollection<TweakItemViewModel> AdvancedTweaks { get; }
    public ICollectionView EssentialView { get; }
    public ICollectionView AdvancedView { get; }

    // --- Right column: toggle tweaks ---------------------------------------------------------------
    public ObservableCollection<ToggleTweakViewModel> Toggles { get; }
    public ICollectionView TogglesView { get; }

    // --- DNS (left column combobox) ----------------------------------------------------------------
    public ObservableCollection<DnsOptionViewModel> DnsOptions { get; }

    [ObservableProperty]
    private DnsOptionViewModel? _selectedDns;

    private static bool IsEssential(string? category) =>
        string.Equals(category, EssentialCategory, StringComparison.OrdinalIgnoreCase);

    private static bool IsAdvanced(string? category) =>
        category is not null
        && (category.Contains("Advanced", StringComparison.OrdinalIgnoreCase)
            || category.StartsWith("z__", StringComparison.OrdinalIgnoreCase));

    private bool FilterCheckbox(object item)
        => item is TweakItemViewModel tweak && tweak.Matches(Shell.SearchText);

    private bool FilterToggle(object item)
        => item is ToggleTweakViewModel tweak && tweak.Matches(Shell.SearchText);

    protected override void OnShellPropertyChanged(string? propertyName)
    {
        base.OnShellPropertyChanged(propertyName);
        switch (propertyName)
        {
            case nameof(ShellState.IsBusy):
                RunTweaksCommand.NotifyCanExecuteChanged();
                UndoSelectedTweaksCommand.NotifyCanExecuteChanged();
                ApplyDnsCommand.NotifyCanExecuteChanged();
                EnableUltimatePerformanceCommand.NotifyCanExecuteChanged();
                DisableUltimatePerformanceCommand.NotifyCanExecuteChanged();
                RunOosuCommand.NotifyCanExecuteChanged();
                GetInstalledTweaksCommand.NotifyCanExecuteChanged();
                break;
            case nameof(ShellState.SearchText):
                EssentialView.Refresh();
                AdvancedView.Refresh();
                TogglesView.Refresh();
                break;
        }
    }

    private bool CanRun() => !Shell.IsBusy;

    private IReadOnlyList<TweakItemViewModel> SelectedCheckboxes()
        => _checkboxes.Where(t => t.IsSelected).ToList();

    // --- Recommended selections --------------------------------------------------------------------
    [RelayCommand]
    private void ApplyPresetStandard() => ApplyPreset("Standard");

    [RelayCommand]
    private void ApplyPresetMinimal() => ApplyPreset("Minimal");

    [RelayCommand]
    private void ApplyPresetAdvanced() => ApplyPreset("Advanced");

    private void ApplyPreset(string presetName)
    {
        if (!_config.Presets.TryGetValue(presetName, out var ids))
        {
            Shell.Report(TaskProgress.Failed($"Preset '{presetName}' is not defined."));
            return;
        }

        var wanted = ids.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var item in _checkboxes)
        {
            item.IsSelected = wanted.Contains(item.Tweak.Id);
        }

        Shell.Report(TaskProgress.Completed($"{presetName} preset selected ({wanted.Count} tweak(s))."));
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var item in _checkboxes)
        {
            item.IsSelected = false;
        }

        Shell.Report(TaskProgress.Completed("Selection cleared."));
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private void GetInstalledTweaks() =>
        // TODO M4: needs a backend to read the live system and check the matching boxes.
        Shell.Report(TaskProgress.Failed("Get Installed Tweaks is not yet implemented."));

    [RelayCommand]
    private void OpenAppxRemoval() =>
        // TODO: no navigation hook from this leaf VM to switch MainViewModel to the AppX tab yet.
        Shell.Report(TaskProgress.Failed("AppX Removal lives on the AppX tab (navigation not wired yet)."));

    // --- Run / Undo action bar ---------------------------------------------------------------------
    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task RunTweaks() => RunSequentialAsync(apply: true);

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task UndoSelectedTweaks() => RunSequentialAsync(apply: false);

    private async Task RunSequentialAsync(bool apply)
    {
        var items = SelectedCheckboxes();
        if (items.Count == 0)
        {
            Shell.Report(TaskProgress.Failed("No tweaks selected."));
            return;
        }

        if (Shell.IsBusy)
        {
            return;
        }

        Shell.IsBusy = true;
        var failures = 0;
        try
        {
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var percent = (int)(i / (double)items.Count * 100);
                Shell.Report(new TaskProgress(percent, $"{(apply ? "Applying" : "Reverting")} {item.Content}…"));

                var result = await Task.Run(() => apply
                    ? _engine.ApplyAsync(item.Tweak, Shell.Progress)
                    : _engine.UndoAsync(item.Tweak, Shell.Progress)).ConfigureAwait(true);

                if (!result.Success)
                {
                    failures++;
                }
            }

            Shell.Report(failures == 0
                ? TaskProgress.Completed($"{items.Count} tweak(s) {(apply ? "applied" : "reverted")}.")
                : TaskProgress.Failed($"{failures} of {items.Count} tweak(s) failed."));
        }
        catch (Exception ex)
        {
            Shell.Report(TaskProgress.Failed(ex.Message));
        }
        finally
        {
            Shell.IsBusy = false;
        }
    }

    // --- O&O ShutUp10++ ----------------------------------------------------------------------------
    [RelayCommand(CanExecute = nameof(CanRun))]
    private void RunOosu() =>
        // TODO M4: download OOSU10.exe + recommended config and launch it.
        Shell.Report(TaskProgress.Failed("O&O ShutUp10++ is not yet implemented."));

    // --- DNS ---------------------------------------------------------------------------------------
    partial void OnSelectedDnsChanged(DnsOptionViewModel? value)
    {
        if (!_dnsReady)
        {
            return;
        }

        if (ApplyDnsCommand.CanExecute(null))
        {
            _ = ApplyDnsAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task ApplyDnsAsync()
    {
        if (SelectedDns is not { } option)
        {
            return;
        }

        if (_dns is not { } dns)
        {
            // TODO M4: IDnsService is registered in Platform but may resolve to none off-Windows.
            Shell.Report(TaskProgress.Failed("DNS service is not yet available."));
            return;
        }

        var provider = option.Provider;
        await RunGuardedAsync(
            () => Task.Run(() => provider is null
                ? dns.ResetToDhcp()
                : dns.SetDns(provider.Primary, provider.Secondary, provider.Primary6, provider.Secondary6)),
            provider is null ? "Resetting DNS to DHCP…" : $"Applying {option.Name} DNS…");
    }

    // --- Performance plans -------------------------------------------------------------------------
    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task EnableUltimatePerformanceAsync()
    {
        if (_power is not { } power)
        {
            // TODO M4: IPowerPlanService is registered in Platform but may resolve to none off-Windows.
            Shell.Report(TaskProgress.Failed("Power-plan service is not yet available."));
            return;
        }

        await RunGuardedAsync(
            () => Task.Run(() => power.EnableUltimatePerformance()),
            "Enabling the Ultimate Performance power plan…");
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task DisableUltimatePerformanceAsync()
    {
        if (_power is not { } power)
        {
            // TODO M4: IPowerPlanService is registered in Platform but may resolve to none off-Windows.
            Shell.Report(TaskProgress.Failed("Power-plan service is not yet available."));
            return;
        }

        await RunGuardedAsync(
            () => Task.Run(() => power.RestoreDefaultPlans()),
            "Restoring the default power plans…");
    }
}
