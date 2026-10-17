// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// fixtures/palette/golden.json was produced by the theme code as it was inside renderer/app.js before
// it moved to renderer/palette.js; the shared module must give exactly the same colours.
import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { themeVars, hexToHsl, dominantHue } from '../renderer/palette.js';

const root = path.join(path.dirname(fileURLToPath(import.meta.url)), '..');
const golden = JSON.parse(fs.readFileSync(path.join(root, 'fixtures', 'palette', 'golden.json'), 'utf8'));

test('themeVars equals the colours the app produced before the move', () => {
  assert.equal(golden.cases.length, 15);
  for (const c of golden.cases) assert.deepEqual(themeVars(c.hue, c.sat, c.mode), c.vars, `${c.mode} ${c.hue}/${c.sat}`);
});

test('hexToHsl equals the old results', () => {
  for (const [hex, hs] of golden.hex) assert.deepEqual(hexToHsl(hex), hs);
});

test('dominantHue finds the colour of a cover and ignores grey', () => {
  const px = (r, g, b, n) => Array.from({ length: n }, () => [r, g, b, 255]).flat();
  const red = dominantHue(new Uint8ClampedArray([...px(200, 40, 40, 300), ...px(30, 30, 30, 500)]));
  assert.ok(red && (red[0] < 10 || red[0] > 350), `hue ${red?.[0]}`);
  assert.equal(dominantHue(new Uint8ClampedArray(px(128, 128, 128, 800))), null);
});
