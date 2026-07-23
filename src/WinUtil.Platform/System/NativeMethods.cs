using System.Runtime.InteropServices;

namespace WinUtil.Platform.System;

/// <summary>
/// Native Win32 P/Invoke declarations for the platform system services.
/// DllImport is used deliberately: the source-generated <c>LibraryImport</c> emits
/// <c>unsafe</c> marshalling stubs, and this assembly's project does not enable
/// <c>AllowUnsafeBlocks</c> (and the .csproj is out of scope to change).
/// </summary>
internal static class NativeMethods
{
    // ---- Service Control Manager access rights (advapi32) ----
    internal const uint SC_MANAGER_CONNECT = 0x0001;
    internal const uint SERVICE_QUERY_CONFIG = 0x0001;
    internal const uint SERVICE_CHANGE_CONFIG = 0x0002;

    /// <summary>Sentinel meaning "leave this field unchanged" for ChangeServiceConfig.</summary>
    internal const uint SERVICE_NO_CHANGE = 0xFFFFFFFF;

    // ---- dwStartType values ----
    internal const uint SERVICE_BOOT_START = 0x00000000;
    internal const uint SERVICE_SYSTEM_START = 0x00000001;
    internal const uint SERVICE_AUTO_START = 0x00000002;
    internal const uint SERVICE_DEMAND_START = 0x00000003;
    internal const uint SERVICE_DISABLED = 0x00000004;

    /// <summary>dwInfoLevel selecting the delayed-auto-start config for ChangeServiceConfig2.</summary>
    internal const uint SERVICE_CONFIG_DELAYED_AUTO_START_INFO = 3;

    /// <summary>Win32 error returned by OpenService when the named service does not exist.</summary>
    internal const int ERROR_SERVICE_DOES_NOT_EXIST = 1060;

    [StructLayout(LayoutKind.Sequential)]
    internal struct ServiceDelayedAutoStartInfo
    {
        /// <summary>Win32 BOOL (0/1), marshaled as a 4-byte int.</summary>
        public int fDelayedAutostart;
    }

    [DllImport("advapi32.dll", EntryPoint = "OpenSCManagerW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr OpenSCManager(string? machineName, string? databaseName, uint desiredAccess);

    [DllImport("advapi32.dll", EntryPoint = "OpenServiceW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr OpenService(IntPtr hSCManager, string serviceName, uint desiredAccess);

    [DllImport("advapi32.dll", EntryPoint = "ChangeServiceConfigW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ChangeServiceConfig(
        IntPtr hService,
        uint serviceType,
        uint startType,
        uint errorControl,
        string? binaryPathName,
        string? loadOrderGroup,
        IntPtr tagId,
        string? dependencies,
        string? serviceStartName,
        string? password,
        string? displayName);

    [DllImport("advapi32.dll", EntryPoint = "ChangeServiceConfig2W", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ChangeServiceConfig2(
        IntPtr hService,
        uint infoLevel,
        ref ServiceDelayedAutoStartInfo info);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseServiceHandle(IntPtr hScObject);

    // ---- Shell settings broadcast (user32) ----
    internal static readonly IntPtr HWND_BROADCAST = new(0xFFFF);
    internal const uint WM_SETTINGCHANGE = 0x001A;
    internal const uint SMTO_ABORTIFHUNG = 0x0002;

    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", CharSet = CharSet.Unicode)]
    internal static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        uint msg,
        IntPtr wParam,
        string? lParam,
        uint flags,
        uint timeout,
        out IntPtr result);
}
