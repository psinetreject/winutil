using CommunityToolkit.Mvvm.ComponentModel;

namespace WinUtil.App.ViewModels;

/// <summary>A top-nav tab: a header plus the view-model whose View fills the content area.</summary>
public partial class TabViewModel : ObservableObject
{
    public TabViewModel(string header, object content)
    {
        Header = header;
        Content = content;
    }

    public string Header { get; }
    public object Content { get; }

    /// <summary>True when this is the active tab (drives the nav-button highlight).</summary>
    [ObservableProperty]
    private bool _isSelected;
}
