// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// Writes the Android launcher icons (legacy, round and adaptive layers) and splash screens from
// assets/soncle-icon.svg.   node tools/android-icons.mjs   (needs: npm i --no-save @resvg/resvg-js)
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const res = path.join(here, '..', 'android', 'app', 'src', 'main', 'res');
const { Resvg } = await import(process.env.RESVG || '@resvg/resvg-js');
const src = fs.readFileSync(path.join(here, '..', '..', 'assets', 'soncle-icon.svg'), 'utf8');
const defs = src.slice(src.indexOf('<defs>'), src.indexOf('</defs>') + 7);
const glyph = src.slice(src.indexOf('<g stroke'), src.lastIndexOf('</svg>'));
const BG = '#0e0e13';

const wrap = (body, w = 1024, h = 1024, vb = '0 0 1024 1024') => `<svg xmlns="http://www.w3.org/2000/svg" width="${w}" height="${h}" viewBox="${vb}">${defs}${body}</svg>`;
const png = (svg, width) => new Resvg(svg, { fitTo: { mode: 'width', value: width }, font: { loadSystemFonts: false } }).render().asPng();
const put = (rel, buf) => { const f = path.join(res, rel); fs.mkdirSync(path.dirname(f), { recursive: true }); fs.writeFileSync(f, buf); };

const full = (rx) => wrap(`<rect width="1024" height="1024" rx="${rx}" fill="url(#bg)"/><rect width="1024" height="1024" rx="${rx}" fill="url(#glow)"/>${glyph}`);
const layerBg = wrap('<rect width="1024" height="1024" fill="url(#bg)"/><rect width="1024" height="1024" fill="url(#glow)"/>');
// adaptive foreground: the mark must sit inside the central 66/108 safe zone
const layerFg = wrap(`<g transform="translate(512 512) scale(.74) translate(-512 -512)">${glyph}</g>`);

const DENS = { mdpi: 1, hdpi: 1.5, xhdpi: 2, xxhdpi: 3, xxxhdpi: 4 };
for (const [d, k] of Object.entries(DENS)) {
  put(`mipmap-${d}/ic_launcher.png`, png(full(232), 48 * k));
  put(`mipmap-${d}/ic_launcher_round.png`, png(full(512), 48 * k));
  put(`mipmap-${d}/ic_launcher_foreground.png`, png(layerFg, 108 * k));
  put(`mipmap-${d}/ic_launcher_background.png`, png(layerBg, 108 * k));
}
const adaptive = '<?xml version="1.0" encoding="utf-8"?>\n<adaptive-icon xmlns:android="http://schemas.android.com/apk/res/android">\n    <background android:drawable="@mipmap/ic_launcher_background"/>\n    <foreground android:drawable="@mipmap/ic_launcher_foreground"/>\n</adaptive-icon>\n';
put('mipmap-anydpi-v26/ic_launcher.xml', Buffer.from(adaptive));
put('mipmap-anydpi-v26/ic_launcher_round.xml', Buffer.from(adaptive));
fs.rmSync(path.join(res, 'drawable-v24', 'ic_launcher_foreground.xml'), { force: true });
fs.rmSync(path.join(res, 'drawable', 'ic_launcher_background.xml'), { force: true });

// splash: dark background, icon in the middle (replaces Capacitor's default artwork)
const splash = (w, h) => {
  const s = Math.round(Math.min(w, h) * 0.28);
  return png(`<svg xmlns="http://www.w3.org/2000/svg" width="${w}" height="${h}" viewBox="0 0 ${w} ${h}"><rect width="${w}" height="${h}" fill="${BG}"/><svg x="${(w - s) / 2}" y="${(h - s) / 2}" width="${s}" height="${s}" viewBox="0 0 1024 1024">${defs}<rect width="1024" height="1024" rx="232" fill="url(#bg)"/><rect width="1024" height="1024" rx="232" fill="url(#glow)"/>${glyph}</svg></svg>`, w);
};
const PORT = { mdpi: [320, 480], hdpi: [480, 800], xhdpi: [720, 1280], xxhdpi: [960, 1600], xxxhdpi: [1280, 1920] };
for (const [d, [w, h]] of Object.entries(PORT)) { put(`drawable-port-${d}/splash.png`, splash(w, h)); put(`drawable-land-${d}/splash.png`, splash(h, w)); }
put('drawable/splash.png', splash(480, 320));
console.log('Android icons and splash written');
