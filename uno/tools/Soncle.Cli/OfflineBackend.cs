// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Globalization;
using System.Text.Json;
using Soncle.Audio;
using Soncle.Audio.Dsp;
using Soncle.Streams;
using Soncle.YouTube;
using Soncle.YouTube.Parsing;

namespace Soncle.Cli;

/// <summary>
/// A backend that never touches the network: search answers come from a recorded YouTube Music
/// response in <c>fixtures/yt/raw</c>, and the one playable "song" is a synthetic stereo tone whose
/// loudness is measured at start-up and reported the way YouTube reports it (<c>loudnessDb</c>).
/// CI runs every command against this.
/// </summary>
public sealed class OfflineBackend : ICliBackend
{
    /// <summary>The only video id the offline backend can play.</summary>
    public const string SongId = "offline-song";
    /// <summary>Raw little-endian 32-bit float stereo at 48 kHz, written by this backend only.</summary>
    public const string Mime = "audio/x-soncle-f32";
    public const int SampleRate = 48000;

    private readonly string _fixturesRoot;
    private readonly byte[] _song;
    private readonly double _songLufs;

    public OfflineBackend(string fixturesRoot, double seconds = 6, double amplitude = 0.14)
    {
        _fixturesRoot = fixturesRoot;
        (_song, _songLufs) = MakeSong(seconds, amplitude);
        var client = new FixtureStreamClient(this);
        Resolver = new StreamResolver(client);
        Resolver.SetClients(new[] { "IOS" });   // no PO token needed
        Fetcher = new MemoryFetcher(_song);
        Decoder = new F32Decoder();
    }

    public string Name => "offline fixtures";
    public StreamResolver Resolver { get; }
    public IStreamFetcher Fetcher { get; }
    public IAudioDecoder Decoder { get; }

    /// <summary>The synthetic song's integrated loudness in LUFS (measured, not assumed).</summary>
    public double SongLufs => _songLufs;
    internal long SongLength => _song.Length;

    public Task<YtSearchPage> SearchAsync(string query, CancellationToken ct)
    {
        var path = Path.Combine(_fixturesRoot, "yt", "raw", "search-all.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return Task.FromResult(Pages.Search(doc.RootElement.Clone(), query, "all"));
    }

    private static (byte[] Bytes, double Lufs) MakeSong(double seconds, double amp)
    {
        var frames = (int)(seconds * SampleRate);
        var pcm = new float[frames * 2];
        var kf = new KFilter(SampleRate);
        var integ = new Integrator(SampleRate);
        for (var i = 0; i < frames; i++)
        {
            var l = (float)(amp * Math.Sin(2 * Math.PI * 440.0 * i / SampleRate));
            var r = (float)(amp * Math.Sin(2 * Math.PI * 554.37 * i / SampleRate));
            pcm[i * 2] = l; pcm[i * 2 + 1] = r;
            integ.Add(kf.Square(l, 0) + kf.Square(r, 1));
        }
        var bytes = new byte[pcm.Length * 4];
        Buffer.BlockCopy(pcm, 0, bytes, 0, bytes.Length);
        return (bytes, integ.Value() ?? -70);
    }

    private sealed class FixtureStreamClient : IStreamClient
    {
        private readonly OfflineBackend _b;
        public FixtureStreamClient(OfflineBackend b) => _b = b;
        public string UserAgent => "soncle-cli-offline";

        public Task<PlayerResponse> GetPlayerAsync(string videoId, string client, string? potToken, CancellationToken ct)
        {
            if (videoId != SongId)
                return Task.FromResult(new PlayerResponse("UNPLAYABLE", "not in the offline fixtures", Array.Empty<AudioFormat>()));
            // YouTube's loudnessDb is relative to -14 LUFS (LUFS = -14 + loudnessDb)
            var loudnessDb = (_b._songLufs + 14).ToString("R", CultureInfo.InvariantCulture);
            var fmt = new AudioFormat("offline://" + videoId, Mime, 1_536_000, _b._song.Length, LoudnessDb: loudnessDb);
            return Task.FromResult(new PlayerResponse("OK", null, new[] { fmt }));
        }

        public Task<string?> CanSeekAsync(string client, string url, IReadOnlyDictionary<string, string> headers, long length, CancellationToken ct) =>
            Task.FromResult<string?>(null);

        public Task<long> ProbeLengthAsync(string client, string url, IReadOnlyDictionary<string, string> headers, CancellationToken ct) =>
            Task.FromResult((long)_b._song.Length);
    }

    private sealed class MemoryFetcher : IStreamFetcher
    {
        private readonly byte[] _bytes;
        public MemoryFetcher(byte[] bytes) => _bytes = bytes;

        public Task<Stream> GetAsync(string url, IReadOnlyDictionary<string, string> headers, long from, long to, CancellationToken cancellationToken = default)
        {
            if (from < 0 || from >= _bytes.Length) throw new StreamHttpException(416);
            var end = Math.Min(to, _bytes.Length - 1);
            return Task.FromResult<Stream>(new MemoryStream(_bytes, (int)from, (int)(end - from + 1), writable: false));
        }
    }

    private sealed class F32Decoder : IAudioDecoder
    {
        public bool CanDecode(string mime) => mime.StartsWith(Mime, StringComparison.OrdinalIgnoreCase);

        public IAudioSource Decode(Stream data, string mime)
        {
            if (!CanDecode(mime)) throw new NotSupportedException(mime);
            using var ms = new MemoryStream();
            data.CopyTo(ms);
            var bytes = ms.ToArray();
            var pcm = new float[bytes.Length / 4];
            Buffer.BlockCopy(bytes, 0, pcm, 0, pcm.Length * 4);
            return new MemoryPcmSource(pcm, SampleRate);
        }
    }
}
