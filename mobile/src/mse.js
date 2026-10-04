// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// Android has no mstream: proxy, so songs play from a MediaSource that is filled with 1 MB range
// requests made natively (CapacitorHttp, no CORS). The whole song is fetched back to back, which
// on a phone connection takes a few seconds, so seeking works once it has arrived.
const CHUNK = 1024 * 1024;
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

function append(ms, sb, buf) {
  return new Promise((resolve, reject) => {
    const done = () => { sb.removeEventListener('updateend', done); sb.removeEventListener('error', fail); resolve(); };
    const fail = () => { sb.removeEventListener('updateend', done); sb.removeEventListener('error', fail); reject(new Error('append failed')); };
    sb.addEventListener('updateend', done);
    sb.addEventListener('error', fail);
    try { sb.appendBuffer(buf); } catch (e) { sb.removeEventListener('updateend', done); sb.removeEventListener('error', fail); reject(e); }
  });
}

/**
 * @param {string} id  video id
 * @param {{ stream: (id: string) => Promise<{mime: string, length: number}>, range: (s: object, from: number, to: number) => Promise<{buf: Uint8Array, s?: object}>, log?: Function }} io
 *   `range` always gets the stream this MediaSource was opened for (same file, same length), and may
 *   hand back a refreshed URL for that same file.
 * @returns {string} a blob: URL for the media element
 */
export function mediaSourceUrl(id, io) {
  const ms = new MediaSource();
  const url = URL.createObjectURL(ms);
  const log = io.log || (() => {});
  ms.addEventListener('sourceopen', async () => {
    URL.revokeObjectURL(url);   // the element holds on to the MediaSource itself now
    const open = () => ms.readyState === 'open';
    try {
      let s = await io.stream(id);
      if (!open()) return;
      const mime = MediaSource.isTypeSupported(s.mime) ? s.mime : /webm/.test(s.mime) ? 'audio/webm; codecs="opus"' : 'audio/mp4; codecs="mp4a.40.2"';
      const sb = ms.addSourceBuffer(mime);
      let pos = 0;
      while (pos < s.length && open()) {
        const end = Math.min(s.length, pos + CHUNK);
        let got = null;
        for (let attempt = 0; !got && attempt < 4 && open(); attempt++) {
          try { const r = await io.range(s, pos, end); got = r.buf; if (r.s) s = r.s; } catch (e) { log(`mse ${id} @${pos} ${e.message}`); await sleep(600 * (attempt + 1)); }
        }
        if (!got) throw new Error('network');
        // The browser evicts already-played audio when its buffer quota is full; wait for room.
        for (let tries = 0; open(); tries++) {
          try { await append(ms, sb, got); break; } catch (e) {
            if (e.name !== 'QuotaExceededError' || tries > 120) throw e;
            await sleep(2000);
          }
        }
        pos = end;
      }
      if (open()) ms.endOfStream();
    } catch (e) {
      log(`mse ${id} stopped: ${e.message}`);
      try { if (open()) ms.endOfStream('network'); } catch {}
    }
  }, { once: true });
  return url;
}
