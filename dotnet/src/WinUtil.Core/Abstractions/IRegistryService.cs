namespace WinUtil.Core.Abstractions;

public enum RegistryValueKind
{
    String,
    ExpandString,
    Binary,
    DWord,
    MultiString,
    QWord,
}

/// <summary>
/// Reads and writes the live Windows registry. Offline-hive edits for the ISO builder are handled
/// separately by the MicroWin offline-registry helper.
/// </summary>
public interface IRegistryService
{
    /// <summary>
    /// Sets a value, creating the key path if needed. The sentinel value "&lt;RemoveEntry&gt;" deletes
    /// the value instead (mirrors the PowerShell tool's undo behavior).
    /// </summary>
    OperationResult SetValue(string path, string name, string? value, RegistryValueKind kind);

    OperationResult DeleteValue(string path, string name);

    /// <summary>Reads a value as string; null when the key or value is absent.</summary>
    string? GetValue(string path, string name);
}
