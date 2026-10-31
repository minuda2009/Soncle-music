// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// How words move onto a lyric card, plus the small sync helpers (countdown, start cue, timecode) that
// make it easy to line the silent video up with the song by hand later. Pure functions with no DOM.

export const MOTIONS = [['pop', 'Pop'], ['fade', 'Fade'], ['slide', 'Slide up'], ['type', 'Typewriter'], ['karaoke', 'Karaoke']];

const clamp01 = (x) => Math.max(0, Math.min(1, x));
export const easeOut = (p) => 1 - (1 - p) ** 3;
export const easeBack = (p) => { const c = 1.9; return 1 + (c + 1) * (p - 1) ** 3 + c * (p - 1) ** 2; };

// Seconds each style takes to bring a word in.
const DUR = { pop: 0.24, fade: 0.5, slide: 0.35 };
const KARAOKE_DIM = 0.35;

// The look of one word at video time `t`. `item` is a laid-out word ({ text, px, t, end }).
// Returns { alpha, scale, dx, dy, highlight (0..1, how much of the word is lit), chars (letters shown) }.
// Once a word is fully in, every style rests at alpha 1, scale 1, no offset, fully lit, every letter.
export function wordState(style, t, item) {
  const len = String(item.text || '').length;
  const rest = { alpha: 1, scale: 1, dx: 0, dy: 0, highlight: 1, chars: len };
  if (style === 'type') {
    const dur = Math.min(0.6, Math.max(0.12, ((item.end ?? item.t + 0.5) - item.t) * 0.8));
    if (t < item.t) return { ...rest, alpha: 0, chars: 0 };
    const p = clamp01((t - item.t) / dur);
    return p >= 1 ? rest : { ...rest, chars: Math.max(1, Math.ceil(p * len)) };
  }
  if (style === 'karaoke') {
    const dur = Math.min(1.5, Math.max(0.2, (item.end ?? item.t + 0.5) - item.t));
    const p = clamp01((t - item.t) / dur);
    return p >= 1 ? rest : { ...rest, alpha: KARAOKE_DIM, highlight: p };
  }
  const p = clamp01((t - item.t) / (DUR[style] || DUR.pop));
  if (p >= 1) return rest;
  if (style === 'fade') return { ...rest, alpha: p * p * (3 - 2 * p) };
  if (style === 'slide') return { ...rest, alpha: easeOut(p), dy: (1 - easeOut(p)) * item.px * 0.5 };
  return { ...rest, alpha: easeOut(p), scale: 0.86 + 0.14 * easeBack(p), dy: (1 - easeOut(p)) * item.px * 0.14 };
}

// 3, 2, 1 in the three seconds before the first word (video time `firstWordT`), else null. Only shown
// when the whole countdown fits in the video (it would start at or after 0).
export function countdownAt(t, firstWordT) {
  if (!Number.isFinite(firstWordT) || firstWordT - 3 < -1e-9) return null;
  if (t < firstWordT - 3 - 1e-9 || t >= firstWordT) return null;
  return Math.min(3, Math.max(1, Math.ceil(firstWordT - t - 1e-9)));
}

// Song time as m:ss (or m:ss.s with `tenths`). Negative times count as 0.
export function mmss(sec, tenths = false) {
  const s = Math.max(0, Number(sec) || 0);
  if (tenths) {
    const d = Math.floor(s * 10 + 1e-6), m = Math.floor(d / 600), r = d - m * 600;
    return `${m}:${String(Math.floor(r / 10)).padStart(2, '0')}.${r % 10}`;
  }
  const whole = Math.floor(s + 1e-6);
  return `${Math.floor(whole / 60)}:${String(whole % 60).padStart(2, '0')}`;
}

export const startCueText = (origin) => `Start the song at ${mmss(origin)}`;
