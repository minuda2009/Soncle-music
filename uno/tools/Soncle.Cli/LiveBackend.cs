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
/// downloads with <see cref="HttpStreamFetcher"/>. Search and decoding are not part of this build.
/// No cookie is read, sent or printed.
/// </summary>
public sealed class LiveBackend : ICliBackend, IDisposable
{
    private readonly HttpClient _http;

    private LiveBackend(HttpClient http, StreamResolver resolver)
    {
        _http = http;
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
        var resolver = new StreamResolver(new InnerTubeStreamClient(http, session));
        resolver.SetClients(new[] { "IOS", "ANDROID_VR" });
        return new LiveBackend(http, resolver);
    }

    public string Name => "YouTube (anonymous)";
    public StreamResolver Resolver { get; }
    public IStreamFetcher Fetcher { get; }
    public IAudioDecoder Decoder { get; }

    public Task<YtSearchPage> SearchAsync(string query, CancellationToken ct) =>
        throw new InvalidOperationException("search on real YouTube Music is not built yet (it needs the catalog's InnerTube search call). Use `resolve <videoId>` or `download <videoId> <file>`.");

    public void Dispose() => _http.Dispose();

    private sealed class NoDecoder : IAudioDecoder
    {
        public bool CanDecode(string mime) => false;
        public IAudioSource Decode(Stream data, string mime) => throw new NotSupportedException(mime);
    }
}
