using WinUtil.Core.Abstractions;

namespace WinUtil.Core.Tweaks.Actions;

/// <summary>
/// Turns BitLocker off on the system drive (starts decryption). Moderate (SEC). Undo turns it back on.
/// </summary>
public sealed class DisableBitLockerAction : ICustomTweakAction
{
    public string Id => "WPFTweaksDisableBitLocker";
    public bool SupportsUndo => true;

    private static string SystemDrive => Environment.GetEnvironmentVariable("SystemDrive") ?? "C:";

    public async Task<OperationResult> ApplyAsync(TweakActionContext context)
    {
        // PARITY: PS uses `Disable-BitLocker -MountPoint $Env:SystemDrive`; manage-bde -off is the
        // no-WMI equivalent that begins volume decryption.
        var result = await context.Process.RunAsync("manage-bde.exe", ["-off", SystemDrive], context.CancellationToken);
        return result.Succeeded
            ? OperationResult.Ok($"BitLocker decryption started on {SystemDrive}")
            : OperationResult.Fail($"manage-bde -off exited {result.ExitCode}: {result.StandardError}");
    }

    public async Task<OperationResult> UndoAsync(TweakActionContext context)
    {
        // PARITY: PS uses `Enable-BitLocker`, which provisions default (TPM) protectors. `manage-bde -on`
        // begins encryption but a machine with no protector configured may require one to be added first
        // (e.g. `manage-bde -protectors -add`). Verify against the target's protector policy.
        var result = await context.Process.RunAsync("manage-bde.exe", ["-on", SystemDrive], context.CancellationToken);
        return result.Succeeded
            ? OperationResult.Ok($"BitLocker encryption started on {SystemDrive}")
            : OperationResult.Fail($"manage-bde -on exited {result.ExitCode}: {result.StandardError}");
    }
}
