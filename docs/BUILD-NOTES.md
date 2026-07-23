# Build notes — first Windows compile pass

This codebase was authored on Linux without a .NET SDK, so it has **not been compiled**. WPF and the
Windows system layer require Windows. Treat the first `dotnet build` on Windows as a mechanical
fix-up pass; the architecture and contracts are fixed, so errors should be local and shallow.

## Prerequisites
- Windows 10/11
- .NET 10 SDK (`10.0.x`)
- Windows SDK `10.0.22621` (for the WinRT projections used by `WinUtil.Platform`)

## Build / test / publish
```powershell
cd dotnet
dotnet restore
dotnet build -c Release
dotnet test tests/WinUtil.Tests -c Release      # Core is net10.0 — runs without Windows APIs too
dotnet publish src/WinUtil.App -c Release -r win-x64 --self-contained `
  -p:PublishSingleFile=true -p:PublishReadyToRun=true
```

## Expected fix-up areas (in likely order)
1. **NuGet versions** — the versions in `WinUtil.App.csproj` and `WinUtil.Platform.csproj`
   (`Microsoft.Dism`, `TaskScheduler`, `System.Management`, `CommunityToolkit.Mvvm`, Serilog,
   Extensions.*) are best-guess. `dotnet restore` will report the correct available versions.
2. **DI class-name reconciliation** — `PlatformServiceCollectionExtensions.AddWinUtilPlatform`
   references concrete types by the names dictated to the implementation agents
   (`RegistryService`, `ServiceManager`, `ProcessRunner`, `ElevationService`, `ExplorerRefresher`,
   `WingetPackageManager`, `ChocoPackageManager`, `PackageInstaller`, `AppxManager`,
   `WindowsFeatureService`, `PowerPlanService`, `DnsService`, `SystemRestoreService`,
   `TaskSchedulerService`, `MicroWinBuilder`, `UsbWriter`). If any subsystem named a class
   differently, fix the reference here.
3. **ManagedDism / TaskScheduler API surface** — `WinUtil.Platform/Provisioning` uses these libs;
   verify exact method names against the restored package versions.
4. **WinRT projection** — `AppxManager` uses `Windows.Management.Deployment.PackageManager`; confirm
   the `net10.0-windows10.0.22621.0` TFM resolves it (it should with the SDK installed).
5. **Source-gen JSON accessors** — `ConfigLoader` uses `WinUtilJsonContext.Default.<Name>` from
   `TypeInfoPropertyName`; confirm they generate.
6. **Ported config** — `dotnet/config/tweaks.json` and `features.json` are embedded and required by
   `ConfigLoader`; the config-integrity tests exercise them.
7. **`TreatWarningsAsErrors` is ON** — if analyzer nits block bring-up, relax it in
   `Directory.Build.props` temporarily.

## Not yet done (post-parity)
- **Code signing** — needs an OV/EV Authenticode cert; see the commented step in
  `.github/workflows/dotnet-build.yaml`. A brand-new signed exe still faces a SmartScreen reputation
  ramp; publish hashes + reproducible builds to recover some of the `irm|iex` auditability.
- **Parity audit** — search the C# for `// PARITY:` and `// TODO` markers left by the port.
- **ARM64 publish** — add a `win-arm64` publish leg once x64 is green.
