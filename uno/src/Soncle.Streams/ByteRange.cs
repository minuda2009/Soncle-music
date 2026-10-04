// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Globalization;
using System.Text.RegularExpressions;

namespace Soncle.Streams;

/// <summary>
/// A byte range, ported from <c>parseRange</c> in <c>src/streamproxy.mjs</c>. C# decoding reads
/// welcome a stream from a position directly (<see cref="SongDownloader.OpenRead"/>), but the heads
/// still speak HTTP ranges, so the same semantics live here.
/// </summary>
public readonly record struct ByteRange(long Start, long? End)
{
    private static readonly Regex Pattern = new(@"bytes=(\d*)-(\d*)", RegexOptions.Compiled);

    /// <summary>
    /// Parses a <c>Range</c> header against a known size. A suffix range <c>bytes=-500</c> takes the
    /// last 500 bytes; <c>bytes=N-</c> runs to the end. <see cref="End"/> is the last byte index
    /// (inclusive), or null when the size is unknown. <see cref="Start"/> ≥ size means unsatisfiable.
    /// </summary>
    public static ByteRange Parse(string? range, long size)
    {
        long start = 0;
        long? end = size > 0 ? size - 1 : null;
        var m = range is null ? Match.Empty : Pattern.Match(range);
        if (m.Success)
        {
            var first = m.Groups[1].Value;
            var second = m.Groups[2].Value;
            if (first.Length > 0) start = long.Parse(first, CultureInfo.InvariantCulture);
            else if (second.Length > 0 && size > 0) start = Math.Max(0, size - long.Parse(second, CultureInfo.InvariantCulture));
            if (first.Length > 0 && second.Length > 0)
            {
                var bounded = long.Parse(second, CultureInfo.InvariantCulture);
                end = size > 0 ? Math.Min(bounded, size - 1) : bounded;
            }
        }
        return new ByteRange(start, end);
    }
}
