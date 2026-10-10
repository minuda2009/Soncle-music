// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// Generates fixtures/contract/api.json: every `window.api` member (from src/preload.cjs) with its
// kind (method/event), so the C# contracts (M02) can be checked against it and a JS test can fail
// if the surface changes without the fixture. Deterministic; no network.
//
//   node tools/fixtures/export-contract.mjs
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const root = path.join(here, '..', '..');
const out = path.join(root, 'fixtures', 'contract');
fs.mkdirSync(out, { recursive: true });

const src = fs.readFileSync(path.join(root, 'src', 'preload.cjs'), 'utf8');
const body = src.slice(src.indexOf('exposeInMainWorld'), src.lastIndexOf('}'));
const memberRx = /^\s{2}([A-Za-z_][A-Za-z0-9_]*)\s*:/gm;
const members = [];
let m;
while ((m = memberRx.exec(body))) members.push(m[1]);

const events = members.filter((n) => /^on[A-Z]/.test(n));
const properties = ['platform'];
const methods = members.filter((n) => !events.includes(n) && !properties.includes(n));

// return-shape hints, from how app.js uses each method (kept short; the C# models are the real spec)
const shapes = {
  home: 'Page', homeMore: 'Page', explore: 'Page', browse: 'Page', search: 'Page', searchMore: 'Page',
  album: 'Album', artist: 'Artist', playlist: 'Playlist', upNext: 'Page', radio: 'Page', related: 'Page',
  library: 'Library', songInfo: 'SongInfo', lyrics: 'LyricsResult', hpProfile: 'HeadphoneProfile',
  downloads: 'Downloads', info: 'AppInfo', power: 'PowerState',
};

const api = {
  platform: 'android|win32|darwin|linux',
  members: members.map((name) => ({
    name,
    kind: events.includes(name) ? 'event' : properties.includes(name) ? 'property' : 'method',
    returns: shapes[name] || null,
  })),
  counts: { methods: methods.length, events: events.length, properties: properties.length },
};

fs.writeFileSync(path.join(out, 'api.json'), JSON.stringify(api, null, 1) + '\n');
console.log(`wrote fixtures/contract/api.json (${members.length} members)`);
