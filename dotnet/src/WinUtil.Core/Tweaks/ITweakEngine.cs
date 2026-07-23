using WinUtil.Core.Abstractions;
using WinUtil.Core.Models;

namespace WinUtil.Core.Tweaks;

/// <summary>
/// Applies/undoes tweaks by dispatching their declarative registry + service actions and any custom
/// C# action. Mirrors Invoke-WinUtilTweaks in the PowerShell tool.
/// </summary>
public interface ITweakEngine
{
    Task<OperationResult> ApplyAsync(Tweak tweak, IProgress<TaskProgress>? progress = null, CancellationToken cancellationToken = default);
    Task<OperationResult> UndoAsync(Tweak tweak, IProgress<TaskProgress>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>For toggle tweaks: reads live system state (registry) to decide on/off.</summary>
    bool GetToggleState(Tweak tweak);
}
