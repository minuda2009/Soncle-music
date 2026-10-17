// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// Lyric card layout: turns synced lyric lines into "scenes" of words set in mixed sizes (the
// kinetic-typography look used for Reels and Stories). Pure functions with no DOM: text width is
// measured through a callback, so the same code runs under node:test and on a canvas.

export const FORMATS = {
  story: { id: 'story', label: 'Story / Reel 9:16', w: 1080, h: 1920 },
  portrait: { id: 'portrait', label: 'Post 4:5', w: 1080, h: 1350 },
  square: { id: 'square', label: 'Square 1:1', w: 1080, h: 1080 }
};

// Small words that stay quiet so the others can be loud.
const FILLER = new Set(('a an the and or but nor of to in on at for with as is it its i im ill ive id you your youre me my we us our he she they them his her be am are was were do does did so if than ' +
  'that this these those from by up out oh ooh yeah uh la na hey just got get gonna wanna cause cos til').split(' '));
const bare = (w) => w.toLowerCase().replace(/[^\p{L}\p{N}]/gu, '');
export const isFiller = (w) => FILLER.has(bare(w));

export const SIZE = { big: 1, mid: 0.56, small: 0.34 };
const WEIGHT = { big: 800, mid: 600, small: 500 };

// One lyric line -> words with start and end times. Enhanced-LRC words are used as they are; for a
// plain synced line the words share the line's time by length (about 85% of it, then a short rest).
export function lineWords(line) {
  const t0 = line.t, t1 = line.end ?? line.t + 4;
  let words;
  if (line.words?.length) {
    words = line.words.map((w) => ({ text: String(w.text).trim(), t: w.t, end: w.end })).filter((w) => w.text);
  } else {
    const parts = String(line.text || '').split(/\s+/).filter(Boolean);
    const weights = parts.map((p) => p.length + 1.5), total = weights.reduce((a, b) => a + b, 0) || 1;
    const span = Math.max(0.4, (t1 - t0) * 0.85);
    let at = t0;
    words = parts.map((p, i) => { const w = { text: p, t: at }; at += (weights[i] / total) * span; return w; });
  }
  for (let i = 0; i < words.length; i++) words[i].end = words[i + 1] ? words[i + 1].t : Math.max(words[i].t + 0.25, Math.min(t1, words[i].t + 1.2));
  return words;
}

// Size class per word: the longest and last words of the line lead, filler words stay small.
export function classify(words) {
  const score = words.map((w, i) => (isFiller(w.text) ? -1 : Math.min(bare(w.text).length, 9) + (i === words.length - 1 ? 2.5 : 0)));
  const idx = score.map((s, i) => [s, i]).filter(([s]) => s >= 0).sort((a, b) => b[0] - a[0] || a[1] - b[1]);
  const bigN = idx.length ? Math.max(1, Math.round(idx.length * 0.34)) : 0;
  const big = new Set(idx.slice(0, bigN).map(([, i]) => i));
  return words.map((w, i) => (score[i] < 0 ? 'small' : big.has(i) ? 'big' : 'mid'));
}

// Lines -> scenes. Two short neighbouring lines share one scene; a scene runs until its last word
// has finished (plus a moment) or the next scene starts.
export function buildScenes(lines, { maxWordsPerScene = 8 } = {}) {
  const src = lines.filter((l) => l && l.text && l.text.trim());
  const groups = [];
  for (let i = 0; i < src.length; i++) {
    const a = lineWords(src[i]);
    const prev = groups[groups.length - 1];
    if (prev && prev.words.length + a.length <= maxWordsPerScene && src[i].t - prev.lastLineEnd < 1.2 && prev.lines < 2 && a.length <= 4 && prev.words.length <= 4) {
      prev.words.push(...a); prev.lines++; prev.lastLineEnd = src[i].end ?? src[i].t + 4; continue;
    }
    groups.push({ words: a, lines: 1, lastLineEnd: src[i].end ?? src[i].t + 4 });
  }
  const scenes = groups.map((g) => ({ words: g.words, t0: g.words[0].t, lineEnd: g.lastLineEnd }));
  for (let i = 0; i < scenes.length; i++) {
    const s = scenes[i], lastEnd = s.words[s.words.length - 1].end;
    const next = scenes[i + 1]?.words[0].t ?? Infinity;
    s.t1 = Math.max(s.t0 + 0.8, Math.min(s.lineEnd, lastEnd + 1.2, next));
    s.sizes = classify(s.words);
  }
  return scenes;
}

// Sets one scene's words in rows. `measure(text, px, weight)` returns the text width. The base size
// is searched downwards until every row fits the box, so a short line gets huge type and a long
// one gets smaller type. Returns { items: [{ text, px, weight, x, y (baseline), w, k, ... }] }.
export function layoutWords(words, sizes, box, measure, { align = 'center', upper = true } = {}) {
  const { w: maxW, h: maxH } = box;
  const shown = words.map((w, i) => (upper && sizes[i] === 'big' && w.text.length <= 12 ? w.text.toUpperCase() : w.text));
  const tryBase = (B) => {
    const rows = [];
    let row = null;
    for (let i = 0; i < shown.length; i++) {
      const px = B * SIZE[sizes[i]], wd = measure(shown[i], px, WEIGHT[sizes[i]]), gap = px * 0.3;
      if (wd > maxW) return null;
      if (!row || row.w + gap + wd > maxW) rows.push((row = { items: [], w: 0, h: 0 }));
      row.items.push({ i, px, wd, gap }); row.w += (row.items.length > 1 ? gap : 0) + wd; row.h = Math.max(row.h, px);
    }
    const lead = 0.98, space = B * 0.1;
    const total = rows.reduce((a, r) => a + r.h * lead, 0) + space * (rows.length - 1);
    return total <= maxH ? { rows, lead, space, total } : null;
  };
  let B = Math.min(maxW * 0.3, maxH * 0.3), fit = null;
  for (let n = 0; n < 60 && B > 8; n++, B *= 0.94) if ((fit = tryBase(B))) break;
  if (!fit) { B = 8; fit = tryBase(B) || { rows: [], lead: 1, space: 0, total: 0 }; }
  const items = new Array(words.length);
  let y = box.y + (maxH - fit.total) / 2;
  for (const r of fit.rows) {
    let x = align === 'left' ? box.x : box.x + (maxW - r.w) / 2;
    const base = y + r.h * 0.8;
    r.items.forEach((it, n) => {
      if (n) x += it.gap;   // the space before a word belongs to that word, as when the row was packed
      items[it.i] = { text: shown[it.i], px: it.px, weight: WEIGHT[sizes[it.i]], size: sizes[it.i], x, y: base, w: it.wd, t: words[it.i].t, end: words[it.i].end, i: it.i };
      x += it.wd;
    });
    y += r.h * fit.lead + fit.space;
  }
  return { items, base: B };
}

// The whole timeline: scenes laid out and moved so the video starts at 0 (after a short lead-in).
export function buildTimeline(lines, fmt, measure, opts = {}) {
  const scenes = buildScenes(lines, opts);
  if (!scenes.length) return { scenes: [], duration: 0 };
  const lead = 0.5, origin = scenes[0].t0 - lead;
  const margin = fmt.w * 0.08;
  const box = { x: margin, y: fmt.h * 0.12, w: fmt.w - margin * 2, h: fmt.h * 0.68 };
  const out = scenes.map((s) => {
    const { items } = layoutWords(s.words, s.sizes, box, measure, opts);
    for (const it of items) { it.t -= origin; it.end -= origin; }
    return { t0: s.t0 - origin, t1: s.t1 - origin, items };
  });
  return { scenes: out, duration: out[out.length - 1].t1 + 0.6 };
}

// Every selected word in one image, for the still picture.
export function buildStill(lines, fmt, measure, opts = {}) {
  const scenes = buildScenes(lines, { maxWordsPerScene: 1e9 });
  const words = scenes.flatMap((s) => s.words), sizes = scenes.flatMap((s) => s.sizes);
  const margin = fmt.w * 0.08;
  return layoutWords(words, sizes, { x: margin, y: fmt.h * 0.1, w: fmt.w - margin * 2, h: fmt.h * 0.72 }, measure, opts).items;
}

// A plain (unsynced) line list has no times; give each line a nominal one so a still can be built.
export const untimed = (texts) => texts.map((text, i) => ({ t: i * 3, end: i * 3 + 3, text }));
