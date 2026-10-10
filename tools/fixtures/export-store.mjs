// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// Generates fixtures/store: the defaults and merge/upgrade cases the C# store (M03) must match.
// It runs today's JS (src/defaults.mjs, src/legacy.mjs) and writes plain JSON. Deterministic.
//
//   node tools/fixtures/export-store.mjs
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { defaultSettings, defaults } from '../../src/defaults.mjs';
import { upgradeSettings, BACKUP_MARKER, isBackupMarker } from '../../src/legacy.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const out = path.join(here, '..', '..', 'fixtures', 'store');
fs.mkdirSync(out, { recursive: true });

fs.writeFileSync(path.join(out, 'defaults.json'), JSON.stringify({ defaultSettings, defaults }, null, 1) + '\n');

// merge rule: { ...defaultSettings, ...raw.settings, eq: { ...defaultSettings.eq, ...raw.settings.eq } }
const merge = (raw) => ({ ...defaultSettings, ...(raw.settings || {}), eq: { ...defaultSettings.eq, ...(raw.settings?.eq || {}) } });

const cases = [];
// 1. an old file with no `welcomed` and a legacy xfCurve
{
  const raw = { settings: { xfCurve: 'metrolist', volume: 0.5 }, liked: ['a'] };
  upgradeSettings(raw.settings);
  cases.push({ name: 'legacy-xfcurve', raw, expected: merge(raw) });
}
// 2. partial settings keep defaults for the rest
{
  const raw = { settings: { eq: { preset: 'Rock' } } };
  upgradeSettings(raw.settings);
  cases.push({ name: 'partial-eq', raw, expected: merge(raw) });
}
// 3. unknown keys survive
{
  const raw = { settings: { futureKey: 42 }, extraTop: { x: 1 } };
  upgradeSettings(raw.settings);
  cases.push({ name: 'unknown-keys', raw, expected: merge(raw) });
}
fs.writeFileSync(path.join(out, 'merge-cases.json'), JSON.stringify(cases, null, 1) + '\n');

fs.writeFileSync(path.join(out, 'legacy.json'), JSON.stringify({
  backupMarker: BACKUP_MARKER,
  accepts: [BACKUP_MARKER, 'metrolist-desktop'],
  rejects: ['other', null]
}, null, 1) + '\n');

// a realistic library sample (no cookie/session)
const sample = {
  liked: [{ id: 'vid1', title: 'A Song', artists: [{ name: 'An Artist' }], duration: 200 }],
  playlists: [{ id: 'pl1', name: 'Chill', tracks: [] }],
  history: [{ id: 'vid1', at: 1700000000000 }],
  settings: { ...defaultSettings, volume: 0.6, theme: 'black', eq: { ...defaultSettings.eq, enabled: true, preset: 'Rock' } },
  deviceProfiles: { 'WH-1000XM4': { eq: { enabled: true, preset: 'Headphones' }, volume: 0.5, type: 'headphones', bluetooth: true, model: 'WH-1000XM4', updated: 1700000000000 } },
  lyricsOffsets: { vid1: 0.3 },
  eqPresets: [{ name: 'Mine', gains: [1, 2, 3, 4, 5, 4, 3, 2, 1, 0] }],
  skips: { vid2: 2 }
};
fs.writeFileSync(path.join(out, 'library.sample.json'), JSON.stringify(sample, null, 1) + '\n');

console.log('wrote fixtures/store');
