// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// Cut a decodable excerpt out of a (YouTube DASH) WebM without downloading the whole song:
// keep the EBML header, Info and Tracks, then append whole Clusters from the middle of the song
// (located through the Cues index), with the Segment size set to "unknown".

const ID = { EBML: 0x1a45dfa3, Segment: 0x18538067, SeekHead: 0x114d9b74, Info: 0x1549a966, Tracks: 0x1654ae6b, Cues: 0x1c53bb6b, Cluster: 0x1f43b675, CuePoint: 0xbb, CueTime: 0xb3, CueTrackPositions: 0xb7, CueClusterPosition: 0xf1, TimecodeScale: 0x2ad7b1, Duration: 0x4489 };

function readId(b, p) {
  const x = b[p];
  const len = x >= 0x80 ? 1 : x >= 0x40 ? 2 : x >= 0x20 ? 3 : x >= 0x10 ? 4 : 0;
  if (!len || p + len > b.length) return null;
  let v = 0; for (let i = 0; i < len; i++) v = v * 256 + b[p + i];
  return { v, len };
}
function readSize(b, p) {
  const x = b[p];
  let len = 1, mask = 0x80;
  while (len <= 8 && !(x & mask)) { len++; mask >>= 1; }
  if (len > 8 || p + len > b.length) return null;
  let v = x & (mask - 1), allOnes = v === mask - 1;
  for (let i = 1; i < len; i++) { v = v * 256 + b[p + i]; if (b[p + i] !== 0xff) allOnes = false; }
  return { v: allOnes ? -1 : v, len };
}
function readUint(b, p, n) { let v = 0; for (let i = 0; i < n; i++) v = v * 256 + b[p + i]; return v; }
function readFloat(b, p, n) { const dv = new DataView(b.buffer, b.byteOffset + p, n); return n === 4 ? dv.getFloat32(0) : dv.getFloat64(0); }

function* children(b, start, end) {
  let p = start;
  while (p < end) {
    const id = readId(b, p); if (!id) return;
    const sz = readSize(b, p + id.len); if (!sz) return;
    const dataStart = p + id.len + sz.len;
    const dataEnd = sz.v < 0 ? end : dataStart + sz.v;
    yield { id: id.v, start: p, dataStart, dataEnd, unknown: sz.v < 0 };
    if (sz.v < 0) return;
    p = dataEnd;
  }
}

/** Parse the head of a WebM file. Returns what's needed to slice it, or null. */
export function parseHead(b) {
  let segDataStart = -1, ebmlEnd = -1;
  for (const el of children(b, 0, b.length)) {
    if (el.id === ID.EBML) ebmlEnd = el.dataEnd;
    if (el.id === ID.Segment) { segDataStart = el.dataStart; break; }
  }
  if (segDataStart < 0 || ebmlEnd < 0) return null;
  const out = { ebml: b.subarray(0, ebmlEnd), segDataStart, parts: [], cues: null, firstCluster: -1, scale: 1e6, duration: 0 };
  for (const el of children(b, segDataStart, b.length)) {
    if (el.dataEnd > b.length && el.id !== ID.Cluster) { out.truncated = el.id; break; }
    if (el.id === ID.Info) {
      out.parts.push(b.subarray(el.start, el.dataEnd));
      for (const c of children(b, el.dataStart, el.dataEnd)) {
        if (c.id === ID.TimecodeScale) out.scale = readUint(b, c.dataStart, c.dataEnd - c.dataStart);
        if (c.id === ID.Duration) out.duration = readFloat(b, c.dataStart, c.dataEnd - c.dataStart);
      }
    } else if (el.id === ID.Tracks) out.parts.push(b.subarray(el.start, el.dataEnd));
    else if (el.id === ID.Cues) {
      const cues = [];
      for (const cp of children(b, el.dataStart, el.dataEnd)) {
        if (cp.id !== ID.CuePoint) continue;
        let time = 0, pos = -1;
        for (const c of children(b, cp.dataStart, cp.dataEnd)) {
          if (c.id === ID.CueTime) time = readUint(b, c.dataStart, c.dataEnd - c.dataStart);
          if (c.id === ID.CueTrackPositions) for (const d of children(b, c.dataStart, c.dataEnd)) if (d.id === ID.CueClusterPosition) pos = readUint(b, d.dataStart, d.dataEnd - d.dataStart);
        }
        if (pos >= 0) cues.push({ t: (time * out.scale) / 1e9, pos: segDataStart + pos });
      }
      out.cues = cues;
    } else if (el.id === ID.Cluster) { out.firstCluster = el.start; break; }
  }
  if (out.parts.length < 2) return null;
  return out;
}

/** Byte range [from, to) of whole clusters covering about `secs` seconds starting near `atFrac` of the song. */
export function pickRange(head, total, { atFrac = 0.35, secs = 30 } = {}) {
  const cues = head.cues;
  if (cues?.length > 2) {
    const dur = head.duration ? (head.duration * head.scale) / 1e9 : cues[cues.length - 1].t;
    const t0 = dur * atFrac;
    let i = 0; while (i + 1 < cues.length && cues[i + 1].t <= t0) i++;
    let j = i; while (j < cues.length && cues[j].t < cues[i].t + secs) j++;
    return { from: cues[i].pos, to: j < cues.length ? cues[j].pos : total, t0: cues[i].t };
  }
  if (head.firstCluster > 0) return { from: head.firstCluster, to: Math.min(total, head.firstCluster + 600_000), t0: 0, cutAtCluster: true };
  return null;
}

/** Keep only complete clusters from a byte run that starts on a cluster boundary. */
export function wholeClusters(b) {
  let end = 0;
  for (const el of children(b, 0, b.length)) { if (el.id !== ID.Cluster || el.dataEnd > b.length) break; end = el.dataEnd; }
  return b.subarray(0, end);
}

/** Assemble a standalone WebM: EBML header + Segment(unknown size){Info, Tracks, clusters…} */
export function buildFile(head, clusters) {
  const segHead = new Uint8Array([0x18, 0x53, 0x80, 0x67, 0x01, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff]);
  const parts = [head.ebml, segHead, ...head.parts, clusters];
  const len = parts.reduce((a, p) => a + p.length, 0);
  const out = new Uint8Array(len);
  let o = 0; for (const p of parts) { out.set(p, o); o += p.length; }
  return out;
}
