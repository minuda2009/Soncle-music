// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
namespace Soncle.Streams;

/// <summary>A song's playable stream: where it is, the headers it needs, and its length.</summary>
public sealed record StreamSource(string Url, IReadOnlyDictionary<string, string> Headers, long Length, string? Mime = null);

/// <summary>
/// Asks for a fresh <see cref="StreamSource"/> for the same song when a download gets a 403
/// (googlevideo URLs expire). The app side knows how to re-resolve; the downloader only accepts a
/// refresh whose length matches, so a song can never be mixed with another stream.
/// </summary>
public interface IStreamRefresher
{
    Task<StreamSource?> RefreshAsync(string songKey, CancellationToken cancellationToken = default);
}

/// <summary>
/// Sleep seam so the retry/back-off budget can be tested without real waits: the real
/// implementation waits, tests advance time instead.
/// </summary>
public interface IStreamClock
{
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default);
}

/// <summary>Waits on the wall clock; the default for <see cref="SongDownloader"/>.</summary>
public sealed class RealStreamClock : IStreamClock
{
    public static readonly RealStreamClock Instance = new();
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default) =>
        Task.Delay(delay, cancellationToken);
}
