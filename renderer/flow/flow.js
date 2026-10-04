// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// Flow radio: order a pool of candidate songs so each transition mixes well —
// tempo stays in a comfortable band and keys stay harmonically compatible (Camelot wheel).
// Works with whatever is known: analysed BPM/key/energy when available, otherwise YouTube's
// relevance order, loudness, duration and title hints, with less confidence.

export function parseCamelot(c) {
  const m = /^(\d{1,2})([AB])$/.exec(c || '');
  return m ? { n: Number(m[1]), l: m[2] } : null;
}
/** 0..1 harmonic compatibility of two Camelot keys */
export function keyCompat(a, b) {
  const x = parseCamelot(a), y = parseCamelot(b);
  if (!x || !y) return null;
  const d = Math.min((x.n - y.n + 12) % 12, (y.n - x.n + 12) % 12);
  if (x.l === y.l) return d === 0 ? 1 : d === 1 ? 0.9 : d === 2 ? 0.55 : d === 7 ? 0.45 : 0.15;   // ±1 = adjacent, +2 = energy lift
  return d === 0 ? 0.85 : d === 1 ? 0.5 : 0.1;                                                        // relative major/minor
}
/** 0..1 tempo compatibility, treating half/double time as related */
export function tempoCompat(a, b) {
  if (!a || !b) return null;
  let best = 1e9;
  for (const m of [1, 2, 0.5]) best = Math.min(best, Math.abs(Math.log2((b * m) / a)));
  const pct = (Math.pow(2, best) - 1) * 100;              // % difference after octave folding
  const oct = Math.abs(Math.log2(b / a)) > 0.5 ? 0.85 : 1;  // half/double time is fine but not perfect
  const s = pct <= 3 ? 1 : pct <= 6 ? 0.85 : pct <= 10 ? 0.6 : pct <= 16 ? 0.3 : 0.05;
  return s * oct;
}
const HINTS = [[/\b(acoustic|unplugged|piano version|stripped|lullaby|sleep)\b/i, -0.25], [/\b(slowed|reverb)\b/i, -0.2], [/\b(remix|club mix|extended mix|edit|bass boosted|sped up|nightcore|phonk|dnb)\b/i, 0.2], [/\blive\b/i, 0]];
function hintEnergy(t) {
  let e = 0;
  for (const [re, v] of HINTS) if (re.test(t.title || '')) e += v;
  return e;
}
/** estimated energy 0..1 with a confidence */
function energyOf(t, f) {
  if (f?.energy != null) return { e: f.energy, c: 0.8 };
  let e = 0.5, c = 0.15;
  if (t.loudnessDb != null) { e += Math.max(-0.2, Math.min(0.2, t.loudnessDb / 30)); c = 0.3; }
  const h = hintEnergy(t);
  if (h) { e += h; c = Math.max(c, 0.35); }
  return { e: Math.max(0, Math.min(1, e)), c };
}

/**
 * Transition score a→b in 0..1 plus how sure we are (0..1).
 * @param {object} fa features of a ({bpm,bpmConf,camelot,keyConf,energy}) or null
 */
export function transition(a, fa, b, fb, opts = {}) {
  const parts = [];
  const tc = tempoCompat(fa?.bpm, fb?.bpm);
  if (tc != null) parts.push([tc, 0.4 * Math.min(fa.bpmConf ?? 0.5, fb.bpmConf ?? 0.5) + 0.05]);
  const kc = keyCompat(fa?.camelot, fb?.camelot);
  if (kc != null) parts.push([kc, 0.35 * Math.min(fa.keyConf ?? 0.5, fb.keyConf ?? 0.5) + 0.05]);
  const ea = energyOf(a, fa), eb = energyOf(b, fb);
  parts.push([1 - Math.min(1, Math.abs(ea.e - eb.e) * 1.6), 0.25 * Math.min(ea.c, eb.c)]);
  // song length similarity is a weak "same kind of track" signal
  if (a.duration && b.duration) parts.push([1 - Math.min(1, Math.abs(Math.log2(b.duration / a.duration))), 0.03]);
  let w = 0, s = 0;
  for (const [v, wt] of parts) { s += v * wt; w += wt; }
  return { score: w ? s / w : 0.5, sure: Math.min(1, w / 0.8) };
}

/**
 * Listening context, like Spotify's "time of day" moods: what energy / tempo fits right now.
 * Late night winds down, mornings ramp up, daytime and early evening carry the most energy,
 * weekends a touch more upbeat. Returns targets in the same 0..1 energy scale as the features.
 */
export function contextFor(date = new Date()) {
  const h = date.getHours() + date.getMinutes() / 60, weekend = [0, 6].includes(date.getDay());
  // smooth daily curve (peaks mid-afternoon, lowest around 3–4 am)
  const energy = 0.5 + 0.22 * Math.sin(((h - 9) / 24) * 2 * Math.PI) + (weekend && h > 11 ? 0.05 : 0);
  const label = h < 5 ? 'late night' : h < 9 ? 'morning' : h < 12 ? 'late morning' : h < 17 ? 'afternoon' : h < 21 ? 'evening' : 'night';
  const maxBpm = h >= 23 || h < 6 ? 115 : null;           // keep late-night radios mellow
  return { hour: h, weekend, energy: Math.max(0.2, Math.min(0.85, energy)), label, maxBpm };
}

/**
 * Order a pool after `seed` for the best flow.
 * @param {object} seed track
 * @param {Array} pool candidate tracks in YouTube's relevance order
 * @param {(id)=>object|null} feat features lookup
 * @param {object} o
 *   band        allowed tempo drift from the seed (fraction)
 *   ctx         contextFor() — time-of-day energy target
 *   taste       { artist(name)→0..1 affinity, liked(id)→bool, recent(id)→0..1 how recently played, skips(id)→count }
 */
export function planFlow(seed, pool, feat, o = {}) {
  const band = o.band ?? 0.12;
  const ctx = o.ctx || null, taste = o.taste || null;
  const seedF = feat(seed.id);
  const rel = new Map(pool.map((t, i) => [t.id, 1 - i / Math.max(1, pool.length)]));   // relevance from YouTube's order
  const left = pool.filter((t) => t.id !== seed.id);
  const out = [];
  let prev = seed, prevF = seedF;
  const recentArtists = [];
  const artistOf = (t) => (t.artists?.[0]?.name || '').toLowerCase();
  const familiar = (t) => (taste ? taste.liked?.(t.id) || (taste.artist?.(artistOf(t)) || 0) > 0.35 : false);
  let famRun = 0, newRun = 0;
  const stepScore = (p, pf, c, step) => {
    const cf = feat(c.id);
    const tr = transition(p, pf, c, cf);
    let s = tr.score * (0.45 + 0.4 * tr.sure) + (rel.get(c.id) ?? 0.5) * (0.35 - 0.2 * tr.sure);
    // keep the tempo in a comfortable band around the seed so the radio doesn't drift away
    if (seedF?.bpm && cf?.bpm) { const tc = tempoCompat(seedF.bpm, cf.bpm); if (tc != null && tc < 0.6) s -= (0.6 - tc) * band * 2.5; }
    // time of day: drift gently toward the energy that fits now (stronger further into the radio)
    if (ctx) {
      const e = energyOf(c, cf);
      const pull = Math.min(1, 0.4 + step / 10) * e.c;
      s -= Math.abs(e.e - ctx.energy) * 0.3 * pull;
      if (ctx.maxBpm && cf?.bpm && (cf.bpmConf ?? 0) > 0.4 && cf.bpm > ctx.maxBpm && !(cf.bpm / 2 > 60)) s -= 0.06;
    }
    // personal taste: artists you play and like, minus songs you keep skipping or just heard
    if (taste) {
      const a = taste.artist?.(artistOf(c)) || 0;
      s += a * 0.1 + (taste.liked?.(c.id) ? 0.06 : 0);
      const sk = taste.skips?.(c.id) || 0;
      if (sk) s -= Math.min(0.3, sk * 0.1);
      const rec = taste.recent?.(c.id) || 0;           // 1 = played minutes ago … 0 = long ago
      s -= rec * 0.25;
      // balance familiar and new like a good radio: don't serve 3 familiar (or 4 new) in a row
      const f = familiar(c);
      if (f && famRun >= 2) s -= 0.08;
      if (!f && newRun >= 3 && a === 0) s -= 0.05;
    }
    const ar = artistOf(c);
    if (ar && recentArtists.includes(ar)) s -= 0.12;          // don't stack the same artist
    if (c.album?.name && p.album?.name && c.album.name === p.album.name) s -= 0.04;
    return { s, tr };
  };
  let step = 0;
  while (left.length) {
    let bi = 0, bs = -1e9, btr = null;
    for (let i = 0; i < left.length; i++) {
      const { s, tr } = stepScore(prev, prevF, left[i], step);
      // one-step lookahead: prefer choices that leave a good next move
      let look = 0;
      if (o.lookahead !== false && left.length > 2 && i < 40) {
        let bestNext = 0;
        for (let j = 0; j < Math.min(left.length, 25); j++) if (j !== i) bestNext = Math.max(bestNext, transition(left[i], feat(left[i].id), left[j], feat(left[j].id)).score);
        look = bestNext * 0.15;
      }
      if (s + look > bs) { bs = s + look; bi = i; btr = tr; }
    }
    const pick = left.splice(bi, 1)[0];
    out.push({ track: pick, score: btr ? Math.round(btr.score * 100) / 100 : null, sure: btr ? Math.round(btr.sure * 100) / 100 : 0 });
    recentArtists.push(artistOf(pick)); if (recentArtists.length > 3) recentArtists.shift();
    if (familiar(pick)) { famRun++; newRun = 0; } else { newRun++; famRun = 0; }
    prev = pick; prevF = feat(pick.id); step++;
  }
  return out;
}

/** Human-friendly label for chips: "124 BPM · 8A" */
export function featLabel(f) {
  if (!f) return '';
  return [f.bpm ? Math.round(f.bpm) + ' BPM' : '', f.camelot && (f.keyConf ?? 1) >= 0.15 ? f.camelot : ''].filter(Boolean).join(' · ');
}
