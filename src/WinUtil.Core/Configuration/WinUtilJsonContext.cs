using System.Text.Json;
using System.Text.Json.Serialization;
using WinUtil.Core.Models;

namespace WinUtil.Core.Configuration;

/// <summary>
/// System.Text.Json source-generated (de)serialization metadata for the embedded configs.
/// Reflection-free and trim/AOT-friendly. TypeInfoPropertyName gives each type a stable accessor
/// (e.g. <c>WinUtilJsonContext.Default.Applications</c>).
/// </summary>
[JsonSourceGenerationOptions(
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(Dictionary<string, AppEntry>), TypeInfoPropertyName = "Applications")]
[JsonSerializable(typeof(Dictionary<string, AppxEntry>), TypeInfoPropertyName = "Appx")]
[JsonSerializable(typeof(Dictionary<string, DnsProvider>), TypeInfoPropertyName = "Dns")]
[JsonSerializable(typeof(Dictionary<string, List<string>>), TypeInfoPropertyName = "Presets")]
[JsonSerializable(typeof(Dictionary<string, Dictionary<string, JsonElement>>), TypeInfoPropertyName = "Themes")]
[JsonSerializable(typeof(Dictionary<string, Tweak>), TypeInfoPropertyName = "Tweaks")]
[JsonSerializable(typeof(Dictionary<string, FeatureEntry>), TypeInfoPropertyName = "Features")]
internal partial class WinUtilJsonContext : JsonSerializerContext;
