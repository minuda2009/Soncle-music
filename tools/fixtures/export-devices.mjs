// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// Generates fixtures/devices: classifyOutput(label, btNames) results the C# port (M09c) must
// match. Deterministic; no network.
//
//   node tools/fixtures/export-devices.mjs
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { classifyOutput, DEVICE_INFO } from '../../renderer/devices.js';

const here = path.dirname(fileURLToPath(import.meta.url));
const out = path.join(here, '..', '..', 'fixtures', 'devices');
fs.mkdirSync(out, { recursive: true });

const cases = [
  ['Headphones (WH-1000XM4 Stereo)', []],
  ['Speakers (Realtek(R) Audio)', []],
  ['Headset (AirPods)', []],
  ['Car Hands-Free (Toyota)', []],
  ['SAMSUNG (TV)', []],
  ['USB DAC (FiiO)', []],
  ['Unknown Device', []],
  ['Some Output (Model X)', ['Model X']],
  ['Default - Headphones (LP-V53) (Bluetooth)', []],
  ['Speakers (USB Audio) (0d8c:0014)', []],
  ['Headphones (Realtek(R) Audio)', []],
  ['Bluetooth audio (Galaxy Buds2 Stereo)', []],
  ['Car audio (Uconnect)', []],
  ['Bluetooth audio (Nothing Ear (a) Stereo)', []],
];

const results = cases.map(([label, bt]) => ({ label, bt, result: classifyOutput(label, bt) }));
fs.writeFileSync(path.join(out, 'classify.json'), JSON.stringify({ cases: results, deviceInfo: DEVICE_INFO }, null, 1) + '\n');
console.log('wrote fixtures/devices');
