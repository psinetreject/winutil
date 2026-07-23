using System.Globalization;
using System.Management;

using Microsoft.Dism;
using Microsoft.Extensions.Logging;

using WinUtil.Core.Abstractions;

namespace WinUtil.Platform.Iso;

// Result-over-exceptions boundary: the public operations convert every failure into OperationResult.Fail
// (mirroring the try/catch in the PS runspaces), and best-effort cleanup swallows secondary failures.
#pragma warning disable CA1031

// ManagementObject/ManagementObjectCollection have subtle ownership when returned from an enumeration or an
// association walk; the WMI helpers below intentionally hand back live objects for the caller to dispose.
#pragma warning disable CA2000

/// <summary>
/// Port of Invoke-WinUtilISO.ps1 (mount/verify, modify, export, clean). Stateful across the mount → modify →
/// export → cleanup lifecycle.
///
/// Managed vs shelled (documented per the task):
///  - ISO mount / drive-letter discovery / dismount → Storage WMI (MSFT_DiskImage.Mount / .Dismount and the
///    MSFT_DiskImage → MSFT_Volume association). No PowerShell is spawned.
///  - Image metadata (editions), WIM mount, WIM dismount-and-save, mounted-image enumeration → ManagedDism.
///  - robocopy (ISO copy), dism /Cleanup-Image /StartComponentCleanup /ResetBase, dism /Export-Image,
///    dism /Get-CurrentEdition, and oscdimg (ISO rebuild) → IProcessRunner. These have no managed API here.
/// </summary>
public sealed class MicroWinBuilder : IMicroWinBuilder
{
    private const string StorageScopePath = @"\\.\ROOT\Microsoft\Windows\Storage";

    // FIX: the PS drive-letter poll was an unbounded do/until; bound it (30 * 500 ms = 15 s).
    private const int MountPollMaxAttempts = 30;
    private const int MountPollDelayMs = 500;

    // robocopy uses a bitmask exit code: values < 8 are success (files copied / nothing to do / extras).
    private const int RobocopyFailureThreshold = 8;

    // The final single-edition install.wim always has image index 1, so the answer file targets index 1.
    private const int FinalImageIndex = 1;

    private readonly IProcessRunner _process;
    private readonly ILogger<MicroWinBuilder> _logger;

    private string? _isoPath;
    private MicroWinMountResult? _mount;
    private MicroWinWorkspace? _workspace;

    public MicroWinBuilder(IProcessRunner process, ILogger<MicroWinBuilder> logger)
    {
        _process = process;
        _logger = logger;
    }

    public async Task<OperationResult<MicroWinMountResult>> MountIsoAsync(string isoPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(isoPath);
        return await Task.Run(() => MountIsoCore(isoPath, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    private OperationResult<MicroWinMountResult> MountIsoCore(string isoPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(isoPath))
        {
            return OperationResult<MicroWinMountResult>.Fail($"ISO file not found: {isoPath}");
        }

        Log($"Mounting ISO: {isoPath}");

        string driveLetter;
        try
        {
            driveLetter = MountDiskImage(isoPath, cancellationToken);
        }
        catch (Exception ex)
        {
            return OperationResult<MicroWinMountResult>.Fail($"Failed to mount ISO: {ex.Message}");
        }

        Log($"Mounted at drive {driveLetter}");

        string wimPath = Path.Combine(driveLetter + Path.DirectorySeparatorChar, "sources", "install.wim");
        string esdPath = Path.Combine(driveLetter + Path.DirectorySeparatorChar, "sources", "install.esd");
        string activeWim = File.Exists(wimPath) ? wimPath : esdPath;

        if (!File.Exists(activeWim))
        {
            TryDismountDiskImage(isoPath);
            return OperationResult<MicroWinMountResult>.Fail(
                "This does not appear to be a valid Windows ISO: install.wim / install.esd was not found.");
        }

        List<MicroWinEdition> editions;
        try
        {
            DismApi.Initialize(DismLogLevel.LogErrors);
            try
            {
                DismImageInfoCollection infos = DismApi.GetImageInfo(activeWim);
                editions = infos.Select(i => new MicroWinEdition(i.ImageIndex, i.ImageName ?? string.Empty)).ToList();
            }
            finally
            {
                DismApi.Shutdown();
            }
        }
        catch (Exception ex)
        {
            TryDismountDiskImage(isoPath);
            return OperationResult<MicroWinMountResult>.Fail($"Could not read image metadata: {ex.Message}");
        }

        if (!editions.Any(e => e.Name.Contains("Windows 11", StringComparison.OrdinalIgnoreCase)))
        {
            TryDismountDiskImage(isoPath);
            return OperationResult<MicroWinMountResult>.Fail(
                "No Windows 11 edition was found in this ISO. Only official Windows 11 ISOs are supported.");
        }

        _isoPath = isoPath;
        _mount = new MicroWinMountResult(driveLetter, activeWim, editions);
        Log($"ISO verified OK. Editions found: {editions.Count.ToString(CultureInfo.InvariantCulture)}");

        return OperationResult<MicroWinMountResult>.Ok(_mount);
    }

    public async Task<OperationResult<MicroWinWorkspace>> ModifyAsync(
        MicroWinModifyOptions options,
        IProgress<TaskProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (_isoPath is null || _mount is null)
        {
            return OperationResult<MicroWinWorkspace>.Fail("No verified ISO. Call MountIsoAsync first.");
        }

        string isoPath = _isoPath;
        MicroWinMountResult mount = _mount;

        string workDir = CreateWorkDir();
        string isoContents = Path.Combine(workDir, "iso_contents");
        string mountDir = Path.Combine(workDir, "wim_mount");
        Directory.CreateDirectory(isoContents);
        Directory.CreateDirectory(mountDir);

        bool dismInitialized = false;
        bool wimMounted = false;

        try
        {
            Report(progress, 10, "Copying ISO contents...");
            // PARITY: the PS script passed the bare drive spec ("D:"), which robocopy resolves against the
            // per-drive current directory; the explicit root ("D:\") copies the whole tree unambiguously.
            string sourceRoot = mount.DriveLetter + Path.DirectorySeparatorChar;
            ProcessResult copy = await RunAsync("robocopy.exe", [sourceRoot, isoContents, "/E", "/NFL", "/NDL", "/NJH", "/NJS"], cancellationToken).ConfigureAwait(false);
            if (copy.ExitCode >= RobocopyFailureThreshold)
            {
                // Throw (rather than return) so the shared catch performs the full teardown below.
                throw new InvalidOperationException($"robocopy failed copying ISO contents (exit {copy.ExitCode.ToString(CultureInfo.InvariantCulture)}).");
            }

            string imageLeaf = Path.GetFileName(mount.WimPath);
            string localWim = Path.Combine(isoContents, "sources", imageLeaf);
            if (!File.Exists(localWim))
            {
                throw new InvalidOperationException($"Copied ISO image file not found: sources\\{imageLeaf}");
            }

            ClearReadOnly(localWim);

            Report(progress, 25, $"Mounting install image (index {options.SelectedImageIndex.ToString(CultureInfo.InvariantCulture)}: {options.SelectedEditionName})...");
            DismApi.Initialize(DismLogLevel.LogErrors);
            dismInitialized = true;

            await Task.Run(() => DismApi.MountImage(localWim, mountDir, options.SelectedImageIndex), cancellationToken).ConfigureAwait(false);
            wimMounted = true;

            Report(progress, 45, "Applying WinUtil modifications...");
            string editionId = await DetectEditionIdAsync(mountDir, options.SelectedEditionName, cancellationToken).ConfigureAwait(false);
            string autounattend = LoadAutounattendXml();

            MicroWinModifier modifier = new(_process, Log);
            MicroWinModifierContext context = new(
                ScratchDir: mountDir,
                IsoContentsDir: isoContents,
                AutoUnattendXml: autounattend,
                InjectCurrentSystemDrivers: options.InjectDrivers,
                InstallEditionId: editionId,
                InstallImageIndex: FinalImageIndex);
            await modifier.ApplyAsync(context, cancellationToken).ConfigureAwait(false);

            Report(progress, 56, "Cleaning up component store (WinSxS)...");
            await RunAsync("dism.exe", ["/English", $"/image:{mountDir}", "/Cleanup-Image", "/StartComponentCleanup", "/ResetBase"], cancellationToken).ConfigureAwait(false);

            Report(progress, 65, "Saving modified install image (this can take several minutes)...");
            await Task.Run(() => DismApi.UnmountImage(mountDir, commitChanges: true), cancellationToken).ConfigureAwait(false);
            wimMounted = false;

            Report(progress, 70, $"Exporting edition '{options.SelectedEditionName}' to a single-edition install.wim...");
            string exportWim = Path.Combine(isoContents, "sources", "install_export.wim");
            ProcessResult export = await RunAsync(
                "dism.exe",
                ["/English", "/Export-Image", $"/SourceImageFile:{localWim}", $"/SourceIndex:{options.SelectedImageIndex.ToString(CultureInfo.InvariantCulture)}", $"/DestinationImageFile:{exportWim}"],
                cancellationToken).ConfigureAwait(false);
            if (!export.Succeeded || !File.Exists(exportWim))
            {
                throw new InvalidOperationException($"dism /Export-Image failed (exit {export.ExitCode.ToString(CultureInfo.InvariantCulture)}).");
            }

            File.Delete(localWim);
            string finalWim = Path.Combine(isoContents, "sources", "install.wim");
            File.Move(exportWim, finalWim, overwrite: true);
            Log($"Unused editions removed. install.wim now contains only '{options.SelectedEditionName}'.");

            // PARITY: the PS "metadata hydration" step (Set-WindowsImage Name/Description) is intentionally
            // omitted — it is best-effort in the source and Set-WindowsImage has no ManagedDism equivalent.

            Report(progress, 80, "Dismounting source ISO...");
            TryDismountDiskImage(isoPath);

            _workspace = new MicroWinWorkspace(workDir, isoContents);
            Report(progress, 100, "Modification complete.");
            return OperationResult<MicroWinWorkspace>.Ok(_workspace);
        }
        catch (Exception ex)
        {
            Log($"ERROR during modification: {ex.Message}");
            // FIX: discard the WIM mount if it is still up (the PS catch attempted the same, but here it runs
            // for any failure path including cancellation) so a failed run never leaves a stale mount behind.
            if (wimMounted)
            {
                TryDiscardWimMount(mountDir);
            }

            TryDismountDiskImage(isoPath);
            SafeDeleteDirectory(workDir);
            return OperationResult<MicroWinWorkspace>.Fail($"install.wim modification failed: {ex.Message}");
        }
        finally
        {
            if (dismInitialized)
            {
                DismApi.Shutdown();
            }
        }
    }

    public async Task<OperationResult> ExportToIsoAsync(
        string outputIsoPath,
        IProgress<TaskProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputIsoPath);

        if (_workspace is null || !Directory.Exists(_workspace.IsoContentsDir))
        {
            return OperationResult.Fail("No modified ISO content found. Run ModifyAsync first.");
        }

        string contentsDir = _workspace.IsoContentsDir;

        string? oscdimg;
        try
        {
            oscdimg = await LocateOscdimgAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return OperationResult.Fail($"Failed while locating oscdimg: {ex.Message}");
        }

        if (oscdimg is null)
        {
            return OperationResult.Fail(
                "oscdimg.exe could not be found or installed. Install it via 'winget install -e --id Microsoft.OSCDIMG' or the Windows ADK.");
        }

        try
        {
            Report(progress, 10, "Building ISO...");

            // Boot-data string reused verbatim from Invoke-WinUtilISO.ps1 (BIOS + UEFI boot sectors).
            string bootData =
                $"2#p0,e,b\"{contentsDir}\\boot\\etfsboot.com\"#pEF,e,b\"{contentsDir}\\efi\\microsoft\\boot\\efisys.bin\"";

            // PARITY RISK: oscdimg's -bootdata uses literal inner quotes around each path. Whether they reach
            // oscdimg intact depends on how IProcessRunner builds the command line; a runner that re-escapes
            // embedded quotes may break boot-data on work dirs containing spaces.
            ProcessResult result = await RunAsync(
                oscdimg,
                ["-m", "-o", "-u2", "-udfver102", $"-bootdata:{bootData}", "-lCTOS_MODIFIED", contentsDir, outputIsoPath],
                cancellationToken).ConfigureAwait(false);

            if (result.Succeeded)
            {
                Report(progress, 100, $"ISO exported successfully: {outputIsoPath}");
                return OperationResult.Ok($"ISO exported: {outputIsoPath}");
            }

            return OperationResult.Fail($"oscdimg exited with code {result.ExitCode.ToString(CultureInfo.InvariantCulture)}.");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail($"ISO export failed: {ex.Message}");
        }
    }

    public async Task<OperationResult> CleanupAsync(CancellationToken cancellationToken = default)
    {
        return await Task.Run(CleanupCore, cancellationToken).ConfigureAwait(false);
    }

    private OperationResult CleanupCore()
    {
        string? workDir = _workspace?.WorkDir;

        try
        {
            DismApi.Initialize(DismLogLevel.LogErrors);
            try
            {
                if (workDir is not null)
                {
                    foreach (DismMountedImageInfo image in DismApi.GetMountedImages())
                    {
                        if (image.MountPath.StartsWith(workDir, StringComparison.OrdinalIgnoreCase))
                        {
                            Log($"Dismounting WIM at {image.MountPath} (discarding changes)...");
                            TryDiscardWimMount(image.MountPath);
                        }
                    }
                }
            }
            finally
            {
                DismApi.Shutdown();
            }
        }
        catch (Exception ex)
        {
            Log($"Warning: could not enumerate/dismount mounted images: {ex.Message}");
        }

        if (_isoPath is not null)
        {
            TryDismountDiskImage(_isoPath);
        }

        if (workDir is not null)
        {
            SafeDeleteDirectory(workDir);
        }

        _isoPath = null;
        _mount = null;
        _workspace = null;
        return OperationResult.Ok("Cleanup complete.");
    }

    // -- Edition ID detection (port of Get-WinUtilMountedImageEditionId + Get-WinUtilEditionIdFromName) ------

    private async Task<string> DetectEditionIdAsync(string mountDir, string editionName, CancellationToken cancellationToken)
    {
        try
        {
            ProcessResult result = await RunAsync(
                "dism.exe",
                ["/English", $"/Image:{mountDir}", "/Get-CurrentEdition"],
                cancellationToken,
                logOutput: false).ConfigureAwait(false);

            foreach (string line in SplitLines(result.StandardOutput))
            {
                int colon = line.IndexOf(':');
                if (colon <= 0)
                {
                    continue;
                }

                if (string.Equals(line[..colon].Trim(), "Current Edition", StringComparison.OrdinalIgnoreCase))
                {
                    string editionId = line[(colon + 1)..].Trim();
                    if (editionId.Length > 0)
                    {
                        Log($"Detected mounted image EditionID: {editionId}");
                        return editionId;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log($"Warning: could not detect mounted image EditionID with DISM: {ex.Message}");
        }

        string fallback = FallbackEditionId(editionName);
        if (fallback.Length > 0)
        {
            Log($"Using fallback EditionID '{fallback}' from selected edition name.");
        }

        return fallback;
    }

    private static string FallbackEditionId(string editionName)
    {
        string normalized = editionName.Trim();
        const string prefix = "Windows 11 ";
        if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[prefix.Length..].Trim();
        }

        return normalized switch
        {
            "Home Single Language" => "CoreSingleLanguage",
            "Home N" => "CoreN",
            "Home" => "Core",
            "Pro for Workstations N" => "ProfessionalWorkstationN",
            "Pro for Workstations" => "ProfessionalWorkstation",
            "Pro Education N" => "ProfessionalEducationN",
            "Pro Education" => "ProfessionalEducation",
            "Pro N" => "ProfessionalN",
            "Pro" => "Professional",
            "Education N" => "EducationN",
            "Education" => "Education",
            "Enterprise LTSC N" => "EnterpriseSN",
            "Enterprise LTSC" => "EnterpriseS",
            "Enterprise N" => "EnterpriseN",
            "Enterprise" => "Enterprise",
            _ => string.Empty,
        };
    }

    // -- oscdimg discovery (ADK / winget) ------------------------------------------------------------------

    private async Task<string?> LocateOscdimgAsync(CancellationToken cancellationToken)
    {
        string? found = FindOscdimgOnDisk();
        if (found is not null)
        {
            return found;
        }

        // PARITY: match the PS behavior of attempting a winget install and re-scanning the package cache.
        Log("oscdimg.exe not found. Attempting to install via winget...");
        try
        {
            await RunAsync(
                "winget.exe",
                ["install", "-e", "--id", "Microsoft.OSCDIMG", "--accept-package-agreements", "--accept-source-agreements"],
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log($"winget not available or install failed: {ex.Message}");
        }

        return FindOscdimgOnDisk();
    }

    private static string? FindOscdimgOnDisk()
    {
        string kits = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Windows Kits");
        string? adk = SafeFindFirst(kits, "oscdimg.exe", _ => true);
        if (adk is not null)
        {
            return adk;
        }

        string wingetPackages = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "WinGet", "Packages");
        return SafeFindFirst(wingetPackages, "oscdimg.exe",
            full => full.Contains("Microsoft.OSCDIMG", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Recursively finds the first matching file, skipping directories that deny enumeration.</summary>
    private static string? SafeFindFirst(string root, string fileName, Func<string, bool> predicate)
    {
        if (!Directory.Exists(root))
        {
            return null;
        }

        Stack<string> pending = new();
        pending.Push(root);

        while (pending.Count > 0)
        {
            string current = pending.Pop();

            try
            {
                foreach (string file in Directory.EnumerateFiles(current, fileName))
                {
                    if (predicate(file))
                    {
                        return file;
                    }
                }

                foreach (string dir in Directory.EnumerateDirectories(current))
                {
                    pending.Push(dir);
                }
            }
            catch (Exception)
            {
                // Skip unreadable directories (access denied / reparse loops) and keep scanning siblings.
            }
        }

        return null;
    }

    // -- Storage WMI: ISO mount / dismount -----------------------------------------------------------------

    private static string MountDiskImage(string imagePath, CancellationToken cancellationToken)
    {
        ManagementScope scope = new(StorageScopePath);
        scope.Connect();

        using (ManagementObject? diskImage = GetDiskImage(scope, imagePath))
        {
            if (diskImage is null)
            {
                throw new InvalidOperationException($"ISO not found in Storage WMI: {imagePath}");
            }

            using ManagementBaseObject inParams = diskImage.GetMethodParameters("Mount");
            using ManagementBaseObject outParams = diskImage.InvokeMethod("Mount", inParams, null);
        }

        // FIX: bounded replacement for the PS unbounded `do { Start-Sleep } until (...DriveLetter)` loop.
        for (int attempt = 0; attempt < MountPollMaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? letter = TryGetDriveLetter(scope, imagePath);
            if (letter is not null)
            {
                return letter;
            }

            Thread.Sleep(MountPollDelayMs);
        }

        throw new TimeoutException(
            $"Mounted ISO did not expose a drive letter within {(MountPollMaxAttempts * MountPollDelayMs / 1000).ToString(CultureInfo.InvariantCulture)} seconds.");
    }

    private void TryDismountDiskImage(string imagePath)
    {
        try
        {
            ManagementScope scope = new(StorageScopePath);
            scope.Connect();
            using ManagementObject? diskImage = GetDiskImage(scope, imagePath);
            if (diskImage is null)
            {
                return;
            }

            using ManagementBaseObject inParams = diskImage.GetMethodParameters("Dismount");
            using ManagementBaseObject outParams = diskImage.InvokeMethod("Dismount", inParams, null);
        }
        catch (Exception ex)
        {
            Log($"Warning: could not dismount ISO {imagePath}: {ex.Message}");
        }
    }

    private static ManagementObject? GetDiskImage(ManagementScope scope, string imagePath)
    {
        // WQL string literals use single quotes and treat backslash as an escape character.
        string escaped = imagePath
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("'", "\\'", StringComparison.Ordinal);

        using ManagementObjectSearcher searcher = new(
            scope,
            new ObjectQuery($"SELECT * FROM MSFT_DiskImage WHERE ImagePath='{escaped}'"));
        using ManagementObjectCollection results = searcher.Get();

        foreach (ManagementBaseObject item in results)
        {
            return (ManagementObject)item;
        }

        return null;
    }

    private static string? TryGetDriveLetter(ManagementScope scope, string imagePath)
    {
        using ManagementObject? diskImage = GetDiskImage(scope, imagePath);
        if (diskImage is null)
        {
            return null;
        }

        // MSFT_DiskImage → MSFT_Volume via the MSFT_DiskImageToVolume association.
        using ManagementObjectCollection related = diskImage.GetRelated("MSFT_Volume");
        foreach (ManagementBaseObject volume in related)
        {
            using (volume)
            {
                char letter = ToDriveChar(volume["DriveLetter"]);
                if (letter != '\0')
                {
                    return letter + ":";
                }
            }
        }

        return null;
    }

    private static char ToDriveChar(object? value) => value switch
    {
        char ch when ch != '\0' => ch,
        ushort u when u != 0 => (char)u,
        short s when s > 0 => (char)s,
        string str when str.Length > 0 => str[0],
        _ => '\0',
    };

    // -- WIM dismount (discard) ----------------------------------------------------------------------------

    private void TryDiscardWimMount(string mountDir)
    {
        try
        {
            DismApi.UnmountImage(mountDir, commitChanges: false);
        }
        catch (Exception ex)
        {
            Log($"Warning: could not discard WIM mount at {mountDir}: {ex.Message}");
        }
    }

    // -- Process + IO helpers ------------------------------------------------------------------------------

    private async Task<ProcessResult> RunAsync(
        string tool,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        bool logOutput = true)
    {
        ProcessResult result = await _process.RunAsync(tool, arguments, cancellationToken).ConfigureAwait(false);

        if (logOutput)
        {
            foreach (string line in SplitLines(result.StandardOutput))
            {
                Log(line);
            }
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

    private static string CreateWorkDir()
    {
        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        string candidate = Path.Combine(Path.GetTempPath(), "WinUtil_Win11ISO_" + stamp);
        if (Directory.Exists(candidate))
        {
            string suffix = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..8];
            candidate = Path.Combine(Path.GetTempPath(), $"WinUtil_Win11ISO_{stamp}_{suffix}");
        }

        Directory.CreateDirectory(candidate);
        return candidate;
    }

    private static string LoadAutounattendXml()
    {
        using Stream? stream = typeof(MicroWinBuilder).Assembly.GetManifestResourceStream("winutil.autounattend.xml");
        if (stream is null)
        {
            return string.Empty;
        }

        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }

    private static void ClearReadOnly(string filePath)
    {
        FileAttributes attributes = File.GetAttributes(filePath);
        if ((attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
        {
            File.SetAttributes(filePath, attributes & ~FileAttributes.ReadOnly);
        }
    }

    private void SafeDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex)
        {
            Log($"Warning: could not remove temp directory {path}: {ex.Message}");
        }
    }

    private void Log(string message) => _logger.LogInformation("{IsoLog}", message);

    private void Report(IProgress<TaskProgress>? progress, int percent, string message)
    {
        Log(message);
        progress?.Report(new TaskProgress(percent, message));
    }
}
