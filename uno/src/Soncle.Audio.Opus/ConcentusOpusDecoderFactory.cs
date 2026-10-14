// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using Soncle.Audio.Decode;
using Concentus;

namespace Soncle.Audio.Opus;

/// <summary>
/// The real Opus codec for <see cref="OpusWebmDecoder"/>: Concentus, a managed port of libopus
/// (no native library, so it runs the same on Windows and Android). Kept in its own project so the
/// rest of the audio code builds without the package. Checked against ffmpeg's numbers in
/// <c>Soncle.Audio.Opus.Tests</c>.
/// </summary>
public sealed class ConcentusOpusDecoderFactory : IOpusDecoderFactory
{
    public Decode.IOpusDecoder Create(int sampleRate, int channels) =>
        new Adapter(OpusCodecFactory.CreateDecoder(sampleRate, channels));

    private sealed class Adapter : Decode.IOpusDecoder
    {
        private readonly Concentus.IOpusDecoder _d;
        public Adapter(Concentus.IOpusDecoder d) => _d = d;
        public int Decode(ReadOnlySpan<byte> packet, Span<float> pcm, int frameSize) => _d.Decode(packet, pcm, frameSize, false);
        public void Dispose() => (_d as IDisposable)?.Dispose();
    }
}
