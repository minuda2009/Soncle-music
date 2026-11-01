// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Soncle.Core.Models;

namespace Soncle.Services;

/// <summary>A track from a folder on disk (the same shape as <c>src/local.mjs</c> builds).</summary>
public sealed class LocalTrack
{
    public string Type { get; set; } = "song";
    public string Id { get; set; } = "";
    public bool Local { get; set; } = true;
    public string Title { get; set; } = "";
    public List<ArtistRef> Artists { get; set; } = new();
    public AlbumRef? Album { get; set; }
    public string AlbumArtist { get; set; } = "";
    public int TrackNo { get; set; }
    public int DiscNo { get; set; }
    public int Year { get; set; }
    public string Genre { get; set; } = "";
    public int Duration { get; set; }
    public string Thumb { get; set; } = "";
    public double? LoudnessDb { get; set; }
    public string Codec { get; set; } = "";
    public long Added { get; set; }
    public string File { get; set; } = "";

    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>Embedded or sidecar lyrics next to a local file.</summary>
public sealed record LocalLyrics(string? Synced, string? Plain, string Source);

/// <summary>Artwork bytes read from a file or its folder.</summary>
public sealed record LocalArt(byte[] Data, string Mime);

/// <summary>Progress of a scan (<c>{ done, total }</c>, with <c>Finished</c> on the last report).</summary>
public sealed record ScanProgress(int Done, int Total, bool Finished = false);

/// <summary>Reads tags from one file (the seam over TagLibSharp, so tests can fake a scanner).</summary>
public interface IAudioTagReader
{
    LocalTrack Read(string path, DateTime BirthTime, DateTime ModifiedTime);
}

/// <summary>
/// Local music library: folders on disk, scanned for audio files, played through the app's own
/// file reader. A port of <c>src/local.mjs</c>: the same id scheme (<c>lc_</c> + the first 16 hex
/// characters of the SHA-1 of the resolved lowercase path), the same JSON shape for
/// <c>local-library.json</c>, the same sort order and the same "loose file" handling. Tag reading
/// is behind <see cref="IAudioTagReader"/> so the app uses TagLibSharp and tests use a fake.
/// </summary>
public sealed partial class LocalLibrary
{
    public static readonly IReadOnlySet<string> AudioExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".m4a", ".aac", ".flac", ".ogg", ".oga", ".opus", ".wav", ".wma", ".webm", ".alac", ".aiff", ".aif", ".ape", ".wv", ".mka",
    };

    public static readonly IReadOnlyDictionary<string, string> Mime = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".mp3"] = "audio/mpeg", [".m4a"] = "audio/mp4", [".aac"] = "audio/aac", [".alac"] = "audio/mp4", [".flac"] = "audio/flac",
        [".ogg"] = "audio/ogg", [".oga"] = "audio/ogg", [".opus"] = "audio/ogg", [".wav"] = "audio/wav", [".webm"] = "audio/webm",
        [".mka"] = "audio/x-matroska", [".wma"] = "audio/x-ms-wma", [".aiff"] = "audio/aiff", [".aif"] = "audio/aiff",
        [".ape"] = "audio/ape", [".wv"] = "audio/wavpack",
    };

    private static readonly string[] CoverNames = { "cover", "folder", "front", "album", "albumart", "albumartsmall" };

    private const int MaxDepth = 12;
    private const int ArtCacheMax = 48;

    private sealed class FileEntry
    {
        public double Mtime { get; set; }
        public long Size { get; set; }
        public LocalTrack? Track { get; set; }
        public bool Loose { get; set; }
    }

    private sealed class Db
    {
        public List<string> Folders { get; set; } = new();
        public Dictionary<string, FileEntry> Files { get; set; } = new();
    }

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    private readonly IAudioTagReader _tags;
    private readonly Action<string> _log;
    private readonly Dictionary<string, LocalArt?> _artCache = new();
    private readonly object _gate = new();

    private string? _dbPath;
    private Db _db = new();
    private readonly Dictionary<string, string> _byId = new();   // id -> absolute path
    private Task<List<LocalTrack>>? _scanning;

    public LocalLibrary(IAudioTagReader tags, Action<string>? log = null)
    {
        _tags = tags;
        _log = log ?? (_ => { });
    }

    // ---------- ids and extensions (pure, used by the UI and the stream layer) ----------

    public static bool IsLocalId(string? id) => id is not null && LocalIdRx().IsMatch(id);

    public static string IdFor(string file)
    {
        var resolved = Path.GetFullPath(file).ToLowerInvariant();
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(resolved));
        return "lc_" + Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    public static bool IsAudioFile(string f) => AudioExt.Contains(Path.GetExtension(f));

    [GeneratedRegex(@"^lc_[0-9a-f]{16}$")]
    private static partial Regex LocalIdRx();

    // ---------- storage ----------

    /// <summary>Loads <c>local-library.json</c> from the app's data folder (as in the JS).</summary>
    public void Init(string dataDir, Action<string>? logger = null)
    {
        _dbPath = Path.Combine(dataDir, "local-library.json");
        lock (_gate)
        {
            _db = new Db();
            try
            {
                if (File.Exists(_dbPath))
                {
                    var parsed = JsonSerializer.Deserialize<Db>(File.ReadAllText(_dbPath), JsonOpts);
                    if (parsed is not null) _db = parsed;
                }
            }
            catch { /* a corrupt file starts empty, as the JS does */ }
            _byId.Clear();
            foreach (var (p, e) in _db.Files)
                if (e.Track is not null) _byId[e.Track.Id] = p;
        }
    }

    private void Save()
    {
        if (_dbPath is null) return;
        try
        {
            var tmp = _dbPath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_db, JsonOpts));
            File.Move(tmp, _dbPath, overwrite: true);
        }
        catch (Exception e) { _log("save failed " + e.Message); }
    }

    /// <summary>Writes the library file now (the JS's <c>flush</c>; there is no debounce here).</summary>
    public void Flush() { lock (_gate) Save(); }

    public List<string> Folders() { lock (_gate) return new List<string>(_db.Folders); }

    public List<string> AddFolder(string dir)
    {
        var resolved = Path.GetFullPath(dir);
        lock (_gate)
        {
            if (!_db.Folders.Any(f => string.Equals(f, resolved, StringComparison.OrdinalIgnoreCase))) _db.Folders.Add(resolved);
            Save();
            return new List<string>(_db.Folders);
        }
    }

    public List<string> RemoveFolder(string dir)
    {
        var low = Path.GetFullPath(dir).ToLowerInvariant();
        lock (_gate)
        {
            _db.Folders = _db.Folders.Where(f => f.ToLowerInvariant() != low).ToList();
            var prefix = low.EndsWith(Path.DirectorySeparatorChar) ? low : low + Path.DirectorySeparatorChar;
            foreach (var p in _db.Files.Keys.ToList())
                if (p.ToLowerInvariant().StartsWith(prefix, StringComparison.Ordinal) && !_db.Files[p].Loose)
                {
                    if (_db.Files[p].Track is { } t) _byId.Remove(t.Id);
                    _db.Files.Remove(p);
                }
            Save();
            return new List<string>(_db.Folders);
        }
    }

    public string? PathFor(string id)
    {
        lock (_gate)
        {
            if (_byId.TryGetValue(id, out var p) && File.Exists(p)) return p;
            return null;
        }
    }

    public LocalTrack? TrackFor(string id)
    {
        lock (_gate)
        {
            if (_byId.TryGetValue(id, out var p) && _db.Files.TryGetValue(p, out var e)) return e.Track;
            return null;
        }
    }

    // ---------- scanning ----------

    /// <summary>Scans every folder. Concurrent calls share one scan (the JS's <c>scanning</c> promise).</summary>
    public Task<List<LocalTrack>> Scan(Action<ScanProgress>? onProgress = null)
    {
        lock (_gate)
        {
            // A finished scan must not block a new one (the JS's `scanning` promise is cleared in a
            // finally, which runs before the next call can see it; a continuation can lag behind).
            if (_scanning is { IsCompleted: false } running) return running;
            var task = Task.Run(() => ScanAsync(onProgress));
            _scanning = task;
            _ = task.ContinueWith(_ =>
            {
                lock (_gate) { if (ReferenceEquals(_scanning, task)) _scanning = null; }
            }, TaskScheduler.Default);
            return task;
        }
    }

    private async Task<List<LocalTrack>> ScanAsync(Action<ScanProgress>? onProgress)
    {
        var folders = Folders();
        var found = new List<string>();
        foreach (var f in folders) Walk(f, found, 0);
        var seen = new HashSet<string>(found, StringComparer.Ordinal);

        lock (_gate)
        {
            foreach (var p in _db.Files.Keys.ToList())
                if (!seen.Contains(p) && !(_db.Files[p].Loose && File.Exists(p)))
                {
                    if (_db.Files[p].Track is { } t) _byId.Remove(t.Id);
                    _db.Files.Remove(p);
                }
        }

        var done = 0;
        var lastEmit = 0L;
        foreach (var f in found)
        {
            await IndexFileAsync(f, loose: false).ConfigureAwait(false);
            done++;
            var now = Environment.TickCount64;
            if (now - lastEmit > 250) { lastEmit = now; onProgress?.Invoke(new ScanProgress(done, found.Count)); }
        }
        onProgress?.Invoke(new ScanProgress(done, found.Count, Finished: true));
        lock (_gate) Save();
        return Tracks();
    }

    public List<LocalTrack> Tracks()
    {
        List<LocalTrack> list;
        lock (_gate) list = _db.Files.Values.Select(e => e.Track).Where(t => t is not null).Select(t => t!).ToList();
        return list
            .OrderBy(t => string.IsNullOrEmpty(t.AlbumArtist) ? (t.Artists.FirstOrDefault()?.Name ?? "") : t.AlbumArtist, StringComparer.InvariantCultureIgnoreCase)
            .ThenBy(t => t.Album?.Name ?? "", StringComparer.InvariantCultureIgnoreCase)
            .ThenBy(t => t.DiscNo)
            .ThenBy(t => t.TrackNo)
            .ThenBy(t => t.Title, StringComparer.InvariantCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Indexes files or folders the user opened directly ("loose" files, kept until removed).</summary>
    public async Task<List<LocalTrack>> OpenFilesAsync(IEnumerable<string> paths)
    {
        var outList = new List<LocalTrack>();
        foreach (var p in paths)
        {
            if (Directory.Exists(p))
            {
                var found = new List<string>();
                Walk(p, found, 0);
                foreach (var f in found.OrderBy(x => x, StringComparer.Ordinal))
                    if (await IndexFileAsync(f, loose: true).ConfigureAwait(false) is { } t) outList.Add(t);
            }
            else if (File.Exists(p) && IsAudioFile(p))
            {
                if (await IndexFileAsync(Path.GetFullPath(p), loose: true).ConfigureAwait(false) is { } t) outList.Add(t);
            }
        }
        lock (_gate) Save();
        return outList;
    }

    private async Task<LocalTrack?> IndexFileAsync(string file, bool loose)
    {
        FileInfo st;
        try { st = new FileInfo(file); if (!st.Exists) return null; } catch { return null; }

        FileEntry? prev;
        lock (_gate) _db.Files.TryGetValue(file, out prev);
        var mtime = st.LastWriteTimeUtc.Subtract(DateTime.UnixEpoch).TotalMilliseconds;
        if (prev?.Track is not null && Math.Abs(prev.Mtime - mtime) < 0.5 && prev.Size == st.Length)
        {
            if (loose) { lock (_gate) { prev.Loose = true; Save(); } }
            return prev.Track;
        }

        LocalTrack track;
        try
        {
            track = await Task.Run(() => _tags.Read(file, st.CreationTimeUtc, st.LastWriteTimeUtc)).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _log($"metadata failed {Path.GetFileName(file)}: {e.Message}");
            track = FallbackTrack(file, st);
        }

        lock (_gate)
        {
            _db.Files[file] = new FileEntry { Mtime = mtime, Size = st.Length, Track = track, Loose = loose || (prev?.Loose ?? false) };
            _byId[track.Id] = file;
        }
        return track;
    }

    /// <summary>A track built from the file name alone (what the JS does when tags can't be read).</summary>
    private static LocalTrack FallbackTrack(string file, FileInfo st)
    {
        var id = IdFor(file);
        var baseName = CleanName(file);
        var artists = new List<ArtistRef>();
        var title = baseName;
        var m = TitleArtistRx().Match(baseName);
        if (m.Success) { artists = SplitArtists(m.Groups[1].Value).Select(n => new ArtistRef { Name = n }).ToList(); title = m.Groups[2].Value; }
        return new LocalTrack
        {
            Id = id,
            Title = title,
            Artists = artists,
            Duration = 0,
            Thumb = $"mstream://{id}/art?v={Math.Round(st.LastWriteTimeUtc.Subtract(DateTime.UnixEpoch).TotalMilliseconds)}",
            Added = (long)st.CreationTimeUtc.Subtract(DateTime.UnixEpoch).TotalMilliseconds,
            File = file,
        };
    }

    private static void Walk(string dir, List<string> outList, int depth)
    {
        if (depth > MaxDepth) return;
        string[] ents;
        try { ents = Directory.GetFileSystemEntries(dir); } catch { return; }
        foreach (var p in ents)
        {
            var name = Path.GetFileName(p);
            if (name.StartsWith('.') || name == "$RECYCLE.BIN" || name == "System Volume Information") continue;
            try
            {
                if (Directory.Exists(p)) Walk(p, outList, depth + 1);
                else if (File.Exists(p) && IsAudioFile(name)) outList.Add(p);
            }
            catch { /* a file that vanished mid-scan is skipped, as in the JS */ }
        }
    }

    // ---------- metadata helpers (ported from local.mjs) ----------

    public static List<string> SplitArtists(string? s) =>
        ArtistSplitRx().Split(s ?? "").Select(x => x.Trim()).Where(x => x.Length > 0).ToList();

    [GeneratedRegex(@"\s*(?:;|/|\u0000| feat\.? | ft\.? | & |, )\s*", RegexOptions.IgnoreCase)]
    private static partial Regex ArtistSplitRx();

    [GeneratedRegex(@"^(.+?)\s+-\s+(.+)$")]
    private static partial Regex TitleArtistRx();

    private static string CleanName(string file) =>
        NumPrefixRx().Replace(Path.GetFileNameWithoutExtension(file), "").Replace('_', ' ').Trim();

    [GeneratedRegex(@"^\d{1,3}[\s.\-_]+")]
    private static partial Regex NumPrefixRx();

    /// <summary>The codec line the JS builds: "FLAC • 900 kbps • lossless".</summary>
    public static string CodecLine(string? codec, int? bitrate, bool lossless)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(codec)) parts.Add(codec!);
        if (bitrate is > 0) parts.Add(Math.Round(bitrate.Value / 1000.0) + " kbps");
        if (lossless) parts.Add("lossless");
        return string.Join(" • ", parts);
    }

    // ---------- artwork and lyrics ----------

    /// <summary>Cover art embedded in the file, else a folder image (cover/folder/front…).</summary>
    public async Task<LocalArt?> ArtworkAsync(string id)
    {
        lock (_gate) { if (_artCache.TryGetValue(id, out var cached)) return cached; }
        var file = PathFor(id);
        LocalArt? art = null;
        if (file is not null) art = await Task.Run(() => ReadArt(file)).ConfigureAwait(false);
        lock (_gate)
        {
            while (_artCache.Count >= ArtCacheMax) _artCache.Remove(_artCache.Keys.First());
            _artCache[id] = art;
        }
        return art;
    }

    private LocalArt? ReadArt(string file)
    {
        try
        {
            var embedded = _tags is IArtworkTagReader a ? a.ReadArt(file) : null;
            if (embedded is not null) return embedded;
        }
        catch { /* fall through to a folder image */ }
        try
        {
            var dir = Path.GetDirectoryName(file);
            if (dir is null) return null;
            var imgs = Directory.GetFiles(dir).Where(n => ImgRx().IsMatch(n)).ToList();
            var pick = imgs.FirstOrDefault(n => CoverNames.Contains(Path.GetFileNameWithoutExtension(n).ToLowerInvariant()))
                       ?? (imgs.Count == 1 ? imgs[0] : null);
            if (pick is null) return null;
            var ext = Path.GetExtension(pick).ToLowerInvariant();
            var mime = ext == ".png" ? "image/png" : ext == ".webp" ? "image/webp" : "image/jpeg";
            return new LocalArt(File.ReadAllBytes(pick), mime);
        }
        catch { return null; }
    }

    [GeneratedRegex(@"\.(jpe?g|png|webp)$", RegexOptions.IgnoreCase)]
    private static partial Regex ImgRx();

    /// <summary>A sidecar <c>.lrc</c>/<c>.txt</c> next to the file, else embedded lyrics.</summary>
    public async Task<LocalLyrics?> LyricsAsync(string id)
    {
        var file = PathFor(id);
        if (file is null) return null;
        var basePath = file[..^Path.GetExtension(file).Length];
        foreach (var ext in new[] { ".lrc", ".LRC", ".txt" })
        {
            try
            {
                if (!File.Exists(basePath + ext)) continue;
                var txt = await File.ReadAllTextAsync(basePath + ext).ConfigureAwait(false);
                if (txt.Trim().Length == 0) continue;
                return LrcRx().IsMatch(txt)
                    ? new LocalLyrics(txt, null, "Local .lrc file")
                    : new LocalLyrics(null, txt, "Local lyrics file");
            }
            catch { /* try the next extension */ }
        }
        try
        {
            var embedded = _tags is ILyricsTagReader l ? await Task.Run(() => l.ReadLyrics(file)).ConfigureAwait(false) : null;
            return embedded;
        }
        catch { return null; }
    }

    [GeneratedRegex(@"\[\d{1,2}:\d{2}")]
    private static partial Regex LrcRx();

    /// <summary>A deterministic gradient placeholder, so a coverless track still has artwork.</summary>
    public static string PlaceholderArt(string id)
    {
        uint hsh = 0;
        foreach (var ch in id) hsh = (hsh * 31 + ch) >>> 0;
        var hue = hsh % 360;
        var hue2 = (hue + 40) % 360;
        return "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"300\" height=\"300\" viewBox=\"0 0 300 300\"><defs>" +
               $"<linearGradient id=\"g\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"1\"><stop offset=\"0\" stop-color=\"hsl({hue},45%,42%)\"/>" +
               $"<stop offset=\"1\" stop-color=\"hsl({hue2},50%,22%)\"/></linearGradient></defs><rect width=\"300\" height=\"300\" fill=\"url(#g)\"/>" +
               "<path fill=\"rgba(255,255,255,.8)\" d=\"M175 80v96a30 30 0 1 1-16-26.5V104l-56 14v76a30 30 0 1 1-16-26.5V96z\" transform=\"translate(20 10)\"/></svg>";
    }
}

/// <summary>Optional: a reader that can also pull embedded artwork (TagLibSharp in the app).</summary>
public interface IArtworkTagReader
{
    LocalArt? ReadArt(string path);
}

/// <summary>Optional: a reader that can also pull embedded lyrics (TagLibSharp in the app).</summary>
public interface ILyricsTagReader
{
    LocalLyrics? ReadLyrics(string path);
}
