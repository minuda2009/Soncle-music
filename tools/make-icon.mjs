// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// Draws the Soncle app icon ("sound circle": a play mark in a disc, ringed by a circular waveform)
// and writes build/icon.png (1024), build/icon.ico (16–256) and renderer/icon.png (256).
// Sizes below 128 px use a simpler drawing without the waveform, so they stay crisp in the taskbar/tray.
//   node tools/make-icon.mjs            (needs: npm i --no-save @resvg/resvg-js)
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const { Resvg } = await import('@resvg/resvg-js');

const C = 512;
function svg({ bars = true, size = 1024 } = {}) {
  const defs = `<defs>
    <linearGradient id="bg" x1="0" y1="0" x2="1" y2="1">
      <stop offset="0" stop-color="#6E56FF"/><stop offset=".55" stop-color="#B14BE0"/><stop offset="1" stop-color="#FF6B8A"/>
    </linearGradient>
    <radialGradient id="glow" cx=".28" cy=".2" r=".75"><stop offset="0" stop-color="#fff" stop-opacity=".28"/><stop offset=".6" stop-color="#fff" stop-opacity="0"/></radialGradient>
    <linearGradient id="play" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#7A55FF"/><stop offset="1" stop-color="#E4568F"/></linearGradient>
  </defs>`;
  const bg = `<rect x="0" y="0" width="1024" height="1024" rx="232" fill="url(#bg)"/><rect x="0" y="0" width="1024" height="1024" rx="232" fill="url(#glow)"/>`;
  let ring = '';
  if (bars) {
    const N = 60, r0 = 268;
    for (let i = 0; i < N; i++) {
      const th = (i / N) * Math.PI * 2 - Math.PI / 2;
      // a smooth, music-like envelope around the circle (never zero, never too tall)
      const e = 0.5 + 0.5 * Math.sin(th * 3 + 0.9) * Math.cos(th * 2 - 0.4);
      const len = 34 + 118 * (0.25 + 0.75 * e) * (0.82 + 0.18 * Math.sin(th * 9));
      const x1 = C + Math.cos(th) * r0, y1 = C + Math.sin(th) * r0;
      const x2 = C + Math.cos(th) * (r0 + len), y2 = C + Math.sin(th) * (r0 + len);
      ring += `<line x1="${x1.toFixed(1)}" y1="${y1.toFixed(1)}" x2="${x2.toFixed(1)}" y2="${y2.toFixed(1)}"/>`;
    }
    ring = `<g stroke="#fff" stroke-opacity=".92" stroke-width="17" stroke-linecap="round">${ring}</g>`;
  }
  const discR = bars ? 212 : 300;
  const disc = `<circle cx="${C}" cy="${C}" r="${discR}" fill="#fff"/>`;
  // rounded play triangle, optically centred (shifted right a little)
  const s = discR * 0.5, cx = C + s * 0.14;
  const p1 = [cx - s * 0.62, C - s * 0.78], p2 = [cx - s * 0.62, C + s * 0.78], p3 = [cx + s * 0.86, C];
  const play = `<path d="M${p1} L${p2} L${p3} Z" fill="url(#play)" stroke="url(#play)" stroke-width="${(s * 0.22).toFixed(1)}" stroke-linejoin="round"/>`;
  return `<svg xmlns="http://www.w3.org/2000/svg" width="${size}" height="${size}" viewBox="0 0 1024 1024">${defs}${bg}${ring}${disc}${play}</svg>`;
}
const render = (opts, px) => new Resvg(svg(opts), { fitTo: { mode: 'width', value: px }, font: { loadSystemFonts: false } }).render().asPng();

// ICO with PNG-compressed entries (supported since Windows Vista)
function ico(entries) {
  const head = Buffer.alloc(6 + entries.length * 16);
  head.writeUInt16LE(0, 0); head.writeUInt16LE(1, 2); head.writeUInt16LE(entries.length, 4);
  let off = head.length;
  entries.forEach(({ size, png }, i) => {
    const o = 6 + i * 16;
    head.writeUInt8(size >= 256 ? 0 : size, o); head.writeUInt8(size >= 256 ? 0 : size, o + 1);
    head.writeUInt16LE(1, o + 4); head.writeUInt16LE(32, o + 6);
    head.writeUInt32LE(png.length, o + 8); head.writeUInt32LE(off, o + 12);
    off += png.length;
  });
  return Buffer.concat([head, ...entries.map((e) => e.png)]);
}

const out = (p, b) => { fs.writeFileSync(path.join(ROOT, p), b); console.log('wrote', p, b.length, 'bytes'); };
out('build/icon.png', render({ bars: true }, 1024));
out('renderer/icon.png', render({ bars: false }, 256));   // titlebar, window and tray: shown small
out('build/icon.ico', ico([16, 24, 32, 48, 64, 128, 256].map((size) => ({ size, png: render({ bars: size >= 128 }, size) }))));
if (process.argv.includes('--svg')) { fs.writeFileSync('/tmp/soncle-icon.svg', svg({ bars: true })); fs.writeFileSync('/tmp/soncle-icon-small.svg', svg({ bars: false })); }
