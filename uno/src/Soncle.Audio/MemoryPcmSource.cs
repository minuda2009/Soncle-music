// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
namespace Soncle.Audio;

/// <summary>
/// A fully decoded stereo song held in memory (interleaved L, R), played by the engine like any
/// other source. With <see cref="Loop"/> it wraps to frame 0 exactly at <see cref="LengthFrames"/>,
/// so a short bundled clip can play for as long as a measurement needs.
/// </summary>
public sealed class MemoryPcmSource : IAudioSource
{
    private readonly float[] _interleaved;
    private long _pos;

    public MemoryPcmSource(float[] interleavedStereo, int sampleRate, bool loop = false)
    {
        _interleaved = interleavedStereo ?? throw new ArgumentNullException(nameof(interleavedStereo));
        SampleRate = sampleRate;
        Loop = loop;
    }

    public int SampleRate { get; }
    public bool Loop { get; set; }
    public long? LengthFrames => _interleaved.Length / 2;
    public bool CanSeek => true;
    public long Position { get => _pos; set => _pos = value; }
    public void Seek(long frame) => _pos = frame;

    public int Read(Span<float> left, Span<float> right, int frames)
    {
        var length = (long)(_interleaved.Length / 2);
        var done = 0;
        while (done < frames)
        {
            if (_pos >= length)
            {
                if (!Loop || length == 0) break;
                _pos = 0;
            }
            var n = (int)Math.Min(frames - done, length - _pos);
            if (n <= 0) break;
            var src = (int)(_pos * 2);
            for (var i = 0; i < n; i++)
            {
                left[done + i] = _interleaved[src + i * 2];
                right[done + i] = _interleaved[src + i * 2 + 1];
            }
            _pos += n;
            done += n;
        }
        if (Loop && length > 0 && _pos >= length) _pos = 0;
        return done;
    }

    /// <summary>Mono mix (the Flow analyser's input).</summary>
    public float[] ToMono()
    {
        var mono = new float[_interleaved.Length / 2];
        for (var i = 0; i < mono.Length; i++) mono[i] = 0.5f * (_interleaved[i * 2] + _interleaved[i * 2 + 1]);
        return mono;
    }
}
