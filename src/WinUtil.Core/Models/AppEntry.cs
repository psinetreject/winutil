using System.Text.Json.Serialization;

namespace WinUtil.Core.Models;

/// <summary>A single installable application from config/applications.json.</summary>
public sealed record AppEntry
{
    [JsonPropertyName("content")]     public required string Content { get; init; }
    [JsonPropertyName("description")] public string? Description { get; init; }
    [JsonPropertyName("category")]    public string? Category { get; init; }
    [JsonPropertyName("link")]        public string? Link { get; init; }
    [JsonPropertyName("winget")]      public string? Winget { get; init; }
    [JsonPropertyName("choco")]       public string? Choco { get; init; }
    [JsonPropertyName("foss")]        public bool Foss { get; init; }

    /// <summary>True when this app has a real Chocolatey package (choco != "na").</summary>
    [JsonIgnore]
    public bool HasChoco =>
        !string.IsNullOrWhiteSpace(Choco) &&
        !string.Equals(Choco, "na", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the winget id routes through the Microsoft Store (msstore: prefix).</summary>
    [JsonIgnore]
    public bool IsMsStore =>
        Winget?.StartsWith("msstore:", StringComparison.OrdinalIgnoreCase) ?? false;
}
