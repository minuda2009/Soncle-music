// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// The lyrics engine shared by the desktop and phone apps: finds every LRCLIB version of a song, scores
// them against what is playing (title, artist, album, length, live/remix tags, whether the timing fits the
// song), picks the best, and returns the others so the person can choose. Pure apart from `fetchImpl`.
import { parseLrc } from '../renderer/lrc.js';

const UA = { 'User-Agent': 'Soncle (https://github.com/minuda2009)', 'Lrclib-Client': 'Soncle (https://github.com/minuda2009)' };
export const cleanTitle = (s) => (s || '').replace(/\s*[\(\[](official|lyric|audio|video|visualizer|hd|4k|mv|remaster|feat\.?|ft\.)[^\)\]]*[\)\]]/gi, '').trim();

// "Café (Live) – Remastered" -> "cafe" ; used for comparing, never shown
export const norm = (s) => (s || '').normalize('NFKD').replace(/[̀-ͯ]/g, '').toLowerCase().replace(/[\(\[][^\)\]]*[\)\]]/g, ' ').replace(/&/g, ' and ').replace(/[^\p{L}\p{N}]+/gu, ' ').trim();
const toks = (s) => norm(s).split(' ').filter(Boolean);

// What kind of version a title says it is. A live recording and the studio song have different timing.
const FLAGS = ['live', 'remix', 'acoustic', 'instrumental', 'karaoke', 'demo', 'radio edit', 'sped up', 'slowed', 'cover'];
export const flagsOf = (s) => { const l = (s || '').toLowerCase(); return FLAGS.filter((f) => new RegExp(`(^|[^a-z])${f}([^a-z]|$)`).test(l)); };

export function titleSimilarity(a, b) {
  const x = toks(a), y = toks(b);
  if (!x.length || !y.length) return 0;
  if (x.join(' ') === y.join(' ')) return 1;
  const sy = new Set(y);
  const common = x.filter((w) => sy.has(w)).length;
  const dice = (2 * common) / (x.length + y.length);
  const contained = common / Math.min(x.length, y.length);
  return Math.max(dice, contained * 0.85);
}

const artistsOf = (t) => (t.artists || []).map((a) => norm(a.name)).filter(Boolean);
const lastLine = (synced) => { try { const l = parseLrc(synced); return l.length ? l[l.length - 1].t : 0; } catch { return 0; } };

// Higher is better; about 100 is a perfect match. `c` is an LRCLIB record.
export function scoreCandidate(t, c) {
  let s = 0;
  s += titleSimilarity(cleanTitle(t.title), c.trackName || c.name) * 40;
  const mine = artistsOf(t), theirs = norm(c.artistName);
  if (mine.length && theirs) s += mine.some((a) => a === theirs || theirs.includes(a) || a.includes(theirs)) ? 25 : -10;
  if (t.album?.name && c.albumName) s += norm(t.album.name) === norm(c.albumName) ? 8 : 0;
  if (t.duration && c.duration) {
    const d = Math.abs(t.duration - c.duration);
    s += d <= 1 ? 20 : d <= 3 ? 15 : d <= 6 ? 8 : d <= 12 ? 2 : d <= 20 ? -15 : -30;
  }
  const want = flagsOf(t.title), got = flagsOf(c.trackName || c.name);
  s -= got.filter((f) => !want.includes(f)).length * 20 + want.filter((f) => !got.includes(f)).length * 10;
  if (c.syncedLyrics) {
    s += 10;
    if (t.duration && lastLine(c.syncedLyrics) > t.duration + 2) s -= 25;   // the timing runs past the end of this song: another version
  } else if (!c.plainLyrics) s -= 30;
  if (c.instrumental) s -= 50;
  return Math.round(s * 10) / 10;
}

export function rankCandidates(t, list) {
  const seen = new Map();
  for (const c of list || []) if (c && (c.id ?? c.trackName) !== undefined && !seen.has(c.id ?? c.trackName + c.artistName + c.duration)) seen.set(c.id ?? c.trackName + c.artistName + c.duration, c);
  return [...seen.values()].map((c) => ({ c, score: scoreCandidate(t, c) }))
    .sort((a, b) => b.score - a.score || Number(!!b.c.syncedLyrics) - Number(!!a.c.syncedLyrics) || Math.abs((t.duration || 0) - (a.c.duration || 0)) - Math.abs((t.duration || 0) - (b.c.duration || 0)));
}

// Sure enough to show without a hint: a clearly good best score and a clear gap to the next version.
export const isConfident = (ranked) => !!ranked.length && ranked[0].score >= 70 && (ranked.length < 2 || ranked[0].score - ranked[1].score >= 8);

export const variantOf = ({ c, score }) => ({ id: c.id, title: c.trackName, artist: c.artistName, album: c.albumName || '', duration: Math.round(c.duration || 0), synced: !!c.syncedLyrics, score });

// Things wrong with a synced version for this song, so the app can say so.
export function syncIssues(synced, duration) {
  if (!synced) return [];
  const end = lastLine(synced);
  const out = [];
  if (duration && end > duration + 2) out.push('runs-past-end');
  if (duration > 90 && end && end < duration * 0.35) out.push('ends-early');
  return out;
}

async function getJson(u, fetchImpl, wait) {
  for (let n = 0; ; n++) {
    try {
      const r = await fetchImpl(u, { headers: UA });
      if (r.ok) return await r.json();
      if (!(r.status === 429 || r.status >= 500)) return null;
    } catch { /* network: retry */ }
    if (n >= 2) return null;
    await wait(400 * 3 ** n);
  }
}
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const API = 'https://lrclib.net/api/';

export async function findCandidates(t, { fetchImpl = fetch, wait = sleep } = {}) {
  const artist = (t.artists || []).map((a) => a.name).filter(Boolean)[0] || '';
  const title = cleanTitle(t.title);
  if (!title) return [];
  const exact = new URLSearchParams({ track_name: title, artist_name: artist });
  if (t.album?.name) exact.set('album_name', t.album.name);
  if (t.duration) exact.set('duration', String(Math.round(t.duration)));
  const [one, byFields, byText] = await Promise.all([
    getJson(API + 'get?' + exact, fetchImpl, wait),
    getJson(API + 'search?' + new URLSearchParams({ track_name: title, artist_name: artist }), fetchImpl, wait),
    getJson(API + 'search?' + new URLSearchParams({ q: `${title} ${artist}`.trim() }), fetchImpl, wait)
  ]);
  return [one && typeof one === 'object' && !Array.isArray(one) ? one : null, ...(Array.isArray(byFields) ? byFields : []), ...(Array.isArray(byText) ? byText : [])].filter(Boolean);
}

// t: { title, artists, album?, duration? }. opts.preferId: the version the person chose for this song.
// Returns { synced, plain, source, id, score, confident, chosen, issues, variants } or null.
export async function getLyrics(t, { fetchImpl = fetch, wait = sleep, preferId } = {}) {
  let ranked = rankCandidates(t, await findCandidates(t, { fetchImpl, wait }));
  let pick = ranked[0];
  let chosen = false;
  if (preferId !== undefined && preferId !== null) {
    let c = ranked.find((x) => String(x.c.id) === String(preferId))?.c;
    if (!c) { const j = await getJson(API + 'get/' + encodeURIComponent(preferId), fetchImpl, wait); if (j && typeof j === 'object' && !Array.isArray(j)) c = j; }
    if (c) { pick = { c, score: scoreCandidate(t, c) }; chosen = true; if (!ranked.some((x) => x.c.id === c.id)) ranked = [pick, ...ranked]; }
  }
  if (!pick || pick.c.instrumental && !pick.c.plainLyrics && !pick.c.syncedLyrics) return null;
  const c = pick.c;
  if (!c.syncedLyrics && !c.plainLyrics) return null;
  // prefer a synced version of about the same score over a plain one the ranking put first
  return {
    synced: c.syncedLyrics || null, plain: c.plainLyrics || null, source: 'LRCLIB', id: c.id, score: pick.score,
    confident: chosen || isConfident(ranked), chosen, issues: syncIssues(c.syncedLyrics, t.duration),
    variants: ranked.slice(0, 8).map(variantOf)
  };
}
