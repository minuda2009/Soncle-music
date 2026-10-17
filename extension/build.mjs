// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// Builds the browser extension into extension/dist/: its own files plus the modules it shares with
// the apps (renderer/lrc.js, renderer/lyriccard/*), copied unchanged so nothing is forked.
//   node extension/build.mjs [outDir]
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const root = path.join(here, '..');
const out = path.resolve(process.argv[2] || path.join(here, 'dist'));

fs.rmSync(out, { recursive: true, force: true });
fs.mkdirSync(path.join(out, 'shared'), { recursive: true });
fs.copyFileSync(path.join(here, 'manifest.json'), path.join(out, 'manifest.json'));
fs.copyFileSync(path.join(root, 'renderer', 'icon.png'), path.join(out, 'icon.png'));
fs.copyFileSync(path.join(here, 'src', 'background.js'), path.join(out, 'background.js'));
fs.copyFileSync(path.join(here, 'src', 'content.js'), path.join(out, 'content.js'));
fs.copyFileSync(path.join(here, 'src', 'lyrics.js'), path.join(out, 'shared', 'lyrics.js'));
fs.copyFileSync(path.join(root, 'renderer', 'lrc.js'), path.join(out, 'shared', 'lrc.js'));
fs.cpSync(path.join(root, 'renderer', 'lyriccard'), path.join(out, 'shared', 'lyriccard'), { recursive: true });
fs.copyFileSync(path.join(root, 'LICENSE'), path.join(out, 'LICENSE'));
console.log('extension built in', out);
