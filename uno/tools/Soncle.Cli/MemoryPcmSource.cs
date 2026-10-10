// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using Soncle.Audio;

namespace Soncle.Cli;

/// <summary>A fully decoded stereo song held in memory, played by the engine like any other source.</summary>
public sealed class MemoryPcmSource : IAudioSource
{
    private readonly float[] _interleaved;
    private long _pos;

    public MemoryPcmSource(float[] interleavedStereo, int sampleRate)
    {
        _interleaved = interleavedStereo;
        SampleRate = sampleRate;
    }

    public int SampleRate { get; }
    public long? LengthFrames => _interleaved.Length / 2;
    public bool CanSeek => true;
    public long Position { get => _pos; set => _pos = value; }
    public void Seek(long frame) => _pos = frame;

    public int Read(Span<float> left, Span<float> right, int frames)
    {
        var n = (int)Math.Min(frames, Math.Max(0, LengthFrames!.Value - _pos));
        for (var i = 0; i < n; i++)
        {
            left[i] = _interleaved[(_pos + i) * 2];
            right[i] = _interleaved[(_pos + i) * 2 + 1];
        }
        _pos += n;
        return n;
    }

    /// <summary>Mono mix (the Flow analyser's input).</summary>
    public float[] ToMono()
    {
        var mono = new float[_interleaved.Length / 2];
        for (var i = 0; i < mono.Length; i++) mono[i] = 0.5f * (_interleaved[i * 2] + _interleaved[i * 2 + 1]);
        return mono;
    }
}
