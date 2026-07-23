using System.Collections;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;

namespace WinUtil.App.ViewModels;

/// <summary>
/// A named, collapsible group of items (apps or tweaks) with a live filtered view. The owning tab
/// view-model supplies the filter predicate (which reads the shared search string) and calls
/// <see cref="Refresh"/> when the search changes.
/// </summary>
public partial class FilteredCategoryViewModel : ObservableObject
{
    public FilteredCategoryViewModel(string name, IEnumerable source, Predicate<object> filter)
    {
        Name = name;
        ItemsView = CollectionViewSource.GetDefaultView(source);
        ItemsView.Filter = filter;
        Refresh(expandMatches: false);
    }

    public string Name { get; }

    public ICollectionView ItemsView { get; }

    /// <summary>False when the current filter hides every item in the group (used to collapse it).</summary>
    [ObservableProperty]
    private bool _hasVisibleItems = true;

    [ObservableProperty]
    private bool _isExpanded;

    public void Refresh(bool expandMatches)
    {
        ItemsView.Refresh();
        HasVisibleItems = !ItemsView.IsEmpty;
        if (expandMatches)
        {
            IsExpanded = HasVisibleItems;
        }
    }
}
