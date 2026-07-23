using System.Globalization;
using System.Text;
using System.Xml;

using WinUtil.Core.Abstractions;

namespace WinUtil.Platform.Iso;

// Result-over-exceptions boundary: best-effort cleanup/logging paths deliberately catch broad failures
// and continue, mirroring the PowerShell script which logged warnings and pressed on.
#pragma warning disable CA1031

/// <summary>Inputs for a single offline-image modification pass.</summary>
/// <param name="ScratchDir">Directory where the selected install image is currently mounted.</param>
/// <param name="IsoContentsDir">Root of the extracted ISO tree (autounattend.xml / ei.cfg / support removal).</param>
/// <param name="AutoUnattendXml">Full autounattend.xml content, or empty to skip the OOBE bypass file.</param>
/// <param name="InjectCurrentSystemDrivers">Export + inject the running host's drivers into install.wim/boot.wim.</param>
/// <param name="InstallEditionId">Edition ID for sources\ei.cfg (detected by the builder), or empty to skip.</param>
/// <param name="InstallImageIndex">Image index setup should install from the final single-edition install.wim.</param>
internal sealed record MicroWinModifierContext(
    string ScratchDir,
    string IsoContentsDir,
    string AutoUnattendXml,
    bool InjectCurrentSystemDrivers,
    string InstallEditionId,
    int InstallImageIndex);

/// <summary>
/// Ports Invoke-WinUtilISOScript.ps1: the offline edits applied to a mounted Windows 11 install image —
/// provisioned-appx removal, optional driver injection, offline-hive registry tweaks, telemetry scheduled-task
/// deletion, answer-file preparation + setup-script staging, and ei.cfg / support-folder handling.
///
/// Managed vs shelled (documented per the task):
///  - Provisioned appx list/remove, driver export/add, boot.wim mount/unmount → dism.exe via IProcessRunner
///    (no ManagedDism equivalent for Export-Driver; the appx and driver flows match the PS 1:1).
///  - Offline registry values → reg.exe add/delete via IProcessRunner (mirrors Set-ISOScriptReg / Remove-ISOScriptReg).
///  - Hive load/unload → RegLoadKey/RegUnLoadKey P/Invoke (<see cref="IsoNativeMethods"/>), wrapped in try/finally.
///  - Answer-file XML, setup-script staging, ei.cfg, task/support deletion → System.Xml + System.IO.
/// </summary>
internal sealed class MicroWinModifier
{
    private readonly IProcessRunner _process;
    private readonly Action<string> _log;

    public MicroWinModifier(IProcessRunner process, Action<string> log)
    {
        _process = process;
        _log = log;
    }

    public async Task ApplyAsync(MicroWinModifierContext ctx, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        await RemoveProvisionedAppxAsync(ctx.ScratchDir, cancellationToken).ConfigureAwait(false);

        if (ctx.InjectCurrentSystemDrivers)
        {
            await InjectDriversAsync(ctx, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            _log("Driver injection skipped.");
        }

        // PARITY: the PS script interleaves the file-based steps (answer file, ei.cfg) between registry writes,
        // all inside one reg-load/reg-unload span. Those file steps do not touch the loaded hives, so here every
        // registry write is consolidated into a single load → apply → unload guard (see ApplyRegistryTweaksAsync)
        // and the file steps run around it. This is functionally equivalent and makes the unload unconditional.
        await ApplyRegistryTweaksAsync(ctx.ScratchDir, cancellationToken).ConfigureAwait(false);

        ApplyAnswerFileAndScripts(ctx);
        WriteEditionConfig(ctx.IsoContentsDir, ctx.InstallEditionId);
        DeleteScheduledTaskFiles(ctx.ScratchDir);
        RemoveSupportFolder(ctx.IsoContentsDir);
    }

    // -- 1. Remove provisioned AppX packages ---------------------------------------------------------------

    private async Task RemoveProvisionedAppxAsync(string scratchDir, CancellationToken cancellationToken)
    {
        _log("Removing provisioned AppX packages...");

        ProcessResult listed = await RunAsync(
            "dism.exe",
            ["/English", $"/image:{scratchDir}", "/Get-ProvisionedAppxPackages"],
            cancellationToken,
            logOutput: false).ConfigureAwait(false);

        IEnumerable<string> matches = ParsePackageNames(listed.StandardOutput)
            .Where(pkg => AppxPrefixes.Any(prefix => pkg.Contains(prefix, StringComparison.OrdinalIgnoreCase)));

        foreach (string packageName in matches)
        {
            await RunAsync(
                "dism.exe",
                ["/English", $"/image:{scratchDir}", "/Remove-ProvisionedAppxPackage", $"/PackageName:{packageName}"],
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static List<string> ParsePackageNames(string dismOutput)
    {
        List<string> names = [];
        foreach (string line in SplitLines(dismOutput))
        {
            int colon = line.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            if (!string.Equals(line[..colon].Trim(), "PackageName", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string value = line[(colon + 1)..].Trim();
            if (value.Length > 0)
            {
                names.Add(value);
            }
        }

        return names;
    }

    // -- 2. Inject current system drivers (optional) -------------------------------------------------------

    private async Task InjectDriversAsync(MicroWinModifierContext ctx, CancellationToken cancellationToken)
    {
        _log("Exporting all drivers from running system...");
        string driverRoot = Path.Combine(Path.GetTempPath(), "WinUtil_DriverExport_" + NewToken());
        Directory.CreateDirectory(driverRoot);

        try
        {
            // PARITY: Export-WindowsDriver -Online has no ManagedDism equivalent; dism /online /Export-Driver
            // is the documented shell equivalent.
            await RunAsync("dism.exe", ["/English", "/online", "/Export-Driver", $"/Destination:{driverRoot}"], cancellationToken).ConfigureAwait(false);

            _log("Injecting current system drivers into install.wim...");
            await RunAsync("dism.exe", ["/English", $"/image:{ctx.ScratchDir}", "/Add-Driver", $"/Driver:{driverRoot}", "/Recurse"], cancellationToken).ConfigureAwait(false);
            _log("install.wim driver injection complete.");

            if (Directory.Exists(ctx.IsoContentsDir))
            {
                string bootWim = Path.Combine(ctx.IsoContentsDir, "sources", "boot.wim");
                if (File.Exists(bootWim))
                {
                    _log("Injecting current system drivers into boot.wim...");
                    await InjectBootWimAsync(bootWim, driverRoot, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    _log("Warning: boot.wim not found - skipping boot.wim driver injection.");
                }
            }
        }
        catch (Exception ex)
        {
            _log($"Error during driver export/injection: {ex.Message}");
        }
        finally
        {
            SafeDeleteDirectory(driverRoot);
        }
    }

    private async Task InjectBootWimAsync(string bootWimPath, string driverDir, CancellationToken cancellationToken)
    {
        ClearReadOnly(bootWimPath);
        string mountDir = Path.Combine(Path.GetTempPath(), "WinUtil_BootMount_" + NewToken());
        Directory.CreateDirectory(mountDir);
        bool mounted = false;

        try
        {
            _log("Mounting boot.wim (index 2) for driver injection...");
            ProcessResult mount = await RunAsync(
                "dism.exe",
                ["/English", "/Mount-Image", $"/ImageFile:{bootWimPath}", "/Index:2", $"/MountDir:{mountDir}"],
                cancellationToken).ConfigureAwait(false);

            if (!mount.Succeeded)
            {
                _log($"Warning: could not mount boot.wim (exit {mount.ExitCode.ToString(CultureInfo.InvariantCulture)}).");
                return;
            }

            mounted = true;
            await RunAsync("dism.exe", ["/English", $"/image:{mountDir}", "/Add-Driver", $"/Driver:{driverDir}", "/Recurse"], cancellationToken).ConfigureAwait(false);

            _log("Saving boot.wim...");
            ProcessResult commit = await RunAsync("dism.exe", ["/English", "/Unmount-Image", $"/MountDir:{mountDir}", "/Commit"], cancellationToken).ConfigureAwait(false);
            if (commit.Succeeded)
            {
                mounted = false;
                _log("boot.wim driver injection complete.");
            }
        }
        finally
        {
            if (mounted)
            {
                // FIX: guarantee the boot.wim mount is torn down even when injection/commit throws or fails.
                // (The PS version only discarded inside its catch, so an unexpected error could leak the mount.)
                ProcessResult discard = await RunAsync("dism.exe", ["/English", "/Unmount-Image", $"/MountDir:{mountDir}", "/Discard"], cancellationToken).ConfigureAwait(false);
                if (!discard.Succeeded)
                {
                    _log($"Warning: could not discard boot.wim mount (exit {discard.ExitCode.ToString(CultureInfo.InvariantCulture)}).");
                }
            }

            SafeDeleteDirectory(mountDir);
        }
    }

    // -- 3. Offline registry tweaks ------------------------------------------------------------------------

    private async Task ApplyRegistryTweaksAsync(string scratchDir, CancellationToken cancellationToken)
    {
        _log("Loading offline registry hives...");
        IsoNativeMethods.EnableHivePrivileges();

        string config = Path.Combine(scratchDir, "Windows", "System32", "config");
        (string SubKey, string File)[] hives =
        [
            ("zCOMPONENTS", Path.Combine(config, "COMPONENTS")),
            ("zDEFAULT", Path.Combine(config, "default")),
            ("zNTUSER", Path.Combine(scratchDir, "Users", "Default", "ntuser.dat")),
            ("zSOFTWARE", Path.Combine(config, "SOFTWARE")),
            ("zSYSTEM", Path.Combine(config, "SYSTEM")),
        ];

        List<string> loaded = [];
        try
        {
            foreach ((string subKey, string file) in hives)
            {
                IsoNativeMethods.LoadHive(subKey, file);
                loaded.Add(subKey);
            }

            _log("Applying offline registry tweaks...");
            foreach (RegSet set in RegistrySets)
            {
                await RegAddAsync(set.Path, set.Name, set.Type, set.Value, cancellationToken).ConfigureAwait(false);
            }

            foreach (string path in RegistryRemovals)
            {
                await RegDeleteAsync(path, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            // FIX: the PS script unloaded the hives only on its happy path — any throw between load and unload
            // left zCOMPONENTS..zSYSTEM loaded in HKLM until reboot. Here the unload is unconditional and covers
            // exactly the hives that actually loaded (reverse order, best-effort per hive).
            _log("Unloading offline registry hives...");
            loaded.Reverse();
            foreach (string subKey in loaded)
            {
                try
                {
                    IsoNativeMethods.UnloadHive(subKey);
                }
                catch (Exception ex)
                {
                    _log($"Warning: could not unload hive {subKey}: {ex.Message}");
                }
            }
        }
    }

    private async Task RegAddAsync(string path, string name, string type, string value, CancellationToken cancellationToken)
    {
        ProcessResult result = await RunAsync(
            "reg.exe",
            ["add", path, "/v", name, "/t", type, "/d", value, "/f"],
            cancellationToken,
            logOutput: false).ConfigureAwait(false);

        _log(result.Succeeded
            ? $"Set registry value: {path}\\{name}"
            : $"Error setting registry value {path}\\{name} (exit {result.ExitCode.ToString(CultureInfo.InvariantCulture)}).");
    }

    private async Task RegDeleteAsync(string path, CancellationToken cancellationToken)
    {
        ProcessResult result = await RunAsync(
            "reg.exe",
            ["delete", path, "/f"],
            cancellationToken,
            logOutput: false).ConfigureAwait(false);

        // A missing key is expected/non-fatal, exactly like the PS Remove-ISOScriptReg best-effort delete.
        _log(result.Succeeded
            ? $"Removed registry key: {path}"
            : $"Note: registry key not removed (may not exist): {path}");
    }

    // -- 4. Answer file + setup-script staging -------------------------------------------------------------

    private void ApplyAnswerFileAndScripts(MicroWinModifierContext ctx)
    {
        if (string.IsNullOrEmpty(ctx.AutoUnattendXml))
        {
            _log("Warning: autounattend.xml content is empty - skipping OOBE bypass file.");
            return;
        }

        string prepared = ctx.AutoUnattendXml;
        try
        {
            prepared = ConvertToAnswerFile(ctx.AutoUnattendXml, ctx.InstallImageIndex);
            _log($"Prepared autounattend.xml to install image index {ctx.InstallImageIndex.ToString(CultureInfo.InvariantCulture)} without forcing a product key.");
        }
        catch (Exception ex)
        {
            _log($"Warning: could not prepare autounattend.xml image selection: {ex.Message}");
        }

        try
        {
            StageSetupScripts(prepared, ctx.ScratchDir);
        }
        catch (Exception ex)
        {
            _log($"Warning: could not pre-stage setup scripts from autounattend.xml: {ex.Message}");
        }

        if (Directory.Exists(ctx.IsoContentsDir))
        {
            string isoDest = Path.Combine(ctx.IsoContentsDir, "autounattend.xml");
            File.WriteAllText(isoDest, prepared, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            _log($"Written autounattend.xml to ISO root ({isoDest}).");
        }
    }

    /// <summary>
    /// Port of ConvertTo-WinUtilISOAnswerFile: ensures a windowsPE Microsoft-Windows-Setup component exists,
    /// strips empty/all-zero product keys, and pins ImageInstall/OSImage/InstallFrom to the given image index
    /// so setup installs the single exported edition without forcing an embedded firmware key.
    /// </summary>
    private static string ConvertToAnswerFile(string xmlContent, int imageIndex)
    {
        if (imageIndex < 1)
        {
            imageIndex = 1;
        }

        const string unattendNs = "urn:schemas-microsoft-com:unattend";
        const string wcmNs = "http://schemas.microsoft.com/WMIConfig/2002/State";

        XmlDocument doc = new() { PreserveWhitespace = true };
        doc.LoadXml(xmlContent);

        XmlElement? root = doc.DocumentElement;
        if (root is null || !string.Equals(root.NamespaceURI, unattendNs, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unexpected autounattend.xml namespace: {root?.NamespaceURI}");
        }

        if (!root.HasAttribute("xmlns:wcm"))
        {
            root.SetAttribute("wcm", "http://www.w3.org/2000/xmlns/", wcmNs);
        }

        XmlNamespaceManager ns = new(doc.NameTable);
        ns.AddNamespace("u", unattendNs);

        if (doc.SelectSingleNode("/u:unattend/u:settings[@pass=\"windowsPE\"]", ns) is not XmlElement windowsPe)
        {
            windowsPe = doc.CreateElement("settings", unattendNs);
            windowsPe.SetAttribute("pass", "windowsPE");
            root.PrependChild(windowsPe);
        }

        if (windowsPe.SelectSingleNode("u:component[@name=\"Microsoft-Windows-Setup\"]", ns) is not XmlElement setup)
        {
            setup = doc.CreateElement("component", unattendNs);
            setup.SetAttribute("name", "Microsoft-Windows-Setup");
            setup.SetAttribute("processorArchitecture", "amd64");
            setup.SetAttribute("publicKeyToken", "31bf3856ad364e35");
            setup.SetAttribute("language", "neutral");
            setup.SetAttribute("versionScope", "nonSxS");
            windowsPe.AppendChild(setup);
        }

        XmlNodeList? productKeys = setup.SelectNodes("u:UserData/u:ProductKey", ns);
        if (productKeys is not null)
        {
            foreach (XmlNode productKey in productKeys.Cast<XmlNode>().ToList())
            {
                string keyValue = productKey.SelectSingleNode("u:Key", ns)?.InnerText.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(keyValue) || string.Equals(keyValue, "00000-00000-00000-00000-00000", StringComparison.Ordinal))
                {
                    productKey.ParentNode?.RemoveChild(productKey);
                }
            }
        }

        XmlElement imageInstall = GetOrCreateChild(setup, "ImageInstall", unattendNs);
        XmlElement osImage = GetOrCreateChild(imageInstall, "OSImage", unattendNs);
        XmlElement installFrom = GetOrCreateChild(osImage, "InstallFrom", unattendNs);

        XmlNodeList? existingMetadata = installFrom.SelectNodes("u:MetaData", ns);
        if (existingMetadata is not null)
        {
            foreach (XmlNode metadataNode in existingMetadata.Cast<XmlNode>().ToList())
            {
                installFrom.RemoveChild(metadataNode);
            }
        }

        XmlElement metadata = doc.CreateElement("MetaData", unattendNs);
        XmlAttribute action = doc.CreateAttribute("wcm", "action", wcmNs);
        action.Value = "add";
        metadata.Attributes.Append(action);

        XmlElement key = doc.CreateElement("Key", unattendNs);
        key.InnerText = "/IMAGE/INDEX";
        metadata.AppendChild(key);

        XmlElement value = doc.CreateElement("Value", unattendNs);
        value.InnerText = imageIndex.ToString(CultureInfo.InvariantCulture);
        metadata.AppendChild(value);

        installFrom.AppendChild(metadata);

        return doc.OuterXml;
    }

    private static XmlElement GetOrCreateChild(XmlElement parent, string name, string namespaceUri)
    {
        foreach (XmlNode child in parent.ChildNodes)
        {
            if (child.NodeType == XmlNodeType.Element
                && string.Equals(child.LocalName, name, StringComparison.Ordinal)
                && string.Equals(child.NamespaceURI, namespaceUri, StringComparison.Ordinal))
            {
                return (XmlElement)child;
            }
        }

        XmlElement created = parent.OwnerDocument!.CreateElement(name, namespaceUri);
        parent.AppendChild(created);
        return created;
    }

    /// <summary>
    /// Writes each &lt;Extensions&gt;&lt;File&gt; node's content into the mounted image at its target path so the
    /// setup scripts survive Windows Setup stripping unknown-namespace elements from the Panther answer-file copy.
    /// </summary>
    private void StageSetupScripts(string preparedXml, string scratchDir)
    {
        XmlDocument doc = new();
        doc.LoadXml(preparedXml);

        XmlNamespaceManager ns = new(doc.NameTable);
        ns.AddNamespace("sg", "https://schneegans.de/windows/unattend-generator/");

        XmlNodeList? files = doc.SelectNodes("//sg:File", ns);
        if (files is null || files.Count == 0)
        {
            _log("Warning: no <Extensions><File> nodes found in autounattend.xml - setup scripts not pre-staged.");
            return;
        }

        foreach (XmlNode node in files)
        {
            if (node is not XmlElement element)
            {
                continue;
            }

            string relPath = StripDriveRoot(element.GetAttribute("path"));
            string destPath = Path.Combine(scratchDir, relPath);
            string? parent = Path.GetDirectoryName(destPath);
            if (parent is not null)
            {
                Directory.CreateDirectory(parent);
            }

            Encoding encoding = Path.GetExtension(destPath).ToLowerInvariant() switch
            {
                ".ps1" or ".xml" => new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
                ".reg" or ".vbs" or ".js" => new UnicodeEncoding(bigEndian: false, byteOrderMark: true),
                _ => Encoding.Default,
            };

            byte[] preamble = encoding.GetPreamble();
            byte[] body = encoding.GetBytes(element.InnerText.Trim());
            File.WriteAllBytes(destPath, [.. preamble, .. body]);
            _log($"Pre-staged setup script: {relPath}");
        }
    }

    private static string StripDriveRoot(string path) =>
        path.Length >= 3 && path[1] == ':' && (path[2] == '\\' || path[2] == '/')
            ? path[3..]
            : path;

    // -- ei.cfg / PID.txt ----------------------------------------------------------------------------------

    private void WriteEditionConfig(string contentRoot, string editionId)
    {
        if (!Directory.Exists(contentRoot))
        {
            return;
        }

        string sourcesDir = Path.Combine(contentRoot, "sources");
        Directory.CreateDirectory(sourcesDir);

        string pidPath = Path.Combine(sourcesDir, "PID.txt");
        if (File.Exists(pidPath))
        {
            File.Delete(pidPath);
            _log("Removed sources\\PID.txt so setup will not force a stale or mismatched product key.");
        }

        if (string.IsNullOrWhiteSpace(editionId))
        {
            _log("Warning: selected edition ID is unknown - skipping sources\\ei.cfg fallback.");
            return;
        }

        string eiCfg = string.Join("\r\n", "[EditionID]", editionId, "[Channel]", "Retail", "[VL]", "0");
        File.WriteAllText(Path.Combine(sourcesDir, "ei.cfg"), eiCfg, new ASCIIEncoding());
        _log($"Written sources\\ei.cfg for EditionID '{editionId}'.");
    }

    // -- Scheduled-task + support-folder deletion ----------------------------------------------------------

    private void DeleteScheduledTaskFiles(string scratchDir)
    {
        _log("Deleting scheduled task definition files...");
        string tasksRoot = Path.Combine(scratchDir, "Windows", "System32", "Tasks");
        foreach (string relative in ScheduledTaskPaths)
        {
            SafeDeletePath(Path.Combine(tasksRoot, relative));
        }

        _log("Scheduled task files deleted.");
    }

    private void RemoveSupportFolder(string isoContentsDir)
    {
        if (!Directory.Exists(isoContentsDir))
        {
            return;
        }

        string support = Path.Combine(isoContentsDir, "support");
        if (Directory.Exists(support))
        {
            _log("Removing ISO support\\ folder...");
            SafeDeleteDirectory(support);
            _log("ISO support\\ folder removed.");
        }
    }

    // -- Shared helpers ------------------------------------------------------------------------------------

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
                _log(line);
            }
        }

        foreach (string line in SplitLines(result.StandardError))
        {
            _log("[stderr] " + line);
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

    private static string NewToken() => Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);

    private static void ClearReadOnly(string filePath)
    {
        FileAttributes attributes = File.GetAttributes(filePath);
        if ((attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
        {
            File.SetAttributes(filePath, attributes & ~FileAttributes.ReadOnly);
        }
    }

    private void SafeDeletePath(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
            else if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            _log($"WARNING: could not delete {path}: {ex.Message}");
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
            _log($"WARNING: could not delete directory {path}: {ex.Message}");
        }
    }

    // -- Static data (ported verbatim from Invoke-WinUtilISOScript.ps1) ------------------------------------

    private sealed record RegSet(string Path, string Name, string Type, string Value);

    private static readonly string[] AppxPrefixes =
    [
        "Clipchamp.Clipchamp",
        "Microsoft.BingNews",
        "Microsoft.BingSearch",
        "Microsoft.BingWeather",
        "Microsoft.GetHelp",
        "Microsoft.MicrosoftOfficeHub",
        "Microsoft.MicrosoftSolitaireCollection",
        "Microsoft.MicrosoftStickyNotes",
        "Microsoft.OutlookForWindows",
        "Microsoft.Paint",
        "Microsoft.PowerAutomateDesktop",
        "Microsoft.StartExperiencesApp",
        "Microsoft.Todos",
        "Microsoft.Windows.DevHome",
        "Microsoft.WindowsFeedbackHub",
        "Microsoft.WindowsSoundRecorder",
        "Microsoft.ZuneMusic",
        "MicrosoftCorporationII.QuickAssist",
        "MSTeams",
    ];

    // NOTE: order is irrelevant — every entry targets an independent key/value, so grouping all "add"s before
    // all "delete"s (rather than the PS interleave) is safe and keeps the audit against the source trivial.
    private static readonly RegSet[] RegistrySets =
    [
        // Bypass system requirements
        new("HKLM\\zDEFAULT\\Control Panel\\UnsupportedHardwareNotificationCache", "SV1", "REG_DWORD", "0"),
        new("HKLM\\zDEFAULT\\Control Panel\\UnsupportedHardwareNotificationCache", "SV2", "REG_DWORD", "0"),
        new("HKLM\\zNTUSER\\Control Panel\\UnsupportedHardwareNotificationCache", "SV1", "REG_DWORD", "0"),
        new("HKLM\\zNTUSER\\Control Panel\\UnsupportedHardwareNotificationCache", "SV2", "REG_DWORD", "0"),
        new("HKLM\\zSYSTEM\\Setup\\LabConfig", "BypassCPUCheck", "REG_DWORD", "1"),
        new("HKLM\\zSYSTEM\\Setup\\LabConfig", "BypassRAMCheck", "REG_DWORD", "1"),
        new("HKLM\\zSYSTEM\\Setup\\LabConfig", "BypassSecureBootCheck", "REG_DWORD", "1"),
        new("HKLM\\zSYSTEM\\Setup\\LabConfig", "BypassStorageCheck", "REG_DWORD", "1"),
        new("HKLM\\zSYSTEM\\Setup\\LabConfig", "BypassTPMCheck", "REG_DWORD", "1"),
        new("HKLM\\zSYSTEM\\Setup\\MoSetup", "AllowUpgradesWithUnsupportedTPMOrCPU", "REG_DWORD", "1"),

        // Disable sponsored apps
        new("HKLM\\zNTUSER\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\ContentDeliveryManager", "OemPreInstalledAppsEnabled", "REG_DWORD", "0"),
        new("HKLM\\zNTUSER\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\ContentDeliveryManager", "PreInstalledAppsEnabled", "REG_DWORD", "0"),
        new("HKLM\\zNTUSER\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\ContentDeliveryManager", "SilentInstalledAppsEnabled", "REG_DWORD", "0"),
        new("HKLM\\zSOFTWARE\\Policies\\Microsoft\\Windows\\CloudContent", "DisableWindowsConsumerFeatures", "REG_DWORD", "1"),
        new("HKLM\\zNTUSER\\Software\\Microsoft\\Windows\\CurrentVersion\\ContentDeliveryManager", "ContentDeliveryAllowed", "REG_DWORD", "0"),
        new("HKLM\\zSOFTWARE\\Microsoft\\PolicyManager\\current\\device\\Start", "ConfigureStartPins", "REG_SZ", "{\"pinnedList\": [{}]}"),
        new("HKLM\\zNTUSER\\Software\\Microsoft\\Windows\\CurrentVersion\\ContentDeliveryManager", "FeatureManagementEnabled", "REG_DWORD", "0"),
        new("HKLM\\zNTUSER\\Software\\Microsoft\\Windows\\CurrentVersion\\ContentDeliveryManager", "PreInstalledAppsEverEnabled", "REG_DWORD", "0"),
        new("HKLM\\zNTUSER\\Software\\Microsoft\\Windows\\CurrentVersion\\ContentDeliveryManager", "SoftLandingEnabled", "REG_DWORD", "0"),
        new("HKLM\\zNTUSER\\Software\\Microsoft\\Windows\\CurrentVersion\\ContentDeliveryManager", "SubscribedContentEnabled", "REG_DWORD", "0"),
        new("HKLM\\zNTUSER\\Software\\Microsoft\\Windows\\CurrentVersion\\ContentDeliveryManager", "SubscribedContent-310093Enabled", "REG_DWORD", "0"),
        new("HKLM\\zNTUSER\\Software\\Microsoft\\Windows\\CurrentVersion\\ContentDeliveryManager", "SubscribedContent-338388Enabled", "REG_DWORD", "0"),
        new("HKLM\\zNTUSER\\Software\\Microsoft\\Windows\\CurrentVersion\\ContentDeliveryManager", "SubscribedContent-338389Enabled", "REG_DWORD", "0"),
        new("HKLM\\zNTUSER\\Software\\Microsoft\\Windows\\CurrentVersion\\ContentDeliveryManager", "SubscribedContent-338393Enabled", "REG_DWORD", "0"),
        new("HKLM\\zNTUSER\\Software\\Microsoft\\Windows\\CurrentVersion\\ContentDeliveryManager", "SubscribedContent-353694Enabled", "REG_DWORD", "0"),
        new("HKLM\\zNTUSER\\Software\\Microsoft\\Windows\\CurrentVersion\\ContentDeliveryManager", "SubscribedContent-353696Enabled", "REG_DWORD", "0"),
        new("HKLM\\zNTUSER\\Software\\Microsoft\\Windows\\CurrentVersion\\ContentDeliveryManager", "SystemPaneSuggestionsEnabled", "REG_DWORD", "0"),
        new("HKLM\\zSOFTWARE\\Policies\\Microsoft\\PushToInstall", "DisablePushToInstall", "REG_DWORD", "1"),
        new("HKLM\\zSOFTWARE\\Policies\\Microsoft\\MRT", "DontOfferThroughWUAU", "REG_DWORD", "1"),
        new("HKLM\\zSOFTWARE\\Policies\\Microsoft\\Windows\\CloudContent", "DisableConsumerAccountStateContent", "REG_DWORD", "1"),
        new("HKLM\\zSOFTWARE\\Policies\\Microsoft\\Windows\\CloudContent", "DisableCloudOptimizedContent", "REG_DWORD", "1"),

        // Enable local accounts on OOBE
        new("HKLM\\zSOFTWARE\\Microsoft\\Windows\\CurrentVersion\\OOBE", "BypassNRO", "REG_DWORD", "1"),

        // Disable reserved storage
        new("HKLM\\zSOFTWARE\\Microsoft\\Windows\\CurrentVersion\\ReserveManager", "ShippedWithReserves", "REG_DWORD", "0"),

        // Disable BitLocker device encryption
        new("HKLM\\zSYSTEM\\ControlSet001\\Control\\BitLocker", "PreventDeviceEncryption", "REG_DWORD", "1"),

        // Disable Chat icon
        new("HKLM\\zSOFTWARE\\Policies\\Microsoft\\Windows\\Windows Chat", "ChatIcon", "REG_DWORD", "3"),
        new("HKLM\\zNTUSER\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced", "TaskbarMn", "REG_DWORD", "0"),

        // Disable OneDrive folder backup
        new("HKLM\\zSOFTWARE\\Policies\\Microsoft\\Windows\\OneDrive", "DisableFileSyncNGSC", "REG_DWORD", "1"),

        // Disable telemetry
        new("HKLM\\zNTUSER\\Software\\Microsoft\\Windows\\CurrentVersion\\AdvertisingInfo", "Enabled", "REG_DWORD", "0"),
        new("HKLM\\zNTUSER\\Software\\Microsoft\\Windows\\CurrentVersion\\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled", "REG_DWORD", "0"),
        new("HKLM\\zNTUSER\\Software\\Microsoft\\Speech_OneCore\\Settings\\OnlineSpeechPrivacy", "HasAccepted", "REG_DWORD", "0"),
        new("HKLM\\zNTUSER\\Software\\Microsoft\\Input\\TIPC", "Enabled", "REG_DWORD", "0"),
        new("HKLM\\zNTUSER\\Software\\Microsoft\\InputPersonalization", "RestrictImplicitInkCollection", "REG_DWORD", "1"),
        new("HKLM\\zNTUSER\\Software\\Microsoft\\InputPersonalization", "RestrictImplicitTextCollection", "REG_DWORD", "1"),
        new("HKLM\\zNTUSER\\Software\\Microsoft\\InputPersonalization\\TrainedDataStore", "HarvestContacts", "REG_DWORD", "0"),
        new("HKLM\\zNTUSER\\Software\\Microsoft\\Personalization\\Settings", "AcceptedPrivacyPolicy", "REG_DWORD", "0"),
        new("HKLM\\zSOFTWARE\\Policies\\Microsoft\\Windows\\DataCollection", "AllowTelemetry", "REG_DWORD", "0"),
        new("HKLM\\zSYSTEM\\ControlSet001\\Services\\dmwappushservice", "Start", "REG_DWORD", "4"),

        // Prevent installation of DevHome and Outlook
        new("HKLM\\zSOFTWARE\\Microsoft\\Windows\\CurrentVersion\\WindowsUpdate\\Orchestrator\\UScheduler_Oobe\\OutlookUpdate", "workCompleted", "REG_DWORD", "1"),
        new("HKLM\\zSOFTWARE\\Microsoft\\Windows\\CurrentVersion\\WindowsUpdate\\Orchestrator\\UScheduler\\OutlookUpdate", "workCompleted", "REG_DWORD", "1"),
        new("HKLM\\zSOFTWARE\\Microsoft\\Windows\\CurrentVersion\\WindowsUpdate\\Orchestrator\\UScheduler\\DevHomeUpdate", "workCompleted", "REG_DWORD", "1"),

        // Disable Copilot
        new("HKLM\\zSOFTWARE\\Policies\\Microsoft\\Windows\\WindowsCopilot", "TurnOffWindowsCopilot", "REG_DWORD", "1"),
        new("HKLM\\zSOFTWARE\\Policies\\Microsoft\\Edge", "HubsSidebarEnabled", "REG_DWORD", "0"),
        new("HKLM\\zSOFTWARE\\Policies\\Microsoft\\Windows\\Explorer", "DisableSearchBoxSuggestions", "REG_DWORD", "1"),

        // Disable Windows Update during OOBE (re-enabled on first logon via FirstLogon.ps1)
        new("HKLM\\zSOFTWARE\\Policies\\Microsoft\\Windows\\WindowsUpdate\\AU", "NoAutoUpdate", "REG_DWORD", "1"),
        new("HKLM\\zSOFTWARE\\Policies\\Microsoft\\Windows\\WindowsUpdate\\AU", "AUOptions", "REG_DWORD", "1"),
        new("HKLM\\zSOFTWARE\\Policies\\Microsoft\\Windows\\WindowsUpdate\\AU", "UseWUServer", "REG_DWORD", "1"),
        new("HKLM\\zSOFTWARE\\Policies\\Microsoft\\Windows\\WindowsUpdate", "DisableWindowsUpdateAccess", "REG_DWORD", "1"),
        new("HKLM\\zSOFTWARE\\Policies\\Microsoft\\Windows\\WindowsUpdate", "WUServer", "REG_SZ", "http://localhost:8080"),
        new("HKLM\\zSOFTWARE\\Policies\\Microsoft\\Windows\\WindowsUpdate", "WUStatusServer", "REG_SZ", "http://localhost:8080"),
        new("HKLM\\zSOFTWARE\\Microsoft\\Windows\\CurrentVersion\\WindowsUpdate\\Orchestrator\\UScheduler_Oobe\\WindowsUpdate", "workCompleted", "REG_DWORD", "1"),
        new("HKLM\\zSOFTWARE\\Microsoft\\Windows\\CurrentVersion\\DeliveryOptimization\\Config", "DODownloadMode", "REG_DWORD", "0"),
        new("HKLM\\zSYSTEM\\ControlSet001\\Services\\BITS", "Start", "REG_DWORD", "4"),
        new("HKLM\\zSYSTEM\\ControlSet001\\Services\\wuauserv", "Start", "REG_DWORD", "4"),
        new("HKLM\\zSYSTEM\\ControlSet001\\Services\\UsoSvc", "Start", "REG_DWORD", "4"),
        new("HKLM\\zSYSTEM\\ControlSet001\\Services\\WaaSMedicSvc", "Start", "REG_DWORD", "4"),

        // Prevent installation of Teams
        new("HKLM\\zSOFTWARE\\Policies\\Microsoft\\Teams", "DisableInstallation", "REG_DWORD", "1"),

        // Prevent installation of new Outlook
        new("HKLM\\zSOFTWARE\\Policies\\Microsoft\\Windows\\Windows Mail", "PreventRun", "REG_DWORD", "1"),
    ];

    private static readonly string[] RegistryRemovals =
    [
        "HKLM\\zNTUSER\\Software\\Microsoft\\Windows\\CurrentVersion\\ContentDeliveryManager\\Subscriptions",
        "HKLM\\zNTUSER\\Software\\Microsoft\\Windows\\CurrentVersion\\ContentDeliveryManager\\SuggestedApps",
        "HKLM\\zSOFTWARE\\Microsoft\\WindowsUpdate\\Orchestrator\\UScheduler_Oobe\\OutlookUpdate",
        "HKLM\\zSOFTWARE\\Microsoft\\WindowsUpdate\\Orchestrator\\UScheduler_Oobe\\DevHomeUpdate",
        "HKLM\\zSOFTWARE\\Microsoft\\WindowsUpdate\\Orchestrator\\UScheduler_Oobe\\WindowsUpdate",
    ];

    private static readonly string[] ScheduledTaskPaths =
    [
        "Microsoft\\Windows\\Application Experience\\Microsoft Compatibility Appraiser",
        "Microsoft\\Windows\\Customer Experience Improvement Program",
        "Microsoft\\Windows\\Application Experience\\ProgramDataUpdater",
        "Microsoft\\Windows\\Chkdsk\\Proxy",
        "Microsoft\\Windows\\Windows Error Reporting\\QueueReporting",
        "Microsoft\\Windows\\InstallService",
        "Microsoft\\Windows\\UpdateOrchestrator",
        "Microsoft\\Windows\\UpdateAssistant",
        "Microsoft\\Windows\\WaaSMedic",
        "Microsoft\\Windows\\WindowsUpdate",
        "Microsoft\\WindowsUpdate",
    ];
}
