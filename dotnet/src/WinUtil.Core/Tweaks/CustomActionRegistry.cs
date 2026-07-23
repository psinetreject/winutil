using System.Diagnostics.CodeAnalysis;

namespace WinUtil.Core.Tweaks;

/// <summary>
/// Look-up of the hand-written custom tweak actions, keyed (case-insensitively) by tweak Id. Built from
/// every <see cref="ICustomTweakAction"/> registered in the DI container.
/// </summary>
public sealed class CustomActionRegistry : ICustomActionRegistry
{
    private readonly Dictionary<string, ICustomTweakAction> _actions;

    public CustomActionRegistry(IEnumerable<ICustomTweakAction> actions)
    {
        _actions = new Dictionary<string, ICustomTweakAction>(StringComparer.OrdinalIgnoreCase);
        foreach (var action in actions)
        {
            // Ids are unique by design; last registration wins if a duplicate slips through.
            _actions[action.Id] = action;
        }
    }

    public bool TryGet(string id, [NotNullWhen(true)] out ICustomTweakAction? action)
        => _actions.TryGetValue(id, out action);

    public IReadOnlyCollection<string> RegisteredIds => _actions.Keys;
}
