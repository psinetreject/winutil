using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using WinUtil.Core.Abstractions;

namespace WinUtil.App.ViewModels;

/// <summary>
/// Base for the tab view-models. Wires the shared <see cref="ShellState"/>, re-exposes the busy flag,
/// and provides a guarded runner that funnels service calls through the single busy guard + status line.
/// </summary>
public abstract class TabViewModelBase : ObservableObject
{
    protected ShellState Shell { get; }

    protected TabViewModelBase(ShellState shell)
    {
        Shell = shell;
        Shell.PropertyChanged += OnShellChanged;
    }

    /// <summary>Mirror of <see cref="ShellState.IsBusy"/> so views can bind locally.</summary>
    public bool IsBusy => Shell.IsBusy;

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
        => OnShellPropertyChanged(e.PropertyName);

    /// <summary>Override to refresh commands/filters when shared shell state changes.</summary>
    protected virtual void OnShellPropertyChanged(string? propertyName)
    {
        if (propertyName == nameof(ShellState.IsBusy))
        {
            OnPropertyChanged(nameof(IsBusy));
        }
    }

    /// <summary>Runs a service operation under the shared busy guard, reporting start/finish to the status bar.</summary>
    protected async Task RunGuardedAsync(Func<Task<OperationResult>> operation, string startMessage)
    {
        if (Shell.IsBusy)
        {
            return;
        }

        Shell.IsBusy = true;
        Shell.Report(TaskProgress.Indeterminate(startMessage));
        try
        {
            var result = await operation().ConfigureAwait(true);
            Shell.Report(result.Success
                ? TaskProgress.Completed(result.Message ?? "Done")
                : TaskProgress.Failed(result.Message ?? "Operation failed"));
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
}
