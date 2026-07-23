using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinUtil.Core.Abstractions;
using WinUtil.Core.Configuration;

namespace WinUtil.App.ViewModels;

/// <summary>
/// Config / Fixes tab: DNS selection, the common "fix" actions, and the Ultimate Performance power plan.
/// Services are injected optionally (via IEnumerable so an unregistered backend resolves to none); the
/// remaining fixes are stubbed until their M4 services land.
/// </summary>
public partial class ConfigViewModel : TabViewModelBase
{
    private readonly IDnsService? _dns;
    private readonly IPowerPlanService? _power;

    public ConfigViewModel(
        WinUtilConfig config,
        IEnumerable<IDnsService> dnsServices,
        IEnumerable<IPowerPlanService> powerServices,
        ShellState shell)
        : base(shell)
    {
        _dns = dnsServices.FirstOrDefault();
        _power = powerServices.FirstOrDefault();

        var options = new List<DnsOptionViewModel> { new("Default (DHCP)", null) };
        options.AddRange(config.Dns
            .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kvp => new DnsOptionViewModel(kvp.Key.Replace('_', ' '), kvp.Value)));

        DnsOptions = new ObservableCollection<DnsOptionViewModel>(options);
        _selectedDns = DnsOptions[0];
    }

    public ObservableCollection<DnsOptionViewModel> DnsOptions { get; }

    [ObservableProperty]
    private DnsOptionViewModel? _selectedDns;

    protected override void OnShellPropertyChanged(string? propertyName)
    {
        base.OnShellPropertyChanged(propertyName);
        if (propertyName == nameof(ShellState.IsBusy))
        {
            ApplyDnsCommand.NotifyCanExecuteChanged();
            EnableUltimatePerformanceCommand.NotifyCanExecuteChanged();
            RestoreDefaultPowerPlansCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanRun() => !Shell.IsBusy;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task ApplyDnsAsync()
    {
        if (SelectedDns is not { } option)
        {
            return;
        }

        if (_dns is not { } dns)
        {
            // TODO M4: IDnsService is not registered yet.
            Shell.Report(TaskProgress.Failed("DNS service is not yet implemented."));
            return;
        }

        var provider = option.Provider;
        await RunGuardedAsync(
            () => Task.Run(() => provider is null
                ? dns.ResetToDhcp()
                : dns.SetDns(provider.Primary, provider.Secondary, provider.Primary6, provider.Secondary6)),
            provider is null ? "Resetting DNS to DHCP…" : $"Applying {option.Name} DNS…");
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task EnableUltimatePerformanceAsync()
    {
        if (_power is not { } power)
        {
            // TODO M4: IPowerPlanService is not registered yet.
            Shell.Report(TaskProgress.Failed("Power-plan service is not yet implemented."));
            return;
        }

        await RunGuardedAsync(
            () => Task.Run(() => power.EnableUltimatePerformance()),
            "Enabling the Ultimate Performance power plan…");
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RestoreDefaultPowerPlansAsync()
    {
        if (_power is not { } power)
        {
            // TODO M4: IPowerPlanService is not registered yet.
            Shell.Report(TaskProgress.Failed("Power-plan service is not yet implemented."));
            return;
        }

        await RunGuardedAsync(
            () => Task.Run(() => power.RestoreDefaultPlans()),
            "Restoring the default power plans…");
    }

    // The following fixes have no Core service yet; expose the command but report not-implemented.
    [RelayCommand]
    private void FixNetwork() => ReportNotImplemented("Reset network stack"); // TODO M4

    [RelayCommand]
    private void FixWindowsUpdate() => ReportNotImplemented("Reset Windows Update components"); // TODO M4

    [RelayCommand]
    private void ResetWinget() => ReportNotImplemented("Reset winget sources"); // TODO M4

    [RelayCommand]
    private void SyncDateTime() => ReportNotImplemented("Re-sync system time (NTP)"); // TODO M4

    private void ReportNotImplemented(string what) =>
        Shell.Report(TaskProgress.Failed($"{what} is not yet implemented."));
}
