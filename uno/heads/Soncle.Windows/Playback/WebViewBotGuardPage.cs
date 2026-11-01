// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Text.Json.Nodes;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Soncle.YouTube;

namespace Soncle.Windows.Playback;

/// <summary>
/// The hidden BotGuard page on Windows (docs/WINUI_PLAN.md §6), the WebView2 counterpart of the
/// hidden window in <c>src/potoken.mjs</c>: a 1×1, invisible <see cref="WebView2"/> that serves
/// the unchanged <c>src/po_token.html</c> (embedded as <c>Soncle.po_token.html</c>) at
/// <see cref="PoTokenMinter.PageUrl"/> from <c>WebResourceRequested</c> and then navigates there,
/// so the page has the https://www.youtube.com origin BotGuard checks (<c>NavigateToString</c>
/// would give it the wrong one). Every other request in this WebView is blocked, as in
/// potoken.mjs. <see cref="PoTokenMinter"/> drives it; it closes the page after 10 minutes idle.
/// <para>Everything touching the WebView runs on its <see cref="DispatcherQueue"/>;
/// <see cref="ExecAsync"/> and <see cref="Dispose"/> may be called from any thread. A script's
/// result comes back through <c>chrome.webview.postMessage</c> (so promises are awaited), and is
/// never logged: it can be a token.</para>
/// <para>Each page gets its own throw-away WebView2 profile (an explicit user-data folder under
/// %TEMP%\Soncle-potoken, deleted after the page closes, best effort; leftovers of earlier runs are
/// swept before this run's first page), the counterpart of potoken.mjs's in-memory
/// <c>session.fromPartition('soncle-potoken')</c>: nothing BotGuard stores on the youtube.com
/// origin outlives the page, and the folder is writable even when the app runs from a read-only
/// one.</para>
/// </summary>
internal sealed class WebViewBotGuardPage : IBotGuardPage
{
    private const string ResourceName = "Soncle.po_token.html";
    private static readonly Lazy<byte[]> Html = new(LoadHtml);

    private readonly Panel _host;
    private readonly DispatcherQueue _ui;
    private readonly WebView2 _view;
    private readonly Dictionary<long, TaskCompletionSource<JsonNode?>> _calls = new();
    private static readonly string ProfilesRoot = Path.Combine(Path.GetTempPath(), "Soncle-potoken");
    private static int _swept;
    private readonly string _profile = Path.Combine(ProfilesRoot, Guid.NewGuid().ToString("N"));
    private CoreWebView2? _core;
    private long _next;
    private int _closed;

    private WebViewBotGuardPage(Panel host, DispatcherQueue ui)
    {
        _host = host;
        _ui = ui;
        _view = new WebView2
        {
            Width = 1,
            Height = 1,
            Opacity = 0,
            IsHitTestVisible = false,
            IsTabStop = false,
        };
    }

    /// <summary>A <see cref="BotGuardPageFactory"/> that puts each page into <paramref name="host"/> (an invisible panel in the window).</summary>
    public static BotGuardPageFactory Factory(Panel host)
    {
        ArgumentNullException.ThrowIfNull(host);
        return ct => OnUi(host.DispatcherQueue, async () =>
        {
            var page = new WebViewBotGuardPage(host, host.DispatcherQueue);
            try
            {
                await page.LoadAsync(ct);
                return (IBotGuardPage)page;
            }
            catch
            {
                page.Dispose();
                throw;
            }
        });
    }

    public bool IsAlive => Volatile.Read(ref _closed) == 0;

    private async Task LoadAsync(CancellationToken ct)
    {
        SweepProfiles();
        Directory.CreateDirectory(_profile);
        var env = await CoreWebView2Environment.CreateWithOptionsAsync("", _profile, new CoreWebView2EnvironmentOptions());
        ct.ThrowIfCancellationRequested();
        _host.Children.Add(_view);
        await _view.EnsureCoreWebView2Async(env);
        ct.ThrowIfCancellationRequested();
        var core = _view.CoreWebView2 ?? throw new InvalidOperationException("WebView2 did not start");
        _core = core;

        var settings = core.Settings;
        settings.UserAgent = PoTokenMinter.UserAgent;
        settings.AreDevToolsEnabled = false;
        settings.AreDefaultContextMenusEnabled = false;
        settings.AreDefaultScriptDialogsEnabled = false;
        settings.AreHostObjectsAllowed = false;
        settings.IsStatusBarEnabled = false;
        settings.IsZoomControlEnabled = false;
        settings.IsScriptEnabled = true;
        settings.IsWebMessageEnabled = true;

        // the helper page is answered here; every other request in this WebView is blocked
        core.AddWebResourceRequestedFilter(PoTokenMinter.PageUrl + "*", CoreWebView2WebResourceContext.All);
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += OnResourceRequested;
        core.WebMessageReceived += OnMessage;
        core.NewWindowRequested += (_, e) => e.Handled = true;
        core.ProcessFailed += (_, _) => Close();

        var loaded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Done(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs e) => loaded.TrySetResult(e.IsSuccess);
        core.NavigationCompleted += Done;
        try
        {
            using (ct.Register(() => loaded.TrySetCanceled(ct)))
            {
                core.Navigate(PoTokenMinter.PageUrl);
                if (!await loaded.Task) throw new InvalidOperationException("the BotGuard page did not load");
            }
        }
        finally
        {
            core.NavigationCompleted -= Done;
        }
    }

    private void OnResourceRequested(CoreWebView2 sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var uri = e.Request.Uri;
        if (uri.StartsWith(PoTokenMinter.PageUrl, StringComparison.Ordinal))
        {
            var content = new MemoryStream(Html.Value, writable: false).AsRandomAccessStream();
            e.Response = sender.Environment.CreateWebResourceResponse(content, 200, "OK", "Content-Type: text/html; charset=utf-8");
        }
        else
        {
            e.Response = sender.Environment.CreateWebResourceResponse(null, 404, "Not Found", "");
        }
    }

    public async Task<JsonNode?> ExecAsync(string code, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(!IsAlive, this);
        var id = Interlocked.Increment(ref _next);
        var call = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_calls) _calls[id] = call;
        using (ct.Register(() => Complete(id, null, new OperationCanceledException(ct))))
        {
            var script = Wrap(id, code);
            if (!_ui.TryEnqueue(async () =>
                {
                    try
                    {
                        if (_core is null || !IsAlive) throw new ObjectDisposedException(nameof(WebViewBotGuardPage));
                        await _core.ExecuteScriptAsync(script);
                    }
                    catch (Exception e)
                    {
                        Complete(id, null, e);
                    }
                }))
                Complete(id, null, new ObjectDisposedException(nameof(WebViewBotGuardPage)));
            return await call.Task.ConfigureAwait(false);
        }
    }

    /// <summary>Runs <paramref name="code"/> (an expression, maybe a promise) and posts <c>{id, ok, value | error}</c> back.</summary>
    private static string Wrap(long id, string code) =>
        "(function () { var post = function (m) { window.chrome.webview.postMessage(JSON.stringify(m)); };" +
        " Promise.resolve().then(function () { return (" + code + "); }).then(" +
        $" function (v) {{ post({{ id: {id}, ok: true, value: v === undefined ? null : v }}); }}," +
        $" function (e) {{ post({{ id: {id}, ok: false, error: String((e && e.message) || e) }}); }});" +
        " return 0; })();";

    private void OnMessage(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!e.Source.StartsWith(PoTokenMinter.PageUrl, StringComparison.Ordinal)) return;
        JsonObject? m;
        try
        {
            m = JsonNode.Parse(e.TryGetWebMessageAsString()) as JsonObject;
        }
        catch (Exception)
        {
            return;   // not one of ours
        }
        if (m?["id"] is not JsonValue idValue || !idValue.TryGetValue<long>(out var id)) return;
        var ok = m["ok"] is JsonValue okValue && okValue.TryGetValue<bool>(out var b) && b;
        if (ok) Complete(id, m["value"]?.DeepClone(), null);
        else Complete(id, null, new InvalidOperationException("BotGuard page: " + ((string?)m["error"] ?? "error")));
    }

    private void Complete(long id, JsonNode? value, Exception? error)
    {
        TaskCompletionSource<JsonNode?>? call;
        lock (_calls)
        {
            if (!_calls.Remove(id, out call)) return;
        }
        if (error is null) call.TrySetResult(value);
        else call.TrySetException(error);
    }

    private void Close()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0) return;
        List<TaskCompletionSource<JsonNode?>> pending;
        lock (_calls)
        {
            pending = _calls.Values.ToList();
            _calls.Clear();
        }
        foreach (var call in pending) call.TrySetException(new ObjectDisposedException(nameof(WebViewBotGuardPage)));
    }

    public void Dispose()
    {
        Close();
        void Tear()
        {
            try
            {
                if (_core is { } core)
                {
                    core.WebResourceRequested -= OnResourceRequested;
                    core.WebMessageReceived -= OnMessage;
                }
                _view.Close();
                _host.Children.Remove(_view);
            }
            catch (Exception e)
            {
                PlaybackHost.Log($"potoken-page-close-failed {e.GetType().Name}");
            }
            DeleteProfileLater(_profile);
        }
        if (_ui.HasThreadAccess) Tear();
        else if (!_ui.TryEnqueue(Tear)) DeleteProfileLater(_profile);
    }

    /// <summary>The browser processes let go of the folder a moment after <c>Close()</c>; tries for about 10 s.</summary>
    private static void DeleteProfileLater(string dir) => _ = Task.Run(async () =>
    {
        for (var i = 0; i < 10; i++)
        {
            await Task.Delay(1000).ConfigureAwait(false);
            if (TryDelete(dir)) return;
        }
    });

    private static bool TryDelete(string dir)
    {
        try
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            return true;
        }
        catch (Exception)
        {
            return false;   // still in use; a later sweep gets it
        }
    }

    /// <summary>
    /// Once per run, before this run's first page: deletes the profiles earlier runs left behind
    /// (a crash, or a delete that lost the race). Never while a page of this run is open.
    /// </summary>
    private static void SweepProfiles()
    {
        if (Interlocked.Exchange(ref _swept, 1) != 0) return;
        try
        {
            if (!Directory.Exists(ProfilesRoot)) return;
            foreach (var dir in Directory.EnumerateDirectories(ProfilesRoot)) TryDelete(dir);
        }
        catch (Exception)
        {
            // best effort
        }
    }

    private static byte[] LoadHtml()
    {
        using var s = typeof(WebViewBotGuardPage).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(ResourceName + " is not embedded");
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    private static Task<T> OnUi<T>(DispatcherQueue ui, Func<Task<T>> work)
    {
        if (ui.HasThreadAccess) return work();
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!ui.TryEnqueue(async () =>
            {
                try { done.TrySetResult(await work()); }
                catch (Exception e) { done.TrySetException(e); }
            }))
            done.TrySetException(new InvalidOperationException("the window is closing"));
        return done.Task;
    }
}
