using Microsoft.Extensions.Logging;
using Microsoft.Win32.TaskScheduler;
using WinUtil.Core.Abstractions;

namespace WinUtil.Platform.Provisioning;

/// <summary>
/// Enables/disables, registers, and removes Windows scheduled tasks through the Task Scheduler 2.0
/// COM wrapper (dahall/TaskScheduler).
/// </summary>
public sealed class TaskSchedulerService(ILogger<TaskSchedulerService> logger) : ITaskSchedulerService
{
    private readonly ILogger<TaskSchedulerService> _logger = logger;

    public OperationResult SetTaskEnabled(string taskPath, bool enabled)
    {
        if (string.IsNullOrWhiteSpace(taskPath))
        {
            return OperationResult.Fail("A task path is required.");
        }

        try
        {
            using TaskService ts = new();

            // GetTask returns null when the path does not resolve to a registered task.
            var task = ts.GetTask(taskPath);
            if (task is null)
            {
                _logger.LogWarning("Scheduled task {TaskPath} was not found.", taskPath);
                return OperationResult.Fail($"Scheduled task '{taskPath}' was not found.");
            }

            task.Enabled = enabled;
            _logger.LogInformation(
                "Set scheduled task {TaskPath} enabled = {Enabled}.", taskPath, enabled);
            return OperationResult.Ok(
                $"Scheduled task '{taskPath}' {(enabled ? "enabled" : "disabled")}.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to toggle scheduled task {TaskPath}.", taskPath);
            return OperationResult.Fail($"Failed to update scheduled task '{taskPath}': {ex.Message}");
        }
    }

    public OperationResult RegisterDailyTask(string taskName, string executable, string arguments, TimeOnly time)
    {
        if (string.IsNullOrWhiteSpace(taskName))
        {
            return OperationResult.Fail("A task name is required.");
        }

        if (string.IsNullOrWhiteSpace(executable))
        {
            return OperationResult.Fail("An executable path is required.");
        }

        try
        {
            using TaskService ts = new();

            TaskDefinition definition = ts.NewTask();
            definition.RegistrationInfo.Description = $"WinUtil scheduled task '{taskName}'.";

            definition.Triggers.Add(new DailyTrigger
            {
                DaysInterval = 1,
                StartBoundary = DateTime.Today.Add(time.ToTimeSpan()),
            });

            definition.Actions.Add(new ExecAction(
                executable,
                string.IsNullOrEmpty(arguments) ? null : arguments,
                null));

            ts.RootFolder.RegisterTaskDefinition(taskName, definition);

            _logger.LogInformation(
                "Registered daily scheduled task {TaskName} at {Time}.", taskName, time);
            return OperationResult.Ok($"Scheduled task '{taskName}' registered.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register scheduled task {TaskName}.", taskName);
            return OperationResult.Fail($"Failed to register scheduled task '{taskName}': {ex.Message}");
        }
    }

    public OperationResult DeleteTask(string taskName)
    {
        if (string.IsNullOrWhiteSpace(taskName))
        {
            return OperationResult.Fail("A task name is required.");
        }

        try
        {
            using TaskService ts = new();

            // exceptionOnNotFound: false — removal is idempotent, an absent task is a success.
            ts.RootFolder.DeleteTask(taskName, false);

            _logger.LogInformation("Deleted scheduled task {TaskName} (if present).", taskName);
            return OperationResult.Ok($"Scheduled task '{taskName}' removed.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete scheduled task {TaskName}.", taskName);
            return OperationResult.Fail($"Failed to delete scheduled task '{taskName}': {ex.Message}");
        }
    }
}
