// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// Synced lyrics from LRCLIB for the browser extension. Same lookup as the app's (src/main.mjs
// `lrclib`): exact match first, then a search that prefers a synced result of about the same length.

export const cleanTitle = (s) => (s || '').replace(/\s*[\(\[](official|lyric|audio|video|visualizer|hd|4k|mv|remaster|feat\.?|ft\.)[^\)\]]*[\)\]]/gi, '').trim();
const HEADERS = { 'Lrclib-Client': 'Soncle (https://github.com/minuda2009)' };

// t: { title, artists: [{ name }], album?: { name }, duration? (seconds) }
export async function lookupLyrics(t, fetchImpl = fetch) {
  const artist = (t.artists || []).map((a) => a.name).filter(Boolean)[0] || '';
  const title = cleanTitle(t.title);
  if (!title) return null;
  const q = new URLSearchParams({ track_name: title, artist_name: artist });
  if (t.album?.name) q.set('album_name', t.album.name);
  if (t.duration) q.set('duration', String(Math.round(t.duration)));
  try {
    const r = await fetchImpl('https://lrclib.net/api/get?' + q, { headers: HEADERS });
    if (r.ok) {
      const j = await r.json();
      if (j.syncedLyrics || j.plainLyrics) return { synced: j.syncedLyrics || null, plain: j.plainLyrics || null, source: 'LRCLIB' };
    }
  } catch { /* fall through to the search */ }
  try {
    const r = await fetchImpl('https://lrclib.net/api/search?' + new URLSearchParams({ track_name: title, artist_name: artist }), { headers: HEADERS });
    if (r.ok) {
      const arr = await r.json();
      const best = arr.find((x) => x.syncedLyrics && (!t.duration || Math.abs(x.duration - t.duration) < 5)) || arr.find((x) => x.syncedLyrics) || arr[0];
      if (best) return { synced: best.syncedLyrics || null, plain: best.plainLyrics || null, source: 'LRCLIB' };
    }
  } catch { /* none */ }
  return null;
}
