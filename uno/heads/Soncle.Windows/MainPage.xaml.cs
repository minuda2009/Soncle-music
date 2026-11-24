// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.ComponentModel;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Soncle.App.ViewModels;
using Soncle.Core.Playback;
using Soncle.Windows.Playback;
using Soncle.Windows.Shell;

namespace Soncle.Windows;

/// <summary>
/// The shell: 48 px title bar with back and search, the four destinations in a rail, the pages
/// (Shell/Views.cs), Now Playing (Shell/NowPlayingView.cs) and the player bar (docs/DESIGN.md
/// §3.1, §4.1), with the playback slice behind them (Playback/PlaybackHost.cs). Everything here runs
/// on the UI thread; a failure in the slice never takes the window down.
/// </summary>
public sealed partial class MainPage : Page
{
    private const string DeviceStopped = "The sound device stopped. Press play to try again.";

    private PlaybackHost? _host;
    private MediaSession? _media;
    private DispatcherTimer? _timer;
    private Navigator? _nav;
    private Views? _views;
    private NowPlayingView? _nowPlaying;
    private Window? _window;
    private bool _started;
    private bool _seekDragging;
    private bool _settingSeek;
    private bool _seekQueued;
    private double _queuedSeek;
    private string? _artworkUrl;
    private string? _deviceError;
    private string? _trackShown;

    public MainPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;

        // Seek on release: the slider's thumb handles the pointer itself, so listen to handled events too.
        SeekBar.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnSeekPointerPressed), true);
        SeekBar.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(OnSeekPointerReleased), true);
        SeekBar.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(OnSeekPointerReleased), true);

        Accelerator(global::Windows.System.VirtualKey.K, global::Windows.System.VirtualKeyModifiers.Control, () => SearchBox.Focus(FocusState.Keyboard));
        Accelerator(global::Windows.System.VirtualKey.Left, global::Windows.System.VirtualKeyModifiers.Menu, () => _nav?.Back());
        Accelerator(global::Windows.System.VirtualKey.Right, global::Windows.System.VirtualKeyModifiers.Control, () => _ = Safe(_host?.Bar.NextAsync(), "next"));
        Accelerator(global::Windows.System.VirtualKey.Left, global::Windows.System.VirtualKeyModifiers.Control, () => _ = Safe(_host?.Bar.PrevAsync(), "prev"));
        Accelerator(global::Windows.System.VirtualKey.F11, global::Windows.System.VirtualKeyModifiers.None, ToggleFullScreen);

        // single keys only when no text box has focus (they bubble here unhandled)
        KeyDown += OnPageKeyDown;
    }

    private void Accelerator(global::Windows.System.VirtualKey key, global::Windows.System.VirtualKeyModifiers mods, Action run)
    {
        var a = new KeyboardAccelerator { Key = key, Modifiers = mods };
        a.Invoked += (_, e) =>
        {
            e.Handled = true;
            run();
        };
        KeyboardAccelerators.Add(a);
    }

    private static async Task Safe(Task? task, string what)
    {
        if (task is null) return;
        try
        {
            await task;
        }
        catch (Exception ex)
        {
            PlaybackHost.Log($"{what}-failed {ex.GetType().Name}");
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_started) return;
        _started = true;

        var app = Application.Current as App;
        _window = app?.MainWindow;
        if (_window is { } window)
        {
            WindowChrome.UseTitleBar(window, TitleBar, BackButton, SearchBox);
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
            PageHost.Content = Ui.Message("Playback is unavailable", "Soncle couldn't set up its cache folder. Restart the app; if it keeps happening, check that your user folder is writable.");
            return;
        }

        SearchBox.IsEnabled = true;
        _host.Bar.PropertyChanged += OnBarChanged;
        _host.Output.Failed += OnOutputFailed;
        _host.QueueChanged += (_, _) => UpdateBar();

        _nav = new Navigator(PageHost);
        _views = new Views(_host, _nav);
        _nav.Changed += (_, _) => BackButton.IsEnabled = _nav.CanGoBack;
        _nowPlaying = new NowPlayingView(_host, CloseNowPlaying, ToggleFullScreen);
        NowPlayingHost.Children.Add(_nowPlaying.Root);

        var settings = AppSettings.Load();
        Notice.IsOpen = !settings.NoticeSeen;
        _host.Volume.SetLevel(settings.Volume);
        VolumeSlider.Value = Math.Round(settings.Volume * 100);

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

        _nav.Reset(_views.Home());
    }

    private void Shutdown()
    {
        _timer?.Stop();
        _nowPlaying?.Close();
        try
        {
            if (_host is { } host)
            {
                var s = AppSettings.Load();
                s.Volume = host.Volume.Level;
                s.Save();
            }
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

    // ---- navigation ---------------------------------------------------------------------------

    private void OnDestinationChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_nav is null || _views is null) return;   // raised while the page is still being built
        if (args.SelectedItem is not NavigationViewItem { Tag: string tag }) return;
        CloseNowPlaying();
        switch (tag)
        {
            case "home":
                _nav.Reset(_views.Home());
                break;
            case "library":
                _nav.Reset(new PageEntry("library", "Library", _ => Task.FromResult(Ui.Message(
                    "Your library comes with sign-in",
                    "Liked songs, your playlists, albums and artists, downloads, history and local files arrive in the next preview builds. For now, everything on Home and in search plays."))));
                break;
            case "sound":
                _nav.Reset(new PageEntry("sound", "Sound", _ => Task.FromResult(SoundPage())));
                break;
            default:
                _nav.Reset(new PageEntry("settings", "Settings", _ => Task.FromResult(SettingsPage())));
                break;
        }
    }

    private UIElement SoundPage()
    {
        var s = new StackPanel { Spacing = 12, Margin = new Thickness(8, 24, 8, 8), MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Left };
        s.Children.Add(Ui.Text("Sound", "HeadlineMedium"));
        s.Children.Add(Ui.Text("What you hear right now", "TitleMedium"));
        s.Children.Add(Ui.Text(
            "Every song is played from its original Opus stream, decoded at 48 kHz in 32-bit float, levelled to −14 LUFS and passed through a true-peak limiter, so nothing clips. Volume is applied after the limiter.",
            "BodyMedium", muted: true, wrap: true));
        s.Children.Add(Ui.Text("Coming to this page", "TitleMedium"));
        s.Children.Add(Ui.Text(
            "Equalizer and AutoEq, loudness settings, Smart mix transitions, output devices and per-device profiles — the same Sound controls as the current Soncle app.",
            "BodyMedium", muted: true, wrap: true));
        return s;
    }

    private UIElement SettingsPage()
    {
        var s = new StackPanel { Spacing = 12, Margin = new Thickness(8, 24, 8, 8), MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Left };
        s.Children.Add(Ui.Text("Settings", "HeadlineMedium"));
        s.Children.Add(Ui.Text("About", "TitleMedium"));
        var version = typeof(MainPage).Assembly.GetName().Version?.ToString() ?? "";
        s.Children.Add(Ui.Text($"Soncle Preview {version}", "BodyMedium"));
        s.Children.Add(Ui.Text(
            "This is an unofficial hobby project. It can’t show YouTube’s ads, so artists aren’t paid through it. Soncle isn’t affiliated with Google or YouTube.",
            "BodyMedium", muted: true, wrap: true));
        s.Children.Add(Ui.Text(
            "This preview keeps its own data and leaves the normal Soncle app and its library untouched. Sign-in, appearance, storage and backup settings come in later builds.",
            "BodyMedium", muted: true, wrap: true));
        var link = new HyperlinkButton { Content = "Source code (GPL-3.0-or-later)", NavigateUri = new Uri("https://github.com/minuda2009/Soncle-music") };
        s.Children.Add(link);
        return s;
    }

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        if (_nowPlaying?.IsOpen == true)
        {
            CloseNowPlaying();
            return;
        }
        _nav?.Back();
    }

    private void OnSearchSubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (_nav is null || _views is null) return;
        var q = (args.QueryText ?? sender.Text ?? "").Trim();
        if (q.Length == 0) return;
        CloseNowPlaying();
        _nav.Push(_views.Search(q));
    }

    private void OnNoticeClosed(InfoBar sender, InfoBarClosedEventArgs args)
    {
        var s = AppSettings.Load();
        s.NoticeSeen = true;
        s.Save();
    }

    private void OnPageKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_host is not { } host || Ui.IsTyping(e.OriginalSource)) return;
        switch (e.Key)
        {
            case global::Windows.System.VirtualKey.Space:
                if (e.OriginalSource is ButtonBase) return;   // a focused button handles its own Space
                e.Handled = true;
                _ = Safe(host.Bar.TogglePlayAsync(), "toggle");
                break;
            case global::Windows.System.VirtualKey.Escape:
                if (_nowPlaying?.IsOpen == true)
                {
                    e.Handled = true;
                    CloseNowPlaying();
                }
                else if (_window is { } w) WindowChrome.ExitFullScreen(w);
                break;
            case global::Windows.System.VirtualKey.F:
                e.Handled = true;
                ToggleNowPlaying();
                break;
            case global::Windows.System.VirtualKey.L:
                e.Handled = true;
                OpenNowPlaying(queue: false);
                break;
            case global::Windows.System.VirtualKey.Q:
                e.Handled = true;
                OpenNowPlaying(queue: true);
                break;
            case global::Windows.System.VirtualKey.M:
                e.Handled = true;
                OnMuteClick(this, new RoutedEventArgs());
                break;
            case global::Windows.System.VirtualKey.S:
                e.Handled = true;
                OnShuffleClick(this, new RoutedEventArgs());
                break;
            case global::Windows.System.VirtualKey.R:
                e.Handled = true;
                OnRepeatClick(this, new RoutedEventArgs());
                break;
        }
    }

    // ---- now playing --------------------------------------------------------------------------

    private void OpenNowPlaying(bool queue)
    {
        if (_nowPlaying is not { } np || _host is not { } host || host.Bar.IsEmpty) return;
        if (queue) np.ShowQueue();
        else np.ShowLyrics();
        if (!np.IsOpen)
        {
            NowPlayingHost.Visibility = Visibility.Visible;
            np.Open();
            ExpandGlyph.Glyph = "";
            BackButton.IsEnabled = true;
        }
    }

    private void CloseNowPlaying()
    {
        if (_nowPlaying is not { IsOpen: true } np) return;
        np.Close();
        NowPlayingHost.Visibility = Visibility.Collapsed;
        ExpandGlyph.Glyph = "";
        BackButton.IsEnabled = _nav?.CanGoBack == true;
    }

    private void ToggleNowPlaying()
    {
        if (_nowPlaying?.IsOpen == true) CloseNowPlaying();
        else OpenNowPlaying(queue: false);
    }

    private void ToggleFullScreen()
    {
        if (_window is { } w) WindowChrome.ToggleFullScreen(w);
    }

    private void OnExpandClick(object sender, RoutedEventArgs e) => ToggleNowPlaying();

    private void OnLyricsClick(object sender, RoutedEventArgs e) => OpenNowPlaying(queue: false);

    private void OnQueueClick(object sender, RoutedEventArgs e) => OpenNowPlaying(queue: true);

    private void OnBarSongTapped(object sender, TappedRoutedEventArgs e) => ToggleNowPlaying();

    // ---- player bar ---------------------------------------------------------------------------

    private async void OnPlayClick(object sender, RoutedEventArgs e)
    {
        if (_host is not { } host) return;
        await Safe(host.Bar.TogglePlayAsync(), "toggle");
        host.Bar.Refresh();
        UpdateBar();
    }

    private async void OnPrevClick(object sender, RoutedEventArgs e)
    {
        if (_host is not { } host) return;
        await Safe(host.Bar.PrevAsync(), "prev");
    }

    private async void OnNextClick(object sender, RoutedEventArgs e)
    {
        if (_host is not { } host) return;
        await Safe(host.Bar.NextAsync(), "next");
    }

    private void OnShuffleClick(object sender, RoutedEventArgs e)
    {
        if (_host is not { } host) return;
        host.Controller.SetShuffle(!host.Controller.Shuffle);
        UpdateModes();
    }

    private void OnRepeatClick(object sender, RoutedEventArgs e)
    {
        if (_host is not { } host) return;
        host.Controller.Repeat = host.Controller.Repeat switch
        {
            RepeatMode.Off => RepeatMode.All,
            RepeatMode.All => RepeatMode.One,
            _ => RepeatMode.Off,
        };
        UpdateModes();
    }

    private void UpdateModes()
    {
        if (_host is not { } host) return;
        var on = Ui.Brush("PrimaryBrush");
        var off = Ui.Brush("OnSurfaceVariantBrush");
        var shuffle = host.Controller.Shuffle;
        if ((shuffle ? on : off) is { } sb) ShuffleGlyph.Foreground = sb;
        var shuffleLabel = shuffle ? "Shuffle is on (S)" : "Shuffle (S)";
        ToolTipService.SetToolTip(ShuffleButton, shuffleLabel);
        AutomationProperties.SetName(ShuffleButton, shuffleLabel);

        var repeat = host.Controller.Repeat;
        RepeatGlyph.Glyph = repeat == RepeatMode.One ? "" : "";
        if ((repeat == RepeatMode.Off ? off : on) is { } rb) RepeatGlyph.Foreground = rb;
        var repeatLabel = repeat switch { RepeatMode.All => "Repeat all (R)", RepeatMode.One => "Repeat one (R)", _ => "Repeat (R)" };
        ToolTipService.SetToolTip(RepeatButton, repeatLabel);
        AutomationProperties.SetName(RepeatButton, repeatLabel);
    }

    private void OnMuteClick(object sender, RoutedEventArgs e)
    {
        if (_host is not { } host) return;
        host.Volume.SetMuted(!host.Volume.Muted);
        UpdateVolumeGlyph();
    }

    private void OnVolumeChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_host is not { } host) return;
        host.Volume.SetLevel(e.NewValue / 100.0);
        if (host.Volume.Muted && e.NewValue > 0) host.Volume.SetMuted(false);
        UpdateVolumeGlyph();
    }

    private void UpdateVolumeGlyph()
    {
        if (_host is not { } host) return;
        var v = host.Volume;
        VolumeGlyph.Glyph = v.Muted || v.Level <= 0 ? "" : v.Level < 0.34 ? "" : v.Level < 0.67 ? "" : "";
        var label = v.Muted ? "Unmute (M)" : "Mute (M)";
        ToolTipService.SetToolTip(MuteButton, label);
        AutomationProperties.SetName(MuteButton, label);
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
        // keyboard (arrows, Home/End) and automation seek without waiting for a release. A click on
        // the track also changes the value before the handled-too PointerPressed marks a drag, so
        // the seek waits one dispatcher turn: if a press arrived meanwhile, the release seeks
        // instead (one fade dip, not two). Repeated keys inside one turn coalesce to the latest value.
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
            BarArtwork.Source = Ui.Art(b.ThumbnailUrl, 112);
        }

        var current = host.Controller.Current?.Id;
        if (current != _trackShown)
        {
            _trackShown = current;
            _nowPlaying?.Refresh();
        }

        if (b.IsPlaying && host.Output.IsRunning) _deviceError = null;
        var error = _deviceError ?? b.ErrorMessage;
        BarError.Text = error ?? "";
        BarError.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;
        if (error is not null && Ui.Brush("ErrorBrush") is { } errorBrush)
        {
            BarError.Foreground = errorBrush;
        }

        var label = b.IsPlaying ? "Pause (Space)" : "Play (Space)";
        PlayGlyph.Glyph = b.IsPlaying ? "" : "";
        ToolTipService.SetToolTip(PlayButton, label);
        AutomationProperties.SetName(PlayButton, b.IsPlaying ? "Pause" : "Play");
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
