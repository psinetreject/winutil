using WinUtil.Core.Abstractions;

namespace WinUtil.Core.Tweaks.Actions;

/// <summary>
/// Disables Windows AI (Copilot / Recall): remove the Copilot + CoreAI + OfficeHub Appx packages for
/// all users, uninstall the winget Copilot app, disable WSAIFabricSvc, and turn off the Recall optional
/// feature via DISM. Complex (DEL · IRR · NOUNDO).
/// </summary>
public sealed class WindowsAIAction : ICustomTweakAction
{
    public string Id => "WPFTweaksWindowsAI";
    public bool SupportsUndo => false;

    // *Copilot* Appx packages removed for all users. PARITY: the PS uses a `*Copilot*` wildcard against
    // the live package list; the platform-agnostic IAppxManager cannot enumerate by wildcard, so the
    // known package identities are removed explicitly. Verify the set against a current Windows build.
    private static readonly string[] CopilotPackages =
    [
        "Microsoft.Copilot",
        "Microsoft.Windows.Ai.Copilot.Provider",
        "MicrosoftWindows.Client.CoreAI",
    ];

    public async Task<OperationResult> ApplyAsync(TweakActionContext context)
    {
        var ct = context.CancellationToken;

        // PARITY: PS additionally writes HKLM\...\Appx\AppxAllUserStore\EndOfLife\<UserSID>\<CoreAI
        // PackageFullName> to stop CoreAI resurrecting. That needs the current-user SID and the package
        // full name — both Windows-only APIs (WindowsIdentity / Get-AppxPackage) unavailable in the
        // platform-agnostic Core. Left for a Platform-side enhancement; the removals below are the
        // functional core of the tweak.

        foreach (var package in CopilotPackages)
        {
            await context.Appx.RemoveAsync(package, removeProvisioned: true, ct);
        }

        // winget-installed Copilot app.
        await context.Process.RunAsync(
            "winget.exe",
            ["uninstall", "-e", "--name", "Copilot", "--silent", "--force", "--accept-source-agreements"],
            ct);

        await context.Appx.RemoveAsync("Microsoft.MicrosoftOfficeHub", removeProvisioned: true, ct);

        context.Services.SetStartupType("WSAIFabricSvc", ServiceStartupType.Disabled);

        // Disable Windows Recall. No managed disable-feature abstraction exists (IWindowsFeatureService
        // only enables), so DISM is invoked directly — equivalent to
        // Disable-WindowsOptionalFeature -FeatureName Recall -Online -NoRestart.
        await context.Process.RunAsync(
            "dism.exe", ["/Online", "/Disable-Feature", "/FeatureName:Recall", "/NoRestart"], ct);

        return OperationResult.Ok("Windows AI disabled");
    }

    public Task<OperationResult> UndoAsync(TweakActionContext context)
        => Task.FromResult(OperationResult.Ok("Windows AI tweak has no undo"));
}
