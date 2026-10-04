// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// Dual-deck audio engine: crossfade / DJ mix / gapless handover, equalizer + headphone correction,
// LUFS loudness normalisation, true-peak limiter (AudioWorklet), skip silence, tempo/pitch.
//
// Signal path, per deck:  media → norm (loudness) → bass (DJ swap shelf) → fade ─┐
//                                  └→ meter (LUFS, only for songs with unknown loudness)
// Shared:  pre → 10-band EQ → headphone correction → loudness comp → width/crossfeed → master
//          → duck → mono → balance → true-peak limiter (-1 dBTP, 2 ms look-ahead) → output
export const EQ_BANDS = [31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000];
export const EQ_PRESETS = {
  Flat: [0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
  'Bass boost': [6, 5, 4, 2, 0, 0, 0, 0, 0, 0],
  'Bass reducer': [-6, -5, -4, -2, 0, 0, 0, 0, 0, 0],
  'Treble boost': [0, 0, 0, 0, 0, 1, 2, 4, 5, 6],
  Vocal: [-2, -2, -1, 1, 3, 4, 3, 1, 0, -1],
  Rock: [5, 4, 2, -1, -2, -1, 2, 3, 4, 4],
  Pop: [-1, 1, 3, 4, 3, 0, -1, -1, 1, 2],
  Jazz: [3, 2, 1, 2, -1, -1, 0, 1, 2, 3],
  Classical: [4, 3, 2, 1, -1, -1, 0, 2, 3, 4],
  Electronic: [5, 4, 1, 0, -2, 2, 1, 1, 4, 5],
  'Hip-hop': [5, 4, 1, 3, -1, -1, 1, -1, 2, 3],
  Acoustic: [4, 3, 2, 1, 2, 2, 3, 3, 2, 1],
  Loudness: [6, 4, 0, 0, -2, 0, -1, -4, 5, 1],
  'Late night': [-3, -2, 0, 1, 2, 2, 1, 0, -2, -4],
  // Focus: warm and steady — a little low-end body, softened presence/sibilance so music sits behind your thoughts
  Focus: [1.5, 1.5, 1, 0, -0.5, -1, -2, -2.5, -2, -1.5],
  // From the user's 6-band curve (60/150/400/1k/2.4k/15k), smoothed and mapped to 10 bands
  'VC (Warm vocal)': [3.5, 4.0, 4.6, 2.9, 1.6, 2.8, 0.6, 0.3, 0.8, 1.2],
  // device-type starting points
  Earbuds: [3, 2.5, 1, 0, -0.5, 0, 1, 2, 2.5, 2],
  Headphones: [2, 1.5, 0.5, 0, -0.5, 0, 0.5, 1.5, 1, 0.5],
  'Bluetooth speaker': [-3, 1, 2, 1.5, 0.5, 1, 1.5, 1.5, 1, 0],
  'Laptop speakers': [-6, -3, 0, 1, 2, 3, 3, 3, 2, 1],
  Car: [4, 3, 1, -1, -1, 0, 1, 2, 3, 2],
  'TV / HDMI': [1, 1, 0, 0, 1, 2, 2, 1, 0, 0]
};
export const PRESET_PREAMP = { 'VC (Warm vocal)': -3, 'Bass boost': -3, Rock: -2, Electronic: -2, Loudness: -2, Car: -2, 'Hip-hop': -2 };
// Convert an arbitrary band spec [[hz, dB], ...] to our 10 bands (linear in log-frequency)
export function specToBands(spec) {
  const pts = [...spec].sort((a, b) => a[0] - b[0]);
  return EQ_BANDS.map((f) => {
    if (f <= pts[0][0]) return pts[0][1];
    if (f >= pts[pts.length - 1][0]) return pts[pts.length - 1][1];
    for (let i = 0; i < pts.length - 1; i++) {
      const [f0, g0] = pts[i], [f1, g1] = pts[i + 1];
      if (f >= f0 && f <= f1) { const x = Math.log2(f / f0) / Math.log2(f1 / f0); return +(g0 + (g1 - g0) * x).toFixed(1); }
    }
    return 0;
  });
}

const FADE = 0.14;

export class Engine extends EventTarget {
  constructor() {
    super();
    this.ctx = null;
    this.decks = [this.#makeDeck(0), this.#makeDeck(1)];
    this.a = 0;
    this.opts = {
      tempo: 1, varispeed: false, skipSilence: false, silenceInstant: false, normalize: true, volume: 0.8, muted: false, mono: false, balance: 0,
      // sound
      autoHeadroom: true,      // lower the preamp by the EQ's biggest boost so boosts never clip
      loudnessComp: false,     // equal-loudness bass/treble lift at low volume
      width: 1,                // stereo width: 0 = mono, 1 = original, 1.5 = wide
      crossfeed: 'off',        // headphone crossfeed: off | light | strong
      xfCurve: 'smooth',       // crossfade curve: smooth | equalpower | linear
      normTarget: -14          // loudness target in LUFS (-19 quiet · -14 normal · -11 loud)
    };
    this.corr = { enabled: true, profile: null };   // headphone correction (AutoEq parametric)
    this.limiter = 'none';                           // 'truepeak' once the worklet is running
    this.idleTimer = 0;
    this.sinkId = '';
    this.eq = { enabled: false, preamp: 0, gains: EQ_PRESETS.Flat.slice() };
    this.xfading = null;
    this.silentMs = 0;
    this.speeding = false;
    this.buf = null;
  }

  get deck() { return this.decks[this.a]; }
  get other() { return this.decks[1 - this.a]; }
  get el() { return this.deck.el; }
  get currentTime() { return this.el.currentTime || 0; }
  set currentTime(t) { this.seek(t); }
  get duration() {
    const d = this.el.duration, md = this.deck.track?.duration || 0;
    // A media duration far shorter than the song's real length means the stream reported a
    // partial size; trust the metadata so crossfade / silence-skip do not fire early.
    if (isFinite(d) && d > 0) return md && d < md * 0.8 ? md : d;
    return md;
  }
  get paused() { return this.el.paused; }
  get buffered() { return this.el.buffered; }
  get track() { return this.deck.track; }

  #makeDeck(i) {
    const el = new Audio();
    el.crossOrigin = 'anonymous';
    el.preload = 'auto';
    const d = { i, el, track: null, src: null, norm: null, bass: null, fade: null, analyser: null, meter: null, token: 0, lufs: null, measured: null };
    const fwd = (type) => el.addEventListener(type, (ev) => {
      if (this.decks[this.a] !== d) return;
      this.dispatchEvent(new CustomEvent(type, { detail: { deck: d, error: el.error } }));
    });
    ['playing', 'pause', 'waiting', 'canplay', 'ended', 'error', 'loadedmetadata', 'seeked', 'durationchange'].forEach(fwd);
    el.addEventListener('ended', () => this.#sleepSoon());   // e.g. the queue ran out: let the audio thread sleep
    return d;
  }

  ensureGraph() {
    if (this.ctx) { this.#wake(); return; }
    const ctx = (this.ctx = new AudioContext({ latencyHint: 'playback' }));
    this.pre = ctx.createGain();
    this.filters = EQ_BANDS.map((f, i) => {
      const b = ctx.createBiquadFilter();
      b.type = i === 0 ? 'lowshelf' : i === EQ_BANDS.length - 1 ? 'highshelf' : 'peaking';
      b.frequency.value = f;
      b.Q.value = 1.41;
      return b;
    });
    // headphone correction sits between the user's EQ and everything else (rebuilt per profile)
    this.corrIn = ctx.createGain(); this.corrOut = ctx.createGain(); this.corrFilters = [];
    // loudness compensation shelves (driven by the volume level)
    this.lcLow = ctx.createBiquadFilter(); this.lcLow.type = 'lowshelf'; this.lcLow.frequency.value = 100;
    this.lcHigh = ctx.createBiquadFilter(); this.lcHigh.type = 'highshelf'; this.lcHigh.frequency.value = 9000;
    // stereo width + headphone crossfeed (one 2x2 matrix, crossfeed paths low-passed and delayed like bs2b)
    const split = ctx.createChannelSplitter(2), merge = ctx.createChannelMerger(2);
    this.mx = { ll: ctx.createGain(), rr: ctx.createGain(), lr: ctx.createGain(), rl: ctx.createGain(), xl: ctx.createGain(), xr: ctx.createGain(), out: ctx.createGain() };
    const lpL = ctx.createBiquadFilter(), lpR = ctx.createBiquadFilter();
    for (const lp of [lpL, lpR]) { lp.type = 'lowpass'; lp.frequency.value = 700; lp.Q.value = 0.5; }
    const dL = ctx.createDelay(0.01), dR = ctx.createDelay(0.01);
    dL.delayTime.value = dR.delayTime.value = 0.00028;
    split.connect(this.mx.ll, 0).connect(merge, 0, 0);
    split.connect(this.mx.rl, 1).connect(merge, 0, 0);
    lpR.connect(dR).connect(this.mx.xl).connect(merge, 0, 0);
    split.connect(this.mx.rr, 1).connect(merge, 0, 1);
    split.connect(this.mx.lr, 0).connect(merge, 0, 1);
    lpL.connect(dL).connect(this.mx.xr).connect(merge, 0, 1);
    merge.connect(this.mx.out);
    this.split = split; this.lp = [lpL, lpR]; this.xfOn = false;
    this.master = ctx.createGain();
    this.duckNode = ctx.createGain();   // smart ducking (other apps playing sound)
    this.monoNode = ctx.createGain();
    this.panner = ctx.createStereoPanner();
    // Fallback limiter until the AudioWorklet is ready (or if it can't load): Chrome's compressor as a
    // hard-knee peak limiter; its automatic make-up gain is cancelled with a matching trim.
    this.comp = ctx.createDynamicsCompressor();
    this.comp.threshold.value = -0.5; this.comp.knee.value = 0; this.comp.ratio.value = 20; this.comp.attack.value = 0.001; this.comp.release.value = 0.08;
    const fullRange = Math.pow(10, (-0.5 + 0.5 / 20) / 20);
    this.trim = ctx.createGain(); this.trim.gain.value = 1 / Math.pow(1 / fullRange, 0.6);
    for (let i = 0; i < this.filters.length - 1; i++) this.filters[i].connect(this.filters[i + 1]);
    this.lcLow.connect(this.lcHigh);
    this.#wireCorrection();
    this.mx.out.connect(this.master).connect(this.duckNode).connect(this.monoNode).connect(this.panner).connect(this.comp).connect(this.trim).connect(ctx.destination);
    this.#applySpace();
    this.#applyChannel();
    if (this.sinkId) this.setSink(this.sinkId).catch(() => {});
    for (const d of this.decks) {
      d.src = ctx.createMediaElementSource(d.el);
      d.norm = ctx.createGain();
      d.bass = ctx.createBiquadFilter(); d.bass.type = 'lowshelf'; d.bass.frequency.value = 180; d.bass.gain.value = 0;
      d.fade = ctx.createGain();
      d.analyser = ctx.createAnalyser();
      d.analyser.fftSize = 1024;
      d.src.connect(d.norm).connect(d.bass).connect(d.fade).connect(this.pre);
      // Mirror the signal into the analyser without routing the audible output
      // through it (an AnalyserNode with no downstream sink can mute the graph).
      d.norm.connect(d.analyser);
    }
    this.buf = new Float32Array(1024);
    this.applyEq();
    this.applyVolume();
    this.ready = this.#loadWorklets();
  }

  // Only stages that change the sound are in the signal path: with the EQ, headphone correction,
  // loudness compensation, width and crossfeed at neutral the audio thread skips them entirely
  // (a biquad at 0 dB still costs CPU). Called whenever one of those is switched on or off.
  #rewire() {
    if (!this.pre) return;
    const eqOn = this.eq.enabled && !this.bypass && this.eq.gains.some((g) => Math.abs(g) > 0.01);
    const corrOn = this.corrFilters.length > 0;
    const lcOn = !!this.opts.loudnessComp && !this.bypass;
    const xf = this.bypass ? 0 : { off: 0, light: 0.35, strong: 0.55 }[this.opts.crossfeed] || 0;
    const w = this.bypass ? 1 : this.opts.width ?? 1;
    const spaceOn = xf > 0 || Math.abs(w - 1) > 0.001;
    const key = `${eqOn}${corrOn}${lcOn}${spaceOn}${xf > 0}`;
    if (key === this.wired) return;
    this.wired = key;
    for (const n of [this.pre, this.filters.at(-1), this.corrOut, this.lcHigh]) n.disconnect();
    let tail = this.pre;
    if (eqOn) { tail.connect(this.filters[0]); tail = this.filters.at(-1); }
    if (corrOn) { tail.connect(this.corrIn); tail = this.corrOut; }
    if (lcOn) { tail.connect(this.lcLow); tail = this.lcHigh; }
    tail.connect(spaceOn ? this.split : this.mx.out);
    if ((xf > 0) !== this.xfOn) {
      this.xfOn = xf > 0;
      if (this.xfOn) { this.split.connect(this.lp[0], 0); this.split.connect(this.lp[1], 1); }
      else { this.split.disconnect(this.lp[0], 0); this.split.disconnect(this.lp[1], 1); }
    }
    if (!spaceOn) this.mx.out.gain.setTargetAtTime(1, this.ctx.currentTime, 0.02);
  }

  // True-peak limiter + LUFS meters run in an AudioWorklet; swapped in as soon as it has loaded.
  async #loadWorklets() {
    const ctx = this.ctx;
    try {
      await ctx.audioWorklet.addModule(new URL('./audio/worklets.js', import.meta.url).href);
      const lim = new AudioWorkletNode(ctx, 'soncle-master', { numberOfInputs: 1, numberOfOutputs: 1, outputChannelCount: [2], channelCount: 2, channelCountMode: 'explicit' });
      lim.port.postMessage({ ceilingDb: -1, meters: !!this.onMeters });
      lim.port.onmessage = (e) => this.onMeters?.(e.data);
      this.panner.disconnect();
      this.panner.connect(lim).connect(ctx.destination);
      this.comp.disconnect(); this.trim.disconnect();
      this.lim = lim;
      this.limiter = 'truepeak';
      for (const d of this.decks) {
        d.meter = new AudioWorkletNode(ctx, 'soncle-meter', { numberOfInputs: 1, numberOfOutputs: 0, channelCount: 2, channelCountMode: 'explicit' });
        d.src.connect(d.meter);
        d.meter.port.onmessage = (e) => this.#onDeckLoudness(d, e.data);
        if (d.track) this.#startMeter(d);
      }
      for (const d of this.decks) this.#applyNorm(d);   // quiet songs may now be lifted (the limiter catches peaks)
    } catch (e) {
      this.limiter = 'compressor';
      console.warn('audio worklet unavailable, using the fallback limiter:', e?.message || e);
    }
  }
  // Keep the audio thread asleep while nothing plays (paused for a while): no CPU, no open stream.
  async #wake() {
    clearTimeout(this.idleTimer);
    const c = this.ctx;
    if (!c) return;
    if (this.suspending) await this.suspending;   // a suspend in flight must finish before resuming
    if (c.state === 'suspended') await c.resume().catch(() => {});
  }
  #sleepSoon() {
    clearTimeout(this.idleTimer);
    this.idleTimer = setTimeout(() => {
      if (this.ctx && this.ctx.state === 'running' && this.decks.every((d) => d.el.paused)) {
        this.suspending = this.ctx.suspend().catch(() => {}).finally(() => { this.suspending = null; });
      }
    }, 12000);
  }
  /** A/B: hear the music without EQ, headphone correction, width, crossfeed and loudness comp. */
  setBypass(on) {
    this.bypass = !!on;
    if (!this.ctx) return;
    this.#wireCorrection(); this.#applySpace(); this.#applyLoudnessComp();
  }
  /** Live output meters (momentary/short-term LUFS, true peak, gain reduction) ~15×/s, or null to stop. */
  setMeters(cb) {
    this.onMeters = cb || null;
    this.lim?.port.postMessage({ meters: !!cb });
  }

  // ---------- settings ----------
  setOptions(o) {
    Object.assign(this.opts, o);
    for (const d of this.decks) this.#applyRate(d);
    this.applyVolume();
    for (const d of this.decks) this.#applyNorm(d);
  }
  #applyRate(d) {
    d.el.preservesPitch = !this.opts.varispeed;
    d.el.defaultPlaybackRate = this.opts.tempo;   // survives src changes (setting src resets playbackRate)
    if (!this.speeding || d !== this.deck) d.el.playbackRate = this.opts.tempo;
  }
  applyVolume() {
    if (!this.master) { for (const d of this.decks) d.el.volume = this.opts.muted ? 0 : this.opts.volume ** 2; return; }
    for (const d of this.decks) d.el.volume = 1;
    const v = this.opts.muted ? 0 : this.opts.volume ** 2, g = this.master.gain, t = this.ctx.currentTime;
    // cancel a pending sleep-timer fade, otherwise it keeps ramping to silence
    if (g.cancelAndHoldAtTime) g.cancelAndHoldAtTime(t); else { g.cancelScheduledValues(t); g.setValueAtTime(g.value, t); }
    g.setTargetAtTime(v, t, 0.02);
    this.#applyLoudnessComp();
  }
  #applyLoudnessComp() {
    if (!this.lcLow) return;
    // attenuation of the volume slider in dB (volume is squared → ~40 dB range)
    const att = this.opts.muted ? 0 : -20 * Math.log10(Math.max(0.01, this.opts.volume ** 2));
    const on = this.opts.loudnessComp && !this.bypass;
    const bass = on ? Math.min(9, att * 0.32) : 0, treble = on ? Math.min(3.5, att * 0.12) : 0;
    const t = this.ctx.currentTime;
    this.lcLow.gain.setTargetAtTime(bass, t, 0.1);
    this.lcHigh.gain.setTargetAtTime(treble, t, 0.1);
    this.lcBoost = Math.max(bass, treble);
    this.applyEq();
  }
  #applySpace() {
    if (!this.mx) return;
    const w = this.bypass ? 1 : Math.max(0, Math.min(2, this.opts.width ?? 1));
    const direct = (1 + w) / 2, cross = (1 - w) / 2;
    const c = this.bypass ? 0 : { off: 0, light: 0.35, strong: 0.55 }[this.opts.crossfeed] || 0;
    const t = this.ctx.currentTime, set = (g, v) => g.gain.setTargetAtTime(v, t, 0.05);
    set(this.mx.ll, direct); set(this.mx.rr, direct); set(this.mx.lr, cross); set(this.mx.rl, cross);
    set(this.mx.xl, c); set(this.mx.xr, c);
    // keep the loudest (low, in-phase) part from getting louder: normalise by the worst-case sum
    set(this.mx.out, 1 / Math.max(1, Math.abs(direct) + Math.abs(cross) + c));
    this.#rewire();
  }
  setSound(o) {
    Object.assign(this.opts, o);
    if (!this.ctx) return;
    this.#applySpace();
    this.#applyLoudnessComp();
  }
  // Loudness normalisation to a LUFS target. Known loudness (YouTube / ReplayGain) applies at once;
  // unknown songs are measured while they play and eased in after a few seconds. Quiet songs are only
  // lifted when the true-peak limiter is running, so a boost can never clip.
  #applyNorm(d, tau = 0.05) {
    if (!d.norm) return;
    const lufs = d.lufs ?? d.measured;
    let db = 0;
    if (this.opts.normalize && lufs != null && isFinite(lufs)) {
      db = this.opts.normTarget - lufs;
      db = Math.max(-20, Math.min(this.limiter === 'truepeak' ? 6 : 0, db));
    }
    d.normDb = db;
    d.norm.gain.setTargetAtTime(Math.pow(10, db / 20), this.ctx.currentTime, tau);
  }
  #startMeter(d) {
    d.measured = null; d.reported = false;
    d.meter?.port.postMessage({ reset: true, token: d.token, on: d.lufs == null && !!d.track });
  }
  #onDeckLoudness(d, m) {
    if (m.token !== d.token || m.lufs == null || d.lufs != null) return;
    if (m.seconds >= 6) {
      const first = d.measured == null;
      d.measured = m.lufs;
      if (first || Math.abs((d.lastApplied ?? m.lufs) - m.lufs) > 0.5) { d.lastApplied = m.lufs; this.#applyNorm(d, 1.2); }
    }
    if (m.seconds >= 40 && !d.reported) {
      d.reported = true;
      d.meter.port.postMessage({ on: false });   // enough to remember it; stop measuring
      this.dispatchEvent(new CustomEvent('loudness', { detail: { track: d.track, lufs: +m.lufs.toFixed(2) } }));
    }
  }
  // ---------- headphone correction ----------
  /** profile: { name, preamp, filters: [{ type: 'PK'|'LSC'|'HSC', fc, gain, q }] } or null */
  setCorrection(profile, enabled = this.corr.enabled) {
    this.corr = { profile: profile || null, enabled: !!enabled };
    this.twinsCorr = null;
    if (this.ctx) { this.#wireCorrection(); this.applyEq(); }
  }
  #wireCorrection() {
    const ctx = this.ctx;
    this.corrIn.disconnect();
    for (const f of this.corrFilters) f.disconnect();
    this.corrFilters = [];
    const p = this.corr.enabled && !this.bypass ? this.corr.profile : null;
    const ny = ctx.sampleRate / 2 - 100;
    let node = this.corrIn;
    for (const f of p?.filters || []) {
      if (!(f.fc > 10 && f.fc < ny) || !isFinite(f.gain)) continue;
      const b = ctx.createBiquadFilter();
      b.type = f.type === 'LSC' ? 'lowshelf' : f.type === 'HSC' ? 'highshelf' : 'peaking';
      b.frequency.value = f.fc; b.gain.value = f.gain; b.Q.value = f.q || 0.707;
      node.connect(b); node = b;
      this.corrFilters.push(b);
    }
    node.connect(this.corrOut);
    this.corrIn.gain.value = p ? Math.pow(10, (p.preamp || 0) / 20) : 1;
    this.#rewire();
  }
  /** Magnitude response (dB) of the headphone correction alone, for drawing it. */
  correctionResponse(freqs) {
    const mags = new Float32Array(freqs.length).fill(0);
    if (!this.ctx || !this.corrFilters.length) return mags;
    const m = new Float32Array(freqs.length), ph = new Float32Array(freqs.length);
    for (const f of this.corrFilters) { f.getFrequencyResponse(freqs, m, ph); for (let i = 0; i < freqs.length; i++) mags[i] += 20 * Math.log10(m[i] || 1e-6); }
    const pre = this.corr.profile?.preamp || 0;
    for (let i = 0; i < freqs.length; i++) mags[i] += pre;
    return mags;
  }
  setEq(eq) { Object.assign(this.eq, eq); this.applyEq(); }
  applyEq() {
    if (!this.filters) return;
    const t = this.ctx.currentTime;
    const on = this.eq.enabled && !this.bypass;
    this.filters.forEach((f, i) => f.gain.setTargetAtTime(on ? this.eq.gains[i] || 0 : 0, t, 0.03));
    this.pre.gain.setTargetAtTime(this.bypass ? 1 : Math.pow(10, this.preampDb() / 20), t, 0.03);
    this.#rewire();
  }
  // Effective preamp: the user's preamp, minus the curve's biggest boost when auto-headroom is on.
  headroomDb() {
    if (!this.opts.autoHeadroom) return 0;
    let peak = this.lcBoost || 0;
    if (this.eq.enabled && this.filters) {
      const n = 96, f = new Float32Array(n);
      for (let i = 0; i < n; i++) f[i] = 20 * Math.pow(1000, i / (n - 1));
      const r = this.eqResponse(f);
      peak = Math.max(0, ...r) + (this.lcBoost || 0) * 0.5;
    }
    return -Math.max(0, peak + (this.eq.enabled ? this.eq.preamp || 0 : 0));
  }
  preampDb() { return (this.eq.enabled ? this.eq.preamp || 0 : 0) + this.headroomDb(); }
  eqResponse(freqs) {
    // magnitude response (dB) of the target EQ curve; uses unconnected twin filters so ramps don't matter
    const mags = new Float32Array(freqs.length).fill(0);
    if (!this.filters) return mags;
    if (!this.twins) this.twins = this.filters.map((f) => { const b = this.ctx.createBiquadFilter(); b.type = f.type; b.frequency.value = f.frequency.value; b.Q.value = f.Q.value; return b; });
    const m = new Float32Array(freqs.length), p = new Float32Array(freqs.length);
    for (let i = 0; i < this.twins.length; i++) {
      const f = this.twins[i];
      f.gain.value = this.eq.enabled ? this.eq.gains[i] || 0 : 0;
      f.getFrequencyResponse(freqs, m, p);
      for (let i = 0; i < freqs.length; i++) mags[i] += 20 * Math.log10(m[i] || 1e-6);
    }
    return mags;
  }

  // ---------- transport ----------
  // lufs: integrated loudness if known (YouTube: -14 + loudnessDb; ReplayGain: -18 - gain).
  // `loudness` (YouTube loudnessDb) is still accepted.
  #prime(d, track, { src, lufs, loudness }) {
    d.token++;
    d.track = track;
    d.leadDone = false;
    d.lufs = lufs != null ? lufs : loudness != null ? -14 + loudness : null;
    d.lastApplied = null;
    this.#startMeter(d);   // clears the previous song's measurement first
    this.#applyNorm(d);
    this.#applyRate(d);
    this.#resetBass(d);
    d.el.src = src;
    d.el.playbackRate = this.opts.tempo;
  }
  #resetBass(d) { if (d.bass) { d.bass.gain.cancelScheduledValues(this.ctx.currentTime); d.bass.gain.setValueAtTime(0, this.ctx.currentTime); } }

  async load(track, { src, lufs, loudness, startAt = 0, autoplay = true } = {}) {
    this.ensureGraph();
    this.cancelCrossfade();
    const d = this.deck;
    this.#stopDeck(this.other);
    this.speeding = false;
    this.silentMs = 0;
    d.fade.gain.cancelScheduledValues(this.ctx.currentTime);
    d.fade.gain.setValueAtTime(0, this.ctx.currentTime);
    this.#prime(d, track, { src, lufs, loudness });
    const tok = d.token;
    if (startAt) {
      await new Promise((res, rej) => {
        const ok = () => { d.el.removeEventListener('error', bad); res(); };
        const bad = () => { d.el.removeEventListener('loadedmetadata', ok); rej(d.el.error || new Error('load failed')); };
        d.el.addEventListener('loadedmetadata', ok, { once: true });
        d.el.addEventListener('error', bad, { once: true });
      });
      if (d.token !== tok) return;   // another track was loaded meanwhile
      d.el.currentTime = startAt;
    }
    if (autoplay && d.token === tok) return this.play();
  }

  async play() {
    this.ensureGraph();
    if (this.limiter === 'none') await Promise.race([this.ready, new Promise((r) => setTimeout(r, 1500))]);
    await this.#wake();
    const d = this.deck;
    if (!d.el.src) return;
    const g = d.fade.gain, t = this.ctx.currentTime;
    d.pausing = false;
    d.wantPause = false;
    g.cancelScheduledValues(t);
    g.setValueAtTime(g.value, t);
    g.linearRampToValueAtTime(1, t + FADE);
    const tok = d.token;
    try {
      await d.el.play();
    } catch (e) {
      // AbortError: something paused the element before play() settled. If that was us (the
      // user paused, or another track was loaded), it isn't an error. If it wasn't (Android's
      // WebView pauses media on its own, e.g. on an audio-focus change while a slow stream is
      // still starting), try once more.
      if (e?.name !== 'AbortError') throw e;
      if (d.token !== tok || d.wantPause) return;
      await new Promise((r) => setTimeout(r, 300));
      if (d.token !== tok || d.wantPause || !d.el.paused) return;
      await d.el.play();
    }
  }

  pause({ fade = FADE } = {}) {
    const d = this.deck;
    d.wantPause = true;
    this.#sleepSoon();
    if (!this.ctx || d.el.paused) return d.el.pause();
    const g = d.fade.gain, t = this.ctx.currentTime;
    g.cancelScheduledValues(t);
    g.setValueAtTime(g.value, t);
    g.linearRampToValueAtTime(0, t + fade);
    d.pausing = true;
    const tok = d.token;
    setTimeout(() => { if (d.token === tok && d.pausing) { d.el.pause(); d.pausing = false; } }, fade * 1000 + 20);
    // fire pause state immediately so the UI feels instant
    this.dispatchEvent(new CustomEvent('pause', { detail: { deck: d } }));
    if (this.xfading) { this.#stopDeck(this.other); this.#resetBass(d); }
  }

  seek(t) {
    const d = this.deck;
    if (!isFinite(t)) return;
    d.el.currentTime = Math.max(0, Math.min(t, (this.duration || t) - 0.05));
  }

  stop() { for (const d of this.decks) this.#stopDeck(d); this.#sleepSoon(); }

  #stopDeck(d) {
    d.token++;
    d.el.pause();
    d.el.removeAttribute('src');
    d.el.load();
    d.track = null;
    d.pausing = false;
    d.meter?.port.postMessage({ on: false });
    if (this.ctx) this.#resetBass(d);
  }

  // Start the other deck on `track` and return once it is audibly playing (or throw).
  async #startOther(track, opts) {
    this.ensureGraph();
    await this.#wake();
    this.cancelCrossfade();
    const from = this.deck, to = this.other;
    const t0 = this.ctx.currentTime;
    to.fade.gain.cancelScheduledValues(t0);
    to.fade.gain.setValueAtTime(0, t0);
    this.#prime(to, track, opts);
    const toTok = to.token, fromTok = from.token;
    if (opts.waitUntil) await opts.waitUntil(to);
    try {
      if (to.token !== toTok) throw new Error('crossfade cancelled');
      await to.el.play();
    } catch (e) {
      if (to.token === toTok) this.#stopDeck(to);
      throw e;
    }
    // paused, or something else loaded, while the next song was buffering
    if (to.token !== toTok || from.token !== fromTok || from.pausing || from.el.paused) {
      if (to.token === toTok) this.#stopDeck(to);
      // the old song simply ran out while the new one was buffering: not a pause, keep playing
      throw new Error(from.el.ended && from.token === fromTok ? 'too late' : 'crossfade cancelled');
    }
    return { from, to };
  }
  #handOver(from, to, seconds) {
    this.a = to.i;
    this.speeding = false;
    this.silentMs = 0;
    const tok = from.token;
    this.xfading = { from, timer: setTimeout(() => { if (from.token === tok) this.#stopDeck(from); this.xfading = null; }, seconds * 1000 + 80) };
    this.dispatchEvent(new CustomEvent('playing', { detail: { deck: to } }));
  }

  /**
   * Blend into the next song over `duration` seconds.
   * style 'fade': volume crossfade with the chosen curve.
   * style 'mix':  DJ-style — the new song comes in with its bass cut, both play full for a moment,
   *               then the basslines swap in the middle and the old song fades out, so two kick drums
   *               and basslines never pile up.
   */
  async crossfadeTo(track, { src, lufs, loudness, duration = 5, style = 'fade' }) {
    const { from, to } = await this.#startOther(track, { src, lufs, loudness });
    const t = this.ctx.currentTime;
    const n = 64, inC = new Float32Array(n), outC = new Float32Array(n);
    const startOut = from.fade.gain.value;
    const curve = this.opts.xfCurve;
    for (let i = 0; i < n; i++) {
      const x = i / (n - 1);
      let a, b;
      if (style === 'mix') {
        a = x < 0.45 ? Math.sin((x / 0.45) * Math.PI / 2) : 1;                 // in: full by 45 %
        b = x < 0.5 ? 1 : Math.cos(((x - 0.5) / 0.5) * Math.PI / 2);          // out: holds, then fades
      } else if (curve === 'equalpower') { a = Math.sin((x * Math.PI) / 2); b = Math.cos((x * Math.PI) / 2); }
      else if (curve === 'linear') { a = x; b = 1 - x; }
      else { a = 1 - (1 - x) * (1 - x); b = (1 - x) * (1 - x); } // smooth: new song rises fast, old one clears out early
      inC[i] = a;
      outC[i] = b * startOut;
    }
    from.fade.gain.cancelScheduledValues(t);
    from.fade.gain.setValueCurveAtTime(outC, t, duration);
    to.fade.gain.setValueCurveAtTime(inC, t, duration);
    if (style === 'mix') {
      const CUT = -26, swapAt = t + duration * 0.5, swap = Math.min(0.5, duration * 0.08);
      to.bass.gain.setValueAtTime(CUT, t);
      to.bass.gain.setValueAtTime(CUT, swapAt);
      to.bass.gain.linearRampToValueAtTime(0, swapAt + swap);
      from.bass.gain.setValueAtTime(0, swapAt);
      from.bass.gain.linearRampToValueAtTime(CUT, swapAt + swap);
    }
    this.#handOver(from, to, duration);
  }

  /**
   * Gapless: start the next song exactly where this one ends, with a 12 ms overlap so there is no
   * click or gap. Call it within the last few seconds; resolves once the new song is playing.
   *
   * Starting a media element takes 10–30 ms, so the next song is warmed up silently first (which
   * also measures how long a start takes on this machine) and then started that much before the
   * end. The new deck becomes the active one *before* it starts, so the old song's 'ended' can
   * never be mistaken for the end of playback.
   */
  async gaplessTo(track, { src, lufs, loudness }) {
    this.ensureGraph();
    await this.#wake();
    this.cancelCrossfade();
    const from = this.deck, to = this.other, fromTok = from.token;
    const remaining = () => ((from.el.duration || this.duration) - from.el.currentTime) / (from.el.playbackRate || 1);
    const userStopped = () => from.token !== fromTok || from.pausing || (from.el.paused && !from.el.ended);
    const OVERLAP = 0.012;
    to.fade.gain.cancelScheduledValues(this.ctx.currentTime);
    to.fade.gain.setValueAtTime(0, this.ctx.currentTime);
    this.#prime(to, track, { src, lufs, loudness });
    const toTok = to.token;
    const fail = (e) => { if (to.token === toTok) this.#stopDeck(to); throw e; };
    // 1. buffered
    if (to.el.readyState < 3) {
      await new Promise((res, rej) => {
        const t = setTimeout(() => rej(new Error('next song not ready')), Math.max(400, remaining() * 1000 - 150));
        to.el.addEventListener('canplay', () => { clearTimeout(t); res(); }, { once: true });
        to.el.addEventListener('error', () => { clearTimeout(t); rej(to.el.error || new Error('load failed')); }, { once: true });
      }).catch(fail);
    }
    if (userStopped() || to.token !== toTok) fail(new Error('crossfade cancelled'));
    // 2. warm up silently and measure the start-up delay
    let startMs = 30;
    try {
      const t0 = performance.now();
      await to.el.play();
      if (to.el.readyState < 3 || to.el.paused) await new Promise((r) => { to.el.addEventListener('playing', r, { once: true }); setTimeout(r, 150); });
      startMs = Math.min(90, Math.max(8, performance.now() - t0));
      to.el.pause();
      to.el.currentTime = 0;
    } catch (e) { fail(e); }
    // 3. wait for the moment, re-aiming as it approaches (timers drift a few ms)
    const lead = startMs / 1000 + OVERLAP;
    for (;;) {
      if (userStopped() || to.token !== toTok) fail(new Error('crossfade cancelled'));
      const r = remaining() - lead;
      if (r <= 0.003 || from.el.ended) break;
      await new Promise((res) => setTimeout(res, r > 0.25 ? (r - 0.2) * 1000 : Math.max(0, r * 1000 - 2)));
    }
    // 4. hand over: the new deck is the active one from here on
    this.a = to.i;
    try { await to.el.play(); }
    catch (e) { this.a = from.i; fail(e); }
    if (to.token !== toTok) { this.a = from.i; throw new Error('crossfade cancelled'); }
    const t = this.ctx.currentTime;
    to.fade.gain.cancelScheduledValues(t);
    to.fade.gain.setValueAtTime(0, t);
    to.fade.gain.linearRampToValueAtTime(1, t + OVERLAP);
    from.fade.gain.cancelScheduledValues(t);
    from.fade.gain.setValueAtTime(from.fade.gain.value, t);
    from.fade.gain.linearRampToValueAtTime(0, t + OVERLAP * 2);
    to.leadDone = true;   // gapless: never jump over a quiet album intro
    this.#handOver(from, to, 0.4);
    this.lastGapless = { startMs: Math.round(startMs), lateMs: from.el.ended ? null : Math.round(remaining() * 1000) };
  }

  cancelCrossfade() {
    if (!this.xfading) return;
    clearTimeout(this.xfading.timer);
    const { from } = this.xfading;
    if (from !== this.deck) this.#stopDeck(from);
    this.xfading = null;
    const g = this.deck.fade.gain;
    if (this.ctx) { g.cancelScheduledValues(this.ctx.currentTime); g.setTargetAtTime(1, this.ctx.currentTime, 0.05); this.#resetBass(this.deck); }
  }

  // Volume fade used by the sleep timer.
  fadeOutAll(seconds) {
    if (!this.ctx) return;
    const t = this.ctx.currentTime;
    const g = this.master.gain;
    g.cancelScheduledValues(t);
    g.setValueAtTime(g.value, t);
    g.linearRampToValueAtTime(0.0001, t + seconds);
  }

  // Called ~10x per second: returns 'silent-end' if trailing silence should skip to next.
  monitorSilence(intervalMs) {
    if (!this.opts.skipSilence || !this.deck.analyser || this.el.paused || this.xfading || this.deck.pausing) {
      if (this.speeding) { this.speeding = false; this.#applyRate(this.deck); }
      this.silentMs = 0;
      return null;
    }
    const an = this.deck.analyser;
    an.getFloatTimeDomainData(this.buf);
    let sum = 0;
    for (let i = 0; i < this.buf.length; i++) sum += this.buf[i] * this.buf[i];
    const rms = Math.sqrt(sum / this.buf.length);
    const t = this.el.currentTime, d = this.duration;
    if (rms < 0.0025 && t > 0.8 && this.el.readyState >= 3) {
      this.silentMs += intervalMs;
      if (this.silentMs >= 350) {
        // trailing silence: if only silence remains, finish the song now
        if (d && d - t < 1.2) return 'silent-end';
        if (this.opts.silenceInstant) this.el.currentTime = Math.min(t + 0.8, d ? d - 0.1 : t + 0.8);
        else if (!this.speeding) { this.speeding = true; this.el.playbackRate = Math.min(4, this.opts.tempo * 3); }
      }
    } else {
      this.silentMs = 0;
      if (this.speeding) { this.speeding = false; this.#applyRate(this.deck); }
    }
    return null;
  }

  // RMS level of a deck in dBFS (post-normalisation, pre-EQ)
  levelDb(d = this.deck) {
    if (!d.analyser || !this.buf) return -120;
    d.analyser.getFloatTimeDomainData(this.buf);
    let sum = 0;
    for (let i = 0; i < this.buf.length; i++) sum += this.buf[i] * this.buf[i];
    return 10 * Math.log10(sum / this.buf.length + 1e-12);
  }
  // Smart crossfade: jump over digital silence at the start of the incoming song.
  trimLeadIn() {
    const d = this.deck;
    if (d.el.paused || d.el.readyState < 3 || d.el.currentTime > 6) return false;
    if (this.levelDb(d) > -58) { d.leadDone = true; return false; }
    if (d.leadDone) return false;
    d.el.currentTime = d.el.currentTime + 0.4;
    return true;
  }
  #applyChannel() {
    if (!this.monoNode) return;
    this.monoNode.channelCount = this.opts.mono ? 1 : 2;
    this.monoNode.channelCountMode = 'explicit';
    this.monoNode.channelInterpretation = 'speakers';
    this.panner.pan.setTargetAtTime(Math.max(-1, Math.min(1, this.opts.balance || 0)), this.ctx.currentTime, 0.03);
  }
  setChannel({ mono, balance }) {
    if (mono != null) this.opts.mono = mono;
    if (balance != null) this.opts.balance = balance;
    this.#applyChannel();
  }
  async setSink(id) {
    this.sinkId = id || '';
    if (this.ctx?.setSinkId) await this.ctx.setSinkId(this.sinkId === 'default' ? '' : this.sinkId);
    else for (const d of this.decks) if (d.el.setSinkId) await d.el.setSinkId(this.sinkId);
  }
  /** Lower the music while another app is making sound (1 = normal). */
  setDuck(level, seconds = 0.35) {
    if (!this.duckNode) return;
    const g = this.duckNode.gain, t = this.ctx.currentTime;
    g.cancelScheduledValues(t); g.setValueAtTime(g.value, t);
    g.linearRampToValueAtTime(Math.max(0.02, Math.min(1, level)), t + seconds);
  }
  setVolumeLevel(v) { this.opts.volume = v; this.opts.muted = false; this.applyVolume(); }
  restoreMaster() { this.applyVolume(); }
}
