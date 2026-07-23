using System.Diagnostics;
using Microsoft.Extensions.Logging;
using WinUtil.Core.Abstractions;

namespace WinUtil.Platform.System;

/// <summary>
/// Notifies the shell of settings changes and restarts Explorer after tweaks.
/// Mirrors the PowerShell tool's Invoke-WinUtilExplorerUpdate. Never throws.
/// </summary>
public sealed class ExplorerRefresher : IExplorerRefresher
{
    private readonly ILogger<ExplorerRefresher> _logger;

    public ExplorerRefresher(ILogger<ExplorerRefresher> logger) => _logger = logger;

    public void BroadcastSettingChange()
    {
        try
        {
            // WM_SETTINGCHANGE with "ImmersiveColorSet" prompts the shell to re-read theme/UI
            // settings without a restart. SMTO_ABORTIFHUNG keeps a hung window from blocking us.
            _ = NativeMethods.SendMessageTimeout(
                NativeMethods.HWND_BROADCAST,
                NativeMethods.WM_SETTINGCHANGE,
                IntPtr.Zero,
                "ImmersiveColorSet",
                NativeMethods.SMTO_ABORTIFHUNG,
                100,
                out _);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to broadcast WM_SETTINGCHANGE");
        }
    }

    public void RestartExplorer()
    {
        foreach (Process explorer in Process.GetProcessesByName("explorer"))
        {
            try
            {
                explorer.Kill();
                explorer.WaitForExit(5000);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to stop an explorer.exe instance");
            }
            finally
            {
                explorer.Dispose();
            }
        }

        // Relaunch the shell in case Windows did not respawn it automatically. Use CreateProcess
        // (UseShellExecute = false); ShellExecute routes through the shell we just killed and can hang
        // until it respawns — which previously froze the tweak operation indefinitely.
        try
        {
            var explorerPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            Process.Start(new ProcessStartInfo(explorerPath) { UseShellExecute = false })?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to relaunch explorer.exe");
        }
    }
}
