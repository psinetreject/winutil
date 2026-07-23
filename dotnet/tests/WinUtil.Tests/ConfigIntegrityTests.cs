using WinUtil.Core.Configuration;
using Xunit;

namespace WinUtil.Tests;

/// <summary>
/// Verifies the embedded JSON configs deserialize into the typed model and satisfy the
/// invariants the rest of the app relies on. Mirrors the intent of pester/configs.Tests.ps1.
/// </summary>
public sealed class ConfigIntegrityTests
{
    private static readonly WinUtilConfig Config = ConfigLoader.Load();

    [Fact]
    public void Applications_LoadAndEveryEntryHasContent()
    {
        Assert.NotEmpty(Config.Applications);
        Assert.All(Config.Applications.Values,
            app => Assert.False(string.IsNullOrWhiteSpace(app.Content)));
    }

    [Fact]
    public void Appx_LoadAndEveryEntryHasContent()
    {
        Assert.NotEmpty(Config.Appx);
        Assert.All(Config.Appx.Values,
            appx => Assert.False(string.IsNullOrWhiteSpace(appx.Content)));
    }

    [Fact]
    public void Dns_EveryProviderHasAPrimaryServer()
    {
        Assert.NotEmpty(Config.Dns);
        Assert.All(Config.Dns.Values,
            dns => Assert.False(string.IsNullOrWhiteSpace(dns.Primary)));
    }

    [Fact]
    public void Presets_ContainStandardWithEntries()
    {
        Assert.Contains("Standard", Config.Presets.Keys);
        Assert.NotEmpty(Config.Presets["Standard"]);
    }

    [Fact]
    public void Themes_ContainLightAndDark()
    {
        Assert.Contains("Light", Config.Themes.Keys);
        Assert.Contains("Dark", Config.Themes.Keys);
    }
}
