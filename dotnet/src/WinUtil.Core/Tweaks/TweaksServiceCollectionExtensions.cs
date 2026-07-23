using Microsoft.Extensions.DependencyInjection;
using WinUtil.Core.Tweaks.Actions;

namespace WinUtil.Core.Tweaks;

/// <summary>
/// Registers the tweak engine, the custom-action registry, and every hand-written
/// <see cref="ICustomTweakAction"/>. Actions are registered alphabetically by type name.
/// </summary>
public static class TweaksServiceCollectionExtensions
{
    public static IServiceCollection AddTweakEngine(this IServiceCollection services)
    {
        services.AddSingleton<ITweakEngine, TweakEngine>();
        services.AddSingleton<ICustomActionRegistry, CustomActionRegistry>();

        // Custom tweak actions — one per imperative PowerShell InvokeScript/UndoScript pair, plus the
        // legacy control-panel launchers. Keep alphabetized.
        services.AddSingleton<ICustomTweakAction, BlockAdobeNetAction>();
        services.AddSingleton<ICustomTweakAction, ComputerAction>();
        services.AddSingleton<ICustomTweakAction, ControlAction>();
        services.AddSingleton<ICustomTweakAction, DeleteTempFilesAction>();
        services.AddSingleton<ICustomTweakAction, DisableBitLockerAction>();
        services.AddSingleton<ICustomTweakAction, DisableExplorerAutoDiscoveryAction>();
        services.AddSingleton<ICustomTweakAction, DisableIPv6Action>();
        services.AddSingleton<ICustomTweakAction, DiskCleanupAction>();
        services.AddSingleton<ICustomTweakAction, FirewallAction>();
        services.AddSingleton<ICustomTweakAction, MouseAction>();
        services.AddSingleton<ICustomTweakAction, NetworkAction>();
        services.AddSingleton<ICustomTweakAction, NfsAction>();
        services.AddSingleton<ICustomTweakAction, PowerAction>();
        services.AddSingleton<ICustomTweakAction, PrinterAction>();
        services.AddSingleton<ICustomTweakAction, ProgramsAction>();
        services.AddSingleton<ICustomTweakAction, RazerBlockAction>();
        services.AddSingleton<ICustomTweakAction, RegBackupAction>();
        services.AddSingleton<ICustomTweakAction, RegionAction>();
        services.AddSingleton<ICustomTweakAction, RemoveEdgeAction>();
        services.AddSingleton<ICustomTweakAction, RemoveOneDriveAction>();
        services.AddSingleton<ICustomTweakAction, ReservedStorageAction>();
        services.AddSingleton<ICustomTweakAction, RestoreAction>();
        services.AddSingleton<ICustomTweakAction, RestorePointAction>();
        services.AddSingleton<ICustomTweakAction, SecurityAction>();
        services.AddSingleton<ICustomTweakAction, ServicesAction>();
        services.AddSingleton<ICustomTweakAction, SoundAction>();
        services.AddSingleton<ICustomTweakAction, SystemAction>();
        services.AddSingleton<ICustomTweakAction, TelemetryAction>();
        services.AddSingleton<ICustomTweakAction, TimedateAction>();
        services.AddSingleton<ICustomTweakAction, WidgetAction>();
        services.AddSingleton<ICustomTweakAction, WindowsAIAction>();

        return services;
    }
}
