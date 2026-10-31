// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using Soncle.Audio;
using Soncle.Audio.Decode;
using Soncle.Audio.Output;
using Soncle.Core.Platform;
using Soncle.Core.Playback;
using Soncle.Streams;
using Soncle.YouTube;

namespace Soncle.App.Tests;

/// <summary>
/// A sound device the test drives: <see cref="Start"/> keeps the callback and <see cref="Pull"/>
/// calls it into a preallocated buffer, exactly as a device thread would.
/// </summary>
internal sealed class FakeAudioOutput : IAudioOutput
{
    private AudioRenderCallback? _render;

    public FakeAudioOutput(int sampleRate = 48_000, int blockFrames = 480)
    {
        SampleRate = sampleRate;
        Buffer = new float[blockFrames * 2];
    }

    public int SampleRate { get; }
    public int Channels => 2;
    public bool IsRunning { get; private set; }
    public int Starts { get; private set; }
    public int Stops { get; private set; }

    /// <summary>The last block rendered (interleaved).</summary>
    public float[] Buffer { get; }

    public void Start(AudioRenderCallback render)
    {
        if (IsRunning) return;
        _render = render;
        IsRunning = true;
        Starts++;
    }

    public void Stop()
    {
        if (IsRunning) Stops++;
        IsRunning = false;
    }

    /// <summary>Renders one block into <see cref="Buffer"/>; silence (no call) when stopped.</summary>
    public int Pull()
    {
        if (!IsRunning || _render is null) { Array.Clear(Buffer); return 0; }
        return _render(Buffer);
    }

    /// <summary>Renders <paramref name="blocks"/> blocks and returns them concatenated (allocates: not for allocation tests).</summary>
    public float[] Record(int blocks)
    {
        var all = new float[blocks * Buffer.Length];
        for (var b = 0; b < blocks; b++)
        {
            Pull();
            Buffer.CopyTo(all, b * Buffer.Length);
        }
        return all;
    }

    public void Dispose() => Stop();
}

/// <summary>
/// A sound device whose <see cref="Stop"/> returns before the session ends, like WASAPI and
/// AudioTrack: the stopped session still pulls one faded last block from the shared callback.
/// <see cref="Start"/> hands over the way <c>WasapiAudioOutput</c> does (worst case: the old
/// session's last block is not due yet), closing the old session's <see cref="RenderGate"/>.
/// Counts how many sessions are inside the callback at once.
/// </summary>
internal sealed class DeferredStopOutput : IAudioOutput
{
    private int _inside;
    private int _maxInside;

    public DeferredStopOutput(int blockFrames = 480) => BlockFrames = blockFrames;

    public int BlockFrames { get; }
    public int SampleRate => 48_000;
    public int Channels => 2;
    public Session? Current { get; private set; }
    public bool IsRunning => Current is { StopRequested: false };

    /// <summary>The most sessions ever inside the callback at the same time (must stay 1).</summary>
    public int MaxConcurrentRenders => Volatile.Read(ref _maxInside);

    public void Start(AudioRenderCallback render)
    {
        if (IsRunning) return;
        if (Current is { } old && !old.Gate.Close(5_000)) throw new InvalidOperationException("old session still rendering");
        Current = new Session(this, render);
    }

    public void Stop()
    {
        if (Current is { } s) s.StopRequested = true;
    }

    public void Dispose() => Stop();

    /// <summary>One Start..Stop; <see cref="Pull"/> is its device thread's read (a GainRamp fade on the last block).</summary>
    internal sealed class Session
    {
        private readonly DeferredStopOutput _owner;
        private readonly AudioRenderCallback _render;
        private volatile bool _stopRequested;
        private bool _first = true;

        public Session(DeferredStopOutput owner, AudioRenderCallback render)
        {
            _owner = owner;
            _render = render;
            Buffer = new float[owner.BlockFrames * 2];
        }

        public RenderGate Gate { get; } = new();
        public float[] Buffer { get; }
        public bool Finished { get; private set; }
        public int Renders { get; private set; }

        public bool StopRequested
        {
            get => _stopRequested;
            set => _stopRequested = value;
        }

        /// <summary>Pulls one block into <see cref="Buffer"/>; returns the frames the callback rendered.</summary>
        public int Pull()
        {
            var frames = 0;
            if (Finished) { Array.Clear(Buffer); return 0; }
            var stopping = _stopRequested;
            if (Gate.TryEnter())
            {
                try
                {
                    var n = Interlocked.Increment(ref _owner._inside);
                    int seen;
                    while (n > (seen = Volatile.Read(ref _owner._maxInside)) && Interlocked.CompareExchange(ref _owner._maxInside, n, seen) != seen) { }
                    frames = _render(Buffer);
                    Renders++;
                }
                finally
                {
                    Interlocked.Decrement(ref _owner._inside);
                    Gate.Exit();
                }
            }
            else
            {
                stopping = true;
            }
            Buffer.AsSpan(frames * 2).Clear();
            if (_first) GainRamp.Apply(Buffer, 2, 0f, 1f);
            if (stopping)
            {
                GainRamp.Apply(Buffer, 2, 1f, 0f);
                Finished = true;
            }
            _first = false;
            return frames;
        }
    }
}

/// <summary>A codec stand-in: every packet decodes to its TOC-given length of a quiet constant, so decode tests run without Concentus.</summary>
internal sealed class FakeOpusCodec : IOpusDecoderFactory
{
    public const float Level = 0.25f;

    public IOpusDecoder Create(int sampleRate, int channels) => new Decoder(channels);

    private sealed class Decoder : IOpusDecoder
    {
        private readonly int _ch;
        public Decoder(int ch) => _ch = ch;
        public int Decode(ReadOnlySpan<byte> packet, Span<float> pcm, int frameSize)
        {
            var n = Math.Min(OpusToc.Samples(packet), frameSize);
            pcm[..(n * _ch)].Fill(Level);
            return n;
        }
        public void Dispose() { }
    }
}

/// <summary>Serves a byte array as googlevideo would (inclusive ranges); counts requests. No network.</summary>
internal sealed class FakeFetcher : IStreamFetcher
{
    private readonly byte[] _data;
    public FakeFetcher(byte[] data) => _data = data;
    public int Calls;

    public Task<Stream> GetAsync(string url, IReadOnlyDictionary<string, string> headers, long from, long to, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref Calls);
        var end = (int)Math.Min(to, _data.Length - 1L);
        var slice = new byte[Math.Max(0, end - (int)from + 1)];
        Array.Copy(_data, from, slice, 0, slice.Length);
        return Task.FromResult<Stream>(new MemoryStream(slice, writable: false));
    }
}

/// <summary>A player client that answers every client with the given formats.</summary>
internal sealed class FakeStreamClient : IStreamClient
{
    private readonly AudioFormat[] _formats;
    public FakeStreamClient(params AudioFormat[] formats) => _formats = formats;
    public string UserAgent => "test-ua";
    public Task<PlayerResponse> GetPlayerAsync(string videoId, string client, string? potToken, CancellationToken ct) =>
        Task.FromResult(new PlayerResponse("OK", null, _formats));
    public Task<string?> CanSeekAsync(string client, string url, IReadOnlyDictionary<string, string> headers, long length, CancellationToken ct) =>
        Task.FromResult<string?>(null);
    public Task<long> ProbeLengthAsync(string client, string url, IReadOnlyDictionary<string, string> headers, CancellationToken ct) =>
        Task.FromResult(0L);
}

/// <summary>Streams that know every song and report no loudness.</summary>
internal sealed class FakeStreams : IPlaybackStreams
{
    public readonly List<string> Prefetched = new();
    public Task<double?> PrefetchAsync(string id, CancellationToken ct)
    {
        Prefetched.Add(id);
        return Task.FromResult<double?>(null);
    }
}

/// <summary>A clock whose delays finish at once (the controller's auto-skip wait).</summary>
internal sealed class InstantClock : IPlaybackClock
{
    public long NowMs => 0;
    public Task DelayAsync(TimeSpan delay, CancellationToken ct) => Task.CompletedTask;
}

/// <summary>An <see cref="IPlayer"/> that records loads; <see cref="Fail"/> makes loads throw.</summary>
internal sealed class FakePlayer : IPlayer
{
    public readonly List<string> Loads = new();
    public string? Fail;
    public PlaybackState State { get; set; } = PlaybackState.Idle;
    public double Position { get; set; }
    public double Duration { get; set; }
    public double BufferedEnd => Duration;

    public Task LoadAsync(string source, double? lufs, double startAt, bool autoplay, CancellationToken ct)
    {
        Loads.Add(source);
        if (Fail is not null) throw new InvalidOperationException(Fail);
        State = autoplay ? PlaybackState.Playing : PlaybackState.Paused;
        if (autoplay) Playing?.Invoke();
        return Task.CompletedTask;
    }

    public Task PlayAsync(CancellationToken ct) { State = PlaybackState.Playing; Playing?.Invoke(); return Task.CompletedTask; }
    public void Pause() { State = PlaybackState.Paused; Paused?.Invoke(); }
    public void Seek(double seconds) => Position = seconds;
    public event Action? Playing;
    public event Action? Paused;
    public event Action? Ended { add { } remove { } }
    public event Action? Waiting { add { } remove { } }
}

internal static class Pcm
{
    /// <summary>A stereo sine (different per channel) as an in-memory song.</summary>
    public static MemoryPcmSource Sine(double seconds, double freq = 440, double amp = 0.3, int rate = 48_000)
    {
        var frames = (int)(seconds * rate);
        var data = new float[frames * 2];
        for (var i = 0; i < frames; i++)
        {
            data[2 * i] = (float)(amp * Math.Sin(2 * Math.PI * freq * i / rate));
            data[2 * i + 1] = (float)(amp * Math.Sin(2 * Math.PI * freq * 1.5 * i / rate));
        }
        return new MemoryPcmSource(data, rate);
    }

    public static double Peak(ReadOnlySpan<float> s)
    {
        var p = 0.0;
        foreach (var x in s) p = Math.Max(p, Math.Abs(x));
        return p;
    }
}
