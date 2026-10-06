// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Text.Json;
using Soncle.Flow;
using Soncle.TestKit;

namespace Soncle.Flow.Tests;

/// <summary>Golden tests: the C# Flow port must match the JS reference in <c>fixtures/flow</c>.</summary>
public class FlowTests
{
    private static JsonElement Fixture() =>
        JsonDocument.Parse(File.ReadAllText(Fixtures.Path("flow/flow.json"))).RootElement;

    private static float[] Groove()
    {
        var bytes = File.ReadAllBytes(Fixtures.Path("flow/groove.f32"));
        var f = new float[bytes.Length / 4];
        Buffer.BlockCopy(bytes, 0, f, 0, bytes.Length);
        return f;
    }

    [Fact]
    public void CamelotCompatibilityMatchesTheJs()
    {
        foreach (var row in Fixture().GetProperty("keyCompat").EnumerateArray())
        {
            var a = row[0].ValueKind == JsonValueKind.Null ? null : row[0].GetString();
            var b = row[1].ValueKind == JsonValueKind.Null ? null : row[1].GetString();
            var expected = row[2].ValueKind == JsonValueKind.Null ? (double?)null : row[2].GetDouble();
            var got = FlowMath.KeyCompat(a, b);
            if (expected is null) Assert.Null(got);
            else Assert.Equal(expected.Value, got!.Value, 6);
        }
    }

    [Fact]
    public void TempoCompatibilityFoldsHalfAndDoubleTime()
    {
        foreach (var row in Fixture().GetProperty("tempoCompat").EnumerateArray())
        {
            var got = FlowMath.TempoCompat(row[0].GetDouble(), row[1].GetDouble());
            Assert.Equal(row[2].GetDouble(), got!.Value, 4);
        }
        Assert.Equal(1, FlowMath.TempoCompat(128, 128)!.Value, 6);
        Assert.True(FlowMath.TempoCompat(128, 64) > 0.8);
        Assert.True(FlowMath.TempoCompat(128, 175) < 0.2);
    }

    [Fact]
    public void PlanFlowMatchesTheJs()
    {
        foreach (var c in Fixture().GetProperty("planFlow").EnumerateArray())
        {
            var name = c.GetProperty("name").GetString()!;
            var seed = Track(c.GetProperty("seed"));
            var pool = c.GetProperty("pool").EnumerateArray().Select(Track).ToList();
            var feats = c.GetProperty("feat").EnumerateObject().ToDictionary(p => p.Name, p => Feature(p.Value));
            FlowFeatures? Feat(string id) => feats.TryGetValue(id, out var f) ? f : null;

            var opts = new FlowOptions();
            if (c.TryGetProperty("opts", out var o))
            {
                if (o.TryGetProperty("ctx", out var ctx) && ctx.ValueKind == JsonValueKind.String)
                    opts = opts with { Context = FlowMath.ContextFor(DateTime.Parse(ctx.GetString()!)) };
                if (o.TryGetProperty("taste", out var taste))
                {
                    var artist = taste.TryGetProperty("artist", out var a) ? a : default;
                    var skips = taste.TryGetProperty("skips", out var s) ? s : default;
                    opts = opts with
                    {
                        Taste = new FlowTaste
                        {
                            Artist = artist.ValueKind == JsonValueKind.Object
                                ? (nm) => artist.TryGetProperty(nm, out var v) ? v.GetDouble() : 0
                                : null,
                            Liked = (_) => false,
                            Skips = skips.ValueKind == JsonValueKind.Object
                                ? (id) => skips.TryGetProperty(id, out var v) ? v.GetInt32() : 0
                                : null,
                            Recent = (_) => 0,
                        }
                    };
                }
            }

            var expected = c.GetProperty("expected").EnumerateArray().Select(x => x.GetString()!).ToList();
            var got = FlowMath.PlanFlow(seed, pool, Feat, opts).Select(x => x.TrackId).ToList();
            Assert.Equal(expected, got);
        }
    }

    [Fact]
    public void AnalysisFindsTempoAndKeyOfTheSyntheticGroove()
    {
        var meta = JsonDocument.Parse(File.ReadAllText(Fixtures.Path("flow/groove.json"))).RootElement;
        var sr = meta.GetProperty("sampleRate").GetInt32();
        var exp = meta.GetProperty("expected");
        var r = Analyzer.Analyze(Groove(), sr);
        Assert.NotNull(r);
        Assert.True(Math.Abs(r!.Bpm!.Value - 128) < 2, $"bpm {r.Bpm}");
        Assert.Equal(exp.GetProperty("camelot").GetString(), r.Camelot);
        Assert.Equal(exp.GetProperty("bpmConf").GetDouble(), r.BpmConf, 2);
        Assert.Equal(exp.GetProperty("keyConf").GetDouble(), r.KeyConf, 2);
        Assert.Equal(exp.GetProperty("energy").GetDouble(), r.Energy, 3);
    }

    private static FlowTrack Track(JsonElement e) => new(
        e.GetProperty("id").GetString()!,
        e.TryGetProperty("title", out var t) ? t.GetString()! : "",
        e.TryGetProperty("duration", out var d) ? d.GetDouble() : 0,
        e.TryGetProperty("album", out var al) && al.ValueKind == JsonValueKind.Object ? al.GetProperty("name").GetString() : null,
        e.TryGetProperty("artists", out var ar) && ar.GetArrayLength() > 0 ? ar[0].GetProperty("name").GetString() : null);

    private static FlowFeatures Feature(JsonElement e)
    {
        double? D(string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
        return new FlowFeatures(D("bpm"), D("bpmConf") ?? 0, e.TryGetProperty("camelot", out var c) ? c.GetString() : null, D("keyConf") ?? 0, D("energy"));
    }
}
