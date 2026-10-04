import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';

// Load renderer/audio/worklets.js in a fake AudioWorkletGlobalScope
function load(sr = 48000) {
  const reg = {};
  class AudioWorkletProcessor { constructor() { this.port = { onmessage: null, sent: [], postMessage(m) { this.sent.push(m); } }; } }
  const ctx = { registerProcessor: (n, c) => (reg[n] = c), AudioWorkletProcessor, sampleRate: sr, Math, Float64Array, Float32Array };
  vm.runInNewContext(fs.readFileSync(new URL('../renderer/audio/worklets.js', import.meta.url), 'utf8'), ctx);
  return reg;
}
function run(proc, L, R) {
  const out = [new Float32Array(L.length), new Float32Array(L.length)];
  for (let i = 0; i < L.length; i += 128) {
    const o = [out[0].subarray(i, i + 128), out[1].subarray(i, i + 128)];
    proc.process([[L.subarray(i, i + 128), R.subarray(i, i + 128)]], [o]);
  }
  return out;
}
// true peak of a signal by 16× band-limited interpolation (reference, independent of the processor)
function truePeak(x) {
  let m = 0;
  for (let i = 8; i < x.length - 8; i++) for (let f = 0; f < 16; f++) {
    const t = f / 16; let s = 0;
    for (let k = -8; k <= 8; k++) { const d = k - t; const sinc = d === 0 ? 1 : Math.sin(Math.PI * d) / (Math.PI * d); s += x[i + k] * sinc * (0.5 + 0.5 * Math.cos(Math.PI * d / 9)); }
    m = Math.max(m, Math.abs(s));
  }
  return m;
}

test('limiter is transparent below the ceiling (pure delay)', () => {
  const M = new (load()['soncle-master'])();
  const n = 48000, L = new Float32Array(n), R = new Float32Array(n);
  for (let i = 0; i < n; i++) { L[i] = 0.3 * Math.sin(i * 0.05); R[i] = 0.3 * Math.sin(i * 0.031); }
  const [oL] = run(M, L, R);
  const D = M.D;
  let err = 0; for (let i = D; i < n; i++) err = Math.max(err, Math.abs(oL[i] - L[i - D]));
  assert.ok(err < 1e-6, 'err ' + err);
  assert.ok(D / 48000 < 0.003, 'latency ' + D);
});

test('limiter keeps true peak under -1 dBTP on hot, clipped-looking material', () => {
  const M = new (load()['soncle-master'])();
  const n = 48000, L = new Float32Array(n), R = new Float32Array(n);
  let seed = 1; const rnd = () => ((seed = (seed * 16807) % 2147483647) / 2147483647) * 2 - 1;
  for (let i = 0; i < n; i++) {
    // loud kick-like bursts + near-Nyquist content (worst case for inter-sample peaks)
    const burst = (i % 12000) < 400 ? 1.8 * Math.sin(i * 0.02) : 0;
    L[i] = 0.9 * Math.sin(i * 2.9) + burst + 0.1 * rnd();
    R[i] = 0.9 * Math.sin(i * 2.9 + 1) - burst + 0.1 * rnd();
  }
  const [oL, oR] = run(M, L, R);
  const ceil = Math.pow(10, -1 / 20);
  let sp = 0; for (let i = 0; i < n; i++) sp = Math.max(sp, Math.abs(oL[i]), Math.abs(oR[i]));
  assert.ok(sp <= ceil + 1e-6, 'sample peak ' + sp);
  const tp = Math.max(truePeak(oL.subarray(2000)), truePeak(oR.subarray(2000)));
  assert.ok(20 * Math.log10(tp) < -0.8, 'true peak dBTP ' + (20 * Math.log10(tp)).toFixed(2));
});

test('meter reads a -23 LUFS reference tone correctly', () => {
  // 1 kHz sine at -23 dBFS... per BS.1770, a 997 Hz sine at 0 dBFS in both channels ≈ +0.0 LUFS offset: L = -3.01 dBFS rms per ch → sum
  const sr = 48000, M = new (load(sr)['soncle-meter'])();
  M.port.onmessage({ data: { reset: true, on: true, token: 7 } });
  const n = sr * 5, L = new Float32Array(n);
  const amp = Math.pow(10, -23 / 20);
  for (let i = 0; i < n; i++) L[i] = amp * Math.sin(2 * Math.PI * 997 * i / sr);
  for (let i = 0; i < n; i += 128) M.process([[L.subarray(i, i + 128), L.subarray(i, i + 128)]]);
  const last = M.port.sent.at(-1);
  // stereo sine peak -23 dBFS: each channel rms -26.01 dB; K-weight ≈ +0.69 dB at 1 kHz; two channels +3.01
  // → -0.691 + 10log10(2·0.5·amp²·10^(0.069)) ≈ -23.0
  assert.equal(last.token, 7);
  assert.ok(Math.abs(last.lufs - -23) < 0.3, 'lufs ' + last.lufs);
});
