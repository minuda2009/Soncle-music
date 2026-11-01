// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
namespace Soncle.Audio.Output;

/// <summary>
/// A short linear gain ramp over one interleaved device block, so a sound device can start and
/// stop without a click: an <c>IAudioOutput</c> ramps its first block up from silence and its last
/// block (the one it plays out before releasing the device) down to silence. The gain stays in
/// [0, 1], so it only ever attenuates what the engine rendered: the master chain's limiter is still
/// the last stage that shapes the signal and its ceiling still holds. Works in place, never allocates.
/// </summary>
public static class GainRamp
{
    /// <summary>
    /// Multiplies every frame of <paramref name="interleaved"/> by a gain that moves linearly from
    /// <paramref name="from"/> to <paramref name="to"/>: frame <c>i</c> of <c>n</c> gets
    /// <c>from + (to - from) * (i + 1) / n</c>, so the last frame lands exactly on <paramref name="to"/>
    /// and the next block continues from there. All channels of a frame get the same gain; a
    /// trailing partial frame is left untouched.
    /// </summary>
    public static void Apply(Span<float> interleaved, int channels, float from, float to)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(channels, 1);
        if (!(from >= 0 && from <= 1)) throw new ArgumentOutOfRangeException(nameof(from), from, "gain must be in [0, 1]");
        if (!(to >= 0 && to <= 1)) throw new ArgumentOutOfRangeException(nameof(to), to, "gain must be in [0, 1]");

        var frames = interleaved.Length / channels;
        if (frames == 0) return;
        var step = (to - from) / frames;
        for (var i = 0; i < frames; i++)
        {
            var gain = i == frames - 1 ? to : from + step * (i + 1);
            var frame = interleaved.Slice(i * channels, channels);
            for (var c = 0; c < channels; c++) frame[c] *= gain;
        }
    }
}
