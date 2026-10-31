// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using Soncle.App.Playback;
using Soncle.Audio;
using Soncle.Audio.Opus;
using Soncle.Core.Platform;
using Soncle.Core.Playback;
using Soncle.TestKit;

namespace Soncle.App.Tests;

/// <summary>
/// The engine-backed player: songs reach the device only through the engine (limiter last), and
/// the render callback stays allocation-free across song swaps and seeks.
/// </summary>
public class EnginePlayerTests
{
    private const int Sr = 48_000;

    private static EnginePlayer Player(IAudioOutput output, params IAudioSource[] songs)
    {
        var byId = songs.Select((s, i) => (s, i)).ToDictionary(p => "s" + p.i, p => p.s);
        return new EnginePlayer(output, (id, ct) => Task.FromResult(byId[id]));
    }

    private static async Task WaitUntil(Func<bool> cond, FakeAudioOutput? pull = null)
    {
        for (var i = 0; i < 500 && !cond(); i++)
        {
            pull?.Pull();
            await Task.Delay(10);
        }
        Assert.True(cond(), "condition not reached");
    }

    [Fact]
    public async Task LoadAndPlayRendersTheSongThroughTheLimiter()
    {
        var output = new FakeAudioOutput();
        // a hot song (near full scale) with unknown loudness: only the limiter keeps it down
        using var player = Player(output, Pcm.Sine(3, 300, 0.99));
        await player.LoadAsync("mstream://s0", lufs: null, startAt: 0, autoplay: true, CancellationToken.None);
        Assert.Equal(PlaybackState.Playing, player.State);
        Assert.True(output.IsRunning);
        Assert.Equal("s0", player.CurrentId);
        Assert.Equal(3.0, player.Duration, 6);

        var got = output.Record(250);   // 2.5 s
        Assert.True(AudioAssert.RmsDb(got) > -20, AudioAssert.RmsDb(got).ToString());
        // the existing engine tests' bound for the -1 dBTP ceiling (4x oversampled meter)
        Assert.True(AudioAssert.TruePeakDb(got) <= -0.8, AudioAssert.TruePeakDb(got).ToString());
        Assert.InRange(player.Position, 2.4, 2.6);
    }

    [Fact]
    public async Task TheRecordedSampleSongPlaysThroughTheEngine()
    {
        // the real codec (Concentus) on fixtures/flow/sample.webm
        var clip = WebmOpusClip.Load(File.ReadAllBytes(Fixtures.Path("flow/sample.webm")));
        var output = new FakeAudioOutput();
        using var player = Player(output, clip);
        await player.LoadAsync("mstream://s0", null, 0, true, CancellationToken.None);
        var got = output.Record(400);   // 4 s
        Assert.True(AudioAssert.RmsDb(got) > -40, AudioAssert.RmsDb(got).ToString());
        Assert.True(AudioAssert.TruePeakDb(got) <= -0.8, AudioAssert.TruePeakDb(got).ToString());
    }

    [Fact]
    public async Task RenderDoesNotAllocateSteadilyNorOnASwapNorOnASeek()
    {
        var output = new FakeAudioOutput();
        using var player = Player(output, Pcm.Sine(20, 300), Pcm.Sine(20, 500));
        await player.LoadAsync("s0", null, 0, true, CancellationToken.None);
        for (var i = 0; i < 200; i++) output.Pull();   // warm up (JIT, first-block paths)

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 500; i++) output.Pull();
        var steady = GC.GetAllocatedBytesForCurrentThread() - before;

        // swap: the new song is built off the render thread, then handed over in Pull
        await player.LoadAsync("s1", null, 0, true, CancellationToken.None);
        before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++) output.Pull();
        var swap = GC.GetAllocatedBytesForCurrentThread() - before;

        player.Seek(10);
        before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++) output.Pull();
        var seek = GC.GetAllocatedBytesForCurrentThread() - before;

        // 0 B locally; the bound leaves room for runtime noise but not for any per-block
        // allocation (the smallest object is 24 B, and each window has at least 100 blocks)
        Assert.True(steady < 1024, $"steady {steady} B");
        Assert.True(swap < 1024, $"swap {swap} B");
        Assert.True(seek < 1024, $"seek {seek} B");
        Assert.InRange(player.Position, 10.9, 11.1);
    }

    [Fact]
    public async Task ASwapRampsTheOldSongDownThenTheNewOneFadesIn()
    {
        var output = new FakeAudioOutput();
        using var player = Player(output, Pcm.Sine(10, 1000, 0.5), Pcm.Sine(10, 700, 0.5));
        await player.LoadAsync("s0", null, 0, true, CancellationToken.None);
        output.Record(50);   // past the first fade-in: the old song plays at full level
        await player.LoadAsync("s1", null, 0, true, CancellationToken.None);

        var swap = output.Record(1);
        Assert.True(Pcm.Peak(swap.AsSpan(0, 100)) > 0.1, "the old song is still audible at the start of the swap block");
        Assert.Equal(0f, swap[^1]);
        Assert.Equal(0f, swap[^2]);
        Assert.True(Pcm.Peak(swap.AsSpan(swap.Length - 20)) < 0.01, "the swap block ends silent");

        var first = output.Record(1);
        Assert.True(Pcm.Peak(first.AsSpan(0, 20)) < 0.01, "the new song starts from silence (its fade-in)");
        var later = output.Record(30);
        Assert.True(Pcm.Peak(later) > 0.3, "the new song comes up to level");
        Assert.InRange(player.Position, 0.3, 0.35);
    }

    [Fact]
    public async Task ASeekMovesThePositionAndFadesAcrossTheJump()
    {
        var output = new FakeAudioOutput();
        using var player = Player(output, Pcm.Sine(30, 1000, 0.5));
        await player.LoadAsync("s0", null, 0, true, CancellationToken.None);
        output.Record(100);
        Assert.InRange(player.Position, 0.99, 1.01);

        player.Seek(20);
        Assert.Equal(20.0, player.Position, 6);   // reported at once, before the render thread runs
        var jump = output.Record(1);
        Assert.Equal(0f, jump[^1]);
        var after = output.Record(1);
        Assert.True(Pcm.Peak(after.AsSpan(0, 20)) < 0.01, "no pre-jump audio (limiter look-ahead) after the jump");
        output.Record(98);
        Assert.InRange(player.Position, 20.98, 21.02);

        player.Seek(1e9);                           // clamped to the end
        Assert.Equal(30.0, player.Position, 6);
    }

    [Fact]
    public async Task PausedSeekAndSwapDoNotReplayAStaleBlock()
    {
        var output = new FakeAudioOutput();
        using var player = Player(output, Pcm.Sine(10, 1000, 0.5), Pcm.Sine(10, 700, 0.5));
        await player.LoadAsync("s0", null, 0, true, CancellationToken.None);
        output.Record(50);
        player.Pause();
        Assert.False(output.IsRunning);
        Assert.Equal(PlaybackState.Paused, player.State);

        player.Seek(5);
        await player.PlayAsync(CancellationToken.None);
        var resumed = output.Record(1);
        Assert.True(Pcm.Peak(resumed.AsSpan(0, 20)) < 0.01, "resuming starts at the new spot, fading in");

        player.Pause();
        await player.LoadAsync("s1", null, 0, true, CancellationToken.None);
        var first = output.Record(1);
        Assert.True(Pcm.Peak(first.AsSpan(0, 20)) < 0.01, "no block of the old song after a paused swap");
        Assert.InRange(player.Position, 0.009, 0.011);
    }

    [Fact]
    public async Task EndedFiresOnceOnTickAndTheControllerMovesOn()
    {
        var output = new FakeAudioOutput();
        using var player = Player(output, Pcm.Sine(0.2, 440), Pcm.Sine(5, 440));
        var controller = new PlaybackController(player, new FakeStreams(), new InstantClock());
        var ended = 0;
        player.Ended += () => ended++;
        controller.SetQueue(new[] { new QueueTrack("s0", "A"), new QueueTrack("s1", "B") });
        await controller.PlayAtAsync(0);
        Assert.Equal(PlaybackState.Playing, player.State);

        player.Tick();
        Assert.Equal(0, ended);                    // not at the end yet
        output.Record(30);                          // 0.3 s > 0.2 s song
        player.Tick();
        player.Tick();
        Assert.Equal(1, ended);

        await WaitUntil(() => controller.Index == 1 && player.CurrentId == "s1" && player.State == PlaybackState.Playing);
        Assert.Equal(1, ended);
        Assert.True(output.IsRunning);
    }

    [Fact]
    public async Task ASampleRateMismatchIsRefusedAndSurfacesThroughFailed()
    {
        var output = new FakeAudioOutput();
        using var player = Player(output, Pcm.Sine(1, 440, rate: 44_100));
        var e = await Assert.ThrowsAsync<NotSupportedException>(() => player.LoadAsync("s0", null, 0, true, CancellationToken.None));
        Assert.Contains("44100", e.Message);
        Assert.Equal(PlaybackState.Error, player.State);
        Assert.False(output.IsRunning);

        var controller = new PlaybackController(player, new FakeStreams(), new InstantClock());
        string? failed = null;
        controller.Failed += (t, reason) => failed = reason;
        controller.SetQueue(new[] { new QueueTrack("s0", "A") });
        await controller.PlayAtAsync(0);
        Assert.NotNull(failed);
        Assert.Contains("44100", failed);
    }

    [Fact]
    public async Task PlayBeforeLoadThrowsAndDisposeStopsTheOutput()
    {
        var output = new FakeAudioOutput();
        var player = Player(output, Pcm.Sine(1, 440));
        await Assert.ThrowsAsync<InvalidOperationException>(() => player.PlayAsync(CancellationToken.None));
        await player.LoadAsync("s0", null, 0, autoplay: false, CancellationToken.None);
        Assert.Equal(PlaybackState.Paused, player.State);
        Assert.False(output.IsRunning);
        await player.PlayAsync(CancellationToken.None);
        Assert.True(output.IsRunning);
        player.Dispose();
        Assert.False(output.IsRunning);
    }

    [Fact]
    public async Task AQuickPauseAndPlayHandsOverWithoutASecondRendererCall()
    {
        // the stopped session has not pulled its faded last block yet when play comes back
        var output = new DeferredStopOutput();
        using var player = Player(output, Pcm.Sine(5, 440, 0.5));
        await player.LoadAsync("s0", null, 0, true, CancellationToken.None);
        var old = output.Current!;
        for (var i = 0; i < 20; i++) old.Pull();
        player.Pause();
        await player.PlayAsync(CancellationToken.None);
        var fresh = output.Current!;
        Assert.NotSame(old, fresh);
        var position = player.Position;

        // the old device thread's late read: silence, and the song does not move
        Assert.Equal(0, old.Pull());
        Assert.Equal(20, old.Renders);
        Assert.True(old.Finished);
        Assert.Equal(0.0, Pcm.Peak(old.Buffer));
        Assert.Equal(position, player.Position);

        // the new session carries on from the same spot, fading in
        Assert.Equal(output.BlockFrames, fresh.Pull());
        Assert.True(Pcm.Peak(fresh.Buffer.AsSpan(0, 20)) < 0.05, "fades in");
        Assert.True(Pcm.Peak(fresh.Buffer) > 0.05, "and plays");
        Assert.Equal(1, output.MaxConcurrentRenders);
    }

    [Fact]
    public async Task AStoppedSessionThatPullsItsLastBlockFirstFadesOut()
    {
        var output = new DeferredStopOutput();
        using var player = Player(output, Pcm.Sine(5, 440, 0.5));
        await player.LoadAsync("s0", null, 0, true, CancellationToken.None);
        var old = output.Current!;
        for (var i = 0; i < 20; i++) old.Pull();
        player.Pause();
        Assert.Equal(output.BlockFrames, old.Pull());
        Assert.True(old.Finished);
        Assert.True(Math.Abs(old.Buffer[^1]) < 0.01 && Math.Abs(old.Buffer[^2]) < 0.01, "the last block ends in silence");
        await player.PlayAsync(CancellationToken.None);
        Assert.Equal(output.BlockFrames, output.Current!.Pull());
        Assert.Equal(0, old.Pull());
        Assert.Equal(1, output.MaxConcurrentRenders);
    }

    [Fact]
    public async Task OverlappingDeviceThreadsNeverRenderAtOnce()
    {
        // real threads: the old session pulls flat out while play comes back on this thread
        for (var round = 0; round < 20; round++)
        {
            var output = new DeferredStopOutput(blockFrames: 64);
            using var player = Player(output, Pcm.Sine(30, 440, 0.5));
            await player.LoadAsync("s0", null, 0, true, CancellationToken.None);
            var old = output.Current!;
            using var go = new ManualResetEventSlim(false);
            var device = new Thread(() =>
            {
                go.Wait();
                while (!old.Finished) old.Pull();
            });
            device.Start();
            player.Pause();
            go.Set();
            Thread.SpinWait(round * 100);
            await player.PlayAsync(CancellationToken.None);
            for (var i = 0; i < 50; i++) output.Current!.Pull();
            device.Join();
            Assert.Equal(1, output.MaxConcurrentRenders);
        }
    }
}
