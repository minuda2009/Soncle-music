// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// Guards fixtures/contract/api.json: if preload.cjs or backend.js gains or loses a `window.api`
// member, this fails, so the C# contracts (M02) and the fixture stay in step with the JS.
import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const root = path.join(here, '..');

function members(file, marker, stopAt) {
  const src = fs.readFileSync(path.join(root, file), 'utf8');
  const start = src.indexOf(marker);
  const stop = stopAt ? src.indexOf(stopAt, start) : -1;
  const end = src.indexOf('\n};', start);
  const limit = stop >= 0 && (end < 0 || stop < end) ? stop : end < 0 ? src.length : end;
  const body = src.slice(start, limit);
  const names = new Set();
  // top-level keys of the exposed object: two-space indent in preload, and both two- and four-space
  // nested keys in backend are excluded by requiring the key not to be preceded by more indent
  const rx = /^\s*([A-Za-z_][A-Za-z0-9_]*)\s*:/gm;
  let m;
  while ((m = rx.exec(body))) names.add(m[1]);
  return names;
}

const fixture = JSON.parse(fs.readFileSync(path.join(root, 'fixtures', 'contract', 'api.json'), 'utf8'));
const listed = new Set(fixture.members.map((x) => x.name));

test('preload.cjs api members are all listed in fixtures/contract/api.json', () => {
  const preload = members('src/preload.cjs', 'exposeInMainWorld');
  const missing = [...preload].filter((n) => !listed.has(n));
  const extra = [...listed].filter((n) => !preload.has(n));
  assert.deepEqual(missing, [], 'new members not in the fixture');
  assert.deepEqual(extra, [], 'fixture members no longer in preload.cjs');
});

test('backend.js provides the same api members as preload.cjs (plus its documented extras)', () => {
  const preload = members('src/preload.cjs', 'exposeInMainWorld');
  const backend = members('mobile/src/backend.js', 'const api = {', 'globalThis.api');
  const androidExtras = new Set(['platform', 'mobile', 'srcFor', 'range', 'diagnostics', 'bluetoothPermission']);
  const missing = [...preload].filter((n) => !backend.has(n) && n !== 'pathForFile');
  assert.deepEqual(missing.filter((n) => !androidExtras.has(n)), [], 'backend is missing preload members');
});
