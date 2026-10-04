import { test } from 'node:test';
import assert from 'node:assert/strict';
import { classifyOutput, androidOutputLabel, DEVICE_INFO } from '../renderer/devices.js';

test('classifies headphones from a stereo endpoint', () => {
  const c = classifyOutput('Headphones (WH-1000XM4 Stereo)', []);
  assert.equal(c.type, 'headphones');
  assert.equal(c.bluetooth, true);
  assert.equal(DEVICE_INFO[c.type].preset, 'Headphones');
});

test('classifies built-in laptop speakers', () => {
  const c = classifyOutput('Speakers (Realtek(R) Audio)', []);
  assert.equal(c.type, 'laptop');
  assert.equal(c.bluetooth, false);
});

test('classifies earbuds', () => {
  assert.equal(classifyOutput('Headset (AirPods)', []).type, 'earbuds');
});

test('classifies car audio', () => {
  assert.equal(classifyOutput('Car Hands-Free (Toyota)', []).type, 'car');
});

test('classifies TV output', () => {
  assert.equal(classifyOutput('SAMSUNG (TV)', []).type, 'tv');
});

test('classifies USB DAC', () => {
  assert.equal(classifyOutput('USB DAC (FiiO)', []).type, 'usb');
});

test('unknown labels fall back to unknown', () => {
  assert.equal(classifyOutput('Unknown Device', []).type, 'unknown');
});

test('bluetooth device names supplied out of band still mark BT', () => {
  const c = classifyOutput('Some Output (Model X)', ['Model X']);
  assert.equal(c.bluetooth, true);
});

test('parses multiple and nested bracket groups in Windows labels', () => {
  const a = classifyOutput('Default - Headphones (LP-V53) (Bluetooth)');
  assert.equal(a.model, 'LP-V53');
  assert.equal(a.bluetooth, true);
  assert.equal(a.endpoint, 'Headphones');
  const b = classifyOutput('Speakers (USB Audio) (0d8c:0014)');
  assert.equal(b.model, 'USB Audio');
  assert.equal(b.bluetooth, false);
  const c = classifyOutput('Headphones (Realtek(R) Audio)');
  assert.equal(c.model, 'Realtek(R) Audio');
  assert.equal(c.type, 'wired');
});

// The phone reports the output as { type, name } from SonclePlugin.java; androidOutputLabel turns
// that into a label the same classifier understands, so per-device profiles work there too.
test('maps Android output types to classified outputs', () => {
  const speaker = classifyOutput(androidOutputLabel({ type: 'speaker', name: '' }), []);
  assert.equal(speaker.type, 'speaker');
  assert.equal(speaker.bluetooth, false);

  const wired = classifyOutput(androidOutputLabel({ type: 'wired', name: '' }), []);
  assert.equal(wired.type, 'headphones');
  assert.equal(wired.bluetooth, false);
  assert.equal(DEVICE_INFO[wired.type].preset, 'Headphones');

  const usb = classifyOutput(androidOutputLabel({ type: 'usb', name: '' }), []);
  assert.equal(usb.type, 'usb');

  const hdmi = classifyOutput(androidOutputLabel({ type: 'hdmi', name: '' }), []);
  assert.equal(hdmi.type, 'tv');
});

test('maps real Bluetooth product names to earbuds or headphones', () => {
  const earbuds = ['Galaxy Buds2', 'AirPods Pro', 'Nothing Ear (a)', 'boAt Airdopes 141', 'Jabra Elite 85t', 'Freebuds 5i'];
  for (const name of earbuds) {
    const c = classifyOutput(androidOutputLabel({ type: 'bluetooth', name }), []);
    assert.equal(c.type, 'earbuds', `${name} should be earbuds`);
    assert.equal(c.bluetooth, true);
    assert.equal(c.model, name);
    assert.equal(DEVICE_INFO[c.type].preset, 'Earbuds');
  }
  const overEar = ['WH-1000XM4', 'LP-V53', 'QuietComfort 45'];
  for (const name of overEar) {
    const c = classifyOutput(androidOutputLabel({ type: 'bluetooth', name }), []);
    assert.equal(c.type, 'headphones', `${name} should be headphones`);
    assert.equal(c.bluetooth, true);
    assert.equal(c.model, name);
  }
});

test('keeps Bluetooth distinct when no product name is available', () => {
  const c = classifyOutput(androidOutputLabel({ type: 'bluetooth', name: '' }), []);
  assert.equal(c.type, 'headphones');
  assert.equal(c.bluetooth, true);
  assert.equal(c.label, 'Bluetooth audio (Bluetooth)');
});

test('maps Android car audio', () => {
  const named = classifyOutput(androidOutputLabel({ type: 'car', name: 'Toyota' }), []);
  assert.equal(named.type, 'car');
  assert.equal(named.model, 'Toyota');
  const bare = classifyOutput(androidOutputLabel({ type: 'car', name: '' }), []);
  assert.equal(bare.type, 'car');
  assert.equal(DEVICE_INFO[bare.type].preset, 'Car');
});

