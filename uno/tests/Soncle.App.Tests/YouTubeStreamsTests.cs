// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using Soncle.App.Playback;
using Soncle.Audio;
using Soncle.Streams;
using Soncle.TestKit;
using Soncle.YouTube;

namespace Soncle.App.Tests;

/// <summary>Resolve → download → streaming decode, against a fake player client and a fake googlevideo (no network).</summary>
public class YouTubeStreamsTests : IDisposable
{
    private const string Opus = "audio/webm; codecs=\"opus\"";
    private readonly string _cache = Path.Combine(Path.GetTempPath(), "soncle-app-tests-" + Guid.NewGuid().ToString("N"));
    private readonly byte[] _webm = File.ReadAllBytes(Fixtures.Path("flow/sample.webm"));

    public void Dispose()
    {
        try { Directory.Delete(_cache, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private (YouTubeStreams Streams, FakeFetcher Fetcher) Build(string mime = Opus, string? loudnessDb = "-3.5", bool filter = true,
        int bitrate = 160_000)
    {
        var client = new FakeStreamClient(
            new AudioFormat($"https://gv/a?clen={_webm.Length}", mime, bitrate, _webm.Length, LoudnessDb: loudnessDb));
        var resolver = new StreamResolver(client, accept: filter ? YouTubeStreams.OpusOnly : null);
        resolver.SetClients(new[] { "ANDROID_VR" });
        var fetcher = new FakeFetcher(_webm);
        var downloader = new SongDownloader(_cache, fetcher);
        return (new YouTubeStreams(resolver, downloader, new FakeOpusCodec()), fetcher);
    }

    private static ResolvedStream Stream(string? loudnessDb) =>
        new("https://gv/a", "IOS", new Dictionary<string, string>(), "ua", Opus, 1, 1, loudnessDb, 0);

    [Fact]
    public void LufsIsTheYouTubeReferencePlusLoudnessDb()
    {
        Assert.Equal(-17.5, YouTubeStreams.LufsOf(Stream("-3.5"))!.Value, 9);
        Assert.Equal(-11.75, YouTubeStreams.LufsOf(Stream("2.25"))!.Value, 9);
        Assert.Null(YouTubeStreams.LufsOf(Stream(null)));
        Assert.Null(YouTubeStreams.LufsOf(Stream("NaN")));
        Assert.Null(YouTubeStreams.LufsOf(Stream("loud")));
    }

    [Fact]
    public void OpusOnlyAcceptsWebmOpusAndRejectsAac()
    {
        static AudioFormat F(string mime) => new("u", mime, 1, 1);
        Assert.True(YouTubeStreams.OpusOnly(F("audio/webm; codecs=\"opus\"")));
        Assert.True(YouTubeStreams.OpusOnly(F("audio/webm")));
        Assert.True(YouTubeStreams.OpusOnly(F("AUDIO/WEBM; CODECS=\"OPUS\"")));
        Assert.False(YouTubeStreams.OpusOnly(F("audio/webm; codecs=\"vorbis\"")));
        Assert.False(YouTubeStreams.OpusOnly(F("audio/mp4; codecs=\"mp4a.40.2\"")));
        Assert.False(YouTubeStreams.OpusOnly(F("")));
    }

    [Fact]
    public async Task PrefetchResolvesRegistersAndReturnsTheLoudness()
    {
        var (streams, _) = Build();
        Assert.Equal(-17.5, (await streams.PrefetchAsync("v1", CancellationToken.None))!.Value, 9);
        var (unknown, _) = Build(loudnessDb: null);
        Assert.Null(await unknown.PrefetchAsync("v1", CancellationToken.None));
    }

    /// <summary>Reads a streaming song to its end, waiting (async) whenever the decoder is behind.</summary>
    private static async Task<long> ReadToEndAsync(IAudioSource song)
    {
        var l = new float[480];
        var r = new float[480];
        var total = 0L;
        var s = (StreamingOpusSource)song;
        for (var spins = 0; spins < 5000; spins++)
        {
            if (s.DecodedEndFrame - s.Position < 480 + StreamingOpusSource.ResumeRampFrames && !s.DecodeComplete) { await Task.Delay(2); continue; }
            var n = song.Read(l, r, 480);
            total += n;
            if (n < 480) return total;
        }
        throw new TimeoutException("the song never ended");
    }

    [Fact]
    public async Task LoadSongStreamsTheSongFromTheDownloader()
    {
        var (streams, fetcher) = Build();
        await streams.PrefetchAsync("v1", CancellationToken.None);
        var song = await streams.LoadSongAsync("v1", CancellationToken.None);
        using var _ = (IDisposable)song;
        Assert.True(fetcher.Calls > 0);
        Assert.IsType<StreamingOpusSource>(song);
        Assert.Equal(48_000, song.SampleRate);
        var l = new float[256];
        var r = new float[256];
        Assert.Equal(256, song.Read(l, r, 256));
        Assert.All(l, x => Assert.True(x > 0, "decoded PCM from the codec"));
        // sample.webm is 8 s; the fake codec yields the packets' TOC lengths minus pre-skip/padding
        var total = 256 + await ReadToEndAsync(song);
        Assert.InRange(total, 7 * 48_000, 9 * 48_000);
        Assert.Equal(total, song.LengthFrames);
        Assert.True(((IBufferingSource)song).Ended);
    }

    [Fact]
    public async Task LoadSongPrefetchesWhenTheSongWasNeverResolved()
    {
        var (streams, _) = Build();
        var song = await streams.LoadSongAsync("v2", CancellationToken.None);
        Assert.True(song.LengthFrames > 0);
    }

    [Fact]
    public async Task ANonOpusStreamIsRefused()
    {
        // without the resolver filter an M4A stream gets through; the loader still refuses it
        var (streams, _) = Build(mime: "audio/mp4; codecs=\"mp4a.40.2\"", filter: false);
        await Assert.ThrowsAsync<NotSupportedException>(() => streams.PrefetchAsync("v1", CancellationToken.None));
        await Assert.ThrowsAsync<NotSupportedException>(() => streams.LoadSongAsync("v1", CancellationToken.None));
        // with the filter the resolver itself finds nothing playable
        var (filtered, _) = Build(mime: "audio/mp4", filter: true);
        var e = await Assert.ThrowsAsync<InvalidOperationException>(() => filtered.PrefetchAsync("v1", CancellationToken.None));
        Assert.Contains("no playable audio format", e.Message);
    }

    [Fact]
    public void TheLengthEstimateComesFromBytesAndBitrate()
    {
        static ResolvedStream S(long length, int bitrate) =>
            new("https://gv/a", "IOS", new Dictionary<string, string>(), "ua", Opus, length, bitrate, null, 0);
        Assert.Equal(240.0, YouTubeStreams.EstimatedSeconds(S(4_800_000, 160_000))!.Value, 9);
        Assert.Null(YouTubeStreams.EstimatedSeconds(S(0, 160_000)));
        Assert.Null(YouTubeStreams.EstimatedSeconds(S(4_800_000, 0)));
    }

    [Fact]
    public async Task ALongSongIsNotRefused()
    {
        // at 100 bit/s the fixture's bytes look like an hour-long song: there is no length limit any more
        var (streams, fetcher) = Build(bitrate: 100);
        Assert.True(_webm.Length * 8.0 / 100 > 20 * 60);
        await streams.PrefetchAsync("v1", CancellationToken.None);
        var song = await streams.LoadSongAsync("v1", CancellationToken.None);
        using var _ = (IDisposable)song;
        Assert.True(fetcher.Calls > 0);
        // the file's own Duration (8 s) replaces the estimate as soon as the head is read
        Assert.InRange(song.LengthFrames!.Value, 7 * 48_000, 9 * 48_000);
    }
}
