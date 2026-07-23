namespace WinUtil.Core.Abstractions;

/// <summary>A physical USB disk enumerated from Storage WMI (MSFT_Disk, BusType == USB).</summary>
public sealed record UsbDisk(int DiskNumber, string FriendlyName, long SizeBytes, string PartitionStyle);

/// <summary>
/// Explicit acknowledgement that writing will PERMANENTLY ERASE a specific physical disk.
/// The caller must mint a token for the exact disk number it intends to wipe;
/// <see cref="IUsbWriter.WriteAsync"/> rejects any token whose <see cref="DiskNumber"/> does not match its
/// <c>diskId</c> argument. This makes the destructive wipe impossible to trigger by accident or by a
/// stale/mismatched selection.
/// </summary>
public readonly record struct UsbWipeConfirmation
{
    public int DiskNumber { get; private init; }

    /// <summary>True only for tokens created via <see cref="Confirm"/> (guards against <c>default</c>).</summary>
    public bool IsConfirmed { get; private init; }

    private UsbWipeConfirmation(int diskNumber)
    {
        DiskNumber = diskNumber;
        IsConfirmed = true;
    }

    /// <summary>Creates a confirmation authorizing the wipe of exactly <paramref name="diskNumber"/>.</summary>
    public static UsbWipeConfirmation Confirm(int diskNumber) => new(diskNumber);
}

/// <summary>
/// Enumerates USB disks and writes a rebuilt Windows install tree to one (port of the PowerShell
/// Invoke-WinUtilISO*USB functions). Writing is destructive and gated behind an explicit
/// <see cref="UsbWipeConfirmation"/>.
/// </summary>
public interface IUsbWriter
{
    /// <summary>Lists candidate USB disks (BusType == USB). See the safety note in the implementation.</summary>
    Task<OperationResult<IReadOnlyList<UsbDisk>>> ListUsbDisksAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Wipes <paramref name="diskId"/>, lays down a GPT + FAT32 layout, splits install.wim if it exceeds the
    /// FAT32 file-size limit, and copies <paramref name="contentPath"/> onto the drive.
    /// </summary>
    /// <param name="confirmation">
    /// Must have been created via <see cref="UsbWipeConfirmation.Confirm"/> for the same <paramref name="diskId"/>;
    /// otherwise the call fails without touching the disk.
    /// </param>
    Task<OperationResult> WriteAsync(
        int diskId,
        string contentPath,
        UsbWipeConfirmation confirmation,
        IProgress<TaskProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
