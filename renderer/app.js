// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
import { icon, setIcon, hasIcon } from './icons.js';
import { Engine, EQ_BANDS, EQ_PRESETS, PRESET_PREAMP } from './engine.js';
import { classifyOutput, DEVICE_INFO } from './devices.js';
import { planFlow, transition, featLabel, contextFor } from './flow/flow.js';
import { sample as sampleAudio, SR as SAMPLE_RATE } from './flow/sampler.js';
// "1 song" / "2 songs"
const nOf = (n, one, many) => `${n} ${n === 1 ? one : many}`;

const api = window.api;
const $ = (s, r = document) => r.querySelector(s);
const $$ = (s, r = document) => [...r.querySelectorAll(s)];

// ================= helpers =================
function h(tag, props, ...kids) {
  const el = document.createElement(tag);
  if (props) {
    for (const [k, v] of Object.entries(props)) {
      if (v == null || v === false) continue;
      if (k === 'class') el.className = v;
      else if (k === 'style' && typeof v === 'object') { for (const [sk, sv] of Object.entries(v)) sk.startsWith('--') ? el.style.setProperty(sk, sv) : (el.style[sk] = sv); }
      else if (k.startsWith('on') && typeof v === 'function') el.addEventListener(k.slice(2).toLowerCase(), v);
      else if (k.startsWith('data-') || k.startsWith('aria-')) el.setAttribute(k, v);
      else if (k in el) { try { el[k] = v; } catch { el.setAttribute(k, v); } }
      else el.setAttribute(k, v);
    }
  }
  for (const k of kids.flat(Infinity)) {
    if (k == null || k === false) continue;
    el.append(k.nodeType ? k : String(k));
  }
  return el;
}
const fmtTime = (s) => {
  if (!isFinite(s) || s < 0) s = 0;
  s = Math.floor(s);
  const hh = Math.floor(s / 3600), m = Math.floor((s % 3600) / 60), sec = s % 60;
  return hh ? `${hh}:${String(m).padStart(2, '0')}:${String(sec).padStart(2, '0')}` : `${m}:${String(sec).padStart(2, '0')}`;
};
const fmtBytes = (b) => (b > 1e9 ? (b / 1e9).toFixed(2) + ' GB' : b > 1e6 ? (b / 1e6).toFixed(1) + ' MB' : Math.round(b / 1e3) + ' KB');
const artistNames = (t) => (t?.artists || []).map((a) => a.name).filter(Boolean).join(', ');
const thumbOf = (t) => t?.thumb || (t?.id && t.type === 'song' && !/^lc_/.test(t.id) ? `https://i.ytimg.com/vi/${t.id}/hqdefault.jpg` : '');
const bigThumb = (t) => {
  const u = thumbOf(t);
  if (/googleusercontent|ggpht/.test(u)) return u.replace(/=w\d+-h\d+[^/]*$/, '=w1200-h1200-l90-rj').replace(/=s\d+[^/]*$/, '=s1200');
  return u;
};
const uid = () => Math.random().toString(36).slice(2, 10);
const volIcon = (v, muted) => (muted || v <= 0 ? 'volumeOff' : v < 0.33 ? 'volumeMute' : v < 0.66 ? 'volumeLow' : 'volume');
const debounce = (fn, ms) => { let t; return (...a) => { clearTimeout(t); t = setTimeout(() => fn(...a), ms); }; };
const slim = (t) => t && ({ type: 'song', id: t.id, title: t.title, artists: (t.artists || []).map((a) => ({ name: a.name, id: a.id })), album: t.album ? { name: t.album.name, id: t.album.id } : null, duration: t.duration || 0, thumb: t.thumb || '', explicit: !!t.explicit, isVideo: !!t.isVideo, ...(t.local ? { local: true, codec: t.codec || '', albumArtist: t.albumArtist || '' } : {}) });
const shuffleArr = (a) => { a = [...a]; for (let i = a.length - 1; i > 0; i--) { const j = Math.floor(Math.random() * (i + 1)); [a[i], a[j]] = [a[j], a[i]]; } return a; };
const hashHue = (s) => { let x = 0; for (const c of String(s)) x = (x * 31 + c.charCodeAt(0)) >>> 0; return x % 360; };

function img(src, cls = '', alt = '') {
  const i = h('img', { class: 'fade ' + cls, loading: 'lazy', decoding: 'async', alt, draggable: false, referrerPolicy: 'no-referrer' });
  i.onload = () => i.classList.add('loaded');
  i.onerror = () => { i.style.visibility = 'hidden'; };
  if (src) i.src = src;
  return i;
}
function bigIcon(name, size = 64) { const i = icon(name); i.style.width = i.style.height = size + 'px'; return i; }

let toastTimer;
function toast(msg, action) {
  // Focus view: nothing interrupts except what matters (connection, focus itself)
  if (document.body.classList.contains('focus-on') && document.getElementById('focusView') && !/focus|connection|online/i.test(msg)) return;
  const t = $('#toast');
  t.replaceChildren(h('span', null, msg));
  if (action) t.append(h('button', { onclick: () => { action.fn(); t.hidden = true; } }, action.label));
  t.hidden = false;
  t.style.animation = 'none'; void t.offsetWidth; t.style.animation = '';
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => (t.hidden = true), action ? 5000 : 2600);
}

// ================= persistent state =================
let DB = null;
let INFO = { platform: api.platform, mica: false };
const saveTimers = {};
const pendingSaves = new Set();
const persist = (...keys) => keys.forEach((k) => {
  pendingSaves.add(k);
  clearTimeout(saveTimers[k]);
  saveTimers[k] = setTimeout(() => {
    clearTimeout(saveTimers[k]);
    pendingSaves.delete(k);
    api.storeSet(k, DB[k]).catch(() => {});
  }, 250);
});
// Flush any debounced writes immediately (used on unload so edits are not lost).
function flushPersist() {
  for (const k of pendingSaves) {
    clearTimeout(saveTimers[k]);
    api.storeSet(k, DB[k]).catch(() => {});
  }
  pendingSaves.clear();
}
const S = () => DB.settings;
function setSetting(k, v) { DB.settings[k] = v; persist('settings'); }

let LIKED = new Set();
const isLiked = (id) => LIKED.has(id);
function toggleLike(t) {
  if (!t) return;
  const like = !isLiked(t.id);
  if (!like) DB.liked = DB.liked.filter((x) => x.id !== t.id);
  else DB.liked.unshift({ ...slim(t), likedAt: Date.now() });
  LIKED = new Set(DB.liked.map((x) => x.id));
  persist('liked');
  toast(like ? 'Added to Liked songs' : 'Removed from Liked songs');
  refreshLikeButtons();
  renderSidebar();
  api.rate(t.id, like).catch(() => {});
  // Optionally cache the newly liked song for offline listening.
  if (like && S().autoDownloadLiked) downloadTracks([t]);
}
function refreshLikeButtons() {
  const cur = P.current;
  for (const b of [$('#pbLike'), $('#npLike')]) {
    const on = !!(cur && isLiked(cur.id));
    setIcon(b, on ? 'heart' : 'heartOutline');
    b.classList.toggle('liked', on);
  }
  $$('.song[data-id]').forEach((row) => {
    const b = row.querySelector('.like-btn');
    if (!b) return;
    const on = isLiked(row.dataset.id);
    if (b.classList.contains('liked') === on) return;
    setIcon(b, on ? 'heart' : 'heartOutline');
    b.classList.toggle('liked', on);
  });
}
const savedKey = (type) => (type === 'album' ? 'savedAlbums' : type === 'artist' ? 'followedArtists' : 'savedPlaylists');
const isSaved = (item) => DB[savedKey(item.type)].some((x) => x.id === item.id);
function toggleSaved(item) {
  const key = savedKey(item.type);
  if (isSaved(item)) {
    DB[key] = DB[key].filter((x) => x.id !== item.id);
    toast(item.type === 'artist' ? `Unfollowed ${item.title}` : 'Removed from library');
  } else {
    DB[key].unshift({ type: item.type, id: item.id, title: item.title, subtitle: item.subtitle || artistNames(item), thumb: item.thumb, savedAt: Date.now() });
    toast(item.type === 'artist' ? `Following ${item.title}` : 'Saved to library');
  }
  persist(key);
  renderSidebar();
}

// downloads
const DL = { done: new Set(), progress: new Map() };
function isDownloaded(id) { return DL.done.has(id); }
async function downloadTracks(tracks) {
  tracks = (tracks || []).filter((t) => !isLocal(t));
  if (!tracks.length) return toast('Local files are already on your computer');
  tracks = (tracks || []).filter((t) => t?.id && !isDownloaded(t.id));
  if (!tracks.length) return toast('Already downloaded');
  const n = await api.download(tracks.map(slim)).catch(() => 0);
  toast(n > 1 ? `Downloading ${n} songs` : n ? `Downloading "${tracks[0].title}"` : 'Already downloading');
}
async function removeDownload(t) {
  await api.removeDownload(t.id);
  DL.done.delete(t.id);
  refreshDownloadMarks();
  toast('Removed download');
}
function refreshDownloadMarks() {
  $$('.song[data-id]').forEach((row) => {
    const id = row.dataset.id;
    const title = row.querySelector('.s-title');
    const mark = title?.querySelector('.dl-mark');
    const want = isDownloaded(id) ? 'done' : DL.progress.has(id) ? 'busy' : '';
    if ((mark?.dataset.state || '') === want) return;
    mark?.remove();
    if (want) title.prepend(dlMark(id));
  });
  const cur = P.current;
  if (cur) { setIcon($('#npDownload'), isDownloaded(cur.id) ? 'offline' : 'download'); $('#npDownload').classList.toggle('on', isDownloaded(cur.id)); $('#npDownload').hidden = isLocal(cur); }
}
function dlMark(id) {
  if (isDownloaded(id)) return h('span', { class: 'dl-mark', title: 'Downloaded', 'data-state': 'done' }, icon('offline'));
  if (DL.progress.has(id)) return h('span', { class: 'dl-mark', title: 'Downloading…', 'data-state': 'busy' }, h('div', { class: 'spinner', style: { width: '14px', height: '14px', borderWidth: '2px' } }));
  return null;
}

// ================= theming =================
const DEFAULT_ACCENT = '#bfc2f0';
function rgbToHsl(r, g, b) {
  r /= 255; g /= 255; b /= 255;
  const max = Math.max(r, g, b), min = Math.min(r, g, b);
  let hh = 0, s = 0; const l = (max + min) / 2;
  if (max !== min) {
    const d = max - min;
    s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
    hh = max === r ? (g - b) / d + (g < b ? 6 : 0) : max === g ? (b - r) / d + 2 : (r - g) / d + 4;
    hh *= 60;
  }
  return [hh, s * 100, l * 100];
}
function hslToRgb(hh, s, l) {
  s /= 100; l /= 100;
  const k = (n) => (n + hh / 30) % 12, a = s * Math.min(l, 1 - l);
  const f = (n) => l - a * Math.max(-1, Math.min(k(n) - 3, Math.min(9 - k(n), 1)));
  return [Math.round(f(0) * 255), Math.round(f(8) * 255), Math.round(f(4) * 255)];
}
const hexToHsl = (hex) => rgbToHsl(parseInt(hex.slice(1, 3), 16), parseInt(hex.slice(3, 5), 16), parseInt(hex.slice(5, 7), 16));
const hsl = (hh, s, l) => `hsl(${hh.toFixed(1)} ${s.toFixed(1)}% ${l.toFixed(1)}%)`;
const systemDark = matchMedia('(prefers-color-scheme: dark)');
function themeMode() {
  const t = S().theme;
  if (t === 'system') return systemDark.matches ? 'dark' : 'light';
  return t;
}
let lastHue = null;
function applyTheme(hue, sat) {
  lastHue = [hue, sat];
  const mode = themeMode();
  const r = document.documentElement;
  const st = r.style;
  const set = (k, v) => st.setProperty(k, v);
  const s = Math.min(sat, 80);
  r.classList.toggle('light', mode === 'light');
  if (mode === 'light') {
    set('--primary', hsl(hue, Math.max(Math.min(s, 70), 35), 40));
    set('--on-primary', '#ffffff');
    set('--primary-container', hsl(hue, Math.max(Math.min(s, 80), 40), 88));
    set('--on-primary-container', hsl(hue, Math.max(s, 40), 14));
    const ts = Math.min(s, 28);
    set('--bg', hsl(hue, ts, 94));
    set('--surface', hsl(hue, ts, 98.5));
    set('--surface-rgb', hslToRgb(hue, ts, 98.5).join(' '));
    set('--surface-1', hsl(hue, ts, 95.5));
    set('--surface-2', hsl(hue, ts, 93.5));
    set('--surface-3', hsl(hue, ts, 91));
    set('--surface-4', hsl(hue, ts, 86));
    set('--on-surface', hsl(hue, 12, 11));
    set('--on-surface-var', hsl(hue, 8, 32));
    set('--outline', hsl(hue, 6, 48));
    set('--outline-var', hsl(hue, 10, 80));
    set('--error', '#ba1a1a');
    set('--ov', '0 0 0');
    set('--scrim', '60 60 80');
  } else {
    const black = mode === 'black';
    set('--primary', hsl(hue, Math.max(s, 35), 80));
    set('--on-primary', hsl(hue, Math.max(s, 30), 18));
    set('--primary-container', hsl(hue, Math.min(s, 42), 28));
    set('--on-primary-container', hsl(hue, Math.max(s, 40), 90));
    const ts = Math.min(s, 22);
    const L = black ? [0, 3.5, 7, 9, 12.5, 17] : [5.5, 8, 11, 13, 17, 22];
    set('--bg', black ? '#000' : hsl(hue, ts, L[0]));
    set('--surface', hsl(hue, ts, L[1]));
    set('--surface-rgb', hslToRgb(hue, ts, L[1]).join(' '));
    set('--surface-1', hsl(hue, ts, L[2]));
    set('--surface-2', hsl(hue, ts, L[3]));
    set('--surface-3', hsl(hue, ts, L[4]));
    set('--surface-4', hsl(hue, ts, L[5]));
    set('--on-surface', hsl(hue, 12, 90));
    set('--on-surface-var', hsl(hue, 10, 79));
    set('--outline', hsl(hue, 6, 58));
    set('--outline-var', hsl(hue, Math.min(s, 10), 29));
    set('--error', '#ffb4ab');
    set('--ov', '255 255 255');
    set('--scrim', '0 0 0');
  }
  syncTitlebar();
}
function syncTitlebar() {
  const npOpen = !$('#nowPlaying').hidden;
  api.titlebarColor(npOpen ? '#ffffff' : themeMode() === 'light' ? '#1b1b21' : '#e4e1e9');
}
function applyStaticTheme() {
  const [hh, s] = hexToHsl(S().accent || DEFAULT_ACCENT);
  applyTheme(hh, s);
}
const androidMotion = () => S().motionStyle === 'android';
function applyAppearance() {
  // Motion style: "classic" (smooth and glowy, default) or "android" (Material 3 expressive specs)
  document.documentElement.classList.toggle('m-android', androidMotion());
  document.body.classList.toggle('mica', !!(INFO.micaSupported && S().mica));
  document.body.classList.toggle('compact', S().density === 'compact');
  if (S().dynamicColor && P.current) themeFromTrack(P.current, true); else applyStaticTheme();
  applyLyricsStyle();
}
systemDark.addEventListener('change', () => { if (S().theme === 'system') lastHue ? applyTheme(...lastHue) : applyStaticTheme(); });

// Bounded LRU helper so in-memory caches cannot grow without limit.
function makeLru(max) {
  const m = new Map();
  return {
    has: (k) => m.has(k),
    get: (k) => { if (!m.has(k)) return undefined; const v = m.get(k); m.delete(k); m.set(k, v); return v; },
    set: (k, v) => { if (m.has(k)) m.delete(k); m.set(k, v); if (m.size > max) m.delete(m.keys().next().value); }
  };
}
const colorCache = makeLru(300);
async function extractColor(url) {
  if (colorCache.has(url)) return colorCache.get(url);
  const im = new Image();
  im.crossOrigin = 'anonymous';
  im.referrerPolicy = 'no-referrer';
  im.src = url;
  await im.decode();
  const c = document.createElement('canvas');
  c.width = c.height = 48;
  const ctx = c.getContext('2d', { willReadFrequently: true });
  ctx.drawImage(im, 0, 0, 48, 48);
  const d = ctx.getImageData(0, 0, 48, 48).data;
  const buckets = new Map();
  for (let i = 0; i < d.length; i += 4) {
    const [hh, s, l] = rgbToHsl(d[i], d[i + 1], d[i + 2]);
    if (l < 12 || l > 92) continue;
    const key = Math.round(hh / 15);
    const w = (s / 100) ** 1.5 * (1 - Math.abs(l - 50) / 60);
    const b = buckets.get(key) || { w: 0, h: 0, s: 0 };
    b.w += w; b.h += hh * w; b.s += s * w;
    buckets.set(key, b);
  }
  let best = null;
  for (const b of buckets.values()) if (!best || b.w > best.w) best = b;
  const res = best && best.w > 2 ? [best.h / best.w, best.s / best.w] : null;
  colorCache.set(url, res);
  return res;
}
async function themeFromTrack(t) {
  if (!S().dynamicColor || !t) return applyStaticTheme();
  try {
    const c = await extractColor(thumbOf(t));
    if (P.current?.id !== t.id) return;
    if (c) applyTheme(c[0], Math.max(c[1], 25)); else applyStaticTheme();
  } catch { applyStaticTheme(); }
}
function applyLyricsStyle() {
  const r = document.documentElement.style;
  r.setProperty('--ly-size', (S().lyricsSize || 30) + 'px');
  r.setProperty('--ly-align', S().lyricsAlign === 'center' ? 'center' : 'left');
  r.setProperty('--ly-origin', S().lyricsAlign === 'center' ? 'center' : 'left');
  r.setProperty('--ly-pad', S().lyricsAlign === 'center' ? '24px' : '11px');
  r.setProperty('--ly-justify', S().lyricsAlign === 'center' ? 'center' : 'flex-start');
}

// ================= router =================
const nav = { stack: [], pos: -1, token: 0 };
const main = $('#main');
const routeKey = (r) => r.name + ':' + JSON.stringify(r.args || {});
// "What is playing" keys: the collection a queue came from, so pages, cards and the sidebar can
// show it as playing (bars on the cover, Pause instead of Play) — like Android.
const normPl = (id) => String(id || '').replace(/^VL/, '');
function srcKey(it) {
  if (!it) return null;
  switch (it.type) {
    case 'album': return 'album:' + it.id;
    case 'playlist': return 'playlist:' + normPl(it.id);
    case 'local': return 'local:' + it.id;
    case 'artist': return 'artist:' + it.id;
    case 'radio': return 'radio:' + it.id;
    case 'liked': return 'liked';
    default: return null;
  }
}
function pageKey(r = nav.stack[nav.pos]) {
  if (!r) return null;
  const a = r.args || {};
  switch (r.name) {
    case 'album': return 'album:' + a.id;
    case 'playlist': return 'playlist:' + normPl(a.id);
    case 'local': return 'local:' + a.id;
    case 'liked': return 'liked';
    case 'artist': return 'artist:' + a.id;
    case 'files': return 'files' + (a.album ? '|al:' + a.album : '') + (a.artist ? '|ar:' + a.artist : '');
    case 'library': return a.tab === 'downloads' ? 'downloads' : a.tab === 'songs' ? 'liked' : routeKey(r);
    default: return routeKey(r);
  }
}
const isSourcePlaying = (key) => !!key && P.sourceKey === key;
// Play button for a collection page: turns into Pause while this collection is the one playing.
function playBtn(onPlay, { cls = 'btn filled', disabled = false, label = 'Play', key = pageKey(), ic = label === 'Shuffle' ? 'shuffle' : 'play' } = {}) {
  const b = h('button', { class: cls + ' src-btn', disabled, 'data-src-btn': key || '', 'data-label': label, 'data-ic': ic, onclick: () => (isSourcePlaying(key) && P.current ? togglePlay() : onPlay()) });
  drawSrcBtn(b);
  return b;
}
function drawSrcBtn(b) {
  const on = isSourcePlaying(b.dataset.srcBtn) && P.current;
  const want = on && (P.playing || P.loading) ? 'pause' : 'play';
  if (b.dataset.state === want) return;
  b.dataset.state = want;
  b.replaceChildren(icon(want === 'pause' ? 'pause' : b.dataset.ic || 'play'), want === 'pause' ? 'Pause' : b.dataset.label);
}
function refreshSourceMarks() {
  for (const el of document.querySelectorAll('[data-src]')) {
    const on = isSourcePlaying(el.dataset.src);
    el.classList.toggle('src-on', on);
    el.classList.toggle('src-playing', on && (P.playing || P.loading));
  }
  document.querySelectorAll('[data-src-btn]').forEach(drawSrcBtn);
}
const songsLabel = (n) => `${n} song${n === 1 ? '' : 's'}`;
// where "Playing from …" links back to
function openSource(key = P.sourceKey) {
  if (!key) return false;
  const [kind, ...rest] = key.split(':'); const id = rest.join(':');
  const r = kind === 'album' ? ['album', { id }] : kind === 'playlist' ? ['playlist', { id: id.startsWith('PL') || id.startsWith('RD') || id.startsWith('OLAK') ? id : 'VL' + id }] : kind === 'local' ? ['local', { id }] : kind === 'liked' ? ['liked', {}]
    : kind === 'artist' ? ['artist', { id }] : key === 'files' ? ['files', {}] : key === 'downloads' ? ['library', { tab: 'downloads' }] : null;
  if (!r) return false;
  if (!$('#nowPlaying').hidden) closeNowPlaying();
  go(r[0], r[1]);
  return true;
}
const canOpenSource = (key = P.sourceKey) => !!key && /^(album|playlist|local|artist):|^(liked|files|downloads)$/.test(key);
function sourceLabel() {
  if (!P.source) return null;
  return canOpenSource() ? h('span', { class: 'lnk', title: 'Go to ' + P.source, onclick: (e) => { e.stopPropagation(); openSource(); } }, P.source) : P.source;
}
function updateNpFrom() {
  const el = $('#npFrom');
  if (!el) return;
  el.replaceChildren(...(P.source ? [h('div', { class: 'np-from-k' }, 'Playing from'), h('div', { class: 'np-from-v' }, sourceLabel())] : []));
}
const nowBars = () => h('div', { class: 'now-bars' }, h('i'), h('i'), h('i'));
const withBars = (el) => { el.style.position = 'relative'; el.append(nowBars()); return el; };
function go(name, args = {}, { replace = false } = {}) {
  const cur = nav.stack[nav.pos];
  if (cur) cur.scroll = main.scrollTop;
  const entry = { name, args };
  if (cur && routeKey(cur) === routeKey(entry) && !replace) { main.scrollTo({ top: 0, behavior: 'smooth' }); return; }
  if (replace && cur) nav.stack[nav.pos] = entry;
  else { nav.stack = nav.stack.slice(0, nav.pos + 1); nav.stack.push(entry); nav.pos++; }
  if (!$('#nowPlaying').hidden) closeNowPlaying();
  render(entry, false, false, 'fwd');
}
function back() { if (nav.pos <= 0) return; nav.stack[nav.pos].scroll = main.scrollTop; nav.pos--; render(nav.stack[nav.pos], false, true, 'back'); }
function forward() { if (nav.pos >= nav.stack.length - 1) return; nav.stack[nav.pos].scroll = main.scrollTop; nav.pos++; render(nav.stack[nav.pos], false, true, 'fwd'); }
const rerender = () => render(nav.stack[nav.pos], true, true);
function updateNavButtons() {
  $('#navBack').disabled = nav.pos <= 0;
  $('#navFwd').disabled = nav.pos >= nav.stack.length - 1;
  const top = nav.stack[nav.pos];
  $$('.nav-item').forEach((a) => {
    const on = !!top && (a.dataset.route === top.name || (a.dataset.route === 'settings' && top.name === 'equalizer') || (a.dataset.route === 'library' && ['liked', 'local'].includes(top.name)));
    a.classList.toggle('active', on);
    // Android swaps outlined → filled icons for the selected destination
    const ni = a.querySelector('.ni-icon');
    if (ni) setIcon(ni, on && hasIcon(ni.dataset.icon + 'Filled') ? ni.dataset.icon + 'Filled' : ni.dataset.icon);
  });
  $$('.nav-pl').forEach((a) => a.classList.toggle('active', !!top && a.dataset.key === routeKey(top)));
}

const VIEWS = {};
// Page change: Android slides the new page in by 1/8 of the width on a spring while the old one
// slides the other way and fades (fade 200 ms, slide ~420 ms). The old page is frozen in an
// overlay so the scroll container can reset underneath it.
function pageOut(old, dir) {
  const r = main.getBoundingClientRect();
  const layer = h('div', { class: 'pg-layer', style: { left: r.left + 'px', top: r.top + 'px', width: r.width + 'px', height: r.height + 'px' } });
  old.removeAttribute('id');
  old.style.transform = `translateY(${-main.scrollTop}px)`;
  const inner = h('div', { class: 'pg-out ' + dir }, old);
  layer.append(inner);
  document.body.append(layer);
  setTimeout(() => layer.remove(), 440);
}
async function render(entry, refresh = false, restoring = false, dir = null) {
  const tok = ++nav.token;
  closeMenu();
  hideSuggest();
  const view = h('div', { id: 'view' });
  const oldView = $('#view');
  document.querySelectorAll('.pg-layer').forEach((l) => l.remove());
  const calm = matchMedia('(prefers-reduced-motion: reduce)').matches;
  if (dir && !calm && androidMotion()) { oldView.replaceWith(view); pageOut(oldView, dir); view.classList.add('pg-in', dir); setTimeout(() => view.classList.remove('pg-in', dir), 460); }
  else { oldView.replaceWith(view); if (dir && !calm) view.classList.add('pg-classic'); }
  if (!restoring) main.scrollTop = 0;
  updateNavButtons();
  const fn = VIEWS[entry.name];
  if (!fn) return;
  const ctx = {
    entry, view, refresh,
    alive: () => tok === nav.token,
    cache: (key, loader) => {
      if (!refresh && entry.data && entry.data[key] !== undefined) return Promise.resolve(entry.data[key]);
      return loader().then((d) => { entry.data = entry.data || {}; entry.data[key] = d; return d; });
    }
  };
  try {
    await fn(ctx, entry.args || {});
    if (ctx.alive()) refreshSourceMarks();
    if (restoring && entry.scroll && ctx.alive()) requestAnimationFrame(() => (main.scrollTop = entry.scroll));
  } catch (e) {
    if (!ctx.alive()) return;
    console.error(e);
    view.replaceChildren(errorBox(e, () => render(entry, true)));
  }
}
function errorBox(e, retry) {
  return h('div', { class: 'error-box' }, icon('error'),
    h('h3', null, navigator.onLine ? "Couldn't load this page" : "You're offline"),
    h('div', null, navigator.onLine ? 'YouTube Music returned an error.' : 'Downloaded songs are still available in Library → Downloads.'),
    h('pre', null, String(e?.message || e).replace(/^Error invoking remote method '[^']+': (Error: )?/, '')),
    retry && h('button', { class: 'btn tonal', onclick: retry }, 'Try again'));
}
const loading = () => h('div', { class: 'center' }, h('div', { class: 'spinner' }));
function skeletonPage() {
  const sec = () => h('div', { class: 'section' },
    h('div', { class: 'skel', style: { width: '220px', height: '26px', marginBottom: '14px' } }),
    h('div', { class: 'row-scroll' }, Array.from({ length: 7 }, () => h('div', null, h('div', { class: 'skel', style: { aspectRatio: '1', borderRadius: '14px' } }), h('div', { class: 'skel', style: { height: '14px', marginTop: '10px', width: '80%' } })))));
  return h('div', null, sec(), sec(), sec());
}

// ================= item actions =================
async function tracksFor(item) {
  switch (item.type) {
    case 'album': return (await api.album(item.id)).tracks;
    case 'playlist': return (await api.playlist(item.id, true)).tracks;
    case 'radio': return api.radio(item.id, item.params);
    case 'artist': {
      const a = await api.artist(item.id);
      if (a.shuffleId) return api.radio(a.shuffleId);
      if (a.radioId) return api.radio(a.radioId);
      const songs = a.sections.find((s) => s.layout === 'list');
      return songs ? songs.items.filter((i) => i.type === 'song') : [];
    }
    case 'local': return DB.playlists.find((p) => p.id === item.id)?.tracks || [];
    case 'liked': return DB.liked;
    case 'song': return [item];
    default: return [];
  }
}
async function playItem(item, { shuffle = false } = {}) {
  if (item.type === 'song') return playSingle(item);
  try {
    toast('Loading…');
    const tracks = await tracksFor(item);
    if (!tracks.length) return toast('Nothing to play');
    playList(tracks, 0, { shuffle, source: item.title || '', key: srcKey(item) });
    $('#toast').hidden = true;
  } catch (e) { toast("Couldn't load: " + shortErr(e.message)); }
}
function openItem(item) {
  switch (item.type) {
    case 'album': return go('album', { id: item.id });
    case 'playlist': return go('playlist', { id: item.id });
    case 'artist': return item.id && go('artist', { id: item.id });
    case 'browse': case 'mood': return go('browse', { id: item.id, params: item.params, title: item.title });
    case 'radio': return playItem(item);
    case 'song': return playSingle(item);
  }
}

// ================= components =================
const explicitBadge = (t) => (t.explicit ? h('span', { class: 'explicit', title: 'Explicit' }, 'E') : null);
function artistLinks(artists, fallback = '') {
  const list = (artists || []).filter((a) => a.name);
  if (!list.length) return [fallback];
  const out = [];
  list.forEach((a, i) => {
    if (i) out.push(', ');
    out.push(a.id ? h('span', { class: 'lnk', onclick: (e) => { e.stopPropagation(); go('artist', { id: a.id }); } }, a.name) : a.name);
  });
  return out;
}
function card(item) {
  const playBtn = ['album', 'playlist', 'radio', 'song'].includes(item.type)
    ? h('button', { class: 'play-over', title: 'Play', onclick: (e) => { e.stopPropagation(); if (isSourcePlaying(srcKey(item)) && P.current) togglePlay(); else playItem(item); } }, icon('play'), icon('pause', 'when-playing')) : null;
  const key = srcKey(item);
  const on = isSourcePlaying(key);
  return h('div', { class: 'card ' + item.type + (item.isVideo ? ' wide' : '') + (on ? ' src-on' : '') + (on && P.playing ? ' src-playing' : ''), 'data-src': key || null, onclick: () => openItem(item), oncontextmenu: (e) => { e.preventDefault(); itemMenu(e, item); } },
    h('div', { class: 'art' }, img(thumbOf(item)), key ? nowBars() : null, playBtn),
    h('div', { class: 'ct', title: item.title }, explicitBadge(item), h('span', null, item.title)),
    h('div', { class: 'cs' }, item.subtitle || artistNames(item)));
}
function leadContent(on) {
  return [h('div', { class: 'bars' }, h('i'), h('i'), h('i')), icon(on && P.playing ? 'pause' : 'play')];
}
function songRow(t, { list, index, showAlbum = false, showThumb = true, onRemove, queueIdx, source } = {}) {
  // in the queue a song can appear twice: only the copy at the playing position is "playing"
  const playing = queueIdx != null ? queueIdx === P.idx && !!P.current : P.current?.id === t.id;
  const lead = h('div', { class: 's-lead' + (showThumb ? '' : ' nothumb') },
    showThumb ? img(thumbOf(t)) : h('div', { class: 'num' }, String((index ?? 0) + 1)),
    h('div', { class: 'hover-play' }, leadContent(playing)));
  const sub = [...artistLinks(t.artists, t.subtitle || '')];
  if (!showAlbum && t.album?.name && t.album.name !== artistNames(t)) sub.push(' • ', h('span', { class: 'lnk', onclick: (e) => { e.stopPropagation(); t.album.id && go('album', { id: t.album.id }); } }, t.album.name));
  if (t.plays) sub.push(' • ' + t.plays);
  const liked = isLiked(t.id);
  const likeBtn = h('button', { class: 'icon-btn small like-btn' + (liked ? ' liked' : ''), title: 'Like', onclick: (e) => { e.stopPropagation(); toggleLike(t); } }, icon(liked ? 'heart' : 'heartOutline'));
  const moreBtn = h('button', { class: 'icon-btn small', title: 'More', onclick: (e) => { e.stopPropagation(); songMenu(e, t, { onRemove, queueIdx }); } }, icon('more'));
  return h('div', {
    class: 'song' + (playing ? ' playing' : '') + (showAlbum ? ' has-album' : ''),
    'data-id': t.id,
    onclick: () => {
      if (P.current?.id === t.id && queueIdx == null) return togglePlay();
      if (queueIdx != null) return queueIdx === P.idx ? togglePlay() : playAt(queueIdx);
      if (list) playList(list, index, { source: source || '' }); else playSingle(t);
    },
    ondblclick: (e) => e.preventDefault(),
    oncontextmenu: (e) => { e.preventDefault(); songMenu(e, t, { onRemove, queueIdx }); }
  },
    lead,
    h('div', { class: 's-main' },
      h('div', { class: 's-title', title: t.title }, dlMark(t.id), explicitBadge(t), h('span', null, t.title)),
      h('div', { class: 's-sub' }, sub)),
    showAlbum ? h('div', { class: 's-album' }, t.album?.name ? h('span', { class: 'lnk', onclick: (e) => { e.stopPropagation(); t.album.id && go('album', { id: t.album.id }); } }, t.album.name) : '') : null,
    h('div', { class: 's-end' }, likeBtn, h('span', { class: 's-dur' }, t.duration ? fmtTime(t.duration) : ''), moreBtn));
}
function sectionHead(title, { strap, thumb, onMore, scroller, actions } = {}) {
  const right = h('div', { class: 'sh-actions' });
  if (actions) right.append(...actions);
  if (onMore) right.append(h('button', { class: 'text-btn', onclick: onMore }, 'More'));
  if (scroller) right.append(
    h('button', { class: 'scroll-btn', title: 'Scroll left', onclick: () => scroller.scrollBy({ left: -scroller.clientWidth * 0.8 }) }, icon('left')),
    h('button', { class: 'scroll-btn', title: 'Scroll right', onclick: () => scroller.scrollBy({ left: scroller.clientWidth * 0.8 }) }, icon('right')));
  return h('div', { class: 'section-head' },
    h('div', { class: 'st' }, thumb ? img(thumb) : null, h('div', null, strap ? h('div', { class: 'strap' }, strap) : null, h('h2', null, title))),
    right);
}
function renderSection(sec) {
  const box = h('div', { class: 'section' });
  const songs = sec.items?.filter((i) => i.type === 'song') || [];
  const onMore = sec.more ? () => (sec.more.id?.startsWith('VL') ? go('playlist', { id: sec.more.id }) : sec.more.query ? go('search', { q: sec.more.query, type: 'all' }) : go('browse', { id: sec.more.id, params: sec.more.params, title: sec.title })) : null;
  if (sec.layout === 'text') { box.append(sectionHead(sec.title || 'About'), h('div', { class: 'about' }, sec.text)); return box; }
  if (sec.layout === 'moods') {
    const grid = h('div', { class: 'moods' }, sec.items.map(moodChip));
    box.append(sectionHead(sec.title, { onMore, scroller: grid }), grid);
    return box;
  }
  if (sec.layout === 'grid-songs') {
    const grid = h('div', { class: 'qp-grid' }, songs.map((t, i) => songRow(t, { list: songs, index: i, source: sec.title })));
    const playAll = h('button', { class: 'btn outline', onclick: () => playList(songs, 0, { source: sec.title }) }, 'Play all');
    box.append(sectionHead(sec.title, { strap: sec.strapline, thumb: sec.thumb, scroller: grid, actions: [playAll] }), grid);
    return box;
  }
  if (sec.layout === 'list') {
    const allSongs = sec.items.every((i) => i.type === 'song');
    box.append(sectionHead(sec.title, { onMore }), h('div', { class: 'song-list' }, sec.items.map((it, i) => (it.type === 'song' ? songRow(it, { list: allSongs ? songs : null, index: allSongs ? i : undefined, source: sec.title }) : listItemRow(it)))));
    return box;
  }
  if (sec.layout === 'grid') {
    box.append(sec.title ? sectionHead(sec.title, { onMore }) : '', h('div', { class: 'grid' }, sec.items.map((it) => card(it))));
    return box;
  }
  const row = h('div', { class: 'row-scroll' }, sec.items.map((it) => card(it)));
  if (sec.items.some((i) => i.isVideo)) row.style.gridAutoColumns = '280px';
  box.append(sectionHead(sec.title, { strap: sec.strapline, thumb: sec.thumb, onMore, scroller: row }), row);
  return box;
}
function listItemRow(it) {
  return h('div', { class: 'song', onclick: () => openItem(it), oncontextmenu: (e) => { e.preventDefault(); itemMenu(e, it); } },
    h('div', { class: 's-lead' }, img(thumbOf(it), it.type === 'artist' ? 'round' : '')),
    h('div', { class: 's-main' }, h('div', { class: 's-title' }, explicitBadge(it), h('span', null, it.title)), h('div', { class: 's-sub' }, it.subtitle || '')),
    h('div', { class: 's-end' }, h('button', { class: 'icon-btn small', onclick: (e) => { e.stopPropagation(); itemMenu(e, it); } }, icon('more'))));
}
function moodChip(m) {
  const color = m.color || `hsl(${hashHue(m.title)} 65% 62%)`;
  return h('div', { class: 'mood', style: { '--mc': color }, onclick: () => go('browse', { id: m.id, params: m.params, title: m.title }) }, m.title);
}
function emptyState(ic, title, text, action) {
  return h('div', { class: 'empty-state' }, icon(ic), h('h3', null, title), h('div', null, text), action ? h('div', { style: { marginTop: '18px' } }, action) : null);
}

// ================= context menus =================
const menuEl = $('#ctxMenu');
function closeMenu() { menuEl.hidden = true; }
function showMenu(e, head, items) {
  menuEl.replaceChildren();
  if (head) menuEl.append(h('div', { class: 'cm-head' }, head.thumb ? img(head.thumb) : null, h('div', null, h('div', { class: 't' }, head.title), h('div', { class: 's' }, head.sub || ''))));
  for (const it of items) {
    if (!it) continue;
    if (it === '-') { menuEl.append(h('div', { class: 'cm-sep' })); continue; }
    menuEl.append(h('div', { class: 'cm-item' + (it.danger ? ' danger' : '') + (it.checked ? ' checked' : ''), onclick: () => { closeMenu(); Promise.resolve().then(it.fn).catch((er) => toast("Couldn't do that: " + shortErr(er?.message))); } }, icon(it.icon), h('span', null, it.label), it.hint ? h('span', { class: 'hint' }, it.hint) : null));
  }
  menuEl.hidden = false;
  const r = menuEl.getBoundingClientRect();
  let x = e.clientX ?? r.left, y = e.clientY ?? r.top;
  const ox = x + r.width > innerWidth - 8 ? 'right' : 'left';
  const oy = y + r.height > innerHeight - 8 ? 'bottom' : 'top';
  if (ox === 'right') x = Math.max(8, x - r.width);
  if (oy === 'bottom') y = Math.max(8, Math.min(y - r.height, innerHeight - r.height - 8));
  menuEl.style.left = x + 'px';
  menuEl.style.top = y + 'px';
  menuEl.style.setProperty('--origin', `${oy} ${ox}`);
}
document.addEventListener('mousedown', (e) => { if (!menuEl.hidden && !menuEl.contains(e.target)) closeMenu(); });
window.addEventListener('blur', closeMenu);

const copy = (text, msg = 'Link copied') => navigator.clipboard.writeText(text).then(() => toast(msg));
const shareUrl = (it) => it.type === 'song' ? `https://music.youtube.com/watch?v=${it.id}` : it.type === 'artist' ? `https://music.youtube.com/channel/${it.id}` : it.type === 'album' ? `https://music.youtube.com/browse/${it.id}` : `https://music.youtube.com/playlist?list=${String(it.id).replace(/^VL/, '')}`;

function songMenu(e, t, { onRemove, queueIdx } = {}) {
  const items = [
    { icon: 'radio', label: 'Start radio', fn: () => startFlowRadio(t) },
    { icon: 'playNext', label: 'Play next', fn: () => playNext([t]) },
    { icon: 'queue', label: 'Add to queue', fn: () => addToQueue([t]) },
    '-',
    { icon: isLiked(t.id) ? 'heart' : 'heartOutline', label: isLiked(t.id) ? 'Remove from Liked songs' : 'Add to Liked songs', fn: () => toggleLike(t) },
    { icon: 'playlistAdd', label: 'Add to playlist', fn: () => addToPlaylistDialog([t]) },
    isLocal(t) ? { icon: 'folder', label: 'Show in folder', fn: () => api.localReveal(t.id).then((ok) => ok || toast('File not found')) }
      : isDownloaded(t.id) ? { icon: 'offline', label: 'Remove download', fn: () => removeDownload(t) } : { icon: 'download', label: 'Download', fn: () => downloadTracks([t]) },
    '-'
  ];
  if (isLocal(t)) {
    items[0] = { icon: 'radio', label: 'Start radio (from YouTube Music)', fn: () => startFlowRadio(t) };
    if (onRemove) items.push({ icon: 'delete', label: onRemove.label, danger: true, fn: onRemove.fn });
    return showMenu(e, { thumb: thumbOf(t), title: t.title, sub: artistNames(t) }, items.filter((x, i) => !(x === '-' && i === items.length - 1)));
  }
  for (const a of (t.artists || []).filter((a) => a.id).slice(0, 3)) items.push({ icon: 'artist', label: `Go to ${a.name}`, fn: () => go('artist', { id: a.id }) });
  if (t.album?.id) items.push({ icon: 'album', label: 'Go to album', fn: () => go('album', { id: t.album.id }) });
  items.push({ icon: 'link', label: 'Copy link', fn: () => copy(shareUrl(t)) });
  items.push({ icon: 'external', label: 'Open in browser', fn: () => api.openExternal(shareUrl(t)) });
  if (queueIdx != null && queueIdx !== P.idx) items.push('-', { icon: 'delete', label: 'Remove from queue', danger: true, fn: () => removeFromQueue(queueIdx) });
  if (onRemove) items.push('-', { icon: 'delete', label: onRemove.label, danger: true, fn: onRemove.fn });
  showMenu(e, { thumb: thumbOf(t), title: t.title, sub: artistNames(t) }, items);
}
function itemMenu(e, it) {
  if (it.type === 'song') return songMenu(e, it);
  const items = [];
  if (['album', 'playlist', 'radio', 'artist', 'local'].includes(it.type)) {
    items.push({ icon: 'play', label: 'Play', fn: () => playItem(it) });
    if (it.type !== 'radio') items.push({ icon: 'shuffle', label: 'Shuffle', fn: () => playItem(it, { shuffle: true }) });
    items.push({ icon: 'playNext', label: 'Play next', fn: async () => playNext(await tracksFor(it)) });
    items.push({ icon: 'queue', label: 'Add to queue', fn: async () => addToQueue(await tracksFor(it)) });
    if (it.type !== 'radio') items.push({ icon: 'radio', label: 'Start radio', fn: () => startFlowRadio(it) });
    items.push('-');
  }
  if (['album', 'playlist', 'artist'].includes(it.type)) {
    const saved = isSaved(it);
    items.push({ icon: it.type === 'artist' ? (saved ? 'subscribed' : 'subscribe') : saved ? 'libAdded' : 'libAdd', label: it.type === 'artist' ? (saved ? 'Unfollow' : 'Follow') : saved ? 'Remove from library' : 'Save to library', fn: () => toggleSaved(it) });
    if (it.type !== 'artist') {
      items.push({ icon: 'playlistAdd', label: 'Add to playlist', fn: async () => addToPlaylistDialog(await tracksFor(it)) });
      items.push({ icon: 'download', label: 'Download', fn: async () => downloadTracks(await tracksFor(it)) });
    }
    items.push({ icon: 'link', label: 'Copy link', fn: () => copy(shareUrl(it)) });
  }
  if (it.type === 'local') {
    items.push({ icon: 'download', label: 'Download', fn: async () => downloadTracks(await tracksFor(it)) });
    items.push({ icon: 'edit', label: 'Rename', fn: () => renamePlaylist(it.id) });
    items.push({ icon: 'delete', label: 'Delete playlist', danger: true, fn: () => deletePlaylist(it.id) });
  }
  if (!items.length) return;
  showMenu(e, { thumb: it.thumb, title: it.title, sub: it.subtitle }, items);
}

// ================= dialogs & local playlists =================
function modal(content) {
  const m = $('#modal');
  m.replaceChildren(content);
  m.hidden = false;
  m.onmousedown = (e) => { if (e.target === m) closeModal(); };
}
function closeModal() { $('#modal').hidden = true; }
// Sign-in happens in your own browser (Google blocks it inside apps), in a separate window and profile.
async function signInFlow() {
  const b = await api.signInBrowser().catch(() => null);
  if (b) toast(`Sign in to Google in the ${b} window that just opened. Soncle picks it up automatically and closes it.`);
  const ok = await api.signIn().catch(() => false);
  if (ok) { toast('Signed in'); invalidateCaches(); rerender(); }
  else if (b) toast('Sign-in was cancelled');
  return ok;
}
async function licencesDialog() {
  const raw = await api.licences().catch(() => 'Could not read the notices file.');
  const txt = raw.replace(/([^\n])\n(?![\n|#\-<])/g, '$1 ');   // un-wrap paragraph lines for display
  modal(h('div', { class: 'dialog licences' }, h('h3', null, 'Open-source licences'),
    h('p', { class: 'muted' }, 'Soncle is free software under the GNU GPL, version 3 or later. It includes the works below, each under its own licence.'),
    h('pre', { class: 'lic-text' }, txt),
    h('div', { class: 'd-actions' }, h('button', { class: 'text-btn', onclick: closeModal }, 'Close'))));
}
function promptDialog(title, value = '', okLabel = 'Create', placeholder = 'Playlist name') {
  return new Promise((resolve) => {
    const input = h('input', { class: 'field', value, placeholder });
    const done = (v) => { closeModal(); resolve(v); };
    input.onkeydown = (e) => { if (e.key === 'Enter' && input.value.trim()) done(input.value.trim()); if (e.key === 'Escape') done(null); };
    modal(h('div', { class: 'dialog' }, h('h3', null, title), input,
      h('div', { class: 'd-actions' }, h('button', { class: 'text-btn', onclick: () => done(null) }, 'Cancel'), h('button', { class: 'text-btn', onclick: () => input.value.trim() && done(input.value.trim()) }, okLabel))));
    setTimeout(() => { input.focus(); input.select(); }, 30);
  });
}
function confirmDialog(title, text, okLabel = 'Delete') {
  return new Promise((resolve) => {
    const done = (v) => { closeModal(); resolve(v); };
    modal(h('div', { class: 'dialog' }, h('h3', null, title), h('p', null, text),
      h('div', { class: 'd-actions' }, h('button', { class: 'text-btn', onclick: () => done(false) }, 'Cancel'), h('button', { class: 'text-btn', onclick: () => done(true) }, okLabel))));
  });
}
async function createPlaylist(tracks = []) {
  const name = await promptDialog('New playlist');
  if (!name) return null;
  const pl = { id: 'local_' + uid(), name, tracks: tracks.map(slim), created: Date.now() };
  DB.playlists.unshift(pl);
  persist('playlists');
  renderSidebar();
  toast(tracks.length ? `Created "${name}" with ${tracks.length} song${tracks.length > 1 ? 's' : ''}` : `Created "${name}"`);
  return pl;
}
function addToPlaylist(pl, tracks) {
  const have = new Set(pl.tracks.map((t) => t.id));
  const add = tracks.filter((t) => !have.has(t.id)).map(slim);
  pl.tracks.push(...add);
  persist('playlists');
  renderSidebar();
  toast(add.length ? `Added ${add.length} to "${pl.name}"` : `Already in "${pl.name}"`);
}
function addToPlaylistDialog(tracks) {
  if (!tracks?.length) return;
  const list = h('div', { class: 'pl-pick' },
    h('div', { class: 'cm-item', onclick: async () => { closeModal(); await createPlaylist(tracks); } }, icon('add'), 'New playlist'),
    DB.playlists.map((pl) => h('div', { class: 'cm-item', onclick: () => { closeModal(); addToPlaylist(pl, tracks); } }, icon('queue'), h('span', null, pl.name), h('span', { class: 'hint' }, pl.tracks.length))));
  modal(h('div', { class: 'dialog' }, h('h3', null, 'Add to playlist'), list, h('div', { class: 'd-actions' }, h('button', { class: 'text-btn', onclick: closeModal }, 'Cancel'))));
}
async function renamePlaylist(id) {
  const pl = DB.playlists.find((p) => p.id === id);
  if (!pl) return;
  const name = await promptDialog('Rename playlist', pl.name, 'Save');
  if (!name) return;
  pl.name = name;
  persist('playlists');
  renderSidebar();
  const top = nav.stack[nav.pos];
  if (top?.name === 'local' && top.args.id === id) rerender();
}
async function deletePlaylist(id) {
  const pl = DB.playlists.find((p) => p.id === id);
  if (!pl || !(await confirmDialog('Delete playlist?', `"${pl.name}" will be permanently deleted.`))) return;
  DB.playlists = DB.playlists.filter((p) => p.id !== id);
  persist('playlists');
  renderSidebar();
  const top = nav.stack[nav.pos];
  if (top?.name === 'local' && top.args.id === id) go('library', {}, { replace: true });
}
function collage(tracks, cls = 'cover') {
  const thumbs = [...new Set(tracks.map(thumbOf).filter(Boolean))].slice(0, 4);
  if (thumbs.length < 4) return h('div', { class: cls, style: { background: thumbs[0] ? `center/cover url("${thumbs[0]}")` : 'var(--primary-container)', display: 'flex', alignItems: 'center', justifyContent: 'center', color: 'var(--on-primary-container)' } }, thumbs[0] ? null : icon('queue'));
  return h('div', { class: cls, style: { display: 'grid', gridTemplateColumns: '1fr 1fr', overflow: 'hidden' } }, thumbs.map((u) => h('div', { style: { background: `center/cover url("${u}")` } })));
}
const likedCover = (cls = 'cover', size = 24) => h('div', { class: cls, style: { background: 'linear-gradient(135deg, #5b3fd1, #ff7a9a)', display: 'flex', alignItems: 'center', justifyContent: 'center', color: '#fff' } }, bigIcon('heart', size));

function rangeInput({ min, max, step, value, oninput, onchange }) {
  const r = h('input', { type: 'range', min, max, step, value });
  const fill = () => r.style.setProperty('--fill', ((r.value - min) / (max - min)) * 100 + '%');
  fill();
  r.addEventListener('input', () => { fill(); oninput?.(Number(r.value)); });
  if (onchange) r.addEventListener('change', () => onchange(Number(r.value)));
  return r;
}

function sleepDialog() {
  const opts = [5, 10, 15, 30, 45, 60, 90, 120];
  const grid = h('div', { class: 'opt-grid' },
    opts.map((m) => h('button', { class: 'chip', onclick: () => { setSleep({ minutes: m }); closeModal(); } }, m < 60 ? `${m} min` : `${m / 60} hr`)),
    h('button', { class: 'chip', onclick: () => { setSleep({ endOfSong: true }); closeModal(); } }, 'End of song'),
    h('button', { class: 'chip', onclick: async () => { closeModal(); const v = await promptDialog('Custom sleep timer', String(S().sleepDefault || 30), 'Start', 'Minutes'); const n = Number(v); if (n > 0) setSleep({ minutes: n }); } }, 'Custom…'));
  modal(h('div', { class: 'dialog' }, h('h3', null, 'Sleep timer'),
    P.sleep ? h('p', null, P.sleep.endOfSong ? 'Music will stop at the end of this song.' : `Music will stop in ${fmtTime((P.sleep.end - Date.now()) / 1000)}.`) : h('p', null, 'Stop playback after:'),
    grid,
    h('div', { class: 'd-actions' },
      P.sleep ? h('button', { class: 'text-btn', onclick: () => { setSleep(null); closeModal(); } }, 'Turn off') : null,
      h('button', { class: 'text-btn', onclick: closeModal }, 'Close'))));
}
function tempoDialog() {
  const out = h('output', null, S().tempo.toFixed(2) + '×');
  const r = rangeInput({ min: 0.5, max: 2, step: 0.05, value: S().tempo, oninput: (v) => { out.textContent = v.toFixed(2) + '×'; setSetting('tempo', v); engine.setOptions({ tempo: v }); updateExtraButtons(); } });
  const sw = h('div', { class: 'switch' + (S().varispeed ? ' on' : ''), onclick: () => { setSetting('varispeed', !S().varispeed); sw.classList.toggle('on', S().varispeed); engine.setOptions({ varispeed: S().varispeed }); } });
  modal(h('div', { class: 'dialog' }, h('h3', null, 'Tempo & pitch'),
    h('div', { class: 'range-row' }, h('label', null, 'Tempo'), r, out),
    h('div', { class: 'range-row' }, h('label', { style: { flex: 1, width: 'auto' } }, 'Pitch follows tempo (varispeed)'), sw),
    h('div', { class: 'd-actions' },
      h('button', { class: 'text-btn', onclick: () => { setSetting('tempo', 1); setSetting('varispeed', false); engine.setOptions({ tempo: 1, varispeed: false }); updateExtraButtons(); closeModal(); } }, 'Reset'),
      h('button', { class: 'text-btn', onclick: closeModal }, 'Done'))));
}

// ================= sidebar =================
function renderSidebar() {
  const box = $('#navPlaylists');
  const items = [];
  items.push({ key: routeKey({ name: 'liked', args: {} }), src: 'liked', go: () => go('liked'), cover: likedCover('cover'), t: 'Liked songs', s: songsLabel(DB.liked.length) });
  for (const pl of DB.playlists) items.push({ key: routeKey({ name: 'local', args: { id: pl.id } }), src: 'local:' + pl.id, go: () => go('local', { id: pl.id }), cover: collage(pl.tracks), t: pl.name, s: `Playlist • ${nOf(pl.tracks.length, 'song', 'songs')}`, menu: { type: 'local', id: pl.id, title: pl.name } });
  for (const it of DB.savedPlaylists) items.push({ key: routeKey({ name: 'playlist', args: { id: it.id } }), src: srcKey(it), go: () => go('playlist', { id: it.id }), cover: img(it.thumb, 'cover'), t: it.title, s: it.subtitle || 'Playlist', menu: it });
  for (const it of DB.savedAlbums) items.push({ key: routeKey({ name: 'album', args: { id: it.id } }), src: srcKey(it), go: () => go('album', { id: it.id }), cover: img(it.thumb, 'cover'), t: it.title, s: 'Album • ' + (it.subtitle || ''), menu: it });
  box.replaceChildren(...items.map((i) => h('div', { class: 'nav-pl', 'data-key': i.key, 'data-src': i.src || null, title: i.t, onclick: i.go, oncontextmenu: (e) => { e.preventDefault(); if (i.menu) itemMenu(e, i.menu); } }, h('div', { class: 'nav-cover' }, i.cover, nowBars()), h('div', null, h('div', { class: 't' }, i.t), h('div', { class: 's' }, i.s)))));
  updateNavButtons();
  refreshSourceMarks();
}

// ================= views =================
// "Made for you": mixes built on this computer from what you play, like and skip, the time of day,
// and tempo/energy learned by Flow radio. Nothing is sent anywhere.
function madeForYou() {
  const now = Date.now(), day = 864e5;
  const plays = new Map(), lastAt = new Map(), byId = new Map();
  for (const e of DB.history.slice(0, 2000)) {
    const t = e.track; if (!t?.id) continue;
    byId.set(t.id, t);
    if (!lastAt.has(t.id)) lastAt.set(t.id, e.at);
    const x = plays.get(t.id) || { n: 0, recent: 0 };
    x.n++; if (now - e.at < 30 * day) x.recent++;
    plays.set(t.id, x);
  }
  for (const t of DB.liked) if (t?.id) byId.set(t.id, t);
  if (byId.size < 12) return null;
  const taste = tasteModel(), c = contextFor(), liked = new Set(DB.liked.map((t) => t.id));
  const artistOf = (t) => (t.artists?.[0]?.name || '').toLowerCase();
  const mixes = [];
  // 1. right now: your songs that fit the time of day
  const fit = (t) => {
    const f = featRanked(t.id);
    let s = taste.artist(artistOf(t)) * 0.35 + (liked.has(t.id) ? 0.3 : 0) + Math.min(1, (plays.get(t.id)?.n || 0) / 6) * 0.2;
    if (f?.energy != null) s += (1 - Math.min(1, Math.abs(f.energy - c.energy) * 2.2)) * 0.45;
    if (f?.bpm && c.maxBpm && f.bpm > c.maxBpm) s -= 0.4;
    return s - taste.recent(t.id) * 0.5 - Math.min(3, taste.skips(t.id)) * 0.15;
  };
  const now1 = [...byId.values()].map((t) => [t, fit(t)]).sort((a, b) => b[1] - a[1]).slice(0, 30).map(([t]) => t);
  if (now1.length >= 8) {
    const [seed, ...rest] = now1;
    const ordered = [seed, ...planFlow(seed, rest, featRanked, { ctx: c, taste }).map((x) => x.track)];
    const name = c.label.replace(/^./, (m) => m.toUpperCase());
    mixes.push({ title: `${name} mix`, sub: `Your music for the ${c.label}, in a smooth flow`, tracks: ordered });
  }
  // 2. on repeat
  const rep = [...plays].filter(([, x]) => x.recent >= 3).sort((a, b) => b[1].recent - a[1].recent).slice(0, 25).map(([id]) => byId.get(id));
  if (rep.length >= 5) mixes.push({ title: 'On repeat', sub: 'What you’ve played most this month', tracks: rep });
  // 3. rediscover: songs you loved that have gone quiet
  const old = [...byId.values()].filter((t) => (liked.has(t.id) || (plays.get(t.id)?.n || 0) >= 3) && now - (lastAt.get(t.id) || 0) > 45 * day)
    .sort((a, b) => (plays.get(b.id)?.n || 0) - (plays.get(a.id)?.n || 0)).slice(0, 25);
  if (old.length >= 5) mixes.push({ title: 'Rediscover', sub: 'Favourites you haven’t heard in a while', tracks: old });
  // 4. discover: a Flow radio around your most-played recent song (new music)
  const seedNew = rep[0] || now1[0];
  if (seedNew) mixes.push({ title: 'Discover', sub: `New songs around ${seedNew.artists?.[0]?.name || seedNew.title}`, tracks: [seedNew, ...now1.slice(1, 4)], radio: seedNew });
  if (!mixes.length) return null;
  const cardFor = (m) => {
    const play = (e) => { e?.stopPropagation(); if (m.radio) startFlowRadio(m.radio); else playList(m.tracks, 0, { source: m.title, key: 'mix:' + m.title }); };
    return h('div', { class: 'mix-card', onclick: play, title: m.sub },
      h('div', { class: 'mix-art' }, collage(m.tracks, 'mix-cover'), h('div', { class: 'mix-shade' }), h('div', { class: 'mix-name' }, m.title),
        h('button', { class: 'mix-play', 'aria-label': 'Play ' + m.title, onclick: play }, icon(m.radio ? 'radio' : 'play'))),
      h('div', { class: 'mix-sub' }, m.radio ? m.sub : `${m.sub} · ${nOf(m.tracks.length, 'song', 'songs')}`));
  };
  return h('div', { class: 'section' }, sectionHead('Made for you', { strap: 'Built on this PC from what you play' }), h('div', { class: 'mix-row' }, mixes.map(cardFor)));
}
VIEWS.home = async (ctx, { chip }) => {
  const v = ctx.view;
  v.append(skeletonPage());
  const data = await ctx.cache('home', () => api.home(chip));
  if (!ctx.alive()) return;
  v.replaceChildren();
  const hour = new Date().getHours();
  v.append(h('div', { class: 'page-title' }, hour < 5 ? 'Late night vibes' : hour < 12 ? 'Good morning' : hour < 18 ? 'Good afternoon' : 'Good evening'));
  if (data.chips?.length) v.append(h('div', { class: 'chips' }, data.chips.map((c) => h('button', { class: 'chip' + (c === chip ? ' active' : ''), onclick: () => go('home', c === chip ? {} : { chip: c }, { replace: true }) }, c))));
  const mfy = !chip ? madeForYou() : null;
  if (mfy) v.append(mfy);
  if (!chip && DB.history.length) {
    const seen = new Set();
    const recent = DB.history.map((x) => x.track).filter((t) => t && !seen.has(t.id) && seen.add(t.id)).slice(0, 20);
    if (recent.length >= 4) v.append(renderSection({ title: 'Listen again', strapline: 'From your history', layout: 'grid-songs', items: recent }));
  }
  if (!chip && DB.followedArtists.length) v.append(renderSection({ title: 'Your artists', layout: 'carousel', items: DB.followedArtists.map((a) => ({ ...a, type: 'artist' })) }));
  const secBox = h('div');
  v.append(secBox);
  data.sections.forEach((s) => secBox.append(renderSection(s)));
  if (data.hasMore) infinite(ctx, v, async () => {
    const more = await api.homeMore();
    data.sections.push(...more.sections);
    more.sections.forEach((s) => secBox.append(renderSection(s)));
    data.hasMore = more.hasMore;
    return more.hasMore;
  });
};
function infinite(ctx, parent, loadMore) {
  const sentinel = h('div', { class: 'center' }, h('div', { class: 'spinner' }));
  parent.append(sentinel);
  let busy = false;
  const io = new IntersectionObserver(async (ents) => {
    if (!ents[0].isIntersecting || busy || !ctx.alive()) return;
    busy = true;
    try { if (!(await loadMore())) { io.disconnect(); sentinel.remove(); } } catch { io.disconnect(); sentinel.remove(); }
    busy = false;
  }, { root: main, rootMargin: '700px' });
  io.observe(sentinel);
}

VIEWS.explore = async (ctx) => {
  const v = ctx.view;
  v.append(h('div', { class: 'page-title' }, 'Explore'), skeletonPage());
  const data = await ctx.cache('explore', () => api.explore());
  if (!ctx.alive()) return;
  v.replaceChildren(h('div', { class: 'page-title' }, 'Explore'));
  const icons = [['newRel', /new/i], ['trending', /chart/i], ['mood', /mood|genre/i]];
  if (data.buttons.length) v.append(h('div', { class: 'actions', style: { marginTop: 0 } }, data.buttons.map((b) => h('button', { class: 'btn tonal', onclick: () => go('browse', { id: b.id, params: b.params, title: b.title }) }, icon(icons.find(([, re]) => re.test(b.title))?.[0] || 'explore'), b.title))));
  data.sections.forEach((s) => v.append(renderSection(s)));
};

VIEWS.browse = async (ctx, { id, params, title }) => {
  const v = ctx.view;
  v.append(h('div', { class: 'page-title' }, title || ''), skeletonPage());
  const data = await ctx.cache('browse', () => api.browse(id, params));
  if (!ctx.alive()) return;
  v.replaceChildren(h('div', { class: 'page-title' }, data.title || title || ''));
  if (!data.sections.length) v.append(emptyState('explore', 'Nothing here', 'This page has no content right now.'));
  data.sections.forEach((s) => {
    if (s.layout === 'moods') v.append(h('div', { class: 'section' }, s.title ? sectionHead(s.title) : '', h('div', { class: 'moods wrap' }, s.items.map(moodChip))));
    else v.append(renderSection(s));
  });
};

const FILTERS = [['all', 'All'], ['song', 'Songs'], ['video', 'Videos'], ['album', 'Albums'], ['artist', 'Artists'], ['playlist', 'Playlists']];
const filterFromTitle = (t) => ({ songs: 'song', videos: 'video', albums: 'album', artists: 'artist', 'community playlists': 'playlist', 'featured playlists': 'playlist', playlists: 'playlist' })[String(t).toLowerCase()];
VIEWS.search = async (ctx, { q, type = 'all' }) => {
  const v = ctx.view;
  sInput.value = q;
  $('#searchClear').hidden = !q;
  const chips = h('div', { class: 'chips', style: { marginBottom: '8px' } }, FILTERS.map(([k, label]) => h('button', { class: 'chip' + (k === type ? ' active' : ''), onclick: () => go('search', { q, type: k }, { replace: true }) }, label)));
  v.append(chips, loading());
  const words = q.toLowerCase().split(/\s+/).filter(Boolean);
  const localHits = type !== 'all' ? [] : await loadLocal().then((ts) => ts.filter((t) => { const hay = [t.title, artistNames(t), t.album?.name].join(' ').toLowerCase(); return words.every((w) => hay.includes(w)); }).slice(0, 6)).catch(() => []);
  const localSec = () => (localHits.length ? renderSection({ title: 'On this computer', layout: 'list', items: localHits }) : null);
  let data;
  try { data = await ctx.cache('search', () => api.search(q, type)); }
  catch (e) { if (!localHits.length) throw e; if (!ctx.alive()) return; v.replaceChildren(chips, localSec(), errorBox(e, () => render(ctx.entry, true))); return; }
  if (!ctx.alive()) return;
  v.replaceChildren(chips);
  if (data.correction) v.append(h('div', { style: { color: 'var(--on-surface-var)', margin: '10px 0' } }, 'Did you mean ', h('span', { class: 'lnk', style: { color: 'var(--primary)', fontWeight: 600 }, onclick: () => go('search', { q: data.correction, type }) }, data.correction), '?'));
  if (!data.top && !data.sections.length && !localHits.length) { v.append(emptyState('search', 'No results', `Nothing found for "${q}"`)); return; }
  if (type === 'all') {
    if (!data.top && localHits.length) v.append(localSec());
    if (data.top) {
      const t = data.top;
      const cover = img(t.thumb);
      Object.assign(cover.style, { width: '120px', height: '120px', borderRadius: t.type === 'artist' ? '50%' : '14px', objectFit: 'cover' });
      v.append(h('div', { class: 'section' }, sectionHead('Top result'),
        h('div', { class: 'top-result', style: { display: 'grid', gridTemplateColumns: 'minmax(300px, 420px) 1fr', gap: '24px' } },
          h('div', { class: 'set-card', style: { padding: '20px', cursor: 'pointer' }, onclick: () => openItem(t), oncontextmenu: (e) => { e.preventDefault(); itemMenu(e, t); } },
            cover,
            h('div', { style: { fontSize: '26px', fontWeight: 700, marginTop: '16px' } }, t.title),
            h('div', { style: { color: 'var(--on-surface-var)', marginTop: '4px' } }, t.subtitle),
            h('div', { class: 'actions' },
              h('button', { class: 'btn filled', onclick: (e) => { e.stopPropagation(); playItem(t); } }, icon(t.type === 'artist' ? 'shuffle' : 'play'), t.type === 'artist' ? 'Shuffle' : 'Play'),
              t.type === 'song' ? h('button', { class: 'btn outline', onclick: (e) => { e.stopPropagation(); addToQueue([t]); } }, icon('queue'), 'Queue') : null)),
          h('div', { class: 'song-list' }, (t.related || []).slice(0, 5).map((it) => (it.type === 'song' ? songRow(it) : listItemRow(it)))))));
      if (localHits.length) v.append(localSec());
    }
    for (const s of data.sections) {
      const f = filterFromTitle(s.title);
      const box = renderSection({ ...s, more: null });
      if (f) box.querySelector('.sh-actions').append(h('button', { class: 'text-btn', onclick: () => go('search', { q, type: f }, { replace: true }) }, 'See all'));
      v.append(box);
    }
    return;
  }
  const all = data.sections.flatMap((s) => s.items);
  const isSongs = type === 'song' || type === 'video';
  const container = h('div', { class: isSongs ? 'song-list' : 'grid', style: { marginTop: '16px' } });
  const add = (list) => list.forEach((it) => container.append(isSongs ? songRow(it) : card(it)));
  add(all);
  v.append(container);
  if (data.hasMore) infinite(ctx, v, async () => { const more = await api.searchMore(q, type); all.push(...more.items); add(more.items); return more.hasMore; });
};

function detailHeader({ kind, title, cover, metaLines, description, actions }) {
  const key = pageKey(), on = isSourcePlaying(key);
  const coverBox = h('div', { class: 'dh-cover' + (on ? ' src-on' : '') + (on && P.playing ? ' src-playing' : ''), 'data-src': key }, cover, nowBars());
  return h('div', { class: 'detail-head' }, coverBox,
    h('div', { class: 'info' },
      h('div', { class: 'kind' }, kind),
      h('h1', { title }, title),
      h('div', { class: 'meta' }, metaLines),
      description ? h('div', { class: 'desc', onclick: (e) => e.currentTarget.classList.toggle('open') }, description) : null,
      h('div', { class: 'actions' }, actions)));
}
const totalDuration = (tracks) => {
  const s = tracks.reduce((a, t) => a + (t.duration || 0), 0);
  if (!s) return '';
  const hh = Math.floor(s / 3600), m = Math.round((s % 3600) / 60);
  return hh ? `${hh} hr ${m} min` : `${m} min`;
};
function saveButton(item) {
  const b = h('button', { class: 'btn outline', onclick: () => { toggleSaved(item); draw(); } });
  const draw = () => b.replaceChildren(icon(isSaved(item) ? 'libAdded' : 'libAdd'), isSaved(item) ? 'Saved' : 'Save');
  draw();
  return b;
}
// Download button that shows when everything here is already offline (Android's "offline" state)
function downloadButton(getTracks, known) {
  const all = known?.length && known.every((t) => isLocal(t) || isDownloaded(t.id));
  return h('button', { class: 'icon-btn' + (all ? ' on' : ''), title: all ? 'Downloaded — available offline' : 'Download', onclick: async () => { if (all) return toast('Already downloaded'); downloadTracks(await getTracks()); } }, icon(all ? 'offline' : 'download'));
}

VIEWS.album = async (ctx, { id }) => {
  const v = ctx.view;
  v.append(loading());
  const a = await ctx.cache('album', () => api.album(id));
  if (!ctx.alive()) return;
  v.replaceChildren();
  const item = { type: 'album', id, title: a.title, thumb: a.thumb, subtitle: artistNames(a) };
  const second = a.second || '';
  v.append(detailHeader({
    kind: a.subtitle?.split(' • ')[0] || 'Album',
    title: a.title,
    cover: img(a.thumb, 'cover'),
    metaLines: [h('div', null, h('b', null, artistLinks(a.artists)), a.subtitle?.split(' • ').slice(1).map((x) => ' • ' + x).join('') || ''), h('div', null, second || [songsLabel(a.tracks.length), totalDuration(a.tracks)].filter(Boolean).join(' • '))],
    description: a.description,
    actions: [
      playBtn(() => playList(a.tracks, 0, { source: a.title })),
      h('button', { class: 'btn tonal', onclick: () => playList(a.tracks, 0, { shuffle: true, source: a.title }) }, icon('shuffle'), 'Shuffle'),
      saveButton(item),
      downloadButton(async () => a.tracks, a.tracks),
      h('button', { class: 'icon-btn', title: 'More', onclick: (e) => itemMenu(e, item) }, icon('more'))
    ]
  }));
  v.append(h('div', { class: 'song-list' }, a.tracks.map((t, i) => songRow(t, { list: a.tracks, index: i, showThumb: false, source: a.title }))));
  a.sections.forEach((s) => v.append(renderSection(s)));
};

VIEWS.playlist = async (ctx, { id }) => {
  const v = ctx.view;
  v.append(loading());
  const p = await ctx.cache('playlist', () => api.playlist(id, false));
  if (!ctx.alive()) return;
  v.replaceChildren();
  let tracks = p.tracks;
  const item = { type: 'playlist', id, title: p.title, thumb: p.thumb, subtitle: artistNames(p) || p.subtitle };
  const full = async () => {
    if (!p.hasMore) return tracks;
    const all = await api.playlist(id, true);
    p.tracks = tracks = all.tracks; p.hasMore = false;
    return tracks;
  };
  v.append(detailHeader({
    kind: 'Playlist', title: p.title, cover: img(p.thumb, 'cover'),
    metaLines: [h('div', null, h('b', null, artistLinks(p.artists, p.subtitle || ''))), h('div', null, p.second || '')],
    description: p.description,
    actions: [
      playBtn(async () => playList(await full(), 0, { source: p.title })),
      h('button', { class: 'btn tonal', onclick: async () => playList(await full(), 0, { shuffle: true, source: p.title }) }, icon('shuffle'), 'Shuffle'),
      saveButton(item),
      h('button', { class: 'btn outline', title: 'Copy into an editable local playlist', onclick: async () => { const t = await full(); DB.playlists.unshift({ id: 'local_' + uid(), name: p.title, tracks: t.map(slim), created: Date.now() }); persist('playlists'); renderSidebar(); toast(`Imported ${nOf(t.length, 'song', 'songs')}`); } }, icon('importIcon'), 'Import'),
      downloadButton(full),
      h('button', { class: 'icon-btn', title: 'More', onclick: (e) => itemMenu(e, item) }, icon('more'))
    ]
  }));
  const listEl = h('div', { class: 'song-list' });
  const draw = () => listEl.replaceChildren(...tracks.map((t, i) => songRow(t, { list: tracks, index: i, showAlbum: true, source: p.title })));
  draw();
  v.append(listEl);
  if (p.hasMore) {
    const more = loading();
    v.append(more);
    full().then(() => { if (ctx.alive()) { draw(); more.remove(); } }).catch(() => more.remove());
  }
};

VIEWS.artist = async (ctx, { id }) => {
  const v = ctx.view;
  v.append(loading());
  const a = await ctx.cache('artist', () => api.artist(id));
  if (!ctx.alive()) return;
  v.replaceChildren();
  const item = { type: 'artist', id, title: a.title, thumb: a.thumb, subtitle: a.subscribers ? `${a.subscribers} subscribers` : '' };
  const followBtn = h('button', { class: 'btn outline', onclick: () => { toggleSaved(item); followBtn.textContent = isSaved(item) ? 'Following' : 'Follow'; } }, isSaved(item) ? 'Following' : 'Follow');
  const subs = [a.subscribers && `${a.subscribers} subscribers`, a.listeners].filter(Boolean).join(' • ');
  v.append(h('div', { class: 'artist-hero' },
    a.thumb ? img(a.thumb.replace(/=w\d+-h\d+[^/]*$/, '=w1600-h900-p-l90-rj'), 'bg') : null,
    h('div', { class: 'ah-info' },
      h('h1', null, a.title),
      h('div', { class: 'sub' }, subs),
      h('div', { class: 'actions' },
        a.shuffleId || a.radioId ? playBtn(async () => playList(await api.radio(a.shuffleId || a.radioId), 0, { source: a.title }), { label: 'Shuffle' }) : null,
        a.radioId ? h('button', { class: 'btn tonal', onclick: () => startFlowRadio({ type: 'artist', id, title: a.title, thumb: a.thumb }) }, icon('radio'), 'Radio') : null,
        followBtn,
        h('button', { class: 'icon-btn', title: 'Copy link', onclick: () => copy(shareUrl(item)) }, icon('link'))))));
  a.sections.forEach((s) => v.append(renderSection(s)));
  if (a.description) v.append(h('div', { class: 'section' }, sectionHead('About'), h('div', { class: 'about' }, a.description)));
};

function localListView(ctx, { kind, title, tracks, cover, subtitle, actions, onRemove }) {
  const v = ctx.view;
  v.append(detailHeader({
    kind, title, cover,
    metaLines: [h('div', null, subtitle), h('div', null, [songsLabel(tracks.length), totalDuration(tracks)].filter(Boolean).join(' • '))],
    actions: [
      playBtn(() => playList(tracks, 0, { source: title }), { disabled: !tracks.length }),
      h('button', { class: 'btn tonal', disabled: !tracks.length, onclick: () => playList(tracks, 0, { shuffle: true, source: title }) }, icon('shuffle'), 'Shuffle'),
      tracks.length ? downloadButton(async () => tracks, tracks) : null,
      ...(actions || [])
    ]
  }));
  if (!tracks.length) { v.append(emptyState('note', 'No songs yet', 'Right-click any song and choose "Add to playlist", or like it to see it here.')); return; }
  const listEl = h('div', { class: 'song-list' });
  tracks.forEach((t, i) => {
    const row = songRow(t, { list: tracks, index: i, showAlbum: true, source: title, onRemove: onRemove && { label: onRemove.label, fn: () => onRemove.fn(t, i) } });
    if (onRemove?.reorder) {
      row.draggable = true;
      row.addEventListener('dragstart', (e) => { e.dataTransfer.setData('text/pl', String(i)); row.classList.add('q-item', 'dragging'); });
      row.addEventListener('dragend', () => row.classList.remove('dragging'));
      row.addEventListener('dragover', (e) => { e.preventDefault(); const r = row.getBoundingClientRect(); const above = e.clientY < r.top + r.height / 2; row.classList.add('q-item'); row.classList.toggle('drop-above', above); row.classList.toggle('drop-below', !above); });
      row.addEventListener('dragleave', () => row.classList.remove('drop-above', 'drop-below'));
      row.addEventListener('drop', (e) => {
        e.preventDefault();
        row.classList.remove('drop-above', 'drop-below');
        const raw = e.dataTransfer.getData('text/pl');
        if (raw === '') return;
        const from = Number(raw);
        if (isNaN(from)) return;
        const r = row.getBoundingClientRect();
        let to = e.clientY < r.top + r.height / 2 ? i : i + 1;
        if (from < to) to--;
        onRemove.reorder(from, to);
      });
    }
    listEl.append(row);
  });
  v.append(listEl);
}
VIEWS.liked = (ctx) => localListView(ctx, {
  kind: 'Playlist', title: 'Liked songs', subtitle: 'Songs you liked', cover: likedCover('cover', 96), tracks: DB.liked,
  onRemove: { label: 'Remove from Liked songs', fn: (t) => { toggleLike(t); rerender(); } }
});
VIEWS.local = (ctx, { id }) => {
  const pl = DB.playlists.find((p) => p.id === id);
  if (!pl) { ctx.view.append(emptyState('queue', 'Playlist not found', 'It may have been deleted.')); return; }
  localListView(ctx, {
    kind: 'Local playlist', title: pl.name, subtitle: 'Drag songs to reorder', cover: collage(pl.tracks), tracks: pl.tracks,
    actions: [h('button', { class: 'icon-btn', title: 'Rename', onclick: () => renamePlaylist(id) }, icon('edit')), h('button', { class: 'icon-btn', title: 'Delete', onclick: () => deletePlaylist(id) }, icon('delete'))],
    onRemove: {
      label: 'Remove from playlist',
      fn: (t, i) => { const [rm] = pl.tracks.splice(i, 1); persist('playlists'); renderSidebar(); rerender(); toast(`Removed from "${pl.name}"`, { label: 'Undo', fn: () => { pl.tracks.splice(i, 0, rm); persist('playlists'); renderSidebar(); rerender(); } }); },
      reorder: (from, to) => { const [m] = pl.tracks.splice(from, 1); pl.tracks.splice(to, 0, m); persist('playlists'); renderSidebar(); rerender(); }
    }
  });
};

VIEWS.library = async (ctx, { tab = 'playlists' }) => {
  const v = ctx.view;
  v.append(h('div', { class: 'page-title' }, 'Library'));
  const signed = await api.authStatus().catch(() => false);
  if (!ctx.alive()) return;
  const tabs = [['playlists', 'Playlists'], ['songs', 'Liked songs'], ['albums', 'Albums'], ['artists', 'Artists'], ['downloads', 'Downloads']];
  if (signed) tabs.push(['account', 'YouTube Music']);
  v.append(h('div', { class: 'chips lib-tabs' }, tabs.map(([k, l]) => h('button', { class: 'chip' + (k === tab ? ' active' : ''), onclick: () => go('library', { tab: k }, { replace: true }) }, l))));
  if (tab === 'playlists') {
    const newCard = h('div', { class: 'card', onclick: () => createPlaylist().then((p) => p && go('local', { id: p.id })) },
      h('div', { class: 'art', style: { display: 'flex', alignItems: 'center', justifyContent: 'center', border: '2px dashed var(--outline-var)', background: 'transparent' } }, bigIcon('add', 48)),
      h('div', { class: 'ct' }, 'New playlist'), h('div', { class: 'cs' }, 'Create a local playlist'));
    const importCard = h('div', { class: 'card', onclick: () => importDialog() },
      h('div', { class: 'art', style: { display: 'flex', alignItems: 'center', justifyContent: 'center', border: '2px dashed var(--outline-var)', background: 'transparent' } }, bigIcon('importIcon', 48)),
      h('div', { class: 'ct' }, 'Import'), h('div', { class: 'cs' }, 'From Spotify, CSV or a list'));
    const likedCard = h('div', { class: 'card', 'data-src': 'liked', onclick: () => go('liked') }, withBars(likedCover('art', 64)), h('div', { class: 'ct' }, 'Liked songs'), h('div', { class: 'cs' }, songsLabel(DB.liked.length)));
    const localCards = DB.playlists.map((p) => h('div', { class: 'card', 'data-src': 'local:' + p.id, onclick: () => go('local', { id: p.id }), oncontextmenu: (e) => { e.preventDefault(); itemMenu(e, { type: 'local', id: p.id, title: p.name }); } }, withBars(collage(p.tracks, 'art')), h('div', { class: 'ct' }, p.name), h('div', { class: 'cs' }, songsLabel(p.tracks.length))));
    v.append(h('div', { class: 'grid' }, newCard, importCard, likedCard, localCards, DB.savedPlaylists.map((p) => card(p))));
  } else if (tab === 'songs') {
    if (!DB.liked.length) v.append(emptyState('heartOutline', 'No liked songs', 'Tap the heart on any song to save it here.'));
    else v.append(h('div', { class: 'song-list' }, DB.liked.map((t, i) => songRow(t, { list: DB.liked, index: i, showAlbum: true, source: 'Liked songs' }))));
  } else if (tab === 'albums') {
    if (!DB.savedAlbums.length) v.append(emptyState('album', 'No saved albums', 'Save albums to find them here.'));
    else v.append(h('div', { class: 'grid' }, DB.savedAlbums.map((a) => card(a))));
  } else if (tab === 'artists') {
    if (!DB.followedArtists.length) v.append(emptyState('artist', 'No artists yet', 'Follow artists to see them here.'));
    else v.append(h('div', { class: 'grid' }, DB.followedArtists.map((a) => card({ ...a, type: 'artist' }))));
  } else if (tab === 'downloads') {
    const list = await api.downloads();
    if (!ctx.alive()) return;
    const size = list.done.reduce((a, d) => a + (d.size || 0), 0);
    const tracks = list.done.map((d) => d.track);
    v.append(h('div', { class: 'section-head' },
      h('div', { style: { color: 'var(--on-surface-var)' } }, `${nOf(tracks.length, 'song', 'songs')} • ${fmtBytes(size)} • available offline`),
      h('div', { class: 'sh-actions' },
        tracks.length ? playBtn(() => playList(tracks, 0, { source: 'Downloads' })) : null,
        tracks.length ? h('button', { class: 'btn tonal', onclick: () => playList(tracks, 0, { shuffle: true, source: 'Downloads' }) }, icon('shuffle'), 'Shuffle') : null,
        h('button', { class: 'icon-btn', title: 'Open downloads folder', onclick: () => api.openDownloads() }, icon('folder')))));
    if (!tracks.length && !list.active.length) v.append(emptyState('offline', 'No downloads', 'Download songs, albums and playlists to listen offline.'));
    v.append(h('div', { class: 'song-list' }, tracks.map((t, i) => songRow(t, { list: tracks, index: i, showAlbum: true, source: 'Downloads', onRemove: { label: 'Remove download', fn: async () => { await removeDownload(t); rerender(); } } }))));
  } else if (tab === 'account') {
    v.append(loading());
    const lib = await ctx.cache('account', () => api.library());
    if (!ctx.alive()) return;
    v.lastChild.remove();
    if (!lib?.sections?.length) v.append(emptyState('library', 'Nothing here', 'Your YouTube Music library is empty.'));
    else lib.sections.forEach((s) => v.append(renderSection({ ...s, layout: s.layout === 'list' ? 'list' : 'grid' })));
  }
};

VIEWS.history = (ctx) => {
  const v = ctx.view;
  v.append(h('div', { class: 'section-head' }, h('div', { class: 'page-title', style: { margin: '8px 0 0' } }, 'History'),
    DB.history.length ? h('button', { class: 'text-btn', onclick: async () => { if (await confirmDialog('Clear history?', 'Your listening history in this app will be removed.', 'Clear')) { DB.history = []; persist('history'); rerender(); } } }, 'Clear history') : ''));
  if (!DB.history.length) { v.append(emptyState('history', 'No history yet', 'Songs you play will show up here.')); return; }
  const groups = new Map();
  const today = new Date().toDateString();
  const yest = new Date(Date.now() - 864e5).toDateString();
  for (const x of DB.history.slice(0, 500)) {
    const d = new Date(x.at).toDateString();
    const label = d === today ? 'Today' : d === yest ? 'Yesterday' : new Date(x.at).toLocaleDateString(undefined, { weekday: 'long', month: 'short', day: 'numeric' });
    if (!groups.has(label)) groups.set(label, []);
    groups.get(label).push(x.track);
  }
  for (const [label, tracks] of groups) v.append(h('div', { class: 'section' }, sectionHead(label), h('div', { class: 'song-list' }, tracks.map((t, i) => songRow(t, { list: tracks, index: i, source: 'History' })))));
};

// ---- settings ----
// Rows for things the Android app doesn't do (windows, tray, Discord, Windows audio devices,
// sign-in and downloads for now) are left out there.
const MOBILE_HIDDEN = new Set(['Signed in to YouTube Music', 'Sign in to YouTube Music', 'Sync likes to YouTube Music', 'Mica window backdrop', 'Output device', 'Per-device sound profiles', 'Auto-tune new devices', 'Saved device profiles', 'Smart ducking', 'Lower music to', 'Focus mode', 'Discord Rich Presence', 'Downloaded songs', 'Auto-download liked songs', 'Open downloads folder', 'Keep running in the tray', 'Keyboard shortcuts']);
function settingRow({ ic, title, sub, control, onclick, disabled }) {
  if (api.mobile && MOBILE_HIDDEN.has(title)) return null;
  // A disabled row must not fire its click handler or its switch handler.
  const click = disabled ? null : onclick;
  if (disabled && control) { control.style.pointerEvents = 'none'; control.style.opacity = '.5'; }
  return h('div', { class: 'set-row' + (onclick ? ' click' : '') + (disabled ? ' disabled' : ''), onclick: click }, icon(ic), h('div', { class: 'sr-text' }, h('div', { class: 'sr-t' }, title), sub ? h('div', { class: 'sr-s' }, sub) : null), control);
}
const group = (title, ...rows) => { const list = rows.flat().filter(Boolean); return list.length ? h('div', { class: 'set-group' }, h('h3', null, title), h('div', { class: 'set-card' }, list)) : document.createDocumentFragment(); };
function sw(key, after) {
  const el = h('div', { class: 'switch' + (S()[key] ? ' on' : ''), role: 'switch', onclick: (e) => { e.stopPropagation(); setSetting(key, !S()[key]); el.classList.toggle('on', !!S()[key]); after?.(S()[key]); } });
  return el;
}
function seg(key, options, after) {
  const box = h('div', { class: 'seg' });
  const draw = () => box.replaceChildren(...options.map(([val, label]) => h('button', { class: S()[key] === val ? 'on' : '', onclick: () => { setSetting(key, val); draw(); after?.(val); } }, label)));
  draw();
  return box;
}
function selectCtl(key, options, after) {
  const el = h('select', { onchange: (e) => { setSetting(key, e.target.value); after?.(e.target.value); } }, options.map(([k, l]) => h('option', { value: k }, l)));
  el.value = S()[key];
  return el;
}
function rangeCtl(key, { min, max, step, fmt, after }) {
  const out = h('output', null, fmt(S()[key]));
  const r = rangeInput({ min, max, step, value: S()[key], oninput: (v) => { out.textContent = fmt(v); DB.settings[key] = v; after?.(v); }, onchange: () => persist('settings') });
  return h('div', { class: 'rng' }, r, out);
}

VIEWS.settings = async (ctx) => {
  const v = ctx.view;
  const signed = await api.authStatus().catch(() => false);
  const dlSize = await api.downloadsSize().catch(() => 0);
  if (!ctx.alive()) return;
  const s = S();
  v.append(h('div', { class: 'page-title' }, 'Settings'));

  v.append(group('Account',
    signed
      ? settingRow({ ic: 'account', title: 'Signed in to YouTube Music', sub: 'Home and library are personalised with your account', control: h('button', { class: 'btn outline', onclick: async () => { await api.signOut(); toast('Signed out'); invalidateCaches(); rerender(); } }, icon('logout'), 'Sign out') })
      : settingRow({ ic: 'login', title: 'Sign in to YouTube Music', sub: 'Optional. Personalised home, your YouTube library and like sync.', control: h('button', { class: 'btn filled', onclick: () => signInFlow() }, 'Sign in') }),
    settingRow({ ic: 'cached', title: 'Sync likes to YouTube Music', sub: 'Liking a song here also likes it on your account', control: sw('syncLikes'), disabled: !signed })));

  const accents = ['#bfc2f0', '#ffb3b5', '#ffb77c', '#e3c46d', '#a6d38d', '#7fd6c9', '#9ecaff', '#d5baff', '#ff8fc7'];
  const swatches = h('div', { class: 'swatches' }, accents.map((c) => h('div', { class: 'swatch' + (s.accent === c ? ' sel' : ''), style: { background: c }, onclick: (e) => { setSetting('accent', c); $$('.swatch').forEach((x) => x.classList.remove('sel')); e.currentTarget.classList.add('sel'); applyAppearance(); } })));
  v.append(group('Appearance',
    settingRow({ ic: 'palette', title: 'Theme', sub: 'Black uses pure black surfaces (great for OLED)', control: seg('theme', [['system', 'System'], ['light', 'Light'], ['dark', 'Dark'], ['black', 'Black']], () => { api.storeSet('settings', DB.settings); applyAppearance(); }) }),
    settingRow({ ic: 'palette', title: 'Dynamic colours', sub: 'Tint the app with colours from the song artwork', control: sw('dynamicColor', applyAppearance) }),
    settingRow({ ic: 'palette', title: 'Accent colour', sub: 'Used when dynamic colours are off or nothing is playing', control: swatches }),
    INFO.micaSupported ? settingRow({ ic: 'gradient', title: 'Mica window backdrop', sub: 'Windows 11 Fluent material behind the sidebar and title bar', control: sw('mica', () => { api.storeSet('settings', DB.settings); applyAppearance(); }) }) : null,
    settingRow({ ic: 'sliders', title: 'Motion style', sub: 'Classic: smooth and glowy. Expressive: Material 3 motion (sliding pages, pill controls, word-by-word lyrics)', control: seg('motionStyle', [['classic', 'Classic'], ['android', 'Android']], () => { applyAppearance(); updatePlayButtons(); if (P.lyrics && !$('#nowPlaying').hidden) renderLyrics(); }) }),
    settingRow({ ic: 'sliders', title: 'Squiggly seekbar', sub: 'Wavy progress bar in the now playing screen', control: sw('squiggly') }),
    settingRow({ ic: 'gridView', title: 'List density', control: seg('density', [['comfortable', 'Comfortable'], ['compact', 'Compact']], applyAppearance) })));

  const q = selectCtl('quality', [['best', 'High'], ['low', 'Low (saves data)']]);
  v.append(group('Player & audio',
    settingRow({ ic: 'quality', title: 'Audio quality', sub: 'Applies to the next song', control: q }),
    settingRow({ ic: 'crossfade', title: 'Crossfade', sub: 'Blend the end of a song into the next one', control: rangeCtl('crossfade', { min: 0, max: 12, step: 1, fmt: (x) => (x ? x + ' s' : 'Off') }) }),
    settingRow({ ic: 'crossfade', title: 'Crossfade style', sub: 'Smooth: the next song rises quickly while the old one clears out', control: seg('xfCurve', [['smooth', 'Smooth'], ['equalpower', 'Equal power'], ['linear', 'Linear']], () => applySound()) }),
    settingRow({ ic: 'crossfade', title: 'Smart crossfade', sub: 'Start the blend when a song really fades out, and skip silence at the start of the next one', control: sw('smartCrossfade') }),
    settingRow({ ic: 'album', title: 'Keep albums gapless', sub: "Don't crossfade between consecutive songs of the same album", control: sw('crossfadeGapless') }),
    settingRow({ ic: 'next', title: 'Crossfade when skipping', sub: 'Short blend when you press next or previous', control: sw('crossfadeOnSkip') }),
    settingRow({ ic: 'volume', title: 'Audio normalisation', sub: 'Keep loudness consistent between songs', control: sw('normalize', (x) => engine.setOptions({ normalize: x })) }),
    settingRow({ ic: 'ff', title: 'Skip silence', sub: 'Fast-forward through silent parts of songs', control: sw('skipSilence', (x) => { engine.setOptions({ skipSilence: x }); updateExtraButtons(); }) }),
    settingRow({ ic: 'next', title: 'Instantly skip silence', sub: 'Jump ahead during silent moments instead of speeding up', control: sw('silenceInstant', (x) => engine.setOptions({ silenceInstant: x })) }),
    settingRow({ ic: 'speed', title: 'Tempo & pitch', sub: `${s.tempo.toFixed(2)}× ${s.varispeed ? '(varispeed)' : ''}`, onclick: tempoDialog, control: icon('right') }),
    settingRow({ ic: 'equalizer', title: 'Sound', sub: [s.eq.enabled ? `EQ ${s.eq.preset}` : 'EQ off', s.headphone && s.headphoneOn !== false ? s.headphone.name : '', s.normalize !== false ? `${s.normTarget ?? -14} LUFS` : ''].filter(Boolean).join(' • '), onclick: () => go('equalizer'), control: icon('right') })));

  const info = OUT.info;
  const meta = DEVICE_INFO[info?.type || 'unknown'];
  const outSel = h('select', { style: { maxWidth: '260px' }, onchange: async (e) => { setSetting('outputDevice', e.target.value); try { await engine.setSink(e.target.value); } catch (er) { toast(er.message); } refreshDevices(true); } },
    OUT.devices.filter((d) => d.deviceId !== 'communications').map((d) => h('option', { value: d.deviceId }, d.deviceId === 'default' ? 'System default' : d.label || 'Unknown device')));
  outSel.value = S().outputDevice || 'default';
  const profiles = Object.entries(DB.deviceProfiles || {});
  v.append(group('Audio output & devices',
    settingRow({ ic: meta.icon, title: info?.label ? info.model : 'System default output', sub: [meta.name, info?.bluetooth ? 'Bluetooth' : '', info?.handsFree ? 'hands-free (call) mode — lower quality' : ''].filter(Boolean).join(' • '), control: h('button', { class: 'btn outline', onclick: () => go('equalizer') }, icon('eqIcon'), 'Sound') }),
    settingRow({ ic: 'speaker', title: 'Output device', sub: 'Where Soncle plays audio', control: outSel }),
    settingRow({ ic: 'headphones', title: 'Per-device sound profiles', sub: 'Remember EQ and volume separately for each headphone, earbud, speaker or car', control: sw('perDeviceSound', () => refreshDevices(false)) }),
    settingRow({ ic: 'autoEq', title: 'Auto-tune new devices', sub: 'Start new devices with an EQ matched to their type (earbuds, headphones, speaker, car, laptop…)', control: sw('autoDeviceEq') }),
    settingRow({ ic: 'volumeOff', title: 'Pause when headphones disconnect', sub: 'Stop music instead of switching to speakers', control: sw('pauseOnDisconnect') }),
    settingRow({ ic: 'ff', title: 'Bluetooth lyrics delay', sub: 'Compensates for wireless audio latency so synced lyrics line up', control: rangeCtl('btLyricsDelay', { min: 0, max: 500, step: 10, fmt: (x) => x + ' ms' }) }),
    settingRow({ ic: 'tune', title: 'Mono audio', sub: 'Combine left and right channels', control: sw('mono', (x) => engine.setChannel({ mono: x })) }),
    profiles.length ? settingRow({ ic: 'equalizer', title: 'Saved device profiles', sub: profiles.map(([k, p]) => `${k} (${DEVICE_INFO[p.type]?.name || 'device'}: ${p.eq?.enabled ? p.eq.preset : 'EQ off'})`).join(' • '), control: h('button', { class: 'text-btn', onclick: () => { DB.deviceProfiles = {}; persist('deviceProfiles'); toast('Device profiles cleared'); rerender(); } }, 'Clear') }) : null));

  v.append(group('Queue',
    settingRow({ ic: 'volumeLow', title: 'Smart ducking', sub: 'When another app plays sound — a video, a voice note, a call — lower the music (or pause it) until it stops. Windows', control: seg('smartDuck', [['duck', 'Lower'], ['pause', 'Pause'], ['off', 'Off']]) }),
    settingRow({ ic: 'volumeMute', title: 'Lower music to', control: rangeCtl('duckLevel', { min: 0.05, max: 0.6, step: 0.05, fmt: (x) => Math.round(x * 100) + '%' }) }),
    settingRow({ ic: 'focus', title: 'Focus mode', sub: 'Zen view, focus EQ and uninterrupted playback for a timed session (Ctrl+Shift+F). Your settings are restored afterwards', control: h('button', { class: 'btn outline', onclick: () => focusDialog() }, FOCUS ? 'End session' : 'Start') }),
    settingRow({ ic: 'radio', title: 'Flow radio', sub: 'Start radio arranges songs by tempo and key so crossfades blend instead of clashing', control: sw('flowRadio') }),
    settingRow({ ic: 'quality', title: 'Learn songs for Flow radio', sub: 'Analyses ~30 s of each song on this computer for tempo and key (about 0.5 MB of data per song) and remembers it', control: sw('flowLearn') }),
    settingRow({ ic: 'crossfade', title: 'Crossfade by how songs mix', sub: 'Use a short blend when two songs would clash', control: sw('flowXf') }),
    settingRow({ ic: 'clearAll', title: 'Forget learned songs', sub: 'Tempo and key data Flow radio has learned', control: h('button', { class: 'btn outline', onclick: async (e) => { await api.flowClear(); FLOW.feats.clear(); FLOW.asked.clear(); FLOW.failed.clear(); e.target.textContent = 'Cleared'; } }, 'Clear') }),
    settingRow({ ic: 'next', title: 'Autoplay', sub: 'Keep playing similar songs when the queue ends', control: sw('autoplay') }),
    settingRow({ ic: 'next', title: 'Skip songs that fail to play', control: sw('autoSkipOnError') }),
    settingRow({ ic: 'queue', title: 'Prevent duplicate songs in queue', control: sw('noDuplicates') }),
    settingRow({ ic: 'queue', title: 'Persistent queue', sub: 'Restore your queue when you reopen the app', control: sw('persistentQueue') }),
    settingRow({ ic: 'shuffle', title: 'Remember shuffle & repeat', control: sw('rememberShuffleRepeat') })));

  v.append(group('Sleep timer',
    settingRow({ ic: 'sleepFade', title: 'Fade out', sub: 'Gradually lower the volume during the last 30 seconds', control: sw('sleepFadeOut') }),
    settingRow({ ic: 'sleepAuto', title: 'Default duration', control: rangeCtl('sleepDefault', { min: 5, max: 120, step: 5, fmt: (x) => x + ' min' }) })));

  v.append(group('Lyrics',
    settingRow({ ic: 'lyrics', title: 'Text size', control: rangeCtl('lyricsSize', { min: 20, max: 48, step: 1, fmt: (x) => x + ' px', after: applyLyricsStyle }) }),
    settingRow({ ic: 'lyrics', title: 'Alignment', control: seg('lyricsAlign', [['left', 'Left'], ['center', 'Centre']], applyLyricsStyle) }),
    settingRow({ ic: 'lyrics', title: 'Auto-scroll', sub: 'Follow the current line', control: sw('lyricsAutoScroll') }),
    settingRow({ ic: 'lyrics', title: 'Glow effect', sub: 'Soft glow on the active line', control: sw('lyricsGlow') })));

  const lang = selectCtl('lang', [['en', 'English'], ['si', 'සිංහල'], ['ta', 'தமிழ்'], ['hi', 'हिन्दी'], ['es', 'Español'], ['fr', 'Français'], ['de', 'Deutsch'], ['pt', 'Português'], ['ja', '日本語'], ['ko', '한국어'], ['ar', 'العربية'], ['ru', 'Русский'], ['id', 'Indonesia'], ['tr', 'Türkçe'], ['it', 'Italiano']], () => { api.storeSet('settings', DB.settings); invalidateCaches(); });
  const loc = selectCtl('location', [['US', 'United States'], ['LK', 'Sri Lanka'], ['IN', 'India'], ['GB', 'United Kingdom'], ['CA', 'Canada'], ['AU', 'Australia'], ['DE', 'Germany'], ['FR', 'France'], ['JP', 'Japan'], ['KR', 'South Korea'], ['BR', 'Brazil'], ['ID', 'Indonesia'], ['AE', 'UAE'], ['SG', 'Singapore'], ['MY', 'Malaysia'], ['PK', 'Pakistan']], () => { api.storeSet('settings', DB.settings); invalidateCaches(); });
  v.append(group('Content',
    settingRow({ ic: 'language', title: 'Content language', sub: 'Language of titles and pages from YouTube Music', control: lang }),
    settingRow({ ic: 'location', title: 'Content country', sub: 'Region for charts, home and explore', control: loc })));

  if (INFO.discord) v.append(group('Integrations',
    settingRow({ ic: 'discord', title: 'Discord Rich Presence', sub: 'Show what you are listening to on your Discord profile (Discord desktop app must be running)', control: sw('discord', () => api.storeSet('settings', DB.settings)) })));

  v.append(group('Storage & downloads',
    settingRow({ ic: 'storage', title: 'Downloaded songs', sub: `${DL.done.size} songs • ${fmtBytes(dlSize)}`, onclick: () => go('library', { tab: 'downloads' }), control: icon('right') }),
    settingRow({ ic: 'download', title: 'Auto-download liked songs', control: sw('autoDownloadLiked', () => api.storeSet('settings', DB.settings)) }),
    settingRow({ ic: 'folder', title: 'Open downloads folder', onclick: () => api.openDownloads(), control: icon('external') }),
    settingRow({ ic: 'clearAll', title: 'Clear cache', sub: 'Image and YouTube session cache', onclick: async () => { await api.clearCache(); toast('Cache cleared'); }, control: null })));

  v.append(group('Backup & restore',
    settingRow({ ic: 'backup', title: 'Back up', sub: 'Save liked songs, playlists, library, history and settings to a file', onclick: async () => { try { if (await api.backup()) toast('Backup saved'); } catch (e) { toast(shortErr(e.message)); } }, control: null }),
    settingRow({ ic: 'restore', title: 'Restore', sub: 'Load a Soncle backup file (replaces current library)', onclick: async () => { try { await api.restore(); } catch (e) { toast(shortErr(e.message)); } }, control: null })));

  v.append(group('System',
    settingRow({ ic: 'tray', title: 'Keep running in the tray', sub: 'Closing the window keeps music playing; quit from the tray icon', control: sw('closeToTray', () => api.storeSet('settings', DB.settings)) })));

  v.append(group('Privacy',
    settingRow({ ic: 'deleteHistory', title: 'Clear listening history', sub: `${nOf(DB.history.length, 'entry', 'entries')}`, onclick: async () => { if (await confirmDialog('Clear history?', 'Your listening history in this app will be removed.', 'Clear')) { DB.history = []; persist('history'); toast('History cleared'); rerender(); } } }),
    settingRow({ ic: 'clearAll', title: 'Clear search history', sub: `${nOf(DB.searchHistory.length, 'entry', 'entries')}`, onclick: () => { DB.searchHistory = []; persist('searchHistory'); toast('Search history cleared'); rerender(); } })));

  v.append(group('About',
    settingRow({ ic: 'info', title: `Soncle ${INFO.version || ''}`, sub: 'An independent YouTube Music player for your desktop. Not affiliated with Google or YouTube.' }),
    settingRow({ ic: 'info', title: 'Open-source licences', sub: 'GPL-3.0-or-later, and the notices for the code, icons and data it includes', onclick: () => licencesDialog() }),
    settingRow({ ic: 'keyboard', title: 'Keyboard shortcuts', sub: 'Ctrl+K command bar • [ / ] lyrics earlier/later (Shift = 0.5 s) • N mini player • Space play/pause • Ctrl+←/→ previous/next • ←/→ seek 5 s • ↑/↓ volume • Ctrl+L like • Ctrl+S shuffle • Ctrl+R repeat • Ctrl+F search • F now playing • Q queue • M mute • F11 full screen' })));
};
function invalidateCaches() { for (const e of nav.stack) delete e.data; }

// ---- equalizer ----
VIEWS.equalizer = (ctx) => {
  const v = ctx.view;
  engine.ensureGraph();
  const eq = S().eq;
  DB.eqPresets = DB.eqPresets || [];
  const card = h('div', { class: 'eq-card' + (eq.enabled ? '' : ' off') });
  const canvas = h('canvas', { class: 'eq-graph' });
  const commit = debounce(() => { persist('settings'); saveDeviceProfile(); }, 300);
  let apply = () => { engine.setEq({ enabled: eq.enabled, preamp: eq.preamp, gains: eq.gains }); drawGraph(); commit(); updateExtraButtons(); };
  const userPreset = (name) => DB.eqPresets.find((p) => p.name === name);
  const loadPreset = (name) => {
    const u = userPreset(name);
    eq.preset = name;
    eq.gains = (u ? u.gains : EQ_PRESETS[name]).slice();
    eq.preamp = u ? u.preamp || 0 : PRESET_PREAMP[name] || 0;
    if (!eq.enabled) { eq.enabled = true; enable.classList.add('on'); card.classList.remove('off'); }
    drawPresets(); drawBands(); apply();
  };
  const presetRow = h('div', { class: 'eq-presets' });
  const drawPresets = () => {
    const chip = (p, mine) => h('button', { class: 'chip' + (eq.preset === p ? ' active' : ''), onclick: () => loadPreset(p) }, mine ? icon('person') : null, p);
    const builtIn = Object.keys(EQ_PRESETS);
    presetRow.replaceChildren(...[
      DB.eqPresets.length ? h('div', { class: 'eq-plabel' }, 'Your presets') : null,
      DB.eqPresets.length ? h('div', { class: 'chips wrap' }, DB.eqPresets.map((p) => chip(p.name, true))) : null,
      h('div', { class: 'eq-plabel' }, 'Presets'),
      h('div', { class: 'chips wrap' }, builtIn.map((p) => chip(p, false)), eq.preset === 'Custom' ? h('button', { class: 'chip active' }, 'Custom') : null),
      h('div', { class: 'actions', style: { marginTop: '12px' } },
        h('button', { class: 'btn tonal', onclick: savePreset }, icon('save'), 'Save as preset'),
        userPreset(eq.preset) ? h('button', { class: 'btn outline', onclick: deletePreset }, icon('delete'), 'Delete preset') : null)].filter(Boolean));
  };
  async function savePreset() {
    const name = await promptDialog('Save EQ preset', userPreset(eq.preset) ? eq.preset : '', 'Save', 'Preset name');
    if (!name) return;
    if (EQ_PRESETS[name]) return toast('That name is used by a built-in preset');
    DB.eqPresets = DB.eqPresets.filter((p) => p.name !== name);
    DB.eqPresets.unshift({ name, gains: eq.gains.slice(), preamp: eq.preamp });
    persist('eqPresets');
    eq.preset = name;
    drawPresets(); apply();
    toast(`Saved preset "${name}"`);
  }
  async function deletePreset() {
    const name = eq.preset;
    if (!(await confirmDialog('Delete preset?', `"${name}" will be removed.`))) return;
    DB.eqPresets = DB.eqPresets.filter((p) => p.name !== name);
    persist('eqPresets');
    eq.preset = 'Custom';
    drawPresets(); apply();
  }
  const markCustom = () => { if (eq.preset !== 'Custom' && !userPreset(eq.preset)) { eq.preset = 'Custom'; drawPresets(); } else if (userPreset(eq.preset)) { eq.preset = 'Custom'; drawPresets(); } };
  const bandsEl = h('div', { class: 'eq-bands' });
  const fmtDb = (x) => (x > 0 ? '+' : '') + x.toFixed(1);
  const drawBands = () => {
    bandsEl.replaceChildren(
      h('div', { class: 'eq-band' }, h('span', { class: 'val' }, fmtDb(eq.preamp)), vRange(eq.preamp, (x, lab) => { eq.preamp = x; lab.textContent = fmtDb(x); apply(); }), h('span', { class: 'hz' }, 'Preamp')),
      ...EQ_BANDS.map((f, i) => h('div', { class: 'eq-band' },
        h('span', { class: 'val' }, fmtDb(eq.gains[i])),
        vRange(eq.gains[i], (x, lab) => { eq.gains[i] = x; lab.textContent = fmtDb(x); markCustom(); apply(); }),
        h('span', { class: 'hz' }, f >= 1000 ? f / 1000 + 'k' : f))));
  };
  function vRange(value, on) {
    const r = rangeInput({ min: -12, max: 12, step: 0.5, value, oninput: (x) => on(x, r.parentElement.querySelector('.val')) });
    r.addEventListener('dblclick', () => { r.value = 0; r.dispatchEvent(new Event('input')); });
    return r;
  }
  // Graph: smooth response curve with draggable band handles (like a parametric EQ)
  const PAD_L = 52, PAD_R = 16, PAD_T = 14, PAD_B = 28;
  const fx = (f, W) => PAD_L + (Math.log10(f / 20) / Math.log10(1000)) * (W - PAD_L - PAD_R);
  const gy = (db, H) => PAD_T + (1 - (db + 12) / 24) * (H - PAD_T - PAD_B);
  let dragBand = -1, hoverBand = -1;
  function drawGraph() {
    const dpr = devicePixelRatio || 1;
    const W = canvas.clientWidth || 900, H = canvas.clientHeight || 220;
    canvas.width = W * dpr; canvas.height = H * dpr;
    const c = canvas.getContext('2d');
    c.scale(dpr, dpr);
    const cs = getComputedStyle(document.documentElement);
    const ov = cs.getPropertyValue('--ov').trim();
    const primary = cs.getPropertyValue('--primary').trim();
    c.font = '600 11px "Segoe UI", system-ui, sans-serif';
    c.fillStyle = `rgb(${ov} / .6)`;
    c.textAlign = 'right';
    for (const db of [12, 6, 0, -6, -12]) {
      const y = gy(db, H);
      c.strokeStyle = `rgb(${ov} / ${db === 0 ? .22 : .08})`;
      c.lineWidth = 1;
      c.beginPath(); c.moveTo(PAD_L, y); c.lineTo(W - PAD_R, y); c.stroke();
      c.fillText((db > 0 ? '+' : '') + db + 'dB', PAD_L - 8, y + 4);
    }
    c.textAlign = 'center';
    EQ_BANDS.forEach((f) => {
      const x = fx(f, W);
      c.strokeStyle = `rgb(${ov} / .06)`;
      c.beginPath(); c.moveTo(x, PAD_T); c.lineTo(x, H - PAD_B); c.stroke();
      c.fillText(f >= 1000 ? f / 1000 + 'kHz' : f + 'Hz', x, H - 8);
    });
    const n = 240, freqs = new Float32Array(n);
    for (let i = 0; i < n; i++) freqs[i] = 20 * Math.pow(1000, i / (n - 1));
    const mags = engine.eqResponse(freqs);
    const pre = 0;
    if (S().headphone && S().headphoneOn !== false) {
      const cm = engine.correctionResponse(freqs), cpre = S().headphone.preamp || 0;   // shape only, without its safety preamp
      for (let i = 0; i < n; i++) cm[i] -= cpre;
      c.save(); c.setLineDash([5, 5]); c.strokeStyle = `rgb(${ov} / .55)`; c.lineWidth = 2;
      c.beginPath();
      for (let i = 0; i < n; i++) { const x = fx(freqs[i], W), y = gy(Math.max(-12, Math.min(12, cm[i])), H); i ? c.lineTo(x, y) : c.moveTo(x, y); }
      c.stroke(); c.restore();
      c.textAlign = 'left'; c.fillStyle = `rgb(${ov} / .6)`; c.fillText('- - headphone correction', PAD_L + 6, PAD_T + 12);
    }
    c.beginPath();
    for (let i = 0; i < n; i++) { const x = fx(freqs[i], W), y = gy(Math.max(-12, Math.min(12, mags[i] + pre)), H); i ? c.lineTo(x, y) : c.moveTo(x, y); }
    c.save();
    c.lineTo(fx(20000, W), H - PAD_B); c.lineTo(fx(20, W), H - PAD_B); c.closePath();
    const g = c.createLinearGradient(0, PAD_T, 0, H - PAD_B);
    g.addColorStop(0, primary.replace(/\)$/, ' / .42)')); g.addColorStop(1, primary.replace(/\)$/, ' / .02)'));
    c.fillStyle = g; c.fill(); c.restore();
    c.beginPath();
    for (let i = 0; i < n; i++) { const x = fx(freqs[i], W), y = gy(Math.max(-12, Math.min(12, mags[i] + pre)), H); i ? c.lineTo(x, y) : c.moveTo(x, y); }
    c.strokeStyle = primary; c.lineWidth = 3.5; c.lineJoin = 'round'; c.lineCap = 'round'; c.stroke();
    EQ_BANDS.forEach((f, i) => {
      const x = fx(f, W), y = gy(eq.enabled ? eq.gains[i] + pre : 0, H);
      c.beginPath(); c.arc(x, y, i === dragBand || i === hoverBand ? 8 : 5.5, 0, Math.PI * 2);
      c.fillStyle = i === dragBand ? primary : '#fff'; c.fill();
      c.lineWidth = 2; c.strokeStyle = primary; c.stroke();
    });
  }
  const bandAt = (e) => {
    const r = canvas.getBoundingClientRect();
    const x = e.clientX - r.left;
    let best = -1, bd = 1e9;
    EQ_BANDS.forEach((f, i) => { const d = Math.abs(fx(f, r.width) - x); if (d < bd) { bd = d; best = i; } });
    return bd < 40 ? best : -1;
  };
  const gainAt = (e) => {
    const r = canvas.getBoundingClientRect();
    const y = e.clientY - r.top;
    const db = 12 - ((y - PAD_T) / (r.height - PAD_T - PAD_B)) * 24;
    return Math.round(Math.max(-12, Math.min(12, db)) * 2) / 2;
  };
  canvas.addEventListener('pointerdown', (e) => {
    const b = bandAt(e);
    if (b < 0) return;
    if (!eq.enabled) { eq.enabled = true; enable.classList.add('on'); card.classList.remove('off'); }
    dragBand = b; canvas.setPointerCapture(e.pointerId);
    eq.gains[b] = gainAt(e); markCustom(); drawBands(); apply();
  });
  canvas.addEventListener('pointermove', (e) => {
    if (dragBand >= 0) { eq.gains[dragBand] = gainAt(e); apply(); const vals = bandsEl.querySelectorAll('.eq-band'); const band = vals[dragBand + 1]; if (band) { band.querySelector('.val').textContent = fmtDb(eq.gains[dragBand]); const r = band.querySelector('input'); r.value = eq.gains[dragBand]; r.style.setProperty('--fill', ((eq.gains[dragBand] + 12) / 24) * 100 + '%'); } return; }
    const hb = bandAt(e);
    if (hb !== hoverBand) { hoverBand = hb; canvas.style.cursor = hb >= 0 ? 'ns-resize' : 'default'; drawGraph(); }
  });
  canvas.addEventListener('pointerup', () => { dragBand = -1; drawGraph(); });
  canvas.addEventListener('dblclick', (e) => { const b = bandAt(e); if (b >= 0) { eq.gains[b] = 0; markCustom(); drawBands(); apply(); } });

  const enable = h('div', { class: 'switch' + (eq.enabled ? ' on' : ''), onclick: () => { eq.enabled = !eq.enabled; enable.classList.toggle('on', eq.enabled); card.classList.toggle('off', !eq.enabled); apply(); } });
  const info = OUT.info;
  const devMeta = DEVICE_INFO[info?.type || 'unknown'];
  const profileChip = S().perDeviceSound && info?.label
    ? h('div', { class: 'dev-chip' }, icon(devMeta.icon), h('span', null, 'Sound profile for ', h('b', null, info.model)), info.bluetooth ? h('span', { class: 'bt-tag' }, icon('bluetooth'), 'Bluetooth') : null, h('span', { class: 'muted' }, ' • ' + devMeta.name))
    : h('div', { class: 'dev-chip' }, icon('equalizer'), h('span', null, 'Global equalizer'));
  // channel controls
  const monoSw = h('div', { class: 'switch' + (S().mono ? ' on' : ''), onclick: () => { setSetting('mono', !S().mono); monoSw.classList.toggle('on', S().mono); engine.setChannel({ mono: S().mono }); } });
  const balOut = h('output', null, balLabel(S().balance));
  const bal = rangeInput({ min: -1, max: 1, step: 0.05, value: S().balance || 0, oninput: (x) => { DB.settings.balance = x; balOut.textContent = balLabel(x); engine.setChannel({ balance: x }); }, onchange: () => persist('settings') });
  bal.addEventListener('dblclick', () => { bal.value = 0; bal.dispatchEvent(new Event('input')); persist('settings'); });
  card.append(
    h('div', { class: 'eq-top' },
      h('div', null, profileChip, h('div', { style: { color: 'var(--on-surface-var)', fontSize: '13px', marginTop: '8px' } }, 'Drag the points on the curve or use the sliders. Double-click to reset a band.')),
      h('div', { style: { display: 'flex', gap: '10px', alignItems: 'center' } }, h('button', { class: 'text-btn', onclick: () => { eq.preset = 'Flat'; eq.gains = EQ_PRESETS.Flat.slice(); eq.preamp = 0; drawPresets(); drawBands(); apply(); } }, 'Reset'), enable)),
    canvas, presetRow, h('div', { style: { height: '22px' } }), bandsEl);
  const chan = h('div', { class: 'eq-card', style: { marginTop: '18px' } },
    h('div', { style: { fontSize: '16px', fontWeight: 650, marginBottom: '8px' } }, 'Channels'),
    h('div', { class: 'range-row', style: { display: 'flex', alignItems: 'center', gap: '14px', margin: '10px 0' } }, h('span', { style: { width: '90px', color: 'var(--on-surface-var)' } }, 'Balance'), h('span', { class: 'muted' }, 'L'), bal, h('span', { class: 'muted' }, 'R'), balOut),
    h('div', { style: { display: 'flex', alignItems: 'center', gap: '14px' } }, h('span', { style: { flex: 1 } }, 'Mono audio', h('div', { class: 'muted', style: { fontSize: '13px' } }, 'Combine left and right channels — handy with one earbud')), monoSw));
  // sound: headroom, loudness compensation, stereo width, crossfeed
  const soundSet = (k, val) => { DB.settings[k] = val; persist('settings'); applySound(); saveDeviceProfile(); drawHeadroom(); };
  const swRow = (k, title, sub, def = false) => {
    const el = h('div', { class: 'switch' + ((S()[k] ?? def) ? ' on' : ''), onclick: () => { soundSet(k, !(S()[k] ?? def)); el.classList.toggle('on', S()[k]); } });
    return h('div', { class: 'snd-row' }, h('div', { style: { flex: 1 } }, title, h('div', { class: 'muted', style: { fontSize: '13px' } }, sub)), el);
  };
  const widthOut = h('output', null, Math.round((S().stereoWidth ?? 1) * 100) + '%');
  const width = rangeInput({ min: 0, max: 1.6, step: 0.05, value: S().stereoWidth ?? 1, oninput: (x) => { DB.settings.stereoWidth = x; widthOut.textContent = Math.round(x * 100) + '%'; applySound(); }, onchange: () => { persist('settings'); saveDeviceProfile(); } });
  width.addEventListener('dblclick', () => { width.value = 1; width.dispatchEvent(new Event('input')); persist('settings'); saveDeviceProfile(); });
  const xfeed = h('div', { class: 'seg' });
  const drawXfeed = () => xfeed.replaceChildren(...[['off', 'Off'], ['light', 'Light'], ['strong', 'Strong']].map(([k, l]) => h('button', { class: (S().crossfeed || 'off') === k ? 'on' : '', onclick: () => { soundSet('crossfeed', k); drawXfeed(); } }, l)));
  drawXfeed();
  const sound = h('div', { class: 'eq-card', style: { marginTop: '18px' } },
    h('div', { style: { fontSize: '16px', fontWeight: 650, marginBottom: '8px' } }, 'Sound'),
    swRow('autoHeadroom', 'Clip protection (auto headroom)', 'Lowers the preamp by exactly the EQ\u2019s biggest boost, so boosting never distorts. A transparent peak limiter catches anything left.', true),
    swRow('loudnessComp', 'Loudness compensation', 'At low volume your ears lose bass and treble; this adds them back gradually as you turn the volume down.'),
    h('div', { class: 'snd-row' }, h('div', { style: { width: '160px' } }, 'Stereo width', h('div', { class: 'muted', style: { fontSize: '13px' } }, '100% = original')), width, widthOut),
    h('div', { class: 'snd-row' }, h('div', { style: { flex: 1 } }, 'Headphone crossfeed', h('div', { class: 'muted', style: { fontSize: '13px' } }, 'Blends a little of each channel into the other, like speakers in a room. Less fatigue on hard-panned songs.')), xfeed));
  const headroomLbl = h('span', { class: 'muted', style: { fontSize: '13px' } });
  function drawHeadroom() { const hd = engine.headroomDb(); headroomLbl.textContent = hd < -0.05 ? `Headroom ${hd.toFixed(1)} dB` : ''; }
  card.querySelector('.eq-top > div:last-child').prepend(headroomLbl);
  const origApply = apply;
  apply = () => { origApply(); drawHeadroom(); };
  drawHeadroom();
  const extras = soundPage(v, drawGraph);
  v.append(h('div', { class: 'page-title' }, 'Sound'), extras.top, extras.headphones, card, extras.loudness, extras.transitions, sound, chan);
  drawPresets(); drawBands();
  requestAnimationFrame(drawGraph);
  new ResizeObserver(() => drawGraph()).observe(canvas);
};
// ---- Sound page: meters + A/B, headphone correction, loudness, transitions ----
const HP_TYPES = ['earbuds', 'headphones', 'wired'];
async function applyHeadphone(entry, { quiet = false } = {}) {
  const prof = await api.hpProfile(entry.path);
  DB.settings.headphone = { ...prof, name: entry.name, source: entry.source, rig: entry.rig || '' };
  DB.settings.headphoneOn = true;
  persist('settings'); applySound(); saveDeviceProfile();
  if (!quiet) toast(`Headphone correction: ${entry.name}`);
}
// When personal audio connects without a correction yet, offer the measured profile once.
async function suggestHeadphoneProfile(info) {
  if (!info?.label || S().hpSuggest === false) return;
  if (!(HP_TYPES.includes(info.type) || info.bluetooth)) return;
  DB.hpAsked = DB.hpAsked || {};
  const key = OUT.key || info.model;
  if (DB.hpAsked[key]) return;
  const m = await api.hpMatch(info.model).catch(() => null);
  if (!m || S().headphone) return;
  DB.hpAsked[key] = Date.now(); persist('hpAsked');
  toast(`Measured sound correction found for ${m.name}`, { label: 'Turn on', fn: () => applyHeadphone(m).catch((e) => toast('Could not load it: ' + shortErr(e.message))) });
}
function soundPage(view, redraw) {
  const fmt = (x, d = 1) => (x == null || !isFinite(x) ? '–' : (x > 0 ? '+' : '') + x.toFixed(d));
  // live meters + hold-to-compare
  const mMom = h('b'), mPeak = h('b'), mRed = h('b'), bar = h('i'), song = h('span', { class: 'muted' });
  const ab = h('button', { class: 'btn tonal ab-btn', title: 'Hold to hear the music without any of your sound settings' }, icon('compare'), 'Hold for original');
  const abOn = (on) => { engine.setBypass(on); ab.classList.toggle('on', on); };
  ab.addEventListener('pointerdown', (e) => { ab.setPointerCapture(e.pointerId); abOn(true); });
  for (const ev of ['pointerup', 'pointercancel', 'lostpointercapture']) ab.addEventListener(ev, () => abOn(false));
  const top = h('div', { class: 'snd-meter' },
    h('div', { class: 'snd-mbar' }, bar),
    h('div', { class: 'snd-mvals' },
      h('span', null, mMom, ' LUFS'), h('span', null, 'Peak ', mPeak, ' dBTP'), h('span', { title: 'How much the limiter is turning down peaks right now' }, 'Limiter ', mRed, ' dB'), song),
    ab);
  let lastSong = 0;
  engine.setMeters((m) => {
    if (!view.isConnected) { engine.setMeters(null); engine.setBypass(false); return; }
    const mom = m.momentary;
    mMom.textContent = isFinite(mom) ? mom.toFixed(1) : '–';
    mPeak.textContent = isFinite(m.truePeak) ? m.truePeak.toFixed(1) : '–';
    mRed.textContent = m.reduction < -0.05 ? m.reduction.toFixed(1) : '0';
    top.classList.toggle('limiting', m.reduction < -0.5);
    bar.style.transform = `scaleX(${isFinite(mom) ? Math.max(0, Math.min(1, (mom + 40) / 40)) : 0})`;
    if (Date.now() - lastSong > 1000) {
      lastSong = Date.now();
      const d = engine.deck, l = d.lufs ?? d.measured;
      song.textContent = !P.playing ? '' : S().normalize === false ? 'Normalisation off' : l == null ? 'Measuring this song…' : `This song ${l.toFixed(1)} LUFS → ${fmt(d.normDb)} dB`;
    }
  });

  // headphone correction
  const hp = h('div', { class: 'eq-card', style: { marginTop: '18px' } });
  const drawHp = async () => {
    const cur = S().headphone, on = S().headphoneOn !== false;
    const sw = h('div', { class: 'switch' + (cur && on ? ' on' : ''), onclick: () => { if (!cur) return input.focus(); DB.settings.headphoneOn = !on; persist('settings'); applySound(); saveDeviceProfile(); drawHp(); redraw(); } });
    const input = h('input', { class: 'hp-search', placeholder: 'Search your headphones or earphones — e.g. "WH-1000XM4", "AirPods Pro"', spellcheck: false });
    const results = h('div', { class: 'hp-results' });
    const run = debounce(async () => {
      const q = input.value.trim();
      if (q.length < 2) return results.replaceChildren();
      results.replaceChildren(h('div', { class: 'muted hp-note' }, 'Searching…'));
      try {
        const list = await api.hpSearch(q);
        results.replaceChildren(...(list.length ? list.slice(0, 12).map((r) => h('button', { class: 'hp-item', onclick: async () => { results.replaceChildren(h('div', { class: 'muted hp-note' }, 'Loading profile…')); try { await applyHeadphone(r); drawHp(); redraw(); } catch (e) { results.replaceChildren(h('div', { class: 'muted hp-note' }, "Couldn't load it: " + shortErr(e.message))); } } },
          icon(r.type === 'over-ear' ? 'headphones' : 'earbuds'), h('span', { class: 'hp-name' }, r.name), h('span', { class: 'muted' }, r.source + (r.rig ? ' · ' + r.rig : '')))) : [h('div', { class: 'muted hp-note' }, 'No measurements for that name. Try just the model, e.g. "HD 600".')]));
      } catch (e) { results.replaceChildren(h('div', { class: 'muted hp-note' }, navigator.onLine ? "Couldn't reach the AutoEq database: " + shortErr(e.message) : 'Connect to the internet once to download the headphone list.')); }
    }, 250);
    input.addEventListener('input', run);
    const suggest = h('div');
    hp.replaceChildren(
      h('div', { class: 'snd-row', style: { paddingTop: 0 } },
        h('div', { style: { flex: 1 } }, h('div', { class: 'snd-title' }, 'Headphone correction'),
          h('div', { class: 'muted', style: { fontSize: '13px' } }, cur
            ? `${cur.name} · measured by ${cur.source}${cur.rig ? ' on ' + cur.rig : ''} · ${cur.filters.length} filters, ${fmt(cur.preamp)} dB`
            : 'Flattens your headphones’ own sound using lab measurements (AutoEq, ~9,000 models), so the EQ you set is what you hear.')),
        sw),
      suggest, input, results);
    const info = OUT.info;
    if (!cur && info?.label && (HP_TYPES.includes(info.type) || info.bluetooth)) {
      const m = await api.hpMatch(info.model).catch(() => null);
      if (m && hp.isConnected) suggest.replaceChildren(h('button', { class: 'chip hp-suggest', onclick: async () => { try { await applyHeadphone(m); drawHp(); redraw(); } catch (e) { toast("Couldn't load it: " + shortErr(e.message)); } } }, icon('headphones'), `Use ${m.name} (${m.source})`));
    }
  };
  drawHp();

  // loudness
  const target = S().normTarget ?? -14;
  const setNorm = (patch) => { Object.assign(DB.settings, patch); persist('settings'); engine.setOptions({ normalize: S().normalize !== false, normTarget: S().normTarget ?? -14 }); drawLoud(); };
  const loud = h('div', { class: 'eq-card', style: { marginTop: '18px' } });
  const drawLoud = () => {
    const on = S().normalize !== false, tg = S().normTarget ?? target;
    loud.replaceChildren(
      h('div', { class: 'snd-row', style: { paddingTop: 0 } },
        h('div', { style: { flex: 1 } }, h('div', { class: 'snd-title' }, 'Loudness'),
          h('div', { class: 'muted', style: { fontSize: '13px' } }, 'Every song plays at the same loudness (measured in LUFS, like Spotify and YouTube). Quiet songs are lifted too; a true-peak limiter makes sure that never clips.')),
        h('div', { class: 'switch' + (on ? ' on' : ''), onclick: () => setNorm({ normalize: !on }) })),
      h('div', { class: 'seg' + (on ? '' : ' disabled') }, [[-19, 'Quiet', 'for late nights and background'], [-14, 'Normal', 'the streaming standard'], [-11, 'Loud', 'for noisy places']].map(([v, l, sub]) =>
        h('button', { class: tg === v ? 'on' : '', title: `${v} LUFS — ${sub}`, onclick: () => setNorm({ normTarget: v, normalize: true }) }, l, h('small', null, ` ${v} LUFS`)))));
  };
  drawLoud();

  // transitions
  const tr = h('div', { class: 'eq-card', style: { marginTop: '18px' } });
  const drawTr = () => {
    const xf = S().crossfade || 0, style = S().xfStyle || 'smart';
    const out = h('output', null, xf ? xf + ' s' : 'Off');
    const slider = rangeInput({ min: 0, max: 12, step: 1, value: xf, oninput: (x) => { out.textContent = x ? x + ' s' : 'Off'; }, onchange: (x) => { setSetting('crossfade', x); drawTr(); } });
    tr.replaceChildren(
      h('div', { class: 'snd-title' }, 'Transitions'),
      h('div', { class: 'snd-row' }, h('div', { style: { width: '160px' } }, 'Crossfade', h('div', { class: 'muted', style: { fontSize: '13px' } }, 'Off = gapless')), slider, out),
      h('div', { class: 'seg' + (xf ? '' : ' disabled') }, [['smart', 'Smart mix'], ['classic', 'Classic fade']].map(([k, l]) => h('button', { class: style === k ? 'on' : '', onclick: () => { setSetting('xfStyle', k); drawTr(); } }, l))),
      h('div', { class: 'muted', style: { fontSize: '13px', margin: '10px 0 4px' } }, style === 'smart'
        ? 'When two songs go well together (tempo and key, learned on this PC) they’re blended like a DJ would: 16 beats long, bass lines swapped in the middle so they never clash. Songs that would clash get a short fade instead.'
        : 'A plain crossfade of the length you set, using the curve chosen in Settings.'),
      h('div', { class: 'snd-row' }, h('div', { style: { flex: 1 } }, 'Gapless albums', h('div', { class: 'muted', style: { fontSize: '13px' } }, 'Songs from the same album run straight into each other, with no gap and no fade (live and concept albums).')),
        h('div', { class: 'switch' + (S().crossfadeGapless !== false ? ' on' : ''), onclick: () => { setSetting('crossfadeGapless', S().crossfadeGapless === false); drawTr(); } })));
  };
  drawTr();
  return { top, headphones: hp, loudness: loud, transitions: tr };
}
function balLabel(x) { x = Number(x) || 0; return Math.abs(x) < 0.03 ? 'Centre' : (x < 0 ? 'L ' : 'R ') + Math.round(Math.abs(x) * 100) + '%'; }

// ---- listening stats ----
VIEWS.stats = (ctx, { range = '30' }) => {
  const v = ctx.view;
  v.append(h('div', { class: 'page-title' }, 'Your stats'));
  v.append(h('div', { class: 'chips', style: { marginBottom: '18px' } }, [['7', 'Last 7 days'], ['30', 'Last 30 days'], ['365', 'This year'], ['all', 'All time']].map(([k, l]) => h('button', { class: 'chip' + (k === range ? ' active' : ''), onclick: () => go('stats', { range: k }, { replace: true }) }, l))));
  const since = range === 'all' ? 0 : Date.now() - Number(range) * 864e5;
  const hist = DB.history.filter((x) => x.at >= since && x.track);
  if (!hist.length) { v.append(emptyState('stats', 'No listening data yet', 'Play some music and your stats will appear here.')); return; }
  const songs = new Map(), artists = new Map(), albums = new Map(), hours = new Array(24).fill(0);
  let secs = 0;
  for (const { track: t, at } of hist) {
    // Only credit time for songs that actually have a known duration; guessing a
    // flat 180s per play inflated "Listening time" for skipped tracks.
    secs += t.duration || 0;
    hours[new Date(at).getHours()]++;
    const s = songs.get(t.id) || { t, n: 0 }; s.n++; songs.set(t.id, s);
    for (const a of (t.artists || []).slice(0, 1)) { if (!a.name) continue; const k = a.id || a.name; const x = artists.get(k) || { a, n: 0, thumb: t.thumb }; x.n++; artists.set(k, x); }
    if (t.album?.id) { const x = albums.get(t.album.id) || { al: t.album, n: 0, thumb: t.thumb, artist: artistNames(t) }; x.n++; albums.set(t.album.id, x); }
  }
  const top = (m, k = 10) => [...m.values()].sort((a, b) => b.n - a.n).slice(0, k);
  const hrs = Math.floor(secs / 3600), mins = Math.round((secs % 3600) / 60);
  const tile = (big, small) => h('div', { class: 'stat-tile' }, h('div', { class: 'stat-big' }, big), h('div', { class: 'muted' }, small));
  v.append(h('div', { class: 'stat-tiles' }, tile(hrs ? `${hrs}h ${mins}m` : `${mins}m`, 'Listening time'), tile(String(hist.length), 'Plays'), tile(String(songs.size), 'Different songs'), tile(String(artists.size), 'Artists')));
  const topSongs = top(songs);
  v.append(h('div', { class: 'section' }, sectionHead('Top songs', { actions: [h('button', { class: 'btn outline', onclick: () => playList(topSongs.map((x) => x.t), 0, { source: 'Your top songs' }) }, icon('play'), 'Play')] }),
    h('div', { class: 'song-list' }, topSongs.map((x, i) => { const row = songRow(x.t, { list: topSongs.map((y) => y.t), index: i, source: 'Your top songs' }); row.querySelector('.s-dur').textContent = x.n + (x.n === 1 ? ' play' : ' plays'); return row; }))));
  const topArtists = top(artists, 12);
  v.append(h('div', { class: 'section' }, sectionHead('Top artists'), h('div', { class: 'row-scroll' }, topArtists.map((x) => { const c = card({ type: 'artist', id: x.a.id, title: x.a.name, thumb: x.thumb, subtitle: `${x.n} plays` }); return c; }))));
  const topAlbums = top(albums, 12);
  if (topAlbums.length) v.append(h('div', { class: 'section' }, sectionHead('Top albums'), h('div', { class: 'row-scroll' }, topAlbums.map((x) => card({ type: 'album', id: x.al.id, title: x.al.name, thumb: x.thumb, subtitle: `${x.artist} • ${x.n} plays` })))));
  const max = Math.max(...hours, 1);
  v.append(h('div', { class: 'section' }, sectionHead('When you listen'),
    h('div', { class: 'hour-chart' }, hours.map((n, i) => h('div', { class: 'hour-col', title: `${i}:00 — ${n} plays` }, h('div', { class: 'hour-bar', style: { height: Math.max(3, (n / max) * 100) + '%' } }), h('span', null, i % 3 === 0 ? String(i) : ''))))));
};

// ================= audio output devices =================
const OUT = { devices: [], info: null, key: '', bt: [] };
async function refreshDevices(announce = false) {
  let list = [];
  try { list = (await navigator.mediaDevices.enumerateDevices()).filter((d) => d.kind === 'audiooutput'); } catch {}
  OUT.bt = await api.btDevices().catch(() => []);
  OUT.devices = list;
  const sel = S().outputDevice || 'default';
  let dev = list.find((d) => d.deviceId === sel);
  if (!dev && sel !== 'default') { setSetting('outputDevice', 'default'); engine.setSink('default').catch(() => {}); }
  dev = dev || list.find((d) => d.deviceId === 'default') || list[0];
  const info = classifyOutput(dev?.label || '', OUT.bt);
  const key = info.label ? info.model : '';
  const changed = key !== OUT.key;
  const prev = OUT.info;
  OUT.info = info;
  OUT.key = key;
  updateDeviceButton();
  if (changed) onDeviceChanged(prev, announce);
}
function onDeviceChanged(prev, announce) {
  const info = OUT.info, meta = DEVICE_INFO[info.type];
  // pause when personal audio disconnects and output falls back to speakers
  const personal = (i) => i && (i.bluetooth || ['earbuds', 'headphones', 'wired'].includes(i.type));
  if (announce && S().pauseOnDisconnect !== false && personal(prev) && !personal(info) && P.playing) { engine.pause(); toast(`${prev.model} disconnected — paused`); }
  let applied = '';
  if (S().perDeviceSound && OUT.key) {
    DB.deviceProfiles = DB.deviceProfiles || {};
    const prof = DB.deviceProfiles[OUT.key];
    // headphone correction belongs to one pair of headphones: never carry it over to another output
    DB.settings.headphone = prof?.sound?.headphone ?? null;
    applySound();
    if (!prof?.sound?.headphone) suggestHeadphoneProfile(info);
    if (prof?.eq) {
      DB.settings.eq = structuredClone(prof.eq);
      applied = prof.eq.enabled ? prof.eq.preset : 'EQ off';
      if (prof.volume != null) setVolume(prof.volume, false);
      if (prof.sound) { for (const k of SOUND_KEYS) if (prof.sound[k] !== undefined) DB.settings[k] = prof.sound[k]; applySound(); }
    } else if (S().autoDeviceEq && meta.preset !== 'Flat') {
      DB.settings.eq = { enabled: true, preset: meta.preset, preamp: PRESET_PREAMP[meta.preset] || 0, gains: EQ_PRESETS[meta.preset].slice() };
      applied = meta.preset;
      saveDeviceProfile();
    }
    engine.setEq(S().eq);
    persist('settings');
    updateExtraButtons();
  }
  if (announce && info.label) toast(`${info.bluetooth ? 'Bluetooth ' : ''}${meta.name.toLowerCase()} connected: ${info.model}${applied ? ` • ${applied} sound` : ''}`);
  if (info.handsFree) setTimeout(() => toast(`${info.model} is in hands-free (call) mode — pick its "Headphones/Stereo" output for full quality`, { label: 'Choose', fn: () => outputMenu({ clientX: innerWidth - 300, clientY: innerHeight - 120 }) }), announce ? 2800 : 0);
  const top = nav.stack[nav.pos];
  if (top && (top.name === 'equalizer' || top.name === 'settings')) rerender();
  lyricIdx = -2;
}
const SOUND_KEYS = ['autoHeadroom', 'loudnessComp', 'stereoWidth', 'crossfeed', 'headphone', 'headphoneOn'];
function applySound() {
  const s = S();
  engine.setSound({ autoHeadroom: s.autoHeadroom !== false, loudnessComp: !!s.loudnessComp, width: s.stereoWidth ?? 1, crossfeed: s.crossfeed || 'off', xfCurve: s.xfCurve || 'smooth' });
  engine.setCorrection(s.headphone || null, s.headphoneOn !== false);
}
function saveDeviceProfile() {
  if (!S().perDeviceSound || !OUT.key || !DB) return;
  DB.deviceProfiles = DB.deviceProfiles || {};
  DB.deviceProfiles[OUT.key] = { eq: structuredClone(S().eq), sound: Object.fromEntries(SOUND_KEYS.map((k) => [k, S()[k]])), volume: S().volume, type: OUT.info.type, bluetooth: OUT.info.bluetooth, model: OUT.info.model, updated: Date.now() };
  persist('deviceProfiles');
}
function updateDeviceButton() {
  const b = $('#pbDevice');
  if (!b) return;
  const info = OUT.info;
  const meta = DEVICE_INFO[info?.type || 'unknown'];
  b.replaceChildren(icon(meta.icon));
  if (info?.bluetooth) b.append(h('span', { class: 'bt-dot' }, icon('bluetooth')));
  b.title = info?.label ? `${info.model} — ${meta.name}${info.bluetooth ? ' (Bluetooth)' : ''}` : 'Audio output';
  b.classList.toggle('on', !!info?.bluetooth);
}
function outputMenu(e) {
  const items = OUT.devices.filter((d) => d.deviceId !== 'communications').map((d) => {
    const c = classifyOutput(d.label, OUT.bt);
    const meta = DEVICE_INFO[c.type];
    const sel = (S().outputDevice || 'default') === d.deviceId;
    return { icon: meta.icon, label: d.deviceId === 'default' ? `System default (${c.model})` : c.label || 'Unknown device', checked: sel, hint: c.bluetooth ? 'BT' : '', fn: async () => { setSetting('outputDevice', d.deviceId); try { await engine.setSink(d.deviceId); } catch (er) { toast("Couldn't switch output: " + er.message); } refreshDevices(true); } };
  });
  if (!items.length) items.push({ icon: 'info', label: 'No outputs found', fn: () => {} });
  const info = OUT.info;
  showMenu(e, info?.label ? { title: info.model, sub: DEVICE_INFO[info.type].name + (info.bluetooth ? ' • Bluetooth' : '') } : null, [
    ...items, '-',
    { icon: 'equalizer', label: 'Sound settings for this device', fn: () => go('equalizer') },
    { icon: 'settings', label: 'Output settings', fn: () => go('settings') }
  ]);
}
navigator.mediaDevices?.addEventListener('devicechange', debounce(() => refreshDevices(true), 900));

// ---- lyrics timing ----
const btLyricsDelay = () => (OUT.info?.bluetooth ? (S().btLyricsDelay ?? 200) / 1000 : 0);
const songLyricsOffset = (id) => (DB.lyricsOffsets?.[id] || 0);
const lyricsOffset = () => (P.current ? songLyricsOffset(P.current.id) + btLyricsDelay() : 0);
function adjustLyricsOffset(delta, reset = false) {
  const t = P.current;
  if (!t) return;
  DB.lyricsOffsets = DB.lyricsOffsets || {};
  const val = reset ? 0 : Math.round((songLyricsOffset(t.id) + delta) * 10) / 10;
  if (Math.abs(val) < 0.05) delete DB.lyricsOffsets[t.id]; else DB.lyricsOffsets[t.id] = Math.max(-10, Math.min(10, val));
  persist('lyricsOffsets');
  updateOffsetLabel();
  lyricIdx = -2;
  syncLyrics(true);
}
function updateOffsetLabel() {
  const el = $('#lyOffset');
  if (!el || !P.current) return;
  const o = songLyricsOffset(P.current.id);
  el.textContent = Math.abs(o) < 0.05 ? 'In sync' : `${o > 0 ? '+' : '−'}${Math.abs(o).toFixed(1)}s ${o > 0 ? 'later' : 'earlier'}`;
  el.classList.toggle('on', Math.abs(o) >= 0.05);
}
function lyricsTools(source) {
  const btn = (label, title, fn) => h('button', { class: 'ly-btn', title, onclick: fn }, label);
  return h('div', { class: 'ly-tools' },
    h('span', { class: 'lyrics-src' }, source ? 'Lyrics from ' + source : ''),
    h('div', { class: 'ly-adj', title: 'Adjust lyrics timing for this song ( [ and ] keys )' },
      icon('ff'),
      btn('−0.5', 'Show lyrics 0.5 s earlier', () => adjustLyricsOffset(-0.5)),
      btn('−0.1', 'Earlier ( [ )', () => adjustLyricsOffset(-0.1)),
      h('span', { id: 'lyOffset', class: 'ly-off', title: 'Click to reset', onclick: () => adjustLyricsOffset(0, true) }),
      btn('+0.1', 'Later ( ] )', () => adjustLyricsOffset(0.1)),
      btn('+0.5', 'Show lyrics 0.5 s later', () => adjustLyricsOffset(0.5)),
      btLyricsDelay() ? h('span', { class: 'bt-tag', title: 'Bluetooth delay compensation is applied' }, icon('bluetooth'), `+${Math.round(btLyricsDelay() * 1000)}ms`) : null));
}

// ---- mini player ----
let MINI = false;
async function toggleMini(force) {
  const on = force ?? !MINI;
  if (on && !$('#nowPlaying').hidden) closeNowPlaying();
  MINI = await api.setMini(on).catch(() => false);
  document.body.classList.toggle('mini', MINI);
  setIcon($('#pbMini'), MINI ? 'fullscreenExit' : 'mini');
  $('#pbMini').title = MINI ? 'Exit mini player' : 'Mini player';
}

// ================= search box =================
const sInput = $('#searchInput');
const sBox = $('#suggestBox');
let sgSel = -1;
function hideSuggest() { sBox.hidden = true; $('#searchBox').classList.remove('open'); sgSel = -1; }
function showSuggest(nodes) {
  if (!nodes.length) return hideSuggest();
  sBox.replaceChildren(...nodes);
  sBox.hidden = false;
  $('#searchBox').classList.add('open');
  sgSel = -1;
}
function doSearch(q) {
  q = q.trim();
  if (!q) return;
  if (handleLink(q)) return;
  DB.searchHistory = [q, ...DB.searchHistory.filter((x) => x.toLowerCase() !== q.toLowerCase())].slice(0, 30);
  persist('searchHistory');
  hideSuggest();
  sInput.blur();
  go('search', { q, type: 'all' });
}
function handleLink(text) {
  if (/open\.spotify\.com\/|spotify:(playlist|album|track):/.test(text)) { sInput.value = ''; hideSuggest(); sInput.blur(); importDialog(text.trim()); return true; }
  const m = text.match(/(?:v=|youtu\.be\/|\/shorts\/)([\w-]{11})/);
  const l = text.match(/[?&]list=([\w-]+)/);
  const b = text.match(/browse\/([\w-]+)|channel\/([\w-]+)/);
  if (!m && !l && !b) return false;
  sInput.value = ''; hideSuggest(); sInput.blur();
  if (m) api.songInfo(m[1]).then(playSingle).catch((er) => toast(shortErr(er.message)));
  else if (l) go('playlist', { id: l[1] });
  else { const id = b[1] || b[2]; id.startsWith('MPRE') ? go('album', { id }) : /^(VL|PL|RD|OLAK)/.test(id) ? go('playlist', { id }) : go('artist', { id }); }
  return true;
}
function historySuggest() {
  showSuggest(DB.searchHistory.slice(0, 8).map((q) => h('div', { class: 'sg-item', 'data-q': q, onmousedown: (e) => { e.preventDefault(); sInput.value = q; doSearch(q); } },
    icon('history'), h('span', null, q),
    h('button', { class: 'icon-btn small sg-remove', onmousedown: (e) => { e.preventDefault(); e.stopPropagation(); DB.searchHistory = DB.searchHistory.filter((x) => x !== q); persist('searchHistory'); historySuggest(); } }, icon('close')))));
}
let sgToken = 0;
const fetchSuggest = debounce(async (q) => {
  const tok = ++sgToken;
  try {
    const r = await api.suggestions(q);
    if (tok !== sgToken || document.activeElement !== sInput) return;
    const nodes = r.queries.map((s) => h('div', { class: 'sg-item', 'data-q': s, onmousedown: (e) => { e.preventDefault(); sInput.value = s; doSearch(s); } }, icon('search'), h('span', null, s)));
    if (r.items.length) {
      nodes.push(h('div', { class: 'sg-sep' }));
      for (const it of r.items) nodes.push(h('div', { class: 'sg-item ' + it.type, onmousedown: (e) => { e.preventDefault(); hideSuggest(); sInput.blur(); openItem(it); } },
        img(thumbOf(it)), h('div', null, h('div', null, it.title), h('div', { class: 'sg-sub' }, it.subtitle || (it.type === 'song' ? 'Song • ' + artistNames(it) : it.type)))));
    }
    showSuggest(nodes);
  } catch {}
}, 160);
sInput.addEventListener('input', () => {
  const q = sInput.value;
  $('#searchClear').hidden = !q;
  if (!q.trim()) { sgToken++; return historySuggest(); }
  fetchSuggest(q);
});
sInput.addEventListener('focus', () => { if (!sInput.value.trim()) historySuggest(); else fetchSuggest(sInput.value); });
sInput.addEventListener('blur', () => setTimeout(hideSuggest, 120));
sInput.addEventListener('keydown', (e) => {
  const items = $$('.sg-item', sBox);
  if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
    e.preventDefault();
    if (!items.length) return;
    sgSel = (sgSel + (e.key === 'ArrowDown' ? 1 : -1) + items.length) % items.length;
    items.forEach((x, i) => x.classList.toggle('sel', i === sgSel));
    if (items[sgSel].dataset.q) sInput.value = items[sgSel].dataset.q;
  } else if (e.key === 'Enter') {
    if (sgSel >= 0 && items[sgSel] && !items[sgSel].dataset.q) items[sgSel].dispatchEvent(new MouseEvent('mousedown'));
    else doSearch(sInput.value);
  } else if (e.key === 'Escape') { hideSuggest(); sInput.blur(); }
});
// Pasting a link no longer auto-plays/navigates. The text lands in the field and
// the user confirms with Enter (doSearch handles links).
sInput.addEventListener('paste', (e) => {
  const t = e.clipboardData?.getData('text') || '';
  if (/open\.spotify\.com\/|spotify:(playlist|album|track):/.test(t)) { e.preventDefault(); sInput.blur(); importDialog(t.trim()); return; }
  if (/(?:v=|youtu\.be\/|\/shorts\/|[?&]list=|browse\/|channel\/)/.test(t)) toast('Press Enter to open this link');
});
$('#searchClear').onclick = () => { sInput.value = ''; $('#searchClear').hidden = true; sInput.focus(); };

// ================= player =================
const engine = new Engine();
const P = { queue: [], idx: -1, current: null, playing: false, loading: false, shuffle: false, repeat: 'off', original: null, source: '', lyrics: null, errors: 0, loadToken: 0, npTab: 'lyrics', autoFilling: false, sleep: null, radioSeed: null };
const shortErr = (m) => { const t = String(m || '').replace(/^Error invoking remote method '[^']+': (Error: )?/, ''); return t.length > 260 ? t.slice(0, 257) + '…' : t; };
const withQid = (t) => ({ ...slim(t), qid: uid() });

function dedupe(tracks) {
  if (!S().noDuplicates) return tracks;
  const have = new Set(P.queue.map((x) => x.id));
  return tracks.filter((t) => !have.has(t.id) && have.add(t.id));
}
function playList(tracks, start = 0, { shuffle = false, source = '', key } = {}) {
  tracks = tracks.filter((t) => t && t.id);
  if (!tracks.length) return;
  let q = tracks.map(withQid);
  P.original = null;
  if (shuffle) {
    const first = start > 0 ? q[start] : q[Math.floor(Math.random() * q.length)];
    P.original = q;
    q = [first, ...shuffleArr(q.filter((x) => x !== first))];
    start = 0;
  }
  P.shuffle = shuffle;
  P.queue = q;
  P.flow = null;
  P.source = source;
  P.sourceKey = key !== undefined ? key : pageKey();
  updateNpFrom();
  P.radioSeed = null;
  playAt(start);
  updateModeButtons();
}
async function playSingle(t) {
  P.queue = [withQid(t)];
  P.original = null;
  P.shuffle = false;
  P.flow = null;
  P.source = 'Radio • ' + t.title;
  P.sourceKey = 'radio:' + t.id;
  updateNpFrom();
  P.radioSeed = t.id;
  playAt(0);
  updateModeButtons();
  try {
    const more = await api.upNext(t.id);
    if (P.radioSeed !== t.id) return;
    P.queue.push(...more.filter((x) => x.id !== t.id).map(withQid));
    queueChanged();
    prefetchNext();
  } catch (e) { console.warn('radio', e); }
}
function playNext(tracks) {
  if (P.flow) P.flow.locked = true;
  tracks = dedupe(tracks || []);
  if (!tracks.length) return toast('Already in queue');
  if (P.idx < 0) return playList(tracks, 0);
  const add = tracks.map(withQid);
  P.queue.splice(P.idx + 1, 0, ...add);
  if (P.original) P.original.push(...add);
  queueChanged();
  toast(tracks.length > 1 ? `${nOf(tracks.length, 'song', 'songs')} will play next` : `"${tracks[0].title}" will play next`);
}
function addToQueue(tracks) {
  if (P.flow) P.flow.locked = true;
  tracks = dedupe(tracks || []);
  if (!tracks.length) return toast('Already in queue');
  if (P.idx < 0) return playList(tracks, 0);
  const add = tracks.map(withQid);
  P.queue.push(...add);
  if (P.original) P.original.push(...add);
  queueChanged();
  toast(tracks.length > 1 ? `Added ${nOf(tracks.length, 'song', 'songs')} to queue` : `Added "${tracks[0].title}" to queue`);
}
function removeFromQueue(i) {
  if (P.flow) P.flow.locked = true;
  if (i === P.idx) return;
  const [rm] = P.queue.splice(i, 1);
  if (P.original) P.original = P.original.filter((x) => x.qid !== rm.qid);
  if (i < P.idx) P.idx--;
  queueChanged();
  const q = P.queue;
  toast(`Removed "${rm.title}" from the queue`, { label: 'Undo', fn: () => { if (P.queue !== q) return; const at = Math.min(i, q.length); q.splice(at, 0, rm); if (at <= P.idx) P.idx++; queueChanged(); } });
}
function queueChanged() { renderQueue(); saveSession(); }

async function playAt(i, { startAt = 0, autoplay = true, viaSkip = false } = {}) {
  if (i < 0 || i >= P.queue.length) return;
  const wasPlaying = P.playing;
  // Optional short crossfade on manual skip
  if (viaSkip && wasPlaying && S().crossfadeOnSkip && !engine.xfading) {
    P.xfPending = true;   // the old song's 'ended' must not skip the song the user just picked
    return crossfadeInto(i, 1.6).finally(() => (P.xfPending = false));
  }
  P.idx = i;
  const t = P.queue[i];
  P.current = t;
  const tok = ++P.loadToken;
  P.lyrics = null;
  P.resumeAt = startAt || 0;
  if (wasPlaying) engine.pause({ fade: 0.1 });
  onTrackChanged(t);
  return loadTrack(t, tok, { startAt, autoplay });
}
// The Android app supplies its own source (a MediaSource fed in chunks); desktop uses the mstream: proxy.
const srcFor = (t, bust) => (api.srcFor ? api.srcFor(t, bust) : 'mstream://' + t.id + (bust ? '?r=' + Date.now() : ''));
async function loadTrack(t, tok, { startAt = 0, autoplay = true } = {}) {
  t._retried = false;
  t._started = false;
  t._autoplay = autoplay;
  P.loading = autoplay;
  updatePlayButtons();
  try {
    const info = await api.prefetch(t.id);
    if (tok !== P.loadToken) return;
    setStreamInfo(t, info);
    await engine.load(t, { src: srcFor(t), lufs: lufsFor(t, info), startAt, autoplay });
    if (tok !== P.loadToken) return;
    if (!P.streamInfo?.txt.includes('kHz')) setStreamInfo(t, info);
    P.errors = 0;
    if (!autoplay) { P.loading = false; updatePlayButtons(); }
  } catch (e) {
    if (tok !== P.loadToken) return;
    handlePlayError(t, e, tok);
    return;
  }
  afterStart(t);
}
function setStreamInfo(t, info) {
  let txt = '';
  if (info?.client === 'LOCAL') txt = ['Local file', t.codec].filter(Boolean).join(' • ');
  else if (info) {
    const codec = /opus/i.test(info.mime || '') ? 'Opus' : /mp4a|mp4/i.test(info.mime || '') ? 'AAC' : '';
    txt = [info.offline ? 'Downloaded' : 'Streaming', codec, info.bitrate ? Math.round(info.bitrate / 1000) + ' kbps' : ''].filter(Boolean).join(' • ');
  }
  if (engine.ctx) txt += (txt ? ' • ' : '') + Math.round(engine.ctx.sampleRate / 100) / 10 + ' kHz output';
  const fl = featLabel(featOf(t.id));
  if (fl) txt += ' • ' + fl + (featOf(t.id)?.key ? ` (${featOf(t.id).key})` : '');
  P.streamInfo = { id: t.id, txt, info };
  const el = $('#npQuality');
  if (el && P.current?.id === t.id) el.textContent = txt;
}
function handlePlayError(t, e, tok) {
  if (P.errTok === tok) return; // one report per load attempt
  if (!navigator.onLine && !isLocal(t) && !isDownloaded(t.id)) { P.errTok = tok; return waitForNetwork(t); }
  P.errTok = tok;
  P.loading = false;
  console.error('play failed', t.id, e?.message || e);
  P.errors++;
  toast(`Can't play "${t.title}"` + (e?.message ? ': ' + shortErr(e.message) : ''));
  updatePlayButtons();
  if (S().autoSkipOnError && P.errors < 4 && P.idx < P.queue.length - 1) setTimeout(() => { if (tok === P.loadToken) next({ auto: true, error: true }); }, 1500);
}
function afterStart(t) {
  if (!t.duration && engine.duration) t.duration = engine.duration;
  // Flow radio: measure the next few unknown songs ahead of time (the playing song itself is measured
  // later from bytes the player already downloaded — see the monitor loop)
  if (P.flow && P.flow.queue === P.queue) ensureFeatures(P.queue.slice(P.idx + 1, P.idx + 4).filter((x) => !featOf(x.id)));
  lyricsFor(t);
  prefetchNext();
  maybeAutofill();
  sendState();
}
function onTrackChanged(t) {
  P.lvlAvg = null; P.quietMs = 0;
  updateNowPlayingUI(true);
  renderQueue();
  markPlayingRows();
  themeFromTrack(t);
  addHistory(t);
  if (!$('#nowPlaying').hidden) renderNpPanel();
  saveSession();
  refreshDownloadMarks();
}

// crossfade into queue index i over `dur` seconds (falls back to a normal load if it can't)
async function crossfadeInto(i, dur, { auto = false, style = 'fade' } = {}) {
  const t = P.queue[i];
  if (!t) return;
  const tok = ++P.loadToken;
  // reflect the change immediately so a second Next press moves on from here
  P.idx = i;
  P.current = t;
  P.lyrics = null;
  P.resumeAt = 0;
  t._retried = false;
  t._started = false;
  onTrackChanged(t);
  try {
    const info = await api.prefetch(t.id);
    if (tok !== P.loadToken) return;
    setStreamInfo(t, info);
    if (auto) {
      const rem = engine.duration - engine.currentTime;
      if (!(rem > 0.3) || engine.paused) throw new Error('too late');
      dur = Math.min(dur, rem / (S().tempo || 1));
    }
    await engine.crossfadeTo(t, { src: srcFor(t), lufs: lufsFor(t, info), duration: dur, style });
  } catch (e) {
    if (tok !== P.loadToken) return;
    // user paused while the next song was buffering → load it paused; otherwise just play it
    return loadTrack(t, tok, { autoplay: e?.message !== 'crossfade cancelled' });
  }
  if (tok !== P.loadToken) return;
  P.idx = Math.max(0, P.queue.indexOf(t));
  P.loading = false;
  P.playing = true;
  P.errors = 0;
  updatePlayButtons();
  afterStart(t);
}

// Loudness (LUFS) of a song: from YouTube / ReplayGain, else what this app measured last time.
function lufsFor(t, info) {
  if (info?.lufs != null) return info.lufs;
  const m = DB.lufs?.[t.id];
  return m != null ? m : null;
}
engine.addEventListener('loudness', (e) => {
  const { track, lufs } = e.detail || {};
  if (!track?.id || lufs == null) return;
  DB.lufs = DB.lufs || {};
  DB.lufs[track.id] = lufs;
  const keys = Object.keys(DB.lufs); if (keys.length > 5000) delete DB.lufs[keys[0]];
  persist('lufs');
});

// How to go from the playing song to the next one:
//   gapless — albums (and crossfade off): the next song starts on the last one's final sample
//   mix     — both songs' tempo/key are known and they go well together: a beat-timed DJ blend
//             (16 beats, bass swap in the middle)
//   fade    — the classic crossfade; shortened when two songs would clash
const albumOf = (x) => x?.album?.id || (x?.album?.name ? x.album.name + '|' + (x.albumArtist || artistNames(x)) : null);
function transitionPlan(cur, nt) {
  const xf = S().crossfade || 0;
  const key = `${cur.qid || cur.id}>${nt.qid || nt.id}|${xf}|${S().xfStyle}|${S().crossfadeGapless}|${S().gapless}`;
  if (P.plan?.key === key && P.plan.at > Date.now() - 20000) return P.plan.v;
  let v;
  const sameAlbum = albumOf(cur) && albumOf(nt) === albumOf(cur);
  if ((sameAlbum && S().crossfadeGapless !== false) || xf === 0) v = S().gapless === false ? null : { kind: 'gapless' };
  else {
    v = { kind: 'fade', dur: xf };
    const fa = featRanked(cur.id), fb = featRanked(nt.id);
    if (fa && fb && S().flowXf !== false) {
      const tr = transition(cur, fa, nt, fb);
      if (tr.sure > 0.5 && tr.score < 0.45) v.dur = Math.min(xf, 2.5);   // clash: keep it short
      else if (S().xfStyle !== 'classic' && tr.sure > 0.5 && tr.score >= 0.6 && fa.bpm > 60) {
        const beat = 60 / fa.bpm;
        let secs = 16 * beat;
        if (secs > Math.max(xf, 4) * 2) secs = 8 * beat;
        v = { kind: 'mix', dur: Math.max(4, Math.min(14, secs)), bpm: fa.bpm };
      }
    }
  }
  P.plan = { key, v, at: Date.now() };
  return v;
}

// Gapless: hand over to queue index i at the exact end of the current song.
async function gaplessInto(i) {
  const t = P.queue[i], cur = P.current, tok = P.loadToken;
  if (!t) return;
  // not ready in time (or failed): one attempt per song; if it already ended, move on normally
  const giveUp = () => { cur._noGapless = true; if (tok === P.loadToken && P.current === cur && engine.el.ended) next({ auto: true }); return false; };
  let info;
  try { info = await api.prefetch(t.id); } catch { return giveUp(); }
  if (tok !== P.loadToken || P.current !== cur) return false;
  try {
    await engine.gaplessTo(t, { src: srcFor(t), lufs: lufsFor(t, info) });
  } catch { return giveUp(); }
  if (tok !== P.loadToken) return false;
  ++P.loadToken;
  P.idx = i; P.current = t; P.lyrics = null; P.resumeAt = 0;
  t._retried = false; t._started = true;
  onTrackChanged(t);
  setStreamInfo(t, info);
  P.loading = false; P.playing = true; P.errors = 0;
  updatePlayButtons();
  afterStart(t);
  return true;
}

function nextIndex() {
  if (P.idx < P.queue.length - 1) return P.idx + 1;
  if (P.repeat === 'all' && P.queue.length) return 0;
  return null;
}
function prefetchNext() { const n = P.queue[P.idx + 1]; if (n) api.prefetch(n.id).catch(() => {}); }
async function maybeAutofill() {
  if (!S().autoplay || P.autoFilling || P.repeat !== 'off') return;
  if (P.queue.length - P.idx > 2) return;
  if (P.flow && P.flow.queue === P.queue) { P.autoFilling = true; try { await growFlowPool(); } finally { P.autoFilling = false; } return; }
  const seed = P.queue[P.queue.length - 1];
  if (!seed) return;
  P.autoFilling = true;
  const q = P.queue;
  try {
    const more = await api.upNext(seed.id);
    if (P.queue !== q) return; // the user started something else meanwhile
    const have = new Set(P.queue.map((x) => x.id));
    const add = more.filter((x) => !have.has(x.id)).slice(0, 25).map((x) => ({ ...withQid(x), auto: true }));
    P.queue.push(...add);
    if (P.original) P.original.push(...add);
    queueChanged();
    prefetchNext();
  } catch {} finally { P.autoFilling = false; }
}
function next({ auto = false, error = false } = {}) {
  if (!P.queue.length) return;
  if (!auto && P.current && engine.currentTime > 1 && engine.currentTime < 30) {
    DB.skips = DB.skips || {};
    DB.skips[P.current.id] = (DB.skips[P.current.id] || 0) + 1;
    const keys = Object.keys(DB.skips); if (keys.length > 3000) delete DB.skips[keys[0]];
    persist('skips');
  }
  if (auto && !error && P.sleep?.endOfSong) { setSleep(null); engine.pause(); toast('Sleep timer: playback stopped'); return; }
  if (auto && !error && P.repeat === 'one') { engine.seek(0); engine.play().catch(() => {}); return; }
  const n = nextIndex();
  if (n != null) return playAt(n, { viaSkip: !auto });
  if (auto) {
    if (S().autoplay) maybeAutofill().then(() => { if (P.idx < P.queue.length - 1) playAt(P.idx + 1); });
    return;
  }
  // Manual Next at the end of the queue: only wrap when repeat is on.
  if (P.repeat === 'all') return playAt(0, { viaSkip: true });
  engine.pause();
  toast('End of queue');
}
function prev() {
  if (engine.currentTime > 3 || P.idx <= 0) { engine.seek(0); return; }
  playAt(P.idx - 1, { viaSkip: true });
}
function togglePlay() {
  if (!P.current) { if (P.queue.length) playAt(Math.max(0, P.idx)); return; }
  if (engine.paused || engine.deck.pausing) {
    if (!engine.el.src) return playAt(P.idx, { startAt: P.resumeAt || 0 });
    engine.play().catch(() => playAt(P.idx));
  } else { P.duckPaused = false; engine.pause(); }
}
function toggleShuffle() {
  if (!P.queue.length) { P.shuffle = !P.shuffle; return updateModeButtons(); }
  const cur = P.queue[P.idx];
  if (!P.shuffle) {
    P.original = [...P.queue];
    P.queue = [...P.queue.slice(0, P.idx + 1), ...shuffleArr(P.queue.filter((_, i) => i > P.idx))];
    P.shuffle = true;
  } else {
    if (P.original) {
      const seen = new Set(P.original.map((x) => x.qid));
      P.queue = [...P.original, ...P.queue.filter((x) => !seen.has(x.qid))];
      P.idx = Math.max(0, P.queue.findIndex((x) => x.qid === cur?.qid));
    }
    P.original = null;
    P.shuffle = false;
  }
  updateModeButtons();
  queueChanged();
  toast(P.shuffle ? 'Shuffle on' : 'Shuffle off');
}
function cycleRepeat() {
  P.repeat = P.repeat === 'off' ? 'all' : P.repeat === 'all' ? 'one' : 'off';
  updateModeButtons();
  saveSession();
  toast(P.repeat === 'off' ? 'Repeat off' : P.repeat === 'all' ? 'Repeat all' : 'Repeat one');
}
function addHistory(t) {
  const last = DB.history[0];
  if (last && last.track.id === t.id && Date.now() - last.at < 60e3) return;
  DB.history.unshift({ track: slim(t), at: Date.now() });
  if (DB.history.length > 2000) DB.history.length = 2000;
  persist('history');
}

// ---- session persistence ----
function saveSessionNow() {
  if (!DB || !S().persistentQueue) return;
  const start = Math.max(0, P.idx - 50);
  api.saveSession({
    queue: P.queue.slice(start, P.idx + 300).map(slim), idx: P.idx - start, pos: engine.currentTime || P.resumeAt || 0, source: P.source, sourceKey: P.sourceKey || null,
    shuffle: P.shuffle, repeat: P.repeat
  });
}
const saveSession = (now = false) => { if (now) saveSessionNow(); else saveSessionDebounced(); };
const saveSessionDebounced = debounce(saveSessionNow, 800);

// ---- sleep timer ----
function setSleep(opt) {
  engine.restoreMaster();
  if (!opt) { P.sleep = null; toast('Sleep timer off'); }
  else if (opt.endOfSong) { P.sleep = { endOfSong: true }; toast('Music will stop at the end of this song'); }
  else { P.sleep = { end: Date.now() + opt.minutes * 60e3, fading: false }; toast(`Sleep timer set for ${opt.minutes} min`); }
  updateExtraButtons();
}
function sleepTick() {
  const sl = P.sleep;
  if (!sl || sl.endOfSong) return;
  const left = sl.end - Date.now();
  if (S().sleepFadeOut && !sl.fading && left <= 30e3 && P.playing) { sl.fading = true; engine.fadeOutAll(Math.max(1, left / 1000)); }
  if (left <= 0) {
    P.sleep = null;
    engine.pause({ fade: 0.3 });
    setTimeout(() => engine.restoreMaster(), 600);
    toast('Sleep timer: playback stopped');
    updateExtraButtons();
  }
}
function updateExtraButtons() {
  const sl = P.sleep;
  for (const id of ['#pbSleep', '#npSleep']) {
    const b = $(id);
    b.replaceChildren(icon('sleep'));
    b.classList.toggle('on', !!sl);
    if (sl) b.append(h('span', { class: 'badge' }, sl.endOfSong ? '♪' : Math.max(1, Math.ceil((sl.end - Date.now()) / 60e3))));
  }
  $('#npTempo').classList.toggle('on', S().tempo !== 1);
  $('#npEq').classList.toggle('on', !!S().eq.enabled);
}

// ---- UI sync ----
function updateModeButtons() {
  for (const id of ['#pbShuffle', '#npShuffle']) { setIcon($(id), 'shuffle'); $(id).classList.toggle('on', P.shuffle); }
  for (const id of ['#pbRepeat', '#npRepeat']) { setIcon($(id), P.repeat === 'one' ? 'repeatOne' : 'repeat'); $(id).classList.toggle('on', P.repeat !== 'off'); }
}
function updatePlayButtons() {
  kickFrame();
  for (const id of ['#pbPlay', '#npPlay']) {
    const b = $(id);
    const want = P.loading ? 'loading' : P.playing ? 'pause' : 'play';
    if (b.dataset.state !== want) {
      b.dataset.state = want;
      if (id === '#npPlay') b.replaceChildren(want === 'loading' ? h('div', { class: 'spinner' }) : icon(want), h('span', null, want === 'pause' ? 'Pause' : 'Play'));
      else if (want === 'loading') b.replaceChildren(h('div', { class: 'spinner' }));
      else setIcon(b, want);
    }
    b.classList.toggle('playing', P.playing || P.loading);
  }
  document.body.classList.toggle('paused', !P.playing);
  markPlayingRows();
  refreshSourceMarks();
}
function markPlayingRows() {
  $$('.song[data-id]').forEach((row) => {
    const on = !!P.current && (row.dataset.qidx != null ? Number(row.dataset.qidx) === P.idx : row.dataset.id === P.current.id);
    const was = row.classList.contains('playing');
    const hp = row.querySelector('.hover-play');
    if (!on && !was) return;
    row.classList.toggle('playing', on);
    if (hp) hp.replaceChildren(...leadContent(on));
  });
}
function restartAnim(el, cls) { el.classList.remove(cls); void el.offsetWidth; el.classList.add(cls); }
function updateNowPlayingUI(changed = false) {
  const t = P.current;
  $('#playerbar').classList.toggle('empty', !t);
  if (!t) return;
  $('#npQuality').textContent = P.streamInfo?.id === t.id ? P.streamInfo.txt : '';
  $('#pbThumb').style.backgroundImage = `url("${thumbOf(t)}")`;
  $('#pbTitle').replaceChildren(explicitBadge(t) || '', t.title);
  $('#pbTitle').title = t.title;
  $('#pbArtist').replaceChildren(...artistLinks(t.artists, t.subtitle || ''));
  if (changed) restartAnim($('#pbMeta'), 'track-anim');
  const art = $('#npArt');
  art.style.backgroundImage = `url("${bigThumb(t)}"), url("${thumbOf(t)}")`;
  if (changed && !$('#nowPlaying').hidden) {
    // Android slides the artwork like a carousel: forward → comes in from the right
    const dir = !androidMotion() ? 'swap' : P.lastArtIdx != null && P.idx < P.lastArtIdx ? 'art-prev' : 'art-next';
    art.classList.remove('art-prev', 'art-next', 'swap'); void art.offsetWidth; art.classList.add(dir);
  }
  P.lastArtIdx = P.idx;
  setNpBackground(thumbOf(t));
  $('#npTitle').textContent = t.title; $('#npTitle').title = t.title;
  $('#npArtist').replaceChildren(...artistLinks(t.artists, t.subtitle || ''), t.album?.name ? ' • ' : '', t.album?.name ? h('span', { class: 'lnk', onclick: () => t.album.id && go('album', { id: t.album.id }) }, t.album.name) : '');
  $('#pbDur').textContent = $('#npDur').textContent = fmtTime(t.duration);
  refreshLikeButtons();
  updatePlayButtons();
  if (changed) setProgress(0, t.duration || 0, 0);
  if ('mediaSession' in navigator) setMediaMetadata(t);
}
// Windows media overlay only accepts http(s)/data/blob artwork; local covers are turned into blob URLs.
let mediaArtUrl = null;
async function setMediaMetadata(t) {
  const meta = (artwork) => { try { navigator.mediaSession.metadata = new MediaMetadata({ title: t.title, artist: artistNames(t), album: t.album?.name || '', artwork }); } catch {} };
  if (!isLocal(t)) return meta([{ src: bigThumb(t), sizes: '1200x1200', type: 'image/jpeg' }, { src: thumbOf(t), sizes: '544x544', type: 'image/jpeg' }].filter((a) => a.src));
  meta([]);
  try {
    const b = await (await fetch(thumbOf(t))).blob();
    if (P.current?.id !== t.id) return;
    if (mediaArtUrl) URL.revokeObjectURL(mediaArtUrl);
    mediaArtUrl = URL.createObjectURL(b);
    meta([{ src: mediaArtUrl, sizes: '512x512', type: b.type || 'image/jpeg' }]);
  } catch {}
}
let lastBg = '';
const bgCache = makeLru(24);
function blurredBg(url) {
  if (bgCache.has(url)) return Promise.resolve(bgCache.get(url));
  return new Promise((res, rej) => {
    const im = new Image();
    im.crossOrigin = 'anonymous';
    im.onload = () => {
      try {
        const c = document.createElement('canvas'); c.width = c.height = 40;
        const g = c.getContext('2d');
        g.filter = 'blur(3px) saturate(1.5) brightness(.55)';
        g.drawImage(im, -6, -6, 52, 52);
        const data = c.toDataURL('image/jpeg', 0.9);
        bgCache.set(url, data); res(data);
      } catch (e) { rej(e); }
    };
    im.onerror = rej;
    im.src = url;
  });
}
function setNpBackground(url) {
  if (url === lastBg) return;
  lastBg = url;
  const wrap = $('#npBgWrap');
  const layer = h('div', { class: 'np-bg enter' });
  wrap.append(layer);
  // Pre-blur once on a tiny canvas and let the GPU upscale it, instead of a live 90 px CSS blur
  // over the whole window (the most expensive paint in the app).
  blurredBg(url).then((data) => { layer.style.backgroundImage = `url("${data}")`; }).catch(() => { layer.classList.add('live-blur'); layer.style.backgroundImage = `url("${url}")`; });
  setTimeout(() => { while (wrap.children.length > 1) wrap.firstChild.remove(); }, 900);
}
function sendState() {
  const t = P.current;
  api.sendState({ duckWait: !!P.duckPaused, playing: P.playing, track: t ? slim(t) : null, position: engine.currentTime, duration: engine.duration || t?.duration || 0 });
}

// ---- sliders ----
function makeSlider(el, { onSeek, onInput, tip }) {
  let dragging = false, tipEl = null;
  const pct = (e) => { const r = el.getBoundingClientRect(); return Math.min(1, Math.max(0, (e.clientX - r.left) / r.width)); };
  el.addEventListener('pointerdown', (e) => { if (e.button !== 0) return; dragging = true; kickFrame(); el.setPointerCapture(e.pointerId); el.classList.add('drag'); onInput?.(pct(e)); });
  el.addEventListener('pointermove', (e) => {
    const p = pct(e);
    if (tip) {
      if (!tipEl) { tipEl = h('div', { class: 'sl-tip' }); el.append(tipEl); }
      tipEl.textContent = tip(p);
      tipEl.style.left = p * 100 + '%';
    }
    if (dragging) onInput?.(p);
  });
  el.addEventListener('pointerleave', () => { if (tipEl && !dragging) { tipEl.remove(); tipEl = null; } });
  el.addEventListener('pointerup', (e) => { if (!dragging) return; dragging = false; el.classList.remove('drag'); onSeek?.(pct(e)); if (tipEl) { tipEl.remove(); tipEl = null; } });
  return { get dragging() { return dragging; } };
}
function setSlider(el, p, buf) {
  el.querySelector('.sl-fill').style.width = p * 100 + '%';
  el.querySelector('.sl-thumb').style.left = p * 100 + '%';
  if (buf != null) { const b = el.querySelector('.sl-buf'); if (b) b.style.width = buf * 100 + '%'; }
}
const dur = () => engine.duration || P.current?.duration || 0;
let seekPreview = null;
const seekSliders = ['#pbSeek', '#npSeek'].map((id) => makeSlider($(id), {
  tip: (p) => fmtTime(p * dur()),
  onInput: (p) => { seekPreview = p; setProgress(p * dur(), dur(), null); },
  onSeek: (p) => {
    seekPreview = null;
    if (!dur()) return;
    if (!engine.el.src && P.current) { P.resumeAt = p * dur(); playAt(P.idx, { startAt: P.resumeAt }); return; }
    engine.seek(p * dur());
    sendState();
  }
}));
// squiggly seekbar pieces
const npSeek = $('#npSeek');
const waveSvg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
waveSvg.setAttribute('class', 'wave');
const wavePath = document.createElementNS('http://www.w3.org/2000/svg', 'path');
waveSvg.append(wavePath);
const restEl = h('div', { class: 'sl-rest' });
npSeek.querySelector('.sl-track').append(restEl, waveSvg);
let waveAmp = 0, wavePhase = 0, lastP = 0;
function drawWave(p) {
  const W = npSeek.clientWidth;
  if (!W) return;
  const L = Math.max(0, p * W);
  const target = S().squiggly && P.playing && !P.loading ? 3.2 : 0;
  waveAmp += (target - waveAmp) * 0.12;
  if (P.playing) wavePhase -= 0.09;
  const cy = 15, lambda = 28;
  let d = `M0 ${cy}`;
  const end = Math.max(0, L - 5);
  for (let x = 2; x <= end; x += 2) d += `L${x} ${(cy + waveAmp * Math.sin((x / lambda) * Math.PI * 2 + wavePhase)).toFixed(2)}`;
  wavePath.setAttribute('d', end > 0 ? d : '');
  waveSvg.setAttribute('width', String(W));
  restEl.style.left = Math.min(W, L + 7) + 'px';
}
function setProgress(cur, total, buf) {
  const p = total ? Math.min(1, cur / total) : 0;
  lastP = p;
  setSlider($('#pbSeek'), p, buf);
  setSlider(npSeek, p, buf);
  $('#pbCur').textContent = fmtTime(cur);
  $('#npCur').textContent = fmtTime(cur);
  if (total) $('#pbDur').textContent = $('#npDur').textContent = fmtTime(total);
}
function bufferedFrac() {
  const d = dur(), b = engine.buffered;
  if (!d || !b?.length) return 0;
  for (let i = 0; i < b.length; i++) if (b.start(i) <= engine.currentTime + 0.5 && b.end(i) >= engine.currentTime) return b.end(i) / d;
  return 0;
}

const volSlider = $('#pbVol');
function setVolume(v, save = true) {
  v = Math.min(1, Math.max(0, v));
  DB.settings.volume = v;
  engine.setVolumeLevel(v);
  setSlider(volSlider, v);
  setIcon($('#pbMute'), volIcon(v, false));
  if (save) { persist('settings'); saveDeviceVolume(); }
}
const saveDeviceVolume = debounce(() => { const p = DB.deviceProfiles?.[OUT.key]; if (p && S().perDeviceSound) { p.volume = S().volume; persist('deviceProfiles'); } }, 600);
makeSlider(volSlider, { onInput: (p) => setVolume(p, false), onSeek: (p) => setVolume(p), tip: (p) => Math.round(p * 100) + '%' });
volSlider.addEventListener('wheel', (e) => { e.preventDefault(); setVolume(DB.settings.volume + (e.deltaY < 0 ? 0.05 : -0.05)); }, { passive: false });
function toggleMute() {
  engine.opts.muted = !engine.opts.muted;
  engine.applyVolume();
  setIcon($('#pbMute'), volIcon(DB.settings.volume, engine.opts.muted));
  setSlider(volSlider, engine.opts.muted ? 0 : DB.settings.volume);
}

// ---- engine events ----
engine.addEventListener('playing', () => { clearTimeout(stallTimer); if (P.current) P.current._stalls = 0; kickFrame(); if (P.current) P.current._started = true; P.playing = true; P.loading = false; updatePlayButtons(); sendState(); if ('mediaSession' in navigator) navigator.mediaSession.playbackState = 'playing'; });
engine.addEventListener('pause', () => { if (engine.xfading && !engine.deck.pausing) return; P.playing = false; updatePlayButtons(); sendState(); saveSession(); if ('mediaSession' in navigator) navigator.mediaSession.playbackState = 'paused'; });
engine.addEventListener('waiting', () => { if (!engine.paused) { P.loading = true; updatePlayButtons(); armStallWatch(); } });
// ---- never stay stuck: stalls, dropped connections and dead streams recover on their own ----
let stallTimer = null;
function armStallWatch() {
  clearTimeout(stallTimer);
  const t = P.current, tok = P.loadToken;
  stallTimer = setTimeout(() => {
    if (tok !== P.loadToken || !P.loading || engine.paused || P.current !== t) return;
    if (!navigator.onLine) return waitForNetwork(t);
    t._stalls = (t._stalls || 0) + 1;
    console.warn('stalled', t.id, 'at', engine.currentTime.toFixed(1), 'attempt', t._stalls);
    if (t._stalls > 2) return handlePlayError(t, new Error('The stream stopped responding'), tok);
    reloadAt(t, engine.currentTime);   // fresh stream URL, same position
  }, 12000);
}
function reloadAt(t, pos) {
  const tok = P.loadToken;
  api.prefetch(t.id).catch(() => null).then((info) => {
    if (tok !== P.loadToken || P.current !== t) return;
    return engine.load(t, { src: srcFor(t, true), lufs: lufsFor(t, info), startAt: pos });
  }).catch((e) => { if (tok === P.loadToken && navigator.onLine) handlePlayError(t, e, tok); else waitForNetwork(t); });
}
function waitForNetwork(t) {
  if (P.waitNet) return;
  P.waitNet = { t, pos: engine.currentTime, tok: P.loadToken };
  P.loading = false; updatePlayButtons();
  toast('Connection lost — will continue when you are back online');
}
window.addEventListener('online', () => {
  const w = P.waitNet;
  P.waitNet = null;
  if (!w || w.tok !== P.loadToken || P.current !== w.t) return;
  toast('Back online — resuming');
  P.loading = true; updatePlayButtons();
  reloadAt(w.t, w.pos);
});

engine.addEventListener('canplay', () => { clearTimeout(stallTimer); if (P.loading && !engine.paused) { P.loading = false; updatePlayButtons(); } });
engine.addEventListener('ended', () => {
  if (P.xfPending) return; // the pending crossfade takes over
  const md = P.current?.duration, at = engine.currentTime;
  if (md && at < md * 0.8) console.warn(`track ended early: ${P.current.id} at ${at.toFixed(1)}s of ${md}s`);
  next({ auto: true });
});
engine.addEventListener('seeked', () => { kickFrame(); sendState(); lyricIdx = -2; });
engine.addEventListener('loadedmetadata', () => {
  const t = P.current;
  if (t && engine.duration) { if (!t.duration) t.duration = engine.duration; setProgress(engine.currentTime, engine.duration, bufferedFrac()); }
});
engine.addEventListener('error', (ev) => {
  const t = P.current;
  if (!t || !engine.el.getAttribute('src')) return;
  const tok = P.loadToken;
  // a failed initial (autoplay) load is reported by loadTrack via the rejected play()
  if (!t._started && t._autoplay !== false) return;
  console.error('audio error', t.id, ev.detail?.error?.message || '');
  P.loading = false;
  updatePlayButtons();
  if (!navigator.onLine && !isLocal(t) && !isDownloaded(t.id)) return waitForNetwork(t);
  if (!t._retried && t._started) {
    // Stream URLs can expire mid-song; reload at the same position once.
    t._retried = true;
    const pos = engine.currentTime;
    api.prefetch(t.id).catch(() => null).then((info) => {
      if (tok !== P.loadToken) return;
      return engine.load(t, { src: srcFor(t, true), lufs: lufsFor(t, info), startAt: pos });
    }).catch((e) => { if (tok === P.loadToken) handlePlayError(t, e, tok); });
    return;
  }
  handlePlayError(t, new Error('Playback error'), tok);
});

// UI animation loop (only paints when something is visible)
let lastPaint = -1;
// Runs only while something on screen moves: playing, loading, seeking or Now Playing open.
// Idle = no animation frames at all (lets the GPU/CPU sleep).
let rafPending = false, frameTimer = 0;
function frame() {
  rafPending = false; frameTimer = 0;
  const npOpen = !$('#nowPlaying').hidden;
  const dragging = seekPreview || seekSliders.some((s) => s.dragging);
  if (!dragging) {
    const ct = engine.currentTime;
    // the player bar moves ~2 px/s: 4 updates a second is plenty; Now Playing gets every frame
    if (Math.abs(ct - lastPaint) > (npOpen ? 0.03 : 0.25) || P.loading) { lastPaint = ct; setProgress(ct, dur(), bufferedFrac()); }
  }
  if (npOpen) { drawWave(lastP); syncLyrics(); }
  // Every display frame only when something moves smoothly (Now Playing, lyrics, a drag). When only
  // the player bar is ticking, 4 plain timer ticks a second: no animation frames, so Chromium's
  // compositor can idle between them (this was most of the CPU used while playing).
  if (dragging || npOpen || P.loading || document.querySelector('.ly-stage')) { rafPending = true; requestAnimationFrame(frame); }
  else if (P.playing) frameTimer = setTimeout(frame, 250);
}
// (re)start the loop now, e.g. when playback starts or Now Playing opens
function kickFrame() {
  if (rafPending) return;
  clearTimeout(frameTimer); frameTimer = 0;
  rafPending = true; requestAnimationFrame(frame);
}
kickFrame();

// Engine monitor runs on a timer so crossfade/skip-silence/sleep keep working when minimised.
let monitorN = 0;
setInterval(() => {
  monitorN++;
  if (!P.playing && !P.sleep && monitorN % 10) return;   // idle: 1 tick a second is enough
  const smart = S().smartCrossfade !== false;
  if (engine.xfading && smart && P.playing) engine.trimLeadIn();
  if (monitorN % 20 === 0 && P.playing && P.current && S().flowLearn !== false && !P.current._learn) {
    const d0 = engine.duration;
    if (d0 > 40 && engine.currentTime > d0 * 0.6) { P.current._learn = true; if (!featOf(P.current.id)) ensureFeatures([P.current], { front: true }); }
  }
  if (P.playing && !P.loading && !engine.xfading) {
    const tempo = S().tempo || 1;
    const d = engine.duration, t = engine.currentTime;
    const n = nextIndex(), nt = n != null ? P.queue[n] : null;
    const plan = nt && d - t < 40 && P.repeat !== 'one' && !P.sleep?.endOfSong ? transitionPlan(P.current, nt) : null;
    // Smart crossfade: follow the song's loudness so the blend starts when the outro actually fades
    // out (or goes silent) instead of blindly at "duration − crossfade".
    let lvl = null;
    const xf = plan && plan.kind !== 'gapless' ? plan.dur : 0;
    if (xf > 0 && smart) {
      lvl = engine.levelDb();
      if (lvl > -60 && t > 5 && d - t > xf + 12) P.lvlAvg = P.lvlAvg == null ? lvl : P.lvlAvg * 0.99 + lvl * 0.01;
    }
    if (plan?.kind === 'gapless') {
      if (!P.xfPending && !P.current._noGapless && d - t <= 3 && d - t > 0.05 && d > 5) {
        P.xfPending = true;
        gaplessInto(n).finally(() => (P.xfPending = false));
      }
    } else if (plan) {
      let start = d - t <= xf, early = false;
      if (!start && lvl != null && P.lvlAvg != null && t > 30 && d - t <= xf + 12) {
        if (lvl < P.lvlAvg - 22) { P.quietMs = (P.quietMs || 0) + 100; if (P.quietMs >= 700) start = early = true; } else P.quietMs = 0;
      }
      if (start && d > xf * 2.5 && d - t > 0.4 && !P.xfPending) {
        P.xfPending = true;
        const media = early ? Math.min(xf, d - t) : d - t;
        crossfadeInto(n, Math.max(0.8, media / tempo), { auto: true, style: plan.kind }).catch((e) => console.warn('crossfade failed', e)).finally(() => (P.xfPending = false));
      }
    }
    if (engine.monitorSilence(100) === 'silent-end' && !P.xfPending) next({ auto: true });
  }
  sleepTick();
  if (monitorN % 50 === 0) {
    if (P.sleep && !P.sleep.endOfSong) updateExtraButtons();
    if (P.playing) { saveSession(); sendState(); }
  }
}, 100);

if ('mediaSession' in navigator) {
  const ms = navigator.mediaSession;
  ms.setActionHandler('play', () => togglePlay());
  ms.setActionHandler('pause', () => togglePlay());
  ms.setActionHandler('previoustrack', () => prev());
  ms.setActionHandler('nexttrack', () => next());
  ms.setActionHandler('seekto', (d) => engine.seek(d.seekTime));
  ms.setActionHandler('seekbackward', () => engine.seek(engine.currentTime - 10));
  ms.setActionHandler('seekforward', () => engine.seek(engine.currentTime + 10));
  ms.setActionHandler('stop', () => engine.pause());
}
setInterval(() => {
  if ('mediaSession' in navigator && P.current && dur() && engine.el.src) {
    try { navigator.mediaSession.setPositionState({ duration: dur(), position: Math.min(engine.currentTime, dur()), playbackRate: S().tempo }); } catch {}
  }
}, 1000);
api.onMediaAction((a) => (a === 'toggle' ? togglePlay() : a === 'next' ? next() : prev()));

// ---- queue panel ----
function buildQueue() {
  const box = h('div');
  if (!P.queue.length) { box.append(emptyState('queue', 'Queue is empty', 'Play something to get started.')); return box; }
  const mk = (t, i) => {
    const row = songRow(t, { queueIdx: i });
    row.classList.add('q-item');
    row.dataset.qidx = i;
    row.dataset.key = t.qid || t.id + ':' + i;
    const fl = featLabel(featOf(t.id));
    if (fl) row.querySelector('.s-sub')?.append(h('span', { class: 'flow-chip', title: 'Tempo · key (Camelot)' + (featOf(t.id)?.key ? ' — ' + featOf(t.id).key : '') }, fl));
    if (i < P.idx) row.classList.add('past');
    if (i !== P.idx) {
      row.draggable = true;
      row.addEventListener('dragstart', (e) => { e.dataTransfer.setData('text/q', String(i)); row.classList.add('dragging'); });
      row.addEventListener('dragend', () => row.classList.remove('dragging'));
    }
    row.addEventListener('dragover', (e) => { e.preventDefault(); const r = row.getBoundingClientRect(); const above = e.clientY < r.top + r.height / 2; row.classList.toggle('drop-above', above); row.classList.toggle('drop-below', !above); });
    row.addEventListener('dragleave', () => row.classList.remove('drop-above', 'drop-below'));
    row.addEventListener('drop', (e) => {
      e.preventDefault();
      row.classList.remove('drop-above', 'drop-below');
      const raw = e.dataTransfer.getData('text/q');
      if (raw === '') return;
      const from = Number(raw);
      if (isNaN(from) || from === P.idx) return;
      const r = row.getBoundingClientRect();
      let to = e.clientY < r.top + r.height / 2 ? i : i + 1;
      if (P.flow) P.flow.locked = true;
      const cur = P.queue[P.idx];
      const [m] = P.queue.splice(from, 1);
      if (from < to) to--;
      P.queue.splice(to, 0, m);
      P.idx = P.queue.indexOf(cur);
      queueChanged();
    });
    return row;
  };
  const now = P.queue[P.idx];
  if (now) box.append(h('div', { class: 'q-label' }, 'Now playing'), mk(now, P.idx));
  const nxt = P.queue[P.idx + 1];
  if (now && nxt) box.append(transitionChip(now, nxt));
  const upcoming = P.queue.slice(P.idx + 1);
  if (upcoming.length) {
    box.append(h('div', { class: 'q-label' }, ...(P.source ? ['Next from: ', sourceLabel()] : ['Next up'])));
    if (P.flow && P.flow.queue === P.queue) {
      const known = upcoming.filter((t) => featOf(t.id)?.bpm).length;
      box.append(h('div', { class: 'flow-note' }, icon('crossfade'), h('span', null, P.flow.locked ? 'Flow radio • you rearranged the queue, so it stays as you set it' : `Flow radio • ${contextFor().label} mood • arranged by tempo, key and your taste • ${known}/${nOf(upcoming.length, 'song', 'songs')} analysed`)));
    }
    upcoming.slice(0, 300).forEach((t, j) => box.append(mk(t, P.idx + 1 + j)));
  }
  if (P.idx > 0) {
    box.append(h('div', { class: 'q-label' }, 'Played'));
    P.queue.slice(Math.max(0, P.idx - 50), P.idx).forEach((t, j) => box.append(mk(t, Math.max(0, P.idx - 50) + j)));
  }
  return box;
}
// Rows glide to their new place after a reorder (FLIP), instead of jumping.
function flipRows(container, rebuild) {
  const calm = matchMedia('(prefers-reduced-motion: reduce)').matches;
  const before = new Map();
  const rows = container.querySelectorAll('.q-item[data-key]');
  if (!calm && rows.length < 160) rows.forEach((r) => before.set(r.dataset.key, r.getBoundingClientRect().top));
  rebuild();
  if (!before.size) return;
  container.querySelectorAll('.q-item[data-key]').forEach((r) => {
    const old = before.get(r.dataset.key);
    if (old == null) return;
    const dy = old - r.getBoundingClientRect().top;
    if (Math.abs(dy) > 2) r.animate([{ transform: `translateY(${dy}px)` }, { transform: 'none' }], { duration: 280, easing: 'cubic-bezier(.2,.8,.2,1)' });
  });
}
function transitionChip(a, b) {
  const plan = transitionPlan(a, b);
  const xf = S().crossfade || 0;
  let ic = 'crossfade', txt;
  if (!plan) txt = 'No crossfade';
  else if (plan.kind === 'gapless') { ic = 'album'; txt = xf ? 'Gapless: same album, straight into the next song' : 'Gapless'; }
  else if (plan.kind === 'mix') txt = `Smart mix · ${Math.round(plan.dur)} s at ${Math.round(plan.bpm)} BPM, bass swap in the middle`;
  else txt = plan.dur < xf ? `Short fade · ${plan.dur} s: these two would clash` : `Crossfade · ${plan.dur} s`;
  return h('div', { class: 'q-transition', title: 'How this song will flow into the next one (Sound → Transitions)', onclick: () => { closeNowPlaying(); go('equalizer'); } }, icon(ic), h('span', null, txt));
}
function renderQueue() {
  const qp = $('#queuePanel');
  const npq = !$('#nowPlaying').hidden && P.npTab === 'queue';
  if (!qp.hidden) {
    const list = $('#queueList');
    const st = list.scrollTop;
    flipRows(list, () => { list.replaceChildren(buildQueue()); list.scrollTop = st; });
    $('#qpSub').textContent = P.queue.length ? `${nOf(P.queue.length, 'song', 'songs')} • ${totalDuration(P.queue.slice(P.idx))} left` : '';
  }
  if (npq) {
    const panel = $('#npPanel .panel-scroll');
    if (panel) { const st = panel.scrollTop; flipRows(panel, () => { panel.replaceChildren(buildQueue()); panel.scrollTop = st; }); }
  }
}
function toggleQueue(force) {
  const qp = $('#queuePanel');
  const open = force != null ? force : qp.hidden || qp.classList.contains('closing');
  clearTimeout(qp._t);
  if (!open) {
    if (qp.hidden) return;
    qp.classList.add('closing');
    $('#pbQueue').classList.remove('on');
    qp._t = setTimeout(() => { qp.hidden = true; qp.classList.remove('closing'); }, 200);
    return;
  }
  qp.classList.remove('closing');
  qp.hidden = false;
  $('#pbQueue').classList.toggle('on', !qp.hidden);
  if (!qp.hidden) { renderQueue(); $('#queueList .song.playing')?.scrollIntoView({ block: 'center' }); }
}
$('#qpClose').onclick = () => toggleQueue(false);
$('#qpClear').onclick = () => {
  if (P.idx < 0) return;
  const cur = P.queue[P.idx];
  P.queue = cur ? [cur] : [];
  P.idx = cur ? 0 : -1;
  P.original = null;
  queueChanged();
};
$('#qpSave').onclick = () => { if (P.queue.length) createPlaylist(P.queue); };

// ---- now playing ----
function viewTransition(fn) {
  if (document.startViewTransition && !matchMedia('(prefers-reduced-motion: reduce)').matches) {
    const vt = document.startViewTransition(fn);
    vt.ready.catch(() => {}); vt.finished.catch(() => {}); vt.updateCallbackDone.catch(() => {});
  } else fn();
}
function openNowPlaying(tab) {
  if (!P.current) return;
  setTimeout(kickFrame, 0);
  if (tab) P.npTab = tab;
  if (!$('#nowPlaying').hidden) { renderNpPanel(); return; }
  closeMenu();
  viewTransition(() => {
    $('#nowPlaying').hidden = false;
    document.body.classList.add('np-open');
    renderNpPanel();
    syncTitlebar();
  });
  if (!document.startViewTransition) restartAnim($('#nowPlaying'), 'anim-in');
}
function closeNowPlaying() {
  if ($('#nowPlaying').hidden) return;
  viewTransition(() => {
    $('#nowPlaying').hidden = true;
    document.body.classList.remove('np-open');
    syncTitlebar();
  });
}
$$('.np-tabs .chip').forEach((c) => (c.onclick = () => { P.npTab = c.dataset.tab; renderNpPanel(); }));
async function renderNpPanel() {
  const panel = $('#npPanel');
  $$('.np-tabs .chip').forEach((c) => c.classList.toggle('active', c.dataset.tab === P.npTab));
  const t = P.current;
  if (!t) return panel.replaceChildren();
  if (P.npTab === 'lyrics') return renderLyrics();
  if (P.npTab === 'queue') {
    panel.replaceChildren(h('div', { class: 'panel-scroll' }));
    renderQueue();
    panel.querySelector('.song.playing')?.scrollIntoView({ block: 'start' });
    return;
  }
  panel.replaceChildren(loading());
  try {
    const secs = await api.related(t.id);
    if (P.current !== t || P.npTab !== 'related') return;
    const box = h('div', { class: 'panel-scroll' });
    secs.forEach((s) => box.append(renderSection(s)));
    if (!secs.length) box.append(emptyState('similar', 'Nothing related', ''));
    panel.replaceChildren(box);
  } catch (e) { panel.replaceChildren(errorBox(e)); }
}

// ---- lyrics ----
function parseLrc(lrc) {
  const out = [];
  const ts = (m, s) => Number(m) * 60 + Number(s);
  for (const line of lrc.split(/\r?\n/)) {
    const tags = [...line.matchAll(/\[(\d+):(\d+(?:\.\d+)?)\]/g)];
    if (!tags.length) continue;
    const body = line.replace(/\[[^\]]*\]/g, '');
    // enhanced LRC: <mm:ss.xx>word <mm:ss.xx>word …
    let words = null;
    if (/<\d+:\d+(?:\.\d+)?>/.test(body)) {
      words = [];
      const re = /<(\d+):(\d+(?:\.\d+)?)>([^<]*)/g;
      let m;
      while ((m = re.exec(body))) { const w = m[3]; if (w.trim()) words.push({ t: ts(m[1], m[2]), text: w }); else if (words.length) words[words.length - 1].end = ts(m[1], m[2]); }
      if (!words.length) words = null;
    }
    const text = body.replace(/<\d+:\d+(\.\d+)?>/g, '').replace(/\s+/g, ' ').trim();
    for (const m of tags) out.push({ t: ts(m[1], m[2]), text, words });
  }
  out.sort((a, b) => a.t - b.t);
  for (let i = 0; i < out.length; i++) out[i].end = out[i + 1] ? out[i + 1].t : out[i].t + 6;
  return out;
}
const lyricsCache = makeLru(200);
async function lyricsFor(t) {
  if (FOCUS) { P.lyrics = null; return; }   // focus: no lyrics fetching or display
  const npLyrics = () => P.npTab === 'lyrics' && !$('#nowPlaying').hidden;
  if (lyricsCache.has(t.id)) { P.lyrics = lyricsCache.get(t.id); if (npLyrics()) renderLyrics(); return; }
  P.lyrics = { loading: true };
  if (npLyrics()) renderLyrics();
  let res = null;
  try { res = await api.lyrics(slim(t)); } catch {}
  const val = res ? { lines: res.synced ? parseLrc(res.synced) : null, plain: res.plain, source: res.source } : { none: true };
  lyricsCache.set(t.id, val);
  if (P.current?.id !== t.id) return;
  P.lyrics = val;
  if (npLyrics()) renderLyrics();
}

// ---- synced lyrics view ----
// Every line is absolutely positioned and moved with translateY. When the active line changes
// each line glides to its new place (750 ms, fast-out-slow-in) with a small per-line stagger
// (20 ms × distance, max 200 ms); the active line sits at 35% of the height. Other lines fade by
// distance. The active line fills word by word (karaoke) from the audio clock.
const LY_EASE = 'cubic-bezier(.4,0,.2,1)';
const LY_ALPHA = [1, 0.2, 0.2, 0.15, 0.1, 0.08];
const LY_ALPHA_CLASSIC = [1, 0.42, 0.34, 0.27, 0.2, 0.15];
let LY = null;          // current view state
let lyricIdx = -1;      // set to -2 by callers to force a re-layout (seek, offset change, device change)

function renderLyrics() {
  const panel = $('#npPanel');
  LY = null; lyricIdx = -1;
  const L = P.lyrics;
  if (!L || L.loading) return panel.replaceChildren(loading());
  if (L.none || (!L.lines?.length && !L.plain)) return panel.replaceChildren(h('div', { class: 'lyrics-empty' }, icon('lyrics'), h('h3', null, 'No lyrics found'), h('div', null, 'Lyrics for this song aren’t available.')));
  if (!L.lines?.length) {
    panel.replaceChildren(h('div', { class: 'lyrics plain unsynced' }, L.plain.split(/\n/).map((x) => h('div', { class: 'lyric-line' }, x || ' '))), h('div', { class: 'lyrics-src' }, L.source ? 'Lyrics from ' + L.source : ''));
    return;
  }
  const stage = h('div', { class: 'ly-stage' + (S().lyricsGlow ? ' glow' : '') });
  const lines = L.lines.map((ln, i) => {
    let el;
    if (!ln.text) {
      // instrumental gap: a progress ring instead of a line
      el = h('div', { class: 'ly-line ly-gap' }, gapRing());
    } else {
      const words = ln.words || ln.text.split(/(\s+)/).filter((w) => w.length).reduce((a, w) => { if (/^\s+$/.test(w) && a.length) a[a.length - 1].text += w; else a.push({ text: w }); return a; }, []);
      el = h('div', { class: 'ly-line' }, words.map((w) => h('span', { class: 'ly-w' }, w.text)));
      el._words = words;
    }
    el.addEventListener('click', () => { engine.seek(ln.t + lyricsOffset() + 0.01); if (engine.paused) togglePlay(); lyResync(); });
    stage.append(el);
    return el;
  });

  const pill = h('button', { class: 'ly-resync', hidden: true, onclick: () => lyResync() }, icon('sync'), 'Auto scroll');
  const wrap = h('div', { class: 'ly-wrap' }, stage, pill);
  panel.replaceChildren(wrap, lyricsTools(L.source));
  LY = { stage, wrap, pill, lines, heights: [], active: -1, manual: 0, auto: S().lyricsAutoScroll !== false, laid: false, wordLine: -1 };
  if (!LY.auto) { pill.hidden = false; }
  lyMeasure();
  new ResizeObserver(() => { if (LY?.stage === stage) { lyMeasure(); lyPlace(false); } }).observe(stage);
  stage.addEventListener('wheel', (e) => {
    e.preventDefault();
    LY.auto = false;
    LY.pill.hidden = false;
    LY.manual -= e.deltaY * (e.deltaMode === 1 ? 32 : 1);
    lyClampManual();
    lyPlace(false);
  }, { passive: false });
  // mouse / touch drag like the Android finger drag
  let drag = null;
  stage.addEventListener('pointerdown', (e) => { if (e.button !== 0) return; drag = { y: e.clientY, m: LY.manual, moved: false, id: e.pointerId }; });
  stage.addEventListener('pointermove', (e) => {
    if (!drag) return;
    const dy = e.clientY - drag.y;
    if (!drag.moved && Math.abs(dy) < 6) return;
    if (!drag.moved) { drag.moved = true; stage.setPointerCapture(drag.id); LY.auto = false; LY.pill.hidden = false; }
    LY.manual = drag.m + dy; lyClampManual(); lyPlace(false);
  });
  const endDrag = (e) => { if (drag?.moved) { e.preventDefault(); stage.addEventListener('click', (ev) => ev.stopPropagation(), { capture: true, once: true }); } drag = null; };
  stage.addEventListener('pointerup', endDrag);
  stage.addEventListener('pointercancel', () => (drag = null));
  syncLyrics(true);
}
function gapRing() {
  const NS = 'http://www.w3.org/2000/svg';
  const svg = document.createElementNS(NS, 'svg');
  svg.setAttribute('viewBox', '0 0 40 40'); svg.setAttribute('class', 'ly-ring');
  for (const cls of ['trk', 'bar']) { const c = document.createElementNS(NS, 'circle'); c.setAttribute('cx', 20); c.setAttribute('cy', 20); c.setAttribute('r', 16); c.setAttribute('class', cls); c.setAttribute('pathLength', 100); svg.append(c); }
  return svg;
}
function lyMeasure() {
  if (!LY) return;
  LY.heights = LY.lines.map((el) => el.offsetHeight || 60);
  LY.H = LY.stage.clientHeight || 500;
}
function lyClampManual() {
  // keep at least the first/last line reachable (Android: 150 px after the first, 100 px before the last)
  const a = Math.max(0, LY.active), gap = 16;
  let above = 0; for (let k = 0; k < a; k++) above += LY.heights[k] + gap;
  let below = 0; for (let k = a; k < LY.lines.length - 1; k++) below += LY.heights[k] + gap;
  LY.manual = Math.max(-below + 100 - LY.H * 0.35, Math.min(above + 150, LY.manual));
}
function lyPlace(animate, stagger = true, dur = 750) {
  if (!LY) return;
  const a = Math.max(0, LY.active), gap = 16, anchor = LY.H * 0.35;
  const n = LY.lines.length, y = new Array(n);
  y[a] = 0;
  for (let k = a + 1; k < n; k++) y[k] = y[k - 1] + LY.heights[k - 1] + gap;
  for (let k = a - 1; k >= 0; k--) y[k] = y[k + 1] - LY.heights[k] - gap;
  for (let k = 0; k < n; k++) {
    const el = LY.lines[k], d = Math.abs(k - LY.active);
    const A = androidMotion() ? LY_ALPHA : LY_ALPHA_CLASSIC;
    const alpha = LY.active < 0 ? (k === 0 ? 0.6 : A[1]) : k === LY.active ? 1 : LY.auto ? A[Math.min(d, 5)] : A[1];
    const ty = anchor + y[k] + LY.manual;
    // skip work for lines far outside the viewport
    const vis = ty > -LY.H && ty < LY.H * 2;
    const sc = `, scale 550ms cubic-bezier(.05,.7,.1,1), text-shadow 450ms ${LY_EASE}`;
    el.style.transition = (animate && vis ? `transform ${dur}ms ${LY_EASE} ${stagger ? Math.min(d * 20, 200) : 0}ms, opacity 250ms ${LY_EASE}` : `opacity 250ms ${LY_EASE}`) + sc;
    el.style.transform = `translate3d(0,${ty.toFixed(1)}px,0)`;
    el.style.opacity = alpha;
    el.classList.toggle('active', k === LY.active);
  }
  LY.laid = true;
}
function lyResync() {
  if (!LY) return;
  const off = Math.abs(LY.manual);
  LY.auto = true;
  LY.pill.hidden = true;
  LY.manual = 0;
  lyPlace(true, true, Math.round(Math.max(200, Math.min(600, off / 4)) + 550));
}
// called every animation frame while the lyrics tab is visible
function syncLyrics(force) {
  if (!LY) return;
  const lines = P.lyrics?.lines;
  if (!lines) return;
  const off = lyricsOffset();
  const now = engine.currentTime - off;
  const hasWords = lines.some((l) => l.words);
  const t = now + (hasWords ? 0 : 0.25);   // Android looks 250 ms ahead for line-synced lyrics
  let i = -1;
  for (let k = 0; k < lines.length; k++) { if (lines[k].t <= t) i = k; else break; }
  if (i !== LY.active || force || lyricIdx === -2) {
    const first = !LY.laid || force || lyricIdx === -2;
    if (i !== LY.active) lyResetWords(LY.lines[i]);
    LY.active = i;
    lyricIdx = i;
    if (LY.auto || first) lyPlace(!first && LY.auto);
    else lyPlace(false);
  }
  // karaoke fill + gap ring on the active line
  if (i < 0) return;
  const karaoke = androidMotion() || !!lines[i].words;
  LY.stage.classList.toggle('karaoke', karaoke);
  const ln = lines[i], el = LY.lines[i];
  if (!ln.text) {
    const p = Math.max(0, Math.min(1, (now - ln.t) / Math.max(0.5, ln.end - ln.t - 0.65)));
    const bar = el.querySelector('.bar');
    if (bar) bar.style.strokeDashoffset = String(100 - p * 100);
    el.classList.toggle('show', ln.end - now > 0.65);
    return;
  }
  if (!karaoke) return;
  const spans = el.children, words = el._words || [];
  for (let w = 0; w < spans.length; w++) {
    const wd = words[w] || {};
    let st, en;
    if (ln.words) { st = wd.t; en = wd.end ?? (words[w + 1]?.t ?? ln.end); }
    else { st = ln.t + w * 0.03; en = st + 0.18; }   // Android's synthetic sweep for line-synced lyrics
    const p = Math.max(0, Math.min(1, (now - st) / Math.max(0.05, en - st)));
    const sp = spans[w];
    if (sp._p !== p) { sp._p = p; sp.style.setProperty('--p', (p * 100).toFixed(1) + '%'); }
    if (p > 0 && !sp._wob) { sp._wob = true; sp.classList.add('wob'); }
  }
}
// leaving a line resets its words so a replay fills again
function lyResetWords(el) { for (const sp of el?.children || []) { sp._p = -1; sp._wob = false; sp.classList.remove('wob'); sp.style.removeProperty('--p'); } }

// ================= ripple =================
document.addEventListener('pointerdown', (e) => {
  const el = e.target.closest('.btn, .icon-btn, .chip, .nav-item, .cm-item, .mood, .set-row.click, .text-btn, .play-btn, .np-pill, .seg button, .sg-item');
  if (!el || e.button !== 0) return;
  el.classList.add('rpl');
  const r = el.getBoundingClientRect();
  const size = Math.max(r.width, r.height) * 2.2;
  const rp = h('span', { class: 'ripple', style: { width: size + 'px', height: size + 'px', left: e.clientX - r.left - size / 2 + 'px', top: e.clientY - r.top - size / 2 + 'px' } });
  el.append(rp);
  setTimeout(() => rp.remove(), 600);
});

// ================= wiring =================
function wire() {
  document.body.classList.toggle('mac', api.platform === 'darwin');
  setIcon($('#navBack'), 'back');
  setIcon($('#navFwd'), 'fwd');
  $('.sb-icon').append(icon('search'));
  setIcon($('#searchClear'), 'close');
  $$('.ni-icon').forEach((s) => setIcon(s, s.dataset.icon));
  setIcon($('#newPlaylistBtn'), 'add');
  setIcon($('#importBtn'), 'importIcon');
  $('#importBtn').onclick = () => importDialog();
  for (const [id, ic] of [['#pbPrev', 'prev'], ['#pbNext', 'next'], ['#npPrev', 'prev'], ['#npNext', 'next'], ['#pbLyrics', 'lyrics'], ['#pbQueue', 'queue'], ['#pbExpand', 'up'], ['#qpClose', 'close'], ['#npClose', 'down'], ['#npMore', 'moreHoriz'], ['#npTempo', 'speed'], ['#npEq', 'equalizer'], ['#npDownload', 'download'], ['#npFull', 'fullscreen']]) setIcon($(id), ic);
  updateModeButtons();
  updatePlayButtons();
  updateExtraButtons();

  $('#navBack').onclick = back;
  $('#navFwd').onclick = forward;
  $$('.nav-item').forEach((a) => (a.onclick = () => go(a.dataset.route)));
  $('#newPlaylistBtn').onclick = () => createPlaylist().then((p) => p && go('local', { id: p.id }));

  $('#pbPlay').onclick = $('#npPlay').onclick = togglePlay;
  {
    // Android's player buttons re-flow on press: the pressed pill widens, the others give way
    const pills = $('#npPills'), W = { rest: [0.45, 1.3, 0.45], npPrev: [0.65, 1.1, 0.45], npPlay: [0.35, 1.9, 0.35], npNext: [0.45, 1.1, 0.65] };
    const set = (w) => [...pills.children].forEach((b, i) => (b.style.flexGrow = w[i]));
    set(W.rest);
    pills.addEventListener('pointerdown', (e) => { const b = e.target.closest('.np-pill'); if (b && e.button === 0 && androidMotion()) set(W[b.id]); });
    for (const ev of ['pointerup', 'pointerleave', 'pointercancel']) pills.addEventListener(ev, () => set(W.rest));
  }
  $('#pbNext').onclick = $('#npNext').onclick = () => next();
  $('#pbPrev').onclick = $('#npPrev').onclick = prev;
  $('#pbShuffle').onclick = $('#npShuffle').onclick = toggleShuffle;
  $('#pbRepeat').onclick = $('#npRepeat').onclick = cycleRepeat;
  $('#pbLike').onclick = $('#npLike').onclick = () => toggleLike(P.current);
  $('#pbMute').onclick = toggleMute;
  $('#pbQueue').onclick = () => toggleQueue();
  $('#pbSleep').onclick = $('#npSleep').onclick = sleepDialog;
  $('#pbDevice').onclick = (e) => { const r = e.currentTarget.getBoundingClientRect(); outputMenu({ clientX: r.left, clientY: r.top - 8 }); };
  setIcon($('#pbMini'), 'mini');
  setIcon($('#pbFocus'), 'focus');
  $('#pbFocus').onclick = focusButtonClick;
  api.onDuck(onDuck);
  $('#pbMini').onclick = () => toggleMini();
  $('#pbThumb').addEventListener('dblclick', () => { if (MINI) toggleMini(false); });
  $('#npTempo').onclick = tempoDialog;
  $('#npEq').onclick = () => { closeNowPlaying(); go('equalizer'); };
  $('#npDownload').onclick = () => { const t = P.current; if (!t) return; isDownloaded(t.id) ? removeDownload(t) : downloadTracks([t]); };
  $('#npFull').onclick = () => api.toggleFullscreen();
  $('#pbLyrics').onclick = () => ($('#nowPlaying').hidden || P.npTab !== 'lyrics' ? openNowPlaying('lyrics') : closeNowPlaying());
  $('#pbExpand').onclick = () => ($('#nowPlaying').hidden ? openNowPlaying() : closeNowPlaying());
  $('#pbThumb').onclick = () => { if (MINI) return toggleMini(false); openNowPlaying(); };
  $('#pbTitle').onclick = () => openNowPlaying();
  $('#npClose').onclick = closeNowPlaying;
  $('#npMore').onclick = (e) => P.current && songMenu(e, P.current);
  $('#npArt').ondblclick = togglePlay;
  $('#npArtist').addEventListener('click', (e) => { if (e.target.closest('.lnk')) closeNowPlaying(); });
  $('#playerbar').addEventListener('contextmenu', (e) => { if (P.current && e.target.closest('.pb-left')) { e.preventDefault(); songMenu(e, P.current); } });
  api.onWinState((s) => { if ('fullscreen' in s) setIcon($('#npFull'), s.fullscreen ? 'fullscreenExit' : 'fullscreen'); });
  api.onDownload((p) => {
    if (p.state === 'done') { DL.done.add(p.id); DL.progress.delete(p.id); }
    else if (p.state === 'removed' || p.state === 'error' || p.state === 'cancelled') { DL.progress.delete(p.id); if (p.state === 'removed') DL.done.delete(p.id); if (p.state === 'error') toast('Download failed: ' + shortErr(p.error)); }
    else DL.progress.set(p.id, p.progress || 0);
    refreshDownloadMarks();
  });

  window.addEventListener('mouseup', (e) => { if (e.button === 3) { e.preventDefault(); back(); } if (e.button === 4) { e.preventDefault(); forward(); } });
  $('#palHint').onclick = (e) => { e.stopPropagation(); openPalette(sInput.value.trim()); };
  document.addEventListener('keydown', (e) => {
    const tgt = e.target instanceof Element ? e.target : document.body;
    const inInput = tgt.matches('input:not([type=range]), textarea, select');
    const k = e.key;
    if (k === 'Escape') {
      if (!$('#modal').hidden) return closeModal();
      if ($('#focusView')) { $('#focusView').dispatchEvent(new PointerEvent('pointermove')); return; }
      if (!menuEl.hidden) return closeMenu();
      if (!$('#nowPlaying').hidden) return closeNowPlaying();
      if (!$('#queuePanel').hidden) return toggleQueue(false);
      if (MINI) return toggleMini(false);
    }
    if ((e.ctrlKey || e.metaKey) && e.shiftKey && k.toLowerCase() === 'f') { e.preventDefault(); focusButtonClick(); return; }
    // Focus: only play/pause and skip work; everything that opens UI (lyrics, search, queue…) is off
    if ($('#focusView')) {
      $('#focusView').dispatchEvent(new PointerEvent('pointermove'));
      if (k === ' ' || k === 'MediaPlayPause') { e.preventDefault(); togglePlay(); }
      else if ((e.ctrlKey || e.metaKey) && k === 'ArrowRight') { e.preventDefault(); next(); }
      else if ((e.ctrlKey || e.metaKey) && k === 'ArrowLeft') { e.preventDefault(); prev(); }
      else if (k !== 'Tab') e.preventDefault();
      return;
    }
    if ((e.ctrlKey || e.metaKey) && k.toLowerCase() === 'k') { e.preventDefault(); if (PAL) PAL.close(); else openPalette(); return; }
    if ((e.ctrlKey || e.metaKey) && k.toLowerCase() === 'f') { e.preventDefault(); sInput.focus(); sInput.select(); return; }
    if (k === 'F5') { e.preventDefault(); invalidateCaches(); rerender(); toast('Refreshed'); return; }
    if (e.altKey && k === 'ArrowLeft') { e.preventDefault(); return back(); }
    if (e.altKey && k === 'ArrowRight') { e.preventDefault(); return forward(); }
    if (inInput || !$('#modal').hidden || tgt.matches('input[type=range]')) return;
    // Don't hijack keys while a button/link has focus (Space would both activate
    // the button and toggle playback), and ignore bare-letter keys with modifiers.
    const onControl = tgt.closest('button, a, [role=button]');
    const c = e.ctrlKey || e.metaKey;
    if (k === ' ' || k === 'MediaPlayPause') { if (onControl && k === ' ' && onControl.matches(':focus-visible')) return; e.preventDefault(); togglePlay(); }
    else if (c && k === 'ArrowRight') { e.preventDefault(); next(); }
    else if (c && k === 'ArrowLeft') { e.preventDefault(); prev(); }
    else if (k === 'ArrowRight' && !onControl) engine.seek(engine.currentTime + 5);
    else if (k === 'ArrowLeft' && !onControl) engine.seek(engine.currentTime - 5);
    else if (k === 'ArrowUp' && !onControl) { e.preventDefault(); setVolume(DB.settings.volume + 0.05); }
    else if (k === 'ArrowDown' && !onControl) { e.preventDefault(); setVolume(DB.settings.volume - 0.05); }
    else if (c && k.toLowerCase() === 'l') { e.preventDefault(); toggleLike(P.current); }
    else if (c && k.toLowerCase() === 's') { e.preventDefault(); toggleShuffle(); }
    else if (c && k.toLowerCase() === 'r') { e.preventDefault(); cycleRepeat(); }
    else if (c || e.altKey) return; // leave other OS/browser shortcuts alone
    else if (k.toLowerCase() === 'm') toggleMute();
    else if (k.toLowerCase() === 'f') ($('#nowPlaying').hidden ? openNowPlaying() : closeNowPlaying());
    else if (k.toLowerCase() === 'q') toggleQueue();
    else if (k === '/') { e.preventDefault(); sInput.focus(); }
    else if (e.code === 'BracketLeft') adjustLyricsOffset(e.shiftKey ? -0.5 : -0.1);
    else if (e.code === 'BracketRight') adjustLyricsOffset(e.shiftKey ? 0.5 : 0.1);
    else if (k.toLowerCase() === 'n') toggleMini();
  });
  window.addEventListener('beforeunload', () => { flushPersist(); saveSession(true); });
}

// ================= Flow radio =================
// "Start radio" that is arranged for the crossfade: YouTube finds similar songs, we order them so
// tempo stays in a comfortable band and keys stay harmonically compatible. Tempo/key/energy come
// from analysing ~30 s of each song locally (no external service) and are remembered, so radios
// get better over time; with nothing known yet it falls back to YouTube's order.
const FLOW = { feats: new Map(), asked: new Set(), queue: [], running: 0, worker: null, jobs: new Map(), jobId: 0, failed: new Set() };
const featOf = (id) => FLOW.feats.get(id) || null;
// Energy is compared by its rank among the songs we know (raw values sit in a narrow band).
let energyIdx = { n: -1, sorted: [] };
function featRanked(id) {
  const f = FLOW.feats.get(id);
  if (!f) return null;
  if (energyIdx.n !== FLOW.feats.size) energyIdx = { n: FLOW.feats.size, sorted: [...FLOW.feats.values()].map((x) => x.energy).filter((x) => x != null).sort((a, b) => a - b) };
  const arr = energyIdx.sorted;
  if (arr.length < 12 || f.energy == null) return f;
  let lo = 0, hi = arr.length; while (lo < hi) { const m = (lo + hi) >> 1; if (arr[m] < f.energy) lo = m + 1; else hi = m; }
  return { ...f, energy: 0.1 + 0.8 * (lo / (arr.length - 1)) };
}
// What you like, from this computer only: artists you play and like, songs you skip or just heard.
let tasteCache = { at: 0, v: null };
function tasteModel() {
  if (tasteCache.v && Date.now() - tasteCache.at < 60e3) return tasteCache.v;
  const now = Date.now(), plays = new Map(), last = new Map();
  for (const hEnt of DB.history.slice(0, 1500)) {
    const t = hEnt.track; if (!t) continue;
    const age = (now - hEnt.at) / 86400e3;
    if (!last.has(t.id)) last.set(t.id, hEnt.at);
    if (age > 90) continue;
    const a = (t.artists?.[0]?.name || '').toLowerCase();
    if (a) plays.set(a, (plays.get(a) || 0) + Math.exp(-age / 30));   // recent plays count more
  }
  for (const t of DB.liked) { const a = (t.artists?.[0]?.name || '').toLowerCase(); if (a) plays.set(a, (plays.get(a) || 0) + 1.5); }
  const max = Math.max(1, ...plays.values());
  const liked = new Set(DB.liked.map((t) => t.id));
  const skips = DB.skips || {};
  const v = {
    artist: (n) => Math.min(1, (plays.get(n) || 0) / max * 1.4),
    liked: (id) => liked.has(id),
    skips: (id) => skips[id] || 0,
    recent: (id) => { const at = last.get(id); if (!at) return 0; const hrs = (now - at) / 3600e3; return hrs < 2 ? 1 : Math.max(0, 1 - hrs / 72); }
  };
  tasteCache = { at: Date.now(), v };
  return v;
}
async function loadFeats(ids) {
  const need = ids.filter((id) => id && !FLOW.feats.has(id) && !FLOW.asked.has(id));
  need.forEach((id) => FLOW.asked.add(id));
  if (!need.length) return;
  try { const got = await api.flowGet(need); for (const [id, f] of Object.entries(got)) if ((f.v || 1) >= 2) FLOW.feats.set(id, f); } catch {}   // v1 keys were biased → re-learn
}
function flowWorker() {
  if (!FLOW.worker) {
    FLOW.worker = new Worker(new URL('./flow/analyze.js', import.meta.url), { type: 'module' });
    FLOW.worker.onmessage = (e) => { const j = FLOW.jobs.get(e.data.id); FLOW.jobs.delete(e.data.id); if (j) (e.data.error ? j.rej(new Error(e.data.error)) : j.res(e.data.result)); };
  }
  return FLOW.worker;
}
const runAnalysis = (pcm) => new Promise((res, rej) => { const id = ++FLOW.jobId; FLOW.jobs.set(id, { res, rej }); flowWorker().postMessage({ id, pcm, sr: SAMPLE_RATE }, [pcm.buffer]); });
/** queue songs for background analysis; `front` = analyse these first */
async function ensureFeatures(tracks, { front = false } = {}) {
  tracks = (tracks || []).filter((t) => t?.id);
  await loadFeats(tracks.map((t) => t.id));
  if (S().flowLearn === false) return;
  const todo = tracks.filter((t) => !FLOW.feats.has(t.id) && !FLOW.failed.has(t.id) && !FLOW.queue.some((x) => x.id === t.id));
  if (front) FLOW.queue.unshift(...todo); else FLOW.queue.push(...todo);
  pumpAnalysis();
}
let onBattery = false;
async function pumpAnalysis() {
  if (engine.xfading || P.xfPending) { setTimeout(pumpAnalysis, 3000); return; }   // never during a blend
  while (FLOW.running < 1 && FLOW.queue.length) {
    const t = FLOW.queue.shift();
    // on battery only the free job (the song playing now, from bytes already downloaded) runs
    if (onBattery && !isLocal(t) && t.id !== P.current?.id) { FLOW.queue.push(t); if (FLOW.queue.every((x) => x.id !== P.current?.id)) return; continue; }
    if (FLOW.feats.has(t.id)) continue;
    FLOW.running++;
    (async () => {
      try {
        const pcm = await sampleAudio(t, { local: isLocal(t) });
        if (!pcm || pcm.length < SAMPLE_RATE * 8) throw new Error('too short');
        const f = await runAnalysis(new Float32Array(pcm));
        if (f) { FLOW.feats.set(t.id, f); api.flowPut(t.id, f).catch(() => {}); onFlowLearned(t.id); }
      } catch (e) { FLOW.failed.add(t.id); console.warn('flow: could not analyse', t.id, e.message); }
      finally { FLOW.running--; (window.requestIdleCallback || setTimeout)(() => pumpAnalysis(), { timeout: 2000 }); }
    })();
  }
}
let flowReplanTimer = null;
function onFlowLearned(id) {
  if (P.flow && (P.flow.pool.some((t) => t.id === id) || P.current?.id === id)) { clearTimeout(flowReplanTimer); flowReplanTimer = setTimeout(replanFlow, 1200); }
  if (P.current?.id === id && P.streamInfo) setStreamInfo(P.current, P.streamInfo.info);
  if (!$('#queuePanel').hidden || (P.npTab === 'queue' && !$('#nowPlaying').hidden)) { clearTimeout(onFlowLearned.t); onFlowLearned.t = setTimeout(renderQueue, 400); }
}
// Keep everything already played and the current song (plus the next one near the end of the
// song); re-arrange the rest from the whole pool.
function replanFlow() {
  const F = P.flow;
  if (!F || P.queue !== F.queue) { P.flow = null; return; }
  if (F.locked) return; // the user rearranged the queue — respect it
  // the next song stays put once we're close to the transition (it may already be crossfading)
  const nearEnd = engine.xfading || P.xfPending || (engine.duration && engine.duration - engine.currentTime < 35);
  const keep = Math.min(P.queue.length, P.idx + (nearEnd ? 2 : 1));
  const fixed = P.queue.slice(0, keep);
  const used = new Set(fixed.map((t) => t.id));
  const rest = F.pool.filter((t) => !used.has(t.id));
  if (!rest.length) return;
  const from = fixed[fixed.length - 1];
  const ctx = FOCUS ? { ...contextFor(), energy: 0.42, maxBpm: 130, label: 'focus' } : contextFor();   // focus: steady, unhurried
  const plan = planFlow(from, rest, featRanked, { ctx, band: FOCUS ? 0.08 : 0.12, taste: tasteModel() }).slice(0, Math.max(0, F.size - fixed.length));
  const tail = plan.map((p) => ({ ...withQid(p.track), auto: true, flowScore: p.score }));
  P.queue.splice(keep, P.queue.length - keep, ...tail);
  F.queue = P.queue;
  queueChanged();
}
/** Start a flow radio from a song, album, playlist, artist or local playlist. */
async function startFlowRadio(item) {
  if (S().flowRadio === false) return item.type === 'song' || !item.type ? playSingle(item) : playItem({ ...item, type: item.type });
  const isSong = !item.type || item.type === 'song';
  toast('Building a flow radio…');
  let seeds = [];
  try { seeds = isSong ? [item] : (await tracksFor(item)).filter((t) => t?.id); } catch (e) { return toast("Couldn't load: " + shortErr(e.message)); }
  if (!seeds.length) return toast('Nothing to start a radio from');
  const seed = isSong ? item : seeds[0];
  // play the seed right away; the rest arrives while it plays
  P.queue = [withQid(seed)];
  P.original = null; P.shuffle = false;
  P.source = 'Flow radio • ' + (item.title || seed.title);
  P.sourceKey = isSong ? 'radio:' + seed.id : (srcKey(item) || 'flow');
  P.radioSeed = seed.id;
  const F = (P.flow = { seed, pool: [], queue: P.queue, size: 40 });
  playAt(0);
  updateModeButtons();
  updateNpFrom();
  const picks = isSong ? [seed] : [seeds[0], seeds[Math.floor(seeds.length / 2)], seeds[seeds.length - 1]].filter((x, i, a) => a.findIndex((y) => y.id === x.id) === i);
  const lists = await Promise.all(picks.map((t) => api.upNext(t.id).catch(() => [])));
  if (P.flow !== F) return;
  const seen = new Set([seed.id]);
  const pool = [];
  // interleave the seeds' radios (keeps YouTube's relevance order) and include the collection itself
  for (let i = 0; i < 60; i++) for (const l of [...lists, isSong ? [] : seeds.slice(1, 16)]) { const t = l[i]; if (t?.id && !seen.has(t.id)) { seen.add(t.id); pool.push(t); } }
  // a few songs you liked from the same artists — familiar anchors between discoveries
  const poolArtists = new Set(pool.map((t) => (t.artists?.[0]?.name || '').toLowerCase()).filter(Boolean));
  const anchors = DB.liked.filter((t) => !seen.has(t.id) && poolArtists.has((t.artists?.[0]?.name || '').toLowerCase())).slice(0, 6);
  anchors.forEach((t) => { seen.add(t.id); pool.splice(Math.min(pool.length, 3 + Math.floor(Math.random() * 12)), 0, t); });
  F.pool = pool;
  await loadFeats([seed.id, ...pool.map((t) => t.id)]);
  if (P.flow !== F) return;
  replanFlow();
  $('#toast').hidden = true;
  ensureFeatures([seed, ...pool.slice(0, 10)], { front: true });
}
// autoplay for flow radios: grow the pool and re-arrange instead of appending blindly
async function growFlowPool() {
  const F = P.flow;
  if (!F || F.growing) return false;
  F.growing = true;
  try {
    const last = P.queue[P.queue.length - 1];
    const more = await api.upNext(last.id).catch(() => []);
    if (P.flow !== F) return true;
    const have = new Set([...F.pool.map((t) => t.id), ...P.queue.map((t) => t.id)]);
    const add = more.filter((t) => t?.id && !have.has(t.id));
    F.pool.push(...add);
    F.size += add.length;
    await loadFeats(add.map((t) => t.id));
    replanFlow();
    ensureFeatures(add.slice(0, 20));
  } finally { F.growing = false; }
  return true;
}

// ================= welcome (brand-new installations only) =================
// A full-screen, animated welcome the very first time the app is opened. Step 2 is the same honest
// note as the one-time card below (see the comment on maybeShowFirstRunNotice for why it exists).
// It never gates playback: the app is loaded behind it and Skip / Esc close it at any time.
async function showWelcome() {
  setSetting('welcomed', true);
  setSetting('noticeSeen', true);   // the note is part of the welcome
  flushPersist();
  const reduce = matchMedia('(prefers-reduced-motion: reduce)').matches;
  let premium = false;
  const signedIn = await api.authStatus().catch(() => false);
  if (signedIn) premium = await Promise.race([api.premiumStatus().catch(() => null), new Promise((r) => setTimeout(() => r(null), 2500))]) === true;
  const openYTM = () => api.openExternal('https://music.youtube.com');

  const feature = (ic, t, d) => h('div', { class: 'wl-feat' }, h('div', { class: 'wl-feat-ic' }, icon(ic)), h('div', null, h('div', { class: 'wl-feat-t' }, t), h('div', { class: 'wl-feat-d' }, d)));
  const tile = (ic, t, d, fn) => h('button', { class: 'wl-tile', onclick: fn }, h('div', { class: 'wl-feat-ic' }, icon(ic)), h('div', { class: 'wl-tile-t' }, t), h('div', { class: 'wl-feat-d' }, d));
  const steps = [
    {
      label: 'Welcome to Soncle',
      body: () => [
        h('img', { class: 'wl-logo', src: 'icon.png', alt: '' }),
        h('h1', { class: 'wl-h', id: 'wlTitle' }, 'Welcome to Soncle'),
        h('p', { class: 'wl-sub' }, 'Your music, beautifully played.'),
        h('div', { class: 'wl-feats' },
          feature('lyrics', 'Synced lyrics', 'Word-by-word, with timing you can nudge'),
          feature('crossfade', 'Crossfade & Flow radio', 'Radios arranged by tempo and key'),
          feature('equalizer', 'Sound that fits', 'EQ and profiles per headphone or speaker'),
          api.mobile ? feature('musicFolder', 'Bring your playlists', 'Import from Spotify, CSV or a list') : feature('musicFolder', 'Your files too', 'Play local music and import from Spotify'))
      ],
      next: 'Continue'
    },
    {
      label: 'A quick note',
      body: () => [
        h('div', { class: 'wl-badge' }, icon('info')),
        h('h1', { class: 'wl-h', id: 'wlTitle' }, 'A quick note'),
        h('p', { class: 'wl-sub wl-note' }, premium ? 'This is an unofficial hobby project.' : 'This is an unofficial hobby project. It can’t show YouTube’s ads, so artists aren’t paid through it.'),
        premium ? null : h('p', { class: 'wl-sub wl-note' }, 'For the official experience — and to support artists — we recommend ',
          h('a', { href: '#', class: 'fn-link', onclick: (e) => { e.preventDefault(); openYTM(); } }, 'music.youtube.com'), ' with YouTube Premium.'),
        premium ? null : h('button', { class: 'btn tonal wl-inline', onclick: openYTM }, icon('external'), 'Open YouTube Music')
      ],
      next: 'Continue'
    },
    {
      label: 'Make it yours',
      body: () => [
        h('h1', { class: 'wl-h', id: 'wlTitle' }, 'Make it yours'),
        h('p', { class: 'wl-sub' }, 'All optional — you can do these any time from Settings and the sidebar.'),
        h('div', { class: 'wl-tiles' },
          signedIn ? null : tile('login', 'Sign in', 'Your YouTube Music library and likes', async () => { close(); await signInFlow(); }),
          tile('musicFolder', 'Add a music folder', 'Play the songs on this computer', () => { close(); addMusicFolder(); }),
          tile('importIcon', 'Import a playlist', 'From a Spotify link or a CSV', () => { close(); importDialog(); }))
      ],
      next: 'Start listening'
    }
  ];

  let i = 0, closed = false;
  const stage = h('div', { class: 'wl-stage' });
  const dots = h('div', { class: 'wl-dots', 'aria-hidden': 'true' }, steps.map(() => h('i')));
  const back = h('button', { class: 'text-btn wl-back', onclick: () => go_(i - 1) }, 'Back');
  const nextBtn = h('button', { class: 'btn filled wl-next', onclick: () => (i < steps.length - 1 ? go_(i + 1) : close()) });
  const skip = h('button', { class: 'text-btn wl-skip', onclick: () => close() }, 'Skip');
  const root = h('div', { class: 'wl' + (reduce ? ' still' : ''), role: 'dialog', 'aria-modal': 'true', 'aria-labelledby': 'wlTitle' },
    h('div', { class: 'wl-bg', 'aria-hidden': 'true' }, h('i', { class: 'b1' }), h('i', { class: 'b2' }), h('i', { class: 'b3' })),
    skip, stage,
    h('div', { class: 'wl-foot' }, back, dots, nextBtn));
  function render_(dir) {
    const s = steps[i];
    const page = h('div', { class: 'wl-page' + (dir ? ' from-' + dir : '') }, s.body());
    [...page.children].forEach((c, k) => c.style.setProperty('--d', k * 70 + 'ms'));
    const old = stage.firstElementChild;
    if (old && !reduce) { old.classList.add('leave-' + (dir || 'next')); old.addEventListener('animationend', () => old.remove(), { once: true }); setTimeout(() => old.remove(), 500); }
    else if (old) old.remove();
    stage.append(page);
    nextBtn.textContent = s.next;
    back.style.visibility = i ? 'visible' : 'hidden';
    [...dots.children].forEach((d, k) => d.classList.toggle('on', k === i));
    root.setAttribute('aria-label', s.label);
    requestAnimationFrame(() => nextBtn.focus({ preventScroll: true }));
  }
  function go_(n) { if (n < 0 || n >= steps.length || n === i) return; const dir = n > i ? 'next' : 'prev'; i = n; render_(dir); }
  function close() {
    if (closed) return;
    closed = true;
    document.removeEventListener('keydown', onKey, true);
    if (reduce) root.remove(); else { root.classList.add('out'); setTimeout(() => root.remove(), 520); }
  }
  function onKey(e) {
    if (e.key === 'Escape') { e.preventDefault(); e.stopPropagation(); return close(); }
    if (e.key === 'ArrowRight') { e.preventDefault(); e.stopPropagation(); return i < steps.length - 1 ? go_(i + 1) : null; }
    if (e.key === 'ArrowLeft') { e.preventDefault(); e.stopPropagation(); return go_(i - 1); }
    if (e.key === 'Tab') {
      // keep focus inside the welcome
      const f = [...root.querySelectorAll('button, a[href]')].filter((x) => x.offsetParent && getComputedStyle(x).visibility !== 'hidden');
      if (!f.length) return;
      const at = f.indexOf(document.activeElement);
      e.preventDefault();
      f[(at + (e.shiftKey ? -1 : 1) + f.length) % f.length].focus();
      return;
    }
    // keep the app's own shortcuts (space = play etc.) from firing underneath
    if (!root.contains(document.activeElement) || e.key.length === 1) e.stopPropagation();
  }
  document.addEventListener('keydown', onKey, true);
  document.body.append(root);
  render_(null);
}

// ================= smart ducking =================
// Another app started making sound (a video, a voice note, a call): lower the music — or pause it —
// and bring it back smoothly when that app goes quiet. Detection runs in the main process.
function onDuck(st) {
  const mode = S().smartDuck || 'duck';
  if (mode === 'off') return;
  if (st.active) {
    if (!P.playing || engine.paused) return;
    if (mode === 'pause') { P.duckPaused = true; engine.pause({ fade: 0.4 }); sendState(); toast('Paused while another app plays sound'); }
    else { P.ducked = true; engine.setDuck(S().duckLevel ?? 0.25, 0.35); }
  } else {
    if (P.ducked) { P.ducked = false; engine.setDuck(1, 1.2); }
    if (P.duckPaused) { P.duckPaused = false; sendState(); if (engine.paused) engine.play().catch(() => {}); }
  }
}

// ================= focus mode =================
// A zen, distraction-free session: minimal full-screen view, a focus EQ, settings tuned for
// uninterrupted long listening, and a session timer. Everything it changes is snapshotted first and
// restored when the session ends or is turned off (also after a crash — the snapshot is persisted).
const FOCUS_KEYS = ['eq', 'crossfade', 'smartCrossfade', 'autoplay', 'autoSkipOnError', 'skipSilence', 'normalize', 'crossfadeOnSkip', 'smartDuck', 'discord', 'noDuplicates'];
const FOCUS_PROFILE = { crossfade: 8, smartCrossfade: true, autoplay: true, autoSkipOnError: true, skipSilence: true, normalize: true, crossfadeOnSkip: true, smartDuck: 'duck', discord: false, noDuplicates: true };
let FOCUS = null;   // { end, minutes, timer, el }
function applyFocusSettings() {
  engine.setOptions({ skipSilence: !!S().skipSilence, normalize: S().normalize !== false });
  engine.setEq(S().eq);
  api.storeSet('settings', DB.settings).catch(() => {});
  updateExtraButtons();
}
function focusDialog() {
  if (FOCUS) return endFocus({ early: true });
  const opts = [25, 50, 90, 0];
  let pauseAtEnd = !!S().focusPauseAtEnd;
  const pauseSw = h('div', { class: 'switch' + (pauseAtEnd ? ' on' : ''), onclick: () => { pauseAtEnd = !pauseAtEnd; pauseSw.classList.toggle('on', pauseAtEnd); } });
  modal(h('div', { class: 'dialog' }, h('h3', null, 'Focus mode'),
    h('p', null, 'A quiet screen with just the song that’s playing — no artwork, lyrics, menus or pop-ups — plus a focus EQ and long smooth crossfades so nothing interrupts. Your settings come back when the session ends.'),
    h('div', { class: 'opt-grid' }, opts.map((m) => h('button', { class: 'chip' + ((S().focusMinutes ?? 50) === m ? ' active' : ''), onclick: () => { closeModal(); setSetting('focusMinutes', m); setSetting('focusPauseAtEnd', pauseAtEnd); startFocus(m, { pauseAtEnd }); } }, m ? `${m} min` : 'No timer'))),
    h('div', { class: 'range-row', style: { marginTop: '14px' } }, h('label', { style: { flex: 1, width: 'auto' } }, 'Pause the music when the session ends'), pauseSw),
    h('div', { class: 'd-actions' }, h('button', { class: 'text-btn', onclick: closeModal }, 'Cancel'))));
}
async function startFocus(minutes, { pauseAtEnd = false, resume = null } = {}) {
  if (!resume) {
    // snapshot what we change (kept on disk until restored)
    DB.settings.focusBackup = { settings: Object.fromEntries(FOCUS_KEYS.map((k) => [k, structuredClone(S()[k])])), at: Date.now() };
    Object.assign(DB.settings, FOCUS_PROFILE);
    DB.settings.eq = { enabled: true, preset: 'Focus', preamp: 0, gains: EQ_PRESETS.Focus.slice() };
    DB.settings.focusSession = { end: minutes ? Date.now() + minutes * 60e3 : 0, minutes, pauseAtEnd };
    persist('settings');
    applyFocusSettings();
  }
  const sess = DB.settings.focusSession;
  FOCUS = { ...sess };
  document.body.classList.add('focus-on');
  setIcon($('#pbFocus'), 'focus'); $('#pbFocus').classList.add('on');
  // keep the music going: if the queue is short, continue with a calm Flow radio
  if (P.current && P.queue.length - P.idx < 4 && S().flowRadio !== false) startFlowRadioFromCurrent();
  else if (!P.current) toast('Pick something to play — Focus mode keeps it going');
  if (!P.playing && P.current && !resume) togglePlay();
  showFocusView();
  FOCUS.timer = setInterval(focusTick, 1000);
  focusTick();
}
function startFlowRadioFromCurrent() {
  const t = P.current;
  if (!t || (P.flow && P.flow.queue === P.queue)) return;
  // grow the current queue into a flow radio without interrupting the song that's playing
  api.upNext(t.id).then(async (more) => {
    if (P.current !== t) return;
    const have = new Set(P.queue.map((x) => x.id));
    const pool = more.filter((x) => x?.id && !have.has(x.id));
    P.flow = { seed: t, pool, queue: P.queue, size: P.queue.length + 30 };
    await loadFeats(pool.map((x) => x.id));
    replanFlow();
    ensureFeatures(pool.slice(0, 10));
  }).catch(() => {});
}
function focusTick() {
  if (!FOCUS) return;
  if (FOCUS.end && Date.now() >= FOCUS.end) return endFocus({ done: true });
  const el = $('#focusView');
  if (!el) return;
  const t = P.current;
  const key = t ? t.id : '';
  if (el.dataset.song !== key) {
    // gentle crossfade between song names — nothing else changes on screen
    el.dataset.song = key;
    const box = el.querySelector('.fz-now');
    const next = h('div', { class: 'fz-song-in' }, h('div', { class: 'fz-title' }, t ? t.title : 'Nothing playing'), h('div', { class: 'fz-artist' }, t ? artistNames(t) : ''));
    const old = box.firstElementChild;
    if (old) { old.classList.add('fz-song-out'); setTimeout(() => old.remove(), 900); }
    box.append(next);
  }
  setIcon(el.querySelector('.fz-play'), P.playing ? 'pause' : 'play');
  const d = engine.duration || t?.duration || 0;
  el.querySelector('.fz-line').style.transform = `scaleX(${d ? Math.min(1, engine.currentTime / d) : 0})`;
}
const endsAt = (ms) => new Date(ms).toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' });
function showFocusView() {
  $('#focusView')?.remove();
  const btn = (ic, title, fn, cls = '') => h('button', { class: 'icon-btn fz-btn ' + cls, title, onclick: fn }, icon(ic));
  // Zen: just the song. No artwork, no clock, no lyrics, no navigation. Controls and the way out
  // appear only when you move the mouse, then fade away again.
  const view = h('div', { id: 'focusView', class: 'fz-idle', role: 'dialog', 'aria-modal': 'true', 'aria-label': 'Focus mode' },
    h('div', { class: 'fz-ambient' }),
    h('div', { class: 'fz-now' }),
    h('div', { class: 'fz-ui' },
      h('div', { class: 'fz-ctrls' }, btn('prev', 'Previous', () => prev()), btn('play', 'Play/Pause', () => togglePlay(), 'fz-play big'), btn('next', 'Next', () => next())),
      h('div', { class: 'fz-meta' }, FOCUS.end ? 'Focus · until ' + endsAt(FOCUS.end) : 'Focus'),
      h('button', { class: 'text-btn fz-end', onclick: () => endFocus({ early: true }) }, 'End focus')),
    h('div', { class: 'fz-track' }, h('div', { class: 'fz-line' })));
  let idleT = null;
  const wake = () => { view.classList.remove('fz-idle'); clearTimeout(idleT); idleT = setTimeout(() => view.classList.add('fz-idle'), 2600); };
  view.addEventListener('pointermove', wake);
  view.addEventListener('pointerdown', wake);
  view.addEventListener('keydown', wake);
  document.body.append(view);
  focusTick();
}
function endFocus({ done = false, early = false } = {}) {
  if (!FOCUS && !DB.settings.focusSession) return;
  const sess = DB.settings.focusSession || FOCUS || {};
  clearInterval(FOCUS?.timer);
  FOCUS = null;
  // restore everything we changed
  const b = DB.settings.focusBackup;
  if (b?.settings) Object.assign(DB.settings, structuredClone(b.settings));
  DB.settings.focusBackup = null;
  DB.settings.focusSession = null;
  persist('settings');
  applyFocusSettings();
  document.body.classList.remove('focus-on');
  $('#pbFocus').classList.remove('on');
  const v = $('#focusView');
  if (v) { v.classList.add('out'); setTimeout(() => v.remove(), 400); }
  if (done) {
    if (sess.pauseAtEnd && P.playing) engine.pause({ fade: 3 });
    toast(`Focus session complete — ${sess.minutes} min. Nice work.`);
  } else if (early) toast('Focus mode off — your settings are back');
}
// ================= Ctrl+K command bar =================
// One box for everything: play/skip/like, jump to any page, change sound settings, start radio or
// Focus, and find music (your library instantly, YouTube Music as you type).
function fuzzy(q, text) {
  if (!q) return 1;
  const t = text.toLowerCase();
  const i = t.indexOf(q);
  if (i >= 0) return 100 - i + (i === 0 || t[i - 1] === ' ' ? 30 : 0) - t.length * 0.05;
  let ti = 0, score = 0, streak = 0;
  for (const ch of q) {
    const j = t.indexOf(ch, ti);
    if (j < 0) return 0;
    streak = j === ti ? streak + 1 : 0;
    score += 1 + streak * 2 + (j === 0 || t[j - 1] === ' ' ? 3 : 0);
    ti = j + 1;
  }
  return score;
}
function paletteCommands() {
  const cur = P.current, s = S();
  const cmd = (title, ic, run, { sub = '', keys = '', when = true, words = '' } = {}) => (when ? { group: 'Actions', title, ic, run, sub, keys, words } : null);
  const page = (title, ic, name, args) => ({ group: 'Go to', title, ic, run: () => { closeNowPlaying(); go(name, args); }, words: 'open page' });
  const setting = (title, ic, run, sub = '') => ({ group: 'Sound & settings', title, ic, run, sub });
  const eqPreset = (name) => setting(`EQ: ${name}`, 'equalizer', () => { const eq = S().eq; eq.enabled = true; eq.preset = name; eq.gains = EQ_PRESETS[name].slice(); eq.preamp = PRESET_PREAMP[name] || 0; engine.setEq(eq); persist('settings'); saveDeviceProfile(); updateExtraButtons(); toast(`EQ: ${name}`); });
  return [
    cmd(P.playing ? 'Pause' : 'Play', P.playing ? 'pause' : 'play', () => togglePlay(), { keys: 'Space', when: !!cur || P.queue.length }),
    cmd('Next song', 'next', () => next(), { keys: 'Ctrl+→', when: !!cur }),
    cmd('Previous song', 'prev', () => prev(), { keys: 'Ctrl+←', when: !!cur }),
    cmd(cur && isLiked(cur.id) ? 'Remove from liked songs' : 'Like this song', 'heart', () => toggleLike(cur), { keys: 'Ctrl+L', when: !!cur, sub: cur?.title }),
    cmd(P.shuffle ? 'Shuffle off' : 'Shuffle on', 'shuffle', () => toggleShuffle(), { keys: 'Ctrl+S' }),
    cmd('Repeat: ' + ({ off: 'all', all: 'one', one: 'off' }[P.repeat] || 'all'), 'repeat', () => cycleRepeat(), { keys: 'Ctrl+R' }),
    cmd('Start Flow radio from this song', 'radio', () => startFlowRadioFromCurrent(), { when: !!cur, sub: cur?.title, words: 'mix similar' }),
    cmd(FOCUS ? 'End Focus session' : 'Start Focus mode', 'focus', () => focusButtonClick(), { keys: 'Ctrl+Shift+F', words: 'zen concentrate work study' }),
    cmd('Now playing', 'up', () => openNowPlaying(), { when: !!cur }),
    cmd('Lyrics', 'lyrics', () => openNowPlaying('lyrics'), { when: !!cur }),
    cmd('Queue', 'queue', () => toggleQueue(true)),
    cmd('Mini player', 'mini', () => toggleMini(true), { keys: 'N' }),
    cmd(s.muted ? 'Unmute' : 'Mute', 'volume', () => toggleMute(), { keys: 'M' }),
    cmd('Sleep timer: 15 minutes', 'sleep', () => setSleep({ minutes: 15 })),
    cmd('Sleep timer: 30 minutes', 'sleep', () => setSleep({ minutes: 30 })),
    cmd('Sleep timer: 1 hour', 'sleep', () => setSleep({ minutes: 60 })),
    cmd('Sleep timer: end of song', 'sleep', () => setSleep({ endOfSong: true })),
    cmd('Turn off sleep timer', 'sleep', () => setSleep(null), { when: !!P.sleep }),
    page('Home', 'home', 'home'), page('Explore', 'explore', 'explore'), page('Library', 'library', 'library'),
    page('Liked songs', 'heart', 'liked'), page('Local files', 'musicFolder', 'files'), page('History', 'history', 'history'),
    page('Your stats', 'stats', 'stats'), page('Settings', 'settings', 'settings'), page('Sound', 'equalizer', 'equalizer'),
    setting(`Loudness: Quiet (-19 LUFS)`, 'volume', () => { setSetting('normalize', true); setSetting('normTarget', -19); engine.setOptions({ normalize: true, normTarget: -19 }); toast('Loudness: Quiet'); }),
    setting(`Loudness: Normal (-14 LUFS)`, 'volume', () => { setSetting('normalize', true); setSetting('normTarget', -14); engine.setOptions({ normalize: true, normTarget: -14 }); toast('Loudness: Normal'); }),
    setting(`Loudness: Loud (-11 LUFS)`, 'volume', () => { setSetting('normalize', true); setSetting('normTarget', -11); engine.setOptions({ normalize: true, normTarget: -11 }); toast('Loudness: Loud'); }),
    s.headphone ? setting(`Headphone correction ${s.headphoneOn !== false ? 'off' : 'on'}`, 'headphones', () => { setSetting('headphoneOn', S().headphoneOn === false); applySound(); saveDeviceProfile(); toast(`Headphone correction ${S().headphoneOn !== false ? 'on' : 'off'}`); }, s.headphone.name) : null,
    setting(s.eq.enabled ? 'Equalizer off' : 'Equalizer on', 'equalizer', () => { const eq = S().eq; eq.enabled = !eq.enabled; engine.setEq(eq); persist('settings'); saveDeviceProfile(); updateExtraButtons(); }),
    ...Object.keys(EQ_PRESETS).map(eqPreset),
    ...[0, 4, 6, 8, 12].map((x) => setting(x ? `Crossfade ${x} s` : 'Crossfade off (gapless)', 'crossfade', () => { setSetting('crossfade', x); toast(x ? `Crossfade ${x} s` : 'Crossfade off: gapless'); })),
    setting('Transitions: Smart mix', 'crossfade', () => { setSetting('xfStyle', 'smart'); toast('Smart mix on'); }),
    setting('Transitions: Classic fade', 'crossfade', () => { setSetting('xfStyle', 'classic'); toast('Classic crossfade'); })
  ].filter(Boolean);
}
let PAL = null;
function openPalette(initial = '') {
  if (PAL) { PAL.input.focus(); PAL.input.select(); return; }
  const input = h('input', { class: 'pal-input', placeholder: 'Type a command, a page, or music to find…', spellcheck: false, value: initial });
  const list = h('div', { class: 'pal-list', role: 'listbox' });
  const box = h('div', { class: 'pal-box', role: 'dialog', 'aria-modal': 'true', 'aria-label': 'Command bar' }, h('div', { class: 'pal-top' }, icon('search'), input, h('kbd', null, 'Esc')), list);
  const wrap = h('div', { class: 'pal-wrap', onpointerdown: (e) => { if (e.target === wrap) close(); } }, box);
  const cmds = paletteCommands();
  let items = [], sel = 0, remote = [], remoteQ = '', seq = 0;
  const libraryHits = (q) => {
    if (q.length < 2) return [];
    const words = q.split(/\s+/);
    const seen = new Set(), out = [];
    const pools = [[DB.liked, 'Liked'], [DB.history.map((x) => x.track).filter(Boolean), 'Played'], [LOCAL?.tracks || [], 'On this computer']];
    for (const [pool, where] of pools) for (const t of pool) {
      if (!t?.id || seen.has(t.id)) continue;
      const hay = (t.title + ' ' + artistNames(t) + ' ' + (t.album?.name || '')).toLowerCase();
      if (words.every((w) => hay.includes(w))) { seen.add(t.id); out.push({ group: 'Your music', title: t.title, sub: `${artistNames(t)} · ${where}`, thumb: thumbOf(t), run: () => playList([t], 0, { source: 'Command bar' }), alt: () => addToQueue([t]) }); }
      if (out.length >= 6) return out;
    }
    for (const pl of DB.playlists || []) if (words.every((w) => pl.name.toLowerCase().includes(w))) out.push({ group: 'Your music', title: pl.name, sub: `Playlist · ${nOf(pl.tracks.length, 'song', 'songs')}`, ic: 'queue', run: () => go('local', { id: pl.id }) });
    return out;
  };
  const draw = () => {
    const q = input.value.trim().toLowerCase();
    const scored = q ? cmds.map((c) => ({ c, sc: Math.max(fuzzy(q, c.title), fuzzy(q, c.words || '') * 0.6, fuzzy(q, c.sub || '') * 0.4) })).filter((x) => x.sc > 0).sort((a, b) => b.sc - a.sc).map((x) => x.c) : cmds.filter((c) => c.group === 'Actions' || c.group === 'Go to');
    items = [...scored.slice(0, q ? 7 : 12), ...libraryHits(q), ...(remoteQ === q ? remote : [])];
    sel = Math.min(sel, Math.max(0, items.length - 1));
    let group = '';
    list.replaceChildren(...items.flatMap((it, i) => {
      const head = it.group !== group ? h('div', { class: 'pal-group' }, (group = it.group)) : null;
      const row = h('div', { class: 'pal-item' + (i === sel ? ' sel' : ''), role: 'option', 'aria-selected': String(i === sel), onpointermove: () => { if (sel !== i) { sel = i; mark(); } }, onclick: () => choose(i) },
        it.thumb ? img(it.thumb, 'pal-thumb') : h('span', { class: 'pal-ic' }, icon(it.ic || 'note')),
        h('div', { class: 'pal-text' }, h('div', { class: 'pal-title' }, it.title), it.sub ? h('div', { class: 'pal-sub' }, it.sub) : null),
        it.keys ? h('kbd', null, it.keys) : null);
      return head ? [head, row] : [row];
    }));
    if (!items.length) list.append(h('div', { class: 'pal-empty' }, q.length < 2 ? 'Type to search' : 'Searching YouTube Music…'));
  };
  const mark = () => list.querySelectorAll('.pal-item').forEach((el, i) => { el.classList.toggle('sel', i === sel); el.setAttribute('aria-selected', String(i === sel)); if (i === sel) el.scrollIntoView({ block: 'nearest' }); });
  const searchRemote = debounce(async () => {
    const q = input.value.trim().toLowerCase(), my = ++seq;
    if (q.length < 3 || !navigator.onLine) return;
    try {
      const data = await api.search(q, 'all');
      if (my !== seq || !PAL) return;
      const found = [data.top, ...(data.sections || []).flatMap((sec) => sec.items || [])].filter((x) => x && ['song', 'video', 'album', 'artist', 'playlist'].includes(x.type));
      const seen = new Set();
      remote = found.filter((x) => !seen.has(x.id) && seen.add(x.id)).slice(0, 8).map((x) => ({ group: 'YouTube Music', title: x.title, sub: ((kind, st) => (st && st.toLowerCase().startsWith(kind.toLowerCase()) ? st : [kind, st].filter(Boolean).join(' · ')))(({ song: 'Song', video: 'Video', album: 'Album', artist: 'Artist', playlist: 'Playlist' })[x.type], x.subtitle || artistNames(x)), thumb: x.thumb, run: () => (x.type === 'song' || x.type === 'video' ? playItem(x) : openItem(x)), alt: x.type === 'song' ? () => addToQueue([x]) : null }));
      remoteQ = q;
      draw();
    } catch { /* offline or rate-limited: local results are enough */ }
  }, 220);
  function choose(i, alt = false) {
    const it = items[i];
    if (!it) return;
    close();
    try { (alt && it.alt ? it.alt : it.run)(); if (alt && it.alt) toast(`Added "${it.title}" to queue`); } catch (e) { console.warn('command failed', e); }
  }
  function close() {
    if (!PAL) return;
    PAL = null;
    wrap.classList.add('out');
    setTimeout(() => wrap.remove(), 140);
    if (prevFocus?.isConnected) prevFocus.focus();
  }
  input.addEventListener('input', () => { sel = 0; draw(); searchRemote(); });
  input.addEventListener('keydown', (e) => {
    if (e.key === 'ArrowDown') { e.preventDefault(); sel = (sel + 1) % Math.max(1, items.length); mark(); }
    else if (e.key === 'ArrowUp') { e.preventDefault(); sel = (sel - 1 + items.length) % Math.max(1, items.length); mark(); }
    else if (e.key === 'Enter') { e.preventDefault(); choose(sel, e.shiftKey); }
    else if (e.key === 'Escape') { e.preventDefault(); e.stopPropagation(); close(); }
    else if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') { e.preventDefault(); close(); }
  });
  const prevFocus = document.activeElement;
  PAL = { input, close };
  document.body.append(wrap);
  draw();
  if (initial) searchRemote();
  input.focus();
}
function focusButtonClick() { if (FOCUS && !$('#focusView')) return showFocusView(); focusDialog(); }

// ================= first-run notice =================
// Shown once per installation (settings.noticeSeen), after the UI has painted, never blocking.
// Why this exists:
//  • This client plays YouTube Music through a third-party audio pipeline, so YouTube's ads cannot
//    be served in it and creators are not compensated through it. Saying so up front is more
//    honest than implying otherwise.
//  • Recommending the official music.youtube.com with YouTube Premium is a good-faith signal to
//    users and the community. It is NOT a legal shield and must never be described as one.
//  • Calling it a hobby project sets accurate expectations: this is not an official or commercial
//    product.
// Non-goals: no fake ad UI, no ad slots, no "support us via ads" — ads can't be billed from this
// client, so simulating them would be misleading. Not re-shown to returning users or after updates.
async function maybeShowFirstRunNotice() {
  if (!S().welcomed) return showWelcome();
  if (S().noticeSeen) return;
  setSetting('noticeSeen', true); // once per installation, even if the app is closed while it's up
  flushPersist();
  // Paying users don't need the ads line (best effort; unknown → keep it). Never needs the network
  // when signed out, and gives up quickly when offline.
  let premium = false;
  if (await api.authStatus().catch(() => false)) premium = await Promise.race([api.premiumStatus().catch(() => null), new Promise((r) => setTimeout(() => r(null), 2500))]) === true;
  const reduce = matchMedia('(prefers-reduced-motion: reduce)').matches;
  const link = h('a', { href: '#', class: 'fn-link', onclick: (e) => { e.preventDefault(); api.openExternal('https://music.youtube.com'); close(); } }, 'music.youtube.com');
  const openBtn = h('button', { class: 'btn tonal', onclick: () => { api.openExternal('https://music.youtube.com'); close(); } }, 'Open YouTube Music');
  const okBtn = h('button', { class: 'btn filled', onclick: () => close() }, 'Got it');
  const card = h('div', { class: 'fn-card' + (reduce ? ' still' : ''), role: 'dialog', 'aria-modal': 'true', 'aria-labelledby': 'fnTitle', 'aria-describedby': 'fnBody', tabindex: '-1' },
    h('div', { class: 'fn-title', id: 'fnTitle' }, 'A quick note'),
    h('div', { id: 'fnBody' },
      h('p', null, premium ? 'This is an unofficial hobby project.' : 'This is an unofficial hobby project. It can’t show YouTube’s ads, so artists aren’t paid through it.'),
      premium ? null : h('p', null, 'For the official experience — and to support artists — we recommend ', link, ' with YouTube Premium.')),
    h('div', { class: 'fn-actions' }, premium ? null : openBtn, okBtn));
  document.body.append(card);
  let closed = false, timer = null;
  function close() {
    if (closed) return;
    closed = true;
    clearTimeout(timer);
    document.removeEventListener('keydown', onKey, true);
    document.removeEventListener('pointerdown', onPointer, true);
    if (reduce) card.remove(); else { card.classList.add('out'); setTimeout(() => card.remove(), 260); }
  }
  // Esc / any key dismisses — except Tab/Enter/Space while moving through or pressing its own buttons.
  function onKey(e) {
    if (card.contains(document.activeElement) && ['Tab', 'Enter', ' ', 'Shift'].includes(e.key)) return;
    if (e.key === 'Tab' || e.key === 'Shift') return;
    close();
  }
  // any click dismisses (clicks on the card's own controls run their action first)
  function onPointer(e) { if (!card.contains(e.target)) close(); }
  card.addEventListener('click', (e) => { if (!e.target.closest('button, a')) close(); });
  document.addEventListener('keydown', onKey, true);
  document.addEventListener('pointerdown', onPointer, true);
  // fades on its own after a while unless the user is reading/hovering it
  const arm = () => { clearTimeout(timer); timer = setTimeout(close, 9000); };
  card.addEventListener('mouseenter', () => clearTimeout(timer));
  card.addEventListener('mouseleave', arm);
  card.addEventListener('focusin', () => clearTimeout(timer));
  arm();
  requestAnimationFrame(() => okBtn.focus({ preventScroll: true }));
}

// ================= local files =================
const isLocal = (t) => /^lc_[0-9a-f]{16}$/.test(String(t?.id || ''));
const LOCAL = { tracks: null, scanning: null, filter: '' };
async function loadLocal(force = false) {
  if (LOCAL.tracks && !force) return LOCAL.tracks;
  LOCAL.tracks = await api.localTracks();
  return LOCAL.tracks;
}
async function rescanLocal() {
  if (LOCAL.scanning) return;
  LOCAL.scanning = { done: 0, total: 0 };
  updateScanUI();
  try { LOCAL.tracks = await api.localScan(); }
  catch (e) { toast("Couldn't scan: " + shortErr(e.message)); }
  LOCAL.scanning = null;
  const top = nav.stack[nav.pos];
  if (top?.name === 'files') rerender();
}
function updateScanUI() {
  const el = $('#scanStatus');
  if (!el) return;
  const s = LOCAL.scanning;
  el.hidden = !s;
  if (s) el.textContent = s.total ? `Scanning… ${s.done} / ${s.total}` : 'Looking for music…';
}
async function playLocalFiles(tracks, { queue = false } = {}) {
  if (!tracks?.length) return toast('No playable audio files found');
  LOCAL.tracks = null; // index changed
  if (queue) addToQueue(tracks); else playList(tracks, 0, { source: tracks.length === 1 ? 'Local file' : 'Local files' });
}
async function openFilesDialog() {
  try { playLocalFiles(await api.localOpen()); } catch (e) { toast(shortErr(e.message)); }
}
async function addMusicFolder() {
  const f = await api.localAddFolder().catch((e) => { toast(shortErr(e.message)); return null; });
  if (f) { go('files', {}, { replace: nav.stack[nav.pos]?.name === 'files' }); rescanLocal(); }
}

VIEWS.files = async (ctx, { tab = 'songs', album, artist }) => {
  const v = ctx.view;
  const folders = await api.localFolders().catch(() => []);
  const tracks = await loadLocal();
  if (!ctx.alive()) return;
  v.append(h('div', { class: 'page-title' }, 'Local files'));
  const scanEl = h('div', { id: 'scanStatus', class: 'scan-status' });
  const actions = h('div', { class: 'local-actions' },
    playBtn(() => playList(tracks, 0, { source: 'Local files', key: 'files' }), { disabled: !tracks.length, label: 'Play all', key: 'files' }),
    h('button', { class: 'btn tonal', disabled: !tracks.length, onclick: () => playList(tracks, 0, { shuffle: true, source: 'Local files' }) }, icon('shuffle'), 'Shuffle'),
    h('button', { class: 'btn outline', onclick: addMusicFolder }, icon('musicFolder'), 'Add folder'),
    h('button', { class: 'btn outline', onclick: openFilesDialog }, icon('audioFile'), 'Open files'),
    folders.length ? h('button', { class: 'icon-btn', title: 'Rescan folders', onclick: rescanLocal }, icon('sync')) : null,
    scanEl);
  v.append(actions);
  updateScanUI();
  if (folders.length) {
    v.append(h('div', { class: 'folder-chips' }, folders.map((f) => h('span', { class: 'folder-chip', title: f }, icon('folder'), h('span', null, f),
      h('button', { class: 'icon-btn small', title: 'Remove this folder from the library (files are not deleted)', onclick: async () => { await api.localRemoveFolder(f); LOCAL.tracks = null; rerender(); } }, icon('close'))))));
  }
  if (!tracks.length) {
    v.append(emptyState('musicFolder', folders.length ? (LOCAL.scanning ? 'Scanning your music…' : 'No music found') : 'Play music from your computer',
      folders.length ? 'No audio files were found in these folders. MP3, FLAC, M4A/AAC, OGG, Opus, WAV and more are supported.' : 'Add a folder with your music (MP3, FLAC, M4A, OGG, Opus, WAV…), or drag files onto the window to play them right away.',
      h('button', { class: 'btn filled', onclick: addMusicFolder }, icon('musicFolder'), 'Add a music folder')));
    return;
  }
  const tabs = [['songs', 'Songs'], ['albums', 'Albums'], ['artists', 'Artists']];
  v.append(h('div', { class: 'chips lib-tabs' }, tabs.map(([k, l]) => h('button', { class: 'chip' + (k === tab && !album && !artist ? ' active' : ''), onclick: () => go('files', { tab: k }, { replace: true }) }, l)),
    h('input', { class: 'field filter-field', placeholder: 'Filter', value: LOCAL.filter, oninput: debounce((e) => { LOCAL.filter = e.target.value; drawList(); }, 150) })));
  const albumKey = (t) => (t.album?.name || 'Unknown album') + '\u0000' + (t.albumArtist || t.artists[0]?.name || '');
  const listBox = h('div');
  v.append(listBox);
  let shown = 0, list = [];
  const pageSize = 150;
  const renderMore = () => {
    const box = listBox.querySelector('.song-list');
    const slice = list.slice(shown, shown + pageSize);
    slice.forEach((t, j) => box.append(songRow(t, { list, index: shown + j, showAlbum: true, source: 'Local files' })));
    shown += slice.length;
    return shown < list.length;
  };
  function drawList() {
    const q = LOCAL.filter.trim().toLowerCase();
    const match = (t) => !q || [t.title, artistNames(t), t.album?.name, t.genre].some((x) => String(x || '').toLowerCase().includes(q));
    listBox.replaceChildren();
    if (album || artist) {
      list = tracks.filter((t) => (album ? albumKey(t) === album : (t.albumArtist === artist || t.artists.some((a) => a.name === artist))) && match(t));
      const first = list[0];
      listBox.append(h('div', { class: 'section-head' }, h('div', { class: 'st' }, h('h2', null, album ? (first?.album?.name || 'Unknown album') : artist)),
        h('div', { class: 'sh-actions' }, playBtn(() => playList(list, 0, { source: album ? first?.album?.name : artist })),
          h('button', { class: 'btn tonal', onclick: () => playList(list, 0, { shuffle: true, source: album ? first?.album?.name : artist }) }, icon('shuffle'), 'Shuffle'))));
    } else if (tab === 'albums' || tab === 'artists') {
      const groups = new Map();
      for (const t of tracks.filter(match)) {
        const k = tab === 'albums' ? albumKey(t) : (t.albumArtist || t.artists[0]?.name || 'Unknown artist');
        if (!groups.has(k)) groups.set(k, []);
        groups.get(k).push(t);
      }
      const cards = [...groups.entries()].sort((a, b) => a[0].localeCompare(b[0], undefined, { sensitivity: 'base' })).map(([k, ts]) => {
        const t0 = ts[0];
        const title = tab === 'albums' ? (t0.album?.name || 'Unknown album') : k;
        const sub = tab === 'albums' ? (t0.albumArtist || artistNames(t0)) + (t0.year ? ' • ' + t0.year : '') : `${ts.length} song${ts.length > 1 ? 's' : ''}`;
        return h('div', { class: 'card' + (tab === 'artists' ? ' artist' : ''), onclick: () => go('files', tab === 'albums' ? { album: k } : { artist: k }) },
          h('div', { class: 'art' }, img(t0.thumb, tab === 'artists' ? 'round' : ''), h('button', { class: 'play-over', title: 'Play', onclick: (e) => { e.stopPropagation(); playList(ts, 0, { source: title }); } }, icon('play'))),
          h('div', { class: 'ct', title }, h('span', null, title)), h('div', { class: 'cs' }, sub));
      });
      listBox.append(h('div', { class: 'grid' }, cards));
      return;
    } else list = tracks.filter(match);
    listBox.append(h('div', { class: 'local-count' }, `${list.length} song${list.length === 1 ? '' : 's'}${q ? ' match' : ''} • ${totalDuration(list)}`), h('div', { class: 'song-list' }));
    shown = 0;
    if (renderMore()) infinite(ctx, listBox, async () => renderMore());
  }
  drawList();
};

// drag & drop audio files / folders anywhere on the window
function wireDrop() {
  let depth = 0;
  const overlay = h('div', { id: 'dropOverlay', hidden: true }, h('div', null, bigIcon('audioFile', 56), h('div', { class: 'd-t' }, 'Drop to play'), h('div', { class: 'd-s' }, 'Hold Shift to add to the queue instead')));
  document.body.append(overlay);
  const hasFiles = (e) => [...(e.dataTransfer?.types || [])].includes('Files');
  window.addEventListener('dragenter', (e) => { if (!hasFiles(e)) return; depth++; overlay.hidden = false; });
  window.addEventListener('dragleave', (e) => { if (!hasFiles(e)) return; if (--depth <= 0) { depth = 0; overlay.hidden = true; } });
  window.addEventListener('dragover', (e) => { if (hasFiles(e)) e.preventDefault(); });
  window.addEventListener('drop', async (e) => {
    if (!hasFiles(e)) return;
    e.preventDefault();
    depth = 0; overlay.hidden = true;
    const paths = [...e.dataTransfer.files].map((f) => api.pathForFile(f)).filter(Boolean);
    if (!paths.length) return;
    try { playLocalFiles(await api.localOpen(paths), { queue: e.shiftKey && P.queue.length > 0 }); } catch (er) { toast(shortErr(er.message)); }
  });
}

// ================= import (Spotify link / CSV / text) =================
const IMPORT = { running: false };
function importDialog(prefill = '') {
  const input = h('textarea', { class: 'field import-field', rows: 5, placeholder: 'Paste a Spotify playlist, album or song link…\n\nor paste a list of songs, one per line:\nLinkin Park - Numb\nAvicii - Wake Me Up' });
  input.value = prefill;
  const go1 = async () => {
    const text = input.value.trim();
    if (!text) return;
    closeModal();
    const isLink = /spotify\.com\/|spotify:/.test(text);
    runImport(async () => (isLink ? api.importSpotify(text) : api.importParse(text, 'Imported playlist')));
  };
  input.onkeydown = (e) => { if (e.key === 'Enter' && (e.ctrlKey || /^\S+$/.test(input.value.trim()))) { e.preventDefault(); go1(); } if (e.key === 'Escape') closeModal(); };
  modal(h('div', { class: 'dialog import-dialog' },
    h('h3', null, 'Import a playlist'),
    h('p', null, 'Public Spotify playlists, albums and songs import directly from their link. For your Spotify Liked Songs or private playlists, export them to CSV (for example with exportify.app) and import the file.'),
    input,
    h('div', { class: 'd-actions' },
      h('button', { class: 'text-btn', style: { marginRight: 'auto' }, onclick: async () => { closeModal(); runImport(() => api.importFile()); } }, icon('importIcon'), 'CSV / text file…'),
      h('button', { class: 'text-btn', onclick: closeModal }, 'Cancel'),
      h('button', { class: 'text-btn', onclick: go1 }, 'Import'))));
  setTimeout(() => input.focus(), 30);
}
async function runImport(getList) {
  if (IMPORT.running) return toast('An import is already running');
  IMPORT.running = true;
  const bar = h('div', { class: 'imp-bar' }, h('div', { class: 'imp-fill' }));
  const label = h('div', { class: 'imp-label' }, 'Reading playlist…');
  const cancelBtn = h('button', { class: 'text-btn', onclick: () => { IMPORT.cancelled = true; api.importCancel(); } }, 'Cancel');
  IMPORT.cancelled = false;
  modal(h('div', { class: 'dialog' }, h('h3', null, 'Importing'), label, bar, h('div', { class: 'd-actions' }, cancelBtn)));
  $('#modal').onmousedown = null;
  IMPORT.onProgress = (p) => {
    const pct = p.total ? Math.round((p.done / p.total) * 100) : 0;
    bar.firstChild.style.width = pct + '%';
    label.textContent = p.phase === 'spotify' ? `Reading songs from Spotify… ${p.done} / ${p.total}` : `Finding songs on YouTube Music… ${p.done} / ${p.total}` + (p.last ? ` — ${p.last}` : '');
  };
  try {
    const src = await getList();
    if (!src) { closeModal(); return; }
    if (!src.tracks?.length) throw new Error('No songs found in that list.');
    label.textContent = `Finding ${nOf(src.tracks.length, 'song', 'songs')} on YouTube Music…`;
    const results = await api.importMatch(src.tracks);
    if (IMPORT.cancelled) { closeModal(); toast('Import cancelled'); return; }
    importResults(src, results.filter(Boolean));
  } catch (e) {
    closeModal();
    toast("Import failed: " + shortErr(e.message));
  } finally { IMPORT.running = false; IMPORT.onProgress = null; }
}
function importResults(src, results) {
  const found = results.filter((r) => r.match);
  const missing = results.filter((r) => !r.match);
  const keep = new Set(found.filter((r) => !r.unsure || r.score >= 0.6).map((r) => r));
  const name = h('input', { class: 'field', value: src.name || 'Imported playlist' });
  const row = (r) => {
    const cb = h('input', { type: 'checkbox', checked: keep.has(r), onchange: () => (cb.checked ? keep.add(r) : keep.delete(r)) });
    const w = r.want;
    return h('label', { class: 'imp-row' + (r.unsure ? ' unsure' : '') }, cb,
      h('div', { class: 'imp-want' }, h('div', { class: 't' }, w.title), h('div', { class: 's' }, (w.artists || []).join(', '))),
      icon('right'),
      h('div', { class: 'imp-got' }, img(thumbOf(r.match)), h('div', null, h('div', { class: 't' }, r.match.title), h('div', { class: 's' }, artistNames(r.match) + (r.unsure ? ' • check this' : '')))));
  };
  const list = h('div', { class: 'imp-list' },
    found.map(row),
    missing.length ? h('div', { class: 'imp-miss-head' }, `Not found on YouTube Music (${missing.length})`) : null,
    missing.map((r) => h('div', { class: 'imp-row miss' }, icon('close'), h('div', { class: 'imp-want' }, h('div', { class: 't' }, r.want.title), h('div', { class: 's' }, (r.want.artists || []).join(', '))),
      h('button', { class: 'text-btn', onclick: () => { closeModal(); go('search', { q: [r.want.title, r.want.artists?.[0]].filter(Boolean).join(' '), type: 'song' }); } }, 'Search'))));
  const create = () => {
    const tracks = found.filter((r) => keep.has(r)).map((r) => slim(r.match));
    const uniq = [...new Map(tracks.map((t) => [t.id, t])).values()];
    if (!uniq.length) return toast('Nothing selected');
    const pl = { id: 'local_' + uid(), name: name.value.trim() || 'Imported playlist', tracks: uniq, created: Date.now(), importedFrom: src.kind };
    DB.playlists.unshift(pl);
    persist('playlists');
    renderSidebar();
    closeModal();
    toast(`Imported ${uniq.length} song${uniq.length > 1 ? 's' : ''} into "${pl.name}"`);
    go('local', { id: pl.id });
  };
  modal(h('div', { class: 'dialog import-dialog wide' },
    h('h3', null, `Found ${found.length} of ${nOf(results.length, 'song', 'songs')}`),
    src.partial ? h('p', { class: 'warn' }, 'Spotify only returned part of this playlist' + (src.partialReason ? ` (${src.partialReason})` : '') + '. Export it to CSV to import everything.') : null,
    h('div', { class: 'range-row' }, h('label', null, 'Playlist name'), name),
    list,
    h('div', { class: 'd-actions' },
      h('button', { class: 'text-btn', onclick: closeModal }, 'Discard'),
      h('button', { class: 'text-btn', onclick: () => { const ts = found.filter((r) => keep.has(r)).map((r) => r.match); closeModal(); playList(ts, 0, { source: src.name }); } }, 'Just play'),
      h('button', { class: 'btn filled', onclick: create }, icon('add'), 'Create playlist'))));
}

// Safety net: an IPC call that fails from a button should say so instead of silently doing nothing.
window.addEventListener('unhandledrejection', (e) => {
  const m = e.reason?.message || String(e.reason || '');
  console.warn('unhandled', m);
  if (/^Error invoking remote method/.test(m)) toast("Couldn't load: " + shortErr(m));
});
async function init() {
  DB = await api.storeGet();
  INFO = { ...INFO, ...(await api.info().catch(() => ({}))) };
  LIKED = new Set(DB.liked.map((t) => t.id));
  const dls = await api.downloads().catch(() => ({ done: [], active: [] }));
  dls.done.forEach((d) => DL.done.add(d.track.id));
  dls.active.forEach((id) => DL.progress.set(id, 0));
  wire();
  engine.setOptions({ tempo: S().tempo || 1, varispeed: !!S().varispeed, skipSilence: !!S().skipSilence, silenceInstant: !!S().silenceInstant, normalize: S().normalize !== false, normTarget: S().normTarget ?? -14 });
  engine.setCorrection(S().headphone, S().headphoneOn !== false);
  applySound();
  engine.setEq(S().eq);
  engine.setChannel({ mono: !!S().mono, balance: S().balance || 0 });
  if (S().outputDevice && S().outputDevice !== 'default') engine.sinkId = S().outputDevice;
  DB.lyricsOffsets = DB.lyricsOffsets || {};
  DB.eqPresets = DB.eqPresets || [];
  DB.deviceProfiles = DB.deviceProfiles || {};
  applyAppearance();
  refreshDevices(false);
  setVolume(S().volume ?? 0.8, false);
  renderSidebar();
  const s = DB.session;
  if (S().persistentQueue && s?.queue?.length && s.idx >= 0) {
    P.queue = s.queue.map(withQid);
    P.idx = Math.min(s.idx, P.queue.length - 1);
    P.current = P.queue[P.idx];
    P.source = s.source || '';
    P.sourceKey = s.sourceKey || null;
    updateNpFrom();
    if (S().rememberShuffleRepeat) { P.repeat = s.repeat || 'off'; P.shuffle = !!s.shuffle; }
    P.resumeAt = s.pos || 0;
    updateNowPlayingUI(true);
    setProgress(P.resumeAt, P.current.duration || 0, 0);
    updateModeButtons();
    themeFromTrack(P.current);
    lyricsFor(P.current);
  }
  wireDrop();
  if (DB.settings.focusSession) {
    const fs0 = DB.settings.focusSession;
    if (fs0.end && fs0.end < Date.now()) endFocus({ done: false });   // expired while closed → just restore
    else startFocus(fs0.minutes, { pauseAtEnd: fs0.pauseAtEnd, resume: true });
  }
  const pollPower = () => api.power().then((p) => { const was = onBattery; onBattery = !!p.onBattery; if (was && !onBattery) pumpAnalysis(); }).catch(() => {});
  pollPower(); setInterval(pollPower, 60000);
  api.onLocalProgress((p) => { if (LOCAL.scanning) { LOCAL.scanning = p; updateScanUI(); } });
  api.onLocalPlay((tracks) => playLocalFiles(tracks));
  api.onImportProgress((p) => IMPORT.onProgress?.(p));
  go('home');
  // after the first paint, never in the way of cold start
  requestAnimationFrame(() => setTimeout(() => maybeShowFirstRunNotice().catch(() => {}), S().welcomed ? 900 : 250));
  api.localPending().then((t) => t?.length && playLocalFiles(t)).catch(() => {});
  // pick up new/changed files in the music folders (fast: unchanged files are cached)
  setTimeout(() => api.localFolders().then((f) => f.length && rescanLocal()).catch(() => {}), 4000);
}
window.__soncle = { engine, P, OUT, importResults, playList, loadLocal, startFlowRadio, featOf, onDuck, startFocus, endFocus, DB: () => DB, S: () => DB.settings, sim: (label) => { const prev = OUT.info; OUT.info = classifyOutput(label, []); OUT.key = OUT.info.model; updateDeviceButton(); onDeviceChanged(prev, true); } };
init();
