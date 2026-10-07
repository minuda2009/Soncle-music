#!/usr/bin/env node
// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// Bundles the core source into one Markdown file for a Claude Project's knowledge:
// the parts that hold the app's logic and will be ported to C#. The UI (renderer/app.js, ~250 KB)
// and Electron wiring are left out to keep it small; the repo stays the source of truth.
//   node tools/project-knowledge.mjs [out.md]      (default: Soncle-core-source.md)
import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const FILES = [
  ['The contract between UI and platform (every app must provide this API)', ['src/preload.cjs', 'src/defaults.mjs']],
  ['YouTube Music access and streaming', ['src/yt.mjs', 'src/botguard.mjs', 'src/potoken.mjs', 'src/streamproxy.mjs']],
  ['Audio engine', ['renderer/engine.js', 'renderer/audio/worklets.js']],
  ['Flow radio (tempo, key, energy, ordering)', ['renderer/flow/analyze.js', 'renderer/flow/flow.js', 'renderer/flow/sampler.js', 'renderer/flow/webm.js']],
  ['Android app', ['mobile/src/backend.js', 'mobile/src/native.js', 'mobile/src/mse.js', 'mobile/android/app/src/main/java/com/minuda2009/soncle/SonclePlugin.java', 'mobile/android/app/src/main/java/com/minuda2009/soncle/MainActivity.java']],
  ['Plans', ['ROADMAP.md']]
];
const lang = { '.mjs': 'js', '.js': 'js', '.cjs': 'js', '.java': 'java', '.md': 'markdown' };
let commit = '';
try { commit = execFileSync('git', ['rev-parse', '--short', 'HEAD'], { cwd: ROOT }).toString().trim(); } catch {}
const out = [`# Soncle core source (snapshot${commit ? ' of commit ' + commit : ''}, ${new Date().toISOString().slice(0, 10)})`, '',
  'Reference copy for planning and porting. The repo minuda2009/Soncle-music is the source of truth; if this disagrees with it, the repo wins.',
  'Not included: renderer/app.js (the UI), src/main.mjs (Electron wiring), tests, tools.', ''];
for (const [title, files] of FILES) {
  out.push(`## ${title}`, '');
  for (const f of files) {
    const p = path.join(ROOT, f);
    if (!fs.existsSync(p)) continue;
    out.push(`### \`${f}\``, '', '````' + (lang[path.extname(f)] || ''), fs.readFileSync(p, 'utf8').trimEnd(), '````', '');
  }
}
const dest = process.argv[2] || path.join(ROOT, 'Soncle-core-source.md');
fs.writeFileSync(dest, out.join('\n'));
console.log(`${dest}: ${(fs.statSync(dest).size / 1024).toFixed(0)} KB`);
