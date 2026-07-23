using System.Globalization;
using System.Management;
using System.Text;

using Microsoft.Extensions.Logging;

using WinUtil.Core.Abstractions;

namespace WinUtil.Platform.Iso;

// Result-over-exceptions boundary: the public operations convert failures into OperationResult.Fail.
#pragma warning disable CA1031

// WMI enumeration hands back live ManagementObjects that the loop disposes explicitly.
#pragma warning disable CA2000

/// <summary>
/// Port of Invoke-WinUtilISOUSB.ps1: enumerate USB disks (Storage WMI) and write a rebuilt Windows install
/// tree to one. The destructive layout is gated behind an explicit <see cref="UsbWipeConfirmation"/>.
///
/// Managed vs shelled (documented per the task):
///  - Disk enumeration and the target-disk safety re-check → Storage WMI (MSFT_Disk, BusType == USB).
///  - clean / convert GPT / create+format FAT32 partition / assign letter → diskpart via IProcessRunner.
///    A single diskpart script keeps partition focus between steps, which avoids the "no volume selected"
///    failure the PS multi-phase version worked around, and is the documented alternative the task allows.
///  - install.wim &gt; FAT32 limit split → dism /Split-Image; file copy → robocopy. Both via IProcessRunner.
/// </summary>
public sealed class UsbWriter : IUsbWriter
{
    private const string StorageScopePath = @"\\.\ROOT\Microsoft\Windows\Storage";
    private const ushort BusTypeUsb = 7;

    // FAT32 partition cap (32 GB) and the SWM split threshold (below the 4 GB FAT32 per-file limit).
    private const int MaxFat32PartitionMb = 32768;
    private const int WimSplitThresholdMb = 3800;

    // Bounded wait for the freshly assigned drive letter to become browsable (PS retried 6 times).
    private const int DriveReadyMaxAttempts = 6;
    private const int DriveReadyDelayMs = 2000;

    private const int RobocopyFailureThreshold = 8;

    private readonly IProcessRunner _process;
    private readonly ILogger<UsbWriter> _logger;

    public UsbWriter(IProcessRunner process, ILogger<UsbWriter> logger)
    {
        _process = process;
        _logger = logger;
    }

    public async Task<OperationResult<IReadOnlyList<UsbDisk>>> ListUsbDisksAsync(CancellationToken cancellationToken = default)
    {
        return await Task.Run(ListUsbDisksCore, cancellationToken).ConfigureAwait(false);
    }

    private static OperationResult<IReadOnlyList<UsbDisk>> ListUsbDisksCore()
    {
        // SAFETY: BusType == USB matches ANY externally-connected USB storage, including large external USB
        // hard drives and dock-attached SSDs — not just flash sticks. The list is presented for the user to
        // choose from, and WriteAsync additionally requires a per-disk confirmation token plus a second USB
        // bus-type check before it will wipe anything, so a mis-click on a valuable external drive still cannot
        // proceed without an explicit, disk-number-matched acknowledgement.
        try
        {
            ManagementScope scope = new(StorageScopePath);
            scope.Connect();

            using ManagementObjectSearcher searcher = new(
                scope,
                new ObjectQuery($"SELECT * FROM MSFT_Disk WHERE BusType = {BusTypeUsb.ToString(CultureInfo.InvariantCulture)}"));
            using ManagementObjectCollection results = searcher.Get();

            List<UsbDisk> disks = [];
            foreach (ManagementBaseObject item in results)
            {
                using (item)
                {
                    int number = (int)ReadUInt32(item["Number"]);
                    string friendlyName = ReadString(item["FriendlyName"]);
                    long size = (long)ReadUInt64(item["Size"]);
                    string style = PartitionStyleName(ReadUInt16(item["PartitionStyle"]));
                    disks.Add(new UsbDisk(number, friendlyName, size, style));
                }
            }

            disks.Sort((a, b) => a.DiskNumber.CompareTo(b.DiskNumber));
            return OperationResult<IReadOnlyList<UsbDisk>>.Ok(disks);
        }
        catch (Exception ex)
        {
            return OperationResult<IReadOnlyList<UsbDisk>>.Fail($"Could not enumerate USB disks: {ex.Message}");
        }
    }

    public async Task<OperationResult> WriteAsync(
        int diskId,
        string contentPath,
        UsbWipeConfirmation confirmation,
        IProgress<TaskProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentPath);

        // The destructive-wipe gate: the token must have been minted for exactly this disk.
        if (!confirmation.IsConfirmed || confirmation.DiskNumber != diskId)
        {
            return OperationResult.Fail(
                "USB write rejected: a confirmation token for this exact disk number is required before erasing.");
        }

        if (!Directory.Exists(contentPath))
        {
            return OperationResult.Fail($"Modified ISO content not found: {contentPath}");
        }

        // SAFETY: re-verify the target is still a USB disk immediately before wiping, so a stale selection
        // (e.g. the disk was removed/renumbered) cannot redirect the wipe onto an internal disk.
        long diskSizeBytes;
        try
        {
            diskSizeBytes = GetUsbDiskSizeOrThrow(diskId);
        }
        catch (Exception ex)
        {
            return OperationResult.Fail($"Refusing to write: {ex.Message}");
        }

        try
        {
            char? drive = FindFreeDriveLetter();
            if (drive is null)
            {
                return OperationResult.Fail("No free drive letters (D-Z) available to assign to the USB partition.");
            }

            char letter = drive.Value;
            string volumeLabel = "W11-" + DateTime.Now.ToString("yyMMdd", CultureInfo.InvariantCulture);

            Report(progress, 10, $"Partitioning and formatting Disk {diskId.ToString(CultureInfo.InvariantCulture)}...");
            OperationResult layout = await PartitionAndFormatAsync(diskId, diskSizeBytes, letter, volumeLabel, cancellationToken).ConfigureAwait(false);
            if (!layout.Success)
            {
                return layout;
            }

            string usbDrive = letter + ":\\";
            Report(progress, 30, $"Waiting for {usbDrive} to become accessible...");
            if (!await WaitForDriveAsync(usbDrive, cancellationToken).ConfigureAwait(false))
            {
                return OperationResult.Fail($"Drive {usbDrive} is not accessible after letter assignment.");
            }

            OperationResult capacity = CheckCapacity(contentPath, letter);
            if (!capacity.Success)
            {
                return capacity;
            }

            Report(progress, 45, "Copying Windows 11 files to USB...");
            OperationResult copy = await CopyContentAsync(contentPath, usbDrive, cancellationToken).ConfigureAwait(false);
            if (!copy.Success)
            {
                return copy;
            }

            Report(progress, 100, "USB write complete. The drive is ready to boot.");
            return OperationResult.Ok("USB drive created successfully.");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail($"USB write failed: {ex.Message}");
        }
    }

    // -- Partition + format (diskpart) ---------------------------------------------------------------------

    private async Task<OperationResult> PartitionAndFormatAsync(
        int diskId,
        long diskSizeBytes,
        char letter,
        string volumeLabel,
        CancellationToken cancellationToken)
    {
        long diskSizeMb = diskSizeBytes / (1024L * 1024L);
        string createPartition = diskSizeMb > MaxFat32PartitionMb
            ? $"create partition primary size={MaxFat32PartitionMb.ToString(CultureInfo.InvariantCulture)}"
            : "create partition primary";

        if (diskSizeMb > MaxFat32PartitionMb)
        {
            Log($"Disk is {diskSizeMb.ToString(CultureInfo.InvariantCulture)} MB; capping the FAT32 partition at {MaxFat32PartitionMb.ToString(CultureInfo.InvariantCulture)} MB (32 GB).");
        }

        string script = string.Join("\r\n",
            $"select disk {diskId.ToString(CultureInfo.InvariantCulture)}",
            "clean",
            "convert gpt",
            createPartition,
            $"format fs=fat32 quick label={volumeLabel}",
            $"assign letter={letter}",
            "exit");

        ProcessResult result = await RunDiskpartAsync(script, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            return OperationResult.Fail($"diskpart failed while partitioning Disk {diskId.ToString(CultureInfo.InvariantCulture)} (exit {result.ExitCode.ToString(CultureInfo.InvariantCulture)}).");
        }

        Log($"Disk {diskId.ToString(CultureInfo.InvariantCulture)} partitioned GPT and formatted FAT32 (label {volumeLabel}, letter {letter}).");
        return OperationResult.Ok();
    }

    private async Task<ProcessResult> RunDiskpartAsync(string script, CancellationToken cancellationToken)
    {
        string scriptFile = Path.Combine(Path.GetTempPath(), "winutil_diskpart_" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".txt");
        await File.WriteAllTextAsync(scriptFile, script, new ASCIIEncoding(), cancellationToken).ConfigureAwait(false);

        try
        {
            return await RunAsync("diskpart.exe", ["/s", scriptFile], cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                File.Delete(scriptFile);
            }
            catch (Exception ex)
            {
                Log($"Warning: could not delete diskpart script {scriptFile}: {ex.Message}");
            }
        }
    }

    // -- Copy (split install.wim if needed) ----------------------------------------------------------------

    private async Task<OperationResult> CopyContentAsync(string contentPath, string usbDrive, CancellationToken cancellationToken)
    {
        string installWim = Path.Combine(contentPath, "sources", "install.wim");
        bool split = false;

        if (File.Exists(installWim))
        {
            long wimSizeMb = new FileInfo(installWim).Length / (1024L * 1024L);
            if (wimSizeMb > WimSplitThresholdMb)
            {
                Log($"install.wim is {wimSizeMb.ToString(CultureInfo.InvariantCulture)} MB - splitting for FAT32 compatibility (this can take several minutes)...");
                string splitDir = Path.Combine(usbDrive, "sources");
                Directory.CreateDirectory(splitDir);
                string swm = Path.Combine(splitDir, "install.swm");

                ProcessResult splitResult = await RunAsync(
                    "dism.exe",
                    ["/English", "/Split-Image", $"/ImageFile:{installWim}", $"/SWMFile:{swm}", $"/FileSize:{WimSplitThresholdMb.ToString(CultureInfo.InvariantCulture)}", "/CheckIntegrity"],
                    cancellationToken).ConfigureAwait(false);
                if (!splitResult.Succeeded)
                {
                    return OperationResult.Fail($"dism /Split-Image failed (exit {splitResult.ExitCode.ToString(CultureInfo.InvariantCulture)}).");
                }

                split = true;
                Log("install.wim split complete.");
            }
        }

        List<string> args = split
            ? [contentPath, usbDrive, "/E", "/XF", "install.wim", "/NFL", "/NDL", "/NJH", "/NJS"]
            : [contentPath, usbDrive, "/E", "/NFL", "/NDL", "/NJH", "/NJS"];

        ProcessResult copy = await RunAsync("robocopy.exe", args, cancellationToken).ConfigureAwait(false);
        if (copy.ExitCode >= RobocopyFailureThreshold)
        {
            return OperationResult.Fail($"robocopy failed copying files to USB (exit {copy.ExitCode.ToString(CultureInfo.InvariantCulture)}).");
        }

        Log("Files copied to USB.");
        return OperationResult.Ok();
    }

    private OperationResult CheckCapacity(string contentPath, char letter)
    {
        long contentBytes = new DirectoryInfo(contentPath)
            .EnumerateFiles("*", SearchOption.AllDirectories)
            .Sum(f => f.Length);

        DriveInfo drive = new(letter + ":\\");
        long capacity = drive.TotalSize;
        long free = drive.AvailableFreeSpace;

        Log($"Source content: {ToGb(contentBytes)} GB. USB partition capacity: {ToGb(capacity)} GB, free: {ToGb(free)} GB.");

        if (contentBytes > capacity)
        {
            return OperationResult.Fail($"ISO content ({ToGb(contentBytes)} GB) is larger than the USB partition capacity ({ToGb(capacity)} GB). Use a larger USB drive.");
        }

        if (contentBytes > free)
        {
            return OperationResult.Fail($"Insufficient free space on the USB partition. Required: {ToGb(contentBytes)} GB, available: {ToGb(free)} GB.");
        }

        return OperationResult.Ok();
    }

    // -- Storage WMI helpers -------------------------------------------------------------------------------

    private static long GetUsbDiskSizeOrThrow(int diskId)
    {
        ManagementScope scope = new(StorageScopePath);
        scope.Connect();

        using ManagementObjectSearcher searcher = new(
            scope,
            new ObjectQuery($"SELECT * FROM MSFT_Disk WHERE Number = {diskId.ToString(CultureInfo.InvariantCulture)}"));
        using ManagementObjectCollection results = searcher.Get();

        foreach (ManagementBaseObject item in results)
        {
            using (item)
            {
                ushort busType = ReadUInt16(item["BusType"]);
                if (busType != BusTypeUsb)
                {
                    throw new InvalidOperationException($"Disk {diskId.ToString(CultureInfo.InvariantCulture)} is not a USB disk (BusType {busType.ToString(CultureInfo.InvariantCulture)}).");
                }

                return (long)ReadUInt64(item["Size"]);
            }
        }

        throw new InvalidOperationException($"Disk {diskId.ToString(CultureInfo.InvariantCulture)} was not found.");
    }

    private static string PartitionStyleName(ushort style) => style switch
    {
        1 => "MBR",
        2 => "GPT",
        _ => "RAW",
    };

    private static uint ReadUInt32(object? value) => value is null ? 0u : Convert.ToUInt32(value, CultureInfo.InvariantCulture);

    private static ulong ReadUInt64(object? value) => value is null ? 0ul : Convert.ToUInt64(value, CultureInfo.InvariantCulture);

    private static ushort ReadUInt16(object? value) => value is null ? (ushort)0 : Convert.ToUInt16(value, CultureInfo.InvariantCulture);

    private static string ReadString(object? value) => value as string ?? string.Empty;

    // -- Misc helpers --------------------------------------------------------------------------------------

    private static char? FindFreeDriveLetter()
    {
        HashSet<char> used = DriveInfo.GetDrives()
            .Select(d => char.ToUpperInvariant(d.Name[0]))
            .ToHashSet();

        for (char c = 'D'; c <= 'Z'; c++)
        {
            if (!used.Contains(c))
            {
                return c;
            }
        }

        return null;
    }

    private static async Task<bool> WaitForDriveAsync(string usbDrive, CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < DriveReadyMaxAttempts; attempt++)
        {
            if (Directory.Exists(usbDrive))
            {
                return true;
            }

            await Task.Delay(DriveReadyDelayMs, cancellationToken).ConfigureAwait(false);
        }

        return Directory.Exists(usbDrive);
    }

    private static string ToGb(long bytes) =>
        (bytes / (1024.0 * 1024.0 * 1024.0)).ToString("0.00", CultureInfo.InvariantCulture);

    private async Task<ProcessResult> RunAsync(string tool, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        ProcessResult result = await _process.RunAsync(tool, arguments, cancellationToken).ConfigureAwait(false);

        foreach (string line in SplitLines(result.StandardOutput))
        {
            Log(line);
        }

        foreach (string line in SplitLines(result.StandardError))
        {
            Log("[stderr] " + line);
        }

        return result;
    }

    private static IEnumerable<string> SplitLines(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            yield break;
        }

        foreach (string raw in text.Split('\n'))
        {
            string line = raw.TrimEnd('\r').Trim();
            if (line.Length > 0)
            {
                yield return line;
            }
        }
    }

    private void Log(string message) => _logger.LogInformation("{IsoLog}", message);

    private void Report(IProgress<TaskProgress>? progress, int percent, string message)
    {
        Log(message);
        progress?.Report(new TaskProgress(percent, message));
    }
}
