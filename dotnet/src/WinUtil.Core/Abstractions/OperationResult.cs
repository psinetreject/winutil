namespace WinUtil.Core.Abstractions;

/// <summary>Outcome of a system operation. Favors returned results over exceptions-as-control-flow.</summary>
public readonly record struct OperationResult
{
    public bool Success { get; private init; }
    public string? Message { get; private init; }

    public static OperationResult Ok(string? message = null) => new() { Success = true, Message = message };
    public static OperationResult Fail(string message) => new() { Success = false, Message = message };
}

/// <summary>Outcome of a system operation that yields a value.</summary>
public readonly record struct OperationResult<T>
{
    public bool Success { get; private init; }
    public string? Message { get; private init; }
    public T? Value { get; private init; }

    public static OperationResult<T> Ok(T value, string? message = null) =>
        new() { Success = true, Value = value, Message = message };

    public static OperationResult<T> Fail(string message) =>
        new() { Success = false, Message = message };
}
