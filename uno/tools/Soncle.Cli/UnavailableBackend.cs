// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using Soncle.Audio;
using Soncle.Streams;
using Soncle.YouTube;
using Soncle.YouTube.Parsing;

namespace Soncle.Cli;

/// <summary>Used when no backend is configured: only `help` works.</summary>
internal sealed class UnavailableBackend : ICliBackend
{
    public string Name => "none";
    public Task<YtSearchPage> SearchAsync(string query, CancellationToken ct) => throw new NotSupportedException("no backend");
    public StreamResolver Resolver => throw new NotSupportedException("no backend");
    public IStreamFetcher Fetcher => throw new NotSupportedException("no backend");
    public IAudioDecoder Decoder => throw new NotSupportedException("no backend");
}
