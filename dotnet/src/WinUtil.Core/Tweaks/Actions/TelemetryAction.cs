using WinUtil.Core.Abstractions;

namespace WinUtil.Core.Tweaks.Actions;

/// <summary>
/// Reduces telemetry: opt out of Defender sample submission, disable the DiagTrack and WER services,
/// opt out of PowerShell 7 telemetry (machine env var), and clear the Feedback-frequency value.
/// Moderate (SEC).
/// </summary>
public sealed class TelemetryAction : ICustomTweakAction
{
    public string Id => "WPFTweaksTelemetry";
    public bool SupportsUndo => true;

    // Machine environment is the Session Manager\Environment key; changes need a settings broadcast to
    // be seen by already-running processes.
    private const string MachineEnvPath =
        "HKLM:\\SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Environment";

    // PARITY: PS uses `Set-MpPreference -SubmitSamplesConsent` (Defender WMI, unavailable in Core). The
    // SpyNet policy value is the closest managed equivalent, but Tamper Protection can block it — verify
    // the effect on a protected machine, or move this to a Platform-side Defender WMI helper.
    private const string SpyNetPath = "HKLM:\\SOFTWARE\\Microsoft\\Windows Defender\\SpyNet";

    public Task<OperationResult> ApplyAsync(TweakActionContext context)
    {
        // 2 = "Never send" auto sample submission.
        context.Registry.SetValue(SpyNetPath, "SubmitSamplesConsent", "2", RegistryValueKind.DWord);

        context.Services.SetStartupType("diagtrack", ServiceStartupType.Disabled);
        context.Services.SetStartupType("wermgr", ServiceStartupType.Disabled);

        context.Registry.SetValue(MachineEnvPath, "POWERSHELL_TELEMETRY_OPTOUT", "1", RegistryValueKind.String);
        context.Explorer.BroadcastSettingChange();

        context.Registry.DeleteValue("HKCU:\\Software\\Microsoft\\Siuf\\Rules", "PeriodInNanoSeconds");

        return Task.FromResult(OperationResult.Ok("Telemetry reduced"));
    }

    public Task<OperationResult> UndoAsync(TweakActionContext context)
    {
        context.Registry.SetValue(SpyNetPath, "SubmitSamplesConsent", "1", RegistryValueKind.DWord);

        context.Services.SetStartupType("diagtrack", ServiceStartupType.Automatic);
        context.Services.SetStartupType("wermgr", ServiceStartupType.Automatic);

        // PS sets the env var to '' which removes it; deleting the value is the equivalent.
        context.Registry.DeleteValue(MachineEnvPath, "POWERSHELL_TELEMETRY_OPTOUT");
        context.Explorer.BroadcastSettingChange();

        return Task.FromResult(OperationResult.Ok("Telemetry settings restored"));
    }
}
