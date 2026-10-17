// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// Generates fixtures/flow/opus-oracle.json: what an independent tool (ffprobe and ffmpeg with
// libopus) says about fixtures/flow/sample.webm and sample-tailcues.webm. The C# Opus path
// (WebM demux in Soncle.Audio, then the Opus decoder) must agree with it:
//   - the packet list (count, sizes, timestamps) and the stream's pre-skip;
//   - the decoded audio: the number of frames after pre-skip and end padding are removed, and the
//     level of each channel.
// There is no JS demuxer or decoder to port here (the JS app lets Chromium decode), so ffmpeg is
// the oracle. Needs ffmpeg and ffprobe on PATH; the numbers are committed, and only change if the
// WebM files or the libopus version do.
//
//   node tools/fixtures/export-opus-oracle.mjs
import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const dir = path.join(here, '..', '..', 'fixtures', 'flow');
const run = (cmd, args, opts = {}) => execFileSync(cmd, args, { maxBuffer: 1 << 28, ...opts });
const round = (x, n = 3) => Math.round(x * 10 ** n) / 10 ** n;

const out = { note: 'ffprobe / ffmpeg (libopus) on fixtures/flow/sample*.webm', files: {} };
for (const name of ['sample.webm', 'sample-tailcues.webm']) {
  const file = path.join(dir, name);
  const probe = JSON.parse(run('ffprobe', ['-v', 'error', '-select_streams', 'a:0', '-show_streams', '-show_packets', '-of', 'json', file]).toString());
  const st = probe.streams[0];
  const packets = probe.packets.map((p) => ({
    size: Number(p.size),
    ptsMs: Math.round(Number(p.pts_time) * 1000),
    durationMs: Math.round(Number(p.duration_time) * 1000),
    skipSamples: (p.side_data_list || []).find((d) => d.side_data_type === 'Skip Samples')?.skip_samples ?? null,
    discardPadding: (p.side_data_list || []).find((d) => d.side_data_type === 'Skip Samples')?.discard_padding ?? null,
  }));
  // decode to interleaved float32 stereo at 48 kHz (ffmpeg removes the pre-skip and the end padding)
  const raw = run('ffmpeg', ['-v', 'error', '-i', file, '-f', 'f32le', '-ar', '48000', '-ac', '2', 'pipe:1']);
  const f = new Float32Array(raw.buffer, raw.byteOffset, raw.length / 4);
  const frames = f.length / 2;
  const ch = [0, 1].map((c) => {
    let sum = 0, peak = 0;
    for (let i = c; i < f.length; i += 2) { sum += f[i] * f[i]; peak = Math.max(peak, Math.abs(f[i])); }
    return { rmsDb: round(10 * Math.log10(sum / frames)), peak: round(peak) };
  });
  out.files[name] = {
    channels: st.channels,
    sampleRate: Number(st.sample_rate),
    initialPadding: st.initial_padding ?? null,
    packetCount: packets.length,
    packets,
    decoded: { sampleRate: 48000, channels: 2, frames, left: ch[0], right: ch[1] },
  };
}
fs.writeFileSync(path.join(dir, 'opus-oracle.json'), JSON.stringify(out) + '\n');
console.log('wrote fixtures/flow/opus-oracle.json');
