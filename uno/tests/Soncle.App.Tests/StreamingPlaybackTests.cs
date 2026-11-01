// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using Soncle.App.Playback;
using Soncle.App.ViewModels;
using Soncle.Audio;
using Soncle.Core.Playback;

namespace Soncle.App.Tests;

/// <summary>
/// The player over a streaming source: buffering reaches <see cref="EnginePlayer"/> and the
/// player bar, an estimated length never ends a song early, and a replaced song's decoding stops.
/// </summary>
public class StreamingPlaybackTests
{
    /// <summary>A stereo sine whose data the test lets run dry, with an estimated length that may be short.</summary>
    private sealed class FakeStreamingSource(double estimateSeconds) : IBufferingSource, IDisposable
    {
        private long _pos;
        public volatile bool Dry;
        public volatile bool AtEnd;
        public bool Disposed { get; private set; }
        public int SampleRate => 48_000;
        public bool CanSeek => true;
        public long Position { get => Volatile.Read(ref _pos); set => _pos = value; }
        public long? LengthFrames => Math.Max((long)(estimateSeconds * 48_000), Position);
        public bool IsBuffering => Dry && !AtEnd;
        public bool Ended => AtEnd;
        public long BufferedEndFrame => Position + (Dry ? 0 : 48_000);
        public void Seek(long frame) => _pos = frame;
        public void Dispose() => Disposed = true;

        public int Read(Span<float> left, Span<float> right, int frames)
        {
            if (AtEnd) return 0;
            if (Dry)
            {
                left[..frames].Clear();
                right[..frames].Clear();
                return frames;
            }
            for (var i = 0; i < frames; i++)
            {
                left[i] = (float)(0.3 * Math.Sin(2 * Math.PI * 440 * (_pos + i) / 48_000));
                right[i] = left[i];
            }
            Volatile.Write(ref _pos, _pos + frames);
            return frames;
        }
    }

    private static EnginePlayer Player(FakeAudioOutput output, params IAudioSource[] songs)
    {
        var byId = songs.Select((s, i) => (s, i)).ToDictionary(p => "s" + p.i, p => p.s);
        return new EnginePlayer(output, (id, ct) => Task.FromResult(byId[id]));
    }

    [Fact]
    public async Task AnUnderrunShowsAsBufferingInThePlayerAndTheBarThenClears()
    {
        var output = new FakeAudioOutput();
        var song = new FakeStreamingSource(60);
        using var player = Player(output, song);
        var controller = new PlaybackController(player, new FakeStreams(), new InstantClock());
        var bar = new PlayerBarViewModel(controller, player);
        var waiting = 0;
        player.Waiting += () => waiting++;
        controller.SetQueue(new[] { new QueueTrack("s0", "A") });
        await controller.PlayAtAsync(0);
        output.Record(10);
        player.Tick();
        bar.Refresh();
        Assert.False(player.IsBuffering);
        Assert.False(bar.IsBuffering);
        Assert.InRange(player.BufferedEnd, 1.09, 1.11);

        song.Dry = true;
        output.Record(10);
        var stalledAt = player.Position;
        player.Tick();
        player.Tick();
        bar.Refresh();
        Assert.Equal(1, waiting);                       // once per stall
        Assert.True(player.IsBuffering);
        Assert.True(bar.IsBuffering);
        Assert.True(bar.IsPlaying);                      // like <audio>: still playing, waiting for data
        Assert.Equal(PlaybackState.Playing, player.State);
        Assert.Equal(stalledAt, player.Position);
        Assert.Equal(stalledAt, player.BufferedEnd, 6);

        song.Dry = false;
        output.Record(10);
        player.Tick();
        bar.Refresh();
        Assert.False(player.IsBuffering);
        Assert.False(bar.IsBuffering);
        Assert.True(player.Position > stalledAt);

        // a second stall raises Waiting again
        song.Dry = true;
        output.Record(2);
        player.Tick();
        Assert.Equal(2, waiting);

        // paused, nothing is "buffering"
        player.Pause();
        Assert.False(player.IsBuffering);
    }

    [Fact]
    public async Task AnEstimatedLengthDoesNotEndTheSongEarly()
    {
        var output = new FakeAudioOutput();
        var song = new FakeStreamingSource(0.2);   // the estimate says 0.2 s; the song is longer
        using var player = Player(output, song);
        var ended = 0;
        player.Ended += () => ended++;
        await player.LoadAsync("s0", null, 0, true, CancellationToken.None);
        Assert.Equal(0.2, player.Duration, 6);

        output.Record(50);                          // 0.5 s, past the estimate
        player.Tick();
        Assert.Equal(0, ended);
        Assert.Equal(PlaybackState.Playing, player.State);
        Assert.InRange(player.Position, 0.49, 0.51);
        Assert.True(player.Duration >= player.Position, "the length is live");

        song.AtEnd = true;                          // the real end
        output.Record(1);
        player.Tick();
        Assert.Equal(1, ended);
    }

    [Fact]
    public async Task AReplacedSongStopsDecodingAndDisposeStopsTheLast()
    {
        var output = new FakeAudioOutput();
        var a = new FakeStreamingSource(60);
        var b = new FakeStreamingSource(60);
        var player = Player(output, a, b);
        await player.LoadAsync("s0", null, 0, true, CancellationToken.None);
        output.Record(5);
        await player.LoadAsync("s1", null, 0, true, CancellationToken.None);
        Assert.True(a.Disposed);
        // the render thread may still play one ramped block of the old song: it must stay readable
        output.Record(5);
        Assert.False(b.Disposed);
        player.Dispose();
        Assert.True(b.Disposed);
    }

    [Fact]
    public async Task ASeekForwardsToTheStreamingSource()
    {
        var output = new FakeAudioOutput();
        var song = new FakeStreamingSource(60);
        using var player = Player(output, song);
        await player.LoadAsync("s0", null, 0, true, CancellationToken.None);
        output.Record(10);
        player.Seek(42);
        output.Record(10);
        Assert.InRange(player.Position, 42.09, 42.11);
        Assert.True(song.Position >= 42 * 48_000);
    }
}
