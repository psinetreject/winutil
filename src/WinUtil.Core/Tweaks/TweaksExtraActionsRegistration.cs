using Microsoft.Extensions.DependencyInjection;
using WinUtil.Core.Tweaks.Actions;

namespace WinUtil.Core.Tweaks;

/// <summary>
/// Registers the second batch of hand-written <see cref="ICustomTweakAction"/>s — the ten tweaks whose
/// ported <c>tweaks.json</c> entries reference a <c>customActionId</c> but were not covered by the
/// first batch in <see cref="TweaksServiceCollectionExtensions"/>. Called from AddWinUtilCore.
/// </summary>
public static class TweaksExtraActionsRegistration
{
    public static IServiceCollection AddTweakExtraActions(this IServiceCollection services)
    {
        services.AddSingleton<ICustomTweakAction, DarkModeAction>();
        services.AddSingleton<ICustomTweakAction, DisableStoreSearchAction>();
        services.AddSingleton<ICustomTweakAction, DisplayAction>();
        services.AddSingleton<ICustomTweakAction, HiberAction>();
        services.AddSingleton<ICustomTweakAction, HiddenFilesAction>();
        services.AddSingleton<ICustomTweakAction, RightClickMenuAction>();
        services.AddSingleton<ICustomTweakAction, ShowExtAction>();
        services.AddSingleton<ICustomTweakAction, StartMenuRecommendationsAction>();
        services.AddSingleton<ICustomTweakAction, TaskbarAlignmentAction>();
        services.AddSingleton<ICustomTweakAction, TeredoAction>();

        return services;
    }
}
