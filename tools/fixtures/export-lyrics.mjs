// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// Generates fixtures/lyrics: LRC samples and their parseLrc() result, from renderer/app.js.
// parseLrc is a UI-local function; this reimplements the same call path by loading app.js is not
// practical, so the expected JSON here is produced by a small copy of parseLrc kept in this file
// (identical to renderer/app.js). Re-running is deterministic.
//
//   node tools/fixtures/export-lyrics.mjs
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const out = path.join(here, '..', '..', 'fixtures', 'lyrics');
fs.mkdirSync(out, { recursive: true });

// verbatim from renderer/app.js
function parseLrc(lrc) {
  const out = [];
  const ts = (m, s) => Number(m) * 60 + Number(s);
  for (const line of lrc.split(/\r?\n/)) {
    const tags = [...line.matchAll(/\[(\d+):(\d+(?:\.\d+)?)\]/g)];
    if (!tags.length) continue;
    const body = line.replace(/\[[^\]]*\]/g, '');
    let words = null;
    if (/<\d+:\d+(?:\.\d+)?>/.test(body)) {
      words = [];
      const re = /<(\d+):(\d+(?:\.\d+)?)>([^<]*)/g;
      let m;
      while ((m = re.exec(body))) { const w = m[3]; if (w.trim()) words.push({ t: ts(m[1], m[2]), text: w }); else if (words.length) words[words.length - 1].end = ts(m[1], m[2]); }
      if (!words.length) words = null;
    }
    const text = body.replace(/<\d+:\d+(\.\d+)?>/g, '').replace(/\s+/g, ' ').trim();
    for (const m of tags) out.push({ t: ts(m[1], m[2]), text, words });
  }
  out.sort((a, b) => a.t - b.t);
  for (let i = 0; i < out.length; i++) out[i].end = out[i + 1] ? out[i + 1].t : out[i].t + 6;
  return out;
}

const samples = {
  'simple.lrc': '[00:12.50]First line\n[00:15.00]Second line\n[00:18.25]Third line\n',
  'multi-tag.lrc': '[00:01.00][00:31.00]Chorus\n[00:05.00]Verse\n',
  'enhanced.lrc': '[00:10.00]<00:10.00>Hello <00:10.80>world <00:11.50>\n[00:12.00]Plain\n',
  'no-tags.lrc': 'just text\n[00:03.00]Real line\n',
};

const cases = Object.entries(samples).map(([name, lrc]) => ({ name, lrc, lines: parseLrc(lrc) }));
fs.writeFileSync(path.join(out, 'lrc.json'), JSON.stringify({ cases }, null, 1) + '\n');
console.log('wrote fixtures/lyrics');
