namespace WinUtil.Core.Abstractions;

public enum ProgressState
{
    Normal,
    Indeterminate,
    Error,
    Done,
}

/// <summary>A progress update marshaled to the UI (replaces the $sync taskbar/progress plumbing).</summary>
public readonly record struct TaskProgress(int Percent, string Message, ProgressState State = ProgressState.Normal)
{
    public static TaskProgress Indeterminate(string message) => new(0, message, ProgressState.Indeterminate);
    public static TaskProgress Completed(string message = "Done") => new(100, message, ProgressState.Done);
    public static TaskProgress Failed(string message) => new(0, message, ProgressState.Error);
}
