// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// Lyric card layout: turns synced lyric lines into "scenes" of words set in mixed sizes (the
// kinetic-typography look used for Reels and Stories). Pure functions with no DOM: text width is
// measured through a callback, so the same code runs under node:test and on a canvas.

export const FORMATS = {
  // `safe`: the parts of a 9:16 Reel that Instagram covers with its own UI (status and header on top,
  // caption, music and buttons at the bottom, like/comment/share on the right), as fractions of the frame.
  story: { id: 'story', label: 'Story / Reel 9:16', w: 1080, h: 1920, safe: { top: 0.12, bottom: 0.22, left: 0.06, right: 0.14 } },
  portrait: { id: 'portrait', label: 'Post 4:5', w: 1080, h: 1350 },
  square: { id: 'square', label: 'Square 1:1', w: 1080, h: 1080 }
};

// Small words that stay quiet so the others can be loud.
const FILLER = new Set(('a an the and or but nor of to in on at for with as is it its i im ill ive id you your youre me my we us our he she they them his her be am are was were do does did so if than ' +
  'that this these those from by up out oh ooh yeah uh la na hey just got get gonna wanna cause cos til').split(' '));
const bare = (w) => w.toLowerCase().replace(/[^\p{L}\p{N}]/gu, '');
export const isFiller = (w) => FILLER.has(bare(w));

export const SIZE = { big: 1, mid: 0.56, small: 0.34 };

// Text size choices. Bigger sizes raise the quiet words, start the fit search higher and put fewer
// words in each scene (a long line is split), so the type really grows; the fit search still keeps
// every word inside the box.
export const TEXT_SIZES = [['normal', 'Normal'], ['large', 'Large'], ['huge', 'Huge']];
const TEXT = {
  normal: { ratio: SIZE, start: 0.3, maxWordsPerScene: 8, splitAt: Infinity },
  large: { ratio: { big: 1, mid: 0.66, small: 0.44 }, start: 0.38, maxWordsPerScene: 6, splitAt: 4 },
  huge: { ratio: { big: 1, mid: 0.76, small: 0.54 }, start: 0.46, maxWordsPerScene: 4, splitAt: 3 }
};
const textOf = (size) => TEXT[size] || TEXT.normal;

// The area kept clear of the platform's overlays (or a plain margin when there is none / it is off).
// A still is posted as a picture or a Story, where there is no like/comment/share column on the right,
// so it keeps the top and bottom insets but uses the left inset on both sides and stays centred.
export function safeRect(fmt, safe = true, still = false) {
  const { w: W, h: H } = fmt;
  if (safe && fmt.safe) {
    const s = fmt.safe, right = still ? s.left : s.right;
    return { x: W * s.left, y: H * s.top, w: W * (1 - s.left - right), h: H * (1 - s.top - s.bottom) };
  }
  return { x: W * 0.06, y: H * 0.04, w: W * 0.88, h: H * 0.92 };
}

// The box the lyrics are set in. Inside the safe area it leaves room for the start cue on top and the
// song name below; without it, it is the classic box (8% side margins).
export function textBox(fmt, { safe = true, still = false } = {}) {
  const { w: W, h: H } = fmt;
  if (safe && fmt.safe) {
    const r = safeRect(fmt, true, still), top = H * (still ? 0.02 : 0.05), bottom = H * 0.09;
    return { x: r.x, y: r.y + top, w: r.w, h: r.h - top - bottom };
  }
  const margin = W * 0.08;
  return still ? { x: margin, y: H * 0.1, w: W - margin * 2, h: H * 0.72 } : { x: margin, y: H * 0.12, w: W - margin * 2, h: H * 0.68 };
}
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
// `splitAt` splits a line with more words than that into even parts, each its own scene.
export function buildScenes(lines, { maxWordsPerScene = 8, splitAt = Infinity } = {}) {
  const src = lines.filter((l) => l && l.text && l.text.trim());
  const groups = [];
  for (let i = 0; i < src.length; i++) {
    const a = lineWords(src[i]);
    if (a.length > splitAt) {
      const n = Math.ceil(a.length / splitAt), per = Math.ceil(a.length / n);
      for (let k = 0; k < a.length; k += per) groups.push({ words: a.slice(k, k + per), lines: 2, lastLineEnd: src[i].end ?? src[i].t + 4 });
      continue;
    }
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
export function layoutWords(words, sizes, box, measure, { align = 'center', upper = true, textSize = 'normal' } = {}) {
  const { w: maxW, h: maxH } = box;
  const { ratio, start } = textOf(textSize);
  const shown = words.map((w, i) => (upper && sizes[i] === 'big' && w.text.length <= 12 ? w.text.toUpperCase() : w.text));
  const tryBase = (B) => {
    const rows = [];
    let row = null;
    for (let i = 0; i < shown.length; i++) {
      const px = B * ratio[sizes[i]], wd = measure(shown[i], px, WEIGHT[sizes[i]]), gap = px * 0.3;
      if (wd > maxW) return null;
      if (!row || row.w + gap + wd > maxW) rows.push((row = { items: [], w: 0, h: 0 }));
      row.items.push({ i, px, wd, gap }); row.w += (row.items.length > 1 ? gap : 0) + wd; row.h = Math.max(row.h, px);
    }
    const lead = 0.98, space = B * 0.1;
    const total = rows.reduce((a, r) => a + r.h * lead, 0) + space * (rows.length - 1);
    return total <= maxH ? { rows, lead, space, total } : null;
  };
  let B = Math.min(maxW * start, maxH * start), fit = null;
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

// The lines that fall inside a clip of the song: from `startAt` seconds, `len` seconds long (0 = to the end).
// A line already under way at `startAt` is kept, so the clip never opens on a gap in the middle of a phrase.
export const clipLines = (lines, startAt, len = 0) => lines.filter((l) => l.text && (l.end ?? l.t + 4) > startAt && (!len || l.t < startAt + len));

// The whole timeline: scenes laid out and moved so the video starts at 0.
// Without `opts.startAt` the video opens at least `opts.leadIn` (0.5 s) before the first word, at a whole
// second of the song: music pickers start a song on whole seconds, so "start the song at m:ss" is then exact.
// It never starts before the song does (the lead-in shrinks instead, and a countdown that no longer fits is
// left out). With `opts.startAt`, video time 0 is exactly that second of the song (so the clip lines up with
// the same song started at that second in a platform's own music picker) and, with `opts.endAt`, the video is
// exactly as long as the clip.
// Also returns `origin` (the song second at video time 0) and `lineStarts` (video times of each line start).
export function buildTimeline(lines, fmt, measure, opts = {}) {
  const ts = textOf(opts.textSize);
  const scenes = buildScenes(lines, { maxWordsPerScene: opts.maxWordsPerScene ?? ts.maxWordsPerScene, splitAt: opts.splitAt ?? ts.splitAt });
  if (!scenes.length) return { scenes: [], duration: 0 };
  const clip = Number.isFinite(opts.startAt);
  const leadIn = Number.isFinite(opts.leadIn) ? opts.leadIn : 0.5;
  const origin = clip ? opts.startAt : Math.max(0, Math.floor(scenes[0].t0 - leadIn + 1e-6));
  const box = textBox(fmt, { safe: opts.safe !== false });
  const out = scenes.map((s) => {
    const { items } = layoutWords(s.words, s.sizes, box, measure, opts);
    for (const it of items) { it.t -= origin; it.end -= origin; }
    return { t0: s.t0 - origin, t1: s.t1 - origin, items };
  });
  const natural = out[out.length - 1].t1 + 0.6;
  const duration = clip && Number.isFinite(opts.endAt) ? Math.max(0, opts.endAt - origin) : natural;
  const lineStarts = lines.filter((l) => l && l.text && l.text.trim()).map((l) => l.t - origin).filter((t) => t >= 0 && t <= duration).sort((a, b) => a - b);
  return { scenes: out, duration, origin, lineStarts };
}

// Every selected word in one image, for the still picture.
export function buildStill(lines, fmt, measure, opts = {}) {
  const scenes = buildScenes(lines, { maxWordsPerScene: 1e9 });
  const words = scenes.flatMap((s) => s.words), sizes = scenes.flatMap((s) => s.sizes);
  return layoutWords(words, sizes, textBox(fmt, { safe: opts.safe !== false, still: true }), measure, opts).items;
}

// A plain (unsynced) line list has no times; give each line a nominal one so a still can be built.
export const untimed = (texts) => texts.map((text, i) => ({ t: i * 3, end: i * 3 + 3, text }));
