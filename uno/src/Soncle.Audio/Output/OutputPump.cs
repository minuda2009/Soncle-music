// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
namespace Soncle.Audio.Output;

/// <summary>
/// Bridges <see cref="Engine.Render"/> (planar blocks of at most <see cref="Engine.MaxBlockFrames"/>)
/// to a device that wants one interleaved stereo buffer of any size, such as an
/// <c>IAudioOutput</c> in a head. Everything goes through the engine, so the master chain and its
/// limiter stay last. The planar scratch is allocated once, so <see cref="Fill"/> never allocates.
/// </summary>
public sealed class OutputPump
{
    private readonly Engine _engine;
    private readonly float[] _l;
    private readonly float[] _r;

    public OutputPump(Engine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _l = new float[engine.MaxBlockFrames];
        _r = new float[engine.MaxBlockFrames];
    }

    public Engine Engine => _engine;

    public int SampleRate => _engine.SampleRate;

    /// <summary>
    /// The source the head loaded, used only for <see cref="Ended"/>. The pump never reads it
    /// directly; the engine does.
    /// </summary>
    public IAudioSource? Source { get; set; }

    /// <summary>
    /// True once <see cref="Source"/> has a known length and has been read to its end. A looping
    /// source wraps before its end, so it never ends. A streaming source
    /// (<see cref="IBufferingSource"/>) says so itself: its length may be an estimate.
    /// </summary>
    public bool Ended => Source switch
    {
        IBufferingSource b => b.Ended,
        { LengthFrames: long length } s => s.Position >= length,
        _ => false,
    };

    /// <summary>
    /// Renders <c>interleaved.Length / 2</c> frames into <paramref name="interleaved"/> (L, R, ...)
    /// and returns that frame count. After the source ends the engine renders silence, so the
    /// buffer is always filled completely; an odd trailing sample is zeroed.
    /// </summary>
    public int Fill(Span<float> interleaved)
    {
        var frames = interleaved.Length / 2;
        var block = _l.Length;
        var done = 0;
        while (done < frames)
        {
            var n = Math.Min(block, frames - done);
            var l = _l.AsSpan(0, n);
            var r = _r.AsSpan(0, n);
            _engine.Render(l, r, n);
            var dst = interleaved.Slice(done * 2, n * 2);
            for (var i = 0; i < n; i++)
            {
                dst[2 * i] = l[i];
                dst[2 * i + 1] = r[i];
            }
            done += n;
        }
        if ((interleaved.Length & 1) != 0) interleaved[^1] = 0;
        return frames;
    }
}
