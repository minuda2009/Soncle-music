import { test } from 'node:test';
import assert from 'node:assert/strict';
import { classifyOutput, DEVICE_INFO } from '../renderer/devices.js';

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
