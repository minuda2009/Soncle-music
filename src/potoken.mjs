// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// PO token (proof-of-origin) generation with Google's BotGuard, run in a hidden Chromium window.
// Port of the PoTokenWebView from the upstream Android app (see THIRD_PARTY_NOTICES.md), using the same po_token.html helper page.
import { BrowserWindow, session, net } from 'electron';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { REQUEST_KEY, CREATE_URL, GENERATE_IT_URL, bgHeaders, b64bytes, parseChallenge, parseBody, tokenFromBytes } from './botguard.mjs';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const HTML = fs.readFileSync(path.join(__dirname, 'po_token.html'), 'utf8');
const UA = `Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/${process.versions.chrome || '140.0.0.0'} Safari/537.36`;
const PAGE = 'https://www.youtube.com/soncle-potoken';

let state = null;
let initPromise = null;
let log = () => {};
export function setLogger(fn) { log = fn; }

const withTimeout = (p, ms, what) => Promise.race([p, new Promise((_, rej) => setTimeout(() => rej(new Error(`${what} timed out`)), ms))]);

async function bgRequest(url, body) {
  const r = await net.fetch(url, { method: 'POST', headers: bgHeaders(UA), body });
  const text = await r.text();
  if (!r.ok || !text) throw new Error(`BotGuard ${url.split('/').pop()} HTTP ${r.status}`);
  return parseBody(text);
}

async function init() {
  const ses = session.fromPartition('soncle-potoken');
  if (!ses.__soncleHandled) {
    // The helper page never needs the network; everything else in this session is blocked.
    ses.protocol.handle('https', (req) => (req.url.startsWith(PAGE) ? new Response(HTML, { headers: { 'content-type': 'text/html; charset=utf-8' } }) : new Response('', { status: 404 })));
    ses.setUserAgent(UA);
    ses.__soncleHandled = true;
  }
  const win = new BrowserWindow({ show: false, width: 400, height: 300, webPreferences: { session: ses, sandbox: true, contextIsolation: true, backgroundThrottling: false, offscreen: false, spellcheck: false } });
  const exec = (code) => withTimeout(win.webContents.executeJavaScript(code, true), 20000, 'BotGuard script');
  try {
    // Load the helper page with a youtube.com origin (BotGuard checks it). Normally the session's
    // https handler serves it; if that load fails (seen on some Windows setups) fall back to a
    // data: URL with a youtube.com base URL — the same trick Android's WebView uses.
    try { if (process.env.SONCLE_PT_DATA) throw new Error('forced'); await win.loadURL(PAGE); }
    catch (e) {
      log('helper page via handler failed (' + e.message + '), using data URL');
      await win.loadURL('data:text/html;charset=utf-8;base64,' + Buffer.from(HTML).toString('base64'), { baseURLForDataURL: 'https://www.youtube.com/' });
    }
    const origin = await exec('location.origin').catch(() => '?');
    if (origin !== 'https://www.youtube.com') log('helper page origin is ' + origin);
    const ch = parseChallenge(await bgRequest(CREATE_URL, JSON.stringify([REQUEST_KEY])));
    if (!ch.interpreterJavascript.privateDoNotAccessOrElseSafeScriptWrappedValue && ch.interpreterJavascript.privateDoNotAccessOrElseTrustedResourceUrlWrappedValue) {
      let u = ch.interpreterJavascript.privateDoNotAccessOrElseTrustedResourceUrlWrappedValue;
      if (u.startsWith('//')) u = 'https:' + u;
      ch.interpreterJavascript.privateDoNotAccessOrElseSafeScriptWrappedValue = await (await net.fetch(u, { headers: { 'User-Agent': UA } })).text();
    }
    const bgResponse = await exec(`runBotGuard(${JSON.stringify(ch)}).then(function (r) { window.__wpso = r.webPoSignalOutput; return r.botguardResponse; })`);
    const it = await bgRequest(GENERATE_IT_URL, JSON.stringify([REQUEST_KEY, bgResponse]));
    const bytes = [...b64bytes(it[0])];
    const ttl = Number(it[1]) || 3600;
    await exec(`createPoTokenMinter(window.__wpso, new Uint8Array(${JSON.stringify(bytes)})).then(function () { return true; })`);
    win.webContents.on('render-process-gone', () => { if (state?.win === win) state = null; });
    state = { win, exec, expires: Date.now() + Math.max(300, ttl - 600) * 1000, cache: new Map() };
    log('PO token minter ready (ttl ' + ttl + 's)');
    return state;
  } catch (e) {
    try { win.destroy(); } catch {}
    throw e;
  }
}

async function ready() {
  if (state && !state.win.isDestroyed() && Date.now() < state.expires) return state;
  if (state) { try { state.win.destroy(); } catch {} state = null; }
  if (!initPromise) initPromise = withTimeout(init(), 45000, 'PO token init').finally(() => { initPromise = null; });
  return initPromise;
}

// The hidden BotGuard window is a whole Chromium renderer (~60–90 MB). Every new song needs a token,
// so it lives while music plays, and is closed after 10 minutes without a request. Starting again
// later rebuilds it in about a second, usually while the previous song is still playing.
const IDLE_CLOSE = 10 * 60 * 1000;
let idleTimer = null;
const touch = () => { clearTimeout(idleTimer); idleTimer = setTimeout(() => { if (state) { log('closing idle PO token window'); reset(); } }, IDLE_CLOSE); idleTimer.unref?.(); };

export async function mint(identifier) {
  const st = await ready();
  touch();
  if (st.cache.has(identifier)) return st.cache.get(identifier);
  const bytes = [...Buffer.from(String(identifier), 'utf8')];
  let out;
  try {
    out = await st.exec(`obtainPoToken(new Uint8Array(${JSON.stringify(bytes)})).then(function (u) { return Array.from(u); })`);
  } catch (e) {
    try { st.win.destroy(); } catch {}
    if (state === st) state = null;
    throw e;
  }
  const tok = tokenFromBytes(out);
  st.cache.set(identifier, tok);
  return tok;
}

export function reset() { clearTimeout(idleTimer); if (state) { try { state.win.destroy(); } catch {} } state = null; }
