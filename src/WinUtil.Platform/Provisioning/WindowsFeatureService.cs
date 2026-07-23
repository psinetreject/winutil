using Microsoft.Dism;
using Microsoft.Extensions.Logging;
using WinUtil.Core.Abstractions;

namespace WinUtil.Platform.Provisioning;

/// <summary>
/// Enables Windows optional features through the DISM online session (ManagedDism).
/// ManagedDism is a synchronous, process-global API, so every call is marshalled onto a
/// background thread and brackets Initialize/Shutdown around a single online session.
/// </summary>
public sealed class WindowsFeatureService(ILogger<WindowsFeatureService> logger) : IWindowsFeatureService
{
    private readonly ILogger<WindowsFeatureService> _logger = logger;

    public async Task<OperationResult> EnableFeatureAsync(
        string featureName,
        IProgress<TaskProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(featureName))
        {
            return OperationResult.Fail("A feature name is required.");
        }

        return await Task.Run(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(TaskProgress.Indeterminate($"Initializing DISM for '{featureName}'…"));

                // ManagedDism keeps global state; Initialize must be paired with Shutdown.
                DismApi.Initialize(DismLogLevel.LogErrors);
                try
                {
                    using DismSession session = DismApi.OpenOnlineSession();

                    // limitAccess: false (allow Windows Update as a source), enableAll: true
                    // (pull in parent features), no explicit source paths. The DISM API never reboots
                    // on its own — a required restart surfaces as DismRebootRequiredException below.
                    // (Granular DISM progress is dropped to match the ManagedDism 3.2 overload set;
                    //  the indeterminate report above still shows the operation running.)
                    DismApi.EnableFeature(session, featureName, false, true, new List<string>());

                    progress?.Report(TaskProgress.Completed($"'{featureName}' enabled."));
                    _logger.LogInformation("Enabled Windows feature {FeatureName}.", featureName);
                    return OperationResult.Ok($"Feature '{featureName}' enabled.");
                }
                finally
                {
                    DismApi.Shutdown();
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Enabling Windows feature {FeatureName} was cancelled.", featureName);
                return OperationResult.Fail($"Enabling '{featureName}' was cancelled.");
            }
            catch (DismRebootRequiredException)
            {
                progress?.Report(TaskProgress.Completed($"'{featureName}' enabled (restart required)."));
                _logger.LogInformation(
                    "Enabled Windows feature {FeatureName}; a restart is required.", featureName);
                return OperationResult.Ok($"Feature '{featureName}' enabled. A restart is required.");
            }
            catch (Exception ex)
            {
                progress?.Report(TaskProgress.Failed($"Failed to enable '{featureName}'."));
                _logger.LogError(ex, "Failed to enable Windows feature {FeatureName}.", featureName);
                return OperationResult.Fail($"Failed to enable '{featureName}': {ex.Message}");
            }
        }, cancellationToken).ConfigureAwait(false);
    }
}
