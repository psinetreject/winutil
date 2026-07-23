# WinUtil — .NET 10 port

A ground-up rewrite of Chris Titus Tech's WinUtil from single-file PowerShell + WPF
into a .NET 10 WPF application.

## Locked architecture
- **.NET 10 (LTS), WPF** (`net10.0-windows`) — reuses the existing XAML/theming concepts.
- **Pure-C# system layer** — native APIs / P/Invoke / CsWinRT / ManagedDism / WinGet COM.
  No PowerShell runtime dependency. (Chocolatey stays a CLI process; it has no managed API.)
- **Delivery:** signed, self-contained single-file exe.
- **Strategy:** big-bang rewrite to parity.

## Projects
| Project | Role |
|---|---|
| `src/WinUtil.Core` | Platform-agnostic domain models + embedded-JSON config loading (`net10.0`) |
| `src/WinUtil.Platform` | Windows system-manipulation services — M1+ (`net10.0-windows`) |
| `src/WinUtil.App` | WPF UI (MVVM), DI host, theming, elevation manifest (`net10.0-windows`) |
| `tests/WinUtil.Tests` | xUnit (`net10.0`) |

The JSON configs are **embedded from the repo's top-level `config/` directory** (single
source of truth during migration). `tweaks.json` / `feature.json` embed imperative
PowerShell and are non-strict JSON; they are migrated to a typed schema in **M3** —
see [`docs/tweaks-inventory.md`](docs/tweaks-inventory.md) for the per-tweak port work-list.

## Milestones
- **M0** ✅ scaffold: solution, config models + loader, DI host, themed shell
- **M1** platform foundations (registry, services, elevation, taskbar progress)
- **M2** install (winget COM spike + choco + Appx)
- **M3** tweaks / fixes / updates (re-port InvokeScripts to C# actions)
- **M4** features / DNS / Ultimate Perf / presets / headless CLI
- **M5** MicroWin ISO + USB
- **M6** parity audit, packaging, code-signing

## Build (Windows only)
```powershell
cd dotnet
dotnet build
dotnet test
dotnet publish src/WinUtil.App -c Release -r win-x64 --self-contained `
  -p:PublishSingleFile=true -p:PublishReadyToRun=true
```
