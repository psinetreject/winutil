using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using WinUtil.Core.Abstractions;

namespace WinUtil.Platform.System;

/// <summary>
/// Changes Windows service startup types via the Service Control Manager
/// (<c>advapi32!ChangeServiceConfig</c>), reading the current state from the registry.
/// Idempotent — a service already in the requested state is left untouched. A missing
/// service is a non-fatal <see cref="OperationResult.Fail(string)"/>; nothing throws.
/// </summary>
public sealed class ServiceManager : IServiceManager
{
    private const string ServicesRoot = @"SYSTEM\CurrentControlSet\Services\";

    private readonly ILogger<ServiceManager> _logger;

    public ServiceManager(ILogger<ServiceManager> logger) => _logger = logger;

    public OperationResult SetStartupType(string serviceName, ServiceStartupType startupType)
    {
        try
        {
            if (!ServiceExists(serviceName))
            {
                _logger.LogWarning("Service {Service} was not found.", serviceName);
                return OperationResult.Fail($"Service '{serviceName}' was not found.");
            }

            if (GetStartupType(serviceName) is { } current && current == startupType)
            {
                return OperationResult.Ok($"Service '{serviceName}' is already {startupType}.");
            }

            return ApplyStartupType(serviceName, startupType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set startup type for service {Service}", serviceName);
            return OperationResult.Fail($"Failed to configure service '{serviceName}': {ex.Message}");
        }
    }

    public ServiceStartupType? GetStartupType(string serviceName)
    {
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(ServicesRoot + serviceName, writable: false);
            if (key is null || key.GetValue("Start") is not int start)
            {
                return null;
            }

            bool delayed = key.GetValue("DelayedAutostart") is int d && d != 0;
            return MapStartType(start, delayed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read startup type for service {Service}", serviceName);
            return null;
        }
    }

    private static bool ServiceExists(string serviceName)
    {
        using RegistryKey? key = Registry.LocalMachine.OpenSubKey(ServicesRoot + serviceName, writable: false);
        return key is not null;
    }

    private static ServiceStartupType? MapStartType(int start, bool delayed) => start switch
    {
        0 => ServiceStartupType.Boot,
        1 => ServiceStartupType.System,
        2 => delayed ? ServiceStartupType.AutomaticDelayed : ServiceStartupType.Automatic,
        3 => ServiceStartupType.Manual,
        4 => ServiceStartupType.Disabled,
        _ => null,
    };

    private OperationResult ApplyStartupType(string serviceName, ServiceStartupType startupType)
    {
        uint startType = startupType switch
        {
            ServiceStartupType.Boot => NativeMethods.SERVICE_BOOT_START,
            ServiceStartupType.System => NativeMethods.SERVICE_SYSTEM_START,
            ServiceStartupType.Automatic => NativeMethods.SERVICE_AUTO_START,
            ServiceStartupType.AutomaticDelayed => NativeMethods.SERVICE_AUTO_START,
            ServiceStartupType.Manual => NativeMethods.SERVICE_DEMAND_START,
            ServiceStartupType.Disabled => NativeMethods.SERVICE_DISABLED,
            _ => NativeMethods.SERVICE_NO_CHANGE,
        };

        IntPtr manager = IntPtr.Zero;
        IntPtr service = IntPtr.Zero;
        try
        {
            manager = NativeMethods.OpenSCManager(null, null, NativeMethods.SC_MANAGER_CONNECT);
            if (manager == IntPtr.Zero)
            {
                return Win32Fail("OpenSCManager", serviceName);
            }

            service = NativeMethods.OpenService(
                manager,
                serviceName,
                NativeMethods.SERVICE_CHANGE_CONFIG | NativeMethods.SERVICE_QUERY_CONFIG);
            if (service == IntPtr.Zero)
            {
                int err = Marshal.GetLastPInvokeError();
                if (err == NativeMethods.ERROR_SERVICE_DOES_NOT_EXIST)
                {
                    _logger.LogWarning("Service {Service} was not found.", serviceName);
                    return OperationResult.Fail($"Service '{serviceName}' was not found.");
                }

                return Win32Fail("OpenService", serviceName, err);
            }

            bool changed = NativeMethods.ChangeServiceConfig(
                service,
                NativeMethods.SERVICE_NO_CHANGE,
                startType,
                NativeMethods.SERVICE_NO_CHANGE,
                null, null, IntPtr.Zero, null, null, null, null);
            if (!changed)
            {
                return Win32Fail("ChangeServiceConfig", serviceName);
            }

            // Set/clear the delayed-auto-start flag for the two Automatic variants so that
            // switching between them behaves correctly. It is ignored for other start types.
            if (startupType is ServiceStartupType.Automatic or ServiceStartupType.AutomaticDelayed)
            {
                var info = new NativeMethods.ServiceDelayedAutoStartInfo
                {
                    fDelayedAutostart = startupType == ServiceStartupType.AutomaticDelayed ? 1 : 0,
                };

                bool delayedSet = NativeMethods.ChangeServiceConfig2(
                    service,
                    NativeMethods.SERVICE_CONFIG_DELAYED_AUTO_START_INFO,
                    ref info);
                if (!delayedSet)
                {
                    return Win32Fail("ChangeServiceConfig2", serviceName);
                }
            }

            _logger.LogInformation("Service {Service} startup type set to {StartupType}.", serviceName, startupType);
            return OperationResult.Ok($"Service '{serviceName}' set to {startupType}.");
        }
        finally
        {
            if (service != IntPtr.Zero)
            {
                NativeMethods.CloseServiceHandle(service);
            }

            if (manager != IntPtr.Zero)
            {
                NativeMethods.CloseServiceHandle(manager);
            }
        }
    }

    private OperationResult Win32Fail(string api, string serviceName, int? error = null)
    {
        int code = error ?? Marshal.GetLastPInvokeError();
        string codeText = code.ToString(CultureInfo.InvariantCulture);
        _logger.LogError("{Api} failed for service {Service} (Win32 error {Error}).", api, serviceName, code);
        return OperationResult.Fail($"{api} failed for service '{serviceName}' (Win32 error {codeText}).");
    }
}
