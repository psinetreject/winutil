using Microsoft.Extensions.DependencyInjection;
using WinUtil.Core.Abstractions;

namespace WinUtil.Core.Tweaks.Actions;

/// <summary>
/// Enables periodic registry backups: set the Configuration Manager policy values and register a daily
/// scheduled task that triggers the built-in RegIdleBackup. Complex (NOUNDO).
/// </summary>
public sealed class RegBackupAction : ICustomTweakAction
{
    public string Id => "WPFFeatureRegBackup";
    public bool SupportsUndo => false;

    private const string ConfigManagerPath =
        "HKLM:\\SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Configuration Manager";

    public Task<OperationResult> ApplyAsync(TweakActionContext context)
    {
        context.Registry.SetValue(ConfigManagerPath, "EnablePeriodicBackup", "1", RegistryValueKind.DWord);
        context.Registry.SetValue(ConfigManagerPath, "BackupCount", "2", RegistryValueKind.DWord);

        var scheduler = context.ServiceProvider.GetService<ITaskSchedulerService>();
        if (scheduler is null)
        {
            return Task.FromResult(OperationResult.Fail("Task scheduler service is unavailable"));
        }

        // PARITY: PS registers this task to run as the SYSTEM principal
        // (`Register-ScheduledTask -User 'System'`). Running-as-SYSTEM is expected to be the default
        // principal used by ITaskSchedulerService.RegisterDailyTask.
        var result = scheduler.RegisterDailyTask(
            "AutoRegBackup",
            "schtasks",
            "/run /i /tn \"\\Microsoft\\Windows\\Registry\\RegIdleBackup\"",
            new TimeOnly(0, 30));

        return Task.FromResult(result.Success
            ? OperationResult.Ok("Periodic registry backup enabled")
            : result);
    }

    public Task<OperationResult> UndoAsync(TweakActionContext context)
        => Task.FromResult(OperationResult.Ok("Registry backup tweak has no undo"));
}
