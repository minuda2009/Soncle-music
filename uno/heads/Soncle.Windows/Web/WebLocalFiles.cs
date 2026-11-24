// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Diagnostics;
using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Core;
using Soncle.Core.Models;
using Soncle.Services;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Soncle.Windows.Web;

/// <summary>
/// Local music for the web UI, on the C# port of the desktop app's local library
/// (<see cref="LocalLibrary"/> + TagLib): the eight <c>local*</c> calls of window.api, and the files
/// themselves at <c>/_soncle/local/&lt;id&gt;</c> (with ranges, so seeking works) and their artwork at
/// <c>/_soncle/local/&lt;id&gt;/art</c> — what the desktop app's <c>mstream://</c> did.
/// </summary>
internal sealed class WebLocalFiles
{
    private const string Prefix = "/_soncle/local/";
    private const long MaxChunk = 8L << 20;   // a range answer is capped at 8 MB; the player asks again

    private readonly LocalLibrary _lib;
    private readonly Window _window;
    private readonly string _origin;

    public WebLocalFiles(string dataDir, Window window, string origin)
    {
        _window = window;
        _origin = origin;
        // first start: keep the desktop app's folders and scan cache (copy, never move)
        var mine = Path.Combine(dataDir, "local-library.json");
        var desktop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Soncle", "local-library.json");
        try
        {
            if (!File.Exists(mine) && File.Exists(desktop)) File.Copy(desktop, mine);
        }
        catch (Exception e)
        {
            WebShell.Log($"local-carry-over-failed {e.GetType().Name}");
        }
        _lib = new LocalLibrary(new TagLibTagReader(), m => WebShell.Log("[local] " + m));
        _lib.Init(dataDir, m => WebShell.Log("[local] " + m));
    }

    public static bool Handles(string path) => path.StartsWith(Prefix, StringComparison.Ordinal);

    // ---- calls --------------------------------------------------------------------------------

    public JsonNode Folders() => ToJson(_lib.Folders());

    public async Task<JsonNode> AddFolderAsync()
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.MusicLibrary };
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(_window));
        var folder = await picker.PickSingleFolderAsync();
        return ToJson(folder is null ? _lib.Folders() : _lib.AddFolder(folder.Path));
    }

    public JsonNode RemoveFolder(string dir) => ToJson(_lib.RemoveFolder(dir));

    public async Task<JsonNode> ScanAsync(Action<ScanProgress> progress)
    {
        var tracks = await _lib.Scan(progress);
        _lib.Flush();
        return new JsonObject { ["count"] = tracks.Count };
    }

    public JsonNode Tracks() => WebJson(_lib.Tracks());

    /// <summary>The "open files" button: a file picker, then the same as opening them in Explorer.</summary>
    public async Task<JsonNode> OpenAsync(IEnumerable<string>? paths)
    {
        var list = paths?.ToList() ?? new List<string>();
        if (list.Count == 0)
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.MusicLibrary };
            foreach (var ext in LocalLibrary.AudioExt) picker.FileTypeFilter.Add(ext);
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(_window));
            var files = await picker.PickMultipleFilesAsync();
            list = files.Select(f => f.Path).ToList();
        }
        if (list.Count == 0) return new JsonArray();
        var tracks = await _lib.OpenFilesAsync(list);
        _lib.Flush();
        return WebJson(tracks);
    }

    /// <summary>Audio files Soncle was opened with (Explorer's "Open with").</summary>
    public async Task<JsonNode> PendingAsync()
    {
        var args = Environment.GetCommandLineArgs().Skip(1)
            .Where(a => !a.StartsWith('-') && LocalLibrary.IsAudioFile(a) && File.Exists(a)).ToList();
        if (args.Count == 0) return new JsonArray();
        return WebJson(await _lib.OpenFilesAsync(args));
    }

    public bool Reveal(string id)
    {
        var p = _lib.PathFor(id);
        if (p is null || !File.Exists(p)) return false;
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{p}\"") { UseShellExecute = false });
        return true;
    }

    /// <summary>
    /// Tracks as JSON for the page, with the desktop app's <c>mstream://id/art</c> artwork URLs
    /// turned into this app's own (on the copy only: the library file keeps its form).
    /// </summary>
    private JsonNode WebJson(IEnumerable<LocalTrack> tracks)
    {
        var arr = ToJson(tracks.ToList()) as JsonArray ?? new JsonArray();
        foreach (var node in arr)
        {
            if (node is not JsonObject t || t["thumb"]?.GetValue<string>() is not { } thumb || t["id"]?.GetValue<string>() is not { } id) continue;
            if (!thumb.StartsWith("mstream://", StringComparison.Ordinal)) continue;
            var q = thumb.IndexOf('?');
            t["thumb"] = $"{_origin}{Prefix}{id}/art{(q >= 0 ? thumb[q..] : "")}";
        }
        return arr;
    }

    private static JsonNode ToJson<T>(T value) => JsonNode.Parse(SoncleJson.Serialize(value)) ?? new JsonArray();

    // ---- files --------------------------------------------------------------------------------

    public async Task<CoreWebView2WebResourceResponse> ServeAsync(CoreWebView2Environment env, Uri uri, string? range)
    {
        var rest = uri.AbsolutePath[Prefix.Length..];
        var art = rest.EndsWith("/art", StringComparison.Ordinal);
        var id = art ? rest[..^4] : rest.TrimEnd('/');
        if (!LocalLibrary.IsLocalId(id)) return env.CreateWebResourceResponse(null, 404, "Not Found", "");

        if (art)
        {
            var a = await _lib.ArtworkAsync(id);
            if (a is not null)
                return env.CreateWebResourceResponse(new MemoryStream(a.Data, writable: false).AsRandomAccessStream(), 200, "OK", $"Content-Type: {a.Mime}\nCache-Control: max-age=86400");
            var svg = System.Text.Encoding.UTF8.GetBytes(LocalLibrary.PlaceholderArt(id));
            return env.CreateWebResourceResponse(new MemoryStream(svg, writable: false).AsRandomAccessStream(), 200, "OK", "Content-Type: image/svg+xml");
        }

        var file = _lib.PathFor(id);
        if (file is null || !File.Exists(file))
            return env.CreateWebResourceResponse(null, 404, "File not found - it may have been moved or deleted.", "");
        var mime = LocalLibrary.Mime.TryGetValue(Path.GetExtension(file), out var m) ? m : "audio/mpeg";
        var size = new FileInfo(file).Length;

        long start = 0, end = size - 1;
        var partial = false;
        if (range is { Length: > 0 } && range.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
        {
            var spec = range[6..].Split(',')[0].Split('-');
            if (long.TryParse(spec[0], out var s)) start = s;
            if (spec.Length > 1 && long.TryParse(spec[1], out var e)) end = e;
            partial = true;
        }
        if (start >= size) return env.CreateWebResourceResponse(null, 416, "Range Not Satisfiable", $"Content-Range: bytes */{size}");
        end = Math.Min(Math.Min(end, size - 1), start + MaxChunk - 1);
        if (!partial && end < size - 1) partial = true;   // a big file comes in pieces too

        var buf = new byte[end - start + 1];
        await using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, useAsync: true))
        {
            fs.Seek(start, SeekOrigin.Begin);
            var read = 0;
            while (read < buf.Length)
            {
                var n = await fs.ReadAsync(buf.AsMemory(read));
                if (n == 0) break;
                read += n;
            }
        }
        var headers = $"Content-Type: {mime}\nAccept-Ranges: bytes\nCache-Control: no-cache";
        if (partial) headers += $"\nContent-Range: bytes {start}-{end}/{size}";
        return env.CreateWebResourceResponse(new MemoryStream(buf, writable: false).AsRandomAccessStream(),
            partial ? 206 : 200, partial ? "Partial Content" : "OK", headers);
    }

    public void Flush() => _lib.Flush();
}
