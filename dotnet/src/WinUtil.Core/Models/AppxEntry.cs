using System.Text.Json.Serialization;

namespace WinUtil.Core.Models;

/// <summary>A Windows Store / Appx package from config/appx.json.</summary>
public sealed record AppxEntry
{
    [JsonPropertyName("Content")]     public required string Content { get; init; }
    [JsonPropertyName("Description")] public string? Description { get; init; }
    [JsonPropertyName("Category")]    public string? Category { get; init; }
    [JsonPropertyName("Panel")]       public string? Panel { get; init; }
    [JsonPropertyName("PackageId")]   public string? PackageId { get; init; }
    [JsonPropertyName("StoreId")]     public string? StoreId { get; init; }
}
