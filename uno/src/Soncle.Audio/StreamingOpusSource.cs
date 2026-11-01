// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using Soncle.Audio.Decode;

namespace Soncle.Audio;

/// <summary>
/// A WebM/Opus song that plays while it downloads: a decode-ahead thread demuxes
/// (<see cref="WebmOpusStreamReader"/>) and decodes into a preallocated single-producer /
/// single-consumer ring, and <see cref="Read"/> on the audio thread only copies out of it. The
/// decoding follows <see cref="OpusWebmDecoder.Decode"/> exactly (pre-skip, output gain,
/// DiscardPadding, mono as dual mono), so a song read start to end is sample-for-sample the
/// whole-file decode.
/// <list type="bullet">
/// <item><see cref="Read"/> never blocks, locks, allocates or touches the network. When the ring
/// is dry before the end it fades the last frames out, plays silence, reports
/// <see cref="IsBuffering"/> and fades back in over <see cref="ResumeRampFrames"/> when audio is
/// there again (so an underrun never clicks).</item>
/// <item><see cref="Seek"/> is lock-free: it bumps a request generation (a seqlock with the
/// frame); the decode thread notices it, aborts a read that waits for bytes, reopens the bytes at
/// the cluster of the last cue at least <see cref="PreRollSamples"/> before the target (the start
/// of the clusters when the file has no Cues in front), resets the decoder and drops what comes
/// before the target. Ring data of an older generation is skipped, never played. Reopening at a
/// byte offset is the opener's business: the <c>SongDownloader</c> serves it from its cache, or
/// from the network when it is far past the download.</item>
/// <item><see cref="LengthFrames"/> is live: the exact length once the end is decoded, until then
/// the estimate (Info Duration, or the caller's), never less than what is decoded. The end is
/// <see cref="Ended"/>, so an estimate that is too short never ends the song early.</item>
/// </list>
/// A failure after the start (the download gave up, a damaged packet) ends the song where it is
/// (<see cref="Failure"/> says why); a seek tries again.
/// </summary>
/// <remarks>
/// Threading: <see cref="Read"/>, <see cref="Seek"/> and the <see cref="Position"/> setter are the
/// consumer side, called by one thread at a time (the render thread, or the loader before the
/// song is published). Everything else may be read from any thread. <see cref="Dispose"/> stops
/// the decode thread without waiting; reading after it stays safe (the ring is kept).
/// </remarks>
public sealed class StreamingOpusSource : IBufferingSource, IDisposable
{
    public const int SampleRate48k = OpusWebmDecoder.SampleRate;
    /// <summary>The ring's default size, frames (about 5.5 s at 48 kHz, 2 MB).</summary>
    public const int DefaultRingFrames = 1 << 18;
    /// <summary>Decoding restarts this many samples (80 ms, RFC 7845 §4.6) before a seek target so the decoder has converged.</summary>
    public const int PreRollSamples = 3840;
    /// <summary>The fade-in after an underrun and the fade-out before one, frames (10 ms).</summary>
    public const int ResumeRampFrames = 480;
    private const int IdleMs = 2;

    private readonly Func<long, Stream> _open;
    private readonly IOpusDecoderFactory _codec;
    private readonly long? _estimatedFrames;
    private readonly Func<double>? _downloaded;
    private readonly float[] _ring;   // interleaved stereo
    private readonly long _mask;
    private readonly int _capacity;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Thread _thread;

    // request (consumer -> producer): a seqlock, odd while being written
    private long _reqSeq;
    private long _reqFrame;
    // segment / ring state (producer -> consumer)
    private long _segGen = -1;
    private long _segStart;
    private long _segStartFrame;
    private long _write;
    private long _eosGen = -1;
    private long _eosWrite;
    private long _exactLength = -1;
    private long _infoFrames = -1;
    private long _decodedEnd;
    private long _readIdx;   // consumer-owned, read by the producer
    private volatile bool _disposed;
    private volatile bool _buffering;
    private volatile bool _ended;
    private volatile string? _failure;

    // consumer only
    private long _gen;
    private bool _adopted;
    private long _pos;
    private long _sinceSeek;
    private int _rampLeft;

    /// <param name="open">Opens the song's bytes at an absolute offset (reads may wait for bytes still on the way).</param>
    /// <param name="codec">The Opus codec.</param>
    /// <param name="estimatedFrames">A length estimate for when the file's Info has no Duration (null = unknown).</param>
    /// <param name="downloadedFraction">How much of the file is downloaded (0..1), for <see cref="BufferedEndFrame"/>; null = only what is decoded.</param>
    /// <param name="ringFrames">The ring size, frames (rounded up to a power of two, at least 4 packets).</param>
    public StreamingOpusSource(Func<long, Stream> open, IOpusDecoderFactory codec, long? estimatedFrames = null,
        Func<double>? downloadedFraction = null, int ringFrames = DefaultRingFrames)
    {
        _open = open ?? throw new ArgumentNullException(nameof(open));
        _codec = codec ?? throw new ArgumentNullException(nameof(codec));
        _estimatedFrames = estimatedFrames is > 0 ? estimatedFrames : null;
        _downloaded = downloadedFraction;
        var cap = 1;
        while (cap < Math.Max(ringFrames, 4 * OpusToc.MaxSamples)) cap <<= 1;
        _capacity = cap;
        _mask = cap - 1;
        _ring = new float[cap * 2];
        _thread = new Thread(Run) { IsBackground = true, Name = "Soncle decode-ahead" };
        _thread.Start();
    }

    /// <summary>Done once the head is parsed and the first audio is decoded (or the song turned out empty); faults when the file can't play.</summary>
    public Task Ready => _ready.Task;

    public int SampleRate => SampleRate48k;
    public bool CanSeek => true;

    public long Position
    {
        get => Volatile.Read(ref _pos);
        set => Seek(value);
    }

    public long? LengthFrames
    {
        get
        {
            var exact = Volatile.Read(ref _exactLength);
            if (exact >= 0) return exact;
            var info = Volatile.Read(ref _infoFrames);
            var estimate = info > 0 ? info : _estimatedFrames;
            return estimate is { } e ? Math.Max(e, DecodedEndFrame) : null;
        }
    }

    public bool IsBuffering => _buffering && !_ended;
    public bool Ended => _ended;

    /// <summary>The end of what is decoded ahead, in frames (for the latest seek; its target until the decoder starts there).</summary>
    public long DecodedEndFrame
    {
        get
        {
            // the segment's generation first: its end is published after its start frame
            var gen = Volatile.Read(ref _reqSeq);
            var current = Volatile.Read(ref _segGen) == gen;
            var end = Volatile.Read(ref _decodedEnd);
            return current ? end : Volatile.Read(ref _reqFrame);
        }
    }

    /// <summary>Whether the decode thread reached the end for the latest seek (nothing more will be decoded).</summary>
    public bool DecodeComplete => Volatile.Read(ref _eosGen) == Volatile.Read(ref _reqSeq);

    public long BufferedEndFrame
    {
        get
        {
            var decoded = DecodedEndFrame;
            if (_downloaded is null || LengthFrames is not { } length) return decoded;
            var f = _downloaded();
            return double.IsFinite(f) ? Math.Max(decoded, (long)(Math.Clamp(f, 0, 1) * length)) : decoded;
        }
    }

    /// <summary>Why decoding stopped early, or null.</summary>
    public string? Failure => _failure;

    // ------------------------------------------------------------------ consumer (audio thread)

    public void Seek(long frame)
    {
        frame = Math.Max(0, frame);
        // idempotent: the renderer and Engine.Load seek to the same frame up to three times
        if (frame == _pos && _sinceSeek == 0) return;
        var s = _reqSeq;
        Interlocked.Exchange(ref _reqSeq, s + 1);
        Interlocked.Exchange(ref _reqFrame, frame);
        Interlocked.Exchange(ref _reqSeq, s + 2);
        _gen = s + 2;
        _adopted = false;
        Volatile.Write(ref _pos, frame);
        _sinceSeek = 0;
        _ended = false;
        _buffering = true;   // until the decoder has audio at the new spot
    }

    /// <summary>Copies decoded audio; never blocks or allocates. Returns <paramref name="frames"/> except at the real end.</summary>
    public int Read(Span<float> left, Span<float> right, int frames)
    {
        if (frames <= 0) return 0;
        if (!_adopted && Volatile.Read(ref _segGen) == _gen)
        {
            Volatile.Write(ref _readIdx, Volatile.Read(ref _segStart));
            _adopted = true;
        }

        long avail = 0;
        var eos = false;
        if (_adopted)
        {
            eos = Volatile.Read(ref _eosGen) == _gen;
            avail = Volatile.Read(ref _write) - _readIdx;
        }
        var n = (int)Math.Min(frames, avail);
        var read = _readIdx;
        var ring = _ring;
        for (var i = 0; i < n; i++)
        {
            var at = (int)((read + i) & _mask) * 2;
            left[i] = ring[at];
            right[i] = ring[at + 1];
        }
        if (_rampLeft > 0 && n > 0)
        {
            // fading back in after an underrun
            var k = Math.Min(n, _rampLeft);
            for (var i = 0; i < k; i++)
            {
                var g = (ResumeRampFrames - _rampLeft + 1) / (float)ResumeRampFrames;
                left[i] *= g; right[i] *= g;
                _rampLeft--;
            }
        }
        Volatile.Write(ref _readIdx, read + n);
        Volatile.Write(ref _pos, _pos + n);
        _sinceSeek += n;

        if (n == frames)
        {
            _buffering = false;
            return n;
        }
        if (eos)
        {
            _ended = true;
            _buffering = false;
            return n;
        }

        // underrun: fade out what we have, then silence until the decoder catches up
        var tail = Math.Min(n, ResumeRampFrames);
        for (var i = 0; i < tail; i++)
        {
            var g = (tail - 1 - i) / (float)tail;
            left[n - tail + i] *= g; right[n - tail + i] *= g;
        }
        left[n..frames].Clear();
        right[n..frames].Clear();
        _rampLeft = ResumeRampFrames;
        _buffering = true;
        return frames;
    }

    public void Dispose() => _disposed = true;

    // ------------------------------------------------------------------ producer (decode thread)

    private sealed class Interrupted : Exception { }

    private Stream? _stream;
    private CancellationTokenSource _cts = new();
    private bool _interruptible;
    private long _curGen;

    private (long Gen, long Frame) Request()
    {
        while (true)
        {
            var g = Volatile.Read(ref _reqSeq);
            if ((g & 1) != 0) { Thread.Yield(); continue; }
            var f = Volatile.Read(ref _reqFrame);
            if (Volatile.Read(ref _reqSeq) == g) return (g, f);
        }
    }

    private bool ShouldStop => _disposed || (_interruptible && Volatile.Read(ref _reqSeq) != _curGen);

    /// <summary>A read that may wait for bytes, abandoned (cancelled) as soon as a seek or Dispose comes.</summary>
    private int ReadBytes(Memory<byte> buffer)
    {
        if (ShouldStop) throw new Interrupted();
        var stream = _stream!;
        var vt = stream.ReadAsync(buffer, _cts.Token);
        if (vt.IsCompletedSuccessfully) return vt.Result;
        var t = vt.AsTask();
        while (!((IAsyncResult)t).AsyncWaitHandle.WaitOne(IdleMs * 5))
        {
            if (!ShouldStop) continue;
            _cts.Cancel();
            _ = t.ContinueWith(static x => _ = x.Exception, TaskScheduler.Default);   // observed, never rethrown
            throw new Interrupted();
        }
        return t.GetAwaiter().GetResult();
    }

    private void Reopen(long position)
    {
        CloseStream();
        _stream = _open(position);
    }

    private void CloseStream()
    {
        if (_cts.IsCancellationRequested)
        {
            _cts.Dispose();
            _cts = new CancellationTokenSource();
        }
        try { _stream?.Dispose(); } catch (IOException) { } catch (ObjectDisposedException) { }
        _stream = null;
    }

    private long FreeFrames() => _capacity - (_write - Math.Max(Volatile.Read(ref _readIdx), _segStart));

    private void Run()
    {
        IOpusDecoder? dec = null;
        try
        {
            WebmOpusStreamReader reader;
            OpusHead head;
            try
            {
                Reopen(0);
                reader = new WebmOpusStreamReader(ReadBytes, 0);
                head = reader.ReadHead();
                if (reader.DurationNs is { } ns) Volatile.Write(ref _infoFrames, Math.Max(0, ns * SampleRate48k / 1_000_000_000L - head.PreSkip));
                dec = _codec.Create(SampleRate48k, head.Channels);
            }
            catch (Exception e)
            {
                _ready.TrySetException(e is Interrupted ? new ObjectDisposedException(nameof(StreamingOpusSource)) : e);
                return;
            }

            var ch = head.Channels;
            var gain = (float)Math.Pow(10, head.OutputGainDb / 20.0);
            var pcm = new float[OpusToc.MaxSamples * ch];
            long counter = 0, skipTo = head.PreSkip;   // stream samples decoded / where output begins
            long t0Ns = -1;                              // the first packet's time (the stream's sample 0)
            var counterFromTime = false;
            var eos = false;
            _curGen = 0;
            PublishSegment(0, 0);
            _interruptible = true;

            while (!_disposed)
            {
                try
                {
                    var (g, f) = Request();
                    if (g != _curGen)
                    {
                        _curGen = g;
                        eos = false;
                        skipTo = f + head.PreSkip;
                        var (pos, fromCue) = ClusterFor(reader, skipTo, t0Ns);
                        if (fromCue && t0Ns < 0) t0Ns = reader.Cues![0].TimeNs;
                        counter = 0;
                        counterFromTime = fromCue;
                        PublishSegment(g, f);
                        Reopen(pos);
                        reader.Reopen(ReadBytes, pos);
                        dec.Dispose();
                        dec = _codec.Create(SampleRate48k, ch);
                        continue;
                    }
                    if (eos || FreeFrames() < OpusToc.MaxSamples)
                    {
                        Thread.Sleep(IdleMs);
                        continue;
                    }
                    if (!reader.Next(out var packet, out var timeNs, out var discardNs))
                    {
                        eos = true;
                        PublishEnd(failed: false);
                        continue;
                    }
                    if (t0Ns < 0) t0Ns = timeNs;
                    var samples = OpusToc.Samples(packet.Span);
                    if (counterFromTime && samples > 0)
                    {
                        // block times are rounded to the timecode scale (1 ms): snap to the packet grid
                        var est = (timeNs - t0Ns) * (double)SampleRate48k / 1e9;
                        counter = (long)Math.Round(est / samples) * samples;
                        counterFromTime = false;
                    }
                    // a packet we cannot size (0 samples) is skipped, like an empty one
                    if (samples == 0) continue;
                    var n = dec.Decode(packet.Span, pcm, OpusToc.MaxSamples);
                    if (n <= 0) throw new InvalidDataException($"the Opus decoder refused a packet (code {n})");
                    var from = counter < skipTo ? (int)Math.Min(n, skipTo - counter) : 0;
                    var to = n;
                    if (discardNs > 0) to = (int)Math.Max(from, n - discardNs * SampleRate48k / 1_000_000_000L);
                    counter += n;
                    var w = _write;
                    for (var i = from; i < to; i++, w++)
                    {
                        var at = (int)(w & _mask) * 2;
                        if (ch == 1) _ring[at] = _ring[at + 1] = pcm[i] * gain;
                        else { _ring[at] = pcm[i * 2] * gain; _ring[at + 1] = pcm[i * 2 + 1] * gain; }
                    }
                    Volatile.Write(ref _write, w);
                    Volatile.Write(ref _decodedEnd, _segStartFrame + (w - _segStart));
                    if (w > _segStart) _ready.TrySetResult();
                }
                catch (Interrupted) { /* a seek or Dispose: the loop picks it up */ }
                catch (Exception e) when (e is not Interrupted)
                {
                    if (_disposed) break;
                    // the song ends where it is; a later seek tries again
                    _failure = e.Message;
                    eos = true;
                    PublishEnd(failed: true);
                }
            }
        }
        catch (Exception e)
        {
            _failure = e.Message;
            _ready.TrySetException(e);
            PublishEnd(failed: true);
        }
        finally
        {
            CloseStream();
            dec?.Dispose();
            _cts.Dispose();
            _ready.TrySetException(new ObjectDisposedException(nameof(StreamingOpusSource)));
        }
    }

    /// <summary>The byte position to decode from for stream sample <paramref name="skipTo"/>, and whether it is a cue (true) or the first cluster.</summary>
    private static (long Pos, bool FromCue) ClusterFor(WebmOpusStreamReader reader, long skipTo, long t0Ns)
    {
        var target = skipTo - PreRollSamples;
        var cues = reader.Cues;
        long pos = -1;
        if (cues is { Count: > 0 } && target > 0)
        {
            var t0 = t0Ns >= 0 ? t0Ns : cues[0].TimeNs;
            for (var i = 0; i < cues.Count; i++)
            {
                if ((cues[i].TimeNs - t0) * SampleRate48k / 1_000_000_000L > target) break;
                if (i > 0 || cues[i].Position != reader.FirstCluster) pos = cues[i].Position;
            }
        }
        return pos >= 0 ? (pos, true) : (reader.FirstCluster, false);
    }

    private void PublishSegment(long gen, long frame)
    {
        _segStartFrame = frame;
        Volatile.Write(ref _segStart, _write);
        Volatile.Write(ref _decodedEnd, frame);
        Volatile.Write(ref _segGen, gen);
    }

    private void PublishEnd(bool failed)
    {
        if (!failed && _exactLength < 0) Volatile.Write(ref _exactLength, _segStartFrame + (_write - _segStart));
        Volatile.Write(ref _eosWrite, _write);
        Volatile.Write(ref _eosGen, _curGen);
        _ready.TrySetResult();
    }
}
