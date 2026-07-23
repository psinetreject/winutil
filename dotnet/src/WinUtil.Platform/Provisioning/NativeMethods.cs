using System.Runtime.InteropServices;

namespace WinUtil.Platform.Provisioning;

// DllImport is used deliberately over LibraryImport: the srclient structs below rely on
// ByValTStr marshalling, which the LibraryImport source generator does not support.
#pragma warning disable SYSLIB1054

/// <summary>
/// Shared P/Invoke surface for the provisioning services (power plans, System Restore).
/// Internal by design; the declaring DLLs (powrprof, srclient, kernel32) all live in System32.
/// </summary>
internal static class NativeMethods
{
    /// <summary>Win32 ERROR_SUCCESS — the success code returned by the powrprof functions.</summary>
    internal const uint ErrorSuccess = 0;

    // ---- System Restore (srclient.dll) event/type constants ----

    /// <summary>BEGIN_SYSTEM_CHANGE — start of a system change bracket.</summary>
    internal const int BeginSystemChange = 100;

    /// <summary>END_SYSTEM_CHANGE — end of a system change bracket.</summary>
    internal const int EndSystemChange = 101;

    /// <summary>MODIFY_SETTINGS — restore-point type used for manual checkpoints.</summary>
    internal const int RestorePointModifySettings = 12;

    // ---- powrprof.dll ----

    /// <summary>
    /// Duplicates an existing power scheme. <paramref name="destinationSchemeGuid"/> receives a
    /// pointer to a freshly-allocated GUID that must be released with <see cref="LocalFree"/>.
    /// </summary>
    [DllImport("powrprof.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern uint PowerDuplicateScheme(
        IntPtr rootPowerKey,
        ref Guid sourceSchemeGuid,
        out IntPtr destinationSchemeGuid);

    /// <summary>Sets the active power scheme for the current user.</summary>
    [DllImport("powrprof.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern uint PowerSetActiveScheme(IntPtr userRootPowerKey, ref Guid schemeGuid);

    // ---- kernel32.dll ----

    /// <summary>Frees memory allocated by <see cref="PowerDuplicateScheme"/>.</summary>
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern IntPtr LocalFree(IntPtr hMem);

    // ---- srclient.dll ----

    /// <summary>Creates or brackets a System Restore point.</summary>
    [DllImport("srclient.dll", CharSet = CharSet.Unicode, SetLastError = false)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SRSetRestorePoint(
        ref RESTOREPOINTINFO restorePtSpec,
        out STATEMGRSTATUS smgrStatus);

    /// <summary>Mirrors the native RESTOREPOINTINFOW structure.</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct RESTOREPOINTINFO
    {
        public int dwEventType;
        public int dwRestorePtType;
        public long llSequenceNumber;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szDescription;
    }

    /// <summary>Mirrors the native STATEMGRSTATUS structure.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct STATEMGRSTATUS
    {
        public int nStatus;
        public long llSequenceNumber;
    }
}

#pragma warning restore SYSLIB1054
