// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using Soncle.App;
using Soncle.Audio;

namespace Soncle.App.Tests;

/// <summary>
/// The user's sound settings: read from a <c>library.json</c> in the JS shape and applied to the
/// shared engine. The defaults must match <c>src/defaults.mjs</c> (normalise on, target -14 LUFS,
/// no EQ, width 1, no crossfeed), so the C# app sounds like the JS apps out of the box.
/// </summary>
public class SoundSettingsTests
{
    [Fact]
    public void MissingOrBrokenLibraryGivesTheJsDefaults()
    {
        foreach (var json in new[] { null, "", "not json", "{}", "{\"settings\":null}" })
        {
            var s = SoundSettings.FromLibraryJson(json);
            Assert.True(s.Normalize);
            Assert.Equal(-14, s.NormTarget);
            Assert.False(s.EqEnabled);
            Assert.Equal(0, s.PreampDb);
            Assert.All(s.EqGains, g => Assert.Equal(0, g));
            Assert.Equal(10, s.EqGains.Length);
            Assert.True(s.AutoHeadroom);
            Assert.False(s.LoudnessComp);
            Assert.Equal(1, s.StereoWidth);
            Assert.Equal("off", s.Crossfeed);
            Assert.False(s.Mono);
            Assert.Equal(0, s.Balance);
        }
    }

    [Fact]
    public void ReadsTheSettingsTheJsAppWrites()
    {
        const string json = """
        {
          "settings": {
            "normalize": true, "normTarget": -11, "autoHeadroom": false, "loudnessComp": true,
            "stereoWidth": 1.25, "crossfeed": "subtle", "mono": true, "balance": -0.2,
            "eq": { "enabled": true, "preset": "Rock", "preamp": -2, "gains": [5,4,2,-1,-2,-1,2,3,4,4] }
          }
        }
        """;
        var s = SoundSettings.FromLibraryJson(json);
        Assert.True(s.Normalize);
        Assert.Equal(-11, s.NormTarget);
        Assert.False(s.AutoHeadroom);
        Assert.True(s.LoudnessComp);
        Assert.Equal(1.25, s.StereoWidth);
        Assert.Equal("subtle", s.Crossfeed);
        Assert.True(s.Mono);
        Assert.Equal(-0.2, s.Balance);
        Assert.True(s.EqEnabled);
        Assert.Equal(-2, s.PreampDb);
        Assert.Equal(new double[] { 5, 4, 2, -1, -2, -1, 2, 3, 4, 4 }, s.EqGains);
    }

    [Fact]
    public void EqGainsArePaddedAndTruncatedToTenBands()
    {
        var shortGains = SoundSettings.FromLibraryJson("{\"settings\":{\"eq\":{\"gains\":[1,2,3]}}}");
        Assert.Equal(10, shortGains.EqGains.Length);
        Assert.Equal(new double[] { 1, 2, 3, 0, 0, 0, 0, 0, 0, 0 }, shortGains.EqGains);

        var longGains = SoundSettings.FromLibraryJson("{\"settings\":{\"eq\":{\"gains\":[1,2,3,4,5,6,7,8,9,10,11,12]}}}");
        Assert.Equal(10, longGains.EqGains.Length);
        Assert.Equal(10, longGains.EqGains[^1]);
    }

    [Fact]
    public void ApplyingTheSettingsReachesTheEngine()
    {
        var engine = new Engine(48_000);
        var s = new SoundSettings
        {
            NormTarget = -11, Normalize = true, EqEnabled = true, PreampDb = -3,
            EqGains = new double[] { 6, 5, 4, 2, 0, 0, 0, 0, 0, 0 },
            AutoHeadroom = false, LoudnessComp = true, StereoWidth = 1.25, Crossfeed = "subtle", Mono = true, Balance = -0.2,
        };
        s.ApplyTo(engine);

        Assert.True(engine.Master.Options.Normalize);
        Assert.Equal(-11, engine.Master.Options.NormTarget);
        Assert.True(engine.Master.Options.EqEnabled);
        Assert.Equal(-3, engine.Master.Options.PreampDb);
        Assert.Equal(6, engine.Master.Options.EqGains[0]);
        Assert.False(engine.Master.Options.AutoHeadroom);
        Assert.True(engine.Master.Options.LoudnessComp);
        Assert.Equal(1.25, engine.Master.Options.Width);
        Assert.Equal("subtle", engine.Master.Options.Crossfeed);
        Assert.True(engine.Master.Options.Mono);
        Assert.Equal(-0.2, engine.Master.Options.Balance);
    }

    [Fact]
    public void TheLimiterStaysLastWithEqAndLoudnessOn()
    {
        // the house rule: whatever the settings, the limiter is the last stage and nothing clips
        var engine = new Engine(48_000);
        var s = new SoundSettings
        {
            EqEnabled = true,
            EqGains = new double[] { 12, 12, 12, 12, 12, 12, 12, 12, 12, 12 },
            LoudnessComp = true,
            StereoWidth = 1.6,
            PreampDb = 0,
        };
        s.ApplyTo(engine);

        var source = new GeneratedSource(48_000, 220, 330, amp: 0.9, lengthFrames: 48_000);
        engine.Load(source, lufs: null);

        var left = new float[480];
        var right = new float[480];
        var peak = 0.0;
        for (var b = 0; b < 50; b++)
        {
            engine.Render(left, right, 480);
            foreach (var v in left) peak = Math.Max(peak, Math.Abs(v));
            foreach (var v in right) peak = Math.Max(peak, Math.Abs(v));
        }
        // a +12 dB EQ with no preamp would clip badly; the master chain's limiter must hold it.
        // The signal must also really be there (a silent chain would pass the bound for free).
        Assert.True(peak > 0.3, $"the chain produced almost nothing (peak {peak})");
        Assert.True(peak <= 1.0, $"true peak {peak} exceeded full scale");
    }
}
