using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using WinUtil.Core.Abstractions;

namespace WinUtil.Platform.Provisioning;

/// <summary>
/// Manages the hidden "Ultimate Performance" power plan via powrprof P/Invoke, and restores the
/// default plan set. Restoring defaults has no single stable native entry point, so it shells out
/// to <c>powercfg /restoredefaultschemes</c>.
/// </summary>
public sealed class PowerPlanService(ILogger<PowerPlanService> logger) : IPowerPlanService
{
    // Well-known GUID of the built-in "Ultimate Performance" scheme template.
    private static readonly Guid UltimatePerformanceScheme =
        new("e9a42b02-d5df-448d-aa00-03f14749eb61");

    private readonly ILogger<PowerPlanService> _logger = logger;

    public OperationResult EnableUltimatePerformance()
    {
        IntPtr duplicated = IntPtr.Zero;
        try
        {
            Guid source = UltimatePerformanceScheme;

            // PowerDuplicateScheme materializes the (normally hidden) template into a concrete,
            // activatable scheme and hands back a pointer to its freshly-minted GUID.
            uint dupResult = NativeMethods.PowerDuplicateScheme(IntPtr.Zero, ref source, out duplicated);
            if (dupResult != NativeMethods.ErrorSuccess || duplicated == IntPtr.Zero)
            {
                _logger.LogError(
                    "PowerDuplicateScheme failed for Ultimate Performance (code {Code}).", dupResult);
                return OperationResult.Fail(
                    $"Could not create the Ultimate Performance plan (error {dupResult}).");
            }

            Guid activeScheme = Marshal.PtrToStructure<Guid>(duplicated);

            uint setResult = NativeMethods.PowerSetActiveScheme(IntPtr.Zero, ref activeScheme);
            if (setResult != NativeMethods.ErrorSuccess)
            {
                _logger.LogError("PowerSetActiveScheme failed (code {Code}).", setResult);
                return OperationResult.Fail(
                    $"Created the Ultimate Performance plan but could not activate it (error {setResult}).");
            }

            _logger.LogInformation(
                "Ultimate Performance plan created and activated ({Scheme}).", activeScheme);
            return OperationResult.Ok("Ultimate Performance power plan enabled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enable the Ultimate Performance power plan.");
            return OperationResult.Fail($"Failed to enable Ultimate Performance: {ex.Message}");
        }
        finally
        {
            if (duplicated != IntPtr.Zero)
            {
                NativeMethods.LocalFree(duplicated);
            }
        }
    }

    public OperationResult RestoreDefaultPlans()
    {
        // Trade-off: there is no documented single-call native API that recreates the full default
        // scheme set the way `powercfg /restoredefaultschemes` does, so we shell out instead of
        // P/Invoking. The cost is a child process and dependency on powercfg.exe being present.
        try
        {
            (int exitCode, string output) = RunProcess("powercfg.exe", "/restoredefaultschemes");
            if (exitCode != 0)
            {
                _logger.LogError(
                    "powercfg /restoredefaultschemes exited with {ExitCode}: {Output}", exitCode, output);
                return OperationResult.Fail(
                    $"Restoring default power plans failed (exit code {exitCode}).");
            }

            _logger.LogInformation("Default power plans restored.");
            return OperationResult.Ok("Default power plans restored.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restore default power plans.");
            return OperationResult.Fail($"Failed to restore default power plans: {ex.Message}");
        }
    }

    private static (int ExitCode, string Output) RunProcess(string fileName, string arguments)
    {
        using Process process = new();
        process.StartInfo = new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        process.Start();
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        string combined = string.Concat(stdout, stderr).Trim();
        return (process.ExitCode, combined);
    }
}
