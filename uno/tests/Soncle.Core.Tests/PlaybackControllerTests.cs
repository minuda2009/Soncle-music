// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Collections.Concurrent;
using Soncle.Core.Playback;

namespace Soncle.Core.Tests;

/// <summary>A fake player: tracks state and lets tests fire engine events.</summary>
internal sealed class FakePlayer : IPlayer
{
    public PlaybackState State { get; internal set; } = PlaybackState.Idle;
    public double Position { get; set; }
    public double Duration { get; set; } = 200;
    public double BufferedEnd { get; set; }

    public int Loads;
    public readonly List<(string Source, double StartAt, bool Autoplay)> LoadHistory = new();
    public int Plays, Pauses, Seeks;
    public bool FailLoads;
    private TaskCompletionSource? _loadGate;
    public void HoldLoads() => _loadGate = new TaskCompletionSource();
    public void ReleaseLoads() => _loadGate?.SetResult();

    public event Action? Playing;
    public event Action? Paused;
    public event Action? Ended;
    public event Action? Waiting;

    public async Task LoadAsync(string source, double? lufs, double startAt, bool autoplay, CancellationToken ct)
    {
        Interlocked.Increment(ref Loads);
        LoadHistory.Add((source, startAt, autoplay));
        if (_loadGate is not null) await _loadGate.Task.WaitAsync(ct);
        if (FailLoads) throw new InvalidOperationException("stream stopped responding");
        Position = startAt;
        if (autoplay) { State = PlaybackState.Playing; Playing?.Invoke(); }
        else State = PlaybackState.Paused;
    }

    public Task PlayAsync(CancellationToken ct)
    {
        Plays++;
        if (State == PlaybackState.Error) return Task.FromException(new InvalidOperationException("aborted"));
        State = PlaybackState.Playing;
        Playing?.Invoke();
        return Task.CompletedTask;
    }

    public void Pause() { Pauses++; State = PlaybackState.Paused; Paused?.Invoke(); }
    public void Seek(double seconds) { Seeks++; Position = seconds; }

    internal void FireEnded() { State = PlaybackState.Paused; Ended?.Invoke(); }
    internal void FireWaiting() { Waiting?.Invoke(); }
}

internal sealed class FakeStreams : IPlaybackStreams
{
    public readonly ConcurrentBag<string> Prefetched = new();
    public TaskCompletionSource? Gate;
    public bool Fail;
    public async Task<double?> PrefetchAsync(string id, CancellationToken ct)
    {
        Prefetched.Add(id);
        if (Gate is not null) await Gate.Task.WaitAsync(ct);
        if (Fail) throw new InvalidOperationException("stream failed");
        return -14;
    }
}

internal sealed class FakeClock : IPlaybackClock
{
    public long NowMs => 0;
    public readonly ConcurrentBag<TimeSpan> Delays = new();
    public async Task DelayAsync(TimeSpan delay, CancellationToken ct) { Delays.Add(delay); await Task.Delay(1, ct).ConfigureAwait(false); }
}

public class PlaybackControllerTests
{
    private static (PlaybackController C, FakePlayer P, FakeStreams S) New(PlaybackOptions? options = null, Func<string, double?>? lufs = null)
    {
        var player = new FakePlayer();
        var streams = new FakeStreams();
        var c = new PlaybackController(player, streams, new FakeClock(), options, lufs);
        return (c, player, streams);
    }

    private static QueueTrack[] Q(params string[] ids) => ids.Select(id => new QueueTrack(id, id)).ToArray();

    [Fact]
    public async Task PauseWhileLookingUpHoldsTheStart()
    {
        var (c, p, s) = New();
        s.Gate = new TaskCompletionSource();   // the prefetch hangs
        c.SetQueue(Q("a", "b"));
        var play = c.PlayAtAsync(0);
        // pause arrives while the stream is being looked up
        await c.TogglePlayAsync();
        Assert.True(c.HoldStart);
        s.Gate.SetResult();
        await play;
        // the load did not autoplay, and the hold is cleared
        Assert.False(p.LoadHistory[^1].Autoplay);
        Assert.False(c.HoldStart);
    }

    [Fact]
    public async Task SystemPlayOnlyPlaysAndSystemPauseOnlyPauses()
    {
        var (c, p, _) = New();
        c.SetQueue(Q("a"));
        await c.PlayAtAsync(0);
        var playsBefore = p.Plays;
        await c.MediaPlayAsync();          // already playing: does nothing
        Assert.Equal(playsBefore, p.Plays);
        c.MediaPause();
        Assert.Equal(PlaybackState.Paused, p.State);
        var pausesBefore = p.Pauses;
        c.MediaPause();                    // already paused: does nothing
        Assert.Equal(pausesBefore, p.Pauses);
    }

    [Fact]
    public async Task NextAndPrevWalkTheQueue()
    {
        var (c, p, _) = New();
        c.SetQueue(Q("a", "b", "c"));
        await c.PlayAtAsync(0);
        await c.NextAsync();
        Assert.Equal("b", c.Current!.Id);
        p.Position = 1;                    // within 3 s: prev goes back a track
        await c.PrevAsync();
        Assert.Equal("a", c.Current!.Id);
        // past 3 s, prev restarts the current track instead
        p.Position = 10;
        await c.PrevAsync();
        Assert.Equal("a", c.Current!.Id);
        Assert.Equal(1, p.Seeks);
    }

    [Fact]
    public async Task RepeatOneReplaysAndRepeatAllWraps()
    {
        var (c, p, _) = New();
        c.SetQueue(Q("a", "b"));
        c.Repeat = RepeatMode.One;
        await c.PlayAtAsync(0);
        p.Position = 50;
        await c.NextAsync(auto: true);
        Assert.Equal(1, p.Seeks);          // repeat one seeks to 0
        Assert.Equal("a", c.Current!.Id);

        c.Repeat = RepeatMode.All;
        await c.PlayAtAsync(1);
        await c.NextAsync(auto: true);
        Assert.Equal("a", c.Current!.Id);  // wrapped
    }

    [Fact]
    public async Task AutoSkipOnErrorStopsAfterThree()
    {
        var (c, p, s) = New(new PlaybackOptions { AutoSkipOnError = true });
        s.Fail = true;
        var failures = new List<string>();
        c.Failed += (t, m) => failures.Add(t.Id);
        c.SetQueue(Q("a", "b", "c", "d", "e"));
        await c.PlayAtAsync(0);
        // it skips up to three times, then stops rather than skipping forever
        Assert.Single(failures);
        Assert.Equal(3, c.Index);
    }

    [Fact]
    public async Task StallWatchWaitsWhileDataArrivesThenReloads()
    {
        var (c, p, _) = New(new PlaybackOptions { StallChecks = 8 });
        c.SetQueue(Q("a"));
        await c.PlayAtAsync(0);
        Assert.Equal(1, p.Loads);
        p.State = PlaybackState.Paused;
        p.BufferedEnd = 5;
        c.SimulateWaiting();
        // data keeps arriving, so the watch keeps waiting rather than giving up
        p.BufferedEnd = 20;
        await Task.Delay(150);
        // eventually the growth stops and it reloads (rather than erroring)
        Assert.True(p.Loads >= 2, p.Loads.ToString());
        Assert.Empty(new List<string>());   // no Failed raised
    }

    [Fact]
    public async Task StallWatchGivesUpAfterTheStallBudget()
    {
        var (c, p, _) = New(new PlaybackOptions { StallChecks = 2, AutoSkipOnError = false });
        var failures = new List<string>();
        c.Failed += (t, m) => failures.Add(m);
        c.SetQueue(Q("a"));
        await c.PlayAtAsync(0);
        p.State = PlaybackState.Paused;
        p.BufferedEnd = 5;               // never grows
        p.FailLoads = true;              // the reload also fails, so it gives up
        c.SimulateWaiting();
        await Task.Delay(200);
        // with a flat buffer it reloads a few times, then gives up with the stall message
        Assert.Contains(failures, m => m.Contains("stopped responding"));
    }

    [Fact]
    public async Task GaplessFallbackDoesNotSkipASong()
    {
        // if the crossfade can't be set up, playback falls back to a normal load of the same track
        var (c, p, _) = New();
        c.SetQueue(Q("a", "b"));
        await c.PlayAtAsync(0);
        var loadsBefore = p.Loads;
        await c.PlayAtAsync(1);   // a normal load, not a skip
        Assert.Equal(loadsBefore + 1, p.Loads);
        Assert.Equal("b", c.Current!.Id);
    }

    [Fact]
    public async Task QueueTracksChangeRaisesTheEvent()
    {
        var (c, _, _) = New();
        var changed = new List<string>();
        c.TrackChanged += t => changed.Add(t.Id);
        c.SetQueue(Q("a", "b"));
        await c.PlayAtAsync(0);
        await c.PlayAtAsync(1);
        Assert.Equal(new[] { "a", "b" }, changed);
    }
}
