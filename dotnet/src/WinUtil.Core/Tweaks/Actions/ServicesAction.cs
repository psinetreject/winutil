using System.Runtime.InteropServices;
using WinUtil.Core.Abstractions;

namespace WinUtil.Core.Tweaks.Actions;

/// <summary>
/// Raises the svchost service-grouping threshold to the machine's total physical RAM (in KB) so Windows
/// stops splitting services into separate svchost processes. Moderate (NOUNDO).
/// </summary>
public sealed class ServicesAction : ICustomTweakAction
{
    public string Id => "WPFTweaksServices";
    public bool SupportsUndo => false;

    public Task<OperationResult> ApplyAsync(TweakActionContext context)
    {
        // PARITY: PS sums Win32_PhysicalMemory.Capacity (installed module capacity) / 1KB. WMI is not
        // available in the platform-agnostic Core, so GlobalMemoryStatusEx.ullTotalPhys is used — this
        // is OS-usable RAM, which can read a few MB lower than the installed total on some machines.
        if (!TryGetTotalPhysicalMemoryKb(out var memoryKb))
        {
            return Task.FromResult(OperationResult.Fail("Unable to query total physical memory"));
        }

        var result = context.Registry.SetValue(
            "HKLM:\\SYSTEM\\CurrentControlSet\\Control",
            "SvcHostSplitThresholdInKB",
            memoryKb.ToString(System.Globalization.CultureInfo.InvariantCulture),
            RegistryValueKind.DWord);

        return Task.FromResult(result.Success
            ? OperationResult.Ok($"svchost split threshold set to {memoryKb} KB")
            : result);
    }

    public Task<OperationResult> UndoAsync(TweakActionContext context)
        => Task.FromResult(OperationResult.Ok("svchost threshold tweak has no undo"));

    private static bool TryGetTotalPhysicalMemoryKb(out long kilobytes)
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (GlobalMemoryStatusEx(ref status))
        {
            kilobytes = (long)(status.TotalPhys / 1024UL);
            return true;
        }

        kilobytes = 0;
        return false;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);
}
