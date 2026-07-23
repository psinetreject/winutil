using Microsoft.Extensions.DependencyInjection;
using WinUtil.Core.Abstractions;
using WinUtil.Platform.Iso;
using WinUtil.Platform.Packages;
using WinUtil.Platform.Provisioning;
using WinUtil.Platform.System;

namespace WinUtil.Platform.DependencyInjection;

/// <summary>
/// Registers the Windows system-manipulation implementations. Class names here must match the
/// concrete types produced by the platform subsystems (System / Packages / Provisioning / Iso).
/// </summary>
public static class PlatformServiceCollectionExtensions
{
    public static IServiceCollection AddWinUtilPlatform(this IServiceCollection services)
    {
        // System
        services.AddSingleton<IRegistryService, RegistryService>();
        services.AddSingleton<IServiceManager, ServiceManager>();
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<IElevationService, ElevationService>();
        services.AddSingleton<IExplorerRefresher, ExplorerRefresher>();

        // Packages (two managers registered as IPackageManager; installer resolves by Kind)
        services.AddSingleton<IPackageManager, WingetPackageManager>();
        services.AddSingleton<IPackageManager, ChocoPackageManager>();
        services.AddSingleton<IPackageInstaller, PackageInstaller>();
        services.AddSingleton<IAppxManager, AppxManager>();

        // Provisioning
        services.AddSingleton<IWindowsFeatureService, WindowsFeatureService>();
        services.AddSingleton<IPowerPlanService, PowerPlanService>();
        services.AddSingleton<IDnsService, DnsService>();
        services.AddSingleton<ISystemRestoreService, SystemRestoreService>();
        services.AddSingleton<ITaskSchedulerService, TaskSchedulerService>();

        // MicroWin ISO
        services.AddSingleton<IMicroWinBuilder, MicroWinBuilder>();
        services.AddSingleton<IUsbWriter, UsbWriter>();

        return services;
    }
}
