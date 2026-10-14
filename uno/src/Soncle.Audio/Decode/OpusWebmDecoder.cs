// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
namespace Soncle.Audio.Decode;

/// <summary>One Opus decoder instance (one stream). The real one wraps a codec library; tests use a fake.</summary>
public interface IOpusDecoder : IDisposable
{
    /// <summary>
    /// Decodes one packet into <paramref name="pcm"/> as interleaved 32-bit float, <c>channels</c> values per
    /// sample. <paramref name="frameSize"/> is the room available per channel. Returns the samples per channel
    /// written (negative or zero is an error).
    /// </summary>
    int Decode(ReadOnlySpan<byte> packet, Span<float> pcm, int frameSize);
}

/// <summary>Creates decoders at 48 kHz, Opus' native rate, for 1 or 2 channels.</summary>
public interface IOpusDecoderFactory
{
    IOpusDecoder Create(int sampleRate, int channels);
}

/// <summary>A whole song decoded to float PCM at 48 kHz, interleaved stereo.</summary>
public sealed record DecodedAudio(float[] InterleavedStereo, int SampleRate)
{
    public int Frames => InterleavedStereo.Length / 2;
}

/// <summary>
/// WebM/Opus bytes in, stereo float PCM out, following RFC 7845 §4: the first <c>PreSkip</c>
/// samples are dropped, the header's output gain is applied, and the end is trimmed by the
/// container's DiscardPadding. Mono becomes dual mono. The codec itself comes from an
/// <see cref="IOpusDecoderFactory"/>; everything else here is plain C#.
/// </summary>
public static class OpusWebmDecoder
{
    public const int SampleRate = 48_000;

    /// <summary>Decodes <paramref name="file"/> in memory. Throws <see cref="NotSupportedException"/> if it has no Opus track.</summary>
    /// <exception cref="InvalidDataException">A packet the codec refuses.</exception>
    public static DecodedAudio Decode(ReadOnlyMemory<byte> file, IOpusDecoderFactory codec)
    {
        var f = WebmOpusReader.Read(file) ?? throw new NotSupportedException("no Opus audio track in this WebM");
        var ch = f.Head.Channels;
        using var dec = codec.Create(SampleRate, ch);

        var total = 0L;
        foreach (var p in f.Packets) total += p.Samples;
        var padding = f.Packets.Sum(p => p.DiscardPaddingNs) * SampleRate / 1_000_000_000L;
        var frames = Math.Max(0, total - f.Head.PreSkip - padding);

        var gain = (float)Math.Pow(10, f.Head.OutputGainDb / 20.0);
        var outPcm = new float[frames * 2];
        var buf = new float[OpusToc.MaxSamples * ch];
        long skip = f.Head.PreSkip, written = 0;

        foreach (var p in f.Packets)
        {
            if (written >= frames) break;
            // a packet we cannot size (0 samples) is skipped, like an empty one
            if (p.Samples == 0) continue;
            var n = dec.Decode(p.Data.Span, buf, OpusToc.MaxSamples);
            if (n <= 0) throw new InvalidDataException($"the Opus decoder refused a packet (code {n})");
            var from = 0;
            if (skip > 0) { var s = (int)Math.Min(skip, n); from = s; skip -= s; }
            for (var i = from; i < n && written < frames; i++, written++)
            {
                var o = (int)written * 2;
                if (ch == 1) outPcm[o] = outPcm[o + 1] = buf[i] * gain;
                else { outPcm[o] = buf[i * 2] * gain; outPcm[o + 1] = buf[i * 2 + 1] * gain; }
            }
        }
        // a stream that ends early (damaged file) gives what was decoded
        if (written < frames) Array.Resize(ref outPcm, (int)written * 2);
        return new DecodedAudio(outPcm, SampleRate);
    }
}
