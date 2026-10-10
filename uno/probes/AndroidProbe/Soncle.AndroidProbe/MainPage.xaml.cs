// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;

namespace Soncle.AndroidProbe;

public sealed record Song(string Title, string Artist, SolidColorBrush Tint);

public sealed partial class MainPage : Page
{
    public MainPage()
    {
        InitializeComponent();
        var songs = new List<Song>(500);
        for (var i = 0; i < 500; i++)
        {
            var c = Windows.UI.Color.FromArgb(255, (byte)(60 + i * 37 % 160), (byte)(60 + i * 53 % 160), (byte)(90 + i * 71 % 150));
            songs.Add(new Song($"Song {i + 1}", $"Artist {i % 40 + 1}", new SolidColorBrush(c)));
        }
        Songs.ItemsSource = songs;
        Startup.Text = "Measuring start-up…";
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += OnFirstFrame;
    }

    private void OnFirstFrame(object? sender, object e)
    {
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= OnFirstFrame;
        var ms = StartupClock.MillisecondsSinceProcessStart();
        Startup.Text = ms is { } v ? $"First frame {v} ms after process start" : "";
        StartupClock.Report(ms);
    }

    private void OnSongClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Song s)
        {
            NowTitle.Text = $"{s.Title} · {s.Artist}";
            NowArt.Background = s.Tint;
        }
    }
}

internal static partial class StartupClock
{
#if __ANDROID__
    public static long? MillisecondsSinceProcessStart() =>
        Android.OS.SystemClock.ElapsedRealtime() - Android.OS.Process.StartElapsedRealtime;

    // adb logcat -s SonclePROBE  →  "first-frame-ms=…"
    public static void Report(long? ms) => Android.Util.Log.Info("SonclePROBE", $"first-frame-ms={ms}");
#else
    public static long? MillisecondsSinceProcessStart() =>
        (long)(DateTime.Now - System.Diagnostics.Process.GetCurrentProcess().StartTime).TotalMilliseconds;

    public static void Report(long? ms) => Console.WriteLine($"SonclePROBE first-frame-ms={ms}");
#endif
}
