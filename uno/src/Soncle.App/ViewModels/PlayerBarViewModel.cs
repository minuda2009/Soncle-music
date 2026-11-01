// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using Soncle.Core.Playback;

namespace Soncle.App.ViewModels;

/// <summary>
/// The player bar (DESIGN.md §4.1): what is playing, play/pause, previous/next and the seek bar.
/// <see cref="IsEmpty"/> stays true until a song is chosen, so the head keeps its empty-state
/// message instead of a blank square with 0:00 / 0:00.
/// </summary>
/// <remarks>
/// Threading: create it on the UI thread. Controller and player events that arrive on another
/// thread are posted to the <see cref="SynchronizationContext"/> captured at construction, so
/// <see cref="ObservableObject.PropertyChanged"/> is raised on the UI thread only. The head calls
/// <see cref="Refresh"/> from a UI timer (after <c>EnginePlayer.Tick</c>) to move the position.
/// </remarks>
public sealed class PlayerBarViewModel : ObservableObject
{
    /// <summary>The user-facing message when a song can't be played.</summary>
    public const string CouldNotPlay = "Couldn't play this song.";

    private readonly PlaybackController _controller;
    private readonly IPlayer _player;
    private readonly Func<string, SongItem?> _describe;
    private readonly SynchronizationContext? _ui;

    private bool _isEmpty = true;
    private string _title = "";
    private string _subtitle = "";
    private string _thumbnailUrl = "";
    private bool _isPlaying;
    private bool _isLoading;
    private double _positionSeconds;
    private double _durationSeconds;
    private string? _errorMessage;
    private string? _errorDetail;

    /// <param name="describe">Looks up a queued id's row (e.g. <see cref="SearchViewModel.Find"/>) for artists and artwork.</param>
    public PlayerBarViewModel(PlaybackController controller, IPlayer player, Func<string, SongItem?>? describe = null)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _describe = describe ?? (_ => null);
        _ui = SynchronizationContext.Current;
        _controller.TrackChanged += t => OnUi(() => OnTrackChanged(t));
        _controller.Failed += (t, reason) => OnUi(() => OnFailed(reason));
        _player.Playing += () => OnUi(Refresh);
        _player.Paused += () => OnUi(Refresh);
        _player.Ended += () => OnUi(Refresh);
    }

    public bool IsEmpty { get => _isEmpty; private set => SetProperty(ref _isEmpty, value); }
    public string Title { get => _title; private set => SetProperty(ref _title, value); }
    /// <summary>The artists line.</summary>
    public string Subtitle { get => _subtitle; private set => SetProperty(ref _subtitle, value); }
    public string ThumbnailUrl { get => _thumbnailUrl; private set => SetProperty(ref _thumbnailUrl, value); }
    public bool IsPlaying { get => _isPlaying; private set => SetProperty(ref _isPlaying, value); }
    /// <summary>A song was chosen and is being looked up, downloaded or decoded.</summary>
    public bool IsLoading { get => _isLoading; private set => SetProperty(ref _isLoading, value); }

    /// <summary>For the seek slider.</summary>
    public double PositionSeconds
    {
        get => _positionSeconds;
        private set { if (SetProperty(ref _positionSeconds, value)) OnPropertyChanged(nameof(PositionText)); }
    }

    /// <summary>For the seek slider's maximum.</summary>
    public double DurationSeconds
    {
        get => _durationSeconds;
        private set { if (SetProperty(ref _durationSeconds, value)) OnPropertyChanged(nameof(DurationText)); }
    }

    public string PositionText => TimeText.Format(PositionSeconds);
    public string DurationText => TimeText.Format(DurationSeconds);

    /// <summary>Plain, user-facing (<see cref="CouldNotPlay"/>); null when nothing failed.</summary>
    public string? ErrorMessage { get => _errorMessage; private set => SetProperty(ref _errorMessage, value); }
    /// <summary>Why it failed, for diagnostics (not for the UI).</summary>
    public string? ErrorDetail { get => _errorDetail; private set => SetProperty(ref _errorDetail, value); }

    public Task TogglePlayAsync() => _controller.TogglePlayAsync();
    public Task NextAsync() => _controller.NextAsync();
    public Task PrevAsync() => _controller.PrevAsync();

    /// <summary>Seeks the current song (clamped to it) and moves the slider at once.</summary>
    public void Seek(double seconds)
    {
        if (IsEmpty || !double.IsFinite(seconds)) return;
        var max = _player.Duration > 0 ? _player.Duration : DurationSeconds;
        var to = Math.Clamp(seconds, 0, Math.Max(0, max));
        _controller.Seek(to);
        PositionSeconds = to;
    }

    /// <summary>Pulls state, position and duration from the player (call it from a UI timer).</summary>
    public void Refresh()
    {
        if (IsEmpty) return;
        IsPlaying = _player.State == PlaybackState.Playing;
        IsLoading = !IsPlaying && ErrorMessage is null && (_controller.Loading || _player.State == PlaybackState.Loading);
        if (_player.State is PlaybackState.Playing or PlaybackState.Paused)
        {
            if (_player.Duration > 0) DurationSeconds = _player.Duration;
            PositionSeconds = Math.Clamp(_player.Position, 0, Math.Max(0, DurationSeconds));
        }
    }

    private void OnTrackChanged(QueueTrack t)
    {
        var row = _describe(t.Id);
        IsEmpty = false;
        ErrorMessage = null;
        ErrorDetail = null;
        Title = !string.IsNullOrEmpty(t.Title) ? t.Title : row?.Title ?? "";
        Subtitle = row?.Artists ?? "";
        ThumbnailUrl = row?.ThumbnailUrl ?? "";
        PositionSeconds = 0;
        DurationSeconds = t.Duration > 0 ? t.Duration : row?.DurationSeconds ?? 0;
        IsPlaying = false;
        IsLoading = true;
    }

    private void OnFailed(string reason)
    {
        ErrorMessage = CouldNotPlay;
        ErrorDetail = reason;
        IsLoading = false;
        IsPlaying = _player.State == PlaybackState.Playing;
    }

    private void OnUi(Action action)
    {
        if (_ui is null || SynchronizationContext.Current == _ui) action();
        else _ui.Post(static a => ((Action)a!)(), action);
    }
}
