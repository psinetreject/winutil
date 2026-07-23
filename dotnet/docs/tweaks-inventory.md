# WinUtil Tweaks Porting Inventory (PowerShell → C#)

Definitive work-list for porting winutil's imperative tweak logic (`InvokeScript` / `UndoScript`)
from embedded PowerShell to pure C#/.NET. Derived from a full read of `config/tweaks.json`
and `config/feature.json`.

Only entries that carry an `InvokeScript` and/or `UndoScript` key are listed here. Pure-`registry`,
pure-`service`, pure-`feature`, and `function`-backed button entries (which are handled by the
generic registry/service/feature/function processors, not by embedded script) are intentionally omitted.

Flag legend: **DL** = downloads from the internet · **DEL** = deletes files/dirs ·
**SEC** = disables/weakens a security feature · **IRR** = effectively irreversible ·
**NOUNDO** = no `UndoScript` provided.

---

## Summary

- **Total entries with an `InvokeScript` and/or `UndoScript`: 46**
  - 43 have a non-empty `InvokeScript`; **3** carry an *empty* `InvokeScript: []`
    (`WPFFeaturesdotnet`, `WPFFeatureslegacymedia`, `WPFFeaturewsl` — no imperative logic to port;
    their real work is DISM feature enablement handled by the shared `feature` array processor).
- **Entries with an `UndoScript`: 19**
- **Entries with NO undo: 27** (of which ~10 make persistent/destructive changes that arguably
  warrant an undo path; the 14 Legacy-Panel entries and 3 empty feature entries do not, since they
  only launch UI or defer to the DISM path).
- **Entries flagged irreversible / destructive-with-no-undo: 8**
  `WPFTweaksWidget`, `WPFTweaksRemoveEdge`, `WPFTweaksRemoveOneDrive`, `WPFTweaksWindowsAI`,
  `WPFTweaksDiskCleanup`, `WPFTweaksDeleteTempFiles`, `WPFTweaksReservedStorage`,
  `WPFTweaksRestorePoint` (restore point creation itself is benign, but it has no undo).

### ~10 highest-effort entries to port

1. **WPFTweaksRemoveOneDrive** — ACL deny/grant, run OneDrive uninstaller, kill Explorer/FileCoAuth, recursive dir deletes, env-var + service changes, winget reinstall on undo.
2. **WPFTweaksWindowsAI** — Appx enumeration + per-SID EndOfLife registry key, Appx removal (all users), winget uninstall, service disable, **DISM** `Disable-WindowsOptionalFeature Recall`.
3. **WPFTweaksRemoveEdge** — resolve versioned setup.exe glob, plant dummy exe to unlock uninstaller, run system-level uninstall, winget reinstall on undo.
4. **WPFTweaksRestorePoint** — `Enable-ComputerRestore` + `Checkpoint-Computer` (SystemRestore API) plus frequency registry override.
5. **WPFFeatureRegBackup** — registry policy values **plus** register a daily **Scheduled Task** running as SYSTEM.
6. **WPFTweaksDiskCleanup** — `cleanmgr /VERYLOWDISK` + **DISM** `StartComponentCleanup /ResetBase` (irreversible).
7. **WPFTweaksWidget** — stop Widgets process, remove two Appx packages for all users, restart Explorer.
8. **WPFTweaksBlockAdobeNet** — **download** a hosts blocklist over HTTPS, append to system `hosts`, flush DNS; regex-strip on undo.
9. **WPFTweaksReservedStorage** — **DISM** `/Set-ReservedStorageState`.
10. **WPFTweaksTelemetry** — Defender `Set-MpPreference`, disable two services, set a machine env var, remove a registry property (multi-surface).

---

## Trivial

Single simple operation: one process launch, one registry write, or a UI broadcast/refresh.

| id | InvokeScript does | UndoScript does | C# target | flags |
|----|-------------------|-----------------|-----------|-------|
| `WPFTweaksHiber` | `powercfg.exe /hibernate off` | `powercfg.exe /hibernate on` | Process wrapper (or `powrprof.dll` P/Invoke). Registry `HibernateEnabled`/`ShowHibernateOption` handled by `registry` array. | |
| `WPFTweaksDisableStoreSearch` | `icacls` **deny** `Everyone:F` on the Store `store.db` file. | `icacls` **grant** `Everyone:F` back. | System.IO + `System.Security.AccessControl.FileSecurity` (or Process wrapper for icacls). | |
| `WPFTweaksDisplay` | Writes binary `UserPreferencesMask` under `HKCU:\Control Panel\Desktop` (byte[] 144,18,3,128,16,0,0,0). | Removes the `UserPreferencesMask` property. | `Microsoft.Win32.RegistryKey` (Binary value). | |
| `WPFTweaksTeredo` | `netsh interface teredo set state disabled`. | `netsh interface teredo set state default`. | Process wrapper (`netsh`). Registry `DisabledComponents` via `registry` array. | |
| `WPFTweaksRightClickMenu` | Creates empty `HKCU:\...\CLSID\{86ca1aa0-...}\InprocServer32` key, then `Stop-Process explorer`. | Recursively removes the CLSID key. | `Microsoft.Win32.RegistryKey` + Process kill/`SendMessageTimeout` refresh. | |
| `WPFToggleDarkMode` | `Invoke-WinUtilExplorerUpdate`; if theme button is auto, `Invoke-WinutilThemeChange -theme Auto` (app-internal). | Same as invoke. | `SendMessageTimeout` broadcast + bespoke app theme call. Registry theme keys via `registry` array. | |
| `WPFToggleShowExt` | `Invoke-WinUtilExplorerUpdate -action restart` (restart Explorer). | Same. | `SendMessageTimeout` P/Invoke + Explorer restart. | |
| `WPFToggleHiddenFiles` | Restart Explorer. | Same. | `SendMessageTimeout` P/Invoke + Explorer restart. | |
| `WPFToggleStartMenuRecommendations` | Restart Explorer. | Same. | `SendMessageTimeout` P/Invoke + Explorer restart. | |
| `WPFToggleTaskbarAlignment` | Restart Explorer. | Same. | `SendMessageTimeout` P/Invoke + Explorer restart. | |
| `WPFFeatureEnableLegacyRecovery` | `bcdedit /set bootmenupolicy legacy`. | none | Process wrapper (`bcdedit`). | NOUNDO |
| `WPFFeatureDisableLegacyRecovery` | `bcdedit /set bootmenupolicy standard`. | none | Process wrapper (`bcdedit`). | NOUNDO |
| `WPFPanelComputer` | Launches `compmgmt.msc`. | none | Process wrapper. | NOUNDO |
| `WPFPanelControl` | Launches `control` (Control Panel). | none | Process wrapper. | NOUNDO |
| `WPFPanelMouse` | Launches `main.cpl`. | none | Process wrapper. | NOUNDO |
| `WPFPanelNetwork` | Launches `ncpa.cpl`. | none | Process wrapper. | NOUNDO |
| `WPFPanelPower` | Launches `powercfg.cpl`. | none | Process wrapper. | NOUNDO |
| `WPFPanelPrinter` | `Start-Process 'shell:::{A8A91A66-3A7D-4424-8D24-04E180695C7A}'` (Devices & Printers). | none | Process wrapper (shell verb). | NOUNDO |
| `WPFPanelPrograms` | Launches `appwiz.cpl`. | none | Process wrapper. | NOUNDO |
| `WPFPanelRegion` | Launches `intl.cpl`. | none | Process wrapper. | NOUNDO |
| `WPFPanelSecurity` | Launches `wscui.cpl`. | none | Process wrapper. | NOUNDO |
| `WPFPanelSound` | Launches `mmsys.cpl`. | none | Process wrapper. | NOUNDO |
| `WPFPanelSystem` | Launches `sysdm.cpl`. | none | Process wrapper. | NOUNDO |
| `WPFPanelTimedate` | Launches `timedate.cpl`. | none | Process wrapper. | NOUNDO |
| `WPFPanelFirewall` | Launches `firewall.cpl`. | none | Process wrapper. | NOUNDO |
| `WPFPanelRestore` | Launches `rstrui.exe` (System Restore UI). | none | Process wrapper. | NOUNDO |
| `WPFFeaturesdotnet` | *empty* `InvokeScript`. Actual work: enable optional features `NetFx4-AdvSrvs`, `NetFx3`. | none | No script to port. Feature enablement → **ManagedDism** via shared `feature` array. | NOUNDO |
| `WPFFeatureslegacymedia` | *empty* `InvokeScript`. Features: `WindowsMediaPlayer`, `MediaPlayback`, `DirectPlay`, `LegacyComponents`. | none | No script to port. Feature enablement → **ManagedDism**. | NOUNDO |
| `WPFFeaturewsl` | *empty* `InvokeScript`. Features: `VirtualMachinePlatform`, `Microsoft-Windows-Subsystem-Linux`. | none | No script to port. Feature enablement → **ManagedDism**. | NOUNDO |

---

## Moderate

Multiple operations, a WMI/CIM query, ACL manipulation, or recursive registry/file work — but no download, scheduled task, DISM, or disk-partition operation.

| id | InvokeScript does | UndoScript does | C# target | flags |
|----|-------------------|-----------------|-----------|-------|
| `WPFTweaksServices` | Queries total physical memory (`Get-CimInstance Win32_PhysicalMemory`, sum / 1KB), then sets `HKLM:\SYSTEM\CurrentControlSet\Control\SvcHostSplitThresholdInKB` to that value. (Service startup changes handled by `service` array.) | none | CIM/WMI `Win32_PhysicalMemory` query + `Microsoft.Win32.RegistryKey`. | NOUNDO |
| `WPFTweaksTelemetry` | `Set-MpPreference -SubmitSamplesConsent 2` (Defender); disable `diagtrack` and `wermgr` services; set machine env var `POWERSHELL_TELEMETRY_OPTOUT=1`; remove `HKCU:\...\Siuf\Rules\PeriodInNanoSeconds`. | Re-enable Defender sample submission (=1); set `diagtrack`/`wermgr` to Automatic; clear the env var. | Defender: WMI `MSFT_MpPreference` (or `Set-MpPreference` interop). Services: `ServiceController` + `ChangeServiceConfig` P/Invoke. Env var: `Environment.SetEnvironmentVariable(...,Machine)`. Registry: `RegistryKey`. | SEC |
| `WPFTweaksDisableBitLocker` | `Disable-BitLocker -MountPoint $Env:SystemDrive` (starts volume decryption). | `Enable-BitLocker -MountPoint $Env:SystemDrive`. | WMI `Win32_EncryptableVolume` (or `manage-bde` Process wrapper). | SEC |
| `WPFTweaksDisableIPv6` | `Disable-NetAdapterBinding -Name * -ComponentID ms_tcpip6` (unbinds IPv6 on all adapters). | `Enable-NetAdapterBinding -Name * -ComponentID ms_tcpip6`. | CIM/WMI `MSFT_NetAdapterBindingSettingData` (or `netsh` Process wrapper). Registry `DisabledComponents` via `registry` array. | |
| `WPFTweaksRazerBlock` | Ensures `%SystemRoot%\Installer\Razer` exists (create dir, or delete its contents if present), then `icacls /deny Everyone:(W)` to block writes. | `icacls /remove:d Everyone` on that folder. | System.IO (create/clear dir) + `DirectorySecurity` ACL (or icacls Process wrapper). | DEL |
| `WPFTweaksDeleteTempFiles` | Recursively deletes `%Temp%\*` and `%SystemRoot%\Temp\*`. | none | System.IO recursive delete (with locked-file tolerance). | DEL · IRR · NOUNDO |
| `WPFTweaksDisableExplorerAutoDiscovery` | Recursively deletes the Explorer `Bags` and `BagMRU` keys under `HKCU:\...\Shell`, creates `Bags\AllFolders\Shell`, and sets its `FolderType` string to `NotSpecified`. | Recursively deletes `Bags` and `BagMRU` only. | `Microsoft.Win32.RegistryKey` (recursive `DeleteSubKeyTree` + create + set). | |
| `WPFFeaturenfs` | `nfsadmin client stop`; set `AnonymousUID`/`AnonymousGID`=0 under `HKLM:\...\ClientForNFS\CurrentVersion\Default`; `nfsadmin client start`; `nfsadmin client localhost config fileaccess=755 SecFlavors=+sys -krb5 -krb5i`. (Also enables NFS optional features via `feature` array.) | none | Process wrapper (`nfsadmin`) + `Microsoft.Win32.RegistryKey`. Feature enablement → ManagedDism. | NOUNDO |

---

## Complex

Downloads, scheduled tasks, DISM, or multi-step orchestration.

| id | InvokeScript does | UndoScript does | C# target | flags |
|----|-------------------|-----------------|-----------|-------|
| `WPFTweaksWidget` | `Stop-Process *Widget*`; remove Appx `Microsoft.WidgetsPlatformRuntime` and `MicrosoftWindows.Client.WebExperience` for **all users**; restart Explorer. | none | WinRT `PackageManager` (CsWinRT) for Appx removal + Process kill + `SendMessageTimeout`/Explorer restart. | DEL · IRR · NOUNDO |
| `WPFTweaksRemoveEdge` | Resolves versioned `Program Files (x86)\Microsoft\Edge\Application\*\Installer\setup.exe`; plants a dummy `MicrosoftEdge.exe` in the legacy SystemApps folder to unlock the uninstaller; runs `setup.exe --uninstall --system-level --force-uninstall --delete-profile` and waits. | `winget install Microsoft.Edge --source winget`. | System.IO (glob resolve + create dummy) + Process wrapper (uninstaller). Undo: winget Process (**download**). | DEL · DL · IRR |
| `WPFTweaksRemoveOneDrive` | `icacls $OneDrive /deny Administrators:(D,DC)`; run `System32\OneDriveSetup.exe /uninstall` (wait); `Stop-Process FileCoAuth,Explorer`; recursively delete `%LocalAppData%\Microsoft\OneDrive` and `%ProgramData%\Microsoft OneDrive`; `icacls /grant` back; if OneDrive folder empty, delete it and clear the `OneDrive` user env var; disable `OneSyncSvc` service. | `winget install Microsoft.Onedrive`; set `OneSyncSvc` to Automatic. | `DirectorySecurity`/icacls + Process (uninstaller) + Process kill + System.IO recursive delete + `Environment.SetEnvironmentVariable` + `ServiceController`/`ChangeServiceConfig`. Undo: winget (**download**). | DEL · DL · IRR |
| `WPFTweaksWindowsAI` | Reads `MicrosoftWindows.Client.CoreAI` PackageFullName and current user SID; writes an `AppxAllUserStore\EndOfLife\<SID>\<pkg>` registry key; removes all `*Copilot*` Appx for all users; `winget uninstall "Copilot"`; removes `Microsoft.MicrosoftOfficeHub`; removes the CoreAI package; disables `WSAIFabricSvc` service; `Disable-WindowsOptionalFeature Recall -Online`. | none | WinRT `PackageManager` (enumerate/remove) + `RegistryKey` (SID-scoped key) + winget Process + `ServiceController`/`ChangeServiceConfig` + **ManagedDism** (Recall feature). | DEL · IRR · NOUNDO |
| `WPFTweaksDiskCleanup` | `cleanmgr.exe /d C: /VERYLOWDISK`; `Dism.exe /online /Cleanup-Image /StartComponentCleanup /ResetBase` (permanently removes superseded update components). | none | Process wrapper (`cleanmgr`) + **ManagedDism** (`StartComponentCleanup`/`ResetBase`). | IRR · NOUNDO |
| `WPFTweaksReservedStorage` | `DISM /Online /Set-ReservedStorageState /State:Disabled`. | `DISM /Online /Set-ReservedStorageState /State:Enabled`. | **ManagedDism** (reserved storage state API, or DISM Process wrapper). | |
| `WPFTweaksRestorePoint` | If no restore point exists, `Enable-ComputerRestore -Drive $SystemDrive`; then `Checkpoint-Computer -RestorePointType MODIFY_SETTINGS`. (Frequency override registry value set via `registry` array.) | none | SystemRestore WMI (`SystemRestore.Enable` / `CreateRestorePoint`) or `srclient.dll` P/Invoke. | NOUNDO |
| `WPFTweaksBlockAdobeNet` | `Invoke-RestMethod` **downloads** the Ruddernation Adobe URL block list from GitHub and appends it to `%SystemRoot%\System32\drivers\etc\hosts`; `ipconfig /flushdns`. | Regex-strips the appended block (`#New Ver...` to end) from `hosts`; `ipconfig /flushdns`. | `HttpClient` (download) + System.IO (append/rewrite hosts) + Process (`ipconfig /flushdns`). | DL |
| `WPFFeatureRegBackup` | Sets `EnablePeriodicBackup`=1 and `BackupCount`=2 under `HKLM:\...\Configuration Manager`; registers a daily **Scheduled Task** `AutoRegBackup` at 00:30 running as SYSTEM that triggers `\Microsoft\Windows\Registry\RegIdleBackup`. | none | `Microsoft.Win32.RegistryKey` + **TaskScheduler** (managed lib or COM `ITaskService`) for task registration. | NOUNDO |

---

## Notes for implementers

- **Explorer refresh vs restart.** Many toggles call `Invoke-WinUtilExplorerUpdate` (broadcast a
  settings-changed message) or `-action restart` (kill + relaunch `explorer.exe`). Model both as a
  single shared helper: `SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, ...)` for the refresh
  path, and a managed kill/relaunch for the restart path.
- **`winget` calls** in undo paths (`RemoveEdge`, `RemoveOneDrive`) and `WindowsAI` are external
  Process invocations that reach the network; treat them as downloads for consent/gating purposes.
- **The three empty-`InvokeScript` feature entries** carry no script to port. Their behavior comes
  entirely from the shared `feature` array → DISM enablement path, which is itself Complex but is
  implemented once (ManagedDism) rather than per entry.
- **`registry` / `service` arrays** attached to entries above are handled by the generic
  registry/service processors and are called out only where they interact with the script logic.
