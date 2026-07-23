using WinUtil.Core.Abstractions;

namespace WinUtil.Core.Tweaks.Actions;

/// <summary>
/// Disables IPv6. Moderate. Apply unbinds/disables IPv6; Undo restores it.
/// </summary>
public sealed class DisableIPv6Action : ICustomTweakAction
{
    public string Id => "WPFTweaksDisableIPv6";
    public bool SupportsUndo => true;

    // Global IPv6 disable flag consumed by tcpip6 at boot. 0xFF disables all IPv6 components (incl.
    // tunnel interfaces and prefix policies).
    private const string Tcpip6ParametersPath =
        "HKLM:\\SYSTEM\\CurrentControlSet\\Services\\Tcpip6\\Parameters";

    public Task<OperationResult> ApplyAsync(TweakActionContext context)
    {
        // PARITY: PS runs `Disable-NetAdapterBinding -Name * -ComponentID ms_tcpip6`, a per-adapter
        // unbind driven by WMI (MSFT_NetAdapterBindingSettingData) that has no netsh/managed-in-Core
        // equivalent. As a portable stand-in this writes the global DisabledComponents flag, which
        // disables IPv6 after a reboot. A Platform-side WMI implementation should replace this for exact
        // parity (the per-adapter unbind takes effect without a reboot). Note: on this tweak the config
        // `registry` array may already set DisabledComponents; this write is idempotent with it.
        var result = context.Registry.SetValue(
            Tcpip6ParametersPath, "DisabledComponents", "255", RegistryValueKind.DWord);

        return Task.FromResult(result.Success
            ? OperationResult.Ok("IPv6 disabled (effective after reboot)")
            : result);
    }

    public Task<OperationResult> UndoAsync(TweakActionContext context)
    {
        // PARITY: mirrors `Enable-NetAdapterBinding`. Clearing DisabledComponents (0) re-enables IPv6.
        var result = context.Registry.SetValue(
            Tcpip6ParametersPath, "DisabledComponents", "0", RegistryValueKind.DWord);

        return Task.FromResult(result.Success
            ? OperationResult.Ok("IPv6 re-enabled (effective after reboot)")
            : result);
    }
}
