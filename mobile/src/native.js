// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// Android-native helpers (SonclePlugin.java): PO tokens from BotGuard in a hidden youtube.com
// WebView, byte-range requests with full headers, and Google sign-in.
import { registerPlugin } from '@capacitor/core';
import { REQUEST_KEY, CREATE_URL, GENERATE_IT_URL, bgHeaders, b64bytes, parseChallenge, parseBody, tokenFromBytes } from '../../src/botguard.mjs';
import PO_TOKEN_HTML from '../../src/po_token.html';

export const Soncle = registerPlugin('Soncle');

// Same browser identity the YTMUSIC/WEB stream clients use in src/yt.mjs, so tokens and requests match.
export const WEB_UA = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36';

// ---------- requests with any headers (googlevideo wants Origin/Referer/User-Agent for some clients) ----------
function decode(b64) {
  const bin = atob(b64 || '');
  const out = new Uint8Array(bin.length);
  for (let i = 0; i < bin.length; i++) out[i] = bin.charCodeAt(i);
  return out;
}
/** fetch()-compatible GET done natively; used for every googlevideo request. */
export async function nativeFetch(url, opts = {}) {
  const r = await Soncle.httpBytes({ url: String(url), headers: { ...(opts.headers || {}) } });
  const headers = r.contentRange ? { 'content-range': r.contentRange } : {};
  const body = decode(r.data);
  return new Response(r.status === 204 ? null : body, { status: r.status || 200, headers });
}

// ---------- PO tokens ----------
const withTimeout = (p, ms, what) => Promise.race([p, new Promise((_, rej) => setTimeout(() => rej(new Error(`${what} timed out`)), ms))]);
const exec = async (code) => JSON.parse((await withTimeout(Soncle.botguardExec({ code }), 20000, 'BotGuard script')).json);

async function bgRequest(url, body) {
  const r = await fetch(url, { method: 'POST', headers: bgHeaders(WEB_UA), body });
  const text = await r.text();
  if (!r.ok || !text) throw new Error(`BotGuard ${url.split('/').pop()} HTTP ${r.status}`);
  return parseBody(text);
}

let state = null, starting = null, failedAt = 0;
async function init(log) {
  await withTimeout(Soncle.botguardLoad({ html: PO_TOKEN_HTML, userAgent: WEB_UA }), 15000, 'BotGuard page');
  const origin = await exec('location.origin').catch(() => '?');
  if (origin !== 'https://www.youtube.com') log('BotGuard page origin is ' + origin);
  const ch = parseChallenge(await bgRequest(CREATE_URL, JSON.stringify([REQUEST_KEY])));
  const js = ch.interpreterJavascript;
  if (!js.privateDoNotAccessOrElseSafeScriptWrappedValue && js.privateDoNotAccessOrElseTrustedResourceUrlWrappedValue) {
    let u = js.privateDoNotAccessOrElseTrustedResourceUrlWrappedValue;
    if (u.startsWith('//')) u = 'https:' + u;
    js.privateDoNotAccessOrElseSafeScriptWrappedValue = await (await fetch(u, { headers: { 'User-Agent': WEB_UA } })).text();
  }
  const bgResponse = await exec(`runBotGuard(${JSON.stringify(ch)}).then(function (r) { window.__wpso = r.webPoSignalOutput; return r.botguardResponse; })`);
  const it = await bgRequest(GENERATE_IT_URL, JSON.stringify([REQUEST_KEY, bgResponse]));
  const ttl = Number(it[1]) || 3600;
  await exec(`createPoTokenMinter(window.__wpso, new Uint8Array(${JSON.stringify([...b64bytes(it[0])])})).then(function () { return true; })`);
  log('PO token minter ready (ttl ' + ttl + 's)');
  return { expires: Date.now() + Math.max(300, ttl - 600) * 1000, cache: new Map() };
}

/** PO token for a visitor id or video id. Fails fast for 2 minutes after a failed start. */
export async function mintPoToken(identifier, log = () => {}) {
  if (!state || Date.now() > state.expires) {
    if (Date.now() - failedAt < 120000) throw new Error('PO token provider unavailable');
    if (!starting) {
      starting = withTimeout(init(log), 25000, 'PO token init')
        .then((s) => { state = s; return s; })
        .catch((e) => { failedAt = Date.now(); state = null; log('PO token init failed: ' + e.message); Soncle.botguardReset().catch(() => {}); throw e; })
        .finally(() => { starting = null; });
    }
  }
  const st = state && Date.now() <= state.expires ? state : await starting;
  const key = String(identifier);
  if (st.cache.has(key)) return st.cache.get(key);
  const bytes = [...new TextEncoder().encode(key)];
  let out;
  try {
    out = await exec(`obtainPoToken(new Uint8Array(${JSON.stringify(bytes)})).then(function (u) { return Array.from(u); })`);
  } catch (e) {
    if (state === st) state = null;
    throw new Error('PO token: ' + e.message);
  }
  const tok = tokenFromBytes(out);
  st.cache.set(key, tok);
  return tok;
}

// ---------- sign-in ----------
export const SIGNIN_URL = 'https://accounts.google.com/ServiceLogin?ltmpl=music&service=youtube&passive=true&continue=https%3A%2F%2Fwww.youtube.com%2Fsignin%3Faction_handle_signin%3Dtrue%26next%3Dhttps%253A%252F%252Fmusic.youtube.com%252F';
/** Opens Google's sign-in page; resolves the YouTube cookie header, or throws if cancelled. */
export async function nativeSignIn() {
  const r = await Soncle.signIn({ url: SIGNIN_URL });
  return r.cookie || '';
}
export const nativeSignOut = () => Soncle.signOut();
