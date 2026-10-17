// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using Soncle.Audio;
using Soncle.Audio.Decode;
using Soncle.Audio.Opus;

namespace Soncle.Cli;

/// <summary>YouTube's WebM/Opus audio, decoded in memory to float PCM (managed codec, no native library).</summary>
public sealed class OpusDecoder : IAudioDecoder
{
    private readonly IOpusDecoderFactory _codec;

    internal IOpusDecoderFactory Codec => _codec;

    public OpusDecoder() : this(new ConcentusOpusDecoderFactory()) { }
    public OpusDecoder(IOpusDecoderFactory codec) => _codec = codec;

    // "audio/webm" or "audio/webm; codecs=\"opus\"" (the resolver reports the container's mime)
    public bool CanDecode(string mime) =>
        mime.StartsWith("audio/webm", StringComparison.OrdinalIgnoreCase)
        && (!mime.Contains("codecs", StringComparison.OrdinalIgnoreCase) || mime.Contains("opus", StringComparison.OrdinalIgnoreCase));

    public IAudioSource Decode(Stream data, string mime)
    {
        if (!CanDecode(mime)) throw new NotSupportedException(mime);
        using var ms = new MemoryStream();
        data.CopyTo(ms);
        var pcm = OpusWebmDecoder.Decode(ms.ToArray(), _codec);
        return new MemoryPcmSource(pcm.InterleavedStereo, pcm.SampleRate);
    }
}
