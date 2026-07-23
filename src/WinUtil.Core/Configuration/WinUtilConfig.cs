using System.Text.Json;
using WinUtil.Core.Models;

namespace WinUtil.Core.Configuration;

/// <summary>Strongly-typed, immutable view over the embedded WinUtil JSON configuration.</summary>
public sealed class WinUtilConfig
{
    public required IReadOnlyDictionary<string, AppEntry> Applications { get; init; }
    public required IReadOnlyDictionary<string, AppxEntry> Appx { get; init; }
    public required IReadOnlyDictionary<string, DnsProvider> Dns { get; init; }
    public required IReadOnlyDictionary<string, IReadOnlyList<string>> Presets { get; init; }
    public required IReadOnlyDictionary<string, IReadOnlyDictionary<string, JsonElement>> Themes { get; init; }
    public required IReadOnlyDictionary<string, Tweak> Tweaks { get; init; }
    public required IReadOnlyDictionary<string, FeatureEntry> Features { get; init; }
}
