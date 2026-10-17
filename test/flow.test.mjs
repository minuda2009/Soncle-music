import test from 'node:test';
import assert from 'node:assert/strict';
import { keyCompat, tempoCompat, transition, planFlow, flowCoverage, analysisOrder, afterFailure, pickNext } from '../renderer/flow/flow.js';
import { analyze, camelotOf } from '../renderer/flow/analyze.js';

test('camelot compatibility', () => {
  assert.equal(keyCompat('8A', '8A'), 1);
  assert.equal(keyCompat('8A', '9A'), 0.9);
  assert.equal(keyCompat('12B', '1B'), 0.9);
  assert.equal(keyCompat('8A', '8B'), 0.85);
  assert.ok(keyCompat('8A', '2A') < 0.2);
  assert.equal(keyCompat(null, '8A'), null);
  assert.equal(camelotOf(9, true), '8A');   // A minor
  assert.equal(camelotOf(0, false), '8B');  // C major
  assert.equal(camelotOf(7, false), '9B');  // G major
});
test('tempo compatibility folds half/double time', () => {
  assert.equal(tempoCompat(128, 128), 1);
  assert.ok(tempoCompat(128, 64) > 0.8);
  assert.ok(tempoCompat(128, 175) < 0.2);
  assert.ok(tempoCompat(120, 124) > 0.8);
});
test('flow order keeps tempo/key smooth and degrades without data', () => {
  const T = (id, dur = 200) => ({ id, title: id, duration: dur, artists: [{ name: 'A' + id }] });
  const F = { s: { bpm: 124, bpmConf: 0.9, camelot: '8A', keyConf: 0.8, energy: 0.6 },
    fast: { bpm: 174, bpmConf: 0.9, camelot: '3B', keyConf: 0.8, energy: 0.95 },
    near: { bpm: 126, bpmConf: 0.9, camelot: '9A', keyConf: 0.8, energy: 0.62 },
    ballad: { bpm: 70, bpmConf: 0.8, camelot: '2B', keyConf: 0.7, energy: 0.2 },
    near2: { bpm: 122, bpmConf: 0.9, camelot: '8B', keyConf: 0.8, energy: 0.58 } };
  const feat = (id) => F[id] || null;
  const pool = ['fast', 'ballad', 'unknown', 'near', 'near2'].map((id) => T(id));
  const plan = planFlow(T('s'), pool, feat);
  const order = plan.map((p) => p.track.id);
  assert.deepEqual(order.slice(0, 2).sort(), ['near', 'near2']);
  assert.ok(order.indexOf('fast') > order.indexOf('near2'));
  // with no data at all it falls back to YouTube's relevance order
  const plain = planFlow(T('x'), pool, () => null).map((p) => p.track.id);
  assert.deepEqual(plain, ['fast', 'ballad', 'unknown', 'near', 'near2']);
  const tr = transition(T('s'), F.s, T('near'), F.near);
  assert.ok(tr.score > 0.8 && tr.sure > 0.8);
});
test('analysis finds tempo and key of a synthetic groove', () => {
  const sr = 22050, secs = 20, n = sr * secs, x = new Float32Array(n), bpm = 128, beat = 60 / bpm;
  for (let i = 0; i < n; i++) {
    const t = i / sr, bt = t % beat;
    let s = Math.exp(-bt * 30) * Math.sin(2 * Math.PI * 60 * bt) * 0.8;
    for (const semi of [0, 3, 7]) s += 0.08 * Math.sin(2 * Math.PI * 220 * Math.pow(2, semi / 12) * t);
    x[i] = s * 0.5;
  }
  const r = analyze(x, sr);
  assert.ok(Math.abs(r.bpm - 128) < 2, String(r.bpm));
  assert.equal(r.camelot, '8A');
});

test('time of day and taste shape the radio', async () => {
  const { contextFor } = await import('../renderer/flow/flow.js');
  const night = contextFor(new Date(2026, 8, 29, 2, 0)), day = contextFor(new Date(2026, 8, 29, 15, 0));
  assert.ok(night.energy < day.energy && night.maxBpm && !day.maxBpm);
  const T = (id, a) => ({ id, title: id, duration: 200, artists: [{ name: a }] });
  const F = { s: { bpm: 100, bpmConf: 0.9, camelot: '8A', keyConf: 0.8, energy: 0.5 },
    calm: { bpm: 100, bpmConf: 0.9, camelot: '8A', keyConf: 0.8, energy: 0.25 },
    hype: { bpm: 100, bpmConf: 0.9, camelot: '8A', keyConf: 0.8, energy: 0.8 } };
  const feat = (id) => F[id] || null;
  const fill = Array.from({ length: 18 }, (_, i) => T('f' + i, 'F' + i));
  const pool = [...fill.slice(0, 9), T('hype', 'X'), T('calm', 'Y'), ...fill.slice(9)];
  const known = (plan) => plan.map((p) => p.track.id).filter((id) => F[id]);
  assert.deepEqual(known(planFlow(T('s', 'Z'), pool, feat, { ctx: night })), ['calm', 'hype']);
  assert.deepEqual(known(planFlow(T('s', 'Z'), pool, feat, { ctx: day })), ['hype', 'calm']);
  // skipped / just-played songs sink, loved artists rise
  const taste = { artist: (n) => (n === 'y' ? 1 : 0), liked: () => false, skips: (id) => (id === 'hype' ? 3 : 0), recent: () => 0 };
  assert.deepEqual(known(planFlow(T('s', 'Z'), pool, feat, { taste })), ['calm', 'hype']);
});

// ---- whole-pool measuring (roadmap item 8) ----
const radioPool = (n) => Array.from({ length: n }, (_, i) => ({ id: 's' + i, title: 'Song ' + i, artists: [{ name: 'Artist ' + i }], duration: 200 }));
const camelots = ['8A', '9A', '8B', '10A', '7A', '3B'];
const featFor = (i) => ({ v: 2, bpm: 120 + (i % 5), bpmConf: 0.8, camelot: camelots[i % camelots.length], keyConf: 0.7, energy: 0.3 + (i % 7) / 10 });

test('coverage counts known, given-up and pending songs', () => {
  const pool = radioPool(10);
  const feats = new Map(pool.slice(0, 4).map((t, i) => [t.id, featFor(i)]));
  const c = flowCoverage(pool, (id) => feats.get(id) || null, new Set(['s9']));
  assert.deepEqual([c.total, c.known, c.failed, c.pending, c.partial, c.done], [10, 4, 1, 5, true, false]);
  const all = flowCoverage(pool, () => featFor(0));
  assert.deepEqual([all.known, all.pending, all.partial, all.done], [10, 0, false, true]);
});

test('measuring order: the next slots first, then the rest, never known or given-up songs', () => {
  const pool = radioPool(8);
  const known = new Set(['s0']);
  const order = analysisOrder(pool, (id) => (known.has(id) ? {} : null), { next: [pool[5], pool[2], pool[0]], failed: new Set(['s7']) });
  assert.deepEqual(order.map((t) => t.id), ['s5', 's2', 's1', 's3', 's4', 's6']);
});

test('a song that fails is retried once, then given up on', () => {
  const tries = new Map();
  assert.equal(afterFailure(tries, 'a'), 'retry');
  assert.equal(afterFailure(tries, 'a'), 'give-up');
  assert.equal(afterFailure(tries, 'b'), 'retry');
});

test('an unmeasured song never wins on YouTube position alone', () => {
  const pool = radioPool(50);
  const seed = { id: 'seed', title: 'Seed', duration: 200 };
  // only the LAST ten songs are measured; YouTube lists them as the least relevant
  const feats = new Map(pool.slice(40).map((t, i) => [t.id, featFor(i)]));
  feats.set('seed', featFor(0));
  const feat = (id) => feats.get(id) || null;
  const held = planFlow(seed, pool, feat, { holdUnmeasured: true });
  assert.ok(held.slice(0, 10).every((x) => feats.has(x.track.id)), 'the ten measured songs come before any unmeasured one');
  const pick = pickNext(seed, pool, feat);
  assert.ok(feats.has(pick.track.id));
  assert.equal(pick.known, 10);
  assert.equal(pick.total, 50);
  assert.equal(pick.partial, true);
});

test('50-song radio: measured one by one in order, every pick sees the whole pool at the end', () => {
  const pool = radioPool(50);
  const seed = { id: 'seed', title: 'Seed', duration: 200 };
  const feats = new Map([['seed', featFor(0)]]);
  const feat = (id) => feats.get(id) || null;
  const failed = new Set();
  const log = [];
  let queue = analysisOrder(pool, feat, { next: pool.slice(0, 3), failed });
  assert.equal(queue.length, 50);
  let measured = 0;
  while (queue.length) {
    const t = queue.shift();
    feats.set(t.id, featFor(Number(t.id.slice(1))));
    measured++;
    // after each measurement the next pick comes only from measured songs while any exist
    const pick = pickNext(seed, pool, feat, { failed });
    assert.ok(feats.has(pick.track.id), 'pick is a measured song');
    assert.equal(pick.known, measured);
    log.push(pick.partial);
    queue = analysisOrder(pool, feat, { next: [], failed });
  }
  assert.equal(measured, 50);
  assert.equal(log[48], true);    // 49 of 50: still honest about it
  assert.equal(log[49], false);   // 50 of 50: the pick used the whole pool
  assert.equal(flowCoverage(pool, feat, failed).done, true);
});
