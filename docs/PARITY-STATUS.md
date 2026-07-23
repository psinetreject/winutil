# WinUtil .NET 10 port — assembled status & parity map

First-pass big-bang rewrite. **~118 files, ~7,980 lines of C#** across 4 projects. Authored without a
.NET SDK (Linux) — **not yet compiled**; see [BUILD-NOTES.md](BUILD-NOTES.md) for the first-Windows-build
fix-up checklist.

## Subsystem status

| Subsystem | State | Notes |
|---|---|---|
| Config load (7 JSON → typed records, source-gen) | ✅ complete | `tweaks.json`/`features.json` ported to strict schema; 5 others reused verbatim |
| DI / host / logging (Serilog) | ✅ complete | `AddWinUtilCore` → `AddWinUtilPlatform` → `AddWinUtilApp` |
| Registry / services / process / elevation / explorer | ✅ complete | P/Invoke + advapi32 |
| Package managers (winget CLI, choco CLI, WinRT Appx) | ✅ complete | winget COM is a future pure-API upgrade; choco stays CLI |
| Tweak engine + 41 custom actions | ✅ complete | every Checkbox/Toggle tweak the UI surfaces is covered |
| Features / power / DNS / restore / scheduled-tasks | ✅ complete | ManagedDism, powrprof, netsh, srclient, TaskScheduler |
| WPF UI (Install/Tweaks/Config/Updates) + theming | ✅ complete | MVVM, data-templated, `themes.json`-driven |
| MicroWin ISO + USB | ✅ built | Storage WMI + ManagedDism + shelled tools; 3 PS bugs fixed |
| Config-tab fixes (network/update/winget/NTP) | 🟡 stubbed | `// TODO M4` — commands exist, backends pending |
| Updates presets (Default/Security/Disable) | 🟡 stubbed | `// TODO M4` |
| Win11 ISO tab UI + AppX tab UI | 🟡 placeholder | backends done (ISO builder, `IAppxManager`); views pending |
| Headless CLI (`-Preset`/`-Config`), import/export | ❌ not started | M4 |
| Code signing, ARM64 publish | ❌ not started | M6 |

## Milestones
- **M0** ✅ scaffold · **M1** ✅ platform foundations · **M2** ✅ install · **M3** ✅ tweaks/fixes engine
- **M4** 🟡 fixes/updates wiring, headless CLI, ISO/AppX views — *partially stubbed*
- **M5** ✅ ISO builder backend (UI pending) · **M6** ❌ packaging/signing

## Tweak-engine coverage
41 `ICustomTweakAction`s implemented (9 Complex + 8 Moderate tweaks/features, 14 legacy-panel launchers,
10 checkbox/toggle gap-closers). The 16 unmatched `customActionId`s in the config are **Button/Combobox/
feature entries not routed through the engine** — the UI drives them as Config/Updates commands:

- Wired: `WPFAddUltPerf`, `WPFRemoveUltPerf` (Ultimate Perf), `WPFchangedns` (DNS combo)
- Stubbed `// TODO M4`: `WPFFixesNetwork`, `WPFFixesUpdate`, `WPFFixesWinget`, `WPFFixesNTPPool`,
  `WPFOOSUbutton`, `WPFFeatureInstall`, `WPFPanelAutologin`, `WPFPanelDISM`,
  `WPFWinUtilInstallPSProfile`, `WPFWinUtilUninstallPSProfile`, `WPFWinUtilSSHServer`,
  `WPFFeatureEnableLegacyRecovery`, `WPFFeatureDisableLegacyRecovery`

*(Cleanup: these could drop their `customActionId` from the config since they're UI-command-driven.)*

## Parity approximations to review (search the code for `// PARITY:`)
- **WindowsAI** — omits the per-SID `AppxAllUserStore\EndOfLife` key (needs live SID + PackageFullName); `*Copilot*` uses a fixed id list, not live enumeration.
- **Telemetry** — Defender `SubmitSamplesConsent` via SpyNet policy, not `Set-MpPreference` (Tamper-Protection may block).
- **DisableIPv6** — global `DisabledComponents=0xFF` (reboot) instead of per-adapter `Disable-NetAdapterBinding`.
- **ServicesAction** — RAM via `GlobalMemoryStatusEx` (a few MB under installed).
- **DisableStoreSearch** — surfaces `icacls` non-zero exit as failure (PS silently tolerated a missing db).
- **RightClickMenu / DarkMode** — port restarts Explorer / broadcasts on undo for immediate effect; drops the UI-only theme-button flip.
- **ISO** — `install.esd` in-place mount ported faithfully (may fail on ESD-only ISOs); `oscdimg -bootdata` and `reg /d` quoting depend on the process-runner's command-line building; metadata "hydration" step omitted.
- **Engine** — the PS `KeepServiceStartup` guard (skip a service the user already customized) is **not** replicated; services apply unconditionally.

## Remaining work to full parity
1. M4: wire the Config-tab fixes + Updates presets to real backends (new `IUpdatePolicyService`, `IFixesService`, `IPsProfileService`, `ISshServerService`, `IAutologinService`).
2. Build the Win11 ISO and AppX **views** over the already-implemented backends.
3. Headless `-Preset`/`-Config` CLI (System.CommandLine) + import/export.
4. Expand tests beyond config integrity (engine apply/undo, package routing, toggle state).
5. First Windows compile-fix pass (BUILD-NOTES) → code signing (M6).
