// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Text.Json;
using Soncle.Core.Theme;
using Soncle.TestKit;

namespace Soncle.Core.Tests;

/// <summary>
/// Golden-diff for the dynamic-colour palette ported from <c>renderer/palette.js</c>. The authority
/// is <c>fixtures/palette/golden.json</c> (the colours the app produced before the module split),
/// pinned in JS by <c>test/palette.test.mjs</c>; the C# port must give exactly the same output.
/// Strings compare ordinally and numbers compare with no tolerance, so every colour must match
/// character-for-character.
/// </summary>
public class PaletteTests
{
    private static JsonElement Golden =>
        JsonDocument.Parse(File.ReadAllText(Fixtures.Path("palette/golden.json"))).RootElement;

    [Fact]
    public void ThemeVars_EqualsTheColoursTheAppProducedBeforeTheMove()
    {
        var cases = Golden.GetProperty("cases");
        Assert.Equal(15, cases.GetArrayLength());

        foreach (var c in cases.EnumerateArray())
        {
            var hue = c.GetProperty("hue").GetDouble();
            var sat = c.GetProperty("sat").GetDouble();
            var mode = c.GetProperty("mode").GetString()!;

            var expected = c.GetProperty("vars").GetRawText();
            var actual = JsonSerializer.Serialize(Palette.ThemeVars(hue, sat, mode));

            JsonAssert.Equal(expected, actual);
        }
    }

    [Fact]
    public void HexToHsl_EqualsTheOldResults()
    {
        foreach (var pair in Golden.GetProperty("hex").EnumerateArray())
        {
            var hex = pair[0].GetString()!;
            var expected = pair[1].GetRawText();

            var (h, s, l) = Palette.HexToHsl(hex);
            var actual = JsonSerializer.Serialize(new[] { h, s, l });

            JsonAssert.Equal(expected, actual);
        }
    }

    [Fact]
    public void DominantHue_FindsTheColourOfACoverAndIgnoresGrey()
    {
        var cover = new List<byte>();
        AddPixels(cover, 200, 40, 40, 300);
        AddPixels(cover, 30, 30, 30, 500);
        var red = Palette.DominantHue(cover.ToArray());
        Assert.NotNull(red);
        Assert.True(red!.Value.Hue < 10 || red.Value.Hue > 350, $"hue {red.Value.Hue}");

        var grey = new List<byte>();
        AddPixels(grey, 128, 128, 128, 800);
        Assert.Null(Palette.DominantHue(grey.ToArray()));
    }

    private static void AddPixels(List<byte> buffer, byte r, byte g, byte b, int count)
    {
        for (var i = 0; i < count; i++)
        {
            buffer.Add(r);
            buffer.Add(g);
            buffer.Add(b);
            buffer.Add(255);
        }
    }
}
