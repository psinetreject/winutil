using System.Text.Json.Serialization;

namespace WinUtil.Core.Models;

/// <summary>A DNS provider's IPv4/IPv6 servers from config/dns.json.</summary>
public sealed record DnsProvider
{
    [JsonPropertyName("Primary")]    public string? Primary { get; init; }
    [JsonPropertyName("Secondary")]  public string? Secondary { get; init; }
    [JsonPropertyName("Primary6")]   public string? Primary6 { get; init; }
    [JsonPropertyName("Secondary6")] public string? Secondary6 { get; init; }
}
