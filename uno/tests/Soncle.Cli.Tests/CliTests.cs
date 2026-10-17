// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using Soncle.Audio;
using Soncle.Cli;
using Soncle.Streams;
using Soncle.TestKit;
using Soncle.YouTube;
using Soncle.YouTube.Parsing;

namespace Soncle.Cli.Tests;

/// <summary>Every command against the offline backend: no network, no real account.</summary>
public class CliTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "soncle-cli-tests-" + Guid.NewGuid().ToString("N")[..8]);

    public CliTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static OfflineBackend Backend() => new(Fixtures.RootPath);

    private static async Task<(int Code, string Out, string Err)> Run(ICliBackend b, params string[] args)
    {
        var o = new StringWriter(); var e = new StringWriter();
        var code = await new CliApp(b, o, e).RunAsync(args);
        return (code, o.ToString(), e.ToString());
    }

    [Fact]
    public async Task SearchPrintsTheTopResultAndSongs()
    {
        var (code, o, _) = await Run(Backend(), "search", "daft", "punk");
        Assert.Equal(CliApp.Ok, code);
        Assert.Contains("top:", o);
        Assert.Contains("song ", o);
        Assert.Contains("Daft Punk", o);
    }

    [Fact]
    public async Task ResolveShowsTheFormatAndLoudnessButNoUrl()
    {
        var b = Backend();
        var (code, o, _) = await Run(b, "resolve", OfflineBackend.SongId);
        Assert.Equal(CliApp.Ok, code);
        Assert.Contains("client:   IOS", o);
        Assert.Contains(OfflineBackend.Mime, o);
        Assert.Contains("LUFS", o);
        Assert.DoesNotContain("offline://", o);
        Assert.DoesNotContain("http", o);
    }

    [Fact]
    public async Task ResolveOfAnUnknownSongFailsCleanly()
    {
        var (code, _, e) = await Run(Backend(), "resolve", "nope");
        Assert.Equal(CliApp.Failed, code);
        Assert.StartsWith("error:", e);
    }

    [Fact]
    public async Task DownloadWritesTheWholeSong()
    {
        var file = Path.Combine(_dir, "song.bin");
        var b = Backend();
        var (code, o, _) = await Run(b, "download", OfflineBackend.SongId, file);
        Assert.Equal(CliApp.Ok, code);
        Assert.Equal(b.SongLength, new FileInfo(file).Length);
        Assert.Contains("downloading 100%", o);
    }

    [Fact]
    public async Task AnalyzeReportsKeyAndEnergy()
    {
        var (code, o, _) = await Run(Backend(), "analyze", OfflineBackend.SongId);
        Assert.Equal(CliApp.Ok, code);
        Assert.Contains("key:", o);
        Assert.Contains("energy:", o);
    }

    [Theory]
    [InlineData(-14.0)]
    [InlineData(-12.0)]
    [InlineData(-19.0)]
    public async Task PlayWritesAWavAtTheTargetLoudnessAndUnderTheCeiling(double target)
    {
        var wav = Path.Combine(_dir, "out.wav");
        var (code, o, _) = await Run(Backend(), "play", OfflineBackend.SongId, wav, "--target", target.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(CliApp.Ok, code);
        Assert.Contains("peak memory", o);

        var (l, r, sr) = WavWriter.Read(wav);
        Assert.Equal(OfflineBackend.SampleRate, sr);
        var lufs = Loudness.Integrated(l, r, sr);
        Assert.NotNull(lufs);
        Assert.InRange(lufs!.Value, target - 0.5, target + 0.5);
        // the limiter keeps the true peak at or under -1 dBTP, so the sample peak is under it too
        Assert.True(Loudness.PeakDb(l, r) <= -1.0 + 0.01, $"peak {Loudness.PeakDb(l, r)} dBFS");
    }


    [Fact]
    public async Task PlayNeverBoostsAQuietSongByMoreThanSixDb()
    {
        var b = Backend();
        var wav = Path.Combine(_dir, "loud.wav");
        var (code, _, _) = await Run(b, "play", OfflineBackend.SongId, wav, "--target", "-5");
        Assert.Equal(CliApp.Ok, code);
        var (l, r, sr) = WavWriter.Read(wav);
        var lufs = Loudness.Integrated(l, r, sr);
        Assert.NotNull(lufs);
        // the engine's cap (the JS limit when the limiter runs) is +6 dB, however far the target is
        Assert.InRange(lufs!.Value, b.SongLufs + 6 - 0.3, b.SongLufs + 6 + 0.3);
    }

    [Fact]
    public async Task PlayOfAFormatItCannotDecodeSaysSo()
    {
        var (code, _, e) = await Run(new NoDecoder(Backend()), "play", OfflineBackend.SongId, Path.Combine(_dir, "x.wav"));
        Assert.Equal(CliApp.Unsupported, code);
        Assert.Contains("can't decode", e);
    }

    [Fact]
    public async Task UnknownCommandsAreUsageErrors()
    {
        Assert.Equal(CliApp.Usage, (await Run(Backend(), "dance")).Code);
        Assert.Equal(CliApp.Usage, (await Run(Backend(), "resolve")).Code);
        Assert.Equal(CliApp.Usage, (await Run(Backend(), "play", "x", "--target", "loud")).Code);
        Assert.Equal(CliApp.Ok, (await Run(Backend(), "help")).Code);
    }

    [Fact]
    public void WavRoundTrips()
    {
        var l = new float[] { 0, 0.5f, -0.25f }; var r = new float[] { 1, -1, 0.125f };
        var wav = Path.Combine(_dir, "t.wav");
        WavWriter.Write(wav, l, r, 44100);
        var (l2, r2, sr) = WavWriter.Read(wav);
        Assert.Equal(44100, sr);
        Assert.Equal(l, l2);
        Assert.Equal(r, r2);
    }

    /// <summary>Answers every request with the recorded search page and remembers what was asked.</summary>
    private sealed class SearchStub : HttpMessageHandler
    {
        public readonly List<(string Url, string Body, bool HasCookie)> Seen = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Seen.Add((request.RequestUri!.ToString(), request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct), request.Headers.Contains("Cookie")));
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(File.ReadAllText(Fixtures.Path("yt/raw/search-all.json")), System.Text.Encoding.UTF8, "application/json"),
            };
        }
    }

    [Fact]
    public async Task LiveSearchPrintsTheSameResultsAsTheRecordedPage()
    {
        // `--live search` goes through the InnerTube catalog client; here the network is a stub
        var stub = new SearchStub();
        using var live = LiveBackend.Create(new HttpClient(stub), new Soncle.YouTube.InnerTube.InnerTubeSession { VisitorData = "v" });
        var (code, o, e) = await Run(live, "search", "daft", "punk");
        Assert.Equal(CliApp.Ok, code);
        Assert.Equal("", e);
        Assert.Contains("(YouTube (anonymous))", o);
        Assert.Contains("top:", o);
        Assert.Contains("Daft Punk", o);

        var (_, offline, _) = await Run(Backend(), "search", "daft", "punk");
        // the lines after the header are the same rows as the offline backend prints
        Assert.Equal(offline.Split('\n').Skip(1), o.Split('\n').Skip(1));

        var req = Assert.Single(stub.Seen);
        Assert.Contains("/youtubei/v1/search", req.Url);
        Assert.Contains("\"query\":\"daft punk\"", req.Body);
        Assert.False(req.HasCookie);
    }

    /// <summary>The offline backend with a decoder that refuses everything (a format we cannot decode yet).</summary>
    private sealed class NoDecoder : ICliBackend
    {
        private readonly ICliBackend _inner;
        public NoDecoder(ICliBackend inner) => _inner = inner;
        public string Name => _inner.Name;
        public Task<YtSearchPage> SearchAsync(string query, CancellationToken ct) => _inner.SearchAsync(query, ct);
        public StreamResolver Resolver => _inner.Resolver;
        public IStreamFetcher Fetcher => _inner.Fetcher;
        public IAudioDecoder Decoder { get; } = new Refuses();
        private sealed class Refuses : IAudioDecoder
        {
            public bool CanDecode(string mime) => false;
            public IAudioSource Decode(Stream data, string mime) => throw new NotSupportedException(mime);
        }
    }
}
