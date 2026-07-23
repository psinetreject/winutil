using System.Management;
using Microsoft.Extensions.Logging;
using WinUtil.Core.Abstractions;

namespace WinUtil.Platform.Provisioning;

/// <summary>
/// Creates System Restore checkpoints via srclient's <c>SRSetRestorePoint</c>. System Protection is
/// enabled on the system drive first (it is off by default on many SKUs) and the 24-hour throttle
/// is cleared so the checkpoint is actually written.
/// </summary>
public sealed class SystemRestoreService(ILogger<SystemRestoreService> logger) : ISystemRestoreService
{
    private const string DefaultNamespace = @"\\.\root\default";
    private const uint HKeyLocalMachine = 0x80000002;
    private const string SystemRestoreKey =
        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore";

    private readonly ILogger<SystemRestoreService> _logger = logger;

    public async Task<OperationResult> CreateRestorePointAsync(
        string description,
        CancellationToken cancellationToken = default)
    {
        string label = string.IsNullOrWhiteSpace(description) ? "WinUtil Restore Point" : description.Trim();

        return await Task.Run(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                EnableSystemProtection();
                ClearCreationThrottle();

                cancellationToken.ThrowIfCancellationRequested();

                var info = new NativeMethods.RESTOREPOINTINFO
                {
                    dwEventType = NativeMethods.BeginSystemChange,
                    dwRestorePtType = NativeMethods.RestorePointModifySettings,
                    llSequenceNumber = 0,
                    szDescription = label.Length > 255 ? label[..255] : label,
                };

                if (!NativeMethods.SRSetRestorePoint(ref info, out NativeMethods.STATEMGRSTATUS status))
                {
                    _logger.LogError(
                        "SRSetRestorePoint (begin) failed with status {Status}.", status.nStatus);
                    return OperationResult.Fail(
                        $"Could not create the restore point (status {status.nStatus}).");
                }

                // Close the change bracket using the sequence number handed back by the begin call.
                var end = new NativeMethods.RESTOREPOINTINFO
                {
                    dwEventType = NativeMethods.EndSystemChange,
                    dwRestorePtType = NativeMethods.RestorePointModifySettings,
                    llSequenceNumber = status.llSequenceNumber,
                    szDescription = label.Length > 255 ? label[..255] : label,
                };
                _ = NativeMethods.SRSetRestorePoint(ref end, out _);

                _logger.LogInformation(
                    "Created System Restore point '{Description}' (sequence {Sequence}).",
                    label, status.llSequenceNumber);
                return OperationResult.Ok($"Restore point '{label}' created.");
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Restore-point creation was cancelled.");
                return OperationResult.Fail("Restore-point creation was cancelled.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create System Restore point '{Description}'.", label);
                return OperationResult.Fail($"Failed to create restore point: {ex.Message}");
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Turns on System Protection for the system drive via the SystemRestore WMI class.</summary>
    private void EnableSystemProtection()
    {
        try
        {
            using ManagementClass srClass = new(DefaultNamespace, "SystemRestore", null);
            using ManagementBaseObject inParams = srClass.GetMethodParameters("Enable");
            inParams["Drive"] = @"C:\";
            using ManagementBaseObject outParams = srClass.InvokeMethod("Enable", inParams, null);

            uint rc = Convert.ToUInt32(outParams?["ReturnValue"] ?? 0u);
            if (rc != 0)
            {
                _logger.LogWarning("SystemRestore.Enable returned {Code}; continuing anyway.", rc);
            }
        }
        catch (Exception ex)
        {
            // Non-fatal: protection may already be enabled, or the SKU may not expose the class.
            _logger.LogWarning(ex, "Could not enable System Protection on C:; continuing anyway.");
        }
    }

    /// <summary>
    /// Sets SystemRestorePointCreationFrequency to 0 so Windows does not skip the checkpoint just
    /// because one was created in the last 24 hours. Uses StdRegProv to avoid a hard dependency on
    /// Microsoft.Win32.Registry.
    /// </summary>
    private void ClearCreationThrottle()
    {
        try
        {
            using ManagementClass reg = new(DefaultNamespace, "StdRegProv", null);
            using ManagementBaseObject inParams = reg.GetMethodParameters("SetDWORDValue");
            inParams["hDefKey"] = HKeyLocalMachine;
            inParams["sSubKeyName"] = SystemRestoreKey;
            inParams["sValueName"] = "SystemRestorePointCreationFrequency";
            inParams["uValue"] = 0u;
            using ManagementBaseObject outParams = reg.InvokeMethod("SetDWORDValue", inParams, null);

            uint rc = Convert.ToUInt32(outParams?["ReturnValue"] ?? 0u);
            if (rc != 0)
            {
                _logger.LogWarning(
                    "Could not clear the restore-point creation throttle (code {Code}).", rc);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not clear the restore-point creation throttle; continuing anyway.");
        }
    }
}
