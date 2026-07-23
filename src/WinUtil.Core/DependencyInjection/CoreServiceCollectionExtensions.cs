using Microsoft.Extensions.DependencyInjection;
using WinUtil.Core.Configuration;
using WinUtil.Core.Tweaks;

namespace WinUtil.Core.DependencyInjection;

/// <summary>Registers the platform-agnostic Core services (config + tweak engine).</summary>
public static class CoreServiceCollectionExtensions
{
    public static IServiceCollection AddWinUtilCore(this IServiceCollection services)
    {
        // The embedded JSON config, loaded once and shared.
        services.AddSingleton(_ => ConfigLoader.Load());

        // TweakEngine + CustomActionRegistry + every ICustomTweakAction (defined in WinUtil.Core.Tweaks).
        services.AddTweakEngine();

        // The remaining 10 checkbox/toggle actions (DisableStoreSearch, Display, Hiber, RightClickMenu,
        // Teredo + the 5 Explorer-refresh toggles) that close the tweak-engine parity gap.
        services.AddTweakExtraActions();

        return services;
    }
}
