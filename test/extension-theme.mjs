// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const root = path.join(here, '..');
// theme.js imports ./palette.js — load from shared paths via a tiny shim dir
const tmp = path.join(root, '.tmp', 'ext-theme-test');
mkdirSync(tmp, { recursive: true });
writeFileSync(path.join(tmp, 'palette.js'), readFileSync(path.join(root, 'renderer', 'palette.js')));
writeFileSync(path.join(tmp, 'theme.js'), readFileSync(path.join(root, 'extension', 'src', 'theme.js')));

const { cssFor, isStyleOnly, DEFAULTS, ACCENTS } = await import(path.join(tmp, 'theme.js'));

test('cssFor emits Soncle tokens and mapped YTM variables', () => {
  const css = cssFor(250, 40, 'dark');
  assert.match(css, /--sc-bg:/);
  assert.match(css, /--ytmusic-player-bar-background:\s*var\(--sc-surface-2\)/);
  assert.match(css, /--yt-spec-call-to-action:\s*var\(--sc-primary\)/);
  assert.match(css, /ytmusic-nav-bar/);
  assert.match(css, /border-radius:\s*14px/);
});

test('style layer never hides or blocks', () => {
  for (const mode of ['dark', 'black', 'light']) {
    const css = cssFor(200, 50, mode);
    assert.equal(isStyleOnly(css), true, mode);
    assert.doesNotMatch(css, /display\s*:\s*none/i);
    assert.doesNotMatch(css, /ytd-ad|masthead-ad/i);
  }
});

test('defaults and accents are stable', () => {
  assert.equal(DEFAULTS.on, true);
  assert.equal(ACCENTS.length, 9);
});
