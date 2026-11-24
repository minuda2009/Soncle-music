// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
namespace Soncle.Windows.Shell;

/// <summary>One page: what it is called and how to build it.</summary>
internal sealed record PageEntry(string Key, string Title, Func<CancellationToken, Task<UIElement>> Build);

/// <summary>
/// The content area's history: Home, Search, album, artist, playlist… Each page is built when it is
/// shown, inside one vertical scroller; going back rebuilds it (the network answers are small and
/// the pages stay simple). A page that is replaced while still loading is cancelled and dropped.
/// </summary>
internal sealed class Navigator
{
    private readonly ContentControl _host;
    private readonly List<PageEntry> _stack = new();
    private CancellationTokenSource? _cts;
    private long _seq;

    public Navigator(ContentControl host) => _host = host;

    public PageEntry? Current => _stack.Count > 0 ? _stack[^1] : null;

    public bool CanGoBack => _stack.Count > 1;

    /// <summary>Raised after the history changed (for the back button).</summary>
    public event EventHandler? Changed;

    /// <summary>Starts a new history at <paramref name="root"/> (a rail destination).</summary>
    public void Reset(PageEntry root)
    {
        _stack.Clear();
        Push(root);
    }

    public void Push(PageEntry page)
    {
        // the same page twice in a row (e.g. a second search for the same words) replaces itself
        if (Current is { } top && top.Key == page.Key) _stack.RemoveAt(_stack.Count - 1);
        _stack.Add(page);
        Show(page);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Back()
    {
        if (!CanGoBack) return;
        _stack.RemoveAt(_stack.Count - 1);
        Show(_stack[^1]);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Reload()
    {
        if (Current is { } page) Show(page);
    }

    private async void Show(PageEntry page)
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        var seq = ++_seq;
        _host.Content = Wrap(Ui.Loading());
        UIElement content;
        try
        {
            content = await page.Build(cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            return;
        }
        catch (Exception e)
        {
            Playback.PlaybackHost.Log($"page-failed {page.Key} {e}");
            var why = e is AggregateException { InnerException: { } inner } ? inner : e;
            content = Ui.Message("Couldn't load this page",
                "Check your connection and try again. If it keeps happening, YouTube may have changed something on its side.\n\n"
                + $"Details: {why.GetType().Name}: {Playback.PlaybackHost.Redact(why.Message)}",
                "Try again", Reload);
        }
        if (seq != _seq) return;
        _host.Content = Wrap(content);
    }

    private static ScrollViewer Wrap(UIElement content) => new()
    {
        Content = content,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollMode = ScrollMode.Disabled,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        Padding = new Thickness(24, 8, 24, 32),
    };
}
