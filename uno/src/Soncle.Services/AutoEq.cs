// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Soncle.Services;

/// <summary>One AutoEq entry (name, path, measurement source, rig, type).</summary>
public sealed record AutoEqEntry(string Name, string Path, string Source, string Rig, string Type);

/// <summary>Parsed parametric EQ: preamp and filters.</summary>
public sealed record AutoEqProfile(string? Name, string? Path, double Preamp, IReadOnlyList<AutoEqFilter> Filters);

public sealed record AutoEqFilter(string Type, double Fc, double Gain, double Q);

/// <summary>Pluggable storage, as in the JS (<c>{read, write}</c>).</summary>
public interface IAutoEqStore
{
    Task<string> ReadAsync(string name);
    Task WriteAsync(string name, string text);
}

/// <summary>
/// Headphone correction from the AutoEq project (MIT). A port of <c>src/autoeq.mjs</c>: the index
/// is fetched once and cached for 30 days; a chosen headphone's profile is cached by the first 20
/// hex characters of the SHA-1 of its path, so existing caches keep working.
/// </summary>
public static partial class AutoEq
{
    private const string Raw = "https://raw.githubusercontent.com/jaakkopasanen/AutoEq/master/results/";
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(30);

    private static readonly Dictionary<string, int> Rank = new()
    {
        ["oratory1990"] = 0, ["crinacle"] = 1, ["Rtings"] = 2, ["Innerfidelity"] = 3, ["Super Review"] = 4, ["Headphone_com"] = 5,
    };

    private static IAutoEqStore? _store;
    private static Func<string, Task<(bool Ok, int Status, string Text)>>? _fetch;
    private static Action<string> _log = _ => { };
    private static List<AutoEqEntry>? _index;
    private static Task<List<AutoEqEntry>>? _loading;

    public static void Init(IAutoEqStore store, Func<string, Task<(bool Ok, int Status, string Text)>> fetch, Action<string>? log = null)
    {
        _store = store;
        _fetch = fetch;
        if (log is not null) _log = log;
    }

    /// <summary>For tests: reset the cached index.</summary>
    public static void Reset()
    {
        _index = null;
        _loading = null;
    }

    private static string Sha1Hex(string text) =>
        Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    public static string Norm(string? s) =>
        Regex.Replace((s ?? "").ToLowerInvariant().Normalize(NormalizationForm.FormKD), "[^a-z0-9]+", " ").Trim();

    [GeneratedRegex(@"^- \[(.+?)\]\(\.\/(.+)\) by (.+?)(?: on (.+))?$")]
    private static partial Regex IndexRx();

    /// <summary>Parse AutoEq's <c>results/INDEX.md</c>.</summary>
    public static List<AutoEqEntry> ParseIndex(string md)
    {
        var outv = new List<AutoEqEntry>();
        foreach (var rawLine in md.Split('\n'))
        {
            var m = IndexRx().Match(rawLine.Trim());
            if (!m.Success) continue;
            string p;
            try { p = Uri.UnescapeDataString(m.Groups[2].Value); } catch { continue; }
            var type = Regex.IsMatch(p, "in-ear", RegexOptions.IgnoreCase) ? "in-ear"
                : Regex.IsMatch(p, "earbud", RegexOptions.IgnoreCase) ? "earbud" : "over-ear";
            outv.Add(new AutoEqEntry(m.Groups[1].Value, p, m.Groups[3].Value, m.Groups[4].Value, type));
        }
        return outv;
    }

    [GeneratedRegex(@"Filter\s+\d+:\s*ON\s+(PK|LSC|HSC|LS|HS)\s+Fc\s+([\d.]+)\s*Hz\s+Gain\s+(-?[\d.]+)\s*dB(?:\s+Q\s+([\d.]+))?", RegexOptions.IgnoreCase)]
    private static partial Regex FilterRx();
    [GeneratedRegex(@"Preamp:\s*(-?[\d.]+)\s*dB", RegexOptions.IgnoreCase)]
    private static partial Regex PreampRx();

    /// <summary>Parse "&lt;name&gt; ParametricEQ.txt".</summary>
    public static AutoEqProfile ParseProfile(string txt)
    {
        var pre = PreampRx().Match(txt);
        var filters = new List<AutoEqFilter>();
        foreach (Match m in FilterRx().Matches(txt))
        {
            var t = m.Groups[1].Value.ToUpperInvariant();
            if (t == "LS") t = "LSC";
            else if (t == "HS") t = "HSC";
            var q = m.Groups[4].Success ? double.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture) : 0.707;
            filters.Add(new AutoEqFilter(t, double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture), q));
        }
        if (filters.Count == 0) throw new InvalidOperationException("no filters in profile");
        var preamp = pre.Success ? double.Parse(pre.Groups[1].Value, CultureInfo.InvariantCulture)
            : Math.Min(0, -filters.Max(f => f.Gain));
        return new AutoEqProfile(null, null, preamp, filters);
    }

    private static async Task<string> GetText(string url)
    {
        var r = await _fetch!(url);
        if (!r.Ok) throw new InvalidOperationException($"AutoEq: HTTP {r.Status}");
        return r.Text;
    }

    private static Task<List<AutoEqEntry>> LoadIndex()
    {
        if (_index is not null) return Task.FromResult(_index);
        if (_loading is not null) return _loading;
        _loading = LoadIndexCore().ContinueWith(t => { _loading = null; return t.Result; });
        return _loading;
    }

    private static async Task<List<AutoEqEntry>> LoadIndexCore()
    {
        List<AutoEqEntry>? cached = null;
        try
        {
            var json = await _store!.ReadAsync("index.json");
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var at = doc.RootElement.GetProperty("at").GetInt64();
            var items = ParseItems(doc.RootElement.GetProperty("items"));
            if (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - at < MaxAge.TotalMilliseconds)
                return _index = items;
            cached = items;
        }
        catch { /* first use */ }
        try
        {
            var items = ParseIndex(await GetText(Raw + "INDEX.md"));
            if (items.Count < 100) throw new InvalidOperationException("index looks incomplete");
            try { await _store!.WriteAsync("index.json", BuildCacheJson(items)); } catch { /* optional */ }
            _log($"autoeq: index of {items.Count} profiles");
            return _index = items;
        }
        catch when (cached is not null)
        {
            return _index = cached;   // offline: an old list is fine
        }
    }

    private static List<AutoEqEntry> ParseItems(System.Text.Json.JsonElement arr)
    {
        var list = new List<AutoEqEntry>();
        foreach (var e in arr.EnumerateArray())
            list.Add(new AutoEqEntry(e.GetProperty("n").GetString()!, e.GetProperty("p").GetString()!,
                e.GetProperty("s").GetString()!, e.GetProperty("r").GetString()!, e.GetProperty("t").GetString()!));
        return list;
    }

    private static string BuildCacheJson(List<AutoEqEntry> items)
    {
        var arr = items.Select(i => new { n = i.Name, p = i.Path, s = i.Source, r = i.Rig, t = i.Type }).ToArray();
        return System.Text.Json.JsonSerializer.Serialize(new { at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), items = arr });
    }

    private static double Score(AutoEqEntry item, string[] words)
    {
        var s = (Rank.TryGetValue(item.Source, out var r) ? r : 8) + Norm(item.Name).Length / 40.0;
        if (Norm(item.Name).StartsWith(words[0])) s -= 2;
        return s;
    }

    /// <summary>Headphones whose name contains every word of the query (best source first).</summary>
    public static async Task<IReadOnlyList<AutoEqEntry>> SearchAsync(string q, int limit = 40)
    {
        var items = await LoadIndex();
        var words = Norm(q).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return Array.Empty<AutoEqEntry>();
        var hits = items.Where(it => words.All(w => Norm(it.Name).Contains(w))).ToList();
        hits.Sort((a, b) =>
        {
            var c = Score(a, words).CompareTo(Score(b, words));
            return c != 0 ? c : string.CompareOrdinal(a.Name, b.Name);
        });
        return hits.Take(limit).ToList();
    }

    [GeneratedRegex(@"\b(stereo|hands free|handsfree|headset|headphones|audio|bluetooth|le|hd)\b")]
    private static partial Regex MatchCleanup();

    /// <summary>The best profile for an output device name, or null when not confident.</summary>
    public static async Task<AutoEqEntry?> MatchAsync(string model)
    {
        var k = MatchCleanup().Replace(Norm(model), " ");
        k = Regex.Replace(k, @"\s+", " ").Trim();
        if (k.Length < 4 || (!k.Any(char.IsDigit) && k.Split(' ').Length < 2)) return null;
        var items = await LoadIndex();
        var words = k.Split(' ');
        var hits = items.Where(it => words.All(w => Norm(it.Name).Contains(w)) && Norm(it.Name).Length <= k.Length + 18).ToList();
        if (hits.Count == 0) return null;
        hits.Sort((a, b) => Score(a, words).CompareTo(Score(b, words)));
        return hits[0];
    }

    /// <summary>Load (and cache) a profile by its AutoEq path.</summary>
    public static async Task<AutoEqProfile> ProfileAsync(string p)
    {
        if (string.IsNullOrEmpty(p) || p.Contains("..")) throw new InvalidOperationException("bad profile path");
        var file = Sha1Hex(p).Substring(0, 20) + ".json";
        try
        {
            var json = await _store!.ReadAsync(file);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            var filters = new List<AutoEqFilter>();
            foreach (var f in root.GetProperty("filters").EnumerateArray())
                filters.Add(new AutoEqFilter(f.GetProperty("type").GetString()!, f.GetProperty("fc").GetDouble(),
                    f.GetProperty("gain").GetDouble(), f.GetProperty("q").GetDouble()));
            return new AutoEqProfile(root.GetProperty("name").GetString(), root.GetProperty("path").GetString(),
                root.GetProperty("preamp").GetDouble(), filters);
        }
        catch { /* not cached yet */ }

        var name = p.Split('/').Last();
        var url = Raw + string.Join('/', p.Split('/').Select(Uri.EscapeDataString)) + "/" + Uri.EscapeDataString(name + " ParametricEQ.txt");
        var parsed = ParseProfile(await GetText(url));
        var prof = parsed with { Name = name, Path = p };
        try { await _store!.WriteAsync(file, SerializeProfile(prof)); } catch { /* optional */ }
        return prof;
    }

    private static string SerializeProfile(AutoEqProfile prof) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            name = prof.Name,
            path = prof.Path,
            preamp = prof.Preamp,
            filters = prof.Filters.Select(f => new { type = f.Type, fc = f.Fc, gain = f.Gain, q = f.Q }).ToArray(),
        });
}
