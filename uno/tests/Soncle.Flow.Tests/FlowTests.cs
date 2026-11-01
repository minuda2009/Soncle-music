// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Text.Json;
using Soncle.Core.Models;
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

    // ---- whole-pool measuring (roadmap item 8) ----
    // The C# port must match the JS reference in fixtures/flow/flow.json (wholePool) exactly.
    [Fact]
    public void CoverageCountsKnownGivenUpAndPendingSongs()
    {
        var w = Fixture().GetProperty("wholePool");
        foreach (var c in w.GetProperty("coverage").EnumerateArray())
        {
            var pool = RadioPool(10);
            var known = c.GetProperty("known").EnumerateArray().Select(x => x.GetString()!).ToHashSet();
            var failed = c.GetProperty("failed").EnumerateArray().Select(x => x.GetString()!).ToHashSet();
            var exp = c.GetProperty("expected");
            var got = FlowMath.FlowCoverageOf(pool, (id) => known.Contains(id) ? new FlowFeatures() : null, failed);
            Assert.Equal(exp.GetProperty("total").GetInt32(), got.Total);
            Assert.Equal(exp.GetProperty("known").GetInt32(), got.Known);
            Assert.Equal(exp.GetProperty("failed").GetInt32(), got.Failed);
            Assert.Equal(exp.GetProperty("pending").GetInt32(), got.Pending);
            Assert.Equal(exp.GetProperty("done").GetBoolean(), got.Done);
            Assert.Equal(exp.GetProperty("partial").GetBoolean(), got.Partial);
        }
    }

    [Fact]
    public void MeasuringOrderPutsNextSlotsFirstAndSkipsKnownOrGivenUpSongs()
    {
        var w = Fixture().GetProperty("wholePool");
        foreach (var c in w.GetProperty("analysisOrder").EnumerateArray())
        {
            var pool = RadioPool(c.GetProperty("size").GetInt32());
            var known = c.GetProperty("known").EnumerateArray().Select(x => x.GetString()!).ToHashSet();
            var failed = c.GetProperty("failed").EnumerateArray().Select(x => x.GetString()!).ToHashSet();
            var next = c.GetProperty("next").EnumerateArray().Select(x => pool.First(t => t.Id == x.GetString())).ToList();
            var expected = c.GetProperty("expected").EnumerateArray().Select(x => x.GetString()!).ToList();
            var got = FlowMath.AnalysisOrder(pool, (id) => known.Contains(id) ? new FlowFeatures() : null, next, failed).Select(t => t.Id).ToList();
            Assert.Equal(expected, got);
        }
    }

    [Fact]
    public void AFailedSongIsRetriedOnceThenGivenUpOn()
    {
        var w = Fixture().GetProperty("wholePool");
        var tries = new Dictionary<string, int>();
        foreach (var c in w.GetProperty("afterFailure").EnumerateArray())
            Assert.Equal(c.GetProperty("expected").GetString(), FlowMath.AfterFailure(tries, c.GetProperty("id").GetString()!));
    }

    [Fact]
    public void AnUnmeasuredSongNeverWinsOnYouTubePositionAlone()
    {
        var w = Fixture().GetProperty("wholePool");
        var h = w.GetProperty("holdUnmeasured");
        var pool = RadioPool(50);
        // only the LAST ten songs are measured, and YouTube lists them as the least relevant
        var feats = Feats(w.GetProperty("featHold"));
        FlowFeatures? Feat(string id) => feats.TryGetValue(id, out var f) ? f : null;
        var seed = new FlowTrack("seed", "Seed", 200);

        var held = FlowMath.PlanFlow(seed, pool, Feat, new FlowOptions { HoldUnmeasured = true });
        var firstTen = h.GetProperty("expectedFirstTen").EnumerateArray().Select(x => x.GetString()!).ToList();
        Assert.Equal(firstTen, held.Take(10).Select(x => x.TrackId).ToList());
        Assert.All(held.Take(10), x => Assert.True(feats.ContainsKey(x.TrackId), "the ten measured songs come before any unmeasured one"));

        var pick = FlowMath.PickNext(seed, pool, Feat)!;
        var ep = h.GetProperty("pick");
        Assert.Equal(ep.GetProperty("track").GetString(), pick.TrackId);
        Assert.Equal(ep.GetProperty("known").GetInt32(), pick.Known);
        Assert.Equal(ep.GetProperty("total").GetInt32(), pick.Total);
        Assert.Equal(ep.GetProperty("partial").GetBoolean(), pick.Partial);
    }

    [Fact]
    public void FiftySongRadioIsMeasuredOneByOneAndTheLastPickSeesTheWholePool()
    {
        var w = Fixture().GetProperty("wholePool");
        var s = w.GetProperty("scenario");
        var pool = RadioPool(50);
        var seed = new FlowTrack("seed", "Seed", 200);
        var allFeats = Feats(w.GetProperty("featScenario"));
        var feats = new Dictionary<string, FlowFeatures> { ["seed"] = allFeats["seed"] };
        FlowFeatures? Feat(string id) => feats.TryGetValue(id, out var f) ? f : null;
        var failed = new HashSet<string>();
        var queue = FlowMath.AnalysisOrder(pool, Feat, pool.Take(3).ToList(), failed);
        var known = new List<int>(); var partial = new List<bool>();
        while (queue.Count > 0)
        {
            var t = queue[0];
            queue.RemoveAt(0);
            feats[t.Id] = allFeats[t.Id];
            var pick = FlowMath.PickNext(seed, pool, Feat, new FlowOptions { Failed = failed })!;
            Assert.True(feats.ContainsKey(pick.TrackId), "pick is a measured song");
            known.Add(pick.Known); partial.Add(pick.Partial);
            queue = FlowMath.AnalysisOrder(pool, Feat, null, failed);
        }
        Assert.Equal(s.GetProperty("expectedMeasured").GetInt32(), known.Count);
        Assert.Equal(s.GetProperty("expectedKnown").EnumerateArray().Select(x => x.GetInt32()).ToList(), known);
        Assert.Equal(s.GetProperty("expectedPartial").EnumerateArray().Select(x => x.GetBoolean()).ToList(), partial);
        Assert.True(FlowMath.FlowCoverageOf(pool, Feat, failed).Done);
    }

    private static List<FlowTrack> RadioPool(int n) =>
        Enumerable.Range(0, n).Select(i => new FlowTrack("s" + i, "Song " + i, 200, null, "Artist " + i)).ToList();

    /// <summary>The fixture's per-id feature map (the same numbers the JS reference used).</summary>
    private static Dictionary<string, FlowFeatures> Feats(JsonElement e) =>
        e.EnumerateObject().ToDictionary(p => p.Name, p => Feature(p.Value));

    private static FlowTrack Track(JsonElement e) => new(
        e.GetProperty("id").GetString()!,
        e.TryGetProperty("title", out var t) ? t.GetString()! : "",
        e.TryGetProperty("duration", out var d) ? d.GetDouble() : 0,
        e.TryGetProperty("album", out var al) && al.ValueKind == JsonValueKind.Object ? al.GetProperty("name").GetString() : null,
        e.TryGetProperty("artists", out var ar) && ar.GetArrayLength() > 0 ? ar[0].GetProperty("name").GetString() : null);

    private static FlowFeatures Feature(JsonElement e)
    {
        double? D(string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
        return new FlowFeatures { Bpm = D("bpm"), BpmConf = D("bpmConf") ?? 0, Camelot = e.TryGetProperty("camelot", out var c) ? c.GetString() : null, KeyConf = D("keyConf") ?? 0, Energy = D("energy") };
    }
}
