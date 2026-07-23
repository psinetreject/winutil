using CommunityToolkit.Mvvm.ComponentModel;
using WinUtil.Core.Abstractions;

namespace WinUtil.App.ViewModels;

/// <summary>
/// Shared shell state observed by every tab view-model. Holds the single busy guard, the active
/// package-manager preference, the shared search string, and the live progress/status snapshot.
/// Registered as a singleton so <see cref="MainViewModel"/> and the tab view-models see one instance
/// (avoids a circular dependency between the root VM and the tab VMs it hosts).
/// </summary>
public partial class ShellState : ObservableObject
{
    private readonly Progress<TaskProgress> _progress;

    public ShellState()
    {
        // Progress<T> captures the current SynchronizationContext (the WPF dispatcher when the DI
        // container builds this on the UI thread), so background service callbacks marshal home.
        _progress = new Progress<TaskProgress>(Apply);
    }

    /// <summary>Hand this to Core services; their <see cref="TaskProgress"/> updates land on the UI thread.</summary>
    public IProgress<TaskProgress> Progress => _progress;

    /// <summary>Shared concurrency guard: only one system operation runs at a time.</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Active package-manager backend (winget / choco).</summary>
    [ObservableProperty]
    private PackageManagerKind _packageManager = PackageManagerKind.Winget;

    /// <summary>Shared search/filter text applied across the Install and Tweaks tabs.</summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>Latest progress snapshot.</summary>
    [ObservableProperty]
    private TaskProgress _status = new(0, "Ready");

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private int _progressPercent;

    [ObservableProperty]
    private ProgressState _progressState = ProgressState.Normal;

    [ObservableProperty]
    private bool _isIndeterminate;

    /// <summary>Push a progress update from the UI thread (guarded commands call this directly).</summary>
    public void Report(TaskProgress progress) => Apply(progress);

    private void Apply(TaskProgress progress)
    {
        Status = progress;
        StatusMessage = progress.Message;
        ProgressPercent = progress.Percent;
        ProgressState = progress.State;
        IsIndeterminate = progress.State == ProgressState.Indeterminate;
    }

    // Safety net: whenever the busy guard clears, stop any indeterminate animation — a late async
    // progress callback must never leave the progress bar looping forever.
    partial void OnIsBusyChanged(bool value)
    {
        if (!value)
        {
            IsIndeterminate = false;
        }
    }
}
