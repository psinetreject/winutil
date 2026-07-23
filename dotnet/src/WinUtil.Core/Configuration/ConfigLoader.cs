using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace WinUtil.Core.Configuration;

/// <summary>Loads and deserializes the embedded WinUtil JSON configuration.</summary>
public static class ConfigLoader
{
    private static readonly Assembly ResourceAssembly = typeof(ConfigLoader).Assembly;

    /// <summary>Reads every embedded config resource into a typed <see cref="WinUtilConfig"/>.</summary>
    public static WinUtilConfig Load()
    {
        var applications = Deserialize("winutil.applications.json", WinUtilJsonContext.Default.Applications);
        var appx = Deserialize("winutil.appx.json", WinUtilJsonContext.Default.Appx);
        var dns = Deserialize("winutil.dns.json", WinUtilJsonContext.Default.Dns);
        var presets = Deserialize("winutil.preset.json", WinUtilJsonContext.Default.Presets);
        var themes = Deserialize("winutil.themes.json", WinUtilJsonContext.Default.Themes);
        var tweaks = Deserialize("winutil.tweaks.json", WinUtilJsonContext.Default.Tweaks);
        var features = Deserialize("winutil.features.json", WinUtilJsonContext.Default.Features);

        return new WinUtilConfig
        {
            Applications = applications,
            Appx = appx,
            Dns = dns,
            Presets = presets.ToDictionary(kvp => kvp.Key, kvp => (IReadOnlyList<string>)kvp.Value),
            Themes = themes.ToDictionary(kvp => kvp.Key, kvp => (IReadOnlyDictionary<string, JsonElement>)kvp.Value),
            // Stamp each tweak/feature's Id from its dictionary key.
            Tweaks = tweaks.ToDictionary(kvp => kvp.Key, kvp => kvp.Value with { Id = kvp.Key }),
            Features = features.ToDictionary(kvp => kvp.Key, kvp => kvp.Value with { Id = kvp.Key }),
        };
    }

    private static T Deserialize<T>(string logicalName, JsonTypeInfo<T> typeInfo)
    {
        using var stream = ResourceAssembly.GetManifestResourceStream(logicalName)
            ?? throw new InvalidOperationException($"Embedded config resource '{logicalName}' was not found.");
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();

        return JsonSerializer.Deserialize(json, typeInfo)
            ?? throw new InvalidOperationException($"Config resource '{logicalName}' deserialized to null.");
    }
}
