// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
namespace Soncle.Core.Playback;

/// <summary>
/// The player's rules as a UI-free state machine — a port of the player section of
/// <c>renderer/app.js</c> (≈ lines 2025–2840). Every rule learned the hard way in Android
/// 0.1.2–0.1.6 is a test, so it can't regress. Platform differences are options, not
/// <c>if (android)</c> code.
/// </summary>
public sealed class PlaybackController
{
    private readonly IPlayer _player;
    private readonly IPlaybackStreams _streams;
    private readonly IPlaybackClock _clock;
    private readonly PlaybackOptions _options;
    private readonly Func<string, double?> _lufsFor;

    private readonly List<QueueTrack> _queue = new();
    private int _idx = -1;
    private long _loadToken;
    private long _looking;           // the token whose prefetch is in flight (0 = none)
    private bool _holdStart;
    private bool _loading;
    private bool _playing;
    private int _errors;
    private int _stalls;
    private CancellationTokenSource? _stallCts;
    private readonly List<QueueTrack> _autoSkiped = new();

    public PlaybackController(IPlayer player, IPlaybackStreams streams, IPlaybackClock? clock = null,
        PlaybackOptions? options = null, Func<string, double?>? lufsFor = null)
    {
        _player = player;
        _streams = streams;
        _clock = clock ?? new RealPlaybackClock();
        _options = options ?? new PlaybackOptions();
        _lufsFor = lufsFor ?? (_ => null);
        _player.Playing += OnPlaying;
        _player.Paused += OnPaused;
        _player.Ended += OnEnded;
        _player.Waiting += OnWaiting;
    }

    public IReadOnlyList<QueueTrack> Queue => _queue;
    public int Index => _idx;
    public QueueTrack? Current => _idx >= 0 && _idx < _queue.Count ? _queue[_idx] : null;
    public bool Playing => _playing;
    public bool Loading => _loading;
    public bool HoldStart => _holdStart;
    public RepeatMode Repeat { get; set; } = RepeatMode.Off;
    public bool Shuffle { get; private set; }

    /// <summary>Raised when the current track changes.</summary>
    public event Action<QueueTrack>? TrackChanged;
    /// <summary>Raised on errors that stopped playback (after the auto-skip budget).</summary>
    public event Action<QueueTrack, string>? Failed;

    public void SetQueue(IEnumerable<QueueTrack> tracks, int start = 0)
    {
        _queue.Clear();
        _queue.AddRange(tracks.Where(t => !string.IsNullOrEmpty(t.Id)));
        _idx = _queue.Count == 0 ? -1 : Math.Clamp(start, 0, _queue.Count - 1);
        _errors = 0;
        _autoSkiped.Clear();
    }

    /// <summary>Play the queue from <paramref name="i"/>.</summary>
    public Task PlayAtAsync(int i, double startAt = 0, bool autoplay = true, bool viaSkip = false, CancellationToken ct = default)
    {
        if (i < 0 || i >= _queue.Count) return Task.CompletedTask;
        _idx = i;
        var t = _queue[i];
        _holdStart = false;
        _loading = autoplay;
        TrackChanged?.Invoke(t);
        return LoadTrackAsync(t, ++_loadToken, startAt, autoplay, ct);
    }

    private async Task LoadTrackAsync(QueueTrack t, long token, double startAt, bool autoplay, CancellationToken ct)
    {
        _stalls = 0;
        try
        {
            _looking = token;
            var lufs = await PrefetchAsync(t, ct);
            if (token != _loadToken) return;
            _looking = 0;
            // pause pressed while the stream was being looked up: load, but don't start
            var auto = autoplay && !_holdStart;
            await _player.LoadAsync(SourceFor(t), lufs, startAt, auto, ct);
            if (token != _loadToken) return;
            _errors = 0;               // only a successful load clears the auto-skip budget
            if (!auto) { _loading = false; _holdStart = false; }
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            if (token != _loadToken) return;
            await HandlePlayErrorAsync(t, e.Message, token, ct);
        }
    }

    private string SourceFor(QueueTrack t) => "mstream://" + t.Id;

    private async Task<double?> PrefetchAsync(QueueTrack t, CancellationToken ct) => await _streams.PrefetchAsync(t.Id, ct);

    private async Task<double?> TolerantPrefetchAsync(QueueTrack t, CancellationToken ct)
    {
        try { return await _streams.PrefetchAsync(t.Id, ct); }
        catch { return _lufsFor(t.Id); }
    }

    /// <summary>Play/pause. While a song is still being looked up, this only decides whether it starts.</summary>
    public async Task TogglePlayAsync(CancellationToken ct = default)
    {
        if (Current is null) { if (_queue.Count > 0) await PlayAtAsync(Math.Max(0, _idx), ct: ct); return; }
        // a song is still being looked up: hold or release the start
        if (_looking == _loadToken)
        {
            _holdStart = !_holdStart;
            if (!_holdStart) _loading = true;
            else if (_player.State == PlaybackState.Playing) _player.Pause();
            return;
        }
        if (_player.State != PlaybackState.Playing) await PlayAsync(ct);
        else _player.Pause();
    }

    public async Task PlayAsync(CancellationToken ct = default)
    {
        try { await _player.PlayAsync(ct); }
        catch { await PlayAtAsync(_idx, ct: ct); }
    }

    public void Pause() => _player.Pause();

    public void Seek(double seconds) => _player.Seek(seconds);

    /// <summary>System media "play": only plays, never toggles.</summary>
    public async Task MediaPlayAsync(CancellationToken ct = default)
    {
        if (_holdStart || _player.State != PlaybackState.Playing) await PlayAsync(ct);
    }

    /// <summary>System media "pause": only pauses, never toggles.</summary>
    public void MediaPause()
    {
        if (_playing || (_looking == _loadToken && !_holdStart)) _player.Pause();
    }

    public int? NextIndex()
    {
        if (_idx < _queue.Count - 1) return _idx + 1;
        if (Repeat == RepeatMode.All && _queue.Count > 0) return 0;
        return null;
    }

    public async Task NextAsync(bool auto = false, bool error = false, CancellationToken ct = default)
    {
        if (_queue.Count == 0) return;
        if (auto && !error && Repeat == RepeatMode.One) { _player.Seek(0); await PlayAsync(ct); return; }
        var n = NextIndex();
        if (n is not null) { await PlayAtAsync(n.Value, viaSkip: !auto, ct: ct); return; }
        if (auto)
        {
            // no next: autoplay may have filled more (the caller supplies that), otherwise stop
            return;
        }
        if (Repeat == RepeatMode.All) { await PlayAtAsync(0, viaSkip: true, ct: ct); return; }
        _player.Pause();
    }

    public Task PrevAsync(CancellationToken ct = default)
    {
        if (_player.Position > 3 || _idx <= 0) { _player.Seek(0); return Task.CompletedTask; }
        return PlayAtAsync(_idx - 1, viaSkip: true, ct: ct);
    }

    public void SetShuffle(bool on) => Shuffle = on;

    // ---- engine events ----
    private void OnPlaying()
    {
        _playing = true;
        _loading = false;
        _stalls = 0;
        CancelStallWatch();
    }

    private void OnPaused() => _playing = false;

    private void OnEnded()
    {
        // the auto-advance path; repeat-one is handled in NextAsync
        _ = NextAsync(auto: true);
    }

    private void OnWaiting()
    {
        if (_player.State == PlaybackState.Playing) return;
        _loading = true;
        ArmStallWatch();
    }

    // ---- stall watch ----
    private void CancelStallWatch()
    {
        _stallCts?.Cancel();
        _stallCts = null;
    }

    internal void ArmStallWatch(double waitedSeconds = 0)
    {
        CancelStallWatch();
        var cts = new CancellationTokenSource();
        _stallCts = cts;
        var t = Current;
        var token = _loadToken;
        var had = _player.BufferedEnd;
        _ = Task.Run(async () =>
        {
            try
            {
                await _clock.DelayAsync(TimeSpan.FromSeconds(_options.StallReloadSeconds), cts.Token);
                if (token != _loadToken || !_loading || _player.State == PlaybackState.Playing || Current != t) return;
                // still downloading, just slowly: keep waiting (up to the cap) rather than restarting
                if (_player.BufferedEnd > had + 0.2 && waitedSeconds < _options.StallWaitCapSeconds)
                {
                    ArmStallWatch(waitedSeconds + _options.StallReloadSeconds);
                    return;
                }
                _stalls++;
                // phones ride out dead zones longer
                if (_stalls > _options.StallChecks)
                {
                    await HandlePlayErrorAsync(t!, "The stream stopped responding", token, CancellationToken.None);
                    return;
                }
                // fresh stream URL, same position
                await ReloadAtAsync(t!, _player.Position, token, CancellationToken.None);
            }
            catch (OperationCanceledException) { }
        });
    }

    private async Task ReloadAtAsync(QueueTrack t, double pos, long token, CancellationToken ct)
    {
        try
        {
            var lufs = await TolerantPrefetchAsync(t, ct);
            if (token != _loadToken || Current != t) return;
            await _player.LoadAsync(SourceFor(t), lufs, pos, autoplay: true, ct);
        }
        catch (Exception e)
        {
            if (token == _loadToken) await HandlePlayErrorAsync(t, e.Message, token, ct);
        }
    }

    private async Task HandlePlayErrorAsync(QueueTrack t, string message, long token, CancellationToken ct)
    {
        _loading = false;
        _errors++;
        if (_options.AutoSkipOnError && _errors < 4 && _idx < _queue.Count - 1)
        {
            var skipToken = token;
            await _clock.DelayAsync(TimeSpan.FromSeconds(1.5), ct);
            if (skipToken == _loadToken) await NextAsync(auto: true, error: true, ct: ct);
            return;
        }
        Failed?.Invoke(t, message);
    }

    /// <summary>Test seam: simulate an unrequested pause (an AbortError while starting).</summary>
    public void SimulateUnrequestedPause() => _player.Pause();

    /// <summary>Test seam: simulate a stall check firing immediately.</summary>
    public void SimulateWaiting() => OnWaiting();
}
