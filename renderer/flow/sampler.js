// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// Fetch ~30 s of a song through the app's own stream proxy and decode it to mono PCM for analysis.
import { parseHead, pickRange, wholeClusters, buildFile } from './webm.js';

const SR = 22050;
const HEAD_BYTES = 96 * 1024;

async function getRange(url, from, to) {
  // Android: the app fetches the bytes natively (no mstream: proxy there)
  if (url.startsWith('mstream://') && globalThis.api?.range) return globalThis.api.range(url.slice(10), from, to);
  const r = await fetch(url, { headers: { Range: `bytes=${from}-${to - 1}` } });
  if (!r.ok) throw new Error('HTTP ' + r.status);
  const total = Number((/\/(\d+)/.exec(r.headers.get('content-range') || '') || [])[1]) || 0;
  return { buf: new Uint8Array(await r.arrayBuffer()), total };
}
async function decode(bytes) {
  const ctx = new OfflineAudioContext(1, 1, SR);
  const ab = await ctx.decodeAudioData(bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength));
  const n = ab.length, out = new Float32Array(n);
  for (let c = 0; c < ab.numberOfChannels; c++) { const d = ab.getChannelData(c); for (let i = 0; i < n; i++) out[i] += d[i] / ab.numberOfChannels; }
  return out;
}
const middle = (pcm, secs = 15) => {
  const n = SR * secs;
  if (pcm.length <= n) return pcm;
  const s = Math.floor(pcm.length * 0.35);
  return pcm.subarray(Math.min(s, pcm.length - n), Math.min(s, pcm.length - n) + n);
};

/** @returns {Promise<Float32Array|null>} mono PCM at 22.05 kHz */
export async function sample(track, { local = false } = {}) {
  const url = 'mstream://' + track.id;
  if (local) {
    const r = await fetch(url);
    return middle(await decode(new Uint8Array(await r.arrayBuffer())));
  }
  const { buf: head, total } = await getRange(url, 0, HEAD_BYTES);
  const h = parseHead(head);
  if (h) {
    const rg = pickRange(h, total || Infinity, { secs: 15 });
    if (rg) {
      let { buf } = await getRange(url, rg.from, Math.min(rg.to, rg.from + 500_000));
      buf = wholeClusters(buf);
      if (buf.length > 20_000) return middle(await decode(buildFile(h, buf)));
    }
  }
  // not WebM (e.g. AAC/MP4): try the first ~700 KB as-is
  const { buf } = await getRange(url, 0, 450_000);
  return middle(await decode(buf));
}
export { SR };
