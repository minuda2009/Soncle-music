// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Soncle.YouTube;
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
    /// <summary>The host's own services (relay, local files) live on a second name: the app's
    /// files come from WebView2's fast folder mapping, which can't be intercepted.</summary>
    public const string ServiceHost = "host.soncle.local";
    private const string ServiceOrigin = "https://" + ServiceHost;
    private const string RelayPath = "/_soncle/http";

    /// <summary>Lets the app's page read the services' answers (and media from them).</summary>
    public const string Cors = "Access-Control-Allow-Origin: " + "https://" + Host + "\nAccess-Control-Expose-Headers: *\nAccess-Control-Allow-Methods: GET, POST, HEAD, OPTIONS\nAccess-Control-Allow-Headers: *\nAccess-Control-Max-Age: 86400";

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
    private readonly BotGuardPageFactory _botGuardFactory;
    private IBotGuardPage? _botGuard;
    private WebMediaSession? _media;
    private WebLocalFiles? _local;
    private readonly WebStreams _streams;
    private readonly HttpClient _http;
    private readonly string _root;
    private bool _disposed;

    public WebShell(WebView2 view, Window window, Panel botGuardHost)
    {
        _view = view;
        _window = window;
        _botGuardFactory = Playback.WebViewBotGuardPage.Factory(botGuardHost);
        _root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "web"));
        _http = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            UseCookies = false,
            AllowAutoRedirect = true,
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
            ConnectTimeout = TimeSpan.FromSeconds(20),
            ConnectCallback = ConnectAsync,
            // Like Node (the Electron app's main process): connect directly. .NET would otherwise
            // follow Windows' proxy settings, including "automatically detect" (WPAD) and a proxy
            // an old VPN left behind, and a stale one makes every request hang to the timeout.
            UseProxy = false,
        })
        {
            Timeout = TimeSpan.FromSeconds(45),
        };
        _streams = new WebStreams(_http, key => _window.DispatcherQueue.TryEnqueue(() =>
        {
            if (_view.CoreWebView2 is { } core)
                Reply(core, new JsonObject { ["type"] = "event", ["event"] = "streamExpired", ["data"] = new JsonObject { ["key"] = key } });
        }));
    }

    /// <summary>
    /// Connects the way Node (and so the Electron app) does: IPv4 addresses first, each with a
    /// short timeout, then IPv6. .NET on its own tries addresses one by one with no fallback race,
    /// so on a network that advertises IPv6 but doesn't route it every request hung until the
    /// timeout ("TaskCanceledException" in the relay).
    /// </summary>
    private static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext ctx, CancellationToken ct)
    {
        var host = ctx.DnsEndPoint.Host;
        IPAddress[] addresses = IPAddress.TryParse(host, out var literal)
            ? new[] { literal }
            : await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);
        var ordered = addresses.Where(a => a.AddressFamily == AddressFamily.InterNetwork)
            .Concat(addresses.Where(a => a.AddressFamily == AddressFamily.InterNetworkV6))
            .ToList();
        Exception? last = null;
        foreach (var ip in ordered)
        {
            var socket = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                using var attempt = CancellationTokenSource.CreateLinkedTokenSource(ct);
                attempt.CancelAfter(TimeSpan.FromSeconds(ip.AddressFamily == AddressFamily.InterNetwork ? 6 : 4));
                await socket.ConnectAsync(new IPEndPoint(ip, ctx.DnsEndPoint.Port), attempt.Token).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception e) when (!ct.IsCancellationRequested)
            {
                socket.Dispose();
                last = e;
            }
        }
        throw last ?? new SocketException((int)SocketError.HostNotFound);
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
            // Trimmed: no Edge services the app never uses (translate, autofill, sync, component
            // updates, background networking, spare renderers); one renderer process for one page.
            AdditionalBrowserArguments = "--autoplay-policy=no-user-gesture-required"
                + " --disable-features=HardwareMediaKeyHandling,MediaSessionService,Translate,AutofillServerCommunication,OptimizationHints,SpareRendererForSitePerProcess,msSmartScreenProtection,msEdgeShopping"
                + " --disable-background-networking --disable-component-update --disable-sync --disable-default-apps --no-first-run --renderer-process-limit=1",
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
        s.IsPinchZoomEnabled = false;
        s.IsSwipeNavigationEnabled = false;
        s.IsBuiltInErrorPageEnabled = false;
        s.IsGeneralAutofillEnabled = false;
        s.IsPasswordAutosaveEnabled = false;
        s.IsWebMessageEnabled = true;

        // Output-device names (per-device sound profiles need them): Chromium shows them to a page
        // that holds the microphone permission. The Electron app granted the equivalent
        // ("speaker-selection"); no Soncle code ever calls getUserMedia, so nothing is recorded.
        try
        {
            await core.Profile.SetPermissionStateAsync(CoreWebView2PermissionKind.Microphone, Origin, CoreWebView2PermissionState.Allow);
        }
        catch (Exception ex)
        {
            Log($"device-names-permission-failed {ex.GetType().Name}");
        }
        core.PermissionRequested += (_, e) =>
        {
            var ours = e.Uri.StartsWith(Origin + "/", StringComparison.OrdinalIgnoreCase);
            e.State = ours && e.PermissionKind is CoreWebView2PermissionKind.ClipboardRead or CoreWebView2PermissionKind.Microphone
                ? CoreWebView2PermissionState.Allow
                : CoreWebView2PermissionState.Deny;
            e.Handled = true;
        };

        core.SetVirtualHostNameToFolderMapping(Host, _root, CoreWebView2HostResourceAccessKind.Allow);
        core.AddWebResourceRequestedFilter(ServiceOrigin + "/*", CoreWebView2WebResourceContext.All);
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
        try
        {
            _local = new WebLocalFiles(DataFolder, _window, ServiceOrigin);
        }
        catch (Exception e)
        {
            Log($"local-files-unavailable {e.GetType().Name}: {e.Message}");
        }
        try
        {
            _media = new WebMediaSession(_window, _window.DispatcherQueue, (action, seekTime) =>
            {
                Wake(core);
                var data = new JsonObject { ["action"] = action };
                if (seekTime is { } t) data["seekTime"] = t;
                Reply(core, new JsonObject { ["type"] = "event", ["event"] = "mediaAction", ["data"] = data });
            });
        }
        catch (Exception e)
        {
            // the app plays without media keys rather than not opening
            Log($"media-session-unavailable {e.GetType().Name}");
        }
        _window.AppWindow.Changed += (appWindow, e) =>
        {
            if (e.DidPresenterChange || e.DidVisibilityChange) _ = UpdateMemoryAsync();
        };
        core.Navigate(Origin + "/index.html");
        Log("started");
    }

    private bool _flushed;
    private bool _playing;
    private bool _suspended;

    private bool Minimized => _window.AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } || !_window.AppWindow.IsVisible;

    /// <summary>
    /// Minimised: WebView2 trims its memory ("low" target). Minimised and nothing playing: the page
    /// is suspended outright (no timers, no rendering) and wakes when the window comes back. Media
    /// keys go through the native media session, which wakes it too.
    /// </summary>
    private async Task UpdateMemoryAsync()
    {
        if (_view.CoreWebView2 is not { } core) return;
        try
        {
            var min = Minimized;
            core.MemoryUsageTargetLevel = min ? CoreWebView2MemoryUsageTargetLevel.Low : CoreWebView2MemoryUsageTargetLevel.Normal;
            if (min && !_playing && !_suspended)
            {
                _view.Visibility = Visibility.Collapsed;   // suspension needs a hidden WebView
                _suspended = await core.TrySuspendAsync();
                if (!_suspended) _view.Visibility = Visibility.Visible;
            }
            else if (!min && _suspended)
            {
                Wake(core);
            }
        }
        catch (Exception ex)
        {
            Log($"memory-update-failed {ex.GetType().Name}");
        }
    }

    private void Wake(CoreWebView2 core)
    {
        if (!_suspended) return;
        _suspended = false;
        _view.Visibility = Visibility.Visible;
        try { core.Resume(); } catch (Exception ex) { Log($"resume-failed {ex.GetType().Name}"); }
    }

    /// <summary>
    /// Before the window closes, the page saves what it still holds (library, Flow data — the
    /// same "pause" the Android app gets when it goes to the background), then the window closes.
    /// </summary>
    private async void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_flushed || _view.CoreWebView2 is not { } core) return;
        args.Cancel = true;
        _flushed = true;
        WindowPlacement.Save(_window);
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
        if (string.Equals(e.Request.Method, "OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            e.Response = sender.Environment.CreateWebResourceResponse(null, 204, "No Content", Cors);
            return;
        }
        if (string.Equals(uri.AbsolutePath, RelayPath, StringComparison.Ordinal))
        {
            var deferral = e.GetDeferral();
            _ = RelayAsync(sender, e, uri, deferral);
            return;
        }
        if (WebStreams.Handles(uri.AbsolutePath))
        {
            var deferral = e.GetDeferral();
            var range = e.Request.Headers.Contains("Range") ? e.Request.Headers.GetHeader("Range") : null;
            _ = ServeStreamAsync(sender.Environment, e, uri, range, deferral);
            return;
        }
        if (WebLocalFiles.Handles(uri.AbsolutePath) && _local is { } local)
        {
            var deferral = e.GetDeferral();
            var range = e.Request.Headers.Contains("Range") ? e.Request.Headers.GetHeader("Range") : null;
            _ = ServeLocalAsync(sender.Environment, e, local, uri, range, deferral);
            return;
        }
        e.Response = ServeFile(sender.Environment, uri);
    }

    private async Task ServeStreamAsync(CoreWebView2Environment env, CoreWebView2WebResourceRequestedEventArgs e, Uri uri, string? range, global::Windows.Foundation.Deferral deferral)
    {
        try
        {
            e.Response = await _streams.ServeAsync(env, uri, range);
        }
        catch (Exception ex)
        {
            Log($"stream-serve-failed {ex.GetType().Name}");
            try { e.Response = env.CreateWebResourceResponse(null, 500, "Internal Server Error", Cors); } catch { /* gone */ }
        }
        finally
        {
            deferral.Complete();
        }
    }

    private static async Task ServeLocalAsync(CoreWebView2Environment env, CoreWebView2WebResourceRequestedEventArgs e, WebLocalFiles local, Uri uri, string? range, global::Windows.Foundation.Deferral deferral)
    {
        try
        {
            e.Response = await local.ServeAsync(env, uri, range);
        }
        catch (Exception ex)
        {
            Log($"local-serve-failed {ex.GetType().Name}");
            try { e.Response = env.CreateWebResourceResponse(null, 500, "Internal Server Error", ""); } catch { /* gone */ }
        }
        finally
        {
            deferral.Complete();
        }
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
                e.Response = env.CreateWebResourceResponse(null, 400, "Bad Request", Cors);
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

            // the page's headers come as JSON in the URL (?h=), so the request stays a "simple" one
            // with no CORS preflight; the old header form is still read
            var json = QueryValue(uri.Query, "h")
                ?? (e.Request.Headers.Contains("x-soncle-headers") ? e.Request.Headers.GetHeader("x-soncle-headers") : null);
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
            headers.Append(Cors).Append('\n');
            var reason = string.IsNullOrEmpty(res.ReasonPhrase) ? "OK" : res.ReasonPhrase;
            try
            {
                e.Response = env.CreateWebResourceResponse(stream, status, reason, headers.ToString().TrimEnd());
            }
            catch (Exception hx)
            {
                // WebView2 refused a header: answer with only the ones the page needs
                Log($"relay-headers-refused {hx.GetType().Name}");
                var basic = new StringBuilder(Cors + "\n");
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
                e.Response = env.CreateWebResourceResponse(null, 502, why, Cors);
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
                case "call":
                    _ = CallAsync(sender, m.Clone());
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

    // ---- plugin calls from the page (the "Soncle" plugin's botguard* methods) -----------------

    private async Task CallAsync(CoreWebView2 core, JsonElement m)
    {
        var id = m.TryGetProperty("id", out var idEl) && idEl.TryGetInt64(out var n) ? n : 0;
        var method = m.TryGetProperty("method", out var me) ? me.GetString() : null;
        var args = m.TryGetProperty("args", out var a) ? a : default;
        try
        {
            JsonNode data;
            string Arg(string k) => args.ValueKind == JsonValueKind.Object && args.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            var local = _local;
            switch (method)
            {
                case "botguardLoad":
                    _botGuard?.Dispose();
                    _botGuard = null;
                    using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
                        _botGuard = await _botGuardFactory(cts.Token);
                    data = new JsonObject();
                    break;
                case "botguardExec":
                {
                    var code = args.ValueKind == JsonValueKind.Object && args.TryGetProperty("code", out var c) ? c.GetString() ?? "" : "";
                    var page = _botGuard is { IsAlive: true } p ? p : throw new InvalidOperationException("BotGuard page is not loaded");
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                    var value = await page.ExecAsync(code, cts.Token);
                    data = new JsonObject { ["json"] = value?.ToJsonString() ?? "null" };
                    break;
                }
                case "mediaUpdate":
                    if (args.ValueKind == JsonValueKind.Object)
                    {
                        _media?.Update(args);
                        _playing = args.TryGetProperty("playing", out var pl) && pl.ValueKind == JsonValueKind.True;
                        // the taskbar and Alt+Tab show the song, like the desktop app
                        var title = Arg("title");
                        var artist = Arg("artist");
                        _window.Title = _playing && title.Length > 0 ? (artist.Length > 0 ? $"{title} \u2022 {artist}" : title) : "Soncle Preview";
                    }
                    data = new JsonObject();
                    break;
                case "carryOver":
                    data = CarryOverDesktopLibrary();
                    break;
                case "registerStream":
                {
                    var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty("headers", out var hs) && hs.ValueKind == JsonValueKind.Object)
                        foreach (var h in hs.EnumerateObject())
                            if (h.Value.ValueKind == JsonValueKind.String) headers[h.Name] = h.Value.GetString() ?? "";
                    var length = args.ValueKind == JsonValueKind.Object && args.TryGetProperty("length", out var ln) && ln.ValueKind == JsonValueKind.Number ? ln.GetInt64() : 0;
                    var error = args.ValueKind == JsonValueKind.Object && args.TryGetProperty("error", out var er) && er.ValueKind == JsonValueKind.String ? er.GetString() : null;
                    if (error is null && length <= 0) error = "stream has no length";
                    _streams.Register(Arg("key"), Arg("url"), headers, length, Arg("mime"), error);
                    data = new JsonObject();
                    break;
                }
                case "forgetStream":
                    _streams.Forget(Arg("key"));
                    data = new JsonObject();
                    break;
                case "localFolders":
                    data = local?.Folders() ?? new JsonArray();
                    break;
                case "localAddFolder":
                    data = local is null ? new JsonArray() : await local.AddFolderAsync();
                    break;
                case "localRemoveFolder":
                    data = local?.RemoveFolder(Arg("dir")) ?? new JsonArray();
                    break;
                case "localScan":
                    data = local is null ? new JsonObject { ["count"] = 0 } : await local.ScanAsync(p =>
                        _window.DispatcherQueue.TryEnqueue(() => Reply(core, new JsonObject
                        {
                            ["type"] = "event",
                            ["event"] = "localProgress",
                            ["data"] = new JsonObject { ["done"] = p.Done, ["total"] = p.Total, ["finished"] = p.Finished },
                        })));
                    break;
                case "localTracks":
                    data = local?.Tracks() ?? new JsonArray();
                    break;
                case "localOpen":
                {
                    var paths = args.ValueKind == JsonValueKind.Object && args.TryGetProperty("paths", out var ps) && ps.ValueKind == JsonValueKind.Array
                        ? ps.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList()
                        : null;
                    data = local is null ? new JsonArray() : await local.OpenAsync(paths);
                    break;
                }
                case "localPending":
                    data = local is null ? new JsonArray() : await local.PendingAsync();
                    break;
                case "localReveal":
                    data = JsonValue.Create(local?.Reveal(Arg("id")) ?? false);
                    break;
                case "botguardReset":
                    _botGuard?.Dispose();
                    _botGuard = null;
                    data = new JsonObject();
                    break;
                default:
                    throw new NotSupportedException($"{method} isn't in the Windows app yet");
            }
            Reply(core, new JsonObject { ["type"] = "result", ["id"] = id, ["ok"] = true, ["data"] = data });
        }
        catch (Exception ex)
        {
            Log($"call-failed {method} {ex.GetType().Name}: {Redact(ex.Message)}");
            Reply(core, new JsonObject { ["type"] = "result", ["id"] = id, ["ok"] = false, ["error"] = ex.Message });
        }
    }

    /// <summary>
    /// The desktop app's library and Flow data from %APPDATA%\Soncle, as text, for the page's
    /// first start (mobile/src/backend.js drops the cookie). Read only — nothing there changes.
    /// </summary>
    private static JsonObject CarryOverDesktopLibrary()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Soncle");
        var lib = Path.Combine(dir, "library.json");
        if (!File.Exists(lib)) return new JsonObject();
        var result = new JsonObject { ["library"] = File.ReadAllText(lib) };
        var flow = Path.Combine(dir, "flow-features.json");
        if (File.Exists(flow)) result["flow"] = File.ReadAllText(flow);
        Log("carry-over found the desktop library");
        return result;
    }

    private static void Reply(CoreWebView2 core, JsonObject msg)
    {
        try
        {
            core.PostWebMessageAsJson(msg.ToJsonString());
        }
        catch (Exception ex)
        {
            Log($"reply-failed {ex.GetType().Name}");
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
        _botGuard?.Dispose();
        _media?.Dispose();
        try { _local?.Flush(); } catch { /* best effort */ }
        _http.Dispose();
    }
}
