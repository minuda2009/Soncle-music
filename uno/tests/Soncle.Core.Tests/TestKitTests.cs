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
    public void Fixtures_ResolvesUnderTheRepositoryFixturesFolder()
    {
        // M01 adds fixtures/; until then the walk correctly reports that it is missing.
        if (!Directory.Exists(FindFixturesSibling()))
        {
            Assert.Throws<DirectoryNotFoundException>(() => Fixtures.Path("yt/raw/example.json"));
            return;
        }
        var p = Fixtures.Path("yt/raw/example.json");
        Assert.StartsWith(Fixtures.RootPath, p, StringComparison.Ordinal);
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
    public void AudioAssert_TruePeakSeesBetweenSamples()
    {
        // A 12 kHz tone at 48 kHz lands a sample on every peak at ±1, but the true peak between
        // samples is a little higher than the sample peak; the meter must not under-read it.
        var tone = new float[4800];
        for (var i = 0; i < tone.Length; i++)
            tone[i] = (float)Math.Sin(2 * Math.PI * 12000 * i / 48000);
        Assert.True(AudioAssert.TruePeakDb(tone) >= AudioAssert.PeakDb(tone) - 0.05);
    }

    private static string FindFixturesSibling()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = System.IO.Path.Combine(dir.FullName, "fixtures");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return "<none>";
    }
}
