// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';

test('files mirrored from upstream exist here', () => {
  const cfg = JSON.parse(fs.readFileSync(new URL('../upstream/upstream.json', import.meta.url), 'utf8'));
  assert.ok(cfg.copy.length && cfg.copy.every((c) => fs.existsSync(new URL('../' + c.to, import.meta.url))));
  assert.ok(!('icons' in cfg));   // UI icons come from Material Symbols, not from upstream
});

test('every icon comes from Material Symbols', () => {
  const src = fs.readFileSync(new URL('../renderer/icons.js', import.meta.url), 'utf8');
  const I = JSON.parse(/^const I = (\{.*?\});$/m.exec(src)[1]);
  const sources = JSON.parse(fs.readFileSync(new URL('../tools/icon-sources.json', import.meta.url), 'utf8'));
  for (const name of Object.keys(I)) assert.match(sources[name] || '', /^Material Symbols/, name);
});
