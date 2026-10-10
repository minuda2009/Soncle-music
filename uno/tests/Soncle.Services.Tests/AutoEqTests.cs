// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using Soncle.Services;

namespace Soncle.Services.Tests;

/// <summary>The JS <c>test/autoeq.test.mjs</c> cases, ported.</summary>
public class AutoEqTests : IDisposable
{
    private const string Index = """
        # Index
        - [Sennheiser HD 600](./oratory1990/over-ear/Sennheiser%20HD%20600) by oratory1990
        - [Sennheiser HD 600](./crinacle/GRAS%2043AG-7%20over-ear/Sennheiser%20HD%20600) by crinacle on GRAS 43AG-7
        - [Sony WH-1000XM4](./oratory1990/over-ear/Sony%20WH-1000XM4) by oratory1990
        - [Sony WF-1000XM4](./oratory1990/in-ear/Sony%20WF-1000XM4) by oratory1990
        - [Samsung Galaxy Buds2 Pro](./Rtings/HMS%20II.3%20in-ear/Samsung%20Galaxy%20Buds2%20Pro) by Rtings on HMS II.3
        """;
    private const string Profile = """
        Preamp: -6.3 dB
        Filter 1: ON LSC Fc 105 Hz Gain 6.5 dB Q 0.70
        Filter 2: ON PK Fc 125 Hz Gain -2.7 dB Q 0.55
        Filter 3: ON HSC Fc 10000 Hz Gain -3.1 dB Q 0.70
        """;

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "aeq-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _seen = new();
    private readonly string _index;

    public AutoEqTests()
    {
        var fillers = string.Join("\n", Enumerable.Range(0, 120).Select(i => $"- [Filler {i}](./x/over-ear/Filler%20{i}) by x"));
        _index = Index + "\n" + fillers;
        AutoEq.Reset();
        AutoEq.Init(new MemStore(), async (u) =>
        {
            _seen.Add(u);
            await Task.CompletedTask;
            return (true, 200, u.EndsWith("INDEX.md") ? _index : Profile);
        });
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public async Task IndexParsingSearchOrderAndMatching()
    {
        var items = AutoEq.ParseIndex(_index);
        Assert.Equal("crinacle/GRAS 43AG-7 over-ear/Sennheiser HD 600", items[1].Path);
        Assert.Equal("GRAS 43AG-7", items[1].Rig);

        var hd = await AutoEq.SearchAsync("hd600");
        Assert.Empty(hd);                                  // words must match
        var hd2 = await AutoEq.SearchAsync("hd 600");
        Assert.Equal("oratory1990", hd2[0].Source);        // preferred measurement first

        Assert.Equal("Sony WH-1000XM4", (await AutoEq.MatchAsync("Headphones (WH-1000XM4 Stereo)"))!.Name);
        Assert.Equal("Samsung Galaxy Buds2 Pro", (await AutoEq.MatchAsync("Galaxy Buds2 Pro"))!.Name);
        Assert.Null(await AutoEq.MatchAsync("Speakers (Realtek(R) Audio)"));
    }

    [Fact]
    public async Task ProfileParsingAndCaching()
    {
        var p = await AutoEq.ProfileAsync("oratory1990/over-ear/Sennheiser HD 600");
        Assert.Equal(-6.3, p.Preamp);
        Assert.Equal(new AutoEqFilter("LSC", 105, 6.5, 0.7), p.Filters[0]);
        Assert.EndsWith("/Sennheiser%20HD%20600/Sennheiser%20HD%20600%20ParametricEQ.txt", _seen[^1]);

        var n = _seen.Count;
        await AutoEq.ProfileAsync("oratory1990/over-ear/Sennheiser HD 600");
        Assert.Equal(n, _seen.Count);                      // second time from storage

        await Assert.ThrowsAsync<InvalidOperationException>(() => AutoEq.ProfileAsync("../../etc/passwd"));
    }

    private sealed class MemStore : IAutoEqStore
    {
        private readonly Dictionary<string, string> _files = new();
        public Task<string> ReadAsync(string name) =>
            _files.TryGetValue(name, out var v) ? Task.FromResult(v) : Task.FromException<string>(new FileNotFoundException());
        public Task WriteAsync(string name, string text) { _files[name] = text; return Task.CompletedTask; }
    }
}
