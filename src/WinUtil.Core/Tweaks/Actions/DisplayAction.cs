using WinUtil.Core.Abstractions;

namespace WinUtil.Core.Tweaks.Actions;

/// <summary>
/// Sets Windows visual effects to "best performance". The bulk of the tweak is the declarative
/// <c>registry</c> array (applied by the engine before this runs); this action only writes the packed
/// <c>UserPreferencesMask</c> binary blob the PowerShell InvokeScript set. Moderate. Undo removes the
/// value.
/// </summary>
public sealed class DisplayAction : ICustomTweakAction
{
    public string Id => "WPFTweaksDisplay";
    public bool SupportsUndo => true;

    private const string DesktopPath = "HKCU:\\Control Panel\\Desktop";

    // PS wrote [byte[]](144,18,3,128,16,0,0,0) as a REG_BINARY. As a hex string those eight bytes are
    // 90 12 03 80 10 00 00 00; IRegistryService parses hex digits into the byte[] payload.
    private const string UserPreferencesMaskHex = "9012038010000000";

    public Task<OperationResult> ApplyAsync(TweakActionContext context)
    {
        var result = context.Registry.SetValue(
            DesktopPath, "UserPreferencesMask", UserPreferencesMaskHex, RegistryValueKind.Binary);

        return Task.FromResult(result.Success
            ? OperationResult.Ok("Visual effects set to best performance")
            : result);
    }

    public Task<OperationResult> UndoAsync(TweakActionContext context)
    {
        // Mirrors Remove-ItemProperty -Name "UserPreferencesMask".
        var result = context.Registry.DeleteValue(DesktopPath, "UserPreferencesMask");

        return Task.FromResult(result.Success
            ? OperationResult.Ok("Visual effects restored")
            : result);
    }
}
