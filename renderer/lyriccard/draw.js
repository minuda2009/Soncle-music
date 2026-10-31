// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// Draws lyric cards on a canvas: background, words appearing on their timing, song name. The layout
// comes from layout.js, the word motion and sync cues from motion.js; export.js turns a card into a
// PNG or a (silent) video. Every cue here is drawn only: nothing reads or adds the song's audio.
import { FORMATS, buildTimeline, buildStill, safeRect } from './layout.js';
import { wordState, countdownAt, startCueText, mmss } from './motion.js';

const FONT = '"Segoe UI Variable Display","Segoe UI",Roboto,"Helvetica Neue",system-ui,sans-serif';
const MONO = 'ui-monospace,"Cascadia Mono","Roboto Mono",Menlo,Consolas,monospace';
const clamp01 = (x) => Math.max(0, Math.min(1, x));
const START_CUE_SECONDS = 3;
const PULSE_SECONDS = 0.25;

export const BACKGROUNDS = [['cover', 'Cover'], ['gradient', 'Colour'], ['black', 'Black']];

function paintBackground(ctx, W, H, kind, { cover, hue = 235, sat = 55 }) {
  if (kind === 'cover' && cover) {
    const s = Math.max(W / cover.width, H / cover.height) * 1.25;
    ctx.save();
    ctx.filter = 'blur(60px) saturate(1.35)';
    ctx.drawImage(cover, (W - cover.width * s) / 2, (H - cover.height * s) / 2, cover.width * s, cover.height * s);
    ctx.restore();
    ctx.fillStyle = 'rgb(0 0 0 / .5)';
    ctx.fillRect(0, 0, W, H);
  } else if (kind === 'black') {
    ctx.fillStyle = '#000'; ctx.fillRect(0, 0, W, H);
  } else {
    const g = ctx.createLinearGradient(0, 0, W * 0.4, H);
    g.addColorStop(0, `hsl(${hue} ${sat}% 30%)`); g.addColorStop(1, `hsl(${(hue + 35) % 360} ${sat}% 8%)`);
    ctx.fillStyle = g; ctx.fillRect(0, 0, W, H);
  }
}

// opts: { lines, format, background, align, upper, showTitle, title, artist, cover, hue, sat, accent, startAt?, endAt?,
//   safe (true: keep clear of Reels overlays on 9:16), textSize ('normal'|'large'|'huge'), motion (see MOTIONS),
//   startCue (burn in "Start the song at m:ss" for the first seconds), syncCues (3-2-1 countdown, line pulses,
//   song timecode), leadIn (at least this many seconds before the first word when not a clip) }
// The card's `countdown` says whether the 3-2-1 is actually shown (it needs 3 s before the first word).
// startAt/endAt (seconds into the song) make a clip whose video time 0 is startAt; see buildTimeline.
// `ctxForMeasure` is any 2D context (only measureText is used).
export function createCard(opts, ctxForMeasure) {
  const fmt = FORMATS[opts.format] || FORMATS.story;
  const { w: W, h: H } = fmt;
  const measure = (text, px, weight) => { ctxForMeasure.font = `${weight} ${px}px ${FONT}`; return ctxForMeasure.measureText(text).width; };
  const safe = opts.safe !== false, safeOn = safe && !!fmt.safe, area = safeRect(fmt, safe, !!opts.still);
  const motion = opts.motion || 'pop', accent = opts.accent || '#fff';
  const lo = { align: opts.align || 'center', upper: opts.upper !== false, startAt: opts.startAt, endAt: opts.endAt, safe, textSize: opts.textSize || 'normal', leadIn: opts.leadIn };
  const timeline = opts.still ? null : buildTimeline(opts.lines, fmt, measure, lo);
  const stillItems = opts.still ? buildStill(opts.lines, fmt, measure, lo) : null;
  const bg = typeof OffscreenCanvas !== 'undefined' ? new OffscreenCanvas(W, H) : Object.assign(document.createElement('canvas'), { width: W, height: H });
  const bctx = bg.getContext('2d');
  paintBackground(bctx, W, H, opts.background || 'cover', opts);
  const textColor = '#fff';
  const firstWordT = timeline?.scenes.length ? timeline.scenes[0].t0 : NaN;
  // Whether the 3-2-1 fits before the first word (it does not when a clip opens on or inside a line).
  const countdown = !!opts.syncCues && countdownAt(firstWordT - 3, firstWordT) !== null;

  function footer(ctx) {
    if (opts.showTitle === false || !opts.title) return;
    const px = Math.round(W * 0.03);
    // Inside the safe area the name sits at its bottom, above the platform's caption and music row.
    const x = safeOn ? area.x : fmt.w * 0.08, maxW = safeOn ? area.w : W - x * 2;
    const y = safeOn ? area.y + area.h - px * 0.3 - (opts.artist ? px * 1.2 : 0) : H - H * 0.07;
    ctx.textBaseline = 'alphabetic';
    ctx.font = `700 ${px}px ${FONT}`; ctx.fillStyle = 'rgb(255 255 255 / .92)';
    ctx.fillText(clip(ctx, opts.title, maxW), x, y);
    if (opts.artist) { ctx.font = `500 ${Math.round(px * 0.78)}px ${FONT}`; ctx.fillStyle = 'rgb(255 255 255 / .62)'; ctx.fillText(clip(ctx, opts.artist, maxW), x, y + px * 1.2); }
  }
  // "Start the song at m:ss" pill at the top of the safe area, with a play triangle drawn as a path.
  function startCue(ctx, t) {
    if (!opts.startCue || !timeline?.scenes.length || t > START_CUE_SECONDS + 0.4) return;
    const a = 1 - clamp01((t - START_CUE_SECONDS) / 0.4);
    const px = Math.round(W * 0.034), hgt = Math.round(px * 2), pad = px * 0.8, tri = px * 0.62;
    const text = startCueText(timeline.origin);
    ctx.save();
    ctx.globalAlpha = a;
    ctx.font = `700 ${px}px ${FONT}`;
    const wd = Math.min(area.w, pad * 2 + tri + px * 0.5 + ctx.measureText(text).width);
    const x = area.x, y = area.y + H * 0.005;
    ctx.fillStyle = 'rgb(0 0 0 / .55)';
    ctx.beginPath(); ctx.roundRect(x, y, wd, hgt, hgt / 2); ctx.fill();
    ctx.fillStyle = accent;
    const cx = x + pad, cy = y + hgt / 2;
    ctx.beginPath(); ctx.moveTo(cx, cy - tri / 2); ctx.lineTo(cx + tri * 0.87, cy); ctx.lineTo(cx, cy + tri / 2); ctx.closePath(); ctx.fill();
    ctx.fillStyle = '#fff'; ctx.textBaseline = 'middle';
    ctx.fillText(clip(ctx, text, wd - pad * 2 - tri - px * 0.5), cx + tri + px * 0.5, cy + px * 0.04);
    ctx.restore();
  }
  // Manual-sync helpers: a big 3-2-1 before the first word, a short pulse on every line start, and the
  // song time (origin + t) in the top corner, so the song can be lined up by hand in an editor later.
  function syncCues(ctx, t) {
    if (!opts.syncCues || !timeline?.scenes.length) return;
    ctx.save();
    const n = countdownAt(t, firstWordT);
    if (n) {
      const frac = clamp01(firstWordT - t - (n - 1));   // 1 -> 0 through each second
      const px = Math.round(Math.min(W, H) * 0.32);
      ctx.globalAlpha = 0.25 + 0.75 * frac;
      ctx.font = `800 ${px}px ${FONT}`; ctx.fillStyle = accent; ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
      ctx.fillText(String(n), area.x + area.w / 2, area.y + area.h / 2);
    }
    const tpx = Math.round(W * 0.026), tx = area.x + area.w, ty = area.y + H * 0.005 + tpx;
    let pulse = 0;
    for (const ls of timeline.lineStarts) if (t >= ls && t - ls < PULSE_SECONDS) pulse = Math.max(pulse, 1 - (t - ls) / PULSE_SECONDS);
    if (pulse > 0) {
      ctx.globalAlpha = pulse; ctx.fillStyle = accent;
      ctx.fillRect(area.x, area.y - H * 0.004, area.w, Math.max(4, H * 0.003));
    }
    ctx.font = `600 ${tpx}px ${MONO}`; ctx.textAlign = 'right'; ctx.textBaseline = 'alphabetic';
    const code = mmss(timeline.origin + t, true), cw = ctx.measureText(code).width;
    ctx.globalAlpha = 0.85; ctx.fillStyle = '#fff';
    ctx.fillText(code, tx, ty);
    ctx.globalAlpha = 0.35 + 0.65 * pulse; ctx.fillStyle = pulse > 0 ? accent : 'rgb(255 255 255 / .5)';
    ctx.beginPath(); ctx.arc(tx - cw - tpx * 0.6, ty - tpx * 0.35, tpx * (0.22 + 0.14 * pulse), 0, Math.PI * 2); ctx.fill();
    ctx.restore();
  }
  function clip(ctx, s, max) { if (ctx.measureText(s).width <= max) return s; while (s.length > 1 && ctx.measureText(s + '…').width > max) s = s.slice(0, -1); return s + '…'; }
  // st: a motion.wordState result; `out` is the scene's fade-out (1 = fully shown).
  function word(ctx, it, st, out = 1) {
    const alpha = st.alpha * out;
    if (alpha <= 0 || st.chars === 0) return;
    ctx.save();
    ctx.globalAlpha = clamp01(alpha);
    ctx.translate(it.x + it.w / 2 + st.dx, it.y + st.dy);
    ctx.scale(st.scale, st.scale);
    ctx.font = `${it.weight} ${it.px}px ${FONT}`;
    ctx.fillStyle = it.size === 'small' ? 'rgb(255 255 255 / .78)' : textColor;
    ctx.textBaseline = 'alphabetic';
    const text = st.chars < it.text.length ? it.text.slice(0, st.chars) : it.text;
    ctx.fillText(text, -it.w / 2, 0);
    if (st.highlight > 0 && st.highlight < 1) {   // karaoke: the sung part lit at full strength, left to right
      ctx.globalAlpha = clamp01(out);
      ctx.beginPath(); ctx.rect(-it.w / 2, -it.px * 1.2, it.w * st.highlight, it.px * 1.6); ctx.clip();
      ctx.fillText(text, -it.w / 2, 0);
    }
    ctx.restore();
  }
  const REST = { alpha: 1, scale: 1, dx: 0, dy: 0, highlight: 1, chars: Infinity };

  // Draws the frame at time `t` (seconds) onto ctx, scaled so the card's W x H fills `scale` of itself.
  function render(ctx, t, scale = 1) {
    ctx.save();
    ctx.setTransform(scale, 0, 0, scale, 0, 0);
    ctx.drawImage(bg, 0, 0);
    if (stillItems) for (const it of stillItems) word(ctx, it, REST);
    else {
      for (const s of timeline.scenes) {
        if (t < s.t0 - 0.01 || t > s.t1 + 0.01) continue;
        const out = 1 - clamp01((t - (s.t1 - 0.3)) / 0.3);
        for (const it of s.items) word(ctx, it, wordState(motion, t, it), out);
      }
      syncCues(ctx, t);
      startCue(ctx, t);
    }
    footer(ctx);
    ctx.restore();
  }
  return { W, H, duration: timeline ? timeline.duration : 0, render, timeline, stillItems, countdown };
}
