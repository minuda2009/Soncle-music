// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// Generates fixtures/flow: the reference numbers the C# Flow port (M05) must match. It runs
// today's JS (renderer/flow/flow.js + analyze.js) and writes plain JSON + the synthetic groove's
// PCM. Deterministic: re-running produces byte-identical files. No network.
//
//   node tools/fixtures/export-flow.mjs
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { keyCompat, tempoCompat, transition, planFlow, contextFor } from '../../renderer/flow/flow.js';
import { analyze, camelotOf } from '../../renderer/flow/analyze.js';

const here = path.dirname(fileURLToPath(import.meta.url));
const out = path.join(here, '..', '..', 'fixtures', 'flow');
fs.mkdirSync(out, { recursive: true });
const r4 = (x) => (x == null ? null : Math.round(x * 10000) / 10000);

// ---- compatibility tables ----
const keys = ['1A', '1B', '2A', '5A', '8A', '8B', '9A', '12B', '3B', null];
const keyTable = [];
for (const a of keys) for (const b of keys) keyTable.push([a, b, keyCompat(a, b)]);
const tempos = [128, 64, 60, 120, 124, 126, 175, 174, 70, 96, 90];
const tempoTable = [];
for (const a of tempos) for (const b of tempos) tempoTable.push([a, b, r4(tempoCompat(a, b))]);

// ---- synthetic groove + analysis ----
const sr = 22050, secs = 20, n = sr * secs, x = new Float32Array(n), bpm = 128, beat = 60 / bpm;
for (let i = 0; i < n; i++) {
  const t = i / sr, bt = t % beat;
  let s = Math.exp(-bt * 30) * Math.sin(2 * Math.PI * 60 * bt) * 0.8;
  for (const semi of [0, 3, 7]) s += 0.08 * Math.sin(2 * Math.PI * 220 * Math.pow(2, semi / 12) * t);
  x[i] = s * 0.5;
}
fs.writeFileSync(path.join(out, 'groove.f32'), Buffer.from(x.buffer));
const analysed = analyze(x, sr);
fs.writeFileSync(path.join(out, 'groove.json'), JSON.stringify({ sampleRate: sr, bpm, seconds: secs, expected: analysed }, null, 1) + '\n');

// ---- planFlow cases ----
const T = (id, dur = 200, artist = 'A' + id) => ({ id, title: id, duration: dur, artists: [{ name: artist }] });
const cases = [];

{
  const F = {
    s: { bpm: 124, bpmConf: 0.9, camelot: '8A', keyConf: 0.8, energy: 0.6 },
    fast: { bpm: 174, bpmConf: 0.9, camelot: '3B', keyConf: 0.8, energy: 0.95 },
    near: { bpm: 126, bpmConf: 0.9, camelot: '9A', keyConf: 0.8, energy: 0.62 },
    ballad: { bpm: 70, bpmConf: 0.8, camelot: '2B', keyConf: 0.7, energy: 0.2 },
    near2: { bpm: 122, bpmConf: 0.9, camelot: '8B', keyConf: 0.8, energy: 0.58 },
  };
  const feat = (id) => F[id] || null;
  const pool = ['fast', 'ballad', 'unknown', 'near', 'near2'].map((id) => T(id));
  cases.push({
    name: 'default',
    seed: T('s'), pool, feat: F, opts: {},
    expected: planFlow(T('s'), pool, feat).map((p) => p.track.id),
  });
  cases.push({
    name: 'no-data-falls-back-to-relevance',
    seed: T('x'), pool, feat: {}, opts: {},
    expected: planFlow(T('x'), pool, () => null).map((p) => p.track.id),
  });
}

{
  const F = {
    s: { bpm: 100, bpmConf: 0.9, camelot: '8A', keyConf: 0.8, energy: 0.5 },
    calm: { bpm: 100, bpmConf: 0.9, camelot: '8A', keyConf: 0.8, energy: 0.25 },
    hype: { bpm: 100, bpmConf: 0.9, camelot: '8A', keyConf: 0.8, energy: 0.8 },
  };
  const feat = (id) => F[id] || null;
  const fill = Array.from({ length: 18 }, (_, i) => T('f' + i, 200, 'F' + i));
  const pool = [...fill.slice(0, 9), T('hype', 200, 'X'), T('calm', 200, 'Y'), ...fill.slice(9)];
  cases.push({
    name: 'night-context', seed: T('s', 200, 'Z'), pool, feat: F, opts: { ctx: '2026-09-29T02:00:00' },
    expected: planFlow(T('s', 200, 'Z'), pool, feat, { ctx: contextFor(new Date(2026, 8, 29, 2, 0)) }).map((p) => p.track.id),
  });
  cases.push({
    name: 'day-context', seed: T('s', 200, 'Z'), pool, feat: F, opts: { ctx: '2026-09-29T15:00:00' },
    expected: planFlow(T('s', 200, 'Z'), pool, feat, { ctx: contextFor(new Date(2026, 8, 29, 15, 0)) }).map((p) => p.track.id),
  });
  cases.push({
    name: 'taste', seed: T('s', 200, 'Z'), pool, feat: F,
    opts: { taste: { artist: { y: 1 }, liked: [], skips: { hype: 3 }, recent: {} } },
    expected: planFlow(T('s', 200, 'Z'), pool, feat, {
      taste: { artist: (nm) => (nm === 'y' ? 1 : 0), liked: () => false, skips: (id) => (id === 'hype' ? 3 : 0), recent: () => 0 }
    }).map((p) => p.track.id),
  });
}

// transition cases
const trans = [];
{
  const a = T('s'), b = T('near');
  const fa = { bpm: 124, bpmConf: 0.9, camelot: '8A', keyConf: 0.8, energy: 0.6 };
  const fb = { bpm: 126, bpmConf: 0.9, camelot: '9A', keyConf: 0.8, energy: 0.62 };
  const tr = transition(a, fa, b, fb);
  trans.push({ a: a.id, b: b.id, fa, fb, score: r4(tr.score), sure: r4(tr.sure) });
}

fs.writeFileSync(path.join(out, 'flow.json'), JSON.stringify({ keyCompat: keyTable, tempoCompat: tempoTable, planFlow: cases, transition: trans }, null, 1) + '\n');
console.log('wrote fixtures/flow');
