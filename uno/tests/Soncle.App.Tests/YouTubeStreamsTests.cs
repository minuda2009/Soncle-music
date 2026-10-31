// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using Soncle.App.Playback;
using Soncle.Audio;
using Soncle.Streams;
using Soncle.TestKit;
using Soncle.YouTube;

namespace Soncle.App.Tests;

/// <summary>Resolve → download → decode, against a fake player client and a fake googlevideo (no network).</summary>
public class YouTubeStreamsTests : IDisposable
{
    private const string Opus = "audio/webm; codecs=\"opus\"";
    private readonly string _cache = Path.Combine(Path.GetTempPath(), "soncle-app-tests-" + Guid.NewGuid().ToString("N"));
    private readonly byte[] _webm = File.ReadAllBytes(Fixtures.Path("flow/sample.webm"));

    public void Dispose()
    {
        try { Directory.Delete(_cache, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private (YouTubeStreams Streams, FakeFetcher Fetcher) Build(string mime = Opus, string? loudnessDb = "-3.5", bool filter = true)
    {
        var client = new FakeStreamClient(
            new AudioFormat($"https://gv/a?clen={_webm.Length}", mime, 160_000, _webm.Length, LoudnessDb: loudnessDb));
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

    [Fact]
    public async Task LoadSongDownloadsTheWholeSongAndDecodesIt()
    {
        var (streams, fetcher) = Build();
        await streams.PrefetchAsync("v1", CancellationToken.None);
        var song = await streams.LoadSongAsync("v1", CancellationToken.None);
        Assert.True(fetcher.Calls > 0);
        Assert.Equal(48_000, song.SampleRate);
        // sample.webm is 8 s; the fake codec yields the packets' TOC lengths minus pre-skip/padding
        Assert.InRange(song.LengthFrames!.Value, 7 * 48_000, 9 * 48_000);
        var l = new float[256];
        var r = new float[256];
        Assert.Equal(256, song.Read(l, r, 256));
        Assert.All(l, x => Assert.True(x > 0, "decoded PCM from the codec"));
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
}
