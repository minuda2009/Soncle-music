// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using Soncle.Audio.Dsp;

namespace Soncle.Audio;

/// <summary>Neutral-stage bypass and the master chain settings.</summary>
public sealed class EngineOptions
{
    public bool Normalize { get; set; } = true;
    public double NormTarget { get; set; } = -14;
    public bool AutoHeadroom { get; set; } = true;
    public bool LoudnessComp { get; set; }
    public double Width { get; set; } = 1;
    public string Crossfeed { get; set; } = "off";
    public bool Mono { get; set; }
    public double Balance { get; set; }
    public double PreampDb { get; set; }
    public double[] EqGains { get; set; } = new double[10];
    public bool EqEnabled { get; set; }
    public double BassShelfDb { get; set; }
}

/// <summary>
/// One deck: source → norm → bass shelf → fade. Sample-accurate scheduling
/// (<c>StartAt</c>, <c>FadeTo</c>) replaces Web Audio's clock.
/// </summary>
public sealed class Deck
{
    public const int FadeFrames = 6720;   // 0.14 s at 48 kHz (the JS FADE)

    private readonly int _sr;
    private readonly Biquad _bass = new();
    private readonly float[] _tmpL, _tmpR;

    private IAudioSource? _source;
    private double _fade = 1, _fadeTarget = 1;
    private int _fadeLeft;
    private double _fadeStep;
    private double _normGain = 1;

    public Deck(int sampleRate)
    {
        _sr = sampleRate;
        _tmpL = new float[8192];
        _tmpR = new float[8192];
        _bass.LowShelf(sampleRate, 180, 0);
    }

    public IAudioSource? Source => _source;
    public bool Active => _source is not null;
    public double Fade => _fade;
    public double NormDb { get; private set; }

    public void SetSource(IAudioSource? source)
    {
        _source = source;
        _bass.Reset();
    }

    public void SetNormDb(double db) { NormDb = db; _normGain = Math.Pow(10, db / 20); }

    public void SetBassDb(double db)
    {
        _bass.LowShelf(_sr, 180, db);
    }

    public void SetFade(double gain) { _fade = _fadeTarget = gain; _fadeLeft = 0; }

    public void FadeTo(double gain, int frames)
    {
        _fadeTarget = gain;
        if (frames <= 0) { _fade = gain; _fadeLeft = 0; return; }
        _fadeStep = (gain - _fade) / frames;
        _fadeLeft = frames;
    }

    /// <summary>Reads and processes <paramref name="frames"/>; returns frames produced.</summary>
    public int Render(Span<float> left, Span<float> right, int frames)
    {
        if (_source is null) { left[..frames].Clear(); right[..frames].Clear(); return frames; }
        var done = 0;
        while (done < frames)
        {
            var n = Math.Min(Math.Min(_tmpL.Length, frames - done), Math.Max(0, 1 << 20));
            var read = _source.Read(_tmpL.AsSpan(0, n), _tmpR.AsSpan(0, n), n);
            if (read == 0) { left[done..frames].Clear(); right[done..frames].Clear(); return done; }
            for (var i = 0; i < read; i++)
            {
                var g = _normGain * _fade;
                if (_fadeLeft > 0) { _fade += _fadeStep; _fadeLeft--; }
                left[done + i] = (float)(_bass.Process(_tmpL[i]) * g);
                right[done + i] = (float)(_bass.Process(_tmpR[i]) * g);
            }
            done += read;
            if (read < n) { left[done..frames].Clear(); right[done..frames].Clear(); return done; }
        }
        return done;
    }
}

/// <summary>
/// The master chain: preamp → EQ → loudness comp → width/crossfeed → master → mono → pan → limiter.
/// Neutral stages are bypassed, exactly as the JS <c>#rewire</c> does.
/// </summary>
public sealed class MasterChain
{
    private readonly int _sr;
    private readonly Biquad[] _eqBands;
    private readonly StereoSpace _space;
    private readonly TruePeakLimiter _limiter;
    private readonly float[] _interleaved;

    public EngineOptions Options { get; } = new();

    public MasterChain(int sampleRate)
    {
        _sr = sampleRate;
        _eqBands = new Biquad[10];
        for (var i = 0; i < _eqBands.Length; i++) _eqBands[i] = new Biquad();
        _space = new StereoSpace(sampleRate);
        _limiter = new TruePeakLimiter(sampleRate);
        _interleaved = new float[8192 * 2];
        ConfigureEq();
    }

    public TruePeakLimiter Limiter => _limiter;

    public void ConfigureEq()
    {
        double[] bands = { 31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000 };
        for (var i = 0; i < bands.Length; i++)
        {
            var type = i == 0 ? "lowshelf" : i == bands.Length - 1 ? "highshelf" : "peaking";
            var g = Options.EqEnabled ? Options.EqGains[i] : 0;
            if (type == "lowshelf") _eqBands[i].LowShelf(_sr, bands[i], g);
            else if (type == "highshelf") _eqBands[i].HighShelf(_sr, bands[i], g);
            else _eqBands[i].Peaking(_sr, bands[i], g);
        }
    }

    public void SetEq(bool enabled, double[] gains)
    {
        Options.EqEnabled = enabled;
        Options.EqGains = gains;
        ConfigureEq();
    }

    public void SetSound(bool autoHeadroom, bool loudnessComp, double width, string crossfeed)
    {
        Options.AutoHeadroom = autoHeadroom;
        Options.LoudnessComp = loudnessComp;
        Options.Width = width;
        Options.Crossfeed = crossfeed;
        _space.Set(width, crossfeed switch { "light" => 0.35, "strong" => 0.55, _ => 0 });
    }

    public void SetChannel(bool mono, double balance)
    {
        Options.Mono = mono;
        Options.Balance = balance;
    }

    /// <summary>Processes a stereo block in place through the whole chain.</summary>
    public void Process(Span<float> left, Span<float> right, int frames)
    {
        var eqOn = Options.EqEnabled;
        var spaceOn = Options.Crossfeed != "off" || Math.Abs(Options.Width - 1) > 0.001;
        var master = Math.Pow(10, Options.PreampDb / 20);
        for (var i = 0; i < frames; i++)
        {
            double l = left[i], r = right[i];
            if (eqOn)
                for (var b = 0; b < _eqBands.Length; b++) { l = _eqBands[b].Process((float)l); r = _eqBands[b].Process((float)r); }
            l *= master; r *= master;
            float sl, sr;
            if (spaceOn) _space.Process((float)l, (float)r, out sl, out sr);
            else { sl = (float)l; sr = (float)r; }
            if (Options.Mono) { var m = (sl + sr) / 2; sl = sr = m; }
            if (Options.Balance != 0)
            {
                // equal-power stereo panner, as Web Audio's StereoPannerNode
                var x = (Math.Max(-1, Math.Min(1, Options.Balance)) + 1) / 2;
                sl *= (float)Math.Cos(x * Math.PI / 2);
                sr *= (float)Math.Sin(x * Math.PI / 2);
            }
            left[i] = sl; right[i] = sr;
        }
        // limiter last, interleaved (the buffer is preallocated)
        var interleaved = _interleaved;
        for (var i = 0; i < frames; i++) { interleaved[i * 2] = left[i]; interleaved[i * 2 + 1] = right[i]; }
        _limiter.Process(interleaved.AsSpan(0, frames * 2), frames);
        for (var i = 0; i < frames; i++) { left[i] = interleaved[i * 2]; right[i] = interleaved[i * 2 + 1]; }
    }
}
