// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.ComponentModel;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Soncle.App.ViewModels;
using Soncle.Windows.Playback;

namespace Soncle.Windows;

/// <summary>
/// The shell: 48 px title bar with the search box, the four destinations in a rail, the player
/// bar (docs/DESIGN.md §3.1, §4.1), and the playback slice behind them (Playback/PlaybackHost.cs).
/// Everything here runs on the UI thread; a failure in the slice never takes the window down.
/// </summary>
public sealed partial class MainPage : Page
{
    private const string DeviceStopped = "The sound device stopped. Press play to try again.";

    private PlaybackHost? _host;
    private MediaSession? _media;
    private DispatcherTimer? _timer;
    private bool _started;
    private bool _showResults;
    private bool _seekDragging;
    private bool _settingSeek;
    private bool _seekQueued;
    private double _queuedSeek;
    private string? _artworkUrl;
    private string? _deviceError;

    public MainPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;

        // Seek on release: the slider's thumb handles the pointer itself, so listen to handled events too.
        SeekBar.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnSeekPointerPressed), true);
        SeekBar.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(OnSeekPointerReleased), true);
        SeekBar.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(OnSeekPointerReleased), true);

        // Ctrl+K goes to search (docs/DESIGN.md §3.1).
        var search = new KeyboardAccelerator
        {
            Key = global::Windows.System.VirtualKey.K,
            Modifiers = global::Windows.System.VirtualKeyModifiers.Control,
        };
        search.Invoked += (_, e) =>
        {
            SearchBox.Focus(FocusState.Keyboard);
            e.Handled = true;
        };
        KeyboardAccelerators.Add(search);
    }

    /// <summary>A row's or the bar's artwork, decoded at the size it is shown; null without a usable URL.</summary>
    public static ImageSource? Artwork(string url, int decodeWidth)
    {
        if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }
        return new BitmapImage { UriSource = uri, DecodePixelWidth = decodeWidth, DecodePixelType = DecodePixelType.Logical };
    }

    /// <summary>A result row's 48 px artwork (x:Bind function: one string argument, no literal to convert).</summary>
    public static ImageSource? RowArtwork(string url) => Artwork(url, 96);

    /// <summary>A result row's second line: artists · album · duration (empty parts left out).</summary>
    public static string Details(string artists, string album, string duration) =>
        string.Join(" \u00b7 ", new[] { artists, album, duration }.Where(s => !string.IsNullOrWhiteSpace(s)));

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_started) return;
        _started = true;

        var app = Application.Current as App;
        if (app?.MainWindow is { } window)
        {
            WindowChrome.UseTitleBar(window, TitleBar, SearchBox);
            window.Closed += (_, _) => Shutdown();
        }

        try
        {
            _host = new PlaybackHost(WebViewBotGuardPage.Factory(BotGuardHost));
        }
        catch (Exception ex)
        {
            // e.g. the cache folder can't be created: the shell still opens, without playback
            PlaybackHost.Log($"playback-unavailable {ex.GetType().Name}");
            SearchBox.PlaceholderText = "Search is unavailable in this build";
            return;
        }

        SearchBox.IsEnabled = true;
        ResultsList.ItemsSource = _host.Search.Results;
        _host.Search.PropertyChanged += OnSearchChanged;
        _host.Search.Results.CollectionChanged += (_, _) => UpdateSearch();
        _host.Bar.PropertyChanged += OnBarChanged;
        _host.Output.Failed += OnOutputFailed;

        if (app?.MediaControls is { } controls)
        {
            try
            {
                _media = new MediaSession(controls, _host, DispatcherQueue);
            }
            catch (Exception ex)
            {
                PlaybackHost.Log($"media-session-wiring-failed {ex.GetType().Name}");
            }
        }

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += OnTimerTick;
        _timer.Start();
    }

    private void Shutdown()
    {
        _timer?.Stop();
        try
        {
            _media?.Dispose();
            _host?.Dispose();
        }
        catch (Exception ex)
        {
            PlaybackHost.Log($"shutdown-failed {ex.GetType().Name}");
        }
        _media = null;
        _host = null;
    }

    private void OnTimerTick(object? sender, object e)
    {
        try
        {
            _host?.Tick();
        }
        catch (Exception ex)
        {
            PlaybackHost.Log($"tick-failed {ex.GetType().Name}");
        }
    }

    private void OnDestinationChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (DestinationTitle is not null && args.SelectedItem is NavigationViewItem { Content: string name })
        {
            DestinationTitle.Text = name;
        }
        // picking a destination leaves the results; the next search shows them again
        _showResults = false;
        UpdateSearch();
    }

    // ---- search -------------------------------------------------------------------------------

    private void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (_host is not null && args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            _host.Search.Query = sender.Text;
        }
    }

    private async void OnSearchSubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (_host is not { } host) return;
        host.Search.Query = args.QueryText ?? sender.Text;
        if (string.IsNullOrWhiteSpace(host.Search.Query)) return;
        _showResults = true;
        UpdateSearch();
        try
        {
            await host.Search.SearchAsync();
        }
        catch (Exception ex)
        {
            PlaybackHost.Log($"search-failed {ex.GetType().Name}");
        }
        UpdateSearch();
    }

    private async void OnResultClick(object sender, ItemClickEventArgs e)
    {
        if (_host is not { } host || e.ClickedItem is not SongItem item) return;
        try
        {
            await host.Search.PlayAsync(item);
        }
        catch (Exception ex)
        {
            PlaybackHost.Log($"play-failed {ex.GetType().Name}");
        }
        UpdateBar();
    }

    private void OnSearchChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SearchViewModel.ErrorDetail) && _host?.Search.ErrorDetail is { } detail)
        {
            PlaybackHost.Log($"search-error {detail}");
        }
        UpdateSearch();
    }

    private void UpdateSearch()
    {
        // the rail raises SelectionChanged while the page is still being built
        if (Placeholder is null || ResultsPanel is null) return;
        Placeholder.Visibility = _showResults ? Visibility.Collapsed : Visibility.Visible;
        ResultsPanel.Visibility = _showResults ? Visibility.Visible : Visibility.Collapsed;
        if (_host is not { } host) return;

        var s = host.Search;
        SearchProgress.IsActive = s.IsSearching;
        SearchProgress.Visibility = s.IsSearching ? Visibility.Visible : Visibility.Collapsed;

        var message = s.ErrorMessage ?? s.EmptyMessage;
        SearchMessage.Text = message ?? "";
        SearchMessage.Visibility = message is not null && !s.IsSearching ? Visibility.Visible : Visibility.Collapsed;
        ResultsList.Visibility = message is null ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---- player bar ---------------------------------------------------------------------------

    private async void OnPlayClick(object sender, RoutedEventArgs e)
    {
        if (_host is not { } host) return;
        try
        {
            await host.Bar.TogglePlayAsync();
        }
        catch (Exception ex)
        {
            PlaybackHost.Log($"toggle-failed {ex.GetType().Name}");
        }
        host.Bar.Refresh();
        UpdateBar();
    }

    private async void OnPrevClick(object sender, RoutedEventArgs e)
    {
        if (_host is not { } host) return;
        try
        {
            await host.Bar.PrevAsync();
        }
        catch (Exception ex)
        {
            PlaybackHost.Log($"prev-failed {ex.GetType().Name}");
        }
    }

    private async void OnNextClick(object sender, RoutedEventArgs e)
    {
        if (_host is not { } host) return;
        try
        {
            await host.Bar.NextAsync();
        }
        catch (Exception ex)
        {
            PlaybackHost.Log($"next-failed {ex.GetType().Name}");
        }
    }

    private void OnSeekPointerPressed(object sender, PointerRoutedEventArgs e) => _seekDragging = true;

    private void OnSeekPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_seekDragging) return;
        _seekDragging = false;
        // a click's own queued seek (if it hasn't run yet) is this one
        _queuedSeek = double.NaN;
        _host?.Bar.Seek(SeekBar.Value);
    }

    private void OnSeekValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_settingSeek || _host is not { } host) return;
        if (_seekDragging)
        {
            // show where the release will land; the song moves on release
            BarPosition.Text = TimeText.Format(e.NewValue);
            return;
        }
        // keyboard (arrows, Home/End) and automation seek without waiting for a release. A click on the track also
        // changes the value before the handled-too PointerPressed marks a drag, so the seek waits
        // one dispatcher turn: if a press arrived meanwhile, the release seeks instead (one fade dip,
        // not two). Repeated keys inside one turn coalesce to the latest value.
        _queuedSeek = e.NewValue;
        if (_seekQueued) return;
        _seekQueued = true;
        if (!DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, ApplyQueuedSeek))
        {
            _seekQueued = false;
            host.Bar.Seek(e.NewValue);
        }
    }

    private void ApplyQueuedSeek()
    {
        _seekQueued = false;
        if (_seekDragging || double.IsNaN(_queuedSeek) || _host is not { } host) return;
        host.Bar.Seek(_queuedSeek);
    }

    private void OnOutputFailed(object? sender, string reason)
    {
        PlaybackHost.Log($"audio-device-failed {reason}");
        if (_host is not { } host) return;
        _deviceError = DeviceStopped;
        host.Controller.Pause();
        host.Bar.Refresh();
        UpdateBar();
    }

    private void OnBarChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayerBarViewModel.ErrorDetail) && _host?.Bar.ErrorDetail is { } detail)
        {
            PlaybackHost.Log($"play-error {detail}");
        }
        UpdateBar();
    }

    private void UpdateBar()
    {
        if (_host is not { } host) return;
        var b = host.Bar;
        EmptyBar.Visibility = b.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        NowPlayingBar.Visibility = b.IsEmpty ? Visibility.Collapsed : Visibility.Visible;
        if (b.IsEmpty) return;

        BarTitle.Text = b.Title;
        BarSubtitle.Text = b.Subtitle;
        if (!string.Equals(_artworkUrl, b.ThumbnailUrl, StringComparison.Ordinal))
        {
            _artworkUrl = b.ThumbnailUrl;
            BarArtwork.Source = Artwork(b.ThumbnailUrl, 112);
        }

        if (b.IsPlaying && host.Output.IsRunning) _deviceError = null;
        var error = _deviceError ?? b.ErrorMessage;
        BarError.Text = error ?? "";
        BarError.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;
        if (error is not null && Application.Current.Resources.TryGetValue("ErrorBrush", out var brush) && brush is Brush errorBrush)
        {
            BarError.Foreground = errorBrush;
        }

        var label = b.IsPlaying ? "Pause" : "Play";
        PlayGlyph.Glyph = b.IsPlaying ? "\uE769" : "\uE768";
        ToolTipService.SetToolTip(PlayButton, label);
        AutomationProperties.SetName(PlayButton, label);
        var busy = b.IsLoading || b.IsBuffering;   // looking up / downloading, or waiting for the download mid-song
        BarProgress.IsActive = busy;
        BarProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;

        BarDuration.Text = b.DurationText;
        SeekBar.IsEnabled = b.DurationSeconds > 0;
        if (!_seekDragging)
        {
            BarPosition.Text = b.PositionText;
            _settingSeek = true;
            try
            {
                SeekBar.Maximum = Math.Max(1, b.DurationSeconds);
                SeekBar.Value = Math.Min(b.PositionSeconds, SeekBar.Maximum);
            }
            finally
            {
                _settingSeek = false;
            }
        }

        try
        {
            _media?.Update();
        }
        catch (Exception ex)
        {
            PlaybackHost.Log($"media-session-update-failed {ex.GetType().Name}");
        }
    }
}
