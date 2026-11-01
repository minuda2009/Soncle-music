// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Soncle.App.ViewModels;
using Soncle.Droid.Playback;

namespace Soncle.Droid;

/// <summary>
/// The Android shell: four destinations (bottom bar narrow, rail wide; both drive the same
/// selection), the search box, the player bar (docs/DESIGN.md §4.1) and real playback through
/// <see cref="AndroidPlaybackHost"/> — the same search → resolve → streaming-decode → engine path
/// the Windows head runs. Pages arrive with Soncle.App.
/// </summary>
public sealed partial class MainPage : Page
{
    /// <summary>Below this width (effective px) the bottom bar shows; from it, the rail.</summary>
    private const double RailMinWidth = 600;

    private static readonly string[] Destinations = ["Home", "Library", "Sound", "Settings"];

    /// <summary>The CI smoke test's search (<c>--es soncle.demo play</c>): a long-lived, public song.</summary>
    private const string DemoQuery = "Never Gonna Give You Up";

    private readonly AndroidPlaybackHost _host;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private bool _syncing;
    private bool _showResults;
    private bool _seekDragging;
    private bool _settingSeek;

    public MainPage()
    {
        InitializeComponent();

        // The app's own private folder (Android: /data/data/<pkg>/files); never shared storage.
        var cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "streams");
        _host = new AndroidPlaybackHost(cache);
        _host.Output.Interrupted += (_, reason) => DispatcherQueue.TryEnqueue(() => OnInterrupted(reason));
        _clock.Tick += (_, _) => _host.Tick();

        ResultsList.ItemsSource = _host.Search.Results;
        _host.Search.PropertyChanged += (_, _) => UpdateSearch();
        _host.Search.Results.CollectionChanged += (_, _) => UpdateSearch();
        _host.Bar.PropertyChanged += (_, _) => UpdatePlayerBar();

        BottomBar.RegisterPropertyChangedCallback(
            Uno.Toolkit.UI.TabBar.SelectedIndexProperty,
            (_, _) => SelectDestination(BottomBar.SelectedIndex));

        // Seek on release: the slider's thumb handles the pointer itself, so listen to handled events too.
        SeekBar.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, _) => _seekDragging = true), true);
        SeekBar.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, _) => _seekDragging = false), true);
        SeekBar.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler((_, _) => _seekDragging = false), true);

        SizeChanged += (_, e) => ApplyLayout(e.NewSize.Width);
        Loaded += OnLoaded;
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += OnFirstFrame;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyLayout(ActualWidth);
        _clock.Start();
        UpdatePlayerBar();
        if (LaunchOptions.AutoPlay)
        {
            LaunchOptions.AutoPlay = false;
            _ = RunDemoAsync();
        }
    }

    /// <summary>
    /// The CI smoke test's entry point (<c>--es soncle.demo play</c>): search for a fixed song and
    /// play the first result, so the job can prove real playback without a tap.
    /// </summary>
    private async Task RunDemoAsync()
    {
        try
        {
            SearchBox.Text = DemoQuery;
            _host.Search.Query = DemoQuery;
            _showResults = true;
            UpdateSearch();
            await _host.Search.SearchAsync();
            UpdateSearch();
            if (_host.Search.Results.FirstOrDefault() is { } first) await _host.Search.PlayAsync(first);
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Error(StartupClock.LogTag, $"demo-failed {ex.GetType().Name}: {ex.Message}");
        }
        UpdatePlayerBar();
    }

    private void OnFirstFrame(object? sender, object e)
    {
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= OnFirstFrame;
        var ms = StartupClock.ReportFirstFrame();
        FirstFrameLine.Text = $"Start-up: first frame {ms} ms after the process started.";
    }

    /// <summary>Bottom bar on a phone, rail when the window is wide (docs/DESIGN.md §3.1).</summary>
    private void ApplyLayout(double width)
    {
        if (width <= 0) return;
        var wide = width >= RailMinWidth;
        Rail.IsPaneVisible = wide;
        BottomBar.Visibility = wide ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnRailSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is { } item)
        {
            SelectDestination(Rail.MenuItems.IndexOf(item));
        }
    }

    /// <summary>The one place the destination changes, so the rail and the bottom bar agree.</summary>
    private void SelectDestination(int index)
    {
        if (_syncing || index < 0 || index >= Destinations.Length) return;
        _syncing = true;
        try
        {
            if (BottomBar.SelectedIndex != index) BottomBar.SelectedIndex = index;
            if (!ReferenceEquals(Rail.SelectedItem, Rail.MenuItems[index])) Rail.SelectedItem = Rail.MenuItems[index];
            DestinationTitle.Text = Destinations[index];
            FirstFrameLine.Visibility = Destinations[index] == "Settings" ? Visibility.Visible : Visibility.Collapsed;
            // Picking a destination leaves the search results (docs/DESIGN.md §3.1).
            _showResults = false;
            UpdateSearch();
        }
        finally
        {
            _syncing = false;
        }
    }

    // ---------------- search ----------------

    private void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        _host.Search.Query = sender.Text;
    }

    private async void OnSearchSubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        _host.Search.Query = args.QueryText ?? sender.Text;
        if (string.IsNullOrWhiteSpace(_host.Search.Query)) return;
        _showResults = true;
        UpdateSearch();
        try
        {
            await _host.Search.SearchAsync();
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Error(StartupClock.LogTag, $"search-failed {ex.GetType().Name}: {ex.Message}");
        }
        UpdateSearch();
    }

    private async void OnResultClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not SongItem item) return;
        try
        {
            await _host.Search.PlayAsync(item);
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Error(StartupClock.LogTag, $"play-failed {ex.GetType().Name}: {ex.Message}");
        }
        UpdatePlayerBar();
    }

    /// <summary>Shows the results (or the destination's placeholder) and the search state.</summary>
    private void UpdateSearch()
    {
        var search = _host.Search;
        var show = _showResults;
        ResultsPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        Placeholder.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        if (!show) return;

        SearchSpinner.IsActive = search.IsSearching;
        SearchSpinner.Visibility = search.IsSearching ? Visibility.Visible : Visibility.Collapsed;
        SearchMessage.Text = search.ErrorMessage ?? search.EmptyMessage
            ?? (search.IsSearching ? "Searching…" : $"{search.Results.Count} songs");
    }

    // ---------------- player bar ----------------

    private void OnPlayPauseClick(object sender, RoutedEventArgs e) => _ = _host.Bar.TogglePlayAsync();

    private void OnNextClick(object sender, RoutedEventArgs e) => _ = _host.Bar.NextAsync();

    private void OnPrevClick(object sender, RoutedEventArgs e) => _ = _host.Bar.PrevAsync();

    private void OnSeekValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_settingSeek || !_seekDragging) return;
        _host.Bar.Seek(e.NewValue);
    }

    /// <summary>The output stopped itself: a focus loss reads as a pause, a device failure as an error.</summary>
    private void OnInterrupted(AudioInterruption reason)
    {
        if (reason == AudioInterruption.DeviceFailed)
        {
            _host.Session.Log("output-device-failed");
        }
        UpdatePlayerBar();
    }

    private void UpdatePlayerBar()
    {
        var bar = _host.Bar;
        var has = !bar.IsEmpty;
        PrevButton.IsEnabled = has;
        PlayButton.IsEnabled = has;
        NextButton.IsEnabled = has;
        SeekBar.Visibility = has ? Visibility.Visible : Visibility.Collapsed;

        BarSpinner.IsActive = bar.IsLoading || bar.IsBuffering;
        BarSpinner.Visibility = bar.IsLoading || bar.IsBuffering ? Visibility.Visible : Visibility.Collapsed;

        if (!has)
        {
            // Back to the empty state: a message, not a blank square with 0:00 / 0:00.
            NowArt.Visibility = Visibility.Collapsed;
            NowTitle.Text = "Nothing playing";
            NowDetail.Text = "Search for a song above, then pick it to play.";
            PlayButton.Content = "Play";
            return;
        }

        NowArt.Visibility = Visibility.Visible;
        NowArtImage.Source = string.IsNullOrEmpty(bar.ThumbnailUrl) ? null : new BitmapImage(new Uri(bar.ThumbnailUrl));
        NowTitle.Text = bar.Title;
        var time = $"{bar.PositionText} / {bar.DurationText}";
        NowDetail.Text = bar.ErrorMessage
            ?? (bar.IsBuffering ? "Buffering…"
                : bar.Subtitle.Length > 0 ? bar.Subtitle + " · " + time : time);
        PlayButton.Content = bar.IsPlaying ? "Pause" : "Play";

        // Move the slider without echoing it back as a seek while the user is not dragging.
        if (!_seekDragging)
        {
            _settingSeek = true;
            try
            {
                SeekBar.Maximum = Math.Max(1, bar.DurationSeconds);
                SeekBar.Value = Math.Clamp(bar.PositionSeconds, 0, SeekBar.Maximum);
            }
            finally
            {
                _settingSeek = false;
            }
        }
    }
}
