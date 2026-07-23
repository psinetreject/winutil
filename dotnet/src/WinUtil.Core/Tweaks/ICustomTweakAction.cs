using Microsoft.Extensions.Logging;
using WinUtil.Core.Abstractions;

namespace WinUtil.Core.Tweaks;

/// <summary>Services + reporting handed to a custom tweak action when it runs.</summary>
public sealed class TweakActionContext
{
    public required IRegistryService Registry { get; init; }
    public required IServiceManager Services { get; init; }
    public required IProcessRunner Process { get; init; }
    public required IAppxManager Appx { get; init; }
    public required IExplorerRefresher Explorer { get; init; }

    /// <summary>Escape hatch for actions needing niche services (DISM, power, restore, tasks).</summary>
    public required IServiceProvider ServiceProvider { get; init; }

    public required ILogger Logger { get; init; }
    public IProgress<TaskProgress>? Progress { get; init; }
    public CancellationToken CancellationToken { get; init; }
}

/// <summary>
/// A hand-written replacement for a PowerShell InvokeScript/UndoScript pair. One implementation per
/// tweak that needs imperative logic (see dotnet/docs/tweaks-inventory.md). By convention
/// <see cref="Id"/> equals the tweak's Id (and its config <c>customActionId</c>).
/// </summary>
public interface ICustomTweakAction
{
    string Id { get; }

    /// <summary>True when this action has a meaningful undo (mirrors having an UndoScript).</summary>
    bool SupportsUndo { get; }

    Task<OperationResult> ApplyAsync(TweakActionContext context);
    Task<OperationResult> UndoAsync(TweakActionContext context);
}
