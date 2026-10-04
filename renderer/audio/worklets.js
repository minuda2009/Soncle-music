// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// Audio-thread processors (AudioWorklet). Kept allocation-free inside process().
//
//  soncle-master  true-peak look-ahead limiter + optional output meters (LUFS, true peak, reduction)
//  soncle-meter   per-deck integrated loudness (EBU R128 / ITU-R BS.1770), no audio output
/* global registerProcessor, AudioWorkletProcessor, sampleRate */

// ---------- K-weighting (BS.1770) for any sample rate — coefficients as in libebur128 ----------
function kWeighting(sr) {
  let f0 = 1681.974450955533, G = 3.999843853973347, Q = 0.7071752369554196;
  let K = Math.tan(Math.PI * f0 / sr);
  const Vh = Math.pow(10, G / 20), Vb = Math.pow(Vh, 0.4996667741545416);
  let a0 = 1 + K / Q + K * K;
  const pre = { b0: (Vh + Vb * K / Q + K * K) / a0, b1: 2 * (K * K - Vh) / a0, b2: (Vh - Vb * K / Q + K * K) / a0, a1: 2 * (K * K - 1) / a0, a2: (1 - K / Q + K * K) / a0 };
  f0 = 38.13547087602444; Q = 0.5003270373238773;
  K = Math.tan(Math.PI * f0 / sr);
  a0 = 1 + K / Q + K * K;
  const rlb = { b0: 1, b1: -2, b2: 1, a1: 2 * (K * K - 1) / a0, a2: (1 - K / Q + K * K) / a0 };
  return { pre, rlb };
}
// Two cascaded biquads per channel (transposed direct form II); returns the squared K-weighted sample
class KFilter {
  constructor(sr) { const { pre, rlb } = kWeighting(sr); this.p = pre; this.r = rlb; this.s = new Float64Array(8); }
  sq(x, ch) {
    const s = this.s, o = ch * 4, p = this.p, r = this.r;
    const y1 = p.b0 * x + s[o];
    s[o] = p.b1 * x - p.a1 * y1 + s[o + 1];
    s[o + 1] = p.b2 * x - p.a2 * y1;
    const y2 = r.b0 * y1 + s[o + 2];
    s[o + 2] = r.b1 * y1 - r.a1 * y2 + s[o + 3];
    s[o + 3] = r.b2 * y1 - r.a2 * y2;
    return y2 * y2;
  }
}
const lufsOf = (meanSquare) => (meanSquare > 0 ? -0.691 + 10 * Math.log10(meanSquare) : -Infinity);

// Gated integrated loudness from 100 ms sub-blocks (400 ms blocks, 75 % overlap).
class Integrator {
  constructor(sr) { this.hop = Math.round(sr * 0.1); this.reset(); }
  // block energies live in a preallocated array (grows by doubling only past ~13 minutes)
  reset() { this.acc = 0; this.n = 0; this.sub = [0, 0, 0, 0]; this.subN = 0; if (!this.blocks) this.blocks = new Float64Array(8192); this.count = 0; }
  add(e) {   // e = summed channel energy of one sample
    this.acc += e;
    if (++this.n < this.hop) return;
    this.sub[this.subN++ % 4] = this.acc / this.n;
    this.acc = 0; this.n = 0;
    if (this.subN >= 4) {
      const ms = (this.sub[0] + this.sub[1] + this.sub[2] + this.sub[3]) / 4;
      if (lufsOf(ms) > -70) {                     // absolute gate
        if (this.count === this.blocks.length) { const b = new Float64Array(this.count * 2); b.set(this.blocks); this.blocks = b; }
        this.blocks[this.count++] = ms;
      }
    }
  }
  value() {
    const b = this.blocks, n = this.count;
    if (!n) return null;
    let sum = 0; for (let i = 0; i < n; i++) sum += b[i];
    const rel = lufsOf(sum / n) - 10;                // relative gate
    let s2 = 0, n2 = 0;
    for (let i = 0; i < n; i++) if (lufsOf(b[i]) > rel) { s2 += b[i]; n2++; }
    return n2 ? lufsOf(s2 / n2) : null;
  }
}

// ---------- 4× oversampling interpolator for true-peak estimation (12 taps per phase) ----------
const TAPS = 12, PHASES = 4;
const POLY = (() => {
  const N = TAPS * PHASES, h = new Float64Array(N), c = (N - 1) / 2;
  for (let i = 0; i < N; i++) {
    const x = (i - c) / PHASES;
    const sinc = x === 0 ? 1 : Math.sin(Math.PI * x) / (Math.PI * x);
    const w = 0.5 - 0.5 * Math.cos((2 * Math.PI * (i + 0.5)) / N);   // Hann
    h[i] = sinc * w;
  }
  // phase p uses taps h[p], h[p+4], … ; normalise each phase to unity DC gain
  const out = [];
  for (let p = 0; p < PHASES; p++) {
    const f = new Float64Array(TAPS); let s = 0;
    for (let k = 0; k < TAPS; k++) { f[k] = h[p + k * PHASES]; s += f[k]; }
    for (let k = 0; k < TAPS; k++) f[k] /= s;
    out.push(f);
  }
  return out;
})();

class MasterProcessor extends AudioWorkletProcessor {
  constructor() {
    super();
    const sr = sampleRate;
    this.ceiling = Math.pow(10, -1 / 20);          // -1 dBTP
    this.L = Math.max(16, Math.ceil(sr * 0.002));   // 2 ms look-ahead
    this.FD = TAPS / 2;                             // interpolator delay (samples)
    this.D = this.L - 1 + this.FD;                  // total audio delay
    this.rel = 1 - Math.exp(-1 / (sr * 0.08));      // 80 ms release
    // interpolator history, written twice so a TAPS-long window is always contiguous (no modulo)
    this.h0 = new Float64Array(TAPS * 2); this.h1 = new Float64Array(TAPS * 2); this.hpos = 0;
    this.dsize = 1; while (this.dsize < this.D + 256) this.dsize <<= 1;
    this.dl0 = new Float32Array(this.dsize); this.dl1 = new Float32Array(this.dsize); this.dpos = 0;
    // sliding minimum of the required gain: monotonic deque in a power-of-two ring
    this.qsize = 1; while (this.qsize < this.L + 2) this.qsize <<= 1;
    this.qv = new Float64Array(this.qsize); this.qi = new Float64Array(this.qsize);
    this.qh = 0; this.qt = 0; this.t = 0;
    // moving average of the sliding minimum (length L)
    this.box = new Float64Array(this.L).fill(1); this.bpos = 0; this.bsum = this.L;
    this.env = 1; this.idle = true;
    // meters
    this.metering = false;
    this.kf = new KFilter(sr);
    this.mom = new Float64Array(Math.round(sr * 0.4 / 128) + 1); this.momPos = 0; this.momAcc = 0;
    this.short = new Float64Array(Math.round(sr * 3 / 128) + 1); this.shortPos = 0; this.shortAcc = 0;
    this.tpHold = 0; this.grMin = 1; this.frames = 0; this.reportEvery = Math.round(sr / 15 / 128);
    this.port.onmessage = (e) => {
      const m = e.data || {};
      if (m.ceilingDb != null) this.ceiling = Math.pow(10, m.ceilingDb / 20);
      if (m.meters != null) { this.metering = !!m.meters; this.tpHold = 0; }
    };
  }
  process(inputs, outputs) {
    const inp = inputs[0], out = outputs[0];
    if (!out || !out.length) return true;
    const n = out[0].length;
    const inL = inp && inp[0] ? inp[0] : null, inR = inp && inp[1] ? inp[1] : inL;
    const oL = out[0], oR = out[1] || null;
    const ceil = this.ceiling, gate = ceil * 0.6;   // inter-sample overs only matter within ~4.4 dB
    const h0 = this.h0, h1 = this.h1, dl0 = this.dl0, dl1 = this.dl1, mask = this.dsize - 1, D = this.D;
    const rel = this.rel, L = this.L, qv = this.qv, qi = this.qi, qm = this.qsize - 1, box = this.box;
    const P1 = POLY[1], P2 = POLY[2], P3 = POLY[3], FD = this.FD;
    let hp = this.hpos, dpos = this.dpos, qh = this.qh, qt = this.qt, t = this.t, bpos = this.bpos, bsum = this.bsum, env = this.env;
    let momE = 0, tpBlock = 0, grBlock = 1;
    for (let i = 0; i < n; i++) {
      const xl = inL ? inL[i] : 0, xr = inR ? inR[i] : 0;
      // history: newest sample at hp (and hp+TAPS); window h[hp+1 .. hp+TAPS] oldest→newest
      h0[hp] = xl; h0[hp + TAPS] = xl; h1[hp] = xr; h1[hp + TAPS] = xr;
      const base = hp + 1;   // h[base + TAPS - 1 - k] = sample k ago
      const c0 = base + TAPS - FD, c1 = c0 + 1;   // FD-1 and FD-2 samples ago
      let a0 = h0[c0], a1 = h0[c1], b0 = h1[c0], b1 = h1[c1];
      let peak = a0 < 0 ? -a0 : a0;
      if (a1 > peak) peak = a1; else if (-a1 > peak) peak = -a1;
      if (b0 > peak) peak = b0; else if (-b0 > peak) peak = -b0;
      if (b1 > peak) peak = b1; else if (-b1 > peak) peak = -b1;
      if (peak > gate) {
        let s1 = 0, s2 = 0, s3 = 0, r1 = 0, r2 = 0, r3 = 0;
        for (let k = 0; k < TAPS; k++) {
          const j = base + TAPS - 1 - k, x0 = h0[j], x1 = h1[j];
          s1 += P1[k] * x0; s2 += P2[k] * x0; s3 += P3[k] * x0;
          r1 += P1[k] * x1; r2 += P2[k] * x1; r3 += P3[k] * x1;
        }
        const m = Math.max(Math.abs(s1), Math.abs(s2), Math.abs(s3), Math.abs(r1), Math.abs(r2), Math.abs(r3));
        if (m > peak) peak = m;
      }
      hp = hp + 1 === TAPS ? 0 : hp + 1;
      if (peak > tpBlock) tpBlock = peak;
      const need = peak > ceil ? ceil / peak : 1;
      // sliding minimum over the last L needs
      while (qt !== qh && qv[(qt - 1) & qm] >= need) qt = (qt - 1) & qm;
      qv[qt] = need; qi[qt] = t; qt = (qt + 1) & qm;
      if (qi[qh] <= t - L) qh = (qh + 1) & qm;
      t++;
      const mn = qv[qh];
      bsum += mn - box[bpos]; box[bpos] = mn; bpos = bpos + 1 === L ? 0 : bpos + 1;
      const g = bsum / L;
      if (g < env) env = g; else if (env < 1) { env += (g - env) * rel; if (env > 0.99999) env = 1; }
      // delay line
      dl0[dpos] = xl; dl1[dpos] = xr;
      const r = (dpos - D) & mask; dpos = (dpos + 1) & mask;
      const yl = dl0[r] * env, yr = dl1[r] * env;
      oL[i] = yl; if (oR) oR[i] = yr;
      if (env < grBlock) grBlock = env;
      if (this.metering) momE += this.kf.sq(yl, 0) + this.kf.sq(yr, 1);
    }
    if ((t & 0xffff) < n) { bsum = 0; for (let i = 0; i < L; i++) bsum += box[i]; }   // float drift guard
    this.hpos = hp; this.dpos = dpos; this.qh = qh; this.qt = qt; this.t = t; this.bpos = bpos; this.bsum = bsum; this.env = env;
    if (this.metering) {
      const e = momE / n;
      this.momAcc += e - this.mom[this.momPos]; this.mom[this.momPos] = e; this.momPos = (this.momPos + 1) % this.mom.length;
      this.shortAcc += e - this.short[this.shortPos]; this.short[this.shortPos] = e; this.shortPos = (this.shortPos + 1) % this.short.length;
      if (tpBlock > this.tpHold) this.tpHold = tpBlock;
      if (grBlock < this.grMin) this.grMin = grBlock;
      if (++this.frames >= this.reportEvery) {
        this.frames = 0;
        this.port.postMessage({
          momentary: lufsOf(this.momAcc / this.mom.length), short: lufsOf(this.shortAcc / this.short.length),
          truePeak: this.tpHold > 0 ? 20 * Math.log10(this.tpHold) : -Infinity, reduction: 20 * Math.log10(this.grMin)
        });
        this.grMin = 1;
      }
    }
    return true;
  }
}
registerProcessor('soncle-master', MasterProcessor);

class MeterProcessor extends AudioWorkletProcessor {
  constructor() {
    super();
    this.kf = new KFilter(sampleRate);
    this.int = new Integrator(sampleRate);
    this.on = false; this.token = 0; this.frames = 0; this.every = Math.round(sampleRate / 128);   // report once a second
    this.port.onmessage = (e) => {
      const m = e.data || {};
      if (m.reset) { this.int.reset(); this.kf = new KFilter(sampleRate); this.token = m.token || 0; this.on = !!m.on; this.frames = 0; }
      else if (m.on != null) this.on = !!m.on;
    };
  }
  process(inputs) {
    if (!this.on) return true;
    const inp = inputs[0];
    if (!inp || !inp[0]) return true;
    const l = inp[0], r = inp[1] || inp[0], n = l.length, kf = this.kf, it = this.int;
    for (let i = 0; i < n; i++) it.add(kf.sq(l[i], 0) + kf.sq(r[i], 1));
    if (++this.frames >= this.every) {
      this.frames = 0;
      this.port.postMessage({ token: this.token, lufs: it.value(), seconds: it.count / 10 });
    }
    return true;
  }
}
registerProcessor('soncle-meter', MeterProcessor);
