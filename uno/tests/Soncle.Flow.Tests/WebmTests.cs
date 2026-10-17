// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Security.Cryptography;
using System.Text.Json;
using Soncle.Flow;
using Soncle.TestKit;

namespace Soncle.Flow.Tests;

/// <summary>
/// <c>WebmCues</c> against <c>fixtures/flow/webm.json</c>, which <c>tools/fixtures/export-webm.mjs</c>
/// made by running <c>renderer/flow/webm.js</c> on two real Opus WebM files.
/// </summary>
public class WebmTests
{
    private static readonly JsonElement Golden =
        JsonDocument.Parse(File.ReadAllText(Fixtures.Path("flow/webm.json"))).RootElement;

    private static byte[] Bytes(string name) => File.ReadAllBytes(Fixtures.Path("flow/" + name));

    private static double Num(JsonElement e) => e.ValueKind == JsonValueKind.String && e.GetString() == "Infinity"
        ? double.PositiveInfinity
        : e.GetDouble();

    private static void AssertHead(JsonElement expected, WebmHead? got)
    {
        if (expected.ValueKind == JsonValueKind.Null) { Assert.Null(got); return; }
        Assert.NotNull(got);
        Assert.Equal(expected.GetProperty("ebmlLength").GetInt32(), got!.Ebml.Length);
        Assert.Equal(expected.GetProperty("segDataStart").GetInt64(), got.SegDataStart);
        Assert.Equal(expected.GetProperty("partLengths").EnumerateArray().Select(x => x.GetInt32()), got.Parts.Select(p => p.Length));
        Assert.Equal(expected.GetProperty("firstCluster").GetInt64(), got.FirstCluster);
        Assert.Equal(expected.GetProperty("scale").GetDouble(), got.Scale);
        Assert.Equal(expected.GetProperty("duration").GetDouble(), got.Duration);
        var truncated = expected.GetProperty("truncated");
        Assert.Equal(truncated.ValueKind == JsonValueKind.Null ? null : truncated.GetInt64(), got.Truncated);
        var cues = expected.GetProperty("cues");
        if (cues.ValueKind == JsonValueKind.Null) { Assert.Null(got.Cues); return; }
        Assert.NotNull(got.Cues);
        var want = cues.EnumerateArray().ToList();
        Assert.Equal(want.Count, got.Cues!.Count);
        for (var i = 0; i < want.Count; i++)
        {
            Assert.Equal(want[i].GetProperty("t").GetDouble(), got.Cues[i].T);
            Assert.Equal(want[i].GetProperty("pos").GetInt64(), got.Cues[i].Pos);
        }
    }

    [Fact]
    public void BothFixturesCoverTheTwoLayouts()
    {
        // sample.webm has its Cues before the Clusters (like YouTube's DASH audio); the other has none in the head
        Assert.NotNull(WebmCues.ParseHead(Bytes("sample.webm"))!.Cues);
        Assert.Null(WebmCues.ParseHead(Bytes("sample-tailcues.webm"))!.Cues);
    }

    [Fact]
    public void ParseHeadMatchesTheJs()
    {
        foreach (var f in Golden.GetProperty("files").EnumerateObject())
        {
            var b = Bytes(f.Name);
            Assert.Equal(f.Value.GetProperty("length").GetInt32(), b.Length);
            AssertHead(f.Value.GetProperty("head"), WebmCues.ParseHead(b));
        }
    }

    [Fact]
    public void PickRangeMatchesTheJs()
    {
        foreach (var f in Golden.GetProperty("files").EnumerateObject())
        {
            var head = WebmCues.ParseHead(Bytes(f.Name))!;
            foreach (var c in f.Value.GetProperty("ranges").EnumerateArray())
            {
                var a = c.GetProperty("args");
                var total = Num(a.GetProperty("total"));
                var atFrac = a.TryGetProperty("atFrac", out var af) ? af.GetDouble() : 0.35;
                var secs = a.TryGetProperty("secs", out var sc) ? sc.GetDouble() : 30;
                var got = WebmCues.PickRange(head, total, atFrac, secs);
                var want = c.GetProperty("got");
                Assert.NotNull(got);
                Assert.Equal(want.GetProperty("from").GetInt64(), got!.Value.From);
                Assert.Equal(Num(want.GetProperty("to")), got.Value.To);
                Assert.Equal(want.GetProperty("t0").GetDouble(), got.Value.T0);
                Assert.Equal(want.GetProperty("cutAtCluster").GetBoolean(), got.Value.CutAtCluster);
            }
        }
    }

    [Fact]
    public void WholeClustersDropsACutOffCluster()
    {
        foreach (var f in Golden.GetProperty("files").EnumerateObject())
        {
            var b = Bytes(f.Name);
            foreach (var s in f.Value.GetProperty("slices").EnumerateArray())
            {
                var from = s.GetProperty("from").GetInt32();
                var to = s.GetProperty("to").GetInt32();
                var kept = WebmCues.WholeClusters(b.AsMemory(from, to - from));
                Assert.Equal(s.GetProperty("keptLength").GetInt32(), kept.Length);
            }
        }
    }

    [Fact]
    public void TheRebuiltFileIsByteIdenticalToTheJs()
    {
        foreach (var f in Golden.GetProperty("files").EnumerateObject())
        {
            var b = Bytes(f.Name);
            var head = WebmCues.ParseHead(b)!;
            var clusters = WebmCues.WholeClusters(b.AsMemory((int)head.FirstCluster));
            var rebuilt = WebmCues.BuildFile(head, clusters);
            var want = f.Value.GetProperty("rebuilt");
            Assert.Equal(want.GetProperty("length").GetInt32(), rebuilt.Length);
            Assert.Equal(want.GetProperty("sha256").GetString(), Convert.ToHexString(SHA256.HashData(rebuilt)).ToLowerInvariant());
        }
    }

    [Fact]
    public void TruncatedHeadsGiveTheSameAnswerAsTheJs()
    {
        foreach (var f in Golden.GetProperty("files").EnumerateObject())
        {
            var b = Bytes(f.Name);
            foreach (var c in f.Value.GetProperty("truncatedHeads").EnumerateArray())
                AssertHead(c.GetProperty("head"), WebmCues.ParseHead(b.AsMemory(0, c.GetProperty("n").GetInt32())));
        }
    }

    [Fact]
    public void NotWebmIsNull()
    {
        var garbage = Enumerable.Range(0, 64).Select(i => (byte)((i * 37) & 0xff)).ToArray();
        AssertHead(Golden.GetProperty("notWebm").GetProperty("garbage"), WebmCues.ParseHead(garbage));
        AssertHead(Golden.GetProperty("notWebm").GetProperty("empty"), WebmCues.ParseHead(ReadOnlyMemory<byte>.Empty));
    }

    [Fact]
    public void ARebuiltFileCanBeParsedAgain()
    {
        // buildFile sets the Segment size to "unknown"; the head of the result still parses
        var b = Bytes("sample.webm");
        var head = WebmCues.ParseHead(b)!;
        var rebuilt = WebmCues.BuildFile(head, WebmCues.WholeClusters(b.AsMemory((int)head.FirstCluster)));
        var again = WebmCues.ParseHead(rebuilt);
        Assert.NotNull(again);
        Assert.Null(again!.Cues);
        Assert.True(again.FirstCluster > 0);
        Assert.Equal(head.Scale, again.Scale);
        Assert.Equal(head.Duration, again.Duration);
    }
}
