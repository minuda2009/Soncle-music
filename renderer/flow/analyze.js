// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// Tempo (BPM), musical key (→ Camelot) and energy from ~30 s of mono PCM.
// Pure JS so it runs in a Worker (and in node for tests). No external data.

function fft(re, im) {
  const n = re.length;
  for (let i = 1, j = 0; i < n; i++) {
    let bit = n >> 1;
    for (; j & bit; bit >>= 1) j ^= bit;
    j ^= bit;
    if (i < j) { let t = re[i]; re[i] = re[j]; re[j] = t; t = im[i]; im[i] = im[j]; im[j] = t; }
  }
  for (let len = 2; len <= n; len <<= 1) {
    const ang = (-2 * Math.PI) / len, wr = Math.cos(ang), wi = Math.sin(ang), half = len >> 1;
    for (let i = 0; i < n; i += len) {
      let cr = 1, ci = 0;
      for (let k = 0; k < half; k++) {
        const a = i + k, b = a + half;
        const xr = re[b] * cr - im[b] * ci, xi = re[b] * ci + im[b] * cr;
        re[b] = re[a] - xr; im[b] = im[a] - xi; re[a] += xr; im[a] += xi;
        const ncr = cr * wr - ci * wi; ci = cr * wi + ci * wr; cr = ncr;
      }
    }
  }
}

const NAMES = ['C', 'C#', 'D', 'D#', 'E', 'F', 'F#', 'G', 'G#', 'A', 'A#', 'B'];
// Krumhansl–Kessler key profiles
const PROFILES = {
  kk: [[6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88], [6.33, 2.68, 3.52, 5.38, 2.6, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17]],
  temperley: [[0.748, 0.06, 0.488, 0.082, 0.67, 0.46, 0.096, 0.715, 0.104, 0.366, 0.057, 0.4], [0.712, 0.084, 0.474, 0.618, 0.049, 0.46, 0.105, 0.747, 0.404, 0.067, 0.133, 0.33]],
  albrecht: [[0.238, 0.006, 0.111, 0.006, 0.137, 0.094, 0.016, 0.214, 0.009, 0.08, 0.008, 0.081], [0.22, 0.006, 0.104, 0.123, 0.019, 0.103, 0.012, 0.214, 0.062, 0.022, 0.061, 0.052]]
};
export const opts = { profile: 'kk', harmonics: 1, whiten: false };   // best on the synthetic key sweep (test/flow.test.mjs)
let MAJOR = PROFILES.kk[0], MINOR = PROFILES.kk[1];
// Camelot wheel numbers by tonic pitch class
const CAMELOT_MAJOR = [8, 3, 10, 5, 12, 7, 2, 9, 4, 11, 6, 1];
const CAMELOT_MINOR = [5, 12, 7, 2, 9, 4, 11, 6, 1, 8, 3, 10];
export const camelotOf = (tonic, minor) => (minor ? CAMELOT_MINOR[tonic] + 'A' : CAMELOT_MAJOR[tonic] + 'B');
export const keyName = (tonic, minor) => NAMES[tonic] + (minor ? 'm' : '');

export function __keyChroma(pcm, sr) { return keyChroma(pcm, sr); }
function keyChroma(pcm, sr) {
  const N = 4096, HOP = 2048, half = N / 2;
  const frames = Math.floor((pcm.length - N) / HOP);
  if (frames < 8) return null;
  const win = new Float64Array(N);
  for (let i = 0; i < N; i++) win[i] = 0.5 - 0.5 * Math.cos((2 * Math.PI * i) / (N - 1));
  const re = new Float64Array(N), im = new Float64Array(N), mag = new Float64Array(half), env = new Float64Array(half);
  const binHz = sr / N, lo = Math.floor(100 / binHz), hi = Math.min(half - 2, Math.ceil(2500 / binHz));
  // semitone position of every bin
  const semi = new Float64Array(half);
  for (let k = lo; k <= hi; k++) semi[k] = 12 * Math.log2((k * binHz) / 440) + 69;
  const out = new Float64Array(12);
  for (let f = 0; f < frames; f++) {
    const o = f * HOP;
    let e = 0;
    for (let i = 0; i < N; i++) { const x = pcm[o + i]; re[i] = x * win[i]; im[i] = 0; e += x * x; }
    if (e / N < 1e-6) continue;
    fft(re, im);
    for (let k = 1; k < half; k++) mag[k] = Math.sqrt(Math.sqrt(re[k] * re[k] + im[k] * im[k]));   // compressed magnitude
    // spectral envelope: moving average over ±1/3 octave → whitening
    for (let k = lo; k <= hi; k++) {
      const w = Math.max(2, Math.round(k * 0.12));
      let a = 0, c = 0;
      for (let j = Math.max(1, k - w); j <= Math.min(half - 1, k + w); j++) { a += mag[j]; c++; }
      env[k] = a / c;
    }
    const fc = new Float64Array(12);
    for (let k = lo + 1; k < hi; k++) {
      const m = mag[k];
      if (m <= mag[k - 1] || m < mag[k + 1] || (opts.whiten && m < env[k] * 1.3)) continue;      // spectral peaks only
      // interpolate the true peak position between bins
      const a = mag[k - 1], b = m, c = mag[k + 1], den = a - 2 * b + c;
      const p = den ? 0.5 * (a - c) / den : 0;
      const st0 = 12 * Math.log2(((k + p) * binHz) / 440) + 69;
      const amp = opts.whiten ? m / env[k] - 1 : m;
      // HPCP-style: a peak may be the 2nd–4th harmonic of a lower note, so it also votes (less) for
      // those fundamentals — this cancels the fifth/third bias harmonics otherwise create.
      for (let hN = 1; hN <= opts.harmonics; hN++) {
        const st = st0 - 12 * Math.log2(hN);
        const nearest = Math.round(st), dev = st - nearest;
        if (Math.abs(dev) > 0.5) continue;
        const wgt = Math.cos((Math.abs(dev) / 0.5) * Math.PI / 2) ** 2 * Math.pow(0.6, hN - 1);
        fc[((nearest % 12) + 12) % 12] += amp * wgt;
      }
    }
    let mx = 0; for (let i = 0; i < 12; i++) mx = Math.max(mx, fc[i]);
    if (mx > 0) for (let i = 0; i < 12; i++) out[i] += fc[i] / mx;
  }
  return out;
}

function corr(a, b) {
  const n = a.length; let ma = 0, mb = 0;
  for (let i = 0; i < n; i++) { ma += a[i]; mb += b[i]; }
  ma /= n; mb /= n;
  let num = 0, da = 0, db = 0;
  for (let i = 0; i < n; i++) { const x = a[i] - ma, y = b[i] - mb; num += x * y; da += x * x; db += y * y; }
  return num / Math.sqrt(da * db || 1);
}

/**
 * @param {Float32Array} pcm mono samples
 * @param {number} sr sample rate (22050 recommended)
 */
export function analyze(pcm, sr) {
  [MAJOR, MINOR] = PROFILES[opts.profile] || PROFILES.kk;
  const N = 2048, HOP = 512;
  const frames = Math.floor((pcm.length - N) / HOP);
  if (frames < 40) return null;
  const win = new Float32Array(N);
  for (let i = 0; i < N; i++) win[i] = 0.5 - 0.5 * Math.cos((2 * Math.PI * i) / (N - 1));
  const re = new Float64Array(N), im = new Float64Array(N);
  const half = N / 2;
  // pitch-class map for bins 55 Hz..4.2 kHz
  const pc = new Int8Array(half).fill(-1);
  for (let k = 1; k < half; k++) {
    const f = (k * sr) / N;
    if (f < 55 || f > 4200) continue;
    const midi = 69 + 12 * Math.log2(f / 440);
    pc[k] = ((Math.round(midi) % 12) + 12) % 12;
  }
  // bins per pitch class (octaves above ~1 kHz have many bins) — used to flatten the bias
  const binCount = new Float64Array(12);
  for (let k = 1; k < half; k++) if (pc[k] >= 0) binCount[pc[k]]++;
  const chroma = new Float64Array(12);
  const flux = new Float32Array(frames);
  let prev = new Float32Array(half), cur = new Float32Array(half);
  let rmsSum = 0, loudFrames = 0, centroidSum = 0;
  for (let f = 0; f < frames; f++) {
    const o = f * HOP;
    let e = 0;
    for (let i = 0; i < N; i++) { const x = pcm[o + i]; re[i] = x * win[i]; im[i] = 0; e += x * x; }
    const rms = Math.sqrt(e / N);
    fft(re, im);
    let fl = 0, magSum = 0, cen = 0;
    const fc = new Float64Array(12);
    for (let k = 1; k < half; k++) {
      const m = Math.sqrt(re[k] * re[k] + im[k] * im[k]);
      const lm = Math.log1p(100 * m);
      cur[k] = lm;
      const d = lm - prev[k];
      if (d > 0) fl += d;
      magSum += m; cen += m * k;
      if (pc[k] >= 0) fc[pc[k]] += m;
    }
    flux[f] = fl;
    if (rms > 0.003) {
      // normalise each frame's chroma so loud frames don't dominate
      for (let i = 0; i < 12; i++) fc[i] /= binCount[i] || 1;
      let mx = 0; for (let i = 0; i < 12; i++) mx = Math.max(mx, fc[i]);
      if (mx > 0) for (let i = 0; i < 12; i++) chroma[i] += fc[i] / mx;
      rmsSum += rms; loudFrames++;
      centroidSum += magSum ? (cen / magSum) * (sr / N) : 0;
    }
    const t = prev; prev = cur; cur = t;
  }
  // ---- tempo: autocorrelation of the onset envelope (mean-removed, high-passed) ----
  const fps = sr / HOP;
  let mean = 0; for (let i = 0; i < frames; i++) mean += flux[i]; mean /= frames;
  const env = new Float32Array(frames);
  // subtract a local moving average (≈0.5 s) to keep only the pulses
  const W = Math.round(fps * 0.25);
  let acc = 0;
  const pre = new Float32Array(frames + 1);
  for (let i = 0; i < frames; i++) { acc += flux[i]; pre[i + 1] = acc; }
  for (let i = 0; i < frames; i++) {
    const a = Math.max(0, i - W), b = Math.min(frames, i + W + 1);
    env[i] = Math.max(0, flux[i] - (pre[b] - pre[a]) / (b - a));
  }
  const minLag = Math.floor((fps * 60) / 200), maxLag = Math.ceil((fps * 60) / 55);
  const ac = new Float64Array(maxLag + 2);
  for (let lag = minLag - 1; lag <= maxLag + 1; lag++) {
    let s = 0;
    for (let i = 0; i + lag < frames; i++) s += env[i] * env[i + lag];
    ac[lag] = s / (frames - lag);
  }
  // score each lag with its harmonics (1×, 2× and ½×) and a gentle prior around 120 BPM
  let best = -1, bestS = -Infinity, sumS = 0, cnt = 0;
  const score = (lag) => {
    const v = (l) => { const li = Math.round(l); return li >= minLag - 1 && li <= maxLag + 1 ? ac[li] : 0; };
    const bpm = (60 * fps) / lag;
    const prior = Math.exp(-0.5 * Math.pow(Math.log2(bpm / 120) / 0.9, 2));
    return (v(lag) + 0.5 * v(lag * 2) + 0.25 * v(lag / 2)) * (0.5 + 0.5 * prior);
  };
  for (let lag = minLag; lag <= maxLag; lag++) { const s = score(lag); sumS += s; cnt++; if (s > bestS) { bestS = s; best = lag; } }
  let bpm = null, bpmConf = 0;
  if (best > 0 && bestS > 0) {
    // parabolic refinement on the raw autocorrelation
    const y0 = ac[best - 1], y1 = ac[best], y2 = ac[best + 1];
    const den = y0 - 2 * y1 + y2;
    const shift = den ? Math.max(-0.5, Math.min(0.5, (0.5 * (y0 - y2)) / den)) : 0;
    bpm = (60 * fps) / (best + shift);
    while (bpm < 70) bpm *= 2;
    while (bpm > 180) bpm /= 2;
    // rhythmic strength: how much of the pulse energy repeats at the beat period
    let ac0 = 0; for (let i = 0; i < frames; i++) ac0 += env[i] * env[i]; ac0 /= frames;
    const periodic = ac0 ? ac[best] / ac0 : 0;
    const peaky = bestS / (sumS / cnt);
    bpmConf = Math.max(0, Math.min(1, ((periodic - 0.12) / 0.35) * Math.min(1, (peaky - 1) / 1.5)));
  }
  // ---- chroma: separate high-resolution pass (4096-pt FFT ≈ 5.4 Hz bins), spectral peaks only,
  // whitened against the local spectral envelope so bass/kick energy doesn't bias the key ----
  const chroma2 = keyChroma(pcm, sr);
  if (chroma2) for (let i = 0; i < 12; i++) chroma[i] = chroma2[i];
  // ---- key: correlate the average chroma with the 24 key profiles ----
  let key = null, keyConf = 0;
  if (loudFrames > 20) {
    const scores = [];
    for (let t = 0; t < 12; t++) {
      const rot = (p) => Array.from({ length: 12 }, (_, i) => p[(i - t + 12) % 12]);
      scores.push({ t, minor: false, r: corr(chroma, rot(MAJOR)) });
      scores.push({ t, minor: true, r: corr(chroma, rot(MINOR)) });
    }
    scores.sort((a, b) => b.r - a.r);
    const k = scores[0];
    key = { tonic: k.t, minor: k.minor, name: keyName(k.t, k.minor), camelot: camelotOf(k.t, k.minor) };
    // a flat chroma (noise, drums only) has no key: scale by how tonal the spectrum is
    let cm = 0; for (let i = 0; i < 12; i++) cm += chroma[i]; cm /= 12;
    let cv = 0; for (let i = 0; i < 12; i++) cv += (chroma[i] - cm) ** 2; cv = Math.sqrt(cv / 12) / (cm || 1);
    const tonal = Math.max(0, Math.min(1, (cv - 0.08) / 0.3));
    keyConf = Math.max(0, Math.min(1, ((k.r - scores[1].r) * 8 + (k.r - 0.5)) * tonal));
  }
  // ---- energy 0..1: loudness + brightness + onset density ----
  const rmsDb = loudFrames ? 20 * Math.log10(rmsSum / loudFrames) : -60;
  const bright = loudFrames ? centroidSum / loudFrames : 0;
  let onsetRate = 0; for (let i = 0; i < frames; i++) onsetRate += env[i] > 0 ? env[i] : 0; onsetRate /= frames;
  const energy = Math.max(0, Math.min(1, 0.45 * Math.min(1, Math.max(0, (rmsDb + 30) / 22)) + 0.3 * Math.min(1, bright / 3500) + 0.25 * Math.min(1, onsetRate / (mean || 1) * 0.5)));
  return {
    bpm: bpm ? Math.round(bpm * 10) / 10 : null, bpmConf: +bpmConf.toFixed(2),
    key: key?.name || null, camelot: key?.camelot || null, keyConf: +keyConf.toFixed(2),
    energy: +energy.toFixed(3), rmsDb: +rmsDb.toFixed(1), v: 2
  };
}

// Worker entry
if (typeof self !== 'undefined' && typeof self.postMessage === 'function' && typeof window === 'undefined') {
  self.onmessage = (e) => {
    const { id, pcm, sr } = e.data;
    try { self.postMessage({ id, result: analyze(pcm, sr) }); } catch (err) { self.postMessage({ id, error: err.message }); }
  };
}
