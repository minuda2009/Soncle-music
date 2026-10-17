// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using Soncle.Cli;
using Soncle.Streams;
using Soncle.YouTube;

namespace Soncle.Cli.Tests;

public class StreamRangeReaderTests
{
    private sealed class Recorder : IStreamFetcher
    {
        public readonly List<(long From, long To)> Calls = new();
        public Task<Stream> GetAsync(string url, IReadOnlyDictionary<string, string> headers, long from, long to, CancellationToken ct = default)
        {
            Calls.Add((from, to));
            return Task.FromResult<Stream>(new MemoryStream(Enumerable.Range((int)from, (int)(to - from + 1)).Select(i => (byte)i).ToArray()));
        }
    }

    private static ResolvedStream Stream(long length) =>
        new("https://x/y", "IOS", new Dictionary<string, string>(), "ua", "audio/webm", length, 0, null, 0);

    [Fact]
    public async Task AnExclusiveEndBecomesTheFetchersInclusiveEnd()
    {
        var f = new Recorder();
        var (bytes, total) = await new StreamRangeReader(f, Stream(1000)).ReadAsync(10, 20, CancellationToken.None);
        Assert.Equal((10L, 19L), f.Calls[0]);
        Assert.Equal(10, bytes.Length);
        Assert.Equal(1000L, total);
    }

    [Fact]
    public async Task NeverAsksPastTheEndOfTheFile()
    {
        var f = new Recorder();
        await new StreamRangeReader(f, Stream(100)).ReadAsync(0, 96 * 1024, CancellationToken.None);
        Assert.Equal((0L, 99L), f.Calls[0]);
    }

    [Fact]
    public async Task AnUnknownLengthIsReportedAsZero()
    {
        var f = new Recorder();
        var (_, total) = await new StreamRangeReader(f, Stream(0)).ReadAsync(0, 50, CancellationToken.None);
        Assert.Equal(0L, total);
        Assert.Equal((0L, 49L), f.Calls[0]);
    }
}
