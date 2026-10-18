// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// Synced lyrics from LRCLIB for the browser extension. Same lookup as the app's (src/main.mjs
// `lrclib`): exact match first, then a search that prefers a synced result of about the same length.

export const cleanTitle = (s) => (s || '').replace(/\s*[\(\[](official|lyric|audio|video|visualizer|hd|4k|mv|remaster|feat\.?|ft\.)[^\)\]]*[\)\]]/gi, '').trim();
const HEADERS = { 'Lrclib-Client': 'Soncle (https://github.com/minuda2009)' };

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
// One request, tried again (0.4 s, then 1.2 s) when the network fails or LRCLIB answers 5xx/429.
// A 404 or any other answer is final. `wait` is injectable so tests do not really wait.
export async function getJson(u, fetchImpl = fetch, wait = sleep, state = {}) {
  for (let n = 0; ; n++) {
    try {
      const r = await fetchImpl(u, { headers: HEADERS });
      if (r.ok) return await r.json();
      if (!(r.status === 429 || r.status >= 500)) return null;
    } catch { /* network error: try again */ }
    if (n >= 2) { state.failed = true; return null; }
    await wait(400 * 3 ** n);
  }
}

// t: { title, artists: [{ name }], album?: { name }, duration? (seconds) }
export async function lookupLyrics(t, fetchImpl = fetch, wait = sleep, state = {}) {
  const artist = (t.artists || []).map((a) => a.name).filter(Boolean)[0] || '';
  const title = cleanTitle(t.title);
  if (!title) return null;
  const q = new URLSearchParams({ track_name: title, artist_name: artist });
  if (t.album?.name) q.set('album_name', t.album.name);
  if (t.duration) q.set('duration', String(Math.round(t.duration)));
  const j = await getJson('https://lrclib.net/api/get?' + q, fetchImpl, wait, state);
  if (j && (j.syncedLyrics || j.plainLyrics)) return { synced: j.syncedLyrics || null, plain: j.plainLyrics || null, source: 'LRCLIB' };
  const arr = await getJson('https://lrclib.net/api/search?' + new URLSearchParams({ track_name: title, artist_name: artist }), fetchImpl, wait, state);
  if (Array.isArray(arr) && arr.length) {
    const best = arr.find((x) => x.syncedLyrics && (!t.duration || Math.abs(x.duration - t.duration) < 5)) || arr.find((x) => x.syncedLyrics) || arr[0];
    return { synced: best.syncedLyrics || null, plain: best.plainLyrics || null, source: 'LRCLIB' };
  }
  return null;
}

// ---- cache: lyrics found are kept 30 days, "none found" 1 day (so a later upload is noticed), at most 300 entries ----
export const TTL = { hit: 30 * 864e5, miss: 864e5 };
export const MAX_ENTRIES = 300;
export const cacheKey = (t) => [cleanTitle(t.title), (t.artists || [])[0]?.name || '', Math.round((t.duration || 0) / 5) * 5].join('|').toLowerCase();

// store: { get(): Promise<object>, set(object): Promise<void> } (chrome.storage.local in the extension)
export async function cachedLookup(t, store, { fetchImpl = fetch, wait = sleep, now = Date.now } = {}) {
  const key = cacheKey(t);
  let all = {};
  try { all = (await store.get()) || {}; } catch { /* no cache */ }
  const e = all[key];
  if (e && now() - e.at < (e.v ? TTL.hit : TTL.miss)) return { ...(e.v || {}), found: !!e.v, cached: true };
  const state = {};
  const v = await lookupLyrics(t, fetchImpl, wait, state);
  if (!v && state.failed) return { found: false, cached: false, failed: true };   // offline or LRCLIB down: do not remember "none"
  all[key] = { at: now(), v };
  const keys = Object.keys(all);
  if (keys.length > MAX_ENTRIES) for (const k of keys.sort((a, b) => all[a].at - all[b].at).slice(0, keys.length - MAX_ENTRIES)) delete all[k];
  try { await store.set(all); } catch { /* storage full or gone */ }
  return v ? { ...v, found: true, cached: false } : { found: false, cached: false };
}
