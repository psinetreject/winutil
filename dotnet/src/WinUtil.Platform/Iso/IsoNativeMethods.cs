using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;

namespace WinUtil.Platform.Iso;

// Classic DllImport is used deliberately: RegLoadKey/RegUnLoadKey return their status directly and the
// privilege APIs marshal structs, so the source-generated marshaller adds no value here.
#pragma warning disable SYSLIB1054

/// <summary>
/// P/Invoke surface for offline-hive servicing. <see cref="LoadHive"/>/<see cref="UnloadHive"/> load an offline
/// registry hive file under a subkey of HKLM (machine-wide, exactly like <c>reg load</c>) so that the offline
/// image's registry can be edited, and unload it again. Managing load/unload from C# is what lets
/// <see cref="MicroWinModifier"/> guarantee the hives are unloaded in a <c>finally</c> block — the PowerShell
/// original only unloaded them on the happy path and leaked them on any throw.
/// </summary>
internal static class IsoNativeMethods
{
    /// <summary>Predefined handle for HKEY_LOCAL_MACHINE.</summary>
    internal static readonly nint HkeyLocalMachine = unchecked((nint)0x80000002);

    private const int SePrivilegeEnabled = 0x00000002;
    private const int TokenAdjustPrivileges = 0x0020;
    private const int TokenQuery = 0x0008;
    private const string SeBackupName = "SeBackupPrivilege";
    private const string SeRestoreName = "SeRestorePrivilege";

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegLoadKeyW", SetLastError = false)]
    private static extern int RegLoadKey(nint hKey, string lpSubKey, string lpFile);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegUnLoadKeyW", SetLastError = false)]
    private static extern int RegUnLoadKey(nint hKey, string lpSubKey);

    /// <summary>
    /// Loads <paramref name="hiveFile"/> under <c>HKLM\<paramref name="subKey"/></c>. Throws on failure so the
    /// caller can decide whether to continue; the return value of RegLoadKey is the Win32 status code.
    /// </summary>
    internal static void LoadHive(string subKey, string hiveFile)
    {
        int status = RegLoadKey(HkeyLocalMachine, subKey, hiveFile);
        if (status != 0)
        {
            throw new Win32Exception(status, $"RegLoadKey(HKLM\\{subKey}, {hiveFile}) failed with status {status.ToString(CultureInfo.InvariantCulture)}.");
        }
    }

    /// <summary>Unloads a hive previously loaded with <see cref="LoadHive"/>.</summary>
    internal static void UnloadHive(string subKey)
    {
        int status = RegUnLoadKey(HkeyLocalMachine, subKey);
        if (status != 0)
        {
            throw new Win32Exception(status, $"RegUnLoadKey(HKLM\\{subKey}) failed with status {status.ToString(CultureInfo.InvariantCulture)}.");
        }
    }

    // --- Privilege plumbing ---------------------------------------------------------------------------------
    // RegLoadKey/RegUnLoadKey require SeBackupPrivilege + SeRestorePrivilege to be *enabled* on the process
    // token. Administrators hold them but they are disabled by default; reg.exe enables them implicitly, so
    // when we call the API directly we must do the same or the load fails with ERROR_PRIVILEGE_NOT_HELD (1314).

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LuidAndAttributes
    {
        public Luid Luid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenPrivileges
    {
        public uint PrivilegeCount;
        public LuidAndAttributes Privilege;
    }

    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint hObject);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(nint processHandle, int desiredAccess, out nint tokenHandle);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LookupPrivilegeValue(string? systemName, string name, out Luid luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AdjustTokenPrivileges(
        nint tokenHandle,
        [MarshalAs(UnmanagedType.Bool)] bool disableAllPrivileges,
        ref TokenPrivileges newState,
        int bufferLength,
        nint previousState,
        nint returnLength);

    /// <summary>
    /// Enables SeBackupPrivilege and SeRestorePrivilege on the current process token so that offline-hive
    /// load/unload can succeed. Idempotent; safe to call before every hive load.
    /// </summary>
    internal static void EnableHivePrivileges()
    {
        if (!OpenProcessToken(GetCurrentProcess(), TokenAdjustPrivileges | TokenQuery, out nint token))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenProcessToken failed.");
        }

        try
        {
            EnablePrivilege(token, SeBackupName);
            EnablePrivilege(token, SeRestoreName);
        }
        finally
        {
            CloseHandle(token);
        }
    }

    private static void EnablePrivilege(nint token, string privilegeName)
    {
        if (!LookupPrivilegeValue(null, privilegeName, out Luid luid))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"LookupPrivilegeValue({privilegeName}) failed.");
        }

        TokenPrivileges tp = new()
        {
            PrivilegeCount = 1,
            Privilege = new LuidAndAttributes { Luid = luid, Attributes = SePrivilegeEnabled },
        };

        if (!AdjustTokenPrivileges(token, false, ref tp, 0, nint.Zero, nint.Zero))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"AdjustTokenPrivileges({privilegeName}) failed.");
        }
    }
}
