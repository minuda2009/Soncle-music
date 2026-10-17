// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using Soncle.Audio;
using Soncle.Streams;
using Soncle.YouTube;
using Soncle.YouTube.InnerTube;
using Soncle.YouTube.Parsing;

namespace Soncle.Cli;

/// <summary>
/// Real YouTube, anonymous. Resolves streams through <see cref="InnerTubeStreamClient"/> with the
/// clients that return plain URLs (no PO token, no player script: IOS and ANDROID_VR), and
/// downloads with <see cref="HttpStreamFetcher"/>; searches through <see cref="InnerTubeCatalogClient"/>.
/// Decoding real songs is not part of this build. No cookie is read, sent or printed.
/// </summary>
public sealed class LiveBackend : ICliBackend, IDisposable
{
    private readonly HttpClient _http;
    private readonly InnerTubeCatalogClient _catalog;

    private LiveBackend(HttpClient http, InnerTubeSession session, StreamResolver resolver)
    {
        _http = http;
        _catalog = new InnerTubeCatalogClient(http, session);
        Resolver = resolver;
        Fetcher = new HttpStreamFetcher(http);
        Decoder = new NoDecoder();
    }

    public static async Task<LiveBackend> CreateAsync(CancellationToken ct)
    {
        var http = new HttpClient(new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All })
        {
            Timeout = TimeSpan.FromSeconds(60),
        };
        var session = await InnerTubeSession.CreateAsync(http, ct: ct);
        return Create(http, session);
    }

    /// <summary>The same backend over a given client and session (tests hand in a fake handler).</summary>
    internal static LiveBackend Create(HttpClient http, InnerTubeSession session)
    {
        var resolver = new StreamResolver(new InnerTubeStreamClient(http, session));
        resolver.SetClients(new[] { "IOS", "ANDROID_VR" });
        return new LiveBackend(http, session, resolver);
    }

    public string Name => "YouTube (anonymous)";
    public StreamResolver Resolver { get; }
    public IStreamFetcher Fetcher { get; }
    public IAudioDecoder Decoder { get; }

    public Task<YtSearchPage> SearchAsync(string query, CancellationToken ct) => _catalog.SearchAsync(query, "all", ct);

    public void Dispose() => _http.Dispose();

    private sealed class NoDecoder : IAudioDecoder
    {
        public bool CanDecode(string mime) => false;
        public IAudioSource Decode(Stream data, string mime) => throw new NotSupportedException(mime);
    }
}
