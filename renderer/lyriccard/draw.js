// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// Draws lyric cards on a canvas: background, words appearing on their timing, song name. The layout
// comes from layout.js; export.js turns a card into a PNG or a (silent) video.
import { FORMATS, buildTimeline, buildStill } from './layout.js';

const FONT = '"Segoe UI Variable Display","Segoe UI",Roboto,"Helvetica Neue",system-ui,sans-serif';
const clamp01 = (x) => Math.max(0, Math.min(1, x));
const easeOut = (p) => 1 - (1 - p) ** 3;
const easeBack = (p) => { const c = 1.9; return 1 + (c + 1) * (p - 1) ** 3 + c * (p - 1) ** 2; };

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

// opts: { lines, format, background, align, upper, showTitle, title, artist, cover, hue, sat, accent, startAt?, endAt? }
// startAt/endAt (seconds into the song) make a clip whose video time 0 is startAt; see buildTimeline.
// `ctxForMeasure` is any 2D context (only measureText is used).
export function createCard(opts, ctxForMeasure) {
  const fmt = FORMATS[opts.format] || FORMATS.story;
  const { w: W, h: H } = fmt;
  const measure = (text, px, weight) => { ctxForMeasure.font = `${weight} ${px}px ${FONT}`; return ctxForMeasure.measureText(text).width; };
  const lo = { align: opts.align || 'center', upper: opts.upper !== false, startAt: opts.startAt, endAt: opts.endAt };
  const timeline = opts.still ? null : buildTimeline(opts.lines, fmt, measure, lo);
  const stillItems = opts.still ? buildStill(opts.lines, fmt, measure, lo) : null;
  const bg = typeof OffscreenCanvas !== 'undefined' ? new OffscreenCanvas(W, H) : Object.assign(document.createElement('canvas'), { width: W, height: H });
  const bctx = bg.getContext('2d');
  paintBackground(bctx, W, H, opts.background || 'cover', opts);
  const textColor = '#fff';

  function footer(ctx) {
    if (opts.showTitle === false || !opts.title) return;
    const px = Math.round(W * 0.03), x = fmt.w * 0.08, y = H - H * 0.07;
    ctx.textBaseline = 'alphabetic';
    ctx.font = `700 ${px}px ${FONT}`; ctx.fillStyle = 'rgb(255 255 255 / .92)';
    ctx.fillText(clip(ctx, opts.title, W - x * 2), x, y);
    if (opts.artist) { ctx.font = `500 ${Math.round(px * 0.78)}px ${FONT}`; ctx.fillStyle = 'rgb(255 255 255 / .62)'; ctx.fillText(clip(ctx, opts.artist, W - x * 2), x, y + px * 1.2); }
  }
  function clip(ctx, s, max) { if (ctx.measureText(s).width <= max) return s; while (s.length > 1 && ctx.measureText(s + '…').width > max) s = s.slice(0, -1); return s + '…'; }
  function word(ctx, it, alpha, scale, dy) {
    if (alpha <= 0) return;
    ctx.save();
    ctx.globalAlpha = clamp01(alpha);
    ctx.translate(it.x + it.w / 2, it.y + dy);
    ctx.scale(scale, scale);
    ctx.font = `${it.weight} ${it.px}px ${FONT}`;
    ctx.fillStyle = it.size === 'small' ? 'rgb(255 255 255 / .78)' : textColor;
    ctx.textBaseline = 'alphabetic';
    ctx.fillText(it.text, -it.w / 2, 0);
    ctx.restore();
  }

  // Draws the frame at time `t` (seconds) onto ctx, scaled so the card's W x H fills `scale` of itself.
  function render(ctx, t, scale = 1) {
    ctx.save();
    ctx.setTransform(scale, 0, 0, scale, 0, 0);
    ctx.drawImage(bg, 0, 0);
    if (stillItems) for (const it of stillItems) word(ctx, it, 1, 1, 0);
    else {
      for (const s of timeline.scenes) {
        if (t < s.t0 - 0.01 || t > s.t1 + 0.01) continue;
        const out = 1 - clamp01((t - (s.t1 - 0.3)) / 0.3);
        for (const it of s.items) {
          const p = clamp01((t - it.t) / 0.24);
          word(ctx, it, easeOut(p) * out, 0.86 + 0.14 * easeBack(p), (1 - easeOut(p)) * it.px * 0.14);
        }
      }
    }
    footer(ctx);
    ctx.restore();
  }
  return { W, H, duration: timeline ? timeline.duration : 0, render, timeline, stillItems };
}
