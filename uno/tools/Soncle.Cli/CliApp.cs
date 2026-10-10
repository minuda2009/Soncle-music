// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Diagnostics;
using System.Globalization;
using Soncle.Audio;
using Soncle.Audio.Dsp;
using Soncle.Flow;
using Soncle.Streams;
using Soncle.YouTube;
using Soncle.YouTube.Parsing;

namespace Soncle.Cli;

/// <summary>
/// The commands: search, resolve, download, play, analyze. Output goes to the writers it is given,
/// so tests read it back. It never prints stream URLs, tokens or cookies, and it has no sign-in.
/// </summary>
public sealed class CliApp
{
    public const int Ok = 0, Usage = 1, Failed = 2, Unsupported = 3;
    private const double YouTubeReference = -14;   // YouTube LUFS = -14 + loudnessDb

    private readonly ICliBackend _b;
    private readonly TextWriter _out, _err;

    public CliApp(ICliBackend backend, TextWriter output, TextWriter error)
    {
        _b = backend;
        _out = output;
        _err = error;
    }

    public const string HelpText =
        "soncle — command line for the Soncle libraries\n" +
        "  search <query>                 top songs, albums and artists\n" +
        "  resolve <videoId>              which client, format, bitrate, length and loudness\n" +
        "  download <videoId> <file>      whole song to a file, with progress\n" +
        "  analyze <videoId>              tempo, key and energy (Flow)\n" +
        "  play <videoId> [out.wav] [--target <LUFS>]\n" +
        "                                 decode and normalise; writes a WAV file off Windows\n";

    public async Task<int> RunAsync(string[] args, CancellationToken ct = default)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help") { _out.Write(HelpText); return args.Length == 0 ? Usage : Ok; }
        try
        {
            return args[0] switch
            {
                "search" when args.Length >= 2 => await SearchAsync(string.Join(' ', args.Skip(1)), ct),
                "resolve" when args.Length == 2 => await ResolveAsync(args[1], ct),
                "download" when args.Length == 3 => await DownloadAsync(args[1], args[2], ct),
                "analyze" when args.Length == 2 => await AnalyzeAsync(args[1], ct),
                "play" when args.Length >= 2 => await PlayAsync(args.Skip(1).ToArray(), ct),
                _ => Fail(Usage, "unknown or incomplete command. Run `soncle help`."),
            };
        }
        catch (NotSupportedException e)
        {
            return Fail(Unsupported, $"can't decode '{e.Message}' yet: the WebM/Opus decoder is not part of this build.");
        }
        catch (OperationCanceledException) { return Fail(Failed, "cancelled."); }
        catch (Exception e)
        {
            return Fail(Failed, e.Message);
        }
    }

    private int Fail(int code, string message)
    {
        _err.WriteLine("error: " + message);
        return code;
    }

    // ---------- search ----------
    private async Task<int> SearchAsync(string query, CancellationToken ct)
    {
        var page = await _b.SearchAsync(query, ct);
        _out.WriteLine($"search \"{query}\" ({_b.Name})");
        if (page.Correction is { Length: > 0 } fix) _out.WriteLine($"did you mean: {fix}");
        if (page.Top is { } top) _out.WriteLine("top: " + Line(top));
        var shown = 0;
        foreach (var shelf in page.Sections)
        {
            if (shelf.Items.Count == 0) continue;
            _out.WriteLine(string.IsNullOrEmpty(shelf.Title) ? "results:" : shelf.Title + ":");
            foreach (var it in shelf.Items.Take(8)) { _out.WriteLine("  " + Line(it)); shown++; }
            if (shown >= 24) break;
        }
        if (page.Top is null && shown == 0) _out.WriteLine("nothing found.");
        return Ok;
    }

    private static string Line(YtItem it)
    {
        var who = it.Artists is { Count: > 0 } a ? string.Join(", ", a.Select(x => x.Name)) : it.Subtitle ?? "";
        var title = it.Title ?? "(untitled)";
        var core = who.Length > 0 ? $"{title} — {who}" : title;
        return $"{it.Type,-8} {core}  [{it.Id}]";
    }

    // ---------- resolve ----------
    private async Task<int> ResolveAsync(string id, CancellationToken ct)
    {
        var s = await _b.Resolver.ResolveAsync(id, ct: ct);
        _out.WriteLine($"{id} ({_b.Name})");
        _out.WriteLine($"  client:   {s.Client}");
        _out.WriteLine($"  format:   {s.Mime}");
        _out.WriteLine($"  bitrate:  {s.Bitrate / 1000} kbps");
        _out.WriteLine($"  length:   {s.Length:N0} bytes");
        _out.WriteLine(LufsOf(s) is { } l
            ? $"  loudness: {l.ToString("0.0", CultureInfo.InvariantCulture)} LUFS"
            : "  loudness: unknown (measured while playing)");
        return Ok;
    }

    private static double? LufsOf(ResolvedStream s) =>
        double.TryParse(s.LoudnessDb, NumberStyles.Float, CultureInfo.InvariantCulture, out var db) && double.IsFinite(db)
            ? YouTubeReference + db : null;

    // ---------- download ----------
    private async Task<int> DownloadAsync(string id, string file, CancellationToken ct)
    {
        var s = await _b.Resolver.ResolveAsync(id, ct: ct);
        var cache = NewCacheDir();
        try
        {
            await FetchAsync(id, s, cache, file, ct);
            _out.WriteLine($"saved {new FileInfo(file).Length:N0} bytes to {file}");
            return Ok;
        }
        finally { TryDeleteDir(cache); }
    }

    /// <summary>Downloads the whole song with <see cref="SongDownloader"/> and writes it to <paramref name="file"/>.</summary>
    private async Task FetchAsync(string id, ResolvedStream s, string cacheDir, string file, CancellationToken ct)
    {
        var dl = new SongDownloader(cacheDir, _b.Fetcher);
        var lastStep = -1;
        dl.ProgressChanged += (_, p) =>
        {
            var step = (int)(p.Fraction * 4);
            if (step == lastStep) return;
            lastStep = step;
            _out.WriteLine($"  downloading {(int)(p.Fraction * 100)}%");
        };
        dl.Register(id, new StreamSource(s.Url, s.Headers, s.Length, s.Mime));
        await using (var src = dl.OpenRead(id, 0))
        await using (var dst = File.Create(file))
            await src.CopyToAsync(dst, ct);
        if (new FileInfo(file).Length != s.Length)
            throw new IOException($"incomplete download: {new FileInfo(file).Length} of {s.Length} bytes");
    }

    // ---------- analyze ----------
    private async Task<int> AnalyzeAsync(string id, CancellationToken ct)
    {
        var (source, _, _) = await LoadSongAsync(id, ct);
        if (source is not MemoryPcmSource mem) return Fail(Failed, "this backend's decoder can't give PCM for analysis.");
        var r = Analyzer.Analyze(mem.ToMono(), mem.SampleRate);
        _out.WriteLine($"{id} ({_b.Name})");
        if (r is null) { _out.WriteLine("  not enough audio to analyse."); return Ok; }
        _out.WriteLine("  tempo:  " + (r.Bpm is { } bpm ? $"{bpm:0.0} BPM (confidence {r.BpmConf:0.00})" : "unknown"));
        _out.WriteLine("  key:    " + (r.Key is { } k ? $"{k} / {r.Camelot} (confidence {r.KeyConf:0.00})" : "unknown"));
        _out.WriteLine($"  energy: {r.Energy:0.00}  (RMS {r.RmsDb:0.0} dB)");
        return Ok;
    }

    private async Task<(IAudioSource Source, ResolvedStream Stream, string Cache)> LoadSongAsync(string id, CancellationToken ct)
    {
        var s = await _b.Resolver.ResolveAsync(id, ct: ct);
        if (!_b.Decoder.CanDecode(s.Mime)) throw new NotSupportedException(s.Mime);
        var cache = NewCacheDir();
        try
        {
            var file = Path.Combine(cache, "song.bin");
            await FetchAsync(id, s, cache, file, ct);
            await using var fs = File.OpenRead(file);
            return (_b.Decoder.Decode(fs, s.Mime), s, cache);
        }
        finally { TryDeleteDir(cache); }
    }

    // ---------- play ----------
    private async Task<int> PlayAsync(string[] args, CancellationToken ct)
    {
        var id = args[0];
        string? outFile = null;
        var target = -14.0;
        for (var i = 1; i < args.Length; i++)
        {
            if (args[i] == "--target" && i + 1 < args.Length)
            {
                if (!double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out target))
                    return Fail(Usage, "--target needs a number in LUFS, e.g. -14.");
            }
            else if (outFile is null && !args[i].StartsWith("--")) outFile = args[i];
            else return Fail(Usage, $"unexpected argument '{args[i]}'.");
        }
        outFile ??= id + ".wav";

        var cpu0 = Process.GetCurrentProcess().TotalProcessorTime;
        var clock = Stopwatch.StartNew();

        var (source, stream, _) = await LoadSongAsync(id, ct);
        if (source.SampleRate <= 0 || source.LengthFrames is not { } length || length <= 0)
            return Fail(Failed, "the decoder returned no audio.");
        var sr = source.SampleRate;
        var lufs = LufsOf(stream);

        var engine = new Engine(sr);
        engine.SetNormalize(true, target);
        engine.Load(source, lufs);

        // the song, plus a short tail so the limiter's look-ahead and the fade-in are flushed
        var total = checked((int)length + sr / 10);
        var left = new float[total];
        var right = new float[total];
        for (var done = 0; done < total;)
        {
            var n = Math.Min(engine.MaxBlockFrames, total - done);
            engine.Render(left.AsSpan(done, n), right.AsSpan(done, n), n);
            done += n;
        }

        WavWriter.Write(outFile, left, right, sr);
        var after = Loudness.Integrated(left, right, sr);
        clock.Stop();

        _out.WriteLine($"{id} ({_b.Name})");
        _out.WriteLine($"  source loudness: {(lufs is { } l ? l.ToString("0.0", CultureInfo.InvariantCulture) + " LUFS" : "unknown")}");
        _out.WriteLine($"  target:          {target.ToString("0.0", CultureInfo.InvariantCulture)} LUFS");
        _out.WriteLine($"  output loudness: {(after is { } a ? a.ToString("0.0", CultureInfo.InvariantCulture) + " LUFS" : "silent")}");
        _out.WriteLine($"  peak:            {Loudness.PeakDb(left, right).ToString("0.0", CultureInfo.InvariantCulture)} dBFS");
        _out.WriteLine($"  wrote:           {outFile} ({total / (double)sr:0.0} s)");
        using var p = Process.GetCurrentProcess();
        var cpu = p.TotalProcessorTime - cpu0;
        _out.WriteLine($"  cost:            {cpu.TotalSeconds:0.00} s CPU in {clock.Elapsed.TotalSeconds:0.00} s, peak memory {p.PeakWorkingSet64 / 1048576.0:0} MB");
        return Ok;
    }

    private static string NewCacheDir() =>
        Path.Combine(Path.GetTempPath(), "soncle-cli-" + Environment.ProcessId + "-" + Guid.NewGuid().ToString("N")[..8]);

    private static void TryDeleteDir(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { /* a leftover temp folder is harmless */ }
    }
}

/// <summary>Output-level helpers for the offline checks.</summary>
public static class Loudness
{
    /// <summary>Gated integrated loudness (BS.1770) of a stereo signal, or null if it is silent.</summary>
    public static double? Integrated(ReadOnlySpan<float> left, ReadOnlySpan<float> right, int sampleRate)
    {
        var kf = new KFilter(sampleRate);
        var integ = new Integrator(sampleRate);
        var n = Math.Min(left.Length, right.Length);
        for (var i = 0; i < n; i++) integ.Add(kf.Square(left[i], 0) + kf.Square(right[i], 1));
        return integ.Value();
    }

    public static double PeakDb(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        var peak = 0.0;
        foreach (var s in left) peak = Math.Max(peak, Math.Abs(s));
        foreach (var s in right) peak = Math.Max(peak, Math.Abs(s));
        return peak <= 0 ? -200 : 20 * Math.Log10(peak);
    }
}
