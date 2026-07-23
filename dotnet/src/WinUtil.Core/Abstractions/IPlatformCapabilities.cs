namespace WinUtil.Core.Abstractions;

/// <summary>Reports whether the process is running elevated.</summary>
public interface IElevationService
{
    bool IsElevated { get; }
}

/// <summary>Notifies the shell of settings changes / restarts Explorer after tweaks.</summary>
public interface IExplorerRefresher
{
    void BroadcastSettingChange();
    void RestartExplorer();
}

/// <summary>Creates System Restore checkpoints (srclient / WMI SystemRestore).</summary>
public interface ISystemRestoreService
{
    Task<OperationResult> CreateRestorePointAsync(string description, CancellationToken cancellationToken = default);
}

/// <summary>Enables Windows optional features via DISM.</summary>
public interface IWindowsFeatureService
{
    Task<OperationResult> EnableFeatureAsync(
        string featureName,
        IProgress<TaskProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Manages the "Ultimate Performance" power plan.</summary>
public interface IPowerPlanService
{
    OperationResult EnableUltimatePerformance();
    OperationResult RestoreDefaultPlans();
}

/// <summary>Sets or resets DNS servers on active adapters.</summary>
public interface IDnsService
{
    OperationResult SetDns(string? primary, string? secondary, string? primary6, string? secondary6);
    OperationResult ResetToDhcp();
}

/// <summary>Registers/removes scheduled tasks (RegBackup, Windows-Update toggle tweaks).</summary>
public interface ITaskSchedulerService
{
    OperationResult SetTaskEnabled(string taskPath, bool enabled);
    OperationResult RegisterDailyTask(string taskName, string executable, string arguments, TimeOnly time);
    OperationResult DeleteTask(string taskName);
}
