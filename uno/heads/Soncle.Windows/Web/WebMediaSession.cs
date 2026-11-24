// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Text.Json;
using Microsoft.UI.Dispatching;
using Windows.Media;
using Windows.Storage.Streams;

namespace Soncle.Windows.Web;

/// <summary>
/// Windows' media controls (media keys, the volume flyout, the lock screen) for the web UI. The
/// page's navigator.mediaSession is forwarded here as "mediaUpdate" (mobile/src/backend.js, the
/// same shim the Android app uses); buttons and seeks go back to the page as "mediaAction" events.
/// </summary>
internal sealed class WebMediaSession : IDisposable
{
    private readonly SystemMediaTransportControls _controls;
    private readonly DispatcherQueue _ui;
    private readonly Action<string, double?> _action;
    private string? _shownKey;

    public WebMediaSession(Window window, DispatcherQueue ui, Action<string, double?> action)
    {
        _ui = ui;
        _action = action;
        _controls = WindowChrome.UseMediaSession(window);
        _controls.IsPlayEnabled = true;
        _controls.IsPauseEnabled = true;
        _controls.IsNextEnabled = true;
        _controls.IsPreviousEnabled = true;
        _controls.IsStopEnabled = false;
        _controls.ButtonPressed += OnButton;
        _controls.PlaybackPositionChangeRequested += OnSeek;
    }

    /// <summary>{ title, artist, album, artwork, playing, position, duration, rate } from the page.</summary>
    public void Update(JsonElement a)
    {
        string S(string k) => a.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        double D(string k) => a.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
        var playing = a.TryGetProperty("playing", out var p) && p.ValueKind == JsonValueKind.True;
        var title = S("title");

        _controls.PlaybackStatus = string.IsNullOrEmpty(title) ? MediaPlaybackStatus.Closed
            : playing ? MediaPlaybackStatus.Playing : MediaPlaybackStatus.Paused;

        var key = title + "\n" + S("artist") + "\n" + S("album") + "\n" + S("artwork");
        if (key != _shownKey && title.Length > 0)
        {
            _shownKey = key;
            var display = _controls.DisplayUpdater;
            display.ClearAll();
            display.Type = MediaPlaybackType.Music;
            display.MusicProperties.Title = title;
            display.MusicProperties.Artist = S("artist");
            display.MusicProperties.AlbumTitle = S("album");
            if (Uri.TryCreate(S("artwork"), UriKind.Absolute, out var art) && art.Scheme == Uri.UriSchemeHttps)
                display.Thumbnail = RandomAccessStreamReference.CreateFromUri(art);
            display.Update();
        }

        var duration = D("duration");
        if (duration > 0)
        {
            var timeline = new SystemMediaTransportControlsTimelineProperties
            {
                StartTime = TimeSpan.Zero,
                MinSeekTime = TimeSpan.Zero,
                EndTime = TimeSpan.FromSeconds(duration),
                MaxSeekTime = TimeSpan.FromSeconds(duration),
                Position = TimeSpan.FromSeconds(Math.Clamp(D("position"), 0, duration)),
            };
            _controls.UpdateTimelineProperties(timeline);
        }
    }

    private void OnButton(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
    {
        var action = args.Button switch
        {
            SystemMediaTransportControlsButton.Play => "play",
            SystemMediaTransportControlsButton.Pause => "pause",
            SystemMediaTransportControlsButton.Next => "nexttrack",
            SystemMediaTransportControlsButton.Previous => "previoustrack",
            SystemMediaTransportControlsButton.Stop => "stop",
            _ => null,
        };
        if (action is not null) _ui.TryEnqueue(() => _action(action, null));
    }

    private void OnSeek(SystemMediaTransportControls sender, PlaybackPositionChangeRequestedEventArgs args)
    {
        var to = args.RequestedPlaybackPosition.TotalSeconds;
        _ui.TryEnqueue(() => _action("seekto", to));
    }

    public void Dispose()
    {
        try
        {
            _controls.ButtonPressed -= OnButton;
            _controls.PlaybackPositionChangeRequested -= OnSeek;
            _controls.DisplayUpdater.ClearAll();
            _controls.DisplayUpdater.Update();
            _controls.PlaybackStatus = MediaPlaybackStatus.Closed;
        }
        catch
        {
            // the window is going away
        }
    }
}
