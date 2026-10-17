// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// Soncle's colours: the Material-style palette the apps and the browser extension are tinted with,
// from one hue and saturation (an accent, or the dominant colour of the cover art).

export function rgbToHsl(r, g, b) {
  r /= 255; g /= 255; b /= 255;
  const max = Math.max(r, g, b), min = Math.min(r, g, b);
  let hh = 0, s = 0; const l = (max + min) / 2;
  if (max !== min) {
    const d = max - min;
    s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
    hh = max === r ? (g - b) / d + (g < b ? 6 : 0) : max === g ? (b - r) / d + 2 : (r - g) / d + 4;
    hh *= 60;
  }
  return [hh, s * 100, l * 100];
}
export function hslToRgb(hh, s, l) {
  s /= 100; l /= 100;
  const k = (n) => (n + hh / 30) % 12, a = s * Math.min(l, 1 - l);
  const f = (n) => l - a * Math.max(-1, Math.min(k(n) - 3, Math.min(9 - k(n), 1)));
  return [Math.round(f(0) * 255), Math.round(f(8) * 255), Math.round(f(4) * 255)];
}
export const hexToHsl = (hex) => rgbToHsl(parseInt(hex.slice(1, 3), 16), parseInt(hex.slice(3, 5), 16), parseInt(hex.slice(5, 7), 16));
export const hsl = (hh, s, l) => `hsl(${hh.toFixed(1)} ${s.toFixed(1)}% ${l.toFixed(1)}%)`;

// The CSS variables for a theme. mode: 'dark' | 'black' | 'light'. Returns { '--primary': 'hsl(...)', ... }.
export function themeVars(hue, sat, mode) {
  const s = Math.min(sat, 80);
  const v = {};
  if (mode === 'light') {
    v['--primary'] = hsl(hue, Math.max(Math.min(s, 70), 35), 40);
    v['--on-primary'] = '#ffffff';
    v['--primary-container'] = hsl(hue, Math.max(Math.min(s, 80), 40), 88);
    v['--on-primary-container'] = hsl(hue, Math.max(s, 40), 14);
    const ts = Math.min(s, 28);
    v['--bg'] = hsl(hue, ts, 94);
    v['--surface'] = hsl(hue, ts, 98.5);
    v['--surface-rgb'] = hslToRgb(hue, ts, 98.5).join(' ');
    v['--surface-1'] = hsl(hue, ts, 95.5);
    v['--surface-2'] = hsl(hue, ts, 93.5);
    v['--surface-3'] = hsl(hue, ts, 91);
    v['--surface-4'] = hsl(hue, ts, 86);
    v['--on-surface'] = hsl(hue, 12, 11);
    v['--on-surface-var'] = hsl(hue, 8, 32);
    v['--outline'] = hsl(hue, 6, 48);
    v['--outline-var'] = hsl(hue, 10, 80);
    v['--error'] = '#ba1a1a';
    v['--ov'] = '0 0 0';
    v['--scrim'] = '60 60 80';
  } else {
    const black = mode === 'black';
    v['--primary'] = hsl(hue, Math.max(s, 35), 80);
    v['--on-primary'] = hsl(hue, Math.max(s, 30), 18);
    v['--primary-container'] = hsl(hue, Math.min(s, 42), 28);
    v['--on-primary-container'] = hsl(hue, Math.max(s, 40), 90);
    const ts = Math.min(s, 22);
    const L = black ? [0, 3.5, 7, 9, 12.5, 17] : [5.5, 8, 11, 13, 17, 22];
    v['--bg'] = black ? '#000' : hsl(hue, ts, L[0]);
    v['--surface'] = hsl(hue, ts, L[1]);
    v['--surface-rgb'] = hslToRgb(hue, ts, L[1]).join(' ');
    v['--surface-1'] = hsl(hue, ts, L[2]);
    v['--surface-2'] = hsl(hue, ts, L[3]);
    v['--surface-3'] = hsl(hue, ts, L[4]);
    v['--surface-4'] = hsl(hue, ts, L[5]);
    v['--on-surface'] = hsl(hue, 12, 90);
    v['--on-surface-var'] = hsl(hue, 10, 79);
    v['--outline'] = hsl(hue, 6, 58);
    v['--outline-var'] = hsl(hue, Math.min(s, 10), 29);
    v['--error'] = '#ffb4ab';
    v['--ov'] = '255 255 255';
    v['--scrim'] = '0 0 0';
  }
  return v;
}

// The dominant [hue, saturation] of an image (RGBA bytes of a small copy, e.g. 48x48), or null for a
// grey one. Favours saturated mid-tones so a mostly dark cover still gives its colour.
export function dominantHue(d) {
  const buckets = new Map();
  for (let i = 0; i < d.length; i += 4) {
    const [hh, s, l] = rgbToHsl(d[i], d[i + 1], d[i + 2]);
    if (l < 12 || l > 92) continue;
    const key = Math.round(hh / 15);
    const w = (s / 100) ** 1.5 * (1 - Math.abs(l - 50) / 60);
    const b = buckets.get(key) || { w: 0, h: 0, s: 0 };
    b.w += w; b.h += hh * w; b.s += s * w;
    buckets.set(key, b);
  }
  let best = null;
  for (const b of buckets.values()) if (!best || b.w > best.w) best = b;
  return best && best.w > 2 ? [best.h / best.w, best.s / best.w] : null;
}
