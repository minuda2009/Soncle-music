// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// Generates fixtures/audio: the reference numbers the C# DSP (M04) and engine (M10) must match.
// It runs today's JS (renderer/audio/worklets.js in a fake AudioWorkletGlobalScope, plus the pure
// functions in renderer/engine.js) and writes plain JSON + float32 PCM. Deterministic: re-running
// it produces byte-identical files. No network.
//
//   node tools/fixtures/export-audio.mjs
import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import { fileURLToPath } from 'node:url';
import { xfGains, EQ_BANDS, EQ_PRESETS, PRESET_PREAMP } from '../../renderer/engine.js';

const here = path.dirname(fileURLToPath(import.meta.url));
const out = path.join(here, '..', '..', 'fixtures', 'audio');
fs.mkdirSync(out, { recursive: true });
const SR = 48000;

// ---- load the worklets exactly as test/worklets.test.mjs does ----
function load(sr = SR) {
  const reg = {};
  class AudioWorkletProcessor { constructor() { this.port = { onmessage: null, sent: [], postMessage(m) { this.sent.push(m); } }; } }
  const ctx = { registerProcessor: (n, c) => (reg[n] = c), AudioWorkletProcessor, sampleRate: sr, Math, Float64Array, Float32Array };
  vm.runInNewContext(fs.readFileSync(path.join(here, '..', '..', 'renderer', 'audio', 'worklets.js'), 'utf8'), ctx);
  return reg;
}
function run(proc, L, R) {
  const oL = new Float32Array(L.length), oR = new Float32Array(R.length);
  for (let i = 0; i < L.length; i += 128) {
    const n = Math.min(128, L.length - i);
    const o = [oL.subarray(i, i + n), oR.subarray(i, i + n)];
    proc.process([[L.subarray(i, i + n), R.subarray(i, i + n)]], [o]);
  }
  return [oL, oR];
}
// true peak by 16× band-limited interpolation (independent of the processor), as in the JS test
function truePeak(x) {
  let m = 0;
  for (let i = 8; i < x.length - 8; i++) for (let f = 0; f < 16; f++) {
    const t = f / 16; let s = 0;
    for (let k = -8; k <= 8; k++) { const d = k - t; const sinc = d === 0 ? 1 : Math.sin(Math.PI * d) / (Math.PI * d); s += x[i + k] * sinc * (0.5 + 0.5 * Math.cos(Math.PI * d / 9)); }
    m = Math.max(m, Math.abs(s));
  }
  return m;
}
const f32 = (x) => Buffer.from(new Float32Array(x).buffer);
const r3 = (x) => Math.round(x * 1000) / 1000;

// ---- limiter: a hot signal (true peak must stay ≤ −1 dBTP) ----
{
  const reg = load();
  const M = new reg['soncle-master']();
  M.port.onmessage({ data: { ceilingDb: -1, meters: true } });
  const n = SR * 3;
  const L = new Float32Array(n), R = new Float32Array(n);
  for (let i = 0; i < n; i++) {
    L[i] = 0.98 * Math.sin(i * 0.07) + 0.35 * Math.sin(i * 0.9);
    R[i] = 0.9 * Math.sin(i * 0.05) + 0.4 * Math.sin(i * 0.8);
  }
  const [oL, oR] = run(M, L, R);
  const D = M.D;
  fs.writeFileSync(path.join(out, 'limiter-hot-in.f32'), f32(interleave(L, R)));
  fs.writeFileSync(path.join(out, 'limiter-hot-out.f32'), f32(interleave(oL, oR)));
  fs.writeFileSync(path.join(out, 'limiter-hot.json'), JSON.stringify({
    sampleRate: SR, frames: n, delay: D,
    inTruePeakDb: r3(20 * Math.log10(truePeak(L))),
    outTruePeakDb: r3(20 * Math.log10(truePeak(oL)))
  }, null, 1) + '\n');
}

// ---- limiter clean: transparent below the ceiling (pure delay) ----
{
  const reg = load();
  const M = new reg['soncle-master']();
  const n = SR;
  const L = new Float32Array(n), R = new Float32Array(n);
  for (let i = 0; i < n; i++) { L[i] = 0.3 * Math.sin(i * 0.05); R[i] = 0.3 * Math.sin(i * 0.031); }
  const [oL, oR] = run(M, L, R);
  fs.writeFileSync(path.join(out, 'limiter-clean-in.f32'), f32(interleave(L, R)));
  fs.writeFileSync(path.join(out, 'limiter-clean-out.f32'), f32(interleave(oL, oR)));
  fs.writeFileSync(path.join(out, 'limiter-clean.json'), JSON.stringify({ sampleRate: SR, frames: n, delay: M.D }, null, 1) + '\n');
}

// ---- LUFS reference: −23 LUFS tone ----
{
  const reg = load();
  const M = new reg['soncle-meter']();
  M.port.onmessage({ data: { reset: true, token: 1, on: true } });
  const n = SR * 5;
  // 1 kHz sine at −23 LUFS (mono-ish, both channels): amplitude chosen so the meter reads −23
  const amp = Math.pow(10, -23 / 20) * 1.0;
  const L = new Float32Array(n), R = new Float32Array(n);
  for (let i = 0; i < n; i++) { L[i] = amp * Math.sin(2 * Math.PI * 1000 * i / SR); R[i] = L[i]; }
  for (let i = 0; i < n; i += 128) { const m = Math.min(128, n - i); M.process([[L.subarray(i, i + m), R.subarray(i, i + m)]]); }
  const last = M.port.sent.at(-1);
  fs.writeFileSync(path.join(out, 'lufs-ref-in.f32'), f32(interleave(L, R)));
  fs.writeFileSync(path.join(out, 'lufs-ref.json'), JSON.stringify({ sampleRate: SR, frames: n, amplitude: amp, lufs: r3(last.lufs), seconds: last.seconds }, null, 1) + '\n');
}

// ---- crossfade curves ----
{
  const curves = ['smooth', 'equalpower', 'linear'];
  const points = 1001;
  const data = {};
  for (const c of curves) data[c] = Array.from({ length: points }, (_, i) => xfGains(c, i / (points - 1)).map(r3));
  fs.writeFileSync(path.join(out, 'crossfade-curves.json'), JSON.stringify({ curves: data }, null, 1) + '\n');
}

// ---- EQ presets + bands (the values the engine configures) ----
fs.writeFileSync(path.join(out, 'eq.json'), JSON.stringify({
  bands: EQ_BANDS, presets: EQ_PRESETS, preamp: PRESET_PREAMP,
  bassShelf: { type: 'lowshelf', frequency: 180 },
  loudness: { lowShelf: 100, highShelf: 9000 }
}, null, 1) + '\n');

function interleave(L, R) {
  const out = new Float32Array(L.length * 2);
  for (let i = 0; i < L.length; i++) { out[i * 2] = L[i]; out[i * 2 + 1] = R[i]; }
  return out;
}
console.log('wrote fixtures/audio');
