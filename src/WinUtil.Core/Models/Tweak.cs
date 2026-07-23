using System.Text.Json.Serialization;

namespace WinUtil.Core.Models;

/// <summary>
/// A tweak in the .NET port. The PowerShell tweaks.json stored imperative logic as embedded
/// InvokeScript/UndoScript strings; here those become a <see cref="CustomActionId"/> resolved against
/// the C# custom-action registry (see dotnet/docs/tweaks-inventory.md). Declarative registry/service
/// changes stay data-driven. Deserialized from the ported dotnet/config/tweaks.json.
/// </summary>
public sealed record Tweak
{
    [JsonIgnore] public string Id { get; init; } = string.Empty;   // filled from the dictionary key

    [JsonPropertyName("content")]     public string? Content { get; init; }
    [JsonPropertyName("description")] public string? Description { get; init; }
    [JsonPropertyName("category")]    public string? Category { get; init; }
    [JsonPropertyName("kind")]        public TweakKind Kind { get; init; } = TweakKind.Checkbox;

    [JsonPropertyName("registry")]    public IReadOnlyList<RegistryAction> Registry { get; init; } = [];
    [JsonPropertyName("services")]    public IReadOnlyList<ServiceAction> Services { get; init; } = [];

    /// <summary>Key into the C# custom-action registry, replacing InvokeScript/UndoScript.</summary>
    [JsonPropertyName("customActionId")] public string? CustomActionId { get; init; }

    /// <summary>Assumed value when a toggle's registry key is absent (mirrors DefaultState).</summary>
    [JsonPropertyName("defaultState")]   public bool? DefaultState { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter<TweakKind>))]
public enum TweakKind
{
    Checkbox,
    Toggle,
    Button,
    Combobox,
}

/// <summary>A single registry change with its undo value (mirrors OriginalValue).</summary>
public sealed record RegistryAction
{
    [JsonPropertyName("path")]          public required string Path { get; init; }
    [JsonPropertyName("name")]          public required string Name { get; init; }
    [JsonPropertyName("type")]          public required string Type { get; init; }   // Dword, String, Binary, ...
    [JsonPropertyName("value")]         public string? Value { get; init; }           // apply
    [JsonPropertyName("originalValue")] public string? OriginalValue { get; init; }   // undo ("<RemoveEntry>" => delete)
}

/// <summary>A service startup-type change with its undo type.</summary>
public sealed record ServiceAction
{
    [JsonPropertyName("name")]         public required string Name { get; init; }
    [JsonPropertyName("startupType")]  public required string StartupType { get; init; }   // apply
    [JsonPropertyName("originalType")] public string? OriginalType { get; init; }          // undo
}
