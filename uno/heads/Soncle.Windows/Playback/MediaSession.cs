// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using Microsoft.UI.Dispatching;
using Windows.Media;
using Windows.Storage.Streams;

namespace Soncle.Windows.Playback;

/// <summary>
/// The Windows media session (SystemMediaTransportControls: the media flyout, keyboard media keys,
/// the lock screen) over the playback slice. It shows the playing title and artists with
/// Playing / Paused, and "nothing playing" (Closed) until a song is chosen. Its buttons arrive on
/// a system thread and are handed to the controller on the UI thread.
/// </summary>
internal sealed class MediaSession : IDisposable
{
    private readonly SystemMediaTransportControls _controls;
    private readonly PlaybackHost _host;
    private readonly DispatcherQueue _ui;
    private string? _shownTitle;
    private string? _shownSubtitle;
    private string? _shownArtwork;
    private MediaPlaybackStatus _shownStatus = MediaPlaybackStatus.Closed;

    public MediaSession(SystemMediaTransportControls controls, PlaybackHost host, DispatcherQueue ui)
    {
        _controls = controls;
        _host = host;
        _ui = ui;
        _controls.IsPlayEnabled = true;
        _controls.IsPauseEnabled = true;
        _controls.IsNextEnabled = true;
        _controls.IsPreviousEnabled = true;
        _controls.ButtonPressed += OnButtonPressed;
    }

    /// <summary>Mirrors the player bar; cheap when nothing changed (call it on the UI thread).</summary>
    public void Update()
    {
        var bar = _host.Bar;
        if (bar.IsEmpty) return;

        var status = bar.IsPlaying ? MediaPlaybackStatus.Playing
            : bar.IsLoading ? MediaPlaybackStatus.Changing
            : MediaPlaybackStatus.Paused;
        if (status != _shownStatus)
        {
            _shownStatus = status;
            _controls.PlaybackStatus = status;
        }

        if (bar.Title == _shownTitle && bar.Subtitle == _shownSubtitle && bar.ThumbnailUrl == _shownArtwork) return;
        _shownTitle = bar.Title;
        _shownSubtitle = bar.Subtitle;
        _shownArtwork = bar.ThumbnailUrl;

        var display = _controls.DisplayUpdater;
        display.ClearAll();
        display.Type = MediaPlaybackType.Music;
        display.MusicProperties.Title = bar.Title;
        display.MusicProperties.Artist = bar.Subtitle;
        if (Uri.TryCreate(bar.ThumbnailUrl, UriKind.Absolute, out var art) && art.Scheme == Uri.UriSchemeHttps)
        {
            display.Thumbnail = RandomAccessStreamReference.CreateFromUri(art);
        }
        display.Update();
    }

    public void Dispose()
    {
        _controls.ButtonPressed -= OnButtonPressed;
        _controls.DisplayUpdater.ClearAll();
        _controls.DisplayUpdater.Update();
        _controls.PlaybackStatus = MediaPlaybackStatus.Closed;
    }

    private void OnButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
    {
        var button = args.Button;
        _ui.TryEnqueue(() => _ = HandleAsync(button));
    }

    private async Task HandleAsync(SystemMediaTransportControlsButton button)
    {
        try
        {
            var controller = _host.Controller;
            if (_host.Bar.IsEmpty) return;
            switch (button)
            {
                case SystemMediaTransportControlsButton.Play:
                    await controller.MediaPlayAsync();
                    break;
                case SystemMediaTransportControlsButton.Pause:
                    controller.MediaPause();
                    break;
                case SystemMediaTransportControlsButton.Next:
                    await controller.NextAsync();
                    break;
                case SystemMediaTransportControlsButton.Previous:
                    await controller.PrevAsync();
                    break;
            }
            _host.Bar.Refresh();
            Update();
        }
        catch (Exception e)
        {
            PlaybackHost.Log($"media-button-failed {button} {e.GetType().Name}");
        }
    }
}
