// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
namespace Soncle.Core.Playback;

public enum RepeatMode { Off, All, One }
public enum PlaybackState { Idle, Loading, Playing, Paused, Error }

/// <summary>A track in the queue (id + the fields the controller needs).</summary>
public sealed record QueueTrack(string Id, string Title = "", double Duration = 0);

/// <summary>What the player engine reports back to the controller.</summary>
public interface IPlayer
{
    PlaybackState State { get; }
    double Position { get; }
    double Duration { get; }
    double BufferedEnd { get; }
    /// <summary>Loads a source and (optionally) starts it.</summary>
    Task LoadAsync(string source, double? lufs, double startAt, bool autoplay, CancellationToken ct);
    Task PlayAsync(CancellationToken ct);
    void Pause();
    void Seek(double seconds);
    event Action? Playing;
    event Action? Paused;
    event Action? Ended;
    event Action? Waiting;
}

/// <summary>Resolves and prefetches streams (the YouTube side).</summary>
public interface IPlaybackStreams
{
    Task<double?> PrefetchAsync(string id, CancellationToken ct);
}

/// <summary>A clock so the stall watch is testable without real waits.</summary>
public interface IPlaybackClock
{
    long NowMs { get; }
    Task DelayAsync(TimeSpan delay, CancellationToken ct);
}

public sealed class RealPlaybackClock : IPlaybackClock
{
    public long NowMs => Environment.TickCount64;
    public Task DelayAsync(TimeSpan delay, CancellationToken ct) => Task.Delay(delay, ct);
}

public sealed class PlaybackOptions
{
    /// <summary>Phones ride out dead zones longer (8 stall checks; desktop 2).</summary>
    public int StallChecks { get; init; } = 2;
    public bool AutoSkipOnError { get; init; } = true;
    public double StallReloadSeconds { get; init; } = 12;
    public double StallWaitCapSeconds { get; init; } = 60;
}
