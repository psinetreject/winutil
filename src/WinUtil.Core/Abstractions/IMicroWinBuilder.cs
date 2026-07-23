namespace WinUtil.Core.Abstractions;

/// <summary>A selectable Windows edition inside an install.wim/esd (image index + display name).</summary>
public sealed record MicroWinEdition(int ImageIndex, string Name);

/// <summary>
/// Result of mounting a Windows ISO: the read-only mount drive, the active install image file
/// (install.wim or install.esd on that drive), and every edition discovered inside it.
/// </summary>
public sealed record MicroWinMountResult(
    string DriveLetter,
    string WimPath,
    IReadOnlyList<MicroWinEdition> Editions);

/// <summary>Options that drive the offline rebuild of the mounted image.</summary>
/// <param name="SelectedImageIndex">1-based WIM image index of the edition to keep (mount + export source).</param>
/// <param name="SelectedEditionName">Display name of that edition (used to derive the ei.cfg EditionID fallback).</param>
/// <param name="InjectDrivers">When true, drivers exported from the running host are injected into install.wim and boot.wim.</param>
public sealed record MicroWinModifyOptions(
    int SelectedImageIndex,
    string SelectedEditionName,
    bool InjectDrivers);

/// <summary>
/// The on-disk working set produced by <see cref="IMicroWinBuilder.ModifyAsync"/>: a rebuilt, single-edition
/// ISO tree ready to be exported to an ISO or written to a USB drive.
/// </summary>
public sealed record MicroWinWorkspace(string WorkDir, string IsoContentsDir);

/// <summary>
/// Drives the MicroWin ISO rebuild flow (port of the PowerShell Invoke-WinUtilISO* functions):
/// mount &amp; verify a Windows 11 ISO, rebuild a debloated single-edition image, then export it back to an ISO.
/// The builder is stateful across calls (mount → modify → export → cleanup) and is not thread-safe;
/// the UI is expected to drive one step at a time.
/// </summary>
public interface IMicroWinBuilder
{
    /// <summary>Mounts the ISO, verifies it holds a Windows 11 install image, and returns the drive + editions.</summary>
    Task<OperationResult<MicroWinMountResult>> MountIsoAsync(
        string isoPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Copies the ISO tree, mounts the selected edition, applies the offline WinUtil modifications,
    /// runs component-store cleanup, exports the single edition, and dismounts the source ISO.
    /// Requires a prior successful <see cref="MountIsoAsync"/>.
    /// </summary>
    Task<OperationResult<MicroWinWorkspace>> ModifyAsync(
        MicroWinModifyOptions options,
        IProgress<TaskProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>Rebuilds a bootable ISO from the modified workspace tree using oscdimg.</summary>
    Task<OperationResult> ExportToIsoAsync(
        string outputIsoPath,
        IProgress<TaskProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Best-effort teardown: dismounts any WIM still mounted under the workspace, dismounts the source ISO,
    /// and deletes the temporary working directory. Safe to call at any point.
    /// </summary>
    Task<OperationResult> CleanupAsync(CancellationToken cancellationToken = default);
}
