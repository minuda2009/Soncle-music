// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// Generates fixtures/flow/webm.json: what renderer/flow/webm.js (parseHead, pickRange,
// wholeClusters, buildFile) makes of the two committed WebM files, so the C# port
// (uno/src/Soncle.Flow/WebmCues.cs) can be held to it. Deterministic; no network.
//
// The WebM files are real Opus audio (ffmpeg + libopus, 8 s, 24 kbps, one cluster per second),
// made once and committed; this script only reads them:
//   sample.webm           Cues right after Tracks, before the Clusters (how YouTube's DASH audio is laid out)
//   sample-tailcues.webm  Cues at the end of the file, so the head has none (the "first cluster" path)
// To remake them:
//   SRC='aevalsrc=0.3*sin(2*PI*220*t)*(0.6+0.4*sin(2*PI*2*t))|0.3*sin(2*PI*330*t)*(0.6+0.4*sin(2*PI*2.5*t)):s=48000:d=8'
//   ffmpeg -f lavfi -i "$SRC" -c:a libopus -b:a 24k -vbr on -f webm -cluster_time_limit 1000 \
//     -reserve_index_space 1024 -fflags +bitexact -flags:a +bitexact fixtures/flow/sample.webm
//   (same without -reserve_index_space for sample-tailcues.webm)
//
//   node tools/fixtures/export-webm.mjs
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { fileURLToPath } from 'node:url';
import { parseHead, pickRange, wholeClusters, buildFile } from '../../renderer/flow/webm.js';

const here = path.dirname(fileURLToPath(import.meta.url));
const dir = path.join(here, '..', '..', 'fixtures', 'flow');
const sha = (u8) => crypto.createHash('sha256').update(u8).digest('hex');
const num = (x) => (x === Infinity ? 'Infinity' : x);   // JSON has no Infinity

function summarise(h) {
  if (!h) return null;
  return {
    ebmlLength: h.ebml.length,
    segDataStart: h.segDataStart,
    partLengths: h.parts.map((p) => p.length),
    cues: h.cues ? h.cues.map((c) => ({ t: c.t, pos: c.pos })) : null,
    firstCluster: h.firstCluster,
    scale: h.scale,
    duration: h.duration,
    truncated: h.truncated ?? null,
  };
}
const range = (r) => (r ? { from: r.from, to: num(r.to), t0: r.t0, cutAtCluster: !!r.cutAtCluster } : null);

const files = {};
for (const name of ['sample.webm', 'sample-tailcues.webm']) {
  const b = new Uint8Array(fs.readFileSync(path.join(dir, name)));
  const head = parseHead(b);
  const total = b.length;
  const ranges = [
    { args: { total }, got: range(pickRange(head, total)) },
    { args: { total, atFrac: 0.35, secs: 3 }, got: range(pickRange(head, total, { atFrac: 0.35, secs: 3 })) },
    { args: { total, atFrac: 0.8, secs: 2 }, got: range(pickRange(head, total, { atFrac: 0.8, secs: 2 })) },
    { args: { total, atFrac: 0, secs: 30 }, got: range(pickRange(head, total, { atFrac: 0, secs: 30 })) },
    { args: { total: 'Infinity', atFrac: 0.5, secs: 2 }, got: range(pickRange(head, Infinity, { atFrac: 0.5, secs: 2 })) },
  ];
  // a run of bytes that starts on a cluster boundary: whole clusters are kept, a cut-off one is dropped
  const from = head.firstCluster;
  const slices = [total, total - 1, total - 5000, from + 100, from + 1]
    .filter((to) => to > from)
    .map((to) => ({ from, to, keptLength: wholeClusters(b.subarray(from, to)).length }));
  // the standalone file rebuilt from the head and the clusters
  const clusters = wholeClusters(b.subarray(from));
  const rebuilt = buildFile(head, clusters);
  // truncated heads: cut through Tracks / before Info is complete
  const cuts = [20, 60, 100, 150, head.firstCluster - 1].map((n) => ({ n, head: summarise(parseHead(b.subarray(0, n))) }));
  files[name] = {
    length: total,
    head: summarise(head),
    ranges,
    slices,
    rebuilt: { length: rebuilt.length, sha256: sha(rebuilt) },
    truncatedHeads: cuts,
  };
}

// not WebM at all, and empty
const garbage = new Uint8Array(64).map((_, i) => (i * 37) & 0xff);
const out = {
  note: 'renderer/flow/webm.js results for fixtures/flow/sample*.webm',
  files,
  notWebm: { garbage: summarise(parseHead(garbage)), empty: summarise(parseHead(new Uint8Array(0))) },
};
fs.writeFileSync(path.join(dir, 'webm.json'), JSON.stringify(out, null, 2) + '\n');
console.log('wrote fixtures/flow/webm.json');
