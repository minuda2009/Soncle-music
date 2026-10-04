// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Security.Cryptography;
using Soncle.Streams;

namespace Soncle.Streams.Tests;

public class SongDownloaderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "soncle-streams-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private SongDownloader NewDownloader(FakeGooglevideo server, IStreamRefresher? refresher = null, IStreamClock? clock = null) =>
        new(_dir, server, refresher, clock ?? new FakeStreamClock());

    private static async Task<byte[]> ReadAllAsync(Stream s, int max = int.MaxValue)
    {
        using var ms = new MemoryStream();
        var buf = new byte[64 * 1024];
        while (ms.Length < max)
        {
            var want = (int)Math.Min(buf.Length, max - ms.Length);
            var n = await s.ReadAsync(buf.AsMemory(0, want));
            if (n == 0) break;
            ms.Write(buf, 0, n);
        }
        return ms.ToArray();
    }

    [Fact]
    public async Task FirstBytesAreReadableLongBeforeTheSongCompletes()
    {
        var data = TestData.Make(3_000_000);
        var server = new FakeGooglevideo(data) { FirstCallGate = new TaskCompletionSource() };
        var dl = NewDownloader(server);
        dl.Register("v1", TestData.Source("v1", data.Length));
        await WaitUntilAsync(() => server.Calls >= 1);

        // The first request is held open (mid-download); a reader must still get the first piece.
        var readTask = Task.Run(async () =>
        {
            using var read = dl.OpenRead("v1", 0);
            return await ReadAllAsync(read, 200_000);
        });
        server.FirstCallGate!.SetResult();
        var head = await readTask;

        Assert.Equal(200_000, head.Length);
        Assert.Equal(data.Take(200_000), head);
        // the first request is the small first piece (≤ 256 KB), so playback can start early
        var first = server.FirstRange;
        Assert.True(first.To - first.From + 1 <= 256 * 1024, $"first piece was {first.To - first.From + 1} bytes");
    }

    [Fact]
    public async Task SurvivesADeadZoneMidSongAndKeepsTheBytesIntact()
    {
        var data = TestData.Make(2_500_000);
        var server = new FakeGooglevideo(data)
        {
            RefuseFromByte = 800_000,   // the next request after ~512 KB is refused a few times
            RefuseCount = 3,
        };
        var clock = new FakeStreamClock();
        var dl = NewDownloader(server, clock: clock);
        dl.Register("v1", TestData.Source("v1", data.Length));

        using var read = dl.OpenRead("v1", 0);
        var got = await ReadAllAsync(read);

        Assert.Equal(data, got);
        Assert.True(server.Calls > 3);                          // it retried through the dead zone
    }

    [Fact]
    public async Task RefreshesOn403ForTheSameLengthAndContinuesFromTheSameByte()
    {
        var data = TestData.Make(3_500_000);
        var server = new FakeGooglevideo(data) { FailFrom403 = 1_200_000 };
        var refresher = new FakeRefresher(data.Length);
        var dl = NewDownloader(server, refresher);
        dl.Register("v1", TestData.Source("v1", data.Length));

        using var read = dl.OpenRead("v1", 0);
        var got = await ReadAllAsync(read);

        Assert.Equal(data, got);
        Assert.True(refresher.Calls >= 1);
    }

    [Fact]
    public async Task RejectsARefreshWithADifferentLength()
    {
        var data = TestData.Make(2_000_000);
        var server = new FakeGooglevideo(data) { FailFrom403 = 500_000 };
        var refresher = new FakeRefresher(data.Length) { OverrideLength = data.Length - 1 };
        var dl = NewDownloader(server, refresher);
        dl.Register("v1", TestData.Source("v1", data.Length));

        using var read = dl.OpenRead("v1", 0);
        await Assert.ThrowsAsync<IOException>(async () => await ReadAllAsync(read));
        await WaitUntilAsync(() => dl.Status("v1")!.Value.Failed);
        Assert.Equal("stream expired", dl.Status("v1")!.Value.Error);
    }

    [Fact]
    public async Task WithNoRefresherItFailsAfterTheRetryBudget()
    {
        var data = TestData.Make(2_000_000);
        var server = new FakeGooglevideo(data) { FailFrom403 = 500_000 };
        var clock = new FakeStreamClock();
        var dl = NewDownloader(server, refresher: null, clock: clock);
        dl.Register("v1", TestData.Source("v1", data.Length));

        using var read = dl.OpenRead("v1", 0);
        await Assert.ThrowsAnyAsync<Exception>(async () => await ReadAllAsync(read));
        await WaitUntilAsync(() => dl.Status("v1")!.Value.Failed);
        Assert.Contains(clock.Delays, d => d >= TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task FarSeekIsServedFromTheNetworkImmediately()
    {
        var data = TestData.Make(5_000_000);
        var server = new FakeGooglevideo(data) { FirstCallGate = new TaskCompletionSource() };
        var dl = NewDownloader(server);
        dl.Register("v1", TestData.Source("v1", data.Length));
        await WaitUntilAsync(() => server.Calls >= 1);

        // 4 MB in: far past whatever has downloaded (held at 0)
        using var read = dl.OpenRead("v1", 4_000_000);
        var buf = new byte[1000];
        var n = await read.ReadAsync(buf);
        Assert.Equal(1000, n);
        Assert.Equal(data.Skip(4_000_000).Take(1000), buf);
    }

    [Fact]
    public async Task APrefetchedSongWaitsUntilItIsReadOrTheCurrentOneCompletes()
    {
        var data = TestData.Make(1_000_000);
        var server = new FakeGooglevideo(data) { FirstCallGate = new TaskCompletionSource() };
        var dl = NewDownloader(server);

        dl.Register("playing", TestData.Source("playing", data.Length));
        await WaitUntilAsync(() => server.CallsByKey.TryGetValue("playing", out var c) && c >= 1);

        dl.Register("next", TestData.Source("next", data.Length));
        await Task.Delay(200);
        // "next" downloads ahead, but the song being read ("playing") is not complete, so it waits
        Assert.False(server.CallsByKey.ContainsKey("next"));

        // release "playing"; now "next" runs
        server.FirstCallGate!.SetResult();
        await WaitUntilAsync(() => dl.Status("playing")!.Value.Done);
        dl.OpenRead("playing", 0).Dispose();
        await WaitUntilAsync(() => dl.Status("next")!.Value.Done);
        Assert.Equal(data, await ReadCachedAsync(dl, "next"));
    }

    [Fact]
    public async Task KeepsFourSongsAndDeletesTheEvictedFile()
    {
        var data = TestData.Make(400_000);
        var server = new FakeGooglevideo(data);
        var dl = NewDownloader(server);
        for (var i = 1; i <= 5; i++) dl.Register("v" + i, TestData.Source("v" + i, data.Length));

        await WaitUntilAsync(() => dl.Status("v1") is null);
        Assert.Null(dl.Status("v1"));            // oldest evicted
        Assert.NotNull(dl.Status("v5"));
        var parts = Directory.GetFiles(_dir, "*.part");
        Assert.True(parts.Length <= 4, $"kept {parts.Length} files");
    }

    [Fact]
    public async Task ShortOriginChunksAreHandled()
    {
        var data = TestData.Make(2_000_000);
        var server = new FakeGooglevideo(data) { CapPerRequest = 100_000 };
        var dl = NewDownloader(server);
        dl.Register("v1", TestData.Source("v1", data.Length));

        using var read = dl.OpenRead("v1", 0);
        Assert.Equal(data, await ReadAllAsync(read));
    }

    [Fact]
    public async Task HashingTheFinishedSongMatchesTheSource()
    {
        var data = TestData.Make(3_000_000);
        var server = new FakeGooglevideo(data);
        var dl = NewDownloader(server);
        dl.Register("v1", TestData.Source("v1", data.Length));
        await WaitUntilAsync(() => dl.Status("v1")!.Value.Done);

        var path = Directory.GetFiles(_dir, "*.part").Single();
        await using var f = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(f);
        Assert.Equal(SHA256.HashData(data), hash);
    }

    private static async Task<byte[]> ReadCachedAsync(SongDownloader dl, string key)
    {
        using var read = dl.OpenRead(key, 0);
        return await ReadAllAsync(read);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 15000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.ElapsedMilliseconds > timeoutMs) throw new TimeoutException("condition not reached");
            await Task.Delay(10);
        }
    }
}
