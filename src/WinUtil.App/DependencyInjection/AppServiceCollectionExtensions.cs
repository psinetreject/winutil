using Microsoft.Extensions.DependencyInjection;
using WinUtil.App.Theming;
using WinUtil.App.ViewModels;

namespace WinUtil.App.DependencyInjection;

/// <summary>
/// Registers the WPF application layer: the shell, all view-models, and the theme service. The host
/// composition root is expected to also call AddWinUtilCore / AddWinUtilPlatform (which register
/// <c>WinUtilConfig</c>, <c>IPackageInstaller</c>, <c>ITweakEngine</c>, and the optional fix services)
/// and then resolve <see cref="MainWindow"/>.
/// </summary>
public static class AppServiceCollectionExtensions
{
    public static IServiceCollection AddWinUtilApp(this IServiceCollection services)
    {
        // Shared shell state + theming.
        services.AddSingleton<ShellState>();
        services.AddSingleton<ThemeService>();

        // Tab view-models.
        services.AddSingleton<InstallViewModel>();
        services.AddSingleton<TweaksViewModel>();
        services.AddSingleton<ConfigViewModel>();
        services.AddSingleton<UpdatesViewModel>();

        // Root VM + window.
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        return services;
    }
}
