using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using WinUtil.Core.Models;

namespace WinUtil.App.ViewModels;

/// <summary>A selectable application card on the Install tab.</summary>
public partial class AppItemViewModel : ObservableObject
{
    // Shared client for the favicon fetch. Mirrors the PowerShell tool, which points every app icon at
    // Google's favicon service (roughly one request per app, ~200 requests to Google on a full render).
    private static readonly HttpClient FaviconClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15),
    };

    private bool _faviconRequested;

    public AppItemViewModel(string key, AppEntry entry)
    {
        Key = key;
        Entry = entry;
    }

    public string Key { get; }
    public AppEntry Entry { get; }

    public string Content => Entry.Content;

    /// <summary>Display name shown on the card (alias for <see cref="Content"/>).</summary>
    public string Name => Entry.Content;

    public string? Description => Entry.Description;
    public string Category => string.IsNullOrWhiteSpace(Entry.Category) ? "Uncategorized" : Entry.Category!;
    public string? Link => Entry.Link;

    /// <summary>True for Free and Open Source Software — drives the green dot next to the name.</summary>
    public bool IsFoss => Entry.Foss;

    /// <summary>First letter of the name (dots trimmed, as the PS tool does), shown until/unless the favicon loads.</summary>
    public string FallbackLetter
    {
        get
        {
            var trimmed = Content.TrimStart('.');
            var source = string.IsNullOrEmpty(trimmed) ? Content : trimmed;
            return string.IsNullOrEmpty(source) ? "?" : source.Substring(0, 1).ToUpperInvariant();
        }
    }

    [ObservableProperty]
    private bool _isSelected;

    private ImageSource? _favicon;

    /// <summary>
    /// The app's favicon, fetched lazily from Google's favicon service the first time the card binds to
    /// it (so only rendered cards trigger a request). Null until loaded, and stays null on any failure
    /// (offline, DNS error, HTTP error, undecodable payload) so the card falls back to the letter.
    /// </summary>
    public ImageSource? Favicon
    {
        get
        {
            if (!_faviconRequested)
            {
                _faviconRequested = true;
                _ = LoadFaviconAsync();
            }

            return _favicon;
        }
        private set => SetProperty(ref _favicon, value);
    }

    /// <summary>Matches the shared search over Content + Description.</summary>
    public bool Matches(string term) =>
        string.IsNullOrWhiteSpace(term)
        || Content.Contains(term, StringComparison.OrdinalIgnoreCase)
        || (Description?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false);

    // Non-blocking favicon fetch. Never throws on the UI thread: the network + decode run off-thread,
    // the decoded image is frozen for cross-thread use, and the assignment is marshalled to the
    // dispatcher. Any failure leaves Favicon null (letter placeholder shows).
    private async Task LoadFaviconAsync()
    {
        var link = Entry.Link;
        if (string.IsNullOrWhiteSpace(link))
        {
            return;
        }

        try
        {
            var url = $"https://www.google.com/s2/favicons?sz=64&domain_url={Uri.EscapeDataString(link)}";
            var bytes = await FaviconClient.GetByteArrayAsync(new Uri(url)).ConfigureAwait(false);

            var image = new BitmapImage();
            using (var stream = new MemoryStream(bytes))
            {
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = stream;
                image.EndInit();
            }

            image.Freeze();

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess())
            {
                Favicon = image;
            }
            else
            {
                dispatcher.Invoke(() => Favicon = image);
            }
        }
        catch (Exception)
        {
            // Offline / DNS / HTTP / decode failure: keep the letter placeholder, never surface an error.
        }
    }
}
