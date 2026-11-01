// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using Soncle.Audio.Decode;

namespace Soncle.Audio.Opus;

/// <summary>
/// Decodes a whole WebM/Opus file (for example a clip bundled with a head) with the managed
/// Concentus codec into a <see cref="MemoryPcmSource"/> at 48 kHz stereo, ready for
/// <see cref="Engine.Load"/>. This is the same path the CLI uses, so heads don't duplicate it.
/// </summary>
public static class WebmOpusClip
{
    /// <exception cref="NotSupportedException">The file has no Opus audio track.</exception>
    /// <exception cref="InvalidDataException">A packet the codec refuses.</exception>
    public static MemoryPcmSource Load(ReadOnlyMemory<byte> webm, bool loop = false)
    {
        var pcm = OpusWebmDecoder.Decode(webm, new ConcentusOpusDecoderFactory());
        return new MemoryPcmSource(pcm.InterleavedStereo, pcm.SampleRate, loop);
    }
}
