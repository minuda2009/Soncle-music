// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
import { app, powerMonitor, BrowserWindow, ipcMain, protocol, net, session, shell, nativeImage, nativeTheme, Menu, Tray, dialog, powerSaveBlocker, safeStorage } from 'electron';
import path from 'node:path';
import fs from 'node:fs';
import os from 'node:os';
import { Readable } from 'node:stream';
import { execFile } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import * as yt from './yt.mjs';
import { DiscordRPC, DISCORD_AVAILABLE } from './discord.mjs';
import * as potoken from './potoken.mjs';
import { proxyStream, makeChunkCache } from './streamproxy.mjs';
const chunkCache = makeChunkCache();
import * as local from './local.mjs';
import * as autoeq from './autoeq.mjs';
import { carryOver, upgradeSettings, isBackupMarker, BACKUP_MARKER } from './legacy.mjs';
import { defaultSettings, defaults } from './defaults.mjs';
import { findBrowser, browserName, browserSignIn } from './browser-signin.mjs';
import * as spotify from './spotify.mjs';
import { createDucker } from './ducking.mjs';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.join(__dirname, '..');

export const APP_ID = 'com.minuda2009.soncle';   // keep identical to build.appId in package.json
app.setName('Soncle');
if (process.platform === 'win32') app.setAppUserModelId(APP_ID);
app.commandLine.appendSwitch('enable-features', 'HardwareMediaKeyHandling,MediaSessionService');
// Lighter: run Chromium's audio service inside the main process (one process less) and skip the
// spare renderer Chromium keeps warm for new tabs — this app never opens tabs.
if (!process.env.SONCLE_AUDIO_OOP) app.commandLine.appendSwitch('disable-features', 'AudioServiceOutOfProcess,SpareRendererForSitePerProcess');

protocol.registerSchemesAsPrivileged([
  { scheme: 'mstream', privileges: { stream: true, supportFetchAPI: true, bypassCSP: true, corsEnabled: true } }
]);

// ---------- persistent store ----------
const dataDir = app.getPath('userData');
// Earlier builds used another name (and so another folder): copy their data across once, before
// anything reads it. The old folder is left untouched.
const carryLog = [];
carryOver(app.getPath('appData'), dataDir, (m) => carryLog.push(m));
const storeFile = path.join(dataDir, 'library.json');
const dlDir = path.join(dataDir, 'downloads');
// Sign-in cookies are encrypted with Electron's safeStorage (OS keychain) before
// being written to disk, instead of living in plaintext in library.json.
function encCookie(v) {
  try {
    if (!v) return '';
    if (!safeStorage.isEncryptionAvailable()) return 'plain:' + v;
    return 'enc:' + safeStorage.encryptString(v).toString('base64');
  } catch { return 'plain:' + v; }
}
function decCookie(v) {
  try {
    if (!v) return '';
    if (v.startsWith('enc:')) return safeStorage.decryptString(Buffer.from(v.slice(4), 'base64'));
    if (v.startsWith('plain:')) return v.slice(6);
    return v; // legacy plaintext value
  } catch { return ''; }
}
let store = structuredClone(defaults);
try {
  const raw = JSON.parse(fs.readFileSync(storeFile, 'utf8'));
  upgradeSettings(raw.settings);
  store = { ...defaults, ...raw, settings: { ...defaultSettings, ...(raw.settings || {}), eq: { ...defaultSettings.eq, ...(raw.settings?.eq || {}) } } };
  // Existing installs (library.json from an earlier version) skip the full-screen welcome; they get
  // the small one-time notice instead. Only a brand-new installation sees the welcome.
  if (raw.settings && raw.settings.welcomed === undefined) store.settings.welcomed = true;
  store.cookie = decCookie(store.cookie); // decrypted for in-memory use
} catch {}
let saveTimer = null;
function save(now = false) {
  clearTimeout(saveTimer);
  const write = () => {
    try {
      fs.mkdirSync(dataDir, { recursive: true });
      // Never persist the cookie in plaintext.
      const onDisk = { ...store, cookie: encCookie(store.cookie) };
      fs.writeFileSync(storeFile + '.tmp', JSON.stringify(onDisk));
      fs.renameSync(storeFile + '.tmp', storeFile);
    } catch (e) { flog('store save failed', e); }
  };
  if (now) write(); else saveTimer = setTimeout(write, 400);
}

yt.configure({ cache: path.join(dataDir, 'yt-cache-v2'), cookie: store.cookie, lang: store.settings.lang, location: store.settings.location });
// Diagnostic log (Settings folder → soncle.log) so playback failures can be inspected.
const logPath = path.join(dataDir, 'soncle.log');
try { if (fs.existsSync(logPath) && fs.statSync(logPath).size > 2e6) fs.rmSync(logPath); } catch {}
const flog = (...a) => { const line = `[${new Date().toISOString()}] ${a.map((x) => (x instanceof Error ? x.stack : typeof x === 'string' ? x : JSON.stringify(x))).join(' ')}\n`; try { fs.appendFileSync(logPath, line); } catch {} if (!app.isPackaged) process.stdout.write(line); };
potoken.setLogger((m) => flog('[potoken]', m));
yt.setPoTokenProvider((id) => potoken.mint(id));
if (yt.setLogger) yt.setLogger((m) => flog('[yt]', m));
flog(`Soncle ${app.getVersion()} starting (electron ${process.versions.electron})`);
for (const m of carryLog) flog('[carry-over]', m);   // what was copied, or why not
// Phase 0 probe: SONCLE_METRICS=1 logs CPU and memory of every process every 10 s (dev only).
if (process.env.SONCLE_METRICS) setInterval(() => {
  try {
    const m = app.getAppMetrics();
    const cpu = m.reduce((a, p) => a + (p.cpu?.percentCPUUsage || 0), 0);
    const mem = m.reduce((a, p) => a + (p.memory?.workingSetSize || 0), 0);
    const heap = process.memoryUsage();
    flog('[metrics]', `main heap ${(heap.heapUsed / 1048576).toFixed(0)}/${(heap.rss / 1048576).toFixed(0)} MB •`, `cpu ${cpu.toFixed(1)}% • ram ${(mem / 1024).toFixed(0)} MB •`, m.map((p) => `${p.serviceName ? p.serviceName.replace(/^.*\./, '') : p.type}:${(p.memory?.workingSetSize / 1024 | 0)}MB/${(p.cpu?.percentCPUUsage || 0).toFixed(1)}%`).join(' '));
  } catch {}
}, +(process.env.SONCLE_METRICS_MS || 10000));
local.init(dataDir, (m) => flog('[local]', m));
autoeq.init(dataDir, (u) => net.fetch(u), (m) => flog('[autoeq]', m));
if (process.env.SONCLE_MOCK) import(process.env.SONCLE_MOCK).then((m) => yt.__setClient(m.client));

const discord = new DiscordRPC();

// ---------- window ----------
let win = null;
let tray = null;
let quitting = false;
let lastState = { playing: false };
const icon = nativeImage.createFromPath(path.join(ROOT, 'renderer', 'icon.png'));
const winBuild = process.platform === 'win32' ? Number(os.release().split('.')[2] || 0) : 0;
const micaSupported = winBuild >= 22621;
const useMica = () => micaSupported && store.settings.mica;
const TITLEBAR_H = 48;

function themeSource() {
  const t = store.settings.theme;
  return t === 'light' ? 'light' : t === 'system' ? 'system' : 'dark';
}

function createWindow() {
  nativeTheme.themeSource = themeSource();
  const bounds = store.windowBounds || { width: 1320, height: 840 };
  win = new BrowserWindow({
    ...bounds,
    minWidth: 940,
    minHeight: 600,
    backgroundColor: useMica() ? '#00000000' : (nativeTheme.shouldUseDarkColors ? '#0e0e13' : '#fbf8ff'),
    backgroundMaterial: useMica() ? 'mica' : undefined,
    title: 'Soncle',
    icon,
    show: false,
    titleBarStyle: 'hidden',
    titleBarOverlay: process.platform === 'darwin' ? true : { color: '#00000000', symbolColor: nativeTheme.shouldUseDarkColors ? '#e4e1e9' : '#1b1b21', height: TITLEBAR_H },
    trafficLightPosition: { x: 16, y: 16 },
    webPreferences: {
      preload: path.join(__dirname, 'preload.cjs'),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true,
      backgroundThrottling: false,
      autoplayPolicy: 'no-user-gesture-required',
      spellcheck: false
    }
  });
  if (store.maximized) win.maximize();
  // Only expose audio output device names. Microphone/camera/geolocation/etc. are
  // denied outright (the app never captures input devices).
  win.webContents.session.setPermissionCheckHandler((_wc, perm) => perm === 'speaker-selection' || perm === 'clipboard-sanitized-write');
  win.webContents.session.setPermissionRequestHandler((_wc, perm, cb) => cb(perm === 'speaker-selection' || perm === 'clipboard-sanitized-write'));
  win.once('ready-to-show', () => win.show());
  win.loadFile(path.join(ROOT, 'renderer', 'index.html'));
  const remember = () => {
    if (!win || win.isDestroyed() || miniPrev) return;
    store.maximized = win.isMaximized();
    if (!store.maximized && !win.isMinimized() && win.isVisible()) store.windowBounds = win.getBounds();
    save();
  };
  win.on('resize', remember);
  win.on('move', remember);
  win.on('close', (e) => {
    remember();
    if (store.settings.closeToTray && !quitting) {
      e.preventDefault();
      win.hide();
      ensureTray();
    }
  });
  win.on('enter-full-screen', () => send('win:state', { fullscreen: true }));
  win.on('leave-full-screen', () => send('win:state', { fullscreen: false }));
  win.webContents.setWindowOpenHandler(({ url }) => {
    if (/^https?:/.test(url)) shell.openExternal(url);
    return { action: 'deny' };
  });
  const devMode = !app.isPackaged || !!process.env.SONCLE_DEVTOOLS;
  win.webContents.on('before-input-event', (e, input) => {
    if (input.type !== 'keyDown') return;
    if (devMode && (input.key === 'F12' || (input.control && input.shift && input.key.toLowerCase() === 'i'))) win.webContents.toggleDevTools();
    if (input.key === 'F11') win.setFullScreen(!win.isFullScreen());
  });
  updateThumbar(false);
  // Renderer warnings/errors (play failures, early endings) go to soncle.log.
  if (!process.env.SONCLE_SHOT) win.webContents.on('console-message', (e) => { if (e.message && /warn|error/i.test(String(e.level))) flog('[renderer]', e.message.slice(0, 500)); });
  if (process.env.SONCLE_SHOT) devHarness();
}

function send(ch, data) { if (win && !win.isDestroyed()) win.webContents.send(ch, data); }

function thumbIcon(svgPath, size = 32) {
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${size}" height="${size}" viewBox="0 0 24 24"><path fill="white" d="${svgPath}"/></svg>`;
  return nativeImage.createFromDataURL('data:image/svg+xml;base64,' + Buffer.from(svg).toString('base64'));
}
const ICONS = {
  prev: 'M6 6h2v12H6zm3.5 6l8.5 6V6z',
  next: 'M6 18l8.5-6L6 6v12zM16 6v12h2V6h-2z',
  play: 'M8 5v14l11-7z',
  pause: 'M6 19h4V5H6v14zm8-14v14h4V5h-4z'
};
function updateThumbar(playing) {
  if (process.platform !== 'win32' || !win) return;
  try {
    const act = (a) => () => send('media-action', a);
    win.setThumbarButtons([
      { tooltip: 'Previous', icon: thumbIcon(ICONS.prev), click: act('prev') },
      { tooltip: playing ? 'Pause' : 'Play', icon: thumbIcon(playing ? ICONS.pause : ICONS.play), click: act('toggle') },
      { tooltip: 'Next', icon: thumbIcon(ICONS.next), click: act('next') }
    ]);
  } catch {}
}

function showWindow() {
  if (!win) return createWindow();
  if (win.isMinimized()) win.restore();
  win.show();
  win.focus();
}
function ensureTray() {
  if (tray) return updateTray();
  tray = new Tray(icon.resize({ width: 16, height: 16 }));
  tray.setToolTip('Soncle');
  tray.on('click', showWindow);
  updateTray();
}
function updateTray() {
  if (!tray) return;
  const t = lastState.track;
  tray.setToolTip(t ? `${t.title} — ${(t.artists || []).map((a) => a.name).join(', ')}`.slice(0, 120) : 'Soncle');
  tray.setContextMenu(Menu.buildFromTemplate([
    ...(t ? [{ label: t.title.slice(0, 60), enabled: false }, { type: 'separator' }] : []),
    { label: lastState.playing ? 'Pause' : 'Play', click: () => send('media-action', 'toggle') },
    { label: 'Next', click: () => send('media-action', 'next') },
    { label: 'Previous', click: () => send('media-action', 'prev') },
    { type: 'separator' },
    { label: 'Show Soncle', click: showWindow },
    { label: 'Quit', click: () => { quitting = true; app.quit(); } }
  ]));
}

// ---------- downloads ----------
fs.mkdirSync(dlDir, { recursive: true });
const dlQueue = [];
const dlActive = new Map();
const extFor = (mime) => (/mp4|m4a/.test(mime || '') ? 'm4a' : 'webm');
function downloadedFile(id) {
  const d = store.downloads[id];
  if (!d) return null;
  const f = path.join(dlDir, d.file);
  return fs.existsSync(f) ? { ...d, path: f } : null;
}
function enqueueDownload(tracks) {
  let n = 0;
  for (const t of tracks || []) {
    if (!t?.id || local.isLocalId(t.id) || downloadedFile(t.id) || dlActive.has(t.id) || dlQueue.some((x) => x.id === t.id)) continue;
    dlQueue.push(t);
    n++;
    send('dl:progress', { id: t.id, progress: 0, state: 'queued' });
  }
  pumpDownloads();
  return n;
}
function pumpDownloads() {
  while (dlActive.size < 2 && dlQueue.length) {
    const t = dlQueue.shift();
    const job = { cancel: false };
    dlActive.set(t.id, job);
    runDownload(t, job)
      .then(() => send('dl:progress', { id: t.id, progress: 1, state: 'done' }))
      .catch((e) => { if (!job.cancel) flog('download failed', t.id, e.message); send('dl:progress', { id: t.id, state: job.cancel ? 'cancelled' : 'error', error: e.message }); })
      .finally(() => { dlActive.delete(t.id); pumpDownloads(); });
  }
}
async function runDownload(t, job) {
  const quality = store.settings.quality === 'low' ? 'low' : 'best';
  const s = await yt.resolveStream(t.id, { quality, fetchImpl: net.fetch });
  const file = `${t.id}.${extFor(s.mime)}`;
  const tmp = path.join(dlDir, file + '.part');
  const out = fs.createWriteStream(tmp);
  let pos = 0;
  try {
    if (s.client === 'FAKE') {
      await new Promise((res) => out.end(res));
      fs.copyFileSync(process.env.SONCLE_FAKE_AUDIO, tmp);
      pos = fs.statSync(tmp).size;
    } else {
      // Same chunked, self-healing fetch as playback (switches client if a chunk is refused).
      const res = await proxyStream({
        stream: s, range: null, fetchImpl: net.fetch,
        log: (m) => flog('[download]', t.id, m),
        reresolve: (prev, attempt) => { yt.invalidateStream(t.id); return yt.resolveStreamRotating(t.id, attempt === 0 ? prev.client : null, { quality, fetchImpl: net.fetch }); }
      });
      if (!res.ok) throw new Error(await res.text() || 'HTTP ' + res.status);
      const reader = res.body.getReader();
      let lastSent = 0;
      for (;;) {
        if (job.cancel) { try { await reader.cancel(); } catch {} throw new Error('cancelled'); }
        const { done, value } = await reader.read();
        if (done) break;
        await new Promise((ok, bad) => out.write(value, (e) => (e ? bad(e) : ok())));
        pos += value.length;
        if (Date.now() - lastSent > 200) { lastSent = Date.now(); send('dl:progress', { id: t.id, progress: s.length ? pos / s.length : 0, state: 'downloading' }); }
      }
      await new Promise((res2) => out.end(res2));
      if (s.length && pos < s.length) throw new Error('download incomplete');
    }
    if (job.cancel) throw new Error('cancelled');
    fs.renameSync(tmp, path.join(dlDir, file));
  } catch (e) {
    out.destroy();
    try { fs.unlinkSync(tmp); } catch {}
    throw e;
  }
  if (job.cancel) { try { fs.unlinkSync(path.join(dlDir, file)); } catch {} throw new Error('cancelled'); }
  store.downloads[t.id] = { track: t, file, size: pos, mime: s.mime, loudnessDb: s.loudnessDb, at: Date.now() };
  save();
}
function removeDownload(id) {
  const job = dlActive.get(id);
  if (job) job.cancel = true;
  const qi = dlQueue.findIndex((x) => x.id === id);
  if (qi >= 0) dlQueue.splice(qi, 1);
  const d = store.downloads[id];
  if (d) { try { fs.unlinkSync(path.join(dlDir, d.file)); } catch {} delete store.downloads[id]; save(); }
  send('dl:progress', { id, state: 'removed' });
  return true;
}

// ---------- streaming protocol ----------
function serveFile(file, mime, range) {
  const size = fs.statSync(file).size;
  let start = 0, end = size - 1;
  if (range) {
    const m = /bytes=(\d*)-(\d*)/.exec(range);
    if (m) { if (m[1]) start = Number(m[1]); if (m[2]) end = Math.min(size - 1, Number(m[2])); }
  }
  const body = Readable.toWeb(fs.createReadStream(file, { start, end }));
  return new Response(body, {
    status: range ? 206 : 200,
    headers: {
      'Content-Type': (mime || 'audio/webm').split(';')[0],
      'Content-Length': String(end - start + 1),
      'Content-Range': `bytes ${start}-${end}/${size}`,
      'Accept-Ranges': 'bytes',
      'Access-Control-Allow-Origin': '*'
    }
  });
}

async function handleStream(request) {
  const u = new URL(request.url);
  // Strip any cache-buster query string ("?r=...") the renderer appends.
  const videoId = (u.hostname || u.pathname.replace(/^\/+/, '')).replace(/\/$/, '');
  const range = request.headers.get('range');
  if (local.isLocalId(videoId)) {
    if (u.pathname.startsWith('/art')) {
      const art = await local.artwork(videoId);
      if (art) return new Response(art.data, { headers: { 'Content-Type': art.mime, 'Cache-Control': 'max-age=86400', 'Access-Control-Allow-Origin': '*' } });
      return new Response(local.placeholderArt(videoId), { headers: { 'Content-Type': 'image/svg+xml', 'Access-Control-Allow-Origin': '*' } });
    }
    const file = local.pathFor(videoId);
    if (!file) return new Response('File not found — it may have been moved or deleted.', { status: 404 });
    return serveFile(file, local.MIME[path.extname(file).toLowerCase()] || 'audio/mpeg', range);
  }
  const offline = downloadedFile(videoId);
  if (offline) return serveFile(offline.path, offline.mime, range);
  const quality = store.settings.quality;
  let s;
  try { s = await yt.resolveStream(videoId, { quality, fetchImpl: net.fetch }); }
  catch (e) { flog('[stream]', videoId, e.message); return new Response(String(e.message), { status: 502 }); }
  if (s.client === 'FAKE') return serveFile(process.env.SONCLE_FAKE_AUDIO, s.mime, range);
  return proxyStream({
    stream: s, range, fetchImpl: net.fetch, cache: chunkCache, cacheId: videoId,
    log: (m) => flog('[stream]', videoId, m),
    reresolve: (prev, attempt) => { yt.invalidateStream(videoId); return yt.resolveStreamRotating(videoId, attempt === 0 ? prev.client : null, { quality, fetchImpl: net.fetch }); }
  });
}

// ---------- lyrics ----------
const UA_LYRICS = { 'User-Agent': 'Soncle (https://github.com/minuda2009)' };
const cleanTitle = (s) => (s || '').replace(/\s*[\(\[](official|lyric|audio|video|visualizer|hd|4k|mv|remaster|feat\.?|ft\.)[^\)\]]*[\)\]]/gi, '').trim();
async function lrclib(t) {
  const artist = (t.artists || []).map((a) => a.name).filter(Boolean)[0] || '';
  const title = cleanTitle(t.title);
  const q = new URLSearchParams({ track_name: title, artist_name: artist });
  if (t.album?.name) q.set('album_name', t.album.name);
  if (t.duration) q.set('duration', String(Math.round(t.duration)));
  try {
    const r = await net.fetch('https://lrclib.net/api/get?' + q, { headers: UA_LYRICS });
    if (r.ok) {
      const j = await r.json();
      if (j.syncedLyrics || j.plainLyrics) return { synced: j.syncedLyrics || null, plain: j.plainLyrics || null, source: 'LRCLIB' };
    }
  } catch {}
  try {
    const r = await net.fetch('https://lrclib.net/api/search?' + new URLSearchParams({ track_name: title, artist_name: artist }), { headers: UA_LYRICS });
    if (r.ok) {
      const arr = await r.json();
      const best = arr.find((x) => x.syncedLyrics && (!t.duration || Math.abs(x.duration - t.duration) < 5)) || arr.find((x) => x.syncedLyrics) || arr[0];
      if (best) return { synced: best.syncedLyrics || null, plain: best.plainLyrics || null, source: 'LRCLIB' };
    }
  } catch {}
  return null;
}
async function lyrics(t) {
  if (process.env.SONCLE_MOCK) return { synced: Array.from({ length: 14 }, (_, i) => `[00:${String(i * 2).padStart(2, '0')}.00] Line ${i + 1} of the song, la la la`).join('\n'), plain: null, source: 'LRCLIB (mock)' };
  if (local.isLocalId(t.id)) {
    const own = await local.lyrics(t.id).catch(() => null);
    if (own?.synced) return own;
    const a = await lrclib(t);
    return a?.synced ? a : own || a;
  }
  const a = await lrclib(t);
  if (a?.synced) return a;
  try {
    const b = await yt.ytLyrics(t.id);
    if (b?.plain) return { synced: null, plain: b.plain, source: b.source?.replace(/^Source:\s*/i, '') || 'YouTube Music' };
  } catch {}
  return a;
}

// ---------- sign in ----------
const SIGNIN_URL = 'https://accounts.google.com/ServiceLogin?ltmpl=music&service=youtube&passive=true&continue=https%3A%2F%2Fwww.youtube.com%2Fsignin%3Faction_handle_signin%3Dtrue%26next%3Dhttps%253A%252F%252Fmusic.youtube.com%252F';
const isYouTube = (c) => /(^|\.)youtube\.com$/.test(c.domain);
const signedInCookies = (cs) => cs.some((c) => isYouTube(c) && (c.name === 'SAPISID' || c.name === '__Secure-3PAPISID'));
function cookieHeader(cs) {
  // one value per name; the site-wide .youtube.com cookie wins over subdomain copies
  const pick = new Map();
  for (const c of cs.filter(isYouTube)) if (!pick.has(c.name) || c.domain === '.youtube.com') pick.set(c.name, c.value);
  return [...pick].map(([k, v]) => `${k}=${v}`).join('; ');
}
function acceptSignIn(cookies) {
  store.cookie = cookieHeader(cookies);
  save();
  yt.configure({ cookie: store.cookie });
  if (win) { if (win.isMinimized()) win.restore(); win.show(); win.focus(); }
  return true;
}
// Google blocks sign-in inside embedded browser windows, so the real browser is used when there is
// one (see browser-signin.mjs); the in-app window is only a fallback.
async function signIn() {
  const exe = !process.env.SONCLE_EMBEDDED_SIGNIN && findBrowser();
  if (exe) {
    try {
      const cookies = await browserSignIn({ exe, url: SIGNIN_URL, profileDir: path.join(dataDir, 'signin-browser'), isDone: signedInCookies, log: (m) => flog('[signin]', m) });
      return cookies ? acceptSignIn(cookies) : false;
    } catch (e) { flog('[signin] could not start ' + exe + ': ' + e.message); }
  }
  return embeddedSignIn();
}
async function embeddedSignIn() {
  const part = 'persist:ytlogin';
  const loginWin = new BrowserWindow({
    width: 500, height: 740, parent: win, modal: true, title: 'Sign in to YouTube Music', autoHideMenuBar: true, backgroundColor: '#ffffff',
    webPreferences: { partition: part, sandbox: true, contextIsolation: true }
  });
  loginWin.webContents.setUserAgent('Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:140.0) Gecko/20100101 Firefox/140.0');
  loginWin.loadURL(SIGNIN_URL);
  return new Promise((resolve) => {
    let done = false;
    const check = async (url) => {
      if (done || !url.startsWith('https://music.youtube.com')) return;
      const cookies = await session.fromPartition(part).cookies.get({ domain: '.youtube.com' });
      if (!signedInCookies(cookies)) return;
      done = true;
      acceptSignIn(cookies);
      loginWin.close();
      resolve(true);
    };
    loginWin.webContents.on('did-navigate', (_e, url) => check(url));
    loginWin.webContents.on('did-redirect-navigation', (_e, url) => check(url));
    loginWin.on('closed', () => { if (!done) resolve(false); });
  });
}
async function signOut() {
  store.cookie = '';
  save();
  await session.fromPartition('persist:ytlogin').clearStorageData();
  yt.configure({ cookie: '' });
  return true;
}

// ---------- backup ----------
async function backup() {
  const r = await dialog.showSaveDialog(win, { title: 'Back up Soncle library', defaultPath: `soncle-backup-${new Date().toISOString().slice(0, 10)}.json`, filters: [{ name: 'Soncle backup', extensions: ['json'] }] });
  if (r.canceled || !r.filePath) return false;
  // everything except secrets and per-machine state
  const { cookie: _c, session: _s, windowBounds: _wb, maximized: _m, downloads: _d, ...rest } = store;
  fs.writeFileSync(r.filePath, JSON.stringify({ app: BACKUP_MARKER, version: 1, at: Date.now(), ...rest }, null, 1));
  return true;
}
async function restore() {
  const r = await dialog.showOpenDialog(win, { title: 'Restore Soncle backup', properties: ['openFile'], filters: [{ name: 'Soncle backup', extensions: ['json'] }] });
  if (r.canceled || !r.filePaths[0]) return false;
  const data = JSON.parse(fs.readFileSync(r.filePaths[0], 'utf8'));
  if (!isBackupMarker(data.app)) throw new Error('Not a Soncle backup file');   // backups from before the rename are accepted too
  upgradeSettings(data.settings);
  for (const k of ['liked', 'playlists', 'savedAlbums', 'savedPlaylists', 'followedArtists', 'history', 'searchHistory', 'eqPresets']) if (Array.isArray(data[k])) store[k] = data[k];
  for (const k of ['lyricsOffsets', 'deviceProfiles']) if (data[k] && typeof data[k] === 'object') store[k] = data[k];
  if (data.settings) store.settings = { ...defaultSettings, ...data.settings, eq: { ...defaultSettings.eq, ...(data.settings.eq || {}) } };
  applySettings(store.settings);
  save(true);
  setTimeout(() => win?.reload(), 200);
  return true;
}

// ---------- audio devices ----------
let btCache = { at: 0, names: [] };
function btDevices() {
  if (process.platform !== 'win32') return Promise.resolve([]);
  if (Date.now() - btCache.at < 8000) return Promise.resolve(btCache.names);
  const ps = "Get-PnpDevice -Class Bluetooth -PresentOnly -ErrorAction SilentlyContinue | Where-Object { $_.Status -eq 'OK' -and ($_.InstanceId -like 'BTHENUM*' -or $_.InstanceId -like 'BTHLE*') } | Select-Object -ExpandProperty FriendlyName | Sort-Object -Unique";
  return new Promise((resolve) => {
    execFile('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', ps], { timeout: 8000, windowsHide: true }, (err, out) => {
      const names = err ? [] : String(out).split(/\r?\n/).map((x) => x.trim()).filter(Boolean);
      btCache = { at: Date.now(), names };
      resolve(names);
    });
  });
}

// ---------- mini player ----------
let miniPrev = null;
function setMini(on) {
  if (!win) return false;
  if (on && !miniPrev) {
    if (win.isFullScreen()) win.setFullScreen(false);
    miniPrev = { bounds: win.getBounds(), maximized: win.isMaximized() };
    if (miniPrev.maximized) win.unmaximize();
    win.setMinimumSize(340, 150);
    const b = win.getBounds();
    win.setBounds({ x: b.x + b.width - 440, y: b.y + b.height - 190, width: 420, height: 170 }, true);
    win.setAlwaysOnTop(true, 'floating');
    win.setResizable(false);
    win.setMaximizable(false);
  } else if (!on && miniPrev) {
    win.setAlwaysOnTop(false);
    win.setResizable(true);
    win.setMaximizable(true);
    win.setMinimumSize(940, 600);
    win.setBounds(miniPrev.bounds, true);
    if (miniPrev.maximized) win.maximize();
    miniPrev = null;
  }
  return !!miniPrev;
}

// ---------- IPC ----------
function handle(name, fn) {
  ipcMain.handle(name, async (_e, ...args) => {
    try {
      return { ok: true, data: await fn(...args) };
    } catch (e) {
      // full stack in soncle.log so user reports can be traced to a line
      flog(`[${name}]`, (e?.stack || String(e)).split('\n').slice(0, 6).join(' ⏎ '));
      return { ok: false, error: e?.message || String(e) };
    }
  });
}

// ---------- local files ----------
// Radio from a local song: find it on YouTube Music and continue from there.
async function localRadio(id) {
  const t = local.trackFor(id);
  if (!t) return [];
  const r = await yt.search([t.title, t.artists?.[0]?.name].filter(Boolean).join(' '), 'song');
  const first = r.sections.flatMap((s) => s.items || []).find((x) => x.type === 'song');
  if (!first) return [];
  const more = await yt.upNext(first.id);
  return [first, ...more.filter((x) => x.id !== first.id)];
}
handle('local:folders', () => local.folders());
handle('local:addFolder', async () => {
  const r = await dialog.showOpenDialog(win, { title: 'Add a music folder', properties: ['openDirectory', 'multiSelections'] });
  if (r.canceled) return null;
  for (const f of r.filePaths) local.addFolder(f);
  return local.folders();
});
handle('local:removeFolder', (dir) => local.removeFolder(dir));
handle('local:scan', () => local.scan((p) => send('local:progress', p)));
handle('local:tracks', () => local.tracks());
handle('local:open', async (paths) => {
  if (!paths?.length) {
    const r = await dialog.showOpenDialog(win, { title: 'Play files', properties: ['openFile', 'multiSelections'], filters: [{ name: 'Audio', extensions: [...local.AUDIO_EXT].map((x) => x.slice(1)) }, { name: 'All files', extensions: ['*'] }] });
    if (r.canceled) return [];
    paths = r.filePaths;
  }
  return local.openFiles(paths);
});
handle('local:pending', () => local.openFiles(audioArgs(process.argv)));
handle('local:reveal', (id) => { const p = local.pathFor(id); if (p) shell.showItemInFolder(p); return !!p; });
// Files passed on the command line / "Open with Soncle" / dropped on the taskbar icon.
const audioArgs = (argv) => argv.slice(1).filter((a) => !a.startsWith('-') && local.isAudioFile(a) && fs.existsSync(a));
async function openFromArgs(argv) {
  const files = audioArgs(argv);
  if (!files.length || !win) return;
  const tracks = await local.openFiles(files);
  if (tracks.length) send('local:play', tracks);
}

// ---------- import (Spotify / CSV / text) ----------
let importCancel = false;
handle('import:spotify', (link) => spotify.fetchSpotify(link, net.fetch, (p) => send('import:progress', p)));
handle('import:parse', (text, name) => spotify.parseTrackList(text, name));
handle('import:file', async () => {
  const r = await dialog.showOpenDialog(win, { title: 'Import a playlist file', properties: ['openFile'], filters: [{ name: 'Playlist export (CSV / text)', extensions: ['csv', 'txt', 'tsv'] }] });
  if (r.canceled) return null;
  const f = r.filePaths[0];
  const text = fs.readFileSync(f, 'utf8');
  return spotify.parseTrackList(f.endsWith('.tsv') ? text.replace(/\t/g, ',') : text, path.basename(f, path.extname(f)));
});
handle('import:match', async (tracks) => {
  importCancel = false;
  const searchSongs = async (q) => {
    const r = await yt.search(q, 'song');
    return r.sections.flatMap((s) => s.items || []).filter((x) => x.type === 'song');
  };
  return spotify.matchAll(tracks, searchSongs, { onProgress: (p) => send('import:progress', p), isCancelled: () => importCancel });
});
handle('import:cancel', () => { importCancel = true; return true; });

// ---------- flow radio: learned tempo/key/energy per song ----------
const flowPath = path.join(dataDir, 'flow-features.json');
let flowDb = {};
try { flowDb = JSON.parse(fs.readFileSync(flowPath, 'utf8')) || {}; } catch {}
let flowTimer = null;
const flowSave = () => { clearTimeout(flowTimer); flowTimer = setTimeout(() => { try { fs.writeFileSync(flowPath + '.tmp', JSON.stringify(flowDb)); fs.renameSync(flowPath + '.tmp', flowPath); } catch (e) { flog('[flow] save failed', e.message); } }, 1500); };
handle('flow:get', (ids) => Object.fromEntries((ids || []).filter((id) => flowDb[id]).map((id) => [id, flowDb[id]])));
handle('flow:put', (id, f) => { if (id && f && typeof f === 'object') { flowDb[id] = { ...f, at: Date.now() }; flowSave(); } return true; });
handle('flow:count', () => Object.keys(flowDb).length);
handle('sys:power', () => ({ onBattery: powerMonitor.isOnBatteryPower?.() ?? false }));
handle('flow:clear', () => { flowDb = {}; flowSave(); return true; });

handle('yt:home', (chip) => yt.home(chip));
handle('yt:homeMore', () => yt.homeMore());
handle('yt:explore', () => yt.explore());
handle('yt:browse', (id, params) => yt.browse(id, params));
handle('yt:suggestions', (q) => yt.suggestions(q));
handle('yt:search', (q, type) => yt.search(q, type));
handle('yt:searchMore', (q, type) => yt.searchMore(q, type));
handle('yt:album', (id) => yt.album(id));
handle('yt:artist', (id) => yt.artist(id));
handle('yt:playlist', (id, all) => yt.playlist(id, all));
handle('yt:upNext', async (id, pl) => (local.isLocalId(id) ? localRadio(id) : yt.upNext(id, pl)));
handle('yt:radio', (pl, params) => yt.radio(pl, params));
handle('yt:related', (id) => (local.isLocalId(id) ? { sections: [] } : yt.related(id)));
handle('yt:library', () => yt.library());
handle('yt:songInfo', (id) => yt.songInfo(id));
handle('yt:rate', (id, like) => (store.settings.syncLikes && !local.isLocalId(id) ? yt.rate(id, like) : false));
// `lufs`: the song's integrated loudness. YouTube's loudnessDb is relative to its -14 LUFS reference;
// local files carry ReplayGain (reference -18 LUFS; local.mjs stores loudnessDb = -gain).
const ytLufs = (db) => (db != null && isFinite(db) ? -14 + db : null);
handle('yt:prefetch', async (id) => {
  if (local.isLocalId(id)) {
    const t = local.trackFor(id);
    if (!local.pathFor(id)) throw new Error('File not found — it may have been moved or deleted.');
    return { client: 'LOCAL', lufs: t?.loudnessDb != null ? -18 + t.loudnessDb : null, offline: true };
  }
  const off = downloadedFile(id);
  if (off) return { client: 'OFFLINE', lufs: ytLufs(off.loudnessDb), mime: off.mime, offline: true };
  const s = await yt.resolveStream(id, { quality: store.settings.quality, fetchImpl: net.fetch });
  return { client: s.client, lufs: ytLufs(s.loudnessDb), mime: s.mime, bitrate: s.bitrate };
});
handle('lyrics', (t) => lyrics(t));
handle('eq:hpSearch', (q) => autoeq.search(String(q || '').slice(0, 80)));
handle('eq:hpMatch', (model) => autoeq.match(String(model || '').slice(0, 120)).catch(() => null));
handle('eq:hpProfile', (p) => autoeq.profile(p));
handle('auth:signIn', () => signIn());
handle('auth:browser', () => { const exe = !process.env.SONCLE_EMBEDDED_SIGNIN && findBrowser(); return exe ? browserName(exe) : null; });
handle('auth:signOut', () => signOut());
handle('auth:status', () => yt.isSignedIn());
handle('auth:premium', () => yt.premiumStatus());
handle('store:get', () => { const { cookie: _c, ...rest } = store; return rest; }); // the cookie never reaches the renderer
handle('store:set', (key, value) => {
  if (key === 'cookie' || key === 'downloads') return false;
  store[key] = value;
  if (key === 'settings' && value) applySettings(value);
  save();
  return true;
});
handle('dl:add', (tracks) => enqueueDownload(tracks));
handle('dl:remove', (id) => removeDownload(id));
handle('dl:list', () => ({ done: Object.values(store.downloads).filter((d) => fs.existsSync(path.join(dlDir, d.file))).sort((a, b) => b.at - a.at), active: [...dlActive.keys(), ...dlQueue.map((t) => t.id)] }));
handle('dl:size', () => Object.values(store.downloads).reduce((a, d) => a + (d.size || 0), 0));
handle('dl:openFolder', () => shell.openPath(dlDir));
handle('audio:bt', () => btDevices());
handle('win:mini', (on) => setMini(on));
handle('app:backup', () => backup());
handle('app:restore', () => restore());
handle('app:openExternal', (url) => { if (/^https?:\/\//.test(url)) shell.openExternal(url); return true; });
handle('app:licences', () => fs.readFileSync(path.join(ROOT, 'THIRD_PARTY_NOTICES.md'), 'utf8'));
handle('app:info', () => ({ version: app.getVersion(), discord: DISCORD_AVAILABLE, platform: process.platform, mica: useMica(), micaSupported, dark: nativeTheme.shouldUseDarkColors }));
handle('app:clearCache', async () => { await session.defaultSession.clearCache(); fs.rmSync(path.join(dataDir, 'yt-cache-v2'), { recursive: true, force: true }); yt.configure({}); return true; });

let lastSettingsSnapshot = JSON.stringify({ lang: store.settings.lang, location: store.settings.location });
function applySettings(s) {
  const snap = JSON.stringify({ lang: s.lang, location: s.location });
  if (snap !== lastSettingsSnapshot) { lastSettingsSnapshot = snap; yt.configure({ lang: s.lang, location: s.location }); }
  nativeTheme.themeSource = themeSource();
  if (win && process.platform === 'win32' && micaSupported) {
    try { win.setBackgroundMaterial(useMica() ? 'mica' : 'none'); } catch {}
    if (!useMica()) win.setBackgroundColor(nativeTheme.shouldUseDarkColors ? '#0e0e13' : '#fbf8ff');
  }
  discord.setEnabled(!!s.discord);
  if (s.closeToTray) ensureTray();
  else if (tray) { tray.destroy(); tray = null; }
}

// ---------- smart ducking ----------
const ducker = createDucker({
  ownPids: () => { try { return [process.pid, ...app.getAppMetrics().map((m) => m.pid)]; } catch { return [process.pid]; } },
  onChange: (st) => { send('duck', st); flog('[duck]', st.active ? 'other app is playing sound' : 'other app quiet'); },
  log: (m) => flog('[duck]', m)
});
let blocker = null;
ipcMain.on('player:state', (_e, st) => {
  // watch other apps only while music plays (or while we're waiting to resume after a duck-pause)
  if (store.settings.smartDuck !== 'off' && (st.playing || st.duckWait)) ducker.start(); else ducker.stop(st.duckWait ? 600000 : 45000);
  const changed = st.playing !== lastState.playing || st.track?.id !== lastState.track?.id;
  lastState = st;
  if (changed) {
    updateThumbar(!!st.playing);
    updateTray();
    if (win) win.setTitle(st.track && st.playing ? `${st.track.title} • ${(st.track.artists || []).map((a) => a.name).join(', ')}` : 'Soncle');
    // throttle the renderer in the background while nothing plays (timers must stay precise while playing for crossfades)
    try { win?.webContents.setBackgroundThrottling(!st.playing); } catch {}
    if (st.playing && blocker == null) blocker = powerSaveBlocker.start('prevent-app-suspension');
    if (!st.playing && blocker != null) { powerSaveBlocker.stop(blocker); blocker = null; }
  }
  discord.update(st);
});
ipcMain.on('titlebar:color', (_e, c) => {
  try { if (process.platform !== 'darwin') win?.setTitleBarOverlay({ color: '#00000000', symbolColor: c || '#e4e1e9', height: TITLEBAR_H }); } catch {}
});
ipcMain.on('win:fullscreen', () => win?.setFullScreen(!win.isFullScreen()));
ipcMain.on('session:save', (_e, snap) => { store.session = snap; save(); });

// ---------- dev screenshot harness (only with SONCLE_SHOT) ----------
// Dev only: 6 s CPU profile of the renderer, printed as the top functions by self time.
async function profileRenderer(out) {
  const dbg = win.webContents.debugger;
  try { dbg.attach('1.3'); } catch {}
  await dbg.sendCommand('Profiler.enable');
  await dbg.sendCommand('Profiler.setSamplingInterval', { interval: 200 });
  await dbg.sendCommand('Profiler.start');
  await new Promise((r) => setTimeout(r, 6000));
  const { profile } = await dbg.sendCommand('Profiler.stop');
  const dt = profile.timeDeltas.reduce((a, b) => a + b, 0) / 1000;
  const self = new Map(), byId = new Map(profile.nodes.map((nd) => [nd.id, nd]));
  const counts = new Map(); for (const id of profile.samples) counts.set(id, (counts.get(id) || 0) + 1);
  const per = dt / profile.samples.length;
  for (const [id, c] of counts) { const cf = byId.get(id).callFrame; const k = `${cf.functionName || '(anon)'} ${cf.url.split('/').pop()}:${cf.lineNumber + 1}`; self.set(k, (self.get(k) || 0) + c * per); }
  const top = [...self].sort((a, b) => b[1] - a[1]).slice(0, 25);
  out('[profile]', `${dt.toFixed(0)} ms sampled`);
  for (const [k, ms] of top) out('[profile]', `${ms.toFixed(1).padStart(7)} ms  ${k}`);
  dbg.detach();
}
// Test tooling only: prints the renderer's console and each script step's result to stdout so a
// headless run can be checked. Never active in normal use (production logs warnings/errors via flog).
function devHarness() {
  const out = (...a) => process.stdout.write(a.join(' ') + '\n');
  win.webContents.on('console-message', (e) => { const m = e.message; if (!m) return; if (/warn|error/i.test(String(e.level))) flog('[renderer]', m.slice(0, 500)); else out('[renderer]', m); });
  win.webContents.once('did-finish-load', async () => {
    const wait = (ms) => new Promise((r) => setTimeout(r, ms));
    await wait(2500);
    const steps = process.env.SONCLE_SCRIPT ? fs.readFileSync(process.env.SONCLE_SCRIPT, 'utf8').split('\n---\n') : [''];
    let n = 0;
    for (const step of steps) {
      if (step.trim()) { try { const r = await win.webContents.executeJavaScript(step); if (r !== undefined) out('[step]', JSON.stringify(r)); } catch (e) { out('[step error]', e.message); } }
      await wait(Number(process.env.SONCLE_WAIT || 2000));
      if (process.env.SONCLE_PROFILE && Number(process.env.SONCLE_PROFILE) === n) await profileRenderer(out);
      const img = await win.webContents.capturePage();
      fs.writeFileSync(process.env.SONCLE_SHOT.replace('.png', `_${n++}.png`), img.toPNG());
    }
    quitting = true;
    app.quit();
  });
}

// ---------- lifecycle ----------
const gotLock = app.requestSingleInstanceLock();
if (!gotLock) app.quit();
else {
  app.on('second-instance', (_e, argv) => { showWindow(); openFromArgs(argv); });
  app.on('before-quit', () => { quitting = true; discord.clear(); save(true); local.flush(); });
  app.on('will-quit', () => save(true));
  app.whenReady().then(() => {
    // macOS needs a real menu for ⌘C/⌘V/⌘A in inputs and ⌘Q/⌘W to work.
    if (process.platform === 'darwin') {
      Menu.setApplicationMenu(Menu.buildFromTemplate([
        { role: 'appMenu' },
        { role: 'editMenu' },
        { role: 'viewMenu' },
        { role: 'windowMenu' }
      ]));
    } else {
      Menu.setApplicationMenu(null);
    }
    protocol.handle('mstream', handleStream);
    // Only YouTube artwork hosts need their images readable by the canvas colour
    // sampler; rewrite CORS for those hosts alone rather than globally.
    session.defaultSession.webRequest.onHeadersReceived({ urls: ['https://*.googleusercontent.com/*', 'https://*.ytimg.com/*', 'https://*.ggpht.com/*'] }, (details, cb) => {
      const h = details.responseHeaders || {};
      for (const k of Object.keys(h)) if (k.toLowerCase() === 'access-control-allow-origin') delete h[k];
      h['Access-Control-Allow-Origin'] = ['*'];
      cb({ responseHeaders: h });
    });
    if (process.env.SONCLE_MOCK) {
      protocol.handle('https', (req) => {
        const u = new URL(req.url);
        if (u.hostname === 'raw.githubusercontent.com') return net.fetch(req, { bypassCustomProtocolHandlers: true });   // AutoEq
        if (!/googleusercontent|ytimg|ggpht|music\.youtube\.com/.test(u.hostname)) return new Response('blocked', { status: 404 });
        let hsh = 0; for (const ch of u.pathname) hsh = (hsh * 31 + ch.charCodeAt(0)) >>> 0;
        const hue = hsh % 360, hue2 = (hue + 50) % 360;
        const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="400" height="400"><defs><linearGradient id="g" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="hsl(${hue},70%,55%)"/><stop offset="1" stop-color="hsl(${hue2},60%,25%)"/></linearGradient></defs><rect width="400" height="400" fill="url(#g)"/><circle cx="${120 + hsh % 160}" cy="${140 + (hsh >> 3) % 120}" r="70" fill="hsla(${hue2},80%,80%,.35)"/></svg>`;
        return new Response(svg, { headers: { 'content-type': 'image/svg+xml', 'access-control-allow-origin': '*' } });
      });
    }
    createWindow();
    applySettings(store.settings);
    app.on('activate', showWindow);
  });
  app.on('window-all-closed', () => { if (process.platform !== 'darwin') app.quit(); });
}
