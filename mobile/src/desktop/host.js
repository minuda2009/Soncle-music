// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// The Windows app's web layer talks to its C# host (uno/heads/Soncle.Windows/Web) in two ways:
//  - window.chrome.webview.postMessage for fire-and-forget window actions (full screen, links),
//  - fetch() to other sites goes through the host's HTTP relay at /_soncle/http, which makes the
//    request natively (any headers, no CORS) — the Windows counterpart of CapacitorHttp on Android.

export const post = (msg) => {
  try { window.chrome?.webview?.postMessage(msg); } catch { /* not inside the Windows app */ }
};

// the host's services live on their own name; the app's files come from WebView2's folder mapping
export const SERVICES = 'https://host.soncle.local';
const RELAY = SERVICES + '/_soncle/http?u=';
const nativeFetch = window.fetch.bind(window);

async function relayFetch(input, init = {}) {
  let url, method, headers, body, signal;
  if (input instanceof Request) {
    url = input.url;
    method = init.method || input.method;
    headers = new Headers(init.headers || input.headers);
    signal = init.signal || input.signal;
    body = init.body !== undefined ? init.body : (method === 'GET' || method === 'HEAD' ? undefined : await input.arrayBuffer());
  } else {
    url = String(input instanceof URL ? input.href : input);
    method = init.method || 'GET';
    headers = new Headers(init.headers || {});
    signal = init.signal;
    body = init.body;
  }
  const u = new URL(url, location.href);
  if (u.origin === location.origin || u.origin === SERVICES || !/^https?:$/.test(u.protocol)) return nativeFetch(input, init);
  // every header (User-Agent, Origin, Referer and friends included) travels as JSON in the URL;
  // the host puts them back on the real request. No custom header, so no CORS preflight.
  const all = {};
  headers.forEach((v, k) => { all[k] = v; });
  return nativeFetch(RELAY + encodeURIComponent(u.href) + '&h=' + encodeURIComponent(JSON.stringify(all)), {
    method,
    body,
    signal,
    cache: 'no-store',
    credentials: 'omit',
  });
}

if (window.chrome?.webview) window.fetch = relayFetch;

// The window is dragged by the page's own title bar (-webkit-app-region: drag, as in Electron).
// WebView2 handles that itself where it can; if a press still reaches the page there, the host
// starts the move (and a double-click maximises), so dragging works either way.
function appRegion(el) {
  for (let n = el; n && n.nodeType === 1; n = n.parentElement) {
    if (n.closest && n.matches('button, input, select, textarea, a, [contenteditable], [role="slider"]')) return 'no-drag';
    const cs = getComputedStyle(n);
    const v = cs.getPropertyValue('-webkit-app-region') || cs.getPropertyValue('app-region');
    if (v === 'drag' || v === 'no-drag') return v;
  }
  return 'none';
}
if (window.chrome?.webview) {
  window.addEventListener('pointerdown', (e) => {
    if (e.button !== 0 || e.detail > 1 || appRegion(e.target) !== 'drag') return;
    e.preventDefault();
    post({ type: 'drag' });
  }, true);
  window.addEventListener('dblclick', (e) => {
    if (appRegion(e.target) === 'drag') post({ type: 'maximize' });
  }, true);
}
