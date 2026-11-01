// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using Soncle.Audio;
using Soncle.Core.Platform;
using Soncle.Core.Playback;

namespace Soncle.App.Playback;

/// <summary>Opens one song by id (off the UI thread), decoded or streaming; <see cref="YouTubeStreams.LoadSongAsync"/> in the app.</summary>
public delegate Task<IAudioSource> SongLoader(string id, CancellationToken ct);

/// <summary>
/// The <see cref="IPlayer"/> the <see cref="PlaybackController"/> drives, over the shared
/// <see cref="Engine"/> and a head's <see cref="IAudioOutput"/> (WASAPI on Windows, AudioTrack on
/// Android). Each song is opened off the UI thread (<see cref="Task.Run(Func{Task})"/>): a
/// streaming source (<see cref="IBufferingSource"/>) starts as soon as its first audio is decoded
/// and reports buffering when the download falls behind. Each song gets its own engine (normalised to <see cref="NormTarget"/>, limiter last) and is handed to the
/// render thread through <see cref="SwitchingRenderer"/>, so swaps and seeks never allocate or lock
/// on the audio thread.
/// </summary>
/// <remarks>
/// Threading: call every member from one thread (the UI thread in a head). Events are raised on
/// the calling thread only, never on the audio thread; <see cref="LoadAsync"/> resumes on the
/// caller's <see cref="SynchronizationContext"/>. The end of a song is noticed by
/// <see cref="Tick"/>, which the head calls from a UI timer (a DispatcherTimer, ~4 Hz); so is the
/// start of buffering (<see cref="Waiting"/>).
/// </remarks>
public sealed class EnginePlayer : IPlayer, IDisposable
{
    /// <summary>The loudness target songs are normalised to (YouTube's reference, the JS default).</summary>
    public const double NormTarget = -14;

    private const string Scheme = "mstream://";

    private readonly IAudioOutput _output;
    private readonly SongLoader _load;
    private readonly SwitchingRenderer _renderer = new();
    private readonly AudioRenderCallback _callback;   // one delegate for the device's whole life

    private LoadedSong? _song;
    private long _loadSeq;
    private bool _endedRaised;
    private bool _waitingRaised;
    private bool _disposed;

    public EnginePlayer(IAudioOutput output, SongLoader load)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _load = load ?? throw new ArgumentNullException(nameof(load));
        if (output.Channels != 2) throw new NotSupportedException($"the output must be stereo (has {output.Channels} channels)");
        _callback = _renderer.Render;
    }

    public PlaybackState State { get; private set; } = PlaybackState.Idle;

    /// <summary>The id of the loaded song (null before the first load).</summary>
    public string? CurrentId { get; private set; }

    public double Position
    {
        get
        {
            var s = _song;
            if (s is null || s.SampleRate <= 0) return 0;
            var seek = Volatile.Read(ref s.SeekFrame);
            var frames = seek >= 0 ? seek : s.Source.Position;
            if (s.LengthFrames > 0) frames = Math.Min(frames, s.LengthFrames);
            return frames / (double)s.SampleRate;
        }
    }

    public double Duration => _song is { SampleRate: > 0 } s ? s.LengthFrames / (double)s.SampleRate : 0;

    /// <summary>How far the song is available, s: a decoded song all of it, a streaming one what is decoded or downloaded.</summary>
    public double BufferedEnd => _song switch
    {
        { SampleRate: > 0, Source: IBufferingSource b } s => b.BufferedEndFrame / (double)s.SampleRate,
        _ => Duration,
    };

    /// <summary>The playing song ran dry (silence until more is decoded); <see cref="State"/> stays Playing.</summary>
    public bool IsBuffering =>
        State == PlaybackState.Playing && _song is { Source: IBufferingSource { IsBuffering: true } } s && ReferenceEquals(_renderer.Current, s);

    public event Action? Playing;
    public event Action? Paused;
    public event Action? Ended;

    /// <summary>Raised by <see cref="Tick"/> when the playing song starts buffering (once per stall), like an &lt;audio&gt; element's "waiting".</summary>
    public event Action? Waiting;

    /// <summary>
    /// Loads <paramref name="source"/> (<c>mstream://&lt;id&gt;</c> or a bare id), decoding it off the
    /// calling thread. A load overtaken by a newer one ends with <see cref="OperationCanceledException"/>.
    /// </summary>
    /// <exception cref="NotSupportedException">The song's rate differs from the output's.</exception>
    public async Task LoadAsync(string source, double? lufs, double startAt, bool autoplay, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(source);
        var id = source.StartsWith(Scheme, StringComparison.Ordinal) ? source[Scheme.Length..] : source;
        var seq = ++_loadSeq;
        var rate = _output.SampleRate;
        var wasPlaying = _output.IsRunning;
        State = PlaybackState.Loading;

        LoadedSong song;
        try
        {
            song = await Task.Run(async () =>
            {
                var pcm = await _load(id, ct).ConfigureAwait(false);
                try
                {
                    if (pcm.SampleRate != rate)
                        throw new NotSupportedException($"the song is {pcm.SampleRate} Hz but the output runs at {rate} Hz");
                    var start = Math.Max(0, double.IsFinite(startAt) ? startAt : 0);
                    var built = new LoadedSong(pcm, lufs, start);
                    ct.ThrowIfCancellationRequested();
                    return built;
                }
                catch
                {
                    (pcm as IDisposable)?.Dispose();
                    throw;
                }
            }, ct);
        }
        catch (OperationCanceledException) when (seq == _loadSeq)
        {
            // cancelled by the caller: whatever was playing carries on
            State = _song is null ? PlaybackState.Idle : _output.IsRunning ? PlaybackState.Playing : PlaybackState.Paused;
            throw;
        }
        catch when (seq == _loadSeq)
        {
            // like an <audio> element whose new src failed: the old song doesn't keep playing
            if (_output.IsRunning) _output.Stop();
            State = PlaybackState.Error;
            if (wasPlaying) Paused?.Invoke();
            throw;
        }
        if (seq != _loadSeq)
        {
            song.Release();
            throw new OperationCanceledException("a newer song was loaded");
        }

        var old = _song;
        _song = song;
        CurrentId = id;
        _endedRaised = false;
        _waitingRaised = false;
        _renderer.Publish(song, rampOut: _output.IsRunning);
        // the render thread may still play one ramped block of it: a released source stays readable
        old?.Release();

        if (autoplay) await PlayAsync(ct);
        else if (_output.IsRunning || wasPlaying) Pause();
        else State = PlaybackState.Paused;
    }

    public Task PlayAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var s = _song ?? throw new InvalidOperationException("nothing is loaded");
        // play() after the end starts the song again, like an <audio> element
        if (s.Pump.Ended && Volatile.Read(ref s.SeekFrame) < 0) Seek(0);
        _output.Start(_callback);
        _endedRaised = false;
        State = PlaybackState.Playing;
        Playing?.Invoke();
        return Task.CompletedTask;
    }

    public void Pause()
    {
        if (_output.IsRunning) _output.Stop();
        if (_song is null) return;
        State = PlaybackState.Paused;
        Paused?.Invoke();
    }

    /// <summary>Asks the render thread to move to <paramref name="seconds"/> (clamped to the song).</summary>
    public void Seek(double seconds)
    {
        var s = _song;
        if (s is null || !double.IsFinite(seconds)) return;
        var frame = (long)Math.Round(Math.Max(0, seconds) * s.SampleRate);
        if (s.LengthFrames > 0) frame = Math.Min(frame, s.LengthFrames);
        s.RampOut = _output.IsRunning;
        Interlocked.Exchange(ref s.SeekFrame, frame);
        _endedRaised = false;
    }

    /// <summary>
    /// The non-audio-thread poll (call it from a UI timer): when the playing song has been rendered
    /// to its end, stops the output and raises <see cref="Paused"/> then <see cref="Ended"/>, once.
    /// </summary>
    public void Tick()
    {
        var s = _song;
        if (s is null || _endedRaised || State != PlaybackState.Playing) return;
        var buffering = IsBuffering;
        if (buffering && !_waitingRaised) Waiting?.Invoke();
        _waitingRaised = buffering;
        // the song must be the one rendering and have no seek waiting
        if (!ReferenceEquals(_renderer.Current, s) || Volatile.Read(ref s.SeekFrame) >= 0 || !s.Pump.Ended) return;
        _endedRaised = true;
        Pause();
        Ended?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _output.Stop();
        _song?.Release();
        State = PlaybackState.Idle;
    }
}
