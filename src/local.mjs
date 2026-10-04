// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// Local music library: folders on disk, scanned with music-metadata, played through mstream://lc_<id>.
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
// music-metadata is loaded on first use, not at app start
let mm = null;
const MM = async () => (mm ||= await import('music-metadata'));

export const AUDIO_EXT = new Set(['.mp3', '.m4a', '.aac', '.flac', '.ogg', '.oga', '.opus', '.wav', '.wma', '.webm', '.alac', '.aiff', '.aif', '.ape', '.wv', '.mka']);
export const MIME = {
  '.mp3': 'audio/mpeg', '.m4a': 'audio/mp4', '.aac': 'audio/aac', '.alac': 'audio/mp4', '.flac': 'audio/flac', '.ogg': 'audio/ogg', '.oga': 'audio/ogg',
  '.opus': 'audio/ogg', '.wav': 'audio/wav', '.webm': 'audio/webm', '.mka': 'audio/x-matroska', '.wma': 'audio/x-ms-wma', '.aiff': 'audio/aiff', '.aif': 'audio/aiff',
  '.ape': 'audio/ape', '.wv': 'audio/wavpack'
};
const COVER_NAMES = ['cover', 'folder', 'front', 'album', 'albumart', 'albumartsmall'];

export const isLocalId = (id) => typeof id === 'string' && /^lc_[0-9a-f]{16}$/.test(id);
export const idFor = (file) => 'lc_' + crypto.createHash('sha1').update(path.resolve(file).toLowerCase()).digest('hex').slice(0, 16);
export const isAudioFile = (f) => AUDIO_EXT.has(path.extname(f).toLowerCase());

let dbPath = null;
let db = { folders: [], files: {} };   // files: { [absPath]: { mtime, size, track } }
const byId = new Map();                 // id -> absPath
let log = () => {};
let saveTimer = null;

export function init(dataDir, logger) {
  dbPath = path.join(dataDir, 'local-library.json');
  if (logger) log = logger;
  try { db = { folders: [], files: {}, ...JSON.parse(fs.readFileSync(dbPath, 'utf8')) }; } catch {}
  for (const [p, e] of Object.entries(db.files)) if (e?.track) byId.set(e.track.id, p);
}
function save(now = false) {
  clearTimeout(saveTimer);
  const write = () => { try { fs.writeFileSync(dbPath + '.tmp', JSON.stringify(db)); fs.renameSync(dbPath + '.tmp', dbPath); } catch (e) { log('save failed ' + e.message); } };
  if (now) write(); else saveTimer = setTimeout(write, 500);
}
export const flush = () => save(true);

export const folders = () => [...db.folders];
export function addFolder(dir) {
  dir = path.resolve(dir);
  if (!db.folders.some((f) => f.toLowerCase() === dir.toLowerCase())) db.folders.push(dir);
  save();
  return folders();
}
export function removeFolder(dir) {
  const low = path.resolve(dir).toLowerCase();
  db.folders = db.folders.filter((f) => f.toLowerCase() !== low);
  const prefix = low.endsWith(path.sep) ? low : low + path.sep;
  for (const p of Object.keys(db.files)) {
    if (p.toLowerCase().startsWith(prefix) && !db.files[p].loose) { byId.delete(db.files[p].track?.id); delete db.files[p]; }
  }
  save();
  return folders();
}

export function pathFor(id) {
  const p = byId.get(id);
  return p && fs.existsSync(p) ? p : null;
}
export function trackFor(id) { const p = byId.get(id); return p ? db.files[p]?.track || null : null; }

// ---------- metadata ----------
const splitArtists = (s) => String(s || '').split(/\s*(?:;|\/|\u0000| feat\.? | ft\.? | & |, )\s*/i).map((x) => x.trim()).filter(Boolean);
function cleanName(file) {
  return path.basename(file, path.extname(file)).replace(/^\d{1,3}[\s.\-_]+/, '').replace(/_/g, ' ').trim();
}
async function readTrack(file, st) {
  const id = idFor(file);
  let common = {}, format = {};
  try {
    const md = await (await MM()).parseFile(file, { duration: true, skipCovers: true });
    common = md.common || {}; format = md.format || {};
  } catch (e) { log(`metadata failed ${path.basename(file)}: ${e.message}`); }
  let title = common.title;
  let artists = common.artists?.length ? common.artists.flatMap(splitArtists) : splitArtists(common.artist);
  if (!title) {
    // "Artist - Title.mp3" file names
    const base = cleanName(file);
    const m = /^(.+?)\s+-\s+(.+)$/.exec(base);
    if (m && !artists.length) { artists = splitArtists(m[1]); title = m[2]; } else title = base;
  }
  const rg = common.replaygain_track_gain?.dB ?? common.replaygain_album_gain?.dB;
  return {
    type: 'song', id, local: true,
    title,
    artists: [...new Set(artists)].map((name) => ({ name })),
    album: common.album ? { name: common.album } : null,
    albumArtist: common.albumartist || '',
    trackNo: common.track?.no || 0,
    discNo: common.disk?.no || 0,
    year: common.year || 0,
    genre: common.genre?.[0] || '',
    duration: Math.round(format.duration || 0),
    thumb: `mstream://${id}/art?v=${Math.round(st.mtimeMs)}`,
    loudnessDb: typeof rg === 'number' ? -rg : null,
    codec: [format.codec || format.container, format.bitrate ? Math.round(format.bitrate / 1000) + ' kbps' : '', format.lossless ? 'lossless' : ''].filter(Boolean).join(' • '),
    added: Math.round(st.birthtimeMs || st.mtimeMs),
    file: file
  };
}

async function indexFile(file, { loose = false } = {}) {
  let st;
  try { st = fs.statSync(file); } catch { return null; }
  const prev = db.files[file];
  if (prev?.track && prev.mtime === st.mtimeMs && prev.size === st.size) { if (loose) prev.loose = prev.loose || loose; return prev.track; }
  const track = await readTrack(file, st);
  db.files[file] = { mtime: st.mtimeMs, size: st.size, track, loose: loose || prev?.loose || false };
  byId.set(track.id, file);
  return track;
}

function walk(dir, out, depth = 0) {
  if (depth > 12) return;
  let ents;
  try { ents = fs.readdirSync(dir, { withFileTypes: true }); } catch { return; }
  for (const e of ents) {
    if (e.name.startsWith('.') || e.name === '$RECYCLE.BIN' || e.name === 'System Volume Information') continue;
    const p = path.join(dir, e.name);
    if (e.isDirectory()) walk(p, out, depth + 1);
    else if (e.isFile() && isAudioFile(e.name)) out.push(p);
  }
}

let scanning = null;
export function scan(onProgress = () => {}) {
  if (scanning) return scanning;
  scanning = (async () => {
    const found = [];
    for (const f of db.folders) walk(f, found);
    const seen = new Set(found);
    // drop files that vanished (keep loose files that still exist)
    for (const p of Object.keys(db.files)) {
      if (!seen.has(p) && !(db.files[p].loose && fs.existsSync(p))) { byId.delete(db.files[p].track?.id); delete db.files[p]; }
    }
    let done = 0, lastEmit = 0;
    const queue = [...found];
    const worker = async () => {
      while (queue.length) {
        const f = queue.shift();
        await indexFile(f);
        done++;
        if (Date.now() - lastEmit > 250) { lastEmit = Date.now(); onProgress({ done, total: found.length }); }
      }
    };
    await Promise.all(Array.from({ length: 6 }, worker));
    onProgress({ done, total: found.length, finished: true });
    save();
    return tracks();
  })().finally(() => { scanning = null; });
  return scanning;
}

export function tracks() {
  return Object.values(db.files).map((e) => e.track).filter(Boolean)
    .sort((a, b) => (a.albumArtist || a.artists[0]?.name || '').localeCompare(b.albumArtist || b.artists[0]?.name || '', undefined, { sensitivity: 'base' })
      || (a.album?.name || '').localeCompare(b.album?.name || '', undefined, { sensitivity: 'base' })
      || a.discNo - b.discNo || a.trackNo - b.trackNo || a.title.localeCompare(b.title));
}

export async function openFiles(paths) {
  const out = [];
  for (const p of paths) {
    let st;
    try { st = fs.statSync(p); } catch { continue; }
    if (st.isDirectory()) {
      const found = [];
      walk(p, found);
      for (const f of found.sort()) { const t = await indexFile(f, { loose: true }); if (t) out.push(t); }
    } else if (isAudioFile(p)) {
      const t = await indexFile(path.resolve(p), { loose: true });
      if (t) out.push(t);
    }
  }
  save();
  return out;
}

// ---------- artwork ----------
const artCache = new Map();
export async function artwork(id) {
  if (artCache.has(id)) return artCache.get(id);
  const file = pathFor(id);
  let out = null;
  if (file) {
    try {
      const { parseFile, selectCover } = await MM();
      const md = await parseFile(file, { skipPostHeaders: true });
      const pic = selectCover(md.common.picture);
      if (pic) out = { data: Buffer.from(pic.data), mime: pic.format?.includes('/') ? pic.format : 'image/' + (pic.format || 'jpeg') };
    } catch {}
    if (!out) {
      const dir = path.dirname(file);
      try {
        const imgs = fs.readdirSync(dir).filter((n) => /\.(jpe?g|png|webp)$/i.test(n));
        const pick = imgs.find((n) => COVER_NAMES.includes(path.basename(n, path.extname(n)).toLowerCase())) || (imgs.length === 1 ? imgs[0] : null);
        if (pick) out = { data: fs.readFileSync(path.join(dir, pick)), mime: /png$/i.test(pick) ? 'image/png' : /webp$/i.test(pick) ? 'image/webp' : 'image/jpeg' };
      } catch {}
    }
  }
  if (artCache.size > 48) artCache.delete(artCache.keys().next().value);
  artCache.set(id, out);
  return out;
}

export function placeholderArt(id) {
  let hsh = 0; for (const ch of String(id)) hsh = (hsh * 31 + ch.charCodeAt(0)) >>> 0;
  const hue = hsh % 360, hue2 = (hue + 40) % 360;
  return `<svg xmlns="http://www.w3.org/2000/svg" width="300" height="300" viewBox="0 0 300 300"><defs><linearGradient id="g" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="hsl(${hue},45%,42%)"/><stop offset="1" stop-color="hsl(${hue2},50%,22%)"/></linearGradient></defs><rect width="300" height="300" fill="url(#g)"/><path fill="rgba(255,255,255,.8)" d="M175 80v96a30 30 0 1 1-16-26.5V104l-56 14v76a30 30 0 1 1-16-26.5V96z" transform="translate(20 10)"/></svg>`;
}

// ---------- lyrics next to the file / embedded ----------
export async function lyrics(id) {
  const file = pathFor(id);
  if (!file) return null;
  const base = file.slice(0, -path.extname(file).length);
  for (const ext of ['.lrc', '.LRC', '.txt']) {
    try {
      const txt = fs.readFileSync(base + ext, 'utf8');
      if (txt.trim()) return /\[\d{1,2}:\d{2}/.test(txt) ? { synced: txt, plain: null, source: 'Local .lrc file' } : { synced: null, plain: txt, source: 'Local lyrics file' };
    } catch {}
  }
  try {
    const md = await (await MM()).parseFile(file, { skipCovers: true });
    const l = md.common.lyrics?.[0];
    if (l) {
      if (l.syncText?.length) return { synced: l.syncText.map((x) => { const t = (x.timestamp || 0) / 1000; return `[${String(Math.floor(t / 60)).padStart(2, '0')}:${(t % 60).toFixed(2).padStart(5, '0')}]${x.text}`; }).join('\n'), plain: null, source: 'Embedded lyrics' };
      const text = l.text || (typeof l === 'string' ? l : '');
      if (text) return /\[\d{1,2}:\d{2}/.test(text) ? { synced: text, plain: null, source: 'Embedded lyrics' } : { synced: null, plain: text, source: 'Embedded lyrics' };
    }
  } catch {}
  return null;
}
