// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// Crossfade reference numbers: no blend may make the overlap louder than either song.
import test from 'node:test';
import assert from 'node:assert/strict';
import { xfGains } from '../renderer/engine.js';

const scan = (curve) => {
  let maxPower = 0, minPower = 9, maxSum = 0;
  for (let i = 0; i <= 1000; i++) {
    const [a, b] = xfGains(curve, i / 1000);
    maxPower = Math.max(maxPower, a * a + b * b); minPower = Math.min(minPower, a * a + b * b); maxSum = Math.max(maxSum, a + b);
  }
  return { maxPowerDb: 10 * Math.log10(maxPower), minPowerDb: 10 * Math.log10(minPower), maxPeakDb: 20 * Math.log10(maxSum) };
};

test('every curve starts on the old song and ends on the new one', () => {
  for (const c of ['smooth', 'equalpower', 'linear']) {
    assert.deepEqual(xfGains(c, 0).map((v) => +v.toFixed(6)), [0, 1]);
    assert.deepEqual(xfGains(c, 1).map((v) => +v.toFixed(6)), [1, 0]);
  }
});

test('smooth (the default, same as Metrolist): never louder, coinciding peaks never above full scale', () => {
  const r = scan('smooth');
  assert.ok(r.maxPowerDb <= 0.01, `power ${r.maxPowerDb}`);
  assert.ok(r.maxPeakDb <= 0.01, `peak ${r.maxPeakDb}`);
  assert.ok(r.minPowerDb > -3.1, `dip ${r.minPowerDb}`);
});

test('equal power: constant loudness for unrelated songs', () => {
  const r = scan('equalpower');
  assert.ok(Math.abs(r.maxPowerDb) < 0.01 && Math.abs(r.minPowerDb) < 0.01);
});

test('gains are clamped outside the blend', () => {
  assert.deepEqual(xfGains('smooth', -1), [0, 1]);
  assert.deepEqual(xfGains('linear', 2), [1, 0]);
});
