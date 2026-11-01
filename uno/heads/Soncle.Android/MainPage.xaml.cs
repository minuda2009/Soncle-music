// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using Soncle.Droid.Playback;

namespace Soncle.Droid;

/// <summary>
/// The Android shell: four destinations (bottom bar narrow, rail wide; both drive the same
/// selection), the player bar with the docs/DESIGN.md §4.1 empty state, and the bundled test sound
/// played through Soncle.Audio. Pages arrive with Soncle.App.
/// </summary>
public sealed partial class MainPage : Page
{
    /// <summary>Below this width (effective px) the bottom bar shows; from it, the rail.</summary>
    private const double RailMinWidth = 600;

    private static readonly string[] Destinations = ["Home", "Library", "Sound", "Settings"];

    private readonly AudioTrackOutput _output = new(48000);
    private readonly SamplePlayer _player;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private bool _syncing;
    private bool _starting;
    private string? _error;

    public MainPage()
    {
        InitializeComponent();
        _player = new SamplePlayer(_output, line => global::Android.Util.Log.Info(StartupClock.LogTag, line));
        _output.Interrupted += (_, reason) => DispatcherQueue.TryEnqueue(() => OnInterrupted(reason));
        _clock.Tick += (_, _) => UpdatePlayerBar();

        BottomBar.RegisterPropertyChangedCallback(
            Uno.Toolkit.UI.TabBar.SelectedIndexProperty,
            (_, _) => SelectDestination(BottomBar.SelectedIndex));

        SizeChanged += (_, e) => ApplyLayout(e.NewSize.Width);
        Loaded += OnLoaded;
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += OnFirstFrame;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyLayout(ActualWidth);
        if (LaunchOptions.AutoPlay)
        {
            LaunchOptions.AutoPlay = false;
            _ = StartAsync();
        }
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
        }
        finally
        {
            _syncing = false;
        }
    }

    private void OnPlayClick(object sender, RoutedEventArgs e)
    {
        if (_player.IsPlaying)
        {
            _player.Pause();
            UpdatePlayerBar();
        }
        else
        {
            _ = StartAsync();
        }
    }

    /// <summary>The output stopped itself: a focus loss reads as a pause, a device failure as an error.</summary>
    private void OnInterrupted(AudioInterruption reason)
    {
        if (reason == AudioInterruption.DeviceFailed)
        {
            _error = "The sound device stopped. Tap Test sound to try again.";
        }
        UpdatePlayerBar();
    }

    private async Task StartAsync()
    {
        if (_starting || _player.IsPlaying) return;
        _starting = true;
        _error = null;
        UpdatePlayerBar();
        try
        {
            await _player.PlayAsync();
            if (!_player.IsPlaying) _error = "Another app is using sound. Try again when it stops.";
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Error(StartupClock.LogTag, $"sample-failed {ex.GetType().Name}: {ex.Message}");
            _error = "The test sound could not start on this device.";
        }
        finally
        {
            _starting = false;
            UpdatePlayerBar();
        }
    }

    private void UpdatePlayerBar()
    {
        var playing = _player.IsPlaying;
        if (playing) _clock.Start(); else _clock.Stop();
        PlayButton.IsEnabled = !_starting;

        if (_starting)
        {
            NowArt.Visibility = Visibility.Visible;
            NowTitle.Text = "Test sound · preparing";
            NowDetail.Text = "Decoding the built-in clip.";
            PlayButton.Content = "Test sound";
        }
        else if (playing)
        {
            NowArt.Visibility = Visibility.Visible;
            NowTitle.Text = "Test sound · playing";
            NowDetail.Text = $"{Format(_player.Elapsed)} · built-in 8 s clip, on repeat";
            PlayButton.Content = "Pause";
        }
        else if (_player.IsLoaded && _error is null)
        {
            NowArt.Visibility = Visibility.Visible;
            NowTitle.Text = "Test sound · paused";
            NowDetail.Text = $"{Format(_player.Elapsed)} · built-in 8 s clip, on repeat";
            PlayButton.Content = "Play";
        }
        else
        {
            // Back to the empty state: a message, not a blank square with 0:00 / 0:00.
            NowArt.Visibility = Visibility.Collapsed;
            NowTitle.Text = "Nothing playing";
            NowDetail.Text = _error ?? "Your library is empty in this preview build. Test sound plays a short built-in clip.";
            PlayButton.Content = "Test sound";
        }
    }

    private static string Format(TimeSpan t) => $"{(int)t.TotalMinutes}:{t.Seconds:00}";
}
