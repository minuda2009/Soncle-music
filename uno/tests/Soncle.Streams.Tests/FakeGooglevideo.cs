// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Collections.Concurrent;
using Soncle.Streams;

namespace Soncle.Streams.Tests;

/// <summary>
/// An in-process fake googlevideo: serves byte ranges from a byte array, can refuse requests at or
/// past a byte for a few attempts (a dead zone), cap each response (short origin chunks), or answer
/// 403 for client "A" (an expired URL). No test reaches the network.
/// </summary>
internal sealed class FakeGooglevideo : IStreamFetcher
{
    private readonly byte[] _data;

    internal FakeGooglevideo(byte[] data) => _data = data;

    internal long FailFrom403 { get; set; } = long.MaxValue;     // client "A" 403s from this byte
    internal int CapPerRequest { get; set; } = int.MaxValue;      // short origin chunks
    internal long RefuseFromByte { get; set; } = long.MaxValue;   // dead zone at/after this byte
    internal int RefuseCount { get; set; }                        // how many requests to refuse there
    internal TaskCompletionSource? FirstCallGate { get; set; }    // hold the first call open

    internal int Calls;
    internal readonly ConcurrentDictionary<string, int> CallsByKey = new();
    internal readonly ConcurrentBag<(long From, long To)> Ranges = new();
    private readonly object _firstLock = new();
    private (long From, long To)? _firstRange;

    /// <summary>The very first request's range (the small first piece), captured deterministically.</summary>
    internal (long From, long To) FirstRange
    {
        get { lock (_firstLock) return _firstRange ?? throw new InvalidOperationException("no calls yet"); }
    }

    private int _refused;
    private int _gated;

    public async Task<Stream> GetAsync(string url, IReadOnlyDictionary<string, string> headers, long from, long to, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref Calls);
        CallsByKey.AddOrUpdate(KeyOf(url), 1, (_, n) => n + 1);
        Ranges.Add((from, to));
        lock (_firstLock) _firstRange ??= (from, to);

        if (FirstCallGate is not null && Interlocked.Increment(ref _gated) == 1)
            await FirstCallGate.Task.ConfigureAwait(false);

        if (from >= RefuseFromByte && Interlocked.Increment(ref _refused) <= RefuseCount)
            throw new IOException("dead zone");

        if (ClientOf(url) == "A" && from >= FailFrom403) throw new StreamExpiredException();

        var end = (int)Math.Min(Math.Min(to, from + CapPerRequest - 1), _data.Length - 1L);
        var slice = new byte[Math.Max(0, end - (int)from + 1)];
        Array.Copy(_data, from, slice, 0, slice.Length);
        return new MemoryStream(slice, writable: false);
    }

    internal byte[] Data => _data;

    internal static string KeyOf(string url)
    {
        var slash = url.LastIndexOf('/');
        var q = url.IndexOf('?');
        return slash < 0 ? url : url[(slash + 1)..(q < 0 ? url.Length : q)];
    }

    internal static string ClientOf(string url)
    {
        var i = url.IndexOf("c=", StringComparison.Ordinal);
        return i < 0 ? "" : url[(i + 2)..];
    }
}

/// <summary>An <see cref="IStreamRefresher"/> that hands back a new client for the same song.</summary>
internal sealed class FakeRefresher : IStreamRefresher
{
    private readonly long _length;

    internal FakeRefresher(long length) => _length = length;

    internal int Calls;
    internal long? OverrideLength { get; set; }

    public Task<StreamSource?> RefreshAsync(string songKey, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref Calls);
        var len = OverrideLength ?? _length;
        return Task.FromResult<StreamSource?>(new StreamSource($"http://gv/{songKey}?c=B", new Dictionary<string, string>(), len));
    }
}

/// <summary>An <see cref="IStreamClock"/> that never really waits; tests assert on the delays asked for.</summary>
internal sealed class FakeStreamClock : IStreamClock
{
    internal readonly ConcurrentBag<TimeSpan> Delays = new();

    public async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default)
    {
        Delays.Add(delay);
        await Task.Delay(1, cancellationToken).ConfigureAwait(false);   // yield instead of busy-spinning
    }
}

internal static class TestData
{
    /// <summary>Deterministic bytes (the pattern the JS stream tests use).</summary>
    internal static byte[] Make(int length)
    {
        var b = new byte[length];
        for (var i = 0; i < length; i++) b[i] = (byte)((i * 7) & 255);
        return b;
    }

    internal static StreamSource Source(string key, long length) =>
        new($"http://gv/{key}?c=A", new Dictionary<string, string>(), length, "audio/webm");
}
