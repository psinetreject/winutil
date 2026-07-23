using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinUtil.Core.Models;

namespace WinUtil.App.ViewModels;

/// <summary>A selectable checkbox tweak on the Tweaks tab.</summary>
public partial class TweakItemViewModel : ObservableObject
{
    public TweakItemViewModel(Tweak tweak)
    {
        Tweak = tweak;
    }

    public Tweak Tweak { get; }

    public string Content => Tweak.Content ?? Tweak.Id;
    public string? Description => Tweak.Description;
    public string Category => TweakCategory.Prettify(Tweak.Category);

    /// <summary>Raw (unprettified) category key, used for column bucketing.</summary>
    public string? RawCategory => Tweak.Category;

    /// <summary>
    /// Docs URL surfaced by the "(?)" affordance. The ported tweaks.json carries no link field yet, so
    /// this is null and the "(?)" falls back to a Description tooltip. Populate once Tweak grows a Link.
    /// </summary>
    public string? Link => null; // TODO: no link data in the ported tweaks.json.

    public bool HasLink => !string.IsNullOrWhiteSpace(Link);

    [ObservableProperty]
    private bool _isSelected;

    public bool Matches(string term) =>
        string.IsNullOrWhiteSpace(term)
        || Content.Contains(term, StringComparison.OrdinalIgnoreCase)
        || (Description?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false);

    [RelayCommand(CanExecute = nameof(HasLink))]
    private void OpenLink()
    {
        if (Link is not { } url)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // Opening the browser is best-effort; swallow so a bad URL never crashes the tab.
        }
    }
}

/// <summary>Formatting helpers for the raw tweak category keys.</summary>
internal static class TweakCategory
{
    /// <summary>Strips the sort-order "z__" prefix and normalizes separators for display.</summary>
    public static string Prettify(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "Uncategorized";
        }

        var value = raw!;
        if (value.StartsWith("z__", StringComparison.OrdinalIgnoreCase))
        {
            value = value[3..];
        }

        return value.Replace('_', ' ').Trim();
    }
}
