// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { lookupLyrics, cleanTitle } from '../extension/src/lyrics.js';
import { parseLrc } from '../renderer/lrc.js';

const root = path.join(path.dirname(fileURLToPath(import.meta.url)), '..');

test('the extension builds, and every file it names or imports is in the build', () => {
  const out = fs.mkdtempSync(path.join(os.tmpdir(), 'soncle-ext-'));
  execFileSync('node', [path.join(root, 'extension', 'build.mjs'), out], { stdio: 'pipe' });
  const manifest = JSON.parse(fs.readFileSync(path.join(out, 'manifest.json'), 'utf8'));
  assert.equal(manifest.manifest_version, 3);
  const need = [manifest.background.service_worker, ...manifest.content_scripts.flatMap((c) => c.js), ...Object.values(manifest.icons)];
  for (const f of need) assert.ok(fs.existsSync(path.join(out, f)), `${f} is in the build`);
  // content.js loads shared/*.js by url('...'); background.js imports ./shared/...
  const imports = [...fs.readFileSync(path.join(out, 'content.js'), 'utf8').matchAll(/url\('([^']+\.js)'\)/g)].map((m) => m[1]);
  imports.push(...[...fs.readFileSync(path.join(out, 'background.js'), 'utf8').matchAll(/from '\.\/([^']+)'/g)].map((m) => m[1]));
  assert.ok(imports.length >= 5);
  for (const f of imports) assert.ok(fs.existsSync(path.join(out, f)), `${f} is in the build`);
});

test('the manifest asks for the least it needs and says what it is', () => {
  const m = JSON.parse(fs.readFileSync(path.join(root, 'extension', 'manifest.json'), 'utf8'));
  assert.deepEqual(m.permissions, ['storage']);
  assert.deepEqual(m.host_permissions, ['https://lrclib.net/*']);
  assert.deepEqual(m.content_scripts.map((c) => c.matches).flat(), ['https://music.youtube.com/*']);
  assert.match(m.description, /unofficial/i);
  assert.match(m.description, /not affiliated/i);
  assert.match(m.description, /does not block ads/i);
});

test('the page script reads only the title and clock, never cookies or the audio', () => {
  const src = fs.readFileSync(path.join(root, 'extension', 'src', 'content.js'), 'utf8');
  for (const bad of ['document.cookie', 'createMediaElementSource', 'declarativeNetRequest', 'webRequest']) assert.ok(!src.includes(bad), bad);
});

test('titles lose their "(Official Video)" style tags', () => {
  assert.equal(cleanTitle('Song Name (Official Video)'), 'Song Name');
  assert.equal(cleanTitle('Song [Remastered 2011]'), 'Song');
  assert.equal(cleanTitle('Song (Live)'), 'Song (Live)');
});

const res = (body, ok = true) => ({ ok, json: async () => body });
const T = { title: 'Song (Official Audio)', artists: [{ name: 'Band' }, { name: 'Other' }], album: { name: 'Rec' }, duration: 200.4 };

test('lookup asks LRCLIB for the first artist, the cleaned title and the rounded duration', async () => {
  let seen;
  const r = await lookupLyrics(T, async (u) => { seen = new URL(u); return res({ syncedLyrics: '[00:01.00] hi', plainLyrics: 'hi' }); });
  assert.equal(seen.pathname, '/api/get');
  assert.equal(seen.searchParams.get('track_name'), 'Song');
  assert.equal(seen.searchParams.get('artist_name'), 'Band');
  assert.equal(seen.searchParams.get('album_name'), 'Rec');
  assert.equal(seen.searchParams.get('duration'), '200');
  assert.deepEqual(r, { synced: '[00:01.00] hi', plain: 'hi', source: 'LRCLIB' });
});

test('with no exact match it searches and prefers a synced result of about the same length', async () => {
  const calls = [];
  const r = await lookupLyrics(T, async (u) => {
    calls.push(new URL(u).pathname);
    if (calls.length === 1) return res({}, false);
    return res([{ syncedLyrics: null, plainLyrics: 'p', duration: 200 }, { syncedLyrics: 'wrong', duration: 300 }, { syncedLyrics: '[00:02.00] ok', duration: 202 }]);
  });
  assert.deepEqual(calls, ['/api/get', '/api/search']);
  assert.equal(r.synced, '[00:02.00] ok');
});

test('no result, a failing network, or an empty title gives null', async () => {
  assert.equal(await lookupLyrics(T, async () => res([], true)), null);
  assert.equal(await lookupLyrics(T, async () => { throw new Error('offline'); }), null);
  assert.equal(await lookupLyrics({ title: '  ', artists: [] }, async () => assert.fail('no request')), null);
});

test('the shared LRC parser reads plain and enhanced lines and gives each an end time', () => {
  const lines = parseLrc('[00:01.50] one\n[00:04.00]<00:04.00>two <00:05.00>three\n[00:10.00]');
  assert.equal(lines.length, 3);
  assert.deepEqual([lines[0].t, lines[0].end, lines[0].text], [1.5, 4, 'one']);
  assert.deepEqual(lines[1].words.map((w) => w.text.trim()), ['two', 'three']);
  assert.equal(lines[2].text, '');
  assert.equal(lines[2].end, 16);
});

// ---- the Soncle style layer (imported from a real build, where palette.js sits beside theme.js) ----
async function builtTheme() {
  const out = fs.mkdtempSync(path.join(os.tmpdir(), 'soncle-ext-theme-'));
  execFileSync('node', [path.join(root, 'extension', 'build.mjs'), out], { stdio: 'pipe' });
  return import(path.join(out, 'shared', 'theme.js'));
}

test('the style points the site\'s own variables at Soncle\'s palette, in every mode', async () => {
  const { cssFor } = await builtTheme();
  for (const mode of ['dark', 'black', 'light']) {
    const css = cssFor(250, 40, mode);
    assert.match(css, /--sc-primary: hsl\(/);
    assert.match(css, /--ytmusic-general-background-c: var\(--sc-bg\)/);
    assert.match(css, /--yt-spec-text-primary: var\(--sc-on-surface\)/);
    assert.match(css, new RegExp(`color-scheme: ${mode === 'light' ? 'light' : 'dark'}`));
  }
  assert.match(cssFor(250, 40, 'black'), /--sc-bg: #000;/);
});

test('the style only recolours and rounds: it never hides, removes or blocks anything', async () => {
  const { cssFor } = await builtTheme();
  const css = cssFor(250, 40, 'dark');
  for (const bad of ['display: none', 'display:none', 'visibility', 'opacity: 0', 'ad-', 'promo', 'pointer-events', 'remove']) assert.ok(!css.includes(bad), bad);
});

test('mode and accent settings resolve sensibly', async () => {
  const { resolveMode, accentHs, DEFAULTS, ACCENTS } = await builtTheme();
  assert.equal(resolveMode('system', true), 'dark');
  assert.equal(resolveMode('system', false), 'light');
  assert.equal(resolveMode('black', false), 'black');
  assert.equal(resolveMode('nonsense', false), 'dark');
  assert.deepEqual(accentHs('not a colour'), accentHs(ACCENTS[0]));
  assert.equal(DEFAULTS.on, true);
});
