// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// Uses what a speech model heard in the recording to fix lyrics: which version of the song this is, how
// far off its timing is, and, when no lyrics are found, what the song says. The model itself is plugged in
// as `transcribe()` (it runs on the person's own device, on the song they are already playing); everything
// here is plain maths on its word list, so it is testable and the same on desktop and phone.
//
// A heard word is { t0, t1, text } in seconds from the start of the song.
import { norm } from './lyrics-engine.mjs';
import { parseLrc } from '../renderer/lrc.js';

export const wordsOf = (s) => norm(s).split(' ').filter(Boolean);

// equal, or one slip apart (speech models mishear a letter): enough for 4+ letter words
function within1(a, b) {
  if (Math.abs(a.length - b.length) > 1) return false;
  let i = 0, j = 0, miss = 0;
  while (i < a.length && j < b.length) {
    if (a[i] === b[j]) { i++; j++; continue; }
    if (++miss > 1) return false;
    if (a.length > b.length) i++; else if (a.length < b.length) j++; else { i++; j++; }
  }
  return miss + (a.length - i) + (b.length - j) <= 1;
}
const pairScore = (a, b) => (a === b ? 2 : a.length >= 4 && b.length >= 4 && within1(a, b) ? 1 : -1.5);

// Global alignment of the lyric words to the heard words. Returns map[i] = index of the heard word, or -1.
export function alignWords(lw, sw) {
  const n = lw.length, m = sw.length, GAP = -0.6, W = m + 1;
  const sc = new Float32Array((n + 1) * W), tb = new Uint8Array((n + 1) * W);   // 0 diag, 1 skip lyric word, 2 skip heard word
  for (let i = 1; i <= n; i++) { sc[i * W] = i * GAP; tb[i * W] = 1; }
  for (let j = 1; j <= m; j++) { sc[j] = j * GAP * 0.5; tb[j] = 2; }   // extra heard words (ad-libs, intro talk) are cheap to skip
  for (let i = 1; i <= n; i++) for (let j = 1; j <= m; j++) {
    const d = sc[(i - 1) * W + j - 1] + pairScore(lw[i - 1], sw[j - 1]), u = sc[(i - 1) * W + j] + GAP, l = sc[i * W + j - 1] + GAP * 0.5;
    const k = i * W + j;
    if (d >= u && d >= l) { sc[k] = d; tb[k] = 0; } else if (u >= l) { sc[k] = u; tb[k] = 1; } else { sc[k] = l; tb[k] = 2; }
  }
  const map = new Array(n).fill(-1);
  let i = n, j = m;
  while (i > 0 || j > 0) {
    const t = tb[i * W + j];
    if (i > 0 && j > 0 && t === 0) { if (pairScore(lw[i - 1], sw[j - 1]) > 0) map[i - 1] = j - 1; i--; j--; }
    else if (i > 0 && (t === 1 || j === 0)) i--; else j--;
  }
  return map;
}

// lines: [{ t, text }] (parseLrc output, or plain lines with no t). heard: [{ t0, text }].
// Returns the heard time of every line that was found (null otherwise) and how much of the text was found.
export function alignLines(lines, heard) {
  const lw = [], owner = [];
  lines.forEach((l, k) => { for (const w of wordsOf(l.text)) { lw.push(w); owner.push(k); } });
  const hw = heard.map((h) => norm(h.text));
  const map = alignWords(lw, hw);
  const times = new Array(lines.length).fill(null), got = new Array(lines.length).fill(0), want = new Array(lines.length).fill(0);
  map.forEach((j, i) => { want[owner[i]]++; if (j >= 0) { got[owner[i]]++; if (times[owner[i]] === null) times[owner[i]] = heard[j].t0; } });
  for (let k = 0; k < lines.length; k++) if (want[k] && got[k] < Math.max(1, Math.ceil(want[k] * 0.4))) times[k] = null;
  const matched = map.filter((j) => j >= 0).length;
  return { times, matched, words: lw.length, coverage: lw.length ? matched / lw.length : 0 };
}

const median = (a) => { const s = [...a].sort((x, y) => x - y); const h = s.length >> 1; return s.length ? (s.length % 2 ? s[h] : (s[h - 1] + s[h]) / 2) : 0; };

// Robust fit of heard time = a * lyric time + b. `a` only moves off 1 when the evidence is clear (another
// edit or speed of the same song), and stays within 3 %.
export function fitTiming(lines, times) {
  let pts = lines.map((l, i) => (times[i] === null || times[i] === undefined ? null : [l.t, times[i]])).filter(Boolean);
  if (pts.length < 2) return { a: 1, b: pts.length ? pts[0][1] - pts[0][0] : 0, n: pts.length, mad: Infinity, ok: false };
  let a = 1, b = median(pts.map(([x, y]) => y - x));
  if (pts.length >= 6) {
    const slopes = [];
    for (let i = 0; i < pts.length; i++) for (let j = i + 1; j < pts.length; j++) if (pts[j][0] - pts[i][0] >= 15) slopes.push((pts[j][1] - pts[i][1]) / (pts[j][0] - pts[i][0]));
    if (slopes.length >= 5) { const s = median(slopes); if (Math.abs(s - 1) > 0.003) a = Math.max(0.97, Math.min(1.03, s)); }
  }
  for (let pass = 0; pass < 2; pass++) {
    b = median(pts.map(([x, y]) => y - a * x));
    const kept = pts.filter(([x, y]) => Math.abs(y - a * x - b) <= 3);
    if (kept.length >= 2) pts = kept;
  }
  const mad = median(pts.map(([x, y]) => Math.abs(y - a * x - b)));
  return { a, b, n: pts.length, mad, ok: pts.length >= 4 && mad < 0.7 };
}

export function applyTiming(lines, { a, b }) {
  const f = (t) => Math.max(0, Math.round((a * t + b) * 100) / 100);
  return lines.map((l) => ({ ...l, t: f(l.t), end: l.end !== undefined ? f(l.end) : undefined, words: l.words ? l.words.map((w) => ({ ...w, t: f(w.t), end: w.end !== undefined ? f(w.end) : undefined })) : null }));
}

export function toLrc(lines) {
  const stamp = (t) => `[${String(Math.floor(t / 60)).padStart(2, '0')}:${(t % 60).toFixed(2).padStart(5, '0')}]`;
  return lines.map((l) => `${stamp(l.t)} ${l.text}`).join('\n');
}

// How much of a version's text the recording contains: the way to tell versions apart.
export function coverageOf(version, heard) {
  const lines = version.synced ? parseLrc(version.synced).filter((l) => l.text) : (version.plain || '').split(/\n/).map((text) => ({ text })).filter((l) => l.text.trim());
  if (!lines.length) return { coverage: 0, lines };
  return { ...alignLines(lines, heard), lines };
}

const STOP = new Set('the a an and or but so to of in on at for with is it i you me my we your oh yeah na la ooh hey that this be are was'.split(' '));
// A few distinctive words in a row, to ask LRCLIB which song says this.
export function identifyQuery(heard, n = 7) {
  const w = heard.map((h) => norm(h.text)).filter(Boolean);
  let best = '', bestScore = -1;
  for (let i = 0; i + n <= w.length; i++) {
    const win = w.slice(i, i + n);
    const score = new Set(win.filter((x) => !STOP.has(x) && x.length >= 4)).size;
    if (score > bestScore) { bestScore = score; best = win.join(' '); }
  }
  return best || w.slice(0, n).join(' ');
}

// Heard words -> lines (a new line after a pause or after about eight words), for songs with no lyrics at all.
export function transcriptToLines(heard, { gap = 0.9, maxWords = 8 } = {}) {
  const lines = [];
  let cur = null;
  for (const h of heard) {
    const text = String(h.text || '').trim();
    if (!text) continue;
    if (!cur || h.t0 - cur.last > gap || cur.n >= maxWords) { cur = { t: h.t0, text, last: h.t1 ?? h.t0, n: 1 }; lines.push(cur); }
    else { cur.text += ' ' + text; cur.last = h.t1 ?? h.t0; cur.n++; }
  }
  return lines.map((l, i) => ({ t: l.t, text: l.text, end: lines[i + 1] ? lines[i + 1].t : l.last + 2 }));
}

// current: what getLyrics returned for this song (or null). deps:
//   transcribe()          -> heard words for the playing song (the speech model)
//   search(text)          -> LRCLIB records matching some words (for songs with no lyrics yet)
//   fetchVariant(id)      -> one LRCLIB record by id (to compare other versions)
// Returns { lyrics, how, offset?, drift? } where `how` is one of: nothing-heard, unchanged, retimed, switched,
// identified, transcribed. `lyrics` has { synced, plain, source, id? }.
export async function improveLyrics(t, current, { transcribe, search, fetchVariant, minCoverage = 0.45 }) {
  const heard = await transcribe();
  if (!heard || heard.length < 8) return { lyrics: current, how: 'nothing-heard' };

  const candidates = [];
  if (current) candidates.push({ id: current.id, synced: current.synced, plain: current.plain, source: current.source, current: true });
  if (current?.variants) for (const v of current.variants.filter((x) => x.id !== current.id).slice(0, 4)) { try { const r = await fetchVariant?.(v.id); if (r) candidates.push({ id: r.id, synced: r.syncedLyrics || null, plain: r.plainLyrics || null, source: 'LRCLIB' }); } catch { /* skip it */ } }
  if (!current) {
    for (const r of (await search?.(identifyQuery(heard))) || []) if (r.syncedLyrics || r.plainLyrics) candidates.push({ id: r.id, synced: r.syncedLyrics || null, plain: r.plainLyrics || null, source: 'LRCLIB', found: true });
  }

  let best = null;
  for (const c of candidates) {
    const a = coverageOf(c, heard);
    if (!best || a.coverage > best.a.coverage + 1e-9) best = { c, a };
  }
  if (!best || best.a.coverage < minCoverage) {
    if (current) return { lyrics: current, how: 'unchanged', coverage: best?.a.coverage ?? 0 };
    const lines = transcriptToLines(heard);
    return { lyrics: { synced: toLrc(lines), plain: lines.map((l) => l.text).join('\n'), source: 'Heard in the recording (automatic, may have mistakes)', machine: true }, how: 'transcribed' };
  }
  const cur = candidates.find((c) => c.current);
  const curCov = cur ? coverageOf(cur, heard).coverage : 0;
  const switched = !!best.c && !best.c.current && (!cur || best.a.coverage >= curCov + 0.1);
  const pick = switched ? best : { c: cur || best.c, a: cur ? coverageOf(cur, heard) : best.a };
  let lyrics = { synced: pick.c.synced, plain: pick.c.plain, source: pick.c.source, id: pick.c.id };
  let how = switched ? (current ? 'switched' : 'identified') : (current ? 'unchanged' : 'identified');
  let fit = null;
  if (pick.c.synced) {
    const synced = pick.a.lines.filter((l) => l.t !== undefined);
    fit = fitTiming(synced, pick.a.times);
    if (fit.ok && (Math.abs(fit.b) > 0.4 || Math.abs(fit.a - 1) > 0.004)) {
      lyrics = { ...lyrics, synced: toLrc(applyTiming(parseLrc(pick.c.synced), fit)) };
      how = how === 'unchanged' ? 'retimed' : how;
    }
  }
  return { lyrics, how, offset: fit?.ok ? Math.round(fit.b * 100) / 100 : undefined, drift: fit?.ok ? Math.round((fit.a - 1) * 10000) / 10000 : undefined, coverage: pick.a.coverage };
}
