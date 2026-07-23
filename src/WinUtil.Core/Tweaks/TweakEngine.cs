using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WinUtil.Core.Abstractions;
using WinUtil.Core.Models;

namespace WinUtil.Core.Tweaks;

/// <summary>
/// Applies/undoes tweaks by dispatching their declarative registry + service actions and any custom
/// C# action. Pure-managed port of Invoke-WinUtilTweaks / Set-WinUtilRegistry / Set-WinUtilService.
/// </summary>
/// <remarks>
/// Ordering note: the PowerShell tool ran services → registry → script; this port runs
/// <b>registry → services → custom action</b> per the .NET porting spec. The three phases are
/// independent for every shipped tweak, so the observable result is identical.
/// </remarks>
public sealed class TweakEngine : ITweakEngine
{
    private readonly IRegistryService _registry;
    private readonly IServiceManager _services;
    private readonly ICustomActionRegistry _customActions;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TweakEngine> _logger;

    public TweakEngine(
        IRegistryService registry,
        IServiceManager services,
        ICustomActionRegistry customActions,
        IServiceProvider serviceProvider,
        ILogger<TweakEngine> logger)
    {
        _registry = registry;
        _services = services;
        _customActions = customActions;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public Task<OperationResult> ApplyAsync(Tweak tweak, IProgress<TaskProgress>? progress = null, CancellationToken cancellationToken = default)
        => RunAsync(tweak, undo: false, progress, cancellationToken);

    public Task<OperationResult> UndoAsync(Tweak tweak, IProgress<TaskProgress>? progress = null, CancellationToken cancellationToken = default)
        => RunAsync(tweak, undo: true, progress, cancellationToken);

    /// <summary>
    /// Reads the tweak's registry actions live and reports whether every one already matches its
    /// applied <see cref="RegistryAction.Value"/>. Mirrors Get-WinUtilToggleStatus: absent keys fall
    /// back to <see cref="Tweak.DefaultState"/>; the toggle is "on" only when all entries match.
    /// </summary>
    public bool GetToggleState(Tweak tweak)
    {
        // Vacuously "on" when there is nothing to read (mirrors Get-WinUtilToggleStatus, whose foreach
        // simply falls through to $true for a toggle with no registry entries).
        foreach (var action in tweak.Registry)
        {
            var live = _registry.GetValue(action.Path, action.Name);

            // Absent value → assume the configured DefaultState (true ⇒ Value, false ⇒ OriginalValue).
            live ??= tweak.DefaultState switch
            {
                true => action.Value,
                false => action.OriginalValue,
                _ => null,
            };

            if (!ValuesEqual(live, action.Value))
            {
                return false;
            }
        }

        return true;
    }

    private async Task<OperationResult> RunAsync(Tweak tweak, bool undo, IProgress<TaskProgress>? progress, CancellationToken cancellationToken)
    {
        var direction = undo ? "Undo" : "Apply";
        _logger.LogInformation("{Direction} tweak {TweakId}", direction, tweak.Id);
        progress?.Report(TaskProgress.Indeterminate($"{direction} {tweak.Content ?? tweak.Id}"));

        var failures = new List<string>();

        // 1. Declarative registry changes. On undo we write OriginalValue; the "<RemoveEntry>" sentinel
        //    is honored by IRegistryService.SetValue itself, so no special-casing is needed here.
        foreach (var action in tweak.Registry)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var value = undo ? action.OriginalValue : action.Value;
            if (value is null)
            {
                continue; // no value defined for this direction → nothing to do
            }

            var result = _registry.SetValue(action.Path, action.Name, value, MapRegistryKind(action.Type));
            if (!result.Success)
            {
                failures.Add(result.Message ?? $"registry {action.Path}\\{action.Name}");
            }
        }

        // 2. Declarative service startup changes.
        foreach (var service in tweak.Services)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var typeString = undo ? service.OriginalType : service.StartupType;
            if (string.IsNullOrWhiteSpace(typeString))
            {
                continue;
            }

            if (!TryMapStartupType(typeString, out var startup))
            {
                _logger.LogWarning("Unknown service startup type '{StartupType}' for service {Service}", typeString, service.Name);
                continue;
            }

            var result = _services.SetStartupType(service.Name, startup);
            if (!result.Success)
            {
                failures.Add(result.Message ?? $"service {service.Name}");
            }
        }

        // 3. Imperative custom action (replaces the PowerShell InvokeScript/UndoScript).
        if (!string.IsNullOrEmpty(tweak.CustomActionId))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_customActions.TryGet(tweak.CustomActionId, out var custom))
            {
                var context = BuildContext(progress, cancellationToken);
                try
                {
                    var result = undo ? await custom.UndoAsync(context) : await custom.ApplyAsync(context);
                    if (!result.Success)
                    {
                        failures.Add(result.Message ?? tweak.CustomActionId);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
#pragma warning disable CA1031 // The engine must isolate a misbehaving custom action, not crash the batch.
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Custom tweak action {ActionId} threw", tweak.CustomActionId);
                    failures.Add($"{tweak.CustomActionId}: {ex.Message}");
                }
#pragma warning restore CA1031
            }
            else
            {
                _logger.LogWarning("No custom tweak action registered for id {ActionId}", tweak.CustomActionId);
                failures.Add($"missing custom action '{tweak.CustomActionId}'");
            }
        }

        if (failures.Count > 0)
        {
            var message = string.Join("; ", failures);
            progress?.Report(TaskProgress.Failed(message));
            return OperationResult.Fail($"{direction} '{tweak.Id}' completed with errors: {message}");
        }

        progress?.Report(TaskProgress.Completed());
        return OperationResult.Ok($"{direction} '{tweak.Id}' completed");
    }

    private TweakActionContext BuildContext(IProgress<TaskProgress>? progress, CancellationToken cancellationToken) => new()
    {
        Registry = _registry,
        Services = _services,
        Process = _serviceProvider.GetRequiredService<IProcessRunner>(),
        Appx = _serviceProvider.GetRequiredService<IAppxManager>(),
        Explorer = _serviceProvider.GetRequiredService<IExplorerRefresher>(),
        ServiceProvider = _serviceProvider,
        Logger = _logger,
        Progress = progress,
        CancellationToken = cancellationToken,
    };

    private static RegistryValueKind MapRegistryKind(string type) => type.Trim().ToUpperInvariant() switch
    {
        "DWORD" or "REG_DWORD" => RegistryValueKind.DWord,
        "QWORD" or "REG_QWORD" => RegistryValueKind.QWord,
        "STRING" or "REG_SZ" => RegistryValueKind.String,
        "EXPANDSTRING" or "EXPAND_SZ" or "REG_EXPAND_SZ" => RegistryValueKind.ExpandString,
        "MULTISTRING" or "MULTI_SZ" or "REG_MULTI_SZ" => RegistryValueKind.MultiString,
        "BINARY" or "REG_BINARY" => RegistryValueKind.Binary,
        _ => RegistryValueKind.String,
    };

    private static bool TryMapStartupType(string type, out ServiceStartupType startup)
    {
        switch (type.Trim().ToUpperInvariant())
        {
            case "DISABLED":
            case "DISABLE": // PowerShell bug: WPFTweaksLocation shipped "Disable" — treat as Disabled.
                startup = ServiceStartupType.Disabled;
                return true;
            case "MANUAL":
                startup = ServiceStartupType.Manual;
                return true;
            case "AUTOMATIC":
                startup = ServiceStartupType.Automatic;
                return true;
            case "AUTOMATICDELAYED":
            case "AUTOMATICDELAYEDSTART":
            case "DELAYEDAUTO":
                startup = ServiceStartupType.AutomaticDelayed;
                return true;
            case "BOOT":
                startup = ServiceStartupType.Boot;
                return true;
            case "SYSTEM":
                startup = ServiceStartupType.System;
                return true;
            default:
                startup = ServiceStartupType.Manual;
                return false;
        }
    }

    /// <summary>
    /// Loose value comparison mirroring PowerShell's <c>-ne</c> coercion: numeric values (as written by
    /// DWord/QWord keys) compare numerically; everything else compares as trimmed, case-insensitive text.
    /// </summary>
    private static bool ValuesEqual(string? a, string? b)
    {
        if (a is null && b is null)
        {
            return true;
        }

        if (a is null || b is null)
        {
            return false;
        }

        if (TryParseInteger(a, out var la) && TryParseInteger(b, out var lb))
        {
            return la == lb;
        }

        return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseInteger(string text, out long value)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return long.TryParse(trimmed.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        }

        return long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}
