using System.Text.Json.Serialization;

namespace WinUtil.Core.Models;

/// <summary>A Windows optional-feature bundle from the ported features config.</summary>
public sealed record FeatureEntry
{
    [JsonIgnore] public string Id { get; init; } = string.Empty;   // filled from the dictionary key

    [JsonPropertyName("content")]        public string? Content { get; init; }
    [JsonPropertyName("description")]    public string? Description { get; init; }
    [JsonPropertyName("category")]       public string? Category { get; init; }
    [JsonPropertyName("features")]       public IReadOnlyList<string> Features { get; init; } = [];

    /// <summary>Custom C# action id for feature entries that do more than enable optional features.</summary>
    [JsonPropertyName("customActionId")] public string? CustomActionId { get; init; }
}
