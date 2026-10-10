// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using Soncle.Audio;
using Soncle.Streams;
using Soncle.YouTube;
using Soncle.YouTube.Parsing;

namespace Soncle.Cli;

/// <summary>
/// Everything the command line needs from the outside world. The offline backend answers from
/// fixtures (used by CI); a real backend (InnerTube + HTTP) plugs in here later without touching
/// the commands.
/// </summary>
public interface ICliBackend
{
    /// <summary>A short label printed by the commands, e.g. "offline fixtures".</summary>
    string Name { get; }

    Task<YtSearchPage> SearchAsync(string query, CancellationToken ct);

    /// <summary>Resolves streams with the portable rules in <see cref="StreamResolver"/>.</summary>
    StreamResolver Resolver { get; }

    /// <summary>How a byte range of a song is fetched.</summary>
    IStreamFetcher Fetcher { get; }

    /// <summary>Turns downloaded bytes into PCM, or throws <see cref="NotSupportedException"/> for a mime it cannot decode.</summary>
    IAudioDecoder Decoder { get; }
}

/// <summary>Decodes a downloaded song into a PCM source the engine can play.</summary>
public interface IAudioDecoder
{
    /// <summary>Whether <paramref name="mime"/> can be decoded here.</summary>
    bool CanDecode(string mime);

    /// <summary>Reads the whole stream; the result is fully in memory.</summary>
    IAudioSource Decode(Stream data, string mime);
}
