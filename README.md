# WinUtil (.NET 10 rewrite)

> **Unofficial community rewrite.** A reimplementation of Chris Titus Tech's
> [WinUtil](https://github.com/ChrisTitusTech/winutil) as a **.NET 10 / C# (WPF)** application.
> **Not affiliated with, endorsed by, or maintained by Chris Titus Tech / CT Tech Group LLC.**

A Windows setup / debloat / tweak utility — install apps, apply tweaks, manage Windows Update, and
build custom Windows 11 install media — rebuilt from the original single-file PowerShell tool into a
typed, testable, layered .NET application.

## Build & run (Windows)

The .NET solution lives in **[`dotnet/`](dotnet/)** (requires the .NET 10 SDK on Windows):

```powershell
cd dotnet
dotnet build -c Release
dotnet run --project src/WinUtil.App -c Release   # runs elevated (admin)
```

See [`dotnet/README.md`](dotnet/README.md) and [`dotnet/docs/`](dotnet/docs/) — build notes, parity
status, and the per-tweak port inventory.

## Repository layout

| Path | What it is |
|---|---|
| **[`dotnet/`](dotnet/)** | The .NET 10 rewrite — `Core` (domain + config) · `Platform` (Windows services) · `App` (WPF UI) · `Tests` |
| **[`legacy/`](legacy/)** | The original PowerShell WinUtil, kept for reference |

## Status

Approaching parity with the original: the install flow, tweak engine, and most of the UI work;
some fixes/updates backends and the Win11-ISO / AppX views are still in progress. Details in
[`dotnet/docs/PARITY-STATUS.md`](dotnet/docs/PARITY-STATUS.md).

## Credits & license

This project reuses the original's configuration data (application catalog, tweak/appx/feature
definitions) and `autounattend.xml`, and reimplements its feature set in C#. The original is
MIT-licensed, © 2022 CT Tech Group LLC — see [`LICENSE`](LICENSE) and [`NOTICE`](NOTICE) for
attribution. New code in `dotnet/` is likewise available under the MIT License.
