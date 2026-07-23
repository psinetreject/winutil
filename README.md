# WinUtil (.NET 10 rewrite)

> **Unofficial community rewrite.** A reimplementation of Chris Titus Tech's
> [WinUtil](https://github.com/ChrisTitusTech/winutil) as a **.NET 10 / C# (WPF)** application.
> **Not affiliated with, endorsed by, or maintained by Chris Titus Tech / CT Tech Group LLC.**

A Windows setup / debloat / tweak utility — install apps, apply tweaks, manage Windows Update, and
build custom Windows 11 install media — rebuilt from the original single-file PowerShell tool into a
typed, testable, layered .NET application with **no PowerShell dependency**.

## Build & run (Windows)

Requires the **.NET 10 SDK** on Windows (WPF is Windows-only):

```powershell
dotnet build -c Release
dotnet run --project src/WinUtil.App -c Release   # runs elevated (admin)
```

See [`docs/`](docs/) for build notes, parity status, and the per-tweak port inventory.

## Repository layout

| Path | What it is |
|---|---|
| [`src/WinUtil.Core`](src/WinUtil.Core) | Platform-agnostic domain: config models + loaders, tweak engine + custom actions |
| [`src/WinUtil.Platform`](src/WinUtil.Platform) | Pure-C# Windows layer: registry, services, winget/choco, Appx (WinRT), DISM, power, DNS, restore, tasks, MicroWin ISO |
| [`src/WinUtil.App`](src/WinUtil.App) | WPF MVVM UI (Install / Tweaks / Config / Updates) + theming |
| [`tests/WinUtil.Tests`](tests/WinUtil.Tests) | xUnit |
| [`config/`](config/) | Embedded JSON (apps, tweaks, appx, dns, presets, themes) + `autounattend.xml` |

## Status

Approaching parity with the original: the install flow, tweak engine, and most of the UI work; some
fixes/updates backends and the Win11-ISO / AppX views are still in progress. Details in
[`docs/PARITY-STATUS.md`](docs/PARITY-STATUS.md).

## Credits & license

Reuses the original's configuration data (application catalog, tweak/appx/feature definitions) and
`autounattend.xml`, and reimplements its feature set in C#. The original is MIT-licensed, © 2022 CT
Tech Group LLC — see [`LICENSE`](LICENSE) and [`NOTICE`](NOTICE). New code is likewise MIT.
