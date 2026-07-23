namespace WinUtil.App.ViewModels;

/// <summary>A stand-in tab (Win11 ISO, AppX) whose feature set lands in a later milestone.</summary>
public sealed class PlaceholderViewModel
{
    public PlaceholderViewModel(string title, string message)
    {
        Title = title;
        Message = message;
    }

    public string Title { get; }
    public string Message { get; }
}
