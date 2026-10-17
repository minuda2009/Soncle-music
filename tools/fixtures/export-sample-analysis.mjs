// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// Generates fixtures/flow/sample-analysis.json: what renderer/flow/analyze.js makes of
// fixtures/flow/sample.webm after ffmpeg decodes it to stereo 22.05 kHz and the channels are averaged (what Chromium's
// OfflineAudioContext(1, 1, 22050) hands the JS analyzer). The C# path (Concentus decode, Resampler,
// Analyzer) must land on the same tempo, key and energy. Needs ffmpeg on PATH.
//
//   node tools/fixtures/export-sample-analysis.mjs
import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { analyze } from '../../renderer/flow/analyze.js';

const dir = path.join(path.dirname(fileURLToPath(import.meta.url)), '..', '..', 'fixtures', 'flow');
const raw = execFileSync('ffmpeg', ['-v', 'error', '-i', path.join(dir, 'sample.webm'), '-f', 'f32le', '-ar', '22050', '-ac', '2', 'pipe:1'], { maxBuffer: 1 << 28 });
const st = new Float32Array(raw.buffer, raw.byteOffset, raw.length / 4);
// the channels are averaged exactly as sampler.js's decode() does (ffmpeg's own -ac 1 downmix is -3 dB)
const pcm = new Float32Array(st.length / 2);
for (let i = 0; i < pcm.length; i++) pcm[i] = st[2 * i] / 2 + st[2 * i + 1] / 2;
const out = { note: 'analyze.js on ffmpeg-decoded fixtures/flow/sample.webm (mono, 22050 Hz)', samples: pcm.length, result: analyze(pcm, 22050) };
fs.writeFileSync(path.join(dir, 'sample-analysis.json'), JSON.stringify(out, null, 2) + '\n');
console.log('wrote fixtures/flow/sample-analysis.json');
