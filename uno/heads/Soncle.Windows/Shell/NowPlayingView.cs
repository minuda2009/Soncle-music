// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Soncle.App.ViewModels;
using Soncle.Services;
using Soncle.Windows.Playback;

namespace Soncle.Windows.Shell;

/// <summary>
/// Now Playing: the artwork large over a soft wash of itself, the song, and on the right its synced
/// lyrics (click a line to go there) or the queue. It sits over the content area; the player bar
/// stays below it, so the transport is the same one everywhere.
/// </summary>
internal sealed class NowPlayingView
{
    private readonly PlaybackHost _host;
    private readonly Action _close;
    private readonly Action _toggleFullScreen;
    private readonly Grid _root = new();
    private readonly Image _wash = new() { Stretch = Stretch.UniformToFill, Opacity = 0.55 };
    private readonly Border _art;
    private readonly Image _artImage = new() { Stretch = Stretch.UniformToFill };
    private readonly TextBlock _title;
    private readonly TextBlock _artists;
    private readonly TextBlock _album;
    private readonly StackPanel _lyricsPanel = new() { Spacing = 14, Padding = new Thickness(0, 160, 0, 240) };
    private readonly ScrollViewer _lyricsScroll;
    private readonly StackPanel _queuePanel = new() { Spacing = 0 };
    private readonly ScrollViewer _queueScroll;
    private readonly ToggleButton _lyricsTab;
    private readonly ToggleButton _queueTab;
    private readonly TextBlock _lyricsSource;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };

    private string? _songId;
    private string? _artUrl;
    private List<LrcLine> _lines = new();
    private bool _synced;
    private int _activeLine = -1;
    private CancellationTokenSource? _lyricsCts;
    private bool _queueDirty = true;
    private int _queueIndexShown = -2;

    public NowPlayingView(PlaybackHost host, Action close, Action toggleFullScreen)
    {
        _host = host;
        _close = close;
        _toggleFullScreen = toggleFullScreen;

        _root.Background = Ui.Brush("BackgroundBrush");
        _root.Children.Add(_wash);
        _root.Children.Add(new Border { Background = Ui.Brush("BackgroundBrush"), Opacity = 0.78 });

        var layout = new Grid { Padding = new Thickness(48, 16, 48, 24), ColumnSpacing = 64, RowSpacing = 8 };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6, GridUnitType.Star) });

        // top: close (down chevron) and full screen
        var top = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(-16, 0, 0, 0) };
        top.Children.Add(Ui.IconButton("", "Close Now Playing (Esc)", (_, _) => _close()));
        top.Children.Add(Ui.IconButton("", "Full screen (F11)", (_, _) => _toggleFullScreen()));
        layout.Children.Add(top);

        // left: artwork and the song
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
        _art = new Border
        {
            CornerRadius = new CornerRadius(20),
            Background = Ui.Brush("SurfaceVariantBrush"),
            Child = _artImage,
            Width = 420,
            Height = 420,
            Margin = new Thickness(0, 0, 0, 24),
        };
        left.Children.Add(_art);
        _title = Ui.Text("", "HeadlineMedium", maxLines: 2);
        _artists = Ui.Text("", "TitleMedium", muted: true);
        _album = Ui.Text("", "BodyMedium", muted: true);
        left.Children.Add(_title);
        left.Children.Add(_artists);
        left.Children.Add(_album);
        Grid.SetRow(left, 1);
        layout.Children.Add(left);

        // right: lyrics / up next
        var right = new Grid { RowSpacing = 8 };
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        _lyricsTab = new ToggleButton { Content = "Lyrics", IsChecked = true, CornerRadius = new CornerRadius(16), Padding = new Thickness(16, 6, 16, 6) };
        _queueTab = new ToggleButton { Content = "Up next", CornerRadius = new CornerRadius(16), Padding = new Thickness(16, 6, 16, 6) };
        _lyricsTab.Click += (_, _) => ShowTab(lyrics: true);
        _queueTab.Click += (_, _) => ShowTab(lyrics: false);
        tabs.Children.Add(_lyricsTab);
        tabs.Children.Add(_queueTab);
        right.Children.Add(tabs);

        _lyricsScroll = new ScrollViewer
        {
            Content = _lyricsPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollMode = ScrollMode.Disabled,
        };
        Grid.SetRow(_lyricsScroll, 1);
        right.Children.Add(_lyricsScroll);

        _queueScroll = new ScrollViewer
        {
            Content = _queuePanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Disabled,
            Visibility = Visibility.Collapsed,
        };
        Grid.SetRow(_queueScroll, 1);
        right.Children.Add(_queueScroll);

        _lyricsSource = Ui.Text("", "BodySmall", muted: true);
        Grid.SetRow(_lyricsSource, 2);
        right.Children.Add(_lyricsSource);

        Grid.SetRow(right, 1);
        Grid.SetColumn(right, 1);
        layout.Children.Add(right);
        _root.Children.Add(layout);

        _root.SizeChanged += (_, e) =>
        {
            var size = Math.Clamp(Math.Min(e.NewSize.Width * 0.36, e.NewSize.Height - 260), 200, 560);
            _art.Width = size;
            _art.Height = size;
            left.MaxWidth = size;
        };

        _timer.Tick += (_, _) => Tick();
        _host.QueueChanged += (_, _) => _queueDirty = true;
    }

    public UIElement Root => _root;

    public bool IsOpen { get; private set; }

    public void Open()
    {
        IsOpen = true;
        _queueDirty = true;
        Refresh();
        _timer.Start();
    }

    public void Close()
    {
        IsOpen = false;
        _timer.Stop();
    }

    /// <summary>The song changed (or may have): update the header, fetch its lyrics.</summary>
    public void Refresh()
    {
        var current = _host.Controller.Current;
        if (current is null) return;
        var song = _host.Describe(current.Id);
        _title.Text = !string.IsNullOrEmpty(current.Title) ? current.Title : song?.Title ?? "";
        _artists.Text = song?.Artists ?? "";
        _album.Text = song?.Album ?? "";
        var url = song?.ThumbnailUrl;
        if (url != _artUrl)
        {
            _artUrl = url;
            var big = BigArt(url);
            _artImage.Source = Ui.Art(big, 800);
            _wash.Source = Ui.Art(url, 64);
        }
        if (current.Id != _songId)
        {
            _songId = current.Id;
            _queueDirty = true;
            LoadLyrics(song);
        }
        if (IsOpen) RenderQueueIfNeeded();
    }

    /// <summary>YouTube artwork URLs carry their size (=w120-h120…); ask for a large one.</summary>
    private static string? BigArt(string? url)
    {
        if (string.IsNullOrEmpty(url)) return url;
        var i = url.LastIndexOf("=w", StringComparison.Ordinal);
        return i > 0 && url.IndexOf("googleusercontent", StringComparison.Ordinal) > 0 ? url[..i] + "=w800-h800-l90-rj" : url;
    }

    private void ShowTab(bool lyrics)
    {
        _lyricsTab.IsChecked = lyrics;
        _queueTab.IsChecked = !lyrics;
        _lyricsScroll.Visibility = lyrics ? Visibility.Visible : Visibility.Collapsed;
        _lyricsSource.Visibility = lyrics ? Visibility.Visible : Visibility.Collapsed;
        _queueScroll.Visibility = lyrics ? Visibility.Collapsed : Visibility.Visible;
        if (!lyrics)
        {
            _queueDirty = true;
            RenderQueueIfNeeded();
        }
    }

    public void ShowQueue() => ShowTab(lyrics: false);

    public void ShowLyrics() => ShowTab(lyrics: true);

    // ---- lyrics -------------------------------------------------------------------------------

    private async void LoadLyrics(SongItem? song)
    {
        _lyricsCts?.Cancel();
        var cts = _lyricsCts = new CancellationTokenSource();
        _lines = new List<LrcLine>();
        _synced = false;
        _activeLine = -1;
        _lyricsPanel.Children.Clear();
        _lyricsSource.Text = "";
        if (song is null)
        {
            _lyricsPanel.Children.Add(Ui.Text("No lyrics for this song.", "TitleLarge", muted: true));
            return;
        }
        _lyricsPanel.Children.Add(Ui.Text("Looking for lyrics…", "TitleLarge", muted: true));
        LyricsFetchResult? result = null;
        try
        {
            result = await _host.LyricsAsync(song, cts.Token);
        }
        catch (Exception e) when (!cts.IsCancellationRequested)
        {
            PlaybackHost.Log($"lyrics-failed {e.GetType().Name}");
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (cts.IsCancellationRequested) return;
        _lyricsPanel.Children.Clear();
        if (!string.IsNullOrEmpty(result?.Synced))
        {
            _lines = Lyrics.ParseLrc(result!.Synced!).Where(l => !string.IsNullOrWhiteSpace(l.Text)).ToList();
            _synced = _lines.Count > 0;
        }
        if (!_synced && !string.IsNullOrEmpty(result?.Plain))
        {
            _lines = result!.Plain!.Replace("\r", "").Split('\n').Select(t => new LrcLine(0, t, 0, null)).ToList();
        }
        if (_lines.Count == 0)
        {
            _lyricsPanel.Children.Add(Ui.Text("No lyrics for this song.", "TitleLarge", muted: true));
            return;
        }
        foreach (var line in _lines)
        {
            var t = Ui.Text(line.Text, "HeadlineSmall", muted: _synced, wrap: true);
            t.TextTrimming = TextTrimming.None;
            if (_synced)
            {
                var at = line.T;
                t.Tapped += (_, _) => _host.Bar.Seek(at);
            }
            _lyricsPanel.Children.Add(t);
        }
        _lyricsSource.Text = result?.Source is { Length: > 0 } src ? "Lyrics from " + src : "";
        _lyricsScroll.ChangeView(null, 0, null, true);
    }

    private void Tick()
    {
        if (!IsOpen) return;
        if (_host.Controller.Current?.Id != _songId) Refresh();
        if (!_synced || _lines.Count == 0) return;
        var pos = _host.Player.Position + 0.15;   // a little early reads better than a little late
        var idx = -1;
        for (var i = 0; i < _lines.Count; i++)
        {
            if (_lines[i].T <= pos) idx = i;
            else break;
        }
        if (idx == _activeLine) return;
        var muted = Ui.Brush("OnSurfaceVariantBrush");
        var strong = Ui.Brush("OnSurfaceBrush");
        if (_activeLine >= 0 && _activeLine < _lyricsPanel.Children.Count && _lyricsPanel.Children[_activeLine] is TextBlock old)
        {
            if (muted is not null) old.Foreground = muted;
            old.Opacity = 1;
        }
        _activeLine = idx;
        if (idx >= 0 && idx < _lyricsPanel.Children.Count && _lyricsPanel.Children[idx] is TextBlock now)
        {
            if (strong is not null) now.Foreground = strong;
            var tr = now.TransformToVisual(_lyricsPanel);
            var y = tr.TransformPoint(new global::Windows.Foundation.Point(0, 0)).Y;
            _lyricsScroll.ChangeView(null, Math.Max(0, y - _lyricsScroll.ViewportHeight * 0.38), null, false);
        }
    }

    // ---- queue --------------------------------------------------------------------------------

    private void RenderQueueIfNeeded()
    {
        if (_queueScroll.Visibility != Visibility.Visible) return;
        var c = _host.Controller;
        if (!_queueDirty && _queueIndexShown == c.Index) return;
        _queueDirty = false;
        _queueIndexShown = c.Index;
        _queuePanel.Children.Clear();
        var q = c.Queue;
        if (q.Count == 0)
        {
            _queuePanel.Children.Add(Ui.Text("The queue is empty.", "BodyMedium", muted: true));
            return;
        }
        for (var i = 0; i < q.Count; i++)
        {
            var index = i;
            var t = q[i];
            var s = _host.Describe(t.Id) ?? new SongItem(t.Id, t.Title, "", "", t.Duration, "");
            var row = Ui.SongRow(s, () => _ = PlayAt(index), null, null, showAlbum: false);
            if (i == c.Index)
            {
                row.Background = Ui.Brush("SecondaryContainerBrush") ?? Ui.Brush("SurfaceVariantBrush");
            }
            else if (i < c.Index)
            {
                row.Opacity = 0.55;
            }
            _queuePanel.Children.Add(row);
        }
    }

    private async Task PlayAt(int index)
    {
        try
        {
            await _host.Controller.PlayAtAsync(index);
        }
        catch (Exception e)
        {
            PlaybackHost.Log($"queue-play-failed {e.GetType().Name}");
        }
        _queueDirty = true;
        RenderQueueIfNeeded();
    }
}
