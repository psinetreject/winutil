using System.Diagnostics.CodeAnalysis;

namespace WinUtil.Core.Tweaks;

/// <summary>Look-up of the hand-written custom tweak actions, keyed by tweak Id.</summary>
public interface ICustomActionRegistry
{
    bool TryGet(string id, [NotNullWhen(true)] out ICustomTweakAction? action);
    IReadOnlyCollection<string> RegisteredIds { get; }
}
