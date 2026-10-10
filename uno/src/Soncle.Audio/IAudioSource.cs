// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
namespace Soncle.Audio;

/// <summary>
/// A pull-based PCM source at a fixed rate. Tests use <see cref="GeneratedSource"/> and
/// <see cref="PcmFileSource"/>; decoding WebM/Opus comes later (M12/heads).
/// </summary>
public interface IAudioSource
{
    int SampleRate { get; }
    /// <summary>Fills <paramref name="left"/>/<paramref name="right"/> with <paramref name="frames"/>; returns frames produced (0 at end).</summary>
    int Read(Span<float> left, Span<float> right, int frames);
    bool CanSeek { get; }
    void Seek(long frame);
    long Position { get; set; }
    long? LengthFrames { get; }
}

/// <summary>A generated test source: a sine per channel, or silence.</summary>
public sealed class GeneratedSource : IAudioSource
{
    private readonly double _freqL, _freqR, _amp;
    private long _pos;
    public int SampleRate { get; }
    public long? LengthFrames { get; }
    public bool CanSeek => true;
    public long Position { get => _pos; set => _pos = value; }

    public GeneratedSource(int sampleRate, double freqL, double freqR, double amp = 0.3, long? lengthFrames = null)
    {
        SampleRate = sampleRate; _freqL = freqL; _freqR = freqR; _amp = amp; LengthFrames = lengthFrames;
    }

    public int Read(Span<float> left, Span<float> right, int frames)
    {
        var n = frames;
        if (LengthFrames is not null) n = (int)Math.Min(n, Math.Max(0, LengthFrames.Value - _pos));
        for (var i = 0; i < n; i++)
        {
            var t = _pos + i;
            left[i] = (float)(_amp * Math.Sin(2 * Math.PI * _freqL * t / SampleRate));
            right[i] = (float)(_amp * Math.Sin(2 * Math.PI * _freqR * t / SampleRate));
        }
        _pos += n;
        return n;
    }

    public void Seek(long frame) => _pos = frame;
}

/// <summary>A source reading interleaved float PCM from a fixture file.</summary>
public sealed class PcmFileSource : IAudioSource
{
    private readonly float[] _data;
    private long _pos;
    public int SampleRate { get; }
    public long? LengthFrames => _data.Length / 2;
    public bool CanSeek => true;
    public long Position { get => _pos; set => _pos = value; }

    public PcmFileSource(string path, int sampleRate)
    {
        var bytes = File.ReadAllBytes(path);
        _data = new float[bytes.Length / 4];
        Buffer.BlockCopy(bytes, 0, _data, 0, bytes.Length);
        SampleRate = sampleRate;
    }

    public int Read(Span<float> left, Span<float> right, int frames)
    {
        var avail = (int)Math.Min(frames, LengthFrames!.Value - _pos);
        for (var i = 0; i < avail; i++) { left[i] = _data[(_pos + i) * 2]; right[i] = _data[(_pos + i) * 2 + 1]; }
        _pos += avail;
        return avail;
    }

    public void Seek(long frame) => _pos = frame;
}
