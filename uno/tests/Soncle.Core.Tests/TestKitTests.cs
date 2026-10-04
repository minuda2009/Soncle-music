// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Text.Json;
using Soncle.TestKit;

namespace Soncle.Core.Tests;

public class TestKitTests
{
    [Fact]
    public void JsonAssert_IgnoresPropertyOrder()
    {
        JsonAssert.Equal("""{"a":1,"b":2}""", """{"b":2,"a":1}""");
    }

    [Fact]
    public void JsonAssert_ReportsTheJsonPathOfTheFirstDifference()
    {
        var expected = """{"items":[{"title":"a"},{"title":"b"},{"title":"c"},{"title":"d"}]}""";
        var actual = """{"items":[{"title":"a"},{"title":"b"},{"title":"c"},{"title":"DIFFERENT"}]}""";

        var ex = Assert.Throws<JsonException>(() => JsonAssert.Equal(expected, actual));
        Assert.Contains("$.items[3].title", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void JsonAssert_AcceptsNumbersWithinTolerance()
    {
        JsonAssert.Equal("""{"peak":-1.0}""", """{"peak":-1.05}""", numberTolerance: 0.1);
    }

    [Fact]
    public void JsonAssert_RejectsNumbersOutsideTolerance()
    {
        Assert.Throws<JsonException>(() =>
            JsonAssert.Equal("""{"peak":-1.0}""", """{"peak":-1.2}""", numberTolerance: 0.1));
    }

    [Fact]
    public void Fixtures_ResolvesTheRepositoryFixturesFolder()
    {
        // M01 adds fixtures/; either way the helper must point at the repo's fixtures folder.
        var p = Fixtures.Path("yt/raw/example.json");
        Assert.EndsWith(Path.Combine("fixtures", "yt", "raw", "example.json"), p);
    }

    [Fact]
    public void AudioAssert_MeasuresFullScaleAsZeroDb()
    {
        var tone = new float[4800];
        for (var i = 0; i < tone.Length; i++)
            tone[i] = (float)Math.Sin(2 * Math.PI * 1000 * i / 48000);
        Assert.InRange(AudioAssert.PeakDb(tone), -0.01, 0.01);
        Assert.InRange(AudioAssert.TruePeakDb(tone), -0.2, 0.2);
    }

    [Fact]
    public void AudioAssert_TruePeakNeverReadsBelowAHighFrequencyTone()
    {
        // 21 kHz at 48 kHz: the true peak is at full scale. A weaker interpolator (the one this
        // helper used to have) read ~0.5 dB low here, so a clipping signal could pass a test.
        var tone = new float[4800];
        for (var i = 0; i < tone.Length; i++)
            tone[i] = (float)Math.Sin(2 * Math.PI * 21000 * i / 48000);
        Assert.InRange(AudioAssert.PeakDb(tone), -0.1, 0.0);         // samples nearly reach the peak
        Assert.InRange(AudioAssert.TruePeakDb(tone), -0.05, 0.05);   // the true peak is full scale
    }

    [Fact]
    public void AudioAssert_TruePeakSeesBetweenSamples()
    {
        // A 12 kHz tone at 48 kHz lands a sample on every peak at ±1, but the true peak between
        // samples is a little higher than the sample peak; the meter must not under-read it.
        var tone = new float[4800];
        for (var i = 0; i < tone.Length; i++)
            tone[i] = (float)Math.Sin(2 * Math.PI * 12000 * i / 48000);
        Assert.True(AudioAssert.TruePeakDb(tone) >= AudioAssert.PeakDb(tone) - 0.05);
    }

    [Fact]
    public void JsonAssert_DoesNotTreatTwoLargeIntegersAsEqual()
    {
        // 2^53 and 2^53+1 both round to the same double; the raw text differs.
        Assert.Throws<JsonException>(() =>
            JsonAssert.Equal("""{"id":9007199254740993}""", """{"id":9007199254740994}"""));
    }
}
