// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using Soncle.Audio.Dsp;

namespace Soncle.Audio;

/// <summary>
/// Two decks and the master chain, rendered offline (no sound device): the C# replacement for
/// <c>renderer/engine.js</c>. Gapless and crossfades become sample-accurate because we schedule on
/// the frame grid instead of measuring a start delay.
/// </summary>
public sealed class Engine
{
    private readonly int _sr;
    private readonly Deck[] _decks;
    private readonly MasterChain _master;
    private int _a;                       // the active deck
    private long _pos;
    private double _xf = -1;              // crossfade progress (0..1), -1 = none
    private int _xfFrames, _xfDone;
    private string _xfCurve = "smooth";

    private const int ScratchSize = 8192;
    private readonly float[] _aL = new float[ScratchSize];
    private readonly float[] _aR = new float[ScratchSize];
    private readonly float[] _bL = new float[ScratchSize];
    private readonly float[] _bR = new float[ScratchSize];

    /// <summary>The largest block <see cref="Render"/> accepts.</summary>
    public int MaxBlockFrames => ScratchSize;

    public Engine(int sampleRate)
    {
        _sr = sampleRate;
        _decks = new[] { new Deck(sampleRate), new Deck(sampleRate) };
        _master = new MasterChain(sampleRate);
    }

    public MasterChain Master => _master;
    public int SampleRate => _sr;
    public long Position => _pos;
    public bool Crossfading => _xf >= 0;

    private Deck Active => _decks[_a];
    private Deck Other => _decks[1 - _a];

    /// <summary>Loads a track on the active deck (fade from 0 to 1).</summary>
    public void Load(IAudioSource source, double? lufs, double startAtSeconds = 0)
    {
        var d = Active;
        d.SetSource(source);
        if (startAtSeconds > 0 && source.CanSeek) source.Seek((long)(startAtSeconds * _sr));
        ApplyNorm(d, lufs);
        d.SetFade(0);
        d.FadeTo(1, Deck.FadeFrames);
        _pos = (long)(startAtSeconds * _sr);
    }

    private void ApplyNorm(Deck d, double? lufs)
    {
        var db = 0.0;
        if (_master.Options.Normalize && lufs is not null && double.IsFinite(lufs.Value))
        {
            db = _master.Options.NormTarget - lufs.Value;
            db = Math.Max(-20, Math.Min(6, db));
        }
        d.SetNormDb(db);
    }

    public void SetNormalize(bool on, double target)
    {
        _master.Options.Normalize = on;
        _master.Options.NormTarget = target;
    }

    /// <summary>Starts a crossfade to a new source; the old deck fades out over the same time.</summary>
    public void CrossfadeTo(IAudioSource next, double? lufs, double seconds, string style = "fade", string curve = "smooth")
    {
        var to = Other;
        to.SetSource(next);
        ApplyNorm(to, lufs);
        to.SetFade(0);
        var frames = Math.Max(1, (int)(seconds * _sr));
        to.FadeTo(1, frames);
        Active.FadeTo(0, frames);
        _xf = 0;
        _xfFrames = frames;
        _xfDone = 0;
        _xfCurve = curve;
        // mix applies a bass swap halfway through
        if (style == "mix")
        {
            to.SetBassDb(-26);
            Active.SetBassDb(0);
        }
    }

    /// <summary>Hands over to the next source on the exact frame the current one ends (no fade).</summary>
    public void GaplessTo(IAudioSource next, double? lufs)
    {
        var to = Other;
        to.SetSource(next);
        ApplyNorm(to, lufs);
        to.SetFade(1);          // gapless: the next song starts at full gain on its first sample
        _gaplessPending = true;
    }

    private bool _gaplessPending;

    /// <summary>
    /// Renders <paramref name="frames"/> of stereo audio through the master chain. Scratch buffers
    /// are preallocated, so this allocates nothing per call (the M10 allocation rule).
    /// </summary>
    public void Render(Span<float> left, Span<float> right, int frames)
    {
        if (frames > ScratchSize) throw new ArgumentOutOfRangeException(nameof(frames), $"block must be ≤ {ScratchSize}");
        var aL = _aL; var aR = _aR; var bL = _bL; var bR = _bR;
        var produced = 0;
        while (produced < frames)
        {
            var want = frames - produced;
            var na = Active.Render(aL.AsSpan(produced, want), aR.AsSpan(produced, want), want);
            produced += na;
            if (produced >= frames) break;
            if (!_gaplessPending) break;         // deck ended with no hand-over: pad with silence
            _a = 1 - _a;                         // hand over on the exact frame the last one ended
            _gaplessPending = false;
        }

        if (_xf >= 0)
        {
            // a crossfade needs both decks for the whole block
            var nb = Other.Render(bL, bR, frames);
            for (var i = 0; i < frames; i++)
            {
                var t = _xfFrames == 0 ? 1 : Math.Min(1, _xfDone / (double)_xfFrames);
                var (gi, go) = CrossfadeCurves.Gains(_xfCurve, t);
                left[i] = (float)(aL[i] * go + bL[i] * gi);
                right[i] = (float)(aR[i] * go + bR[i] * gi);
                _xfDone++;
                if (_xfDone >= _xfFrames) { _xf = -1; _a = 1 - _a; }
            }
        }
        else
        {
            for (var i = 0; i < produced; i++) { left[i] = aL[i]; right[i] = aR[i]; }
            for (var i = produced; i < frames; i++) { left[i] = 0; right[i] = 0; }
        }
        _master.Process(left, right, frames);
        _pos += frames;
    }
}
