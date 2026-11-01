// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using Soncle.Audio;
using Soncle.Audio.Decode;
using Soncle.Audio.Output;
using Soncle.TestKit;

namespace Soncle.Audio.Tests;

/// <summary>
/// Streaming start: the incremental demuxer and the decode-ahead source, over a fake download that
/// grows on demand (no network). A stateless fake codec makes every packet decode to values derived
/// from its bytes, so a streamed (or seeked) read can be compared sample for sample with the
/// whole-file decode.
/// </summary>
public class StreamingOpusSourceTests
{
    private const int Sr = 48_000;
    private static readonly string[] Files = { "flow/sample.webm", "flow/sample-tailcues.webm", "flow/mono.webm" };

    private static byte[] Bytes(string name) => File.ReadAllBytes(Fixtures.Path(name));

    /// <summary>Each packet decodes to its TOC length of values derived from its bytes and the sample index (no state).</summary>
    internal sealed class HashCodec : IOpusDecoderFactory
    {
        public IOpusDecoder Create(int sampleRate, int channels) => new D(channels);
        private sealed class D(int ch) : IOpusDecoder
        {
            public int Decode(ReadOnlySpan<byte> packet, Span<float> pcm, int frameSize)
            {
                var n = Math.Min(OpusToc.Samples(packet), frameSize);
                var h = 0;
                foreach (var b in packet) h = h * 31 + b;
                for (var i = 0; i < n; i++)
                {
                    var v = (((h + i) & 0x3ff) - 512) / 2048f;
                    pcm[i * ch] = v;
                    if (ch == 2) pcm[i * ch + 1] = -v * 0.5f;
                }
                return n;
            }
            public void Dispose() { }
        }
    }

    /// <summary>Every packet decodes to a constant level, so a fade is easy to see.</summary>
    internal sealed class ConstCodec : IOpusDecoderFactory
    {
        public const float Level = 0.25f;
        public IOpusDecoder Create(int sampleRate, int channels) => new D(channels);
        private sealed class D(int ch) : IOpusDecoder
        {
            public int Decode(ReadOnlySpan<byte> packet, Span<float> pcm, int frameSize)
            {
                var n = Math.Min(OpusToc.Samples(packet), frameSize);
                pcm[..(n * ch)].Fill(Level);
                return n;
            }
            public void Dispose() { }
        }
    }

    /// <summary>
    /// A song's download as the SongDownloader serves it: <see cref="Have"/> bytes are in the cache
    /// (a read past them waits), an open beyond them goes "to the network" (all bytes from there).
    /// Records every open.
    /// </summary>
    internal sealed class FakeDownload(byte[] data)
    {
        private long _have;
        private long _maxRead;
        public byte[] Data { get; } = data;
        public long Have { get => Volatile.Read(ref _have); set => Volatile.Write(ref _have, Math.Min(value, Data.Length)); }
        /// <summary>The furthest byte any cache reader got to.</summary>
        public long MaxRead => Volatile.Read(ref _maxRead);
        public List<(long Offset, bool Network)> Opens { get; } = new();

        public Stream Open(long offset)
        {
            var network = offset > Have;
            lock (Opens) Opens.Add((offset, network));
            return network ? new MemoryStream(Data, (int)offset, Data.Length - (int)offset, writable: false) : new Cache(this, offset);
        }

        public (long Offset, bool Network) LastOpen { get { lock (Opens) return Opens[^1]; } }
        public int OpenCount { get { lock (Opens) return Opens.Count; } }

        private sealed class Cache(FakeDownload d, long pos) : Stream
        {
            private long _pos = pos;

            private int TryRead(Span<byte> buffer)
            {
                var n = (int)Math.Min(buffer.Length, d.Have - _pos);
                if (n <= 0) return n;
                d.Data.AsSpan((int)_pos, n).CopyTo(buffer);
                _pos += n;
                long seen;
                while (_pos > (seen = d.MaxRead) && Interlocked.CompareExchange(ref d._maxRead, _pos, seen) != seen) { }
                return n;
            }

            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                while (true)
                {
                    if (_pos >= d.Data.Length) return 0;
                    var n = TryRead(buffer.Span);
                    if (n > 0) return n;
                    await Task.Delay(1, cancellationToken);
                }
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                while (true)
                {
                    if (_pos >= d.Data.Length) return 0;
                    var n = TryRead(buffer.AsSpan(offset, count));
                    if (n > 0) return n;
                    Thread.Sleep(1);
                }
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => d.Data.Length;
            public override long Position { get => _pos; set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }

    private static StreamingOpusSource Source(FakeDownload d, IOpusDecoderFactory codec, int ringFrames = StreamingOpusSource.DefaultRingFrames) =>
        new(d.Open, codec, ringFrames: ringFrames);

    private static async Task Until(Func<bool> cond, string what)
    {
        for (var i = 0; i < 2000 && !cond(); i++) await Task.Delay(2);
        Assert.True(cond(), what);
    }

    /// <summary>Waits until <paramref name="frames"/> are decoded ahead (or the decode is complete).</summary>
    private static Task Ahead(StreamingOpusSource s, int frames) =>
        Until(() => s.DecodeComplete || s.DecodedEndFrame - s.Position >= frames, "the decoder never got ahead");

    /// <summary>Reads up to <paramref name="maxFrames"/> (to the end by default), never letting the ring run dry; interleaved.</summary>
    internal static async Task<float[]> ReadAsync(StreamingOpusSource s, long maxFrames = long.MaxValue, int block = 480)
    {
        var l = new float[block];
        var r = new float[block];
        var all = new List<float>();
        while (all.Count / 2 < maxFrames)
        {
            var want = (int)Math.Min(block, maxFrames - all.Count / 2);
            await Ahead(s, want + StreamingOpusSource.ResumeRampFrames);   // a block plus the fade-out reserve
            var n = s.Read(l, r, want);
            for (var i = 0; i < n; i++) { all.Add(l[i]); all.Add(r[i]); }
            if (n < want) break;
        }
        return all.ToArray();
    }

    private static void SameSamples(float[] want, int fromFrame, float[] got)
    {
        Assert.True(fromFrame * 2 + got.Length <= want.Length, $"{got.Length / 2} frames from {fromFrame} run past the song ({want.Length / 2})");
        var bad = -1;
        for (var i = 0; i < got.Length && bad < 0; i++)
            if (want[fromFrame * 2 + i] != got[i]) bad = i;
        Assert.True(bad < 0, bad < 0 ? "" : $"sample {bad} (frame {fromFrame + bad / 2}) is {got[bad]}, the whole-file decode has {want[fromFrame * 2 + bad]}");
    }

    [Fact]
    public void TheStreamReaderYieldsTheWholeFileReadersPackets()
    {
        foreach (var name in Files)
        {
            var bytes = Bytes(name);
            var want = WebmOpusReader.Read(bytes)!;
            var reader = new WebmOpusStreamReader(new MemoryStream(bytes));
            Assert.Equal(want.Head, reader.ReadHead());
            var i = 0;
            while (reader.Next(out var data, out var timeNs, out var discard))
            {
                var p = want.Packets[i++];
                Assert.True(p.Data.Span.SequenceEqual(data.Span), $"{name} packet {i}");
                Assert.Equal(p.TimeNs, timeNs);
                Assert.Equal(p.Samples, OpusToc.Samples(data.Span));
                Assert.Equal(p.DiscardPaddingNs, discard);
            }
            Assert.Equal(want.Packets.Count, i);
        }
        // YouTube's layout has the Cues in front: they are read with the head
        var head = new WebmOpusStreamReader(new MemoryStream(Bytes("flow/sample.webm")));
        head.ReadHead();
        Assert.True(head.Cues is { Count: > 2 });
        Assert.True(head.FirstCluster > 0 && head.Cues![^1].Position > head.FirstCluster);
        var tail = new WebmOpusStreamReader(new MemoryStream(Bytes("flow/sample-tailcues.webm")));
        tail.ReadHead();
        Assert.Null(tail.Cues);
    }

    [Fact]
    public async Task ReadToTheEndItIsTheWholeFileDecodeAndEndsThere()
    {
        foreach (var name in Files)
        {
            var bytes = Bytes(name);
            var whole = OpusWebmDecoder.Decode(bytes, new HashCodec());
            var d = new FakeDownload(bytes) { Have = bytes.Length };
            using var s = Source(d, new HashCodec(), ringFrames: 1 << 15);   // small ring: wraps many times
            await s.Ready;
            var got = await ReadAsync(s);
            Assert.Equal(whole.Frames, got.Length / 2);
            SameSamples(whole.InterleavedStereo, 0, got);
            Assert.True(s.Ended, name);
            Assert.False(s.IsBuffering);
            Assert.Equal(whole.Frames, s.LengthFrames);
            Assert.Equal(whole.Frames, s.Position);
            Assert.Equal(0, s.Read(new float[16], new float[16], 16));
            Assert.Null(s.Failure);
        }
    }

    [Fact]
    public async Task TheFirstAudioComesAfterAFewKilobytes()
    {
        var bytes = Bytes("flow/sample.webm");
        var whole = OpusWebmDecoder.Decode(bytes, new HashCodec());
        var d = new FakeDownload(bytes) { Have = 8 * 1024 };   // of ~43 KB
        using var s = Source(d, new HashCodec());
        await s.Ready;
        var got = await ReadAsync(s, 4800);
        Assert.Equal(4800, got.Length / 2);
        SameSamples(whole.InterleavedStereo, 0, got);
        Assert.True(d.MaxRead <= 8 * 1024, $"read {d.MaxRead} bytes");
        Assert.False(s.Ended);
        // the length is the file's Duration until the end is decoded
        Assert.InRange(s.LengthFrames!.Value, 7 * Sr, 9 * Sr);
    }

    /// <summary>Waits until the decoder has used up what has arrived; returns the decoded end.</summary>
    private static async Task<long> Settled(StreamingOpusSource s)
    {
        long last;
        do { last = s.DecodedEndFrame; await Task.Delay(30); } while (s.DecodedEndFrame != last);
        return last;
    }

    [Fact]
    public async Task AnUnderrunPlaysSilenceReportsBufferingAndFadesBackIn()
    {
        const int R = StreamingOpusSource.ResumeRampFrames;
        var bytes = Bytes("flow/sample.webm");
        var d = new FakeDownload(bytes) { Have = 12 * 1024 };
        using var s = Source(d, new ConstCodec());
        await s.Ready;
        var last = await Settled(s);
        Assert.True(last > 4 * R);

        var l = new float[480];
        var r = new float[480];
        var blocks = 0;
        while (!s.IsBuffering && blocks++ < 1000) Assert.Equal(480, s.Read(l, r, 480));
        Assert.True(s.IsBuffering);
        Assert.False(s.Ended);
        // the block that ran low faded out over the whole reserve, from full level to silence
        Assert.True(l[0] > ConstCodec.Level * 0.99f, $"fade starts at {l[0]}");
        Assert.Equal(0f, l[479]);
        for (var i = 1; i < 480; i++) Assert.True(l[i] < l[i - 1], "a fade, not a step");
        // what is left of the reserve stays in the ring for after the stall
        Assert.InRange(s.Position, last - R, last);
        var stalledAt = s.Position;

        // still dry: a block of silence, still buffering, the position doesn't run on
        Assert.Equal(480, s.Read(l, r, 480));
        Assert.All(l, x => Assert.Equal(0f, x));
        Assert.True(s.IsBuffering);
        Assert.Equal(stalledAt, s.Position);

        // the download catches up: the audio comes back with a fade, no jump
        d.Have = bytes.Length;
        await Ahead(s, 960 + R);
        Assert.Equal(480, s.Read(l, r, 480));
        Assert.False(s.IsBuffering);
        Assert.True(l[0] <= ConstCodec.Level / 400, $"first resumed sample {l[0]}");
        Assert.Equal(ConstCodec.Level, l[479], 6);
        for (var i = 1; i < 480; i++) Assert.True(Math.Abs(l[i] - l[i - 1]) < ConstCodec.Level / 200, "a ramp, not a step");
        Assert.Equal(stalledAt + 480, s.Position);
    }

    [Theory]
    [InlineData(480)]
    [InlineData(128)]
    [InlineData(1000)]
    [InlineData(64)]
    [InlineData(2048)]
    public async Task AStallNeverStepsTheGainWhereverItFallsOnTheBlockGrid(int block)
    {
        var bytes = Bytes("flow/sample.webm");
        var d = new FakeDownload(bytes) { Have = 12 * 1024 };
        using var s = Source(d, new ConstCodec());
        await s.Ready;
        var last = await Settled(s);

        var l = new float[block];
        var r = new float[block];
        var played = new List<float>();
        void Play(int count)
        {
            for (var b = 0; b < count; b++)
            {
                Assert.Equal(block, s.Read(l, r, block));
                played.AddRange(l);
            }
        }
        var blocks = 0;
        while (!s.IsBuffering && blocks++ < 10_000) Play(1);
        Play(2 + 2 * StreamingOpusSource.ResumeRampFrames / block);   // the fade ends, then silence
        Assert.True(s.IsBuffering);
        Assert.Equal(0f, played[^1]);
        Assert.InRange(s.Position, last - block, last);   // less than a block stays in the ring for later

        d.Have = bytes.Length;
        var resume = 2 + StreamingOpusSource.ResumeRampFrames / block;   // the whole fade-in and a bit
        await Ahead(s, (resume + 1) * block + StreamingOpusSource.ResumeRampFrames);
        Play(resume);
        Assert.False(s.IsBuffering);
        Assert.Equal(ConstCodec.Level, played[^1], 6);

        // full level -> faded out -> silence -> faded in, with no step anywhere (the song's start,
        // sample 0, is Engine.Load's fade-in)
        for (var i = 1; i < played.Count; i++)
            Assert.True(Math.Abs(played[i] - played[i - 1]) <= ConstCodec.Level / 400, $"a step of {played[i] - played[i - 1]} at frame {i} (block {block})");
    }

    /// <summary>An opener over <paramref name="d"/> that fails while <see cref="Broken"/> is set, or (cache reads) past <see cref="FailAt"/>.</summary>
    private sealed class Flaky(FakeDownload d)
    {
        public volatile bool Broken;
        public long FailAt = long.MaxValue;
        public Stream Open(long offset)
        {
            if (Broken) throw new IOException("network gone");
            return new Failing(d.Open(offset), offset, this);
        }

        private sealed class Failing(Stream inner, long pos, Flaky f) : Stream
        {
            private long _pos = pos;
            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                var room = Volatile.Read(ref f.FailAt) - _pos;
                if (room <= 0) throw new IOException("stopped");
                var n = await inner.ReadAsync(buffer[..(int)Math.Min(buffer.Length, room)], cancellationToken);
                _pos += n;
                return n;
            }
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => inner.Length;
            public override long Position { get => _pos; set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            protected override void Dispose(bool disposing)
            {
                if (disposing) inner.Dispose();
                base.Dispose(disposing);
            }
        }
    }

    [Fact]
    public async Task ADownloadThatFailsBeforeTheFirstAudioFaultsReady()
    {
        var bytes = Bytes("flow/sample.webm");
        var head = new WebmOpusStreamReader(new MemoryStream(bytes));
        head.ReadHead();
        var d = new FakeDownload(bytes) { Have = bytes.Length };
        // the head arrives, then the download stops a few bytes into the first cluster
        var flaky = new Flaky(d) { FailAt = head.FirstCluster + 200 };
        using var s = new StreamingOpusSource(flaky.Open, new HashCodec());
        var e = await Assert.ThrowsAsync<IOException>(() => s.Ready);
        Assert.Equal("stopped", e.Message);
    }

    [Fact]
    public async Task AFailureMidSongEndsItAndASeekToTheSameSpotRetries()
    {
        var bytes = Bytes("flow/sample.webm");
        var whole = OpusWebmDecoder.Decode(bytes, new HashCodec());
        var d = new FakeDownload(bytes) { Have = bytes.Length };
        var flaky = new Flaky(d) { FailAt = bytes.Length / 2 };
        using var s = new StreamingOpusSource(flaky.Open, new HashCodec());
        await s.Ready;
        var got = await ReadAsync(s);
        Assert.True(s.Ended);
        Assert.NotNull(s.Failure);
        Assert.True(got.Length / 2 < whole.Frames);
        SameSamples(whole.InterleavedStereo, 0, got);

        // a seek fails right away (no network), before anything is read
        flaky.Broken = true;
        s.Seek(2 * Sr);
        await Until(() => s.DecodeComplete, "the failed seek never ended");
        Assert.Equal("network gone", s.Failure);
        Assert.Equal(0, s.Read(new float[480], new float[480], 480));

        // the network is back: the same seek again retries (not swallowed as a repeat)
        flaky.Broken = false;
        flaky.FailAt = long.MaxValue;
        s.Seek(2 * Sr);
        SameSamples(whole.InterleavedStereo, 2 * Sr, await ReadAsync(s, Sr));
        Assert.Null(s.Failure);
    }

    [Fact]
    public async Task ASeekPastTheRealEndDoesNotBecomeTheLength()
    {
        var bytes = Bytes("flow/sample.webm");
        var whole = OpusWebmDecoder.Decode(bytes, new HashCodec());
        var d = new FakeDownload(bytes) { Have = bytes.Length };
        // an overestimate (a slider maximum past the end)
        using var s = new StreamingOpusSource(d.Open, new HashCodec(), estimatedFrames: 30 * Sr);
        await s.Ready;
        s.Seek(20 * Sr);
        Assert.Empty(await ReadAsync(s));
        Assert.True(s.Ended);
        Assert.Equal(whole.Frames, s.LengthFrames);

        s.Seek(Sr);
        Assert.Equal(whole.Frames - Sr, (await ReadAsync(s)).Length / 2);
        Assert.Equal(whole.Frames, s.LengthFrames);
    }

    [Fact]
    public async Task DisposeWhileTheHeadIsStillDownloadingEndsTheLoad()
    {
        var bytes = Bytes("flow/sample.webm");
        var d = new FakeDownload(bytes) { Have = 16 };   // not even the EBML header
        var s = Source(d, new HashCodec());
        await Task.Delay(20);
        Assert.False(s.Ready.IsCompleted);
        s.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => s.Ready.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task ASeekInsideTheDownloadStartsAtACueAndIsSampleExact()
    {
        var bytes = Bytes("flow/sample.webm");
        var whole = OpusWebmDecoder.Decode(bytes, new HashCodec());
        var d = new FakeDownload(bytes) { Have = bytes.Length };
        using var s = Source(d, new HashCodec());
        await s.Ready;
        await ReadAsync(s, Sr / 2);

        var opens = d.OpenCount;
        s.Seek(5 * Sr);
        s.Seek(5 * Sr);   // the renderer seeks up to three times: once is enough
        s.Seek(5 * Sr);
        Assert.Equal(5 * Sr, s.Position);
        var got = await ReadAsync(s, Sr);
        SameSamples(whole.InterleavedStereo, 5 * Sr, got);
        Assert.Equal(6 * Sr, s.Position);
        Assert.Equal(opens + 1, d.OpenCount);

        var head = new WebmOpusStreamReader(new MemoryStream(bytes));
        head.ReadHead();
        var (offset, network) = d.LastOpen;
        Assert.False(network);
        Assert.Contains(head.Cues!, c => c.Position == offset);
        Assert.True(offset > head.FirstCluster, "decoding restarts at a cue, not at the first cluster");
        Assert.True(head.Cues!.Single(c => c.Position == offset).TimeNs <= 5_000_000_000L - 80_000_000L, "at least 80 ms of pre-roll");

        // and back to the start
        s.Seek(0);
        SameSamples(whole.InterleavedStereo, 0, await ReadAsync(s, Sr / 4));
    }

    [Fact]
    public async Task ASeekPastTheDownloadReopensAtAByteOffsetBeyondIt()
    {
        var bytes = Bytes("flow/sample.webm");
        var whole = OpusWebmDecoder.Decode(bytes, new HashCodec());
        var d = new FakeDownload(bytes) { Have = bytes.Length * 3 / 10 };
        using var s = Source(d, new HashCodec());
        await s.Ready;
        await ReadAsync(s, Sr / 2);

        s.Seek(7 * Sr);
        Assert.True(s.IsBuffering || s.DecodedEndFrame >= 7 * Sr);
        var got = await ReadAsync(s);
        SameSamples(whole.InterleavedStereo, 7 * Sr, got);
        Assert.Equal(whole.Frames - 7 * Sr, got.Length / 2);
        Assert.True(s.Ended);
        var (offset, network) = d.LastOpen;
        Assert.True(network, "served from the network");
        Assert.True(offset > d.Have, $"reopened at {offset}, past the {d.Have} downloaded bytes");
    }

    [Fact]
    public async Task WithoutCuesInFrontASeekDecodesFromTheFirstCluster()
    {
        var bytes = Bytes("flow/sample-tailcues.webm");
        var whole = OpusWebmDecoder.Decode(bytes, new HashCodec());
        var d = new FakeDownload(bytes) { Have = bytes.Length };
        using var s = Source(d, new HashCodec());
        await s.Ready;
        s.Seek(5 * Sr);
        var got = await ReadAsync(s, Sr);
        SameSamples(whole.InterleavedStereo, 5 * Sr, got);
        var head = new WebmOpusStreamReader(new MemoryStream(bytes));
        head.ReadHead();
        Assert.Equal(head.FirstCluster, d.LastOpen.Offset);
    }

    [Fact]
    public async Task TheLengthIsLiveAndOnlyTheRealEndEndsThePump()
    {
        var bytes = Bytes("flow/sample.webm");
        var whole = OpusWebmDecoder.Decode(bytes, new HashCodec());
        var d = new FakeDownload(bytes) { Have = bytes.Length };
        // the file's Duration wins over a (too short) caller estimate
        using var s = new StreamingOpusSource(d.Open, new HashCodec(), estimatedFrames: Sr);
        await s.Ready;
        var pump = new OutputPump(new Engine(Sr)) { Source = s };
        Assert.False(pump.Ended);
        Assert.InRange(s.LengthFrames!.Value, 7 * Sr, 9 * Sr);
        await ReadAsync(s, whole.Frames - 10);
        Assert.False(pump.Ended);
        await ReadAsync(s);
        Assert.True(pump.Ended);
        Assert.Equal(whole.Frames, s.LengthFrames);
        // a seek after the end plays again
        s.Seek(Sr);
        Assert.False(pump.Ended);
    }

    [Fact]
    public async Task RenderingAStreamingSongAllocatesNothingPerBlock()
    {
        var bytes = Bytes("flow/sample.webm");
        var d = new FakeDownload(bytes) { Have = bytes.Length };
        using var s = Source(d, new HashCodec());
        await s.Ready;
        var engine = new Engine(Sr);
        engine.Load(s, lufs: null);
        var pump = new OutputPump(engine) { Source = s };
        var buffer = new float[480 * 2];
        await Ahead(s, 480 * 120);
        for (var i = 0; i < 20; i++) pump.Fill(buffer);   // warm up (JIT)
        await Ahead(s, 480 * 100);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++) pump.Fill(buffer);
        var steady = GC.GetAllocatedBytesForCurrentThread() - before;

        // a seek and the dry blocks right after it (buffering) don't allocate either
        before = GC.GetAllocatedBytesForCurrentThread();
        s.Seek(6 * Sr);
        for (var i = 0; i < 100; i++) pump.Fill(buffer);
        var dry = GC.GetAllocatedBytesForCurrentThread() - before;

        // 0 B locally; the bound leaves room for runtime noise but not for any per-block
        // allocation (the smallest object is 24 B, and each window has 100 blocks)
        Assert.True(steady < 1024, $"steady {steady} B");
        Assert.True(dry < 1024, $"seek/underrun {dry} B");
    }

    [Fact]
    public async Task AFileThatIsNotOpusFailsReadyAndDisposeIsSafe()
    {
        var junk = new FakeDownload(new byte[4096]) { Have = 4096 };
        using (var bad = Source(junk, new HashCodec()))
            await Assert.ThrowsAsync<NotSupportedException>(() => bad.Ready);

        var bytes = Bytes("flow/sample.webm");
        var d = new FakeDownload(bytes) { Have = 4 * 1024 };
        var s = Source(d, new HashCodec());
        await s.Ready;
        s.Dispose();
        var l = new float[480];
        var r = new float[480];
        for (var i = 0; i < 50; i++) Assert.True(s.Read(l, r, 480) >= 0);   // stays readable (the render thread may still pull one block)
        s.Seek(Sr);
        Assert.Equal(480, s.Read(l, r, 480));
    }
}
