// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { carryOver, isBackupMarker, upgradeSettings, LEGACY, BACKUP_MARKER } from '../src/legacy.mjs';

test('appId is the same in package.json and main.mjs', () => {
  const pkg = JSON.parse(fs.readFileSync(new URL('../package.json', import.meta.url), 'utf8'));
  const main = fs.readFileSync(new URL('../src/main.mjs', import.meta.url), 'utf8');
  const id = /APP_ID = '([^']+)'/.exec(main)[1];
  assert.equal(pkg.build.appId, id);
  assert.match(id, /^com\.[a-z0-9]+\.soncle$/);
});

test('user data is copied (not moved) from the old folder exactly once', () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'carry-'));
  const oldDir = path.join(root, LEGACY.dirName), newDir = path.join(root, 'Soncle');
  fs.mkdirSync(path.join(oldDir, 'downloads'), { recursive: true });
  fs.writeFileSync(path.join(oldDir, 'downloads', 'abc.webm'), 'audio');
  fs.writeFileSync(path.join(oldDir, 'flow-features.json'), '{"x":1}');
  fs.writeFileSync(path.join(oldDir, 'Local State'), '{}');
  const lib = { liked: [{ id: 'a' }], settings: { xfCurve: LEGACY.xfCurve, volume: 0.5 }, downloads: { abc: { file: path.join(oldDir, 'downloads', 'abc.webm') } } };
  fs.writeFileSync(path.join(oldDir, 'library.json'), JSON.stringify(lib));

  const r = carryOver(root, newDir);
  assert.deepEqual(r.copied, ['library.json', 'downloads', 'flow-features.json', 'Local State']);
  const got = JSON.parse(fs.readFileSync(path.join(newDir, 'library.json'), 'utf8'));
  assert.deepEqual(got.liked, [{ id: 'a' }]);
  assert.equal(got.settings.xfCurve, 'smooth');
  assert.equal(got.downloads.abc.file, path.join(newDir, 'downloads', 'abc.webm'));
  assert.equal(fs.readFileSync(path.join(newDir, 'downloads', 'abc.webm'), 'utf8'), 'audio');
  // the old data is untouched
  assert.equal(JSON.parse(fs.readFileSync(path.join(oldDir, 'library.json'), 'utf8')).settings.xfCurve, LEGACY.xfCurve);
  assert.ok(fs.existsSync(path.join(oldDir, 'downloads', 'abc.webm')));
  // never twice, and never over a library that is already there
  fs.writeFileSync(path.join(newDir, 'library.json'), '{"liked":[]}');
  assert.equal(carryOver(root, newDir), null);
  assert.equal(fs.readFileSync(path.join(newDir, 'library.json'), 'utf8'), '{"liked":[]}');
});

test('fresh installs just mark it done', () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'carry-'));
  assert.deepEqual(carryOver(root, path.join(root, 'Soncle')).copied, []);
  assert.equal(carryOver(root, path.join(root, 'Soncle')), null);
});

test('old and new backups are both accepted', () => {
  assert.ok(isBackupMarker(BACKUP_MARKER));
  for (const m of LEGACY.backupMarkers) assert.ok(isBackupMarker(m));
  assert.ok(!isBackupMarker('something-else'));
  assert.equal(upgradeSettings({ xfCurve: LEGACY.xfCurve }).xfCurve, 'smooth');
});
