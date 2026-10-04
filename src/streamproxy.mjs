// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
/* global ReadableStream, AbortSignal */
// Serves a byte range of a remote googlevideo stream as ONE continuous response, while
// pulling the origin in bounded chunks and switching clients if a chunk is refused.
export const CHUNK = 1024 * 1024;

export function parseRange(range, size) {
  let start = 0, end = size ? size - 1 : null;
  const m = range && /bytes=(\d*)-(\d*)/.exec(range);
  if (m) {
    if (m[1]) start = Number(m[1]);
    else if (m[2] && size) { start = Math.max(0, size - Number(m[2])); }   // suffix range "bytes=-500"
    if (m[1] && m[2]) end = Math.min(Number(m[2]), size ? size - 1 : Infinity);
  }
  return { start, end };
}

/**
 * @param {object} o
 * @param {object} o.stream        resolved stream {url, headers, ua, mime, length, client}
 * @param {string|null} o.range    request Range header
 * @param {Function} o.fetchImpl
 * @param {Function} o.reresolve   async (prev, attempt) => new stream (other client)
 * @param {Function} [o.log]
 * @param {number} [o.chunk]
 */
/**
 * Tiny byte cache for the song that is playing: keeps only the file head and the middle section
 * (what Flow radio analyses), so measuring a song you're listening to costs no extra download.
 */
export function makeChunkCache({ maxSongs = 2, windows = (total) => [[0, 131072], [Math.floor(total * 0.3), Math.floor(total * 0.5)]] } = {}) {
  const songs = new Map();   // id -> { total, parts: [{from, buf}] }
  return {
    put(id, total, from, buf) {
      if (!total) return;
      let e = songs.get(id);
      for (const [a, b] of windows(total)) {
        const s0 = Math.max(a, from), s1 = Math.min(b, from + buf.length);
        if (s1 <= s0) continue;
        if (!e) { e = { total, parts: [] }; songs.set(id, e); while (songs.size > maxSongs) songs.delete(songs.keys().next().value); }
        if (e.parts.some((p) => p.from <= s0 && p.from + p.buf.length >= s1)) continue;
        e.parts.push({ from: s0, buf: buf.slice(s0 - from, s1 - from) });   // copy: don't pin the whole chunk
        e.parts.sort((x, y) => x.from - y.from);
      }
    },
    // returns bytes [from, to] if fully covered by cached parts, else null
    get(id, from, to) {
      const e = songs.get(id);
      if (!e) return null;
      const out = new Uint8Array(to - from + 1);
      let pos = from;
      for (const p of e.parts) {
        const pEnd = p.from + p.buf.length - 1;
        if (pEnd < pos || p.from > pos) continue;
        const take = Math.min(pEnd, to) - pos + 1;
        out.set(p.buf.subarray(pos - p.from, pos - p.from + take), pos - from);
        pos += take;
        if (pos > to) return out;
      }
      return null;
    },
    size() { let n = 0; for (const e of songs.values()) for (const p of e.parts) n += p.buf.length; return n; }
  };
}

export async function proxyStream({ stream, range, fetchImpl, reresolve, log = () => {}, chunk = CHUNK, cache = null, cacheId = null }) {
  const size = stream.length || 0;
  const type = (stream.mime || 'audio/webm').split(';')[0];
  const { start, end } = parseRange(range, size);
  if (size && start >= size) return new Response('range past end', { status: 416, headers: { 'Content-Range': `bytes */${size}` } });
  const hdrs = (s) => s.headers || { 'User-Agent': s.ua };

  if (!size) {
    const res = await fetchImpl(stream.url + `&range=${start}-`, { headers: hdrs(stream) }).catch(() => null);
    if (!res || !res.ok) return new Response('upstream ' + (res?.status || 'error'), { status: 502 });
    return new Response(res.body, { status: 200, headers: { 'Content-Type': type, 'Access-Control-Allow-Origin': '*' } });
  }

  let cur = stream;
  let cancelled = false;
  const fetchChunk = async (from, to) => {
    const hit = cache && cacheId ? cache.get(cacheId, from, to) : null;
    if (hit) return hit;
    for (let attempt = 0; attempt < 4 && !cancelled; attempt++) {
      try {
        // a hung connection must not freeze playback: give each chunk 20 s, then retry / switch client
        const res = await fetchImpl(cur.url + `&range=${from}-${to}`, { headers: hdrs(cur), signal: AbortSignal.timeout(20000) });
        if (res.ok) {
          let buf = new Uint8Array(await res.arrayBuffer());
          if (buf.length > to - from + 1) buf = buf.subarray(0, to - from + 1);
          if (buf.length) { if (cache && cacheId) cache.put(cacheId, size, from, buf); return buf; }
          log(`${cur.client} empty chunk at ${from}`);
        } else {
          log(`${cur.client} upstream ${res.status} at ${from}`);
          try { await res.arrayBuffer(); } catch {}
        }
      } catch (e) { log(`${cur.client} fetch error at ${from}: ${e.message}`); }
      if (cancelled) break;
      try { cur = await reresolve(cur, attempt); } catch (e) { log(`re-resolve failed: ${e.message}`); }
    }
    throw new Error('stream interrupted at byte ' + from);
  };

  let pos = start;
  let pending;
  try { pending = await fetchChunk(pos, Math.min(end, pos + chunk - 1)); }
  catch (e) { return new Response(e.message, { status: 502 }); }
  const body = new ReadableStream({
    async pull(ctrl) {
      try {
        let buf = pending;
        pending = null;
        if (!buf) buf = await fetchChunk(pos, Math.min(end, pos + chunk - 1));
        if (cancelled) return;
        ctrl.enqueue(buf);
        pos += buf.length;
        if (pos > end) ctrl.close();
      } catch (e) { log(e.message); try { ctrl.error(e); } catch {} }
    },
    cancel() { cancelled = true; }
  }, { highWaterMark: 1 });
  return new Response(body, {
    status: range ? 206 : 200,
    headers: {
      'Content-Type': type,
      'Content-Length': String(end - start + 1),
      'Content-Range': `bytes ${start}-${end}/${size}`,
      'Accept-Ranges': 'bytes',
      'Access-Control-Allow-Origin': '*'
    }
  });
}
