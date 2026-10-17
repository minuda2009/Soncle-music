// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using Soncle.Audio.Decode;

namespace Soncle.Flow;

/// <summary>Reads byte ranges [from, to) of one song. <c>Total</c> is the file size, or 0 when it is unknown.</summary>
public interface IRangeReader
{
    Task<(byte[] Bytes, long Total)> ReadAsync(long from, long to, CancellationToken ct);
}

/// <summary>
/// Fetch about 30 s of a song and decode it to mono PCM for analysis: a port of <c>renderer/flow/sampler.js</c>
/// (<c>sample</c>, <c>middle</c>). Only the head and one run of clusters are downloaded. WebM/Opus only;
/// anything else gives null (the JS hands the bytes to Chromium, which C# has no equivalent of yet).
/// </summary>
public static class Sampler
{
    public const int SampleRate = 22050;
    public const int HeadBytes = 96 * 1024;

    /// <summary>The middle <paramref name="secs"/> seconds (starting 35% in, pulled back to fit), or all of a shorter run.</summary>
    public static ReadOnlyMemory<float> Middle(float[] pcm, int secs = 15)
    {
        var n = SampleRate * secs;
        if (pcm.Length <= n) return pcm;
        var s = (int)Math.Floor(pcm.Length * 0.35);
        var from = Math.Min(s, pcm.Length - n);
        return pcm.AsMemory(from, n);
    }

    /// <summary>Mono PCM at 22.05 kHz, or null when this is not a WebM we can decode.</summary>
    /// <param name="minBytes">Fewer cluster bytes than this are not worth decoding (the JS uses 20 000).</param>
    public static async Task<ReadOnlyMemory<float>?> SampleAsync(IRangeReader reader, IOpusDecoderFactory codec,
        int secs = 15, int minBytes = 20_000, CancellationToken ct = default)
    {
        var (head, total) = await reader.ReadAsync(0, HeadBytes, ct);
        var h = WebmCues.ParseHead(head);
        if (h is null) return null;
        var rg = WebmCues.PickRange(h, total > 0 ? total : double.PositiveInfinity, 0.35, secs);
        if (rg is null) return null;
        var to = (long)Math.Min(rg.Value.To, rg.Value.From + 500_000.0);
        var (bytes, _) = await reader.ReadAsync(rg.Value.From, to, ct);
        var clusters = WebmCues.WholeClusters(bytes);
        if (clusters.Length <= minBytes) return null;
        var audio = OpusWebmDecoder.Decode(WebmCues.BuildFile(h, clusters), codec);
        var mono = Resampler.Resample(Resampler.MonoFromStereo(audio.InterleavedStereo), audio.SampleRate, SampleRate);
        return Middle(mono, secs);
    }
}
