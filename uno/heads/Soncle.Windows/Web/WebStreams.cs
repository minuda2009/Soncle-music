// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Collections.Concurrent;
using Microsoft.Web.WebView2.Core;

namespace Soncle.Windows.Web;

/// <summary>
/// Songs for the web UI's audio element, the Windows port of the Android app's SoncleStreams.java
/// (and the desktop app's <c>mstream:</c> proxy): the page registers a stream (<c>registerStream</c>:
/// key, googlevideo URL, headers, length, mime) and plays <c>https://host.soncle.local/_soncle/stream/&lt;key&gt;</c>
/// as a normal seekable file. The whole song downloads in the background in 1 MB pieces; a range
/// is answered as soon as its pieces are in. An expired URL (HTTP 403) asks the page for a fresh
/// one (<c>streamExpired</c>) and carries on. At most four songs are kept in memory.
/// </summary>
internal sealed class WebStreams
{
    private const string Prefix = "/_soncle/stream/";
    private const int Chunk = 1 << 20;
    private const int MaxSongs = 4;

    private sealed class Entry
    {
        public readonly TaskCompletionSource Ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string Url = "";
        public Dictionary<string, string> Headers = new();
        public long Length;
        public string Mime = "audio/webm";
        public string? Error;
        public byte[]?[] Pieces = Array.Empty<byte[]?>();
        public readonly SemaphoreSlim Gate = new(1, 1);
        public TaskCompletionSource? Refreshed;
        public readonly CancellationTokenSource Cts = new();
        public Task? Download;
        public long Order;
    }

    private readonly ConcurrentDictionary<string, Entry> _entries = new();
    private readonly HttpClient _http;
    private readonly Action<string> _expired;
    private long _order;

    public WebStreams(HttpClient http, Action<string> expired)
    {
        _http = http;
        _expired = expired;
    }

    public static bool Handles(string path) => path.StartsWith(Prefix, StringComparison.Ordinal);

    private Entry Get(string key) => _entries.GetOrAdd(key, _ => new Entry { Order = Interlocked.Increment(ref _order) });

    /// <summary>{ key, url, headers, length, mime } or { key, error }.</summary>
    public void Register(string key, string? url, Dictionary<string, string> headers, long length, string? mime, string? error)
    {
        var e = Get(key);
        if (error is not null)
        {
            e.Error = error;
            e.Refreshed?.TrySetResult();
            e.Ready.TrySetResult();
            return;
        }
        var first = e.Url.Length == 0;
        e.Url = url ?? "";
        e.Headers = headers;
        e.Error = null;
        if (first)
        {
            e.Length = length;
            e.Mime = string.IsNullOrEmpty(mime) ? "audio/webm" : mime!.Split(';')[0].Trim();
            e.Pieces = new byte[]?[(int)((length + Chunk - 1) / Chunk)];
            e.Ready.TrySetResult();
            e.Download = Task.Run(() => DownloadAllAsync(key, e));
        }
        e.Refreshed?.TrySetResult();
        Trim();
    }

    public void Forget(string key)
    {
        if (_entries.TryRemove(key, out var e)) e.Cts.Cancel();
    }

    private void Trim()
    {
        while (_entries.Count > MaxSongs)
        {
            var oldest = _entries.OrderBy(kv => kv.Value.Order).First().Key;
            Forget(oldest);
        }
    }

    // ---- downloading --------------------------------------------------------------------------

    private async Task DownloadAllAsync(string key, Entry e)
    {
        try
        {
            for (var i = 0; i < e.Pieces.Length && !e.Cts.IsCancellationRequested; i++)
                await PieceAsync(key, e, i, e.Cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            WebShell.Log($"stream-download-failed {ex.GetType().Name}: {WebShell.Redact(ex.Message)}");
        }
        catch (OperationCanceledException)
        {
            // forgotten
        }
    }

    private async Task<byte[]> PieceAsync(string key, Entry e, int i, CancellationToken ct)
    {
        if (e.Pieces[i] is { } have) return have;
        await e.Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (e.Pieces[i] is { } again) return again;
            var from = (long)i * Chunk;
            var to = Math.Min(e.Length, from + Chunk) - 1;
            for (var attempt = 0; ; attempt++)
            {
                var url = e.Url + (e.Url.Contains('?') ? "&" : "?") + $"range={from}-{to}";
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                foreach (var h in e.Headers) req.Headers.TryAddWithoutValidation(h.Key, h.Value);
                using var res = await _http.SendAsync(req, ct).ConfigureAwait(false);
                if ((int)res.StatusCode == 403 && attempt == 0)
                {
                    // the URL expired: ask the page for a fresh one (it re-resolves), then retry once
                    var refreshed = e.Refreshed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    _expired(key);
                    await refreshed.Task.WaitAsync(TimeSpan.FromSeconds(20), ct).ConfigureAwait(false);
                    if (e.Error is { } err) throw new InvalidOperationException(err);
                    continue;
                }
                res.EnsureSuccessStatusCode();
                var bytes = await res.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
                if (bytes.Length != to - from + 1) throw new InvalidOperationException($"short piece {bytes.Length}");
                e.Pieces[i] = bytes;
                return bytes;
            }
        }
        finally
        {
            e.Gate.Release();
        }
    }

    // ---- serving ------------------------------------------------------------------------------

    public async Task<CoreWebView2WebResourceResponse> ServeAsync(CoreWebView2Environment env, Uri uri, string? range)
    {
        var key = Uri.UnescapeDataString(uri.AbsolutePath[Prefix.Length..]);
        if (!_entries.TryGetValue(key, out var e)) e = Get(key);   // the page may ask before registering
        try
        {
            await e.Ready.Task.WaitAsync(TimeSpan.FromSeconds(45)).ConfigureAwait(true);
        }
        catch (TimeoutException)
        {
            return env.CreateWebResourceResponse(null, 504, "Stream lookup timed out", WebShell.Cors);
        }
        if (e.Error is { } err)
            return env.CreateWebResourceResponse(null, 502, Ascii(err), WebShell.Cors);

        long start = 0, end = e.Length - 1;
        if (range is { Length: > 0 } && range.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
        {
            var spec = range[6..].Split(',')[0].Split('-');
            if (long.TryParse(spec[0], out var s)) start = s;
            if (spec.Length > 1 && long.TryParse(spec[1], out var en)) end = en;
        }
        if (start >= e.Length) return env.CreateWebResourceResponse(null, 416, "Range Not Satisfiable", $"Content-Range: bytes */{e.Length}\n{WebShell.Cors}");
        // answer one piece at a time: the element asks again for the rest
        var piece = (int)(start / Chunk);
        end = Math.Min(end, Math.Min(e.Length - 1, (long)(piece + 1) * Chunk - 1));
        byte[] bytes;
        try
        {
            bytes = await Task.Run(() => PieceAsync(key, e, piece, e.Cts.Token)).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            WebShell.Log($"stream-piece-failed {ex.GetType().Name}: {WebShell.Redact(ex.Message)}");
            return env.CreateWebResourceResponse(null, 502, Ascii("Stream failed: " + ex.Message), WebShell.Cors);
        }
        var offset = (int)(start - (long)piece * Chunk);
        var count = (int)(end - start + 1);
        var headers = $"Content-Type: {e.Mime}\nContent-Length: {count}\nAccept-Ranges: bytes\nContent-Range: bytes {start}-{end}/{e.Length}\nCache-Control: no-cache\n{WebShell.Cors}";
        return env.CreateWebResourceResponse(new MemoryStream(bytes, offset, count, writable: false).AsRandomAccessStream(), 206, "Partial Content", headers);
    }

    private static string Ascii(string s) => new(s.Where(ch => ch >= 0x20 && ch < 0x7F).Take(180).ToArray());
}
