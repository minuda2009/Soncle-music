// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using Soncle.Flow;
using Soncle.Streams;

namespace Soncle.Cli;

/// <summary>Reads byte ranges of one resolved stream, so the sampler downloads a few hundred KB instead of the song.</summary>
public sealed class StreamRangeReader : IRangeReader
{
    private readonly IStreamFetcher _fetcher;
    private readonly ResolvedStream _s;

    public StreamRangeReader(IStreamFetcher fetcher, ResolvedStream s) { _fetcher = fetcher; _s = s; }

    public async Task<(byte[] Bytes, long Total)> ReadAsync(long from, long to, CancellationToken ct)
    {
        // the fetcher's end is inclusive; ours is exclusive, and never past the end of the file
        var last = _s.Length > 0 ? Math.Min(to, _s.Length) - 1 : to - 1;
        await using var input = await _fetcher.GetAsync(_s.Url, _s.Headers, from, last, ct);
        using var ms = new MemoryStream();
        await input.CopyToAsync(ms, ct);
        return (ms.ToArray(), _s.Length);
    }
}
