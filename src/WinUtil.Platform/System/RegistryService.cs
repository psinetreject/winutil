using System.Globalization;
using Microsoft.Extensions.Logging;
using WinUtil.Core.Abstractions;
using Win32 = Microsoft.Win32;

namespace WinUtil.Platform.System;

/// <summary>
/// Live-registry reader/writer backed by <see cref="Win32.RegistryKey"/>. Mirrors the PowerShell
/// tool's Set-WinUtilRegistry: creates key paths on demand, understands the string <c>Type</c>
/// names, and honors the "&lt;RemoveEntry&gt;" undo sentinel. All failures are logged and returned
/// as <see cref="OperationResult.Fail(string)"/> — this type never throws.
/// </summary>
public sealed class RegistryService : IRegistryService
{
    /// <summary>Sentinel value that deletes the entry instead of writing it (undo behavior).</summary>
    private const string RemoveEntrySentinel = "<RemoveEntry>";

    private readonly ILogger<RegistryService> _logger;

    public RegistryService(ILogger<RegistryService> logger) => _logger = logger;

    public OperationResult SetValue(string path, string name, string? value, RegistryValueKind kind)
    {
        if (string.Equals(value, RemoveEntrySentinel, StringComparison.Ordinal))
        {
            return DeleteValue(path, name);
        }

        var parsed = Parse(path);
        if (parsed is null)
        {
            return OperationResult.Fail($"Unrecognized registry path: '{path}'.");
        }

        (Win32.RegistryKey baseKey, string subPath) = parsed.Value;
        if (subPath.Length == 0)
        {
            return OperationResult.Fail($"Registry path '{path}' does not include a sub-key.");
        }

        try
        {
            using Win32.RegistryKey? key = baseKey.CreateSubKey(subPath, writable: true);
            if (key is null)
            {
                return OperationResult.Fail($"Could not open or create registry key '{path}'.");
            }

            object data = ConvertValue(value, kind);
            key.SetValue(name, data, MapKind(kind));
            return OperationResult.Ok($"Set {path}\\{name}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set registry value {Path}\\{Name}", path, name);
            return OperationResult.Fail($"Failed to set {path}\\{name}: {ex.Message}");
        }
    }

    public OperationResult DeleteValue(string path, string name)
    {
        var parsed = Parse(path);
        if (parsed is null)
        {
            return OperationResult.Fail($"Unrecognized registry path: '{path}'.");
        }

        (Win32.RegistryKey baseKey, string subPath) = parsed.Value;
        if (subPath.Length == 0)
        {
            return OperationResult.Fail($"Registry path '{path}' does not include a sub-key.");
        }

        try
        {
            using Win32.RegistryKey? key = baseKey.OpenSubKey(subPath, writable: true);
            if (key is null)
            {
                // Key already absent: nothing to remove (idempotent undo).
                return OperationResult.Ok($"Registry key '{path}' not present; nothing to delete.");
            }

            key.DeleteValue(name, throwOnMissingValue: false);
            return OperationResult.Ok($"Removed {path}\\{name}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete registry value {Path}\\{Name}", path, name);
            return OperationResult.Fail($"Failed to delete {path}\\{name}: {ex.Message}");
        }
    }

    public string? GetValue(string path, string name)
    {
        var parsed = Parse(path);
        if (parsed is null)
        {
            return null;
        }

        (Win32.RegistryKey baseKey, string subPath) = parsed.Value;
        if (subPath.Length == 0)
        {
            return null;
        }

        try
        {
            using Win32.RegistryKey? key = baseKey.OpenSubKey(subPath, writable: false);
            object? raw = key?.GetValue(name, null, Win32.RegistryValueOptions.DoNotExpandEnvironmentNames);
            return raw is null ? null : Stringify(raw);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read registry value {Path}\\{Name}", path, name);
            return null;
        }
    }

    /// <summary>
    /// Resolves a path such as <c>HKLM:\SOFTWARE\Foo</c>, <c>HKCU\Software\Bar</c>, or
    /// <c>HKEY_LOCAL_MACHINE\...</c> into a base <see cref="Win32.RegistryKey"/> and the remaining
    /// sub-key path. Returns <see langword="null"/> when the hive prefix is not recognized.
    /// The returned base key is a process-wide singleton and must not be disposed.
    /// </summary>
    private static (Win32.RegistryKey BaseKey, string SubPath)? Parse(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string p = path.Trim();

        // Strip the ".NET/PowerShell provider" prefix if present.
        if (p.StartsWith("Registry::", StringComparison.OrdinalIgnoreCase))
        {
            p = p["Registry::".Length..];
        }

        int idx = p.AsSpan().IndexOfAny(':', '\\');
        string hive = idx < 0 ? p : p[..idx];
        string sub = idx < 0 ? string.Empty : p[idx..].TrimStart(':', '\\');

        Win32.RegistryKey? baseKey = hive.ToUpperInvariant() switch
        {
            "HKLM" or "HKEY_LOCAL_MACHINE" => Win32.Registry.LocalMachine,
            "HKCU" or "HKEY_CURRENT_USER" => Win32.Registry.CurrentUser,
            "HKCR" or "HKEY_CLASSES_ROOT" => Win32.Registry.ClassesRoot,
            "HKU" or "HKEY_USERS" => Win32.Registry.Users,
            "HKCC" or "HKEY_CURRENT_CONFIG" => Win32.Registry.CurrentConfig,
            _ => null,
        };

        return baseKey is null ? null : (baseKey, sub);
    }

    private static Win32.RegistryValueKind MapKind(RegistryValueKind kind) => kind switch
    {
        RegistryValueKind.String => Win32.RegistryValueKind.String,
        RegistryValueKind.ExpandString => Win32.RegistryValueKind.ExpandString,
        RegistryValueKind.Binary => Win32.RegistryValueKind.Binary,
        RegistryValueKind.DWord => Win32.RegistryValueKind.DWord,
        RegistryValueKind.MultiString => Win32.RegistryValueKind.MultiString,
        RegistryValueKind.QWord => Win32.RegistryValueKind.QWord,
        _ => Win32.RegistryValueKind.String,
    };

    /// <summary>Converts the incoming string value into the CLR type expected by RegistryKey.SetValue.</summary>
    private static object ConvertValue(string? value, RegistryValueKind kind) => kind switch
    {
        RegistryValueKind.DWord => ParseDword(value),
        RegistryValueKind.QWord => ParseQword(value),
        RegistryValueKind.Binary => ParseBinary(value),
        RegistryValueKind.MultiString => ParseMultiString(value),
        _ => value ?? string.Empty,
    };

    private static int ParseDword(string? value)
    {
        var (text, style) = SplitNumber(value);
        if (uint.TryParse(text, style, CultureInfo.InvariantCulture, out uint u))
        {
            return unchecked((int)u);
        }

        return int.TryParse(text, style, CultureInfo.InvariantCulture, out int i) ? i : 0;
    }

    private static long ParseQword(string? value)
    {
        var (text, style) = SplitNumber(value);
        if (ulong.TryParse(text, style, CultureInfo.InvariantCulture, out ulong u))
        {
            return unchecked((long)u);
        }

        return long.TryParse(text, style, CultureInfo.InvariantCulture, out long l) ? l : 0L;
    }

    private static (string Text, NumberStyles Style) SplitNumber(string? value)
    {
        string text = (value ?? string.Empty).Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return (text[2..], NumberStyles.HexNumber);
        }

        return (text, NumberStyles.Integer);
    }

    private static byte[] ParseBinary(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        // Accept "00,01,ff", "0001ff", "hex:00,01" etc. — keep hex digits only.
        char[] digits = value.Where(Uri.IsHexDigit).ToArray();
        return Convert.FromHexString(digits); // throws on odd length -> caught by caller
    }

    private static string[] ParseMultiString(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return [];
        }

        string[] parts = value.Contains('\0')
            ? value.Split('\0', StringSplitOptions.RemoveEmptyEntries)
            : value.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        return [.. parts.Select(static s => s.TrimEnd('\r'))];
    }

    private static string Stringify(object raw) => raw switch
    {
        byte[] bytes => Convert.ToHexString(bytes),
        string[] many => string.Join(", ", many),
        int i => i.ToString(CultureInfo.InvariantCulture),
        long l => l.ToString(CultureInfo.InvariantCulture),
        _ => raw.ToString() ?? string.Empty,
    };
}
