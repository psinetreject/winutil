using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using WinUtil.Core.Configuration;

namespace WinUtil.App.Theming;

/// <summary>
/// Applies theme tokens from <see cref="WinUtilConfig.Themes"/> ("shared" + "Light"/"Dark") into
/// <see cref="Application"/>.Resources as DynamicResource-consumable entries. Mirrors the PowerShell
/// Invoke-WinutilThemeChange type heuristic: *color* → SolidColorBrush, *Radius* → CornerRadius,
/// *RowHeight* → GridLength, *Thickness*/*margin → Thickness, *FontFamily* → FontFamily, else double.
/// </summary>
public sealed class ThemeService
{
    // Tokens that also need a raw Color companion ("C{name}") for effects that take a Color, not a Brush.
    private static readonly string[] ColorAlsoAsColor = ["BorderColor", "ButtonBackgroundMouseoverColor"];

    private readonly WinUtilConfig _config;

    public ThemeService(WinUtilConfig config) => _config = config;

    /// <summary>Applies "shared" then the effective palette. <paramref name="mode"/> is Auto/Light/Dark.</summary>
    public void ApplyTheme(string mode)
    {
        var resources = Application.Current?.Resources;
        if (resources is null)
        {
            return;
        }

        ApplySet(resources, "shared");

        var effective = mode switch
        {
            "Light" => "Light",
            "Dark" => "Dark",
            _ => SystemUsesDarkMode() ? "Dark" : "Light",
        };

        ApplySet(resources, effective);
    }

    private void ApplySet(ResourceDictionary resources, string key)
    {
        if (!_config.Themes.TryGetValue(key, out var tokens))
        {
            return;
        }

        foreach (var (name, element) in tokens)
        {
            var value = element.ValueKind == JsonValueKind.String
                ? element.GetString() ?? string.Empty
                : element.GetRawText();

            if (TryConvert(name, value, out var resource))
            {
                resources[name] = resource;
            }

            if (name.Contains("color", StringComparison.OrdinalIgnoreCase)
                && Array.Exists(ColorAlsoAsColor, n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase))
                && TryParseColor(value, out var color))
            {
                resources["C" + name] = color;
            }
        }
    }

    private static bool TryConvert(string name, string value, out object resource)
    {
        try
        {
            if (name.Contains("color", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryParseColor(value, out var color))
                {
                    resource = value;
                    return false;
                }

                resource = new SolidColorBrush(color);
            }
            else if (name.Contains("Radius", StringComparison.OrdinalIgnoreCase))
            {
                resource = new CornerRadius(ParseDouble(value));
            }
            else if (name.Contains("RowHeight", StringComparison.OrdinalIgnoreCase))
            {
                resource = new GridLength(ParseDouble(value));
            }
            else if (name.Contains("Thickness", StringComparison.OrdinalIgnoreCase)
                     || name.EndsWith("margin", StringComparison.OrdinalIgnoreCase))
            {
                resource = ParseThickness(value);
            }
            else if (name.Contains("FontFamily", StringComparison.OrdinalIgnoreCase))
            {
                resource = new FontFamily(value);
            }
            else
            {
                resource = ParseDouble(value);
            }

            return true;
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException)
        {
            resource = value;
            return false;
        }
    }

    private static bool TryParseColor(string value, out Color color)
    {
        try
        {
            if (ColorConverter.ConvertFromString(value) is Color parsed)
            {
                color = parsed;
                return true;
            }
        }
        catch (FormatException)
        {
            // Fall through to the failure result.
        }

        color = Colors.Transparent;
        return false;
    }

    private static double ParseDouble(string value) =>
        double.Parse(value, CultureInfo.InvariantCulture);

    private static Thickness ParseThickness(string value)
    {
        var parts = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            1 => new Thickness(ParseDouble(parts[0])),
            2 => new Thickness(ParseDouble(parts[0]), ParseDouble(parts[1]), ParseDouble(parts[0]), ParseDouble(parts[1])),
            4 => new Thickness(ParseDouble(parts[0]), ParseDouble(parts[1]), ParseDouble(parts[2]), ParseDouble(parts[3])),
            _ => new Thickness(0),
        };
    }

    private static bool SystemUsesDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int appsUseLightTheme)
            {
                return appsUseLightTheme == 0;
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            // Fall through to the dark default.
        }

        return true;
    }
}
