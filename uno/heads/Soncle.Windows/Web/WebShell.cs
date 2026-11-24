// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.Web.WebView2.Core;
using WinRT.Interop;

namespace Soncle.Windows.Web;

/// <summary>
/// Hosts Soncle's web UI in WebView2 — the same bundle the Android app runs, built for the desktop
/// (<c>mobile/build.mjs --desktop</c>, copied to <c>web/</c> next to the exe). It gives the page the
/// three things a browser can't:
/// <list type="bullet">
/// <item>the app's files, served at <c>https://app.soncle.local/</c> (a secure origin, so
/// MediaSource, IndexedDB and the audio worklets work);</item>
/// <item>an HTTP relay at <c>/_soncle/http</c>: requests to YouTube, googlevideo, LRCLIB and the
/// rest are made here with the headers the page asked for and no CORS — the Windows counterpart of
/// CapacitorHttp on Android (<c>mobile/src/desktop/host.js</c> routes <c>fetch</c> through it);</item>
/// <item>the window: dragging by the page's title bar, maximise, full screen, links in the
/// default browser.</item>
/// </list>
/// No cookie is stored or sent by the relay; nothing here logs a URL's query (tokens live there).
/// </summary>
internal sealed partial class WebShell : IDisposable
{
    public const string Host = "app.soncle.local";
    private const string Origin = "https://" + Host;
    private const string RelayPath = "/_soncle/http";

    private static readonly HashSet<string> SkipRequestHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "host", "content-length", "connection", "accept-encoding", "transfer-encoding", "cookie",
    };

    private static readonly HashSet<string> SkipResponseHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "content-length", "content-encoding", "transfer-encoding", "connection", "set-cookie", "set-cookie2",
        "access-control-allow-origin", "access-control-allow-credentials", "access-control-expose-headers",
        "strict-transport-security", "alt-svc", "content-security-policy", "cross-origin-resource-policy",
        "cross-origin-opener-policy", "cross-origin-embedder-policy",
    };

    private readonly WebView2 _view;
    private readonly Window _window;
    private readonly HttpClient _http;
    private readonly string _root;
    private bool _disposed;

    public WebShell(WebView2 view, Window window)
    {
        _view = view;
        _window = window;
        _root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "web"));
        _http = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            UseCookies = false,
            AllowAutoRedirect = true,
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
        })
        {
            Timeout = TimeSpan.FromSeconds(45),
        };
    }

    private static string DataFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Soncle Preview");

    // ---- window caption -----------------------------------------------------------------------

    /// <summary>
    /// The page draws its own 48 px title bar, as in the Electron app: the content goes under the
    /// caption row, the system's minimise/maximise/close stay on the right with a transparent
    /// background, and WinUI's own drag region is shrunk to <paramref name="dragSpot"/> (1×1) so
    /// the page's search box and buttons get their clicks.
    /// </summary>
    public static void UseCaption(Window window, FrameworkElement dragSpot)
    {
        try
        {
            window.ExtendsContentIntoTitleBar = true;
            window.SetTitleBar(dragSpot);
            var bar = window.AppWindow.TitleBar;
            bar.PreferredHeightOption = TitleBarHeightOption.Tall;
            bar.ButtonBackgroundColor = Colors.Transparent;
            bar.ButtonInactiveBackgroundColor = Colors.Transparent;
            SetCaptionForeground(window, dark: true);
        }
        catch (Exception e)
        {
            Log($"caption-failed {e.GetType().Name}");
        }
    }

    private static void SetCaptionForeground(Window window, bool dark)
    {
        var bar = window.AppWindow.TitleBar;
        var fg = dark ? global::Windows.UI.Color.FromArgb(255, 0xE4, 0xE1, 0xE9) : global::Windows.UI.Color.FromArgb(255, 0x1B, 0x1B, 0x21);
        bar.ButtonForegroundColor = fg;
        bar.ButtonHoverForegroundColor = fg;
        bar.ButtonPressedForegroundColor = fg;
        bar.ButtonInactiveForegroundColor = global::Windows.UI.Color.FromArgb(160, fg.R, fg.G, fg.B);
        bar.ButtonHoverBackgroundColor = dark ? global::Windows.UI.Color.FromArgb(30, 255, 255, 255) : global::Windows.UI.Color.FromArgb(30, 0, 0, 0);
        bar.ButtonPressedBackgroundColor = dark ? global::Windows.UI.Color.FromArgb(50, 255, 255, 255) : global::Windows.UI.Color.FromArgb(50, 0, 0, 0);
    }

    // ---- start --------------------------------------------------------------------------------

    public async Task StartAsync()
    {
        if (!File.Exists(Path.Combine(_root, "index.html")))
            throw new FileNotFoundException("The app's web files are missing (web\\index.html next to Soncle.Windows.exe).");

        Directory.CreateDirectory(DataFolder);
        var options = new CoreWebView2EnvironmentOptions
        {
            // songs start from the player bar's own gesture or from media keys
            AdditionalBrowserArguments = "--autoplay-policy=no-user-gesture-required",
        };
        var env = await CoreWebView2Environment.CreateWithOptionsAsync("", Path.Combine(DataFolder, "WebView2"), options);
        await _view.EnsureCoreWebView2Async(env);
        var core = _view.CoreWebView2 ?? throw new InvalidOperationException("WebView2 did not start");

        var s = core.Settings;
        s.IsNonClientRegionSupportEnabled = true;   // -webkit-app-region: drag in the page's CSS
        s.AreDefaultContextMenusEnabled = false;
        s.AreDevToolsEnabled = true;                 // preview build: F12 for diagnosis
        s.IsStatusBarEnabled = false;
        s.IsZoomControlEnabled = false;
        s.IsGeneralAutofillEnabled = false;
        s.IsPasswordAutosaveEnabled = false;
        s.IsWebMessageEnabled = true;

        core.AddWebResourceRequestedFilter(Origin + "/*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += OnResourceRequested;
        core.WebMessageReceived += OnMessage;
        core.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            OpenExternal(e.Uri);
        };
        core.NavigationStarting += (_, e) =>
        {
            // the app never navigates away from itself; links go to the browser. blob:/data:
            // (a backup being saved) and about: stay with WebView2.
            if (!e.Uri.StartsWith(Origin + "/", StringComparison.OrdinalIgnoreCase)
                && !e.Uri.StartsWith("blob:", StringComparison.OrdinalIgnoreCase)
                && !e.Uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                && !e.Uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
            {
                e.Cancel = true;
                OpenExternal(e.Uri);
            }
        };
        core.ProcessFailed += (sender, e) =>
        {
            Log($"webview-process-failed {e.ProcessFailedKind}");
            // a crashed or killed page comes back instead of leaving a blank window
            if (e.ProcessFailedKind is CoreWebView2ProcessFailedKind.RenderProcessExited or CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)
            {
                try { sender.Reload(); } catch (Exception ex) { Log($"reload-failed {ex.GetType().Name}"); }
            }
        };
        _window.AppWindow.Closing += OnClosing;
        core.Navigate(Origin + "/index.html");
        Log("started");
    }

    private bool _flushed;

    /// <summary>
    /// Before the window closes, the page saves what it still holds (library, Flow data — the
    /// same "pause" the Android app gets when it goes to the background), then the window closes.
    /// </summary>
    private async void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_flushed || _view.CoreWebView2 is not { } core) return;
        args.Cancel = true;
        _flushed = true;
        try
        {
            core.PostWebMessageAsJson("{\"type\":\"pause\"}");
            await Task.Delay(600);
        }
        catch (Exception ex)
        {
            Log($"flush-failed {ex.GetType().Name}");
        }
        _window.Close();
    }

    // ---- requests -----------------------------------------------------------------------------

    private void OnResourceRequested(CoreWebView2 sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        Uri uri;
        try
        {
            uri = new Uri(e.Request.Uri);
        }
        catch (UriFormatException)
        {
            e.Response = sender.Environment.CreateWebResourceResponse(null, 400, "Bad Request", "");
            return;
        }
        if (string.Equals(uri.AbsolutePath, RelayPath, StringComparison.Ordinal))
        {
            var deferral = e.GetDeferral();
            _ = RelayAsync(sender, e, uri, deferral);
            return;
        }
        e.Response = ServeFile(sender.Environment, uri);
    }

    private CoreWebView2WebResourceResponse ServeFile(CoreWebView2Environment env, Uri uri)
    {
        try
        {
            var rel = Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/');
            if (rel.Length == 0) rel = "index.html";
            var full = Path.GetFullPath(Path.Combine(_root, rel.Replace('/', Path.DirectorySeparatorChar)));
            if (!full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
                return env.CreateWebResourceResponse(null, 404, "Not Found", "");
            var bytes = File.ReadAllBytes(full);
            var headers = $"Content-Type: {MimeOf(full)}\nCache-Control: no-cache";
            return env.CreateWebResourceResponse(new MemoryStream(bytes, writable: false).AsRandomAccessStream(), 200, "OK", headers);
        }
        catch (Exception ex)
        {
            Log($"serve-failed {ex.GetType().Name}");
            return env.CreateWebResourceResponse(null, 500, "Internal Server Error", "");
        }
    }

    private static string MimeOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".html" => "text/html; charset=utf-8",
        ".js" or ".mjs" => "text/javascript; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".json" => "application/json; charset=utf-8",
        ".svg" => "image/svg+xml",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        ".ico" => "image/x-icon",
        ".woff2" => "font/woff2",
        ".woff" => "font/woff",
        ".ttf" => "font/ttf",
        ".wasm" => "application/wasm",
        ".md" or ".txt" => "text/plain; charset=utf-8",
        _ => "application/octet-stream",
    };

    /// <summary>
    /// The HTTP relay: <c>/_soncle/http?u=&lt;url&gt;</c> with the page's headers as JSON in
    /// <c>x-soncle-headers</c>. Runs on the UI thread between awaits (WebView2 objects are bound
    /// to it); the network wait itself doesn't block anything.
    /// </summary>
    private async Task RelayAsync(CoreWebView2 sender, CoreWebView2WebResourceRequestedEventArgs e, Uri uri, global::Windows.Foundation.Deferral deferral)
    {
        var env = sender.Environment;
        try
        {
            var target = QueryValue(uri.Query, "u");
            if (target is null || !Uri.TryCreate(target, UriKind.Absolute, out var to) || (to.Scheme != Uri.UriSchemeHttps && to.Scheme != Uri.UriSchemeHttp))
            {
                e.Response = env.CreateWebResourceResponse(null, 400, "Bad Request", "");
                return;
            }

            var method = new HttpMethod(e.Request.Method);
            using var req = new HttpRequestMessage(method, to);
            byte[]? body = null;
            if (e.Request.Content is { } content && method != HttpMethod.Get && method != HttpMethod.Head)
            {
                using var ms = new MemoryStream();
                await content.AsStreamForRead().CopyToAsync(ms);
                body = ms.ToArray();
                req.Content = new ByteArrayContent(body);
            }

            var json = e.Request.Headers.Contains("x-soncle-headers") ? e.Request.Headers.GetHeader("x-soncle-headers") : null;
            if (!string.IsNullOrEmpty(json))
            {
                using var doc = JsonDocument.Parse(json);
                foreach (var h in doc.RootElement.EnumerateObject())
                {
                    if (SkipRequestHeaders.Contains(h.Name) || h.Value.ValueKind != JsonValueKind.String) continue;
                    var v = h.Value.GetString() ?? "";
                    if (h.Name.StartsWith("content-", StringComparison.OrdinalIgnoreCase))
                    {
                        if (req.Content is null) continue;
                        req.Content.Headers.Remove(h.Name);
                        req.Content.Headers.TryAddWithoutValidation(h.Name, v);
                    }
                    else
                    {
                        req.Headers.Remove(h.Name);
                        req.Headers.TryAddWithoutValidation(h.Name, v);
                    }
                }
            }

            using var res = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead);
            var bytes = await res.Content.ReadAsByteArrayAsync();
            var headers = new StringBuilder();
            foreach (var h in res.Headers.Concat(res.Content.Headers))
            {
                if (SkipResponseHeaders.Contains(h.Key)) continue;
                foreach (var v in h.Value)
                {
                    var clean = v.Replace("\r", "").Replace("\n", "");
                    if (clean.Any(ch => ch > 0x7E)) continue;   // WebView2 rejects non-ASCII header values
                    headers.Append(h.Key).Append(": ").Append(clean).Append('\n');
                }
            }
            var status = (int)res.StatusCode;
            var stream = status is 204 or 304 || method == HttpMethod.Head ? null : new MemoryStream(bytes, writable: false).AsRandomAccessStream();
            var reason = string.IsNullOrEmpty(res.ReasonPhrase) ? "OK" : res.ReasonPhrase;
            try
            {
                e.Response = env.CreateWebResourceResponse(stream, status, reason, headers.ToString().TrimEnd());
            }
            catch (Exception hx)
            {
                // WebView2 refused a header: answer with only the ones the page needs
                Log($"relay-headers-refused {hx.GetType().Name}");
                var basic = new StringBuilder();
                if (res.Content.Headers.ContentType is { } ct) basic.Append("Content-Type: ").Append(ct).Append('\n');
                if (res.Content.Headers.ContentRange is { } cr) basic.Append("Content-Range: ").Append(cr).Append('\n');
                var again = status is 204 or 304 || method == HttpMethod.Head ? null : new MemoryStream(bytes, writable: false).AsRandomAccessStream();
                e.Response = env.CreateWebResourceResponse(again, status, reason, basic.ToString().TrimEnd());
            }
        }
        catch (Exception ex)
        {
            Log($"relay-failed {ex.GetType().Name}: {Redact(ex.Message)}" + (ex.InnerException is { } inner ? $" ({inner.GetType().Name}: {Redact(inner.Message)})" : ""));
            try
            {
                // the reason travels in the status text, so the page's error message shows it
                var why = new string(($"Relay {ex.GetType().Name}: " + Redact(ex.InnerException?.Message ?? ex.Message))
                    .Where(ch => ch >= 0x20 && ch < 0x7F).Take(180).ToArray());
                e.Response = env.CreateWebResourceResponse(null, 502, why, "");
            }
            catch
            {
                // the request may be gone already
            }
        }
        finally
        {
            deferral.Complete();
        }
    }

    private static string? QueryValue(string query, string name)
    {
        foreach (var part in query.TrimStart('?').Split('&'))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0) continue;
            if (part[..eq] == name) return Uri.UnescapeDataString(part[(eq + 1)..]);
        }
        return null;
    }

    // ---- messages from the page ---------------------------------------------------------------

    private void OnMessage(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!e.Source.StartsWith(Origin + "/", StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var m = doc.RootElement;
            if (m.ValueKind != JsonValueKind.Object || !m.TryGetProperty("type", out var t)) return;
            switch (t.GetString())
            {
                case "open":
                    if (m.TryGetProperty("url", out var url)) OpenExternal(url.GetString());
                    break;
                case "fullscreen":
                    WindowChrome.ToggleFullScreen(_window);
                    break;
                case "drag":
                    StartDrag();
                    break;
                case "maximize":
                    ToggleMaximize();
                    break;
                case "titlebar":
                    // the page sends the caption *symbol* colour (light symbols on a dark page)
                    if (m.TryGetProperty("color", out var c) && c.GetString() is { } color && IsLight(color) is { } light)
                        SetCaptionForeground(_window, dark: light);
                    break;
            }
        }
        catch (Exception ex)
        {
            Log($"message-failed {ex.GetType().Name}");
        }
    }

    private static void OpenExternal(string? url)
    {
        if (url is null || !Uri.TryCreate(url, UriKind.Absolute, out var u) || (u.Scheme != Uri.UriSchemeHttps && u.Scheme != Uri.UriSchemeHttp && u.Scheme != "mailto")) return;
        try
        {
            Process.Start(new ProcessStartInfo(u.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log($"open-failed {ex.GetType().Name}");
        }
    }

    /// <summary>Whether a CSS colour (#rgb, #rrggbb, rgb()/rgba()) is light; null when it can't be read.</summary>
    private static bool? IsLight(string css)
    {
        css = css.Trim();
        int r, g, b;
        var hex = HexColor().Match(css);
        var rgb = RgbColor().Match(css);
        if (hex.Success)
        {
            var h = hex.Groups[1].Value;
            if (h.Length == 3) h = string.Concat(h.Select(ch => new string(ch, 2)));
            r = Convert.ToInt32(h[..2], 16);
            g = Convert.ToInt32(h[2..4], 16);
            b = Convert.ToInt32(h[4..6], 16);
        }
        else if (rgb.Success)
        {
            r = int.Parse(rgb.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            g = int.Parse(rgb.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
            b = int.Parse(rgb.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
        }
        else return null;
        return (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255 > 0.6;
    }

    [GeneratedRegex(@"^#([0-9a-fA-F]{6}|[0-9a-fA-F]{3})$")]
    private static partial Regex HexColor();

    [GeneratedRegex(@"^rgba?\(\s*(\d+)[\s,]+(\d+)[\s,]+(\d+)")]
    private static partial Regex RgbColor();

    // ---- window moves (fallback when WebView2 doesn't handle app-region itself) ----------------

    private const int WmNcLButtonDown = 0x00A1;
    private const int HtCaption = 2;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendMessage(nint hWnd, int msg, nint wParam, nint lParam);

    private void StartDrag()
    {
        try
        {
            var hwnd = WindowNative.GetWindowHandle(_window);
            ReleaseCapture();
            SendMessage(hwnd, WmNcLButtonDown, HtCaption, 0);
        }
        catch (Exception ex)
        {
            Log($"drag-failed {ex.GetType().Name}");
        }
    }

    private void ToggleMaximize()
    {
        try
        {
            if (_window.AppWindow.Presenter is OverlappedPresenter p)
            {
                if (p.State == OverlappedPresenterState.Maximized) p.Restore();
                else p.Maximize();
            }
        }
        catch (Exception ex)
        {
            Log($"maximize-failed {ex.GetType().Name}");
        }
    }

    // ---- log ----------------------------------------------------------------------------------

    private static readonly object LogGate = new();

    /// <summary>%LOCALAPPDATA%\Soncle Preview\soncle.log (capped at ~1 MB); URLs are cut out.</summary>
    public static void Log(string message)
    {
        var line = "Soncle " + Redact(message);
        Trace.WriteLine(line);
        try
        {
            Directory.CreateDirectory(DataFolder);
            var file = Path.Combine(DataFolder, "soncle.log");
            lock (LogGate)
            {
                if (File.Exists(file) && new FileInfo(file).Length > 1_000_000) File.Delete(file);
                File.AppendAllText(file, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) + " " + line + Environment.NewLine);
            }
        }
        catch
        {
            // logging must never break the app
        }
    }

    public static string Redact(string message) => UrlPattern().Replace(message ?? "", "<url>");

    [GeneratedRegex(@"\b\w+://\S+")]
    private static partial Regex UrlPattern();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _http.Dispose();
    }
}
