import test from 'node:test';
import assert from 'node:assert/strict';
import os from 'node:os';
import fs from 'node:fs';
import path from 'node:path';
import * as aeq from '../src/autoeq.mjs';

const INDEX = `# Index
- [Sennheiser HD 600](./oratory1990/over-ear/Sennheiser%20HD%20600) by oratory1990
- [Sennheiser HD 600](./crinacle/GRAS%2043AG-7%20over-ear/Sennheiser%20HD%20600) by crinacle on GRAS 43AG-7
- [Sony WH-1000XM4](./oratory1990/over-ear/Sony%20WH-1000XM4) by oratory1990
- [Sony WF-1000XM4](./oratory1990/in-ear/Sony%20WF-1000XM4) by oratory1990
- [Samsung Galaxy Buds2 Pro](./Rtings/HMS%20II.3%20in-ear/Samsung%20Galaxy%20Buds2%20Pro) by Rtings on HMS II.3
`.repeat(1) + Array.from({ length: 120 }, (_, i) => `- [Filler ${i}](./x/over-ear/Filler%20${i}) by x`).join('\n');
const PROFILE = `Preamp: -6.3 dB
Filter 1: ON LSC Fc 105 Hz Gain 6.5 dB Q 0.70
Filter 2: ON PK Fc 125 Hz Gain -2.7 dB Q 0.55
Filter 3: ON HSC Fc 10000 Hz Gain -3.1 dB Q 0.70`;

const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'aeq-'));
const seen = [];
aeq.init(dir, async (u) => { seen.push(u); return { ok: true, text: async () => (u.endsWith('INDEX.md') ? INDEX : PROFILE) }; });

test('index parsing, search order and device matching', async () => {
  const items = aeq.parseIndex(INDEX);
  assert.equal(items[1].p, 'crinacle/GRAS 43AG-7 over-ear/Sennheiser HD 600');
  assert.equal(items[1].r, 'GRAS 43AG-7');
  const hd = await aeq.search('hd600');
  assert.equal(hd.length, 0);                      // words must match
  const hd2 = await aeq.search('hd 600');
  assert.equal(hd2[0].source, 'oratory1990');       // preferred measurement first
  assert.equal((await aeq.match('Headphones (WH-1000XM4 Stereo)')).name, 'Sony WH-1000XM4');
  assert.equal((await aeq.match('Galaxy Buds2 Pro')).name, 'Samsung Galaxy Buds2 Pro');
  assert.equal(await aeq.match('Speakers (Realtek(R) Audio)'), null);
});

test('profile parsing and caching', async () => {
  const p = await aeq.profile('oratory1990/over-ear/Sennheiser HD 600');
  assert.equal(p.preamp, -6.3);
  assert.deepEqual(p.filters[0], { type: 'LSC', fc: 105, gain: 6.5, q: 0.7 });
  assert.ok(seen.at(-1).endsWith('/Sennheiser%20HD%20600/Sennheiser%20HD%20600%20ParametricEQ.txt'));
  const n = seen.length;
  await aeq.profile('oratory1990/over-ear/Sennheiser HD 600');
  assert.equal(seen.length, n);                      // second time from disk
  await assert.rejects(aeq.profile('../../etc/passwd'));
});
