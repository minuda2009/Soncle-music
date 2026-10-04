// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// Import from Spotify: public playlist / album / track links (no login), or a CSV export
// (Exportify, Spotify "Liked songs" exports, TuneMyMusic...) or plain "Artist - Title" text.
// Each track is then matched against YouTube Music.

const B62 = '0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ';
export const toGid = (id) => { let n = 0n; for (const c of id) n = n * 62n + BigInt(B62.indexOf(c)); return n.toString(16).padStart(32, '0'); };

export function parseSpotifyLink(text) {
  const s = String(text || '').trim();
  let m = /open\.spotify\.com\/(?:intl-[a-z-]+\/)?(?:embed\/)?(playlist|album|track)\/([A-Za-z0-9]{22})/i.exec(s);
  if (!m) m = /spotify:(playlist|album|track):([A-Za-z0-9]{22})/i.exec(s);
  return m ? { type: m[1].toLowerCase(), id: m[2] } : null;
}

async function embedState(type, id, fetchImpl) {
  const r = await fetchImpl(`https://open.spotify.com/embed/${type}/${id}`, { headers: { 'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36', 'Accept-Language': 'en' } });
  if (r.status === 404) throw new Error('Spotify says this link does not exist (or it is private).');
  if (!r.ok) throw new Error(`Spotify returned HTTP ${r.status}`);
  const html = await r.text();
  const m = /<script id="__NEXT_DATA__"[^>]*>([\s\S]*?)<\/script>/.exec(html);
  if (!m) throw new Error('Could not read the Spotify page (layout changed?)');
  const state = JSON.parse(m[1])?.props?.pageProps?.state;
  if (!state?.data?.entity) throw new Error('This Spotify item is private or unavailable.');
  return state;
}

const fromEmbedTrack = (t) => ({
  title: t.title || t.name || '',
  artists: t.artists?.length ? t.artists.map((a) => a.name) : String(t.subtitle || '').split(/,\s*/).filter(Boolean),
  durationMs: t.duration || 0,
  uri: t.uri || ''
});

/** Fetch a public Spotify playlist/album/track. Returns { name, cover, kind, tracks:[{title, artists[], album?, durationMs}] } */
export async function fetchSpotify(link, fetchImpl, onProgress = () => {}) {
  const ref = typeof link === 'string' ? parseSpotifyLink(link) : link;
  if (!ref) throw new Error('That is not a Spotify playlist, album or track link.');
  const state = await embedState(ref.type, ref.id, fetchImpl);
  const e = state.data.entity;
  const cover = e.coverArt?.sources?.sort((a, b) => (b.width || 0) - (a.width || 0))[0]?.url || e.visualIdentity?.image?.[0]?.url || '';
  const out = { kind: ref.type, id: ref.id, name: e.name || e.title || 'Spotify import', cover, owner: e.subtitle || '', tracks: [] };
  if (ref.type === 'track') { out.tracks = [fromEmbedTrack(e)]; return out; }
  out.tracks = (e.trackList || []).map(fromEmbedTrack);
  if (ref.type === 'album') out.tracks.forEach((t) => (t.album = out.name));
  // The embed page lists at most 100 tracks. Page through the rest with the same anonymous token.
  const token = state.settings?.session?.accessToken;
  if (ref.type === 'playlist' && token && out.tracks.length >= 100) {
    const H = { authorization: 'Bearer ' + token, accept: 'application/json' };
    try {
      const uris = [];
      let from = out.tracks.length, total = Infinity;
      while (from < total && from < 10000) {
        const r = await fetchImpl(`https://spclient.wg.spotify.com/playlist/v2/playlist/${ref.id}?from=${from}&length=100`, { headers: H });
        if (!r.ok) throw new Error('playlist page HTTP ' + r.status);
        const j = await r.json();
        total = j.length ?? 0;
        const items = j.contents?.items || [];
        if (!items.length) break;
        uris.push(...items.map((x) => x.uri).filter((u) => /^spotify:track:/.test(u)));
        from += items.length;
      }
      onProgress({ phase: 'spotify', done: 0, total: uris.length });
      let done = 0;
      const rest = new Array(uris.length);
      const queue = uris.map((u, i) => [u, i]);
      const worker = async () => {
        while (queue.length) {
          const [u, i] = queue.shift();
          try {
            const r = await fetchImpl(`https://spclient.wg.spotify.com/metadata/4/track/${toGid(u.split(':')[2])}?market=from_token`, { headers: H });
            if (r.ok) {
              const md = await r.json();
              rest[i] = { title: md.name, artists: (md.artist || []).map((a) => a.name), album: md.album?.name || '', durationMs: md.duration || 0, uri: u };
            }
          } catch {}
          done++;
          if (done % 10 === 0) onProgress({ phase: 'spotify', done, total: uris.length });
        }
      };
      await Promise.all(Array.from({ length: 6 }, worker));
      out.tracks.push(...rest.filter(Boolean));
      out.partial = rest.some((x) => !x) || undefined;
    } catch (err) {
      out.partial = true;
      out.partialReason = err.message;
    }
  }
  return out;
}

// ---------- CSV / text ----------
export function parseCsv(text) {
  const rows = [];
  let row = [], cell = '', q = false;
  text = String(text).replace(/^﻿/, '');
  for (let i = 0; i < text.length; i++) {
    const c = text[i];
    if (q) {
      if (c === '"') { if (text[i + 1] === '"') { cell += '"'; i++; } else q = false; }
      else cell += c;
    } else if (c === '"') q = true;
    else if (c === ',' || c === ';' && !text.slice(0, 2000).includes(',')) { row.push(cell); cell = ''; }
    else if (c === '\n' || c === '\r') { if (c === '\r' && text[i + 1] === '\n') i++; row.push(cell); rows.push(row); row = []; cell = ''; }
    else cell += c;
  }
  if (cell || row.length) { row.push(cell); rows.push(row); }
  return rows.filter((r) => r.some((x) => x.trim()));
}

/** Tracks from a CSV export or "Artist - Title" lines. */
export function parseTrackList(text, name = 'Imported playlist') {
  const rows = parseCsv(text);
  const head = (rows[0] || []).map((x) => x.trim().toLowerCase());
  const col = (...names) => head.findIndex((h) => names.includes(h));
  const ti = col('track name', 'track', 'title', 'name', 'song', 'song name', 'track title');
  const ai = col('artist name(s)', 'artist names', 'artist name', 'artist', 'artists', 'artist(s)');
  if (rows.length > 1 && ti >= 0) {
    const al = col('album name', 'album', 'album title');
    const di = col('duration (ms)', 'track duration (ms)', 'duration_ms', 'duration');
    const tracks = rows.slice(1).map((r) => {
      let d = di >= 0 ? Number(String(r[di]).trim()) : 0;
      if (di >= 0 && /:/.test(r[di] || '')) { const p = r[di].split(':').map(Number); d = (p.length === 3 ? p[0] * 3600 + p[1] * 60 + p[2] : p[0] * 60 + p[1]) * 1000; }
      else if (d && d < 20000) d *= 1000; // seconds
      return {
        title: (r[ti] || '').trim(),
        artists: ai >= 0 ? String(r[ai] || '').split(/\s*[,;]\s*/).map((x) => x.replace(/\\,/g, ',').trim()).filter(Boolean) : [],
        album: al >= 0 ? (r[al] || '').trim() : '',
        durationMs: d || 0
      };
    }).filter((t) => t.title);
    return { kind: 'csv', name, cover: '', tracks };
  }
  // plain text: one song per line, "Artist - Title" or "Title"
  const tracks = String(text).split(/\r?\n/).map((l) => l.replace(/^\s*\d+[.)]\s*/, '').trim()).filter(Boolean).map((l) => {
    const m = /^(.+?)\s+[-–—]\s+(.+)$/.exec(l);
    return m ? { title: m[2].trim(), artists: [m[1].trim()], durationMs: 0 } : { title: l, artists: [], durationMs: 0 };
  });
  return { kind: 'text', name, cover: '', tracks };
}

// ---------- matching on YouTube Music ----------
export const norm = (s) => String(s || '').normalize('NFKD').replace(/[̀-ͯ]/g, '').toLowerCase()
  .replace(/\s*[([].*?(feat|ft\.|with|remaster|version|edit|mix|live|mono|stereo|deluxe|bonus|explicit|clean|from|prod).*?[)\]]/g, ' ')
  .replace(/\s+-\s+(.*remaster.*|.*version.*|.*edit.*|live.*|mono|stereo)$/g, ' ')
  .replace(/&/g, ' and ').replace(/[^\p{L}\p{N}]+/gu, ' ').trim();
const tokens = (s) => new Set(norm(s).split(' ').filter(Boolean));
function sim(a, b) {
  const A = tokens(a), B = tokens(b);
  if (!A.size || !B.size) return 0;
  let inter = 0; for (const x of A) if (B.has(x)) inter++;
  return inter / Math.max(A.size, B.size) * 0.6 + inter / Math.min(A.size, B.size) * 0.4;
}
export function scoreCandidate(want, c) {
  const titleS = norm(want.title) === norm(c.title) ? 1 : sim(want.title, c.title);
  const wa = (want.artists || []).map(norm).filter(Boolean);
  const ca = (c.artists || []).map((a) => norm(a.name || a)).filter(Boolean);
  let artistS = wa.length ? 0 : 0.5;
  for (const x of wa) for (const y of ca) if (x === y || (x.length > 3 && (x.includes(y) || y.includes(x)))) artistS = 1;
  if (artistS < 1 && wa.length && ca.length) artistS = Math.max(...wa.map((x) => Math.max(...ca.map((y) => sim(x, y))))) * 0.8;
  let durS = 0.5;
  if (want.durationMs && c.duration) { const d = Math.abs(want.durationMs / 1000 - c.duration); durS = d <= 3 ? 1 : d <= 10 ? 0.7 : d <= 30 ? 0.3 : 0; }
  return titleS * 0.55 + artistS * 0.3 + durS * 0.15 - (c.isVideo ? 0.04 : 0);
}

/**
 * @param {Array} tracks        wanted tracks
 * @param {Function} searchSongs async (query) => candidate songs [{id,title,artists,duration,isVideo}]
 */
export async function matchAll(tracks, searchSongs, { onProgress = () => {}, concurrency = 4, isCancelled = () => false } = {}) {
  const results = new Array(tracks.length);
  let done = 0;
  const queue = tracks.map((t, i) => [t, i]);
  const worker = async () => {
    while (queue.length && !isCancelled()) {
      const [t, i] = queue.shift();
      let best = null, bestScore = 0;
      const q = [t.title, (t.artists || [])[0]].filter(Boolean).join(' ');
      for (const query of [q, t.title]) {
        let cands = [];
        try { cands = await searchSongs(query); } catch { cands = []; }
        for (const c of cands.slice(0, 8)) { const s = scoreCandidate(t, c); if (s > bestScore) { best = c; bestScore = s; } }
        if (bestScore >= 0.72 || !(t.artists || []).length) break;
      }
      results[i] = { want: t, match: bestScore >= 0.5 ? best : null, score: Math.round(bestScore * 100) / 100, unsure: bestScore >= 0.5 && bestScore < 0.7 };
      done++;
      onProgress({ phase: 'match', done, total: tracks.length, last: t.title });
    }
  };
  await Promise.all(Array.from({ length: concurrency }, worker));
  return results;
}
