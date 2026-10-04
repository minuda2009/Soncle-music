// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// Android backend: provides the same `window.api` the desktop preload exposes, so the shared UI
// (renderer/) runs unchanged. YouTube access is the shared src/yt.mjs, bundled with youtubei.js's
// web build; network requests go through CapacitorHttp (native, no CORS).
import * as yt from '../../src/yt.mjs';
import * as autoeq from '../../src/autoeq.mjs';
import * as spotify from '../../src/spotify.mjs';
import { defaultSettings, defaults } from '../../src/defaults.mjs';
import { upgradeSettings, isBackupMarker, BACKUP_MARKER } from '../../src/legacy.mjs';
import { Capacitor } from '@capacitor/core';
import { Filesystem, Directory, Encoding } from '@capacitor/filesystem';
import { App } from '@capacitor/app';
import { Browser } from '@capacitor/browser';
import { StatusBar, Style } from '@capacitor/status-bar';
import { mediaSourceUrl } from './mse.js';
import { Soncle, nativeFetch, mintPoToken, nativeSignIn, nativeSignOut } from './native.js';

const VERSION = '0.1.6';
const native = Capacitor.isNativePlatform();
const TEST = globalThis.SONCLE_TEST || null;   // headless test harness only
// Recent messages for "Copy diagnostic log" (Settings). Web addresses are cut to their host, so
// no tokens, signatures or cookies end up in a bug report.
const LOG = [];
const clean = (x) => String(x instanceof Error ? x.message : typeof x === 'object' ? JSON.stringify(x) : x)
  .replace(/https?:\/\/([^/\s?#]+)[^\s]*/g, 'https://$1/…').replace(/(SAPISID|SID|HSID|SSID|APISID|cookie)[^\s;]*/gi, '$1=…').slice(0, 500);
function remember(kind, args) {
  LOG.push(new Date().toISOString().slice(11, 23) + ' ' + kind + ' ' + args.map(clean).join(' '));
  if (LOG.length > 300) LOG.shift();
}
for (const k of ['warn', 'error']) { const orig = console[k].bind(console); console[k] = (...a) => { remember(k, a); orig(...a); }; }
window.addEventListener('error', (e) => remember('uncaught', [e.message]));
window.addEventListener('unhandledrejection', (e) => remember('unhandled', [e.reason?.message || e.reason]));
const log = (...a) => { remember('log', a); console.log('[soncle]', ...a); };

// ---------- files in the app's private storage ----------
const files = {
  async read(name) { return (await Filesystem.readFile({ path: name, directory: Directory.Data, encoding: Encoding.UTF8 })).data; },
  async write(name, text) { await Filesystem.writeFile({ path: name, directory: Directory.Data, encoding: Encoding.UTF8, data: text, recursive: true }); }
};
const readJson = async (name) => { try { return JSON.parse(await files.read(name)); } catch { return null; } };
function saver(name, get, ms = 600) {
  let timer = null;
  return (now = false) => {
    clearTimeout(timer);
    const write = () => files.write(name, JSON.stringify(get())).catch((e) => log('save failed', name, e.message));
    if (now) return write();
    timer = setTimeout(write, ms);
  };
}

// Things the phone app doesn't do (yet) are switched off rather than shown broken.
const MOBILE_SETTINGS = { mica: false, closeToTray: false, smartDuck: 'off', discord: false, perDeviceSound: false, autoDeviceEq: false };
let store = structuredClone(defaults);
let flowDb = {};
let langSnap = '';
let cookie = '';            // YouTube session cookie: kept here and in auth.json, never in the UI's store
const saveAuth = () => files.write('auth.json', JSON.stringify({ cookie })).catch((e) => log('auth save failed', e.message));
const save = saver('library.json', () => store);
const flowSave = saver('flow-features.json', () => flowDb, 1500);

const ready = (async () => {
  const raw = await readJson('library.json');
  if (raw) {
    upgradeSettings(raw.settings);
    store = { ...defaults, ...raw, settings: { ...defaultSettings, ...(raw.settings || {}), eq: { ...defaultSettings.eq, ...(raw.settings?.eq || {}) } } };
  }
  Object.assign(store.settings, MOBILE_SETTINGS);
  if (!store.settings.mobileQuality) { store.settings.quality = 'auto'; store.settings.mobileQuality = 1; }   // phones start on Auto
  flowDb = (await readJson('flow-features.json')) || {};
  yt.configure({ cache: 'yt', lang: store.settings.lang, location: store.settings.location });
  langSnap = store.settings.lang + '|' + store.settings.location;
  cookie = (await readJson('auth.json'))?.cookie || '';
  if (cookie) yt.configure({ cookie });
  yt.setLogger((m) => log(m));
  if (native) {
    // Same stream clients and order as desktop: PO tokens come from BotGuard in a hidden
    // youtube.com WebView (native.js / SonclePlugin.java).
    yt.setPoTokenProvider((id) => mintPoToken(id, log));
  } else {
    yt.setStreamClients(['IOS', 'ANDROID_VR', 'TV', 'WEB_EMBEDDED']);
  }
  autoeq.init({ read: (n) => files.read('autoeq/' + n), write: (n, t) => files.write('autoeq/' + n, t) }, (u) => fetch(u), (m) => log(m));
  if (TEST?.client) yt.__setClient(TEST.client);
  return true;
})();

// ---------- streaming ----------
const ytLufs = (db) => (db != null && isFinite(db) ? -14 + db : null);
// "Auto" quality: the lighter stream on slow or metered-saving connections (2G/3G, Data Saver),
// the best one otherwise. Checked per song, so it follows the signal as you move.
function quality() {
  const q = store.settings.quality;
  if (q !== 'auto') return q;
  const c = navigator.connection;
  return c && (c.saveData || /2g|3g/.test(c.effectiveType || '')) ? 'low' : 'best';
}
async function stream(id, force = false) {
  if (TEST?.stream) return TEST.stream;
  return yt.resolveStream(id, { quality: quality(), force, fetchImpl: gv });
}
// googlevideo requests go through native code so Origin/Referer/User-Agent reach the server as set
const gv = (url, opts) => (native ? nativeFetch(url, opts) : fetch(url, opts));
async function fetchRange(s, from, to) {
  const url = s.url + (s.url.includes('?') ? '&' : '?') + `range=${from}-${to - 1}`;
  const r = await gv(url, { headers: s.headers || {} });
  if (!r.ok) { const e = new Error('HTTP ' + r.status); e.status = r.status; throw e; }
  return new Uint8Array(await r.arrayBuffer());
}
// Bytes from one particular stream (the file a player or analysis started on). On a 403 the URL
// is refreshed, but only a refresh that serves the same file (same length) is used.
async function rangeOf(s, id, from, to) {
  to = Math.min(to, s.length || to);
  try {
    return { buf: await fetchRange(s, from, to), total: s.length, s };
  } catch (e) {
    if (e.status !== 403 || TEST) throw e;
    yt.invalidateStream(id);
    const again = await yt.resolveStreamRotating(id, s.client, { quality: s.quality || quality(), fetchImpl: gv });
    if (again.length !== s.length) { yt.invalidateStream(id); throw new Error('stream changed'); }
    return { buf: await fetchRange(again, from, to), total: again.length, s: again };
  }
}
async function range(id, from, to) { return rangeOf(await stream(id), id, from, to); }

// On the phone songs play from https://localhost/_soncle/stream/<key>, answered natively by
// SoncleStreams.java (the phone's version of desktop's mstream: proxy): the audio element gets a
// normal seekable file, and the bytes come from googlevideo in 1 MB pieces. Each load gets its own
// key; the stream behind it is registered here once resolved, and refreshed when its URL expires.
const streamKeys = new Map();   // key → { id, s }, oldest first
const keyFor = new Map();       // video id → its current key
let streamSeq = 0;
function dropKey(k) { streamKeys.delete(k); Soncle.forgetStream({ key: k }).catch(() => {}); }
function nativeStreamUrl(id, force) {
  let key = keyFor.get(id);
  if (force || !key) {
    if (key) dropKey(key);
    key = id + '~' + (++streamSeq);
    keyFor.set(id, key);
    const k = key;
    streamKeys.set(k, { id, s: null });
    while (streamKeys.size > 4) { const oldest = streamKeys.keys().next().value; if (keyFor.get(streamKeys.get(oldest)?.id) === oldest) keyFor.delete(streamKeys.get(oldest).id); dropKey(oldest); }
    (async () => {
      await ready;
      const s = await stream(id, force);
      if (!streamKeys.has(k)) return;
      streamKeys.get(k).s = s;
      await Soncle.registerStream({ key: k, url: s.url, headers: s.headers || {}, length: s.length, mime: s.mime });
    })().catch((e) => {
      log('stream ' + id + ' failed: ' + e.message);
      if (keyFor.get(id) === k) keyFor.delete(id);
      Soncle.registerStream({ key: k, error: e.message || 'stream failed' }).catch(() => {});
    });
  }
  return location.origin + '/_soncle/stream/' + encodeURIComponent(key);
}
if (native) {
  Soncle.addListener('streamExpired', async ({ key }) => {
    const cur = streamKeys.get(key);
    if (!cur?.s) return Soncle.registerStream({ key, error: 'stream expired' }).catch(() => {});
    try {
      yt.invalidateStream(cur.id);
      const again = await yt.resolveStreamRotating(cur.id, cur.s.client, { quality: quality(), fetchImpl: gv });
      if (again.length !== cur.s.length) throw new Error('stream changed');
      cur.s = again;
      log('stream ' + cur.id + ' refreshed via ' + again.client);
      await Soncle.registerStream({ key, url: again.url, headers: again.headers || {}, length: again.length, mime: again.mime });
    } catch (e) {
      log('stream ' + cur.id + ' refresh failed: ' + e.message);
      Soncle.registerStream({ key, error: e.message }).catch(() => {});
    }
  });
}

// ---------- lyrics (LRCLIB, then YouTube Music) ----------
const UA_LYRICS = { 'User-Agent': 'Soncle (https://github.com/minuda2009)' };
const cleanTitle = (s) => (s || '').replace(/\s*[([](official|lyric|audio|video|visualizer|hd|4k|mv|remaster|feat\.?|ft\.)[^)\]]*[)\]]/gi, '').trim();
async function lrclib(t) {
  const artist = (t.artists || []).map((a) => a.name).filter(Boolean)[0] || '';
  const title = cleanTitle(t.title);
  const q = new URLSearchParams({ track_name: title, artist_name: artist });
  if (t.album?.name) q.set('album_name', t.album.name);
  if (t.duration) q.set('duration', String(Math.round(t.duration)));
  try {
    const r = await fetch('https://lrclib.net/api/get?' + q, { headers: UA_LYRICS });
    if (r.ok) { const j = await r.json(); if (j.syncedLyrics || j.plainLyrics) return { synced: j.syncedLyrics || null, plain: j.plainLyrics || null, source: 'LRCLIB' }; }
  } catch {}
  try {
    const r = await fetch('https://lrclib.net/api/search?' + new URLSearchParams({ track_name: title, artist_name: artist }), { headers: UA_LYRICS });
    if (r.ok) {
      const arr = await r.json();
      const best = arr.find((x) => x.syncedLyrics && (!t.duration || Math.abs(x.duration - t.duration) < 5)) || arr.find((x) => x.syncedLyrics) || arr[0];
      if (best) return { synced: best.syncedLyrics || null, plain: best.plainLyrics || null, source: 'LRCLIB' };
    }
  } catch {}
  return null;
}
async function lyrics(t) {
  if (TEST) return { synced: Array.from({ length: 14 }, (_, i) => `[00:${String(i * 2).padStart(2, '0')}.00] Line ${i + 1} of the song`).join('\n'), plain: null, source: 'LRCLIB (test)' };
  const a = await lrclib(t);
  if (a?.synced) return a;
  try {
    const b = await yt.ytLyrics(t.id);
    if (b?.plain) return { synced: null, plain: b.plain, source: b.source?.replace(/^Source:\s*/i, '') || 'YouTube Music' };
  } catch {}
  return a;
}

// ---------- backup / restore ----------
async function backup() {
  const { cookie: _c, session: _s, downloads: _d, ...rest } = store;
  const name = `soncle-backup-${new Date().toISOString().slice(0, 10)}.json`;
  await Filesystem.writeFile({ path: name, directory: Directory.Documents, encoding: Encoding.UTF8, data: JSON.stringify({ app: BACKUP_MARKER, version: 1, at: Date.now(), ...rest }, null, 1) });
  return true;
}
function pickFile(accept) {
  return new Promise((resolve) => {
    const inp = Object.assign(document.createElement('input'), { type: 'file', accept });
    inp.onchange = () => resolve(inp.files[0] || null);
    inp.addEventListener('cancel', () => resolve(null));   // closing the picker must not leave this hanging
    inp.click();
  });
}
async function restore() {
  const f = await pickFile('application/json,.json');
  if (!f) return false;
  const data = JSON.parse(await f.text());
  if (!isBackupMarker(data.app)) throw new Error('Not a Soncle backup file');
  upgradeSettings(data.settings);
  for (const k of ['liked', 'playlists', 'savedAlbums', 'savedPlaylists', 'followedArtists', 'history', 'searchHistory', 'eqPresets']) if (Array.isArray(data[k])) store[k] = data[k];
  for (const k of ['lyricsOffsets', 'deviceProfiles']) if (data[k] && typeof data[k] === 'object') store[k] = data[k];
  if (data.settings) store.settings = { ...defaultSettings, ...data.settings, eq: { ...defaultSettings.eq, ...(data.settings.eq || {}) }, ...MOBILE_SETTINGS };
  await save(true);
  setTimeout(() => location.reload(), 200);
  return true;
}

// ---------- system media controls (notification + lock screen + headset buttons) ----------
// The shared UI already drives navigator.mediaSession; Android's WebView doesn't surface that to
// the system, so on the phone it is forwarded to a native MediaSession (foreground service).
if (native) {
  // Everything the system shows (notification, lock screen, Google Maps, car, watch) comes from
  // one native state; the shared UI keeps using the standard navigator.mediaSession API.
  const now = { title: '', artist: '', album: '', artwork: '', state: 'none', position: 0, duration: 0, rate: 1 };
  const handlers = new Map();
  let sent = null, timer = null;
  const push = () => {
    clearTimeout(timer);
    timer = setTimeout(() => {
      const playing = now.state === 'playing';
      sent = { pos: now.position, at: Date.now(), rate: now.rate, playing };
      Soncle.mediaUpdate({ title: now.title, artist: now.artist, album: now.album, artwork: now.artwork, playing, position: now.position, duration: now.duration, rate: now.rate }).catch((e) => log('media update failed', e.message));
    }, 40);
  };
  const shim = {
    get metadata() { return now.meta || null; },
    set metadata(m) {
      now.meta = m;
      if (!m) return;
      const art = [...(m.artwork || [])].filter((a) => /^https:/.test(a.src || ''));
      Object.assign(now, { title: m.title || '', artist: m.artist || '', album: m.album || '', artwork: art.length ? art[0].src : '' });
      push();
    },
    get playbackState() { return now.state; },
    set playbackState(s) { if (s !== now.state) { now.state = s; push(); } },
    setActionHandler(action, fn) { if (fn) handlers.set(action, fn); else handlers.delete(action); },
    setPositionState(p) {
      if (!p) return;
      now.position = p.position || 0;
      now.duration = p.duration || 0;
      now.rate = p.playbackRate || 1;
      // the system extrapolates the position itself; only resend after a seek or a drift
      const expected = sent ? sent.pos + (sent.playing ? ((Date.now() - sent.at) / 1000) * sent.rate : 0) : -99;
      if (!sent || Math.abs(expected - now.position) > 1.5 || sent.rate !== now.rate) push();
    }
  };
  Soncle.addListener('mediaAction', (d) => {
    const fn = handlers.get(d.action);
    if (fn) fn({ action: d.action, seekTime: d.seekTime });
  });
  try { Object.defineProperty(navigator, 'mediaSession', { value: shim, configurable: true }); } catch (e) { log('media session shim failed', e.message); }
  globalThis.MediaMetadata = globalThis.MediaMetadata || class { constructor(o) { Object.assign(this, o); } };
  StatusBar.setOverlaysWebView({ overlay: true }).catch(() => {});
  StatusBar.setStyle({ style: Style.Dark }).catch(() => {});
}

// Android back: close the open sheet, then go back a page, then send the app to the background
// (music keeps playing).
App.addListener?.('backButton', () => {
  const np = document.getElementById('nowPlaying');
  const modal = document.getElementById('modal');
  const queue = document.getElementById('queuePanel');
  if (modal && !modal.hidden && modal.childElementCount) { document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true })); return; }
  if (np && !np.hidden) { document.getElementById('npClose')?.click(); return; }
  if (queue && !queue.hidden && document.body.classList.contains('queue-open')) { document.getElementById('qpClose')?.click(); return; }
  const back = document.getElementById('navBack');
  if (back && !back.disabled) { back.click(); return; }
  App.minimizeApp().catch(() => {});
}).catch?.(() => {});

const noop = () => {};
const later = (what) => { throw new Error(`${what} isn't in the Android app yet.`); };
const withReady = (fn) => async (...a) => { await ready; return fn(...a); };

const api = {
  platform: 'android',
  mobile: true,
  home: withReady((chip) => yt.home(chip)),
  homeMore: withReady(() => yt.homeMore()),
  explore: withReady(() => yt.explore()),
  browse: withReady((id, params) => yt.browse(id, params)),
  suggestions: withReady((q) => yt.suggestions(q)),
  search: withReady((q, type) => yt.search(q, type)),
  searchMore: withReady((q, type) => yt.searchMore(q, type)),
  album: withReady((id) => yt.album(id)),
  artist: withReady((id) => yt.artist(id)),
  playlist: withReady((id, all) => yt.playlist(id, all)),
  upNext: withReady((id, pl) => yt.upNext(id, pl)),
  radio: withReady((pl, params) => yt.radio(pl, params)),
  related: withReady((id) => yt.related(id)),
  library: withReady(() => yt.library()),
  songInfo: withReady((id) => yt.songInfo(id)),
  rate: withReady((id, like) => (store.settings.syncLikes && cookie ? yt.rate(id, like) : false)),
  prefetch: withReady(async (id) => {
    const s = await stream(id);
    if (native && !TEST) nativeStreamUrl(id, false);   // start downloading it now (see SoncleStreams.java)
    return { client: s.client, lufs: ytLufs(s.loudnessDb), mime: s.mime, bitrate: s.bitrate };
  }),
  lyrics: withReady((t) => lyrics(t)),
  // Android-only: the player's source and raw byte ranges (Flow analysis)
  srcFor: (t, bust) => {
    // On the phone a reload keeps the song's download (it retries and refreshes its own URL);
    // only a song whose lookup failed is looked up again.
    if (native) return nativeStreamUrl(t.id, false);
    if (bust) yt.invalidateStream(t.id);
    return mediaSourceUrl(t.id, { stream: async (id) => { await ready; return { ...(await stream(id, !!bust)), quality: quality() }; }, range: (s, a, b) => rangeOf(s, t.id, a, b), log });
  },
  range: withReady((id, from, to) => range(id, from, to)),

  signIn: withReady(async () => {
    if (!native) return later('Signing in');
    const c = await nativeSignIn();
    if (!c) return false;
    cookie = c;
    await saveAuth();
    yt.configure({ cookie });
    return true;
  }),
  signInBrowser: async () => null,
  signOut: withReady(async () => {
    cookie = '';
    await saveAuth();
    yt.configure({ cookie: '' });
    if (native) await nativeSignOut().catch(() => {});
    return true;
  }),
  authStatus: withReady(() => !!cookie),
  premiumStatus: withReady(() => (cookie ? yt.premiumStatus() : false)),

  storeGet: withReady(() => { const { cookie: _c, ...rest } = store; return structuredClone(rest); }),
  storeSet: withReady((key, value) => {
    if (key === 'cookie' || key === 'downloads') return false;
    store[key] = value;
    if (key === 'settings' && value) {
      Object.assign(value, MOBILE_SETTINGS);
      // only when the content language/region actually changed (it restarts the YouTube session)
      const snap = value.lang + '|' + value.location;
      if (snap !== langSnap) { langSnap = snap; yt.configure({ lang: value.lang, location: value.location }); }
    }
    save();
    return true;
  }),

  download: async () => later('Downloads'),
  removeDownload: async () => true,
  downloads: async () => ({ done: [], active: [] }),
  downloadsSize: async () => 0,
  openDownloads: async () => false,
  btDevices: async () => [],
  setMini: async () => false,
  backup: withReady(backup),
  restore: withReady(restore),
  clearCache: withReady(async () => { yt.configure({}); return true; }),
  openExternal: async (u) => { if (/^https?:\/\//.test(u)) await Browser.open({ url: u }); return true; },
  info: async () => ({ version: VERSION, discord: false, platform: 'android', mobile: true, mica: false, micaSupported: false, dark: matchMedia('(prefers-color-scheme: dark)').matches }),
  licences: async () => (await fetch('THIRD_PARTY_NOTICES.md')).text(),
  hpSearch: withReady((q) => autoeq.search(String(q || '').slice(0, 80))),
  hpMatch: withReady((m) => autoeq.match(String(m || '').slice(0, 120)).catch(() => null)),
  hpProfile: withReady((p) => autoeq.profile(p)),

  flowGet: withReady((ids) => Object.fromEntries((ids || []).filter((id) => flowDb[id]).map((id) => [id, flowDb[id]]))),
  flowPut: withReady((id, f) => { if (id && f && typeof f === 'object') { flowDb[id] = { ...f, at: Date.now() }; flowSave(); } return true; }),
  flowCount: withReady(() => Object.keys(flowDb).length),
  flowClear: withReady(() => { flowDb = {}; flowSave(); return true; }),
  // Phones are always on battery; Flow analysis is a few hundred KB per song, so it isn't held back.
  power: async () => ({ onBattery: false }),

  localFolders: async () => [],
  localAddFolder: async () => later('Local music'),
  localRemoveFolder: async () => [],
  localScan: async () => ({ count: 0 }),
  localTracks: async () => [],
  localOpen: async () => [],
  localPending: async () => [],
  localReveal: async () => false,
  pathForFile: () => '',
  onLocalProgress: noop,
  onLocalPlay: noop,

  importSpotify: withReady((link) => spotify.fetchSpotify(link, (u, o) => fetch(u, o), (p) => importListeners.forEach((f) => f(p)))),
  importParse: async (text, name) => spotify.parseTrackList(text, name),
  importFile: async () => {
    const f = await pickFile('.csv,.txt,.tsv,text/*');
    if (!f) return null;
    const text = await f.text();
    return spotify.parseTrackList(f.name.endsWith('.tsv') ? text.replace(/\t/g, ',') : text, f.name.replace(/\.[^.]+$/, ''));
  },
  importMatch: withReady(async (tracks) => {
    importCancelled = false;
    const searchSongs = async (q) => (await yt.search(q, 'song')).sections.flatMap((s) => s.items || []).filter((x) => x.type === 'song');
    return spotify.matchAll(tracks, searchSongs, { onProgress: (p) => importListeners.forEach((f) => f(p)), isCancelled: () => importCancelled });
  }),
  importCancel: async () => { importCancelled = true; return true; },
  onImportProgress: (fn) => importListeners.push(fn),

  diagnostics: async () => [`Soncle ${VERSION} (Android) · ${navigator.userAgent.replace(/\s*\(KHTML.*$/, '')}`, ...LOG].join('\n'),
  sendState: noop,
  saveSession: (s) => { store.session = s; save(); },
  titlebarColor: noop,
  toggleFullscreen: noop,
  onMediaAction: noop,       // handled through navigator.mediaSession above
  onDownload: noop,
  onWinState: noop,
  onDuck: noop
};
let importCancelled = false;
const importListeners = [];

// Save straight away when the app is sent to the background (Android may kill it later).
App.addListener?.('pause', () => { save(true); flowSave(true); })?.catch?.(() => {});

globalThis.api = api;
document.documentElement.classList.add('mobile');

// Now playing on the phone stacks the panel under the controls; picking a tab scrolls to it.
document.addEventListener('click', (e) => {
  if (e.target.closest?.('.np-tabs .chip')) setTimeout(() => document.getElementById('npPanel')?.scrollIntoView({ behavior: 'smooth', block: 'start' }), 60);
}, true);
