using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinUtil.Core.Abstractions;

namespace WinUtil.App.ViewModels;

/// <summary>The three Windows Update policies offered on the Updates tab.</summary>
public enum WindowsUpdateOption
{
    Default,
    Security,
    Disable,
}

/// <summary>
/// Updates tab: three mutually-exclusive update policies with an Apply command. The apply path is
/// stubbed until the M4 Windows-Update services exist.
/// </summary>
public partial class UpdatesViewModel : TabViewModelBase
{
    public UpdatesViewModel(ShellState shell)
        : base(shell)
    {
    }

    [ObservableProperty]
    private WindowsUpdateOption _selectedOption = WindowsUpdateOption.Default;

    protected override void OnShellPropertyChanged(string? propertyName)
    {
        base.OnShellPropertyChanged(propertyName);
        if (propertyName == nameof(ShellState.IsBusy))
        {
            ApplyCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanRun() => !Shell.IsBusy;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private void Apply()
    {
        // TODO M4: wire to the Windows Update policy services (Default / Security-only / Disable).
        Shell.Report(TaskProgress.Failed($"Applying the '{SelectedOption}' update policy is not yet implemented."));
    }
}
