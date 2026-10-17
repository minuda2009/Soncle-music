// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
import test from 'node:test';
import assert from 'node:assert/strict';
import { FORMATS, lineWords, classify, buildScenes, layoutWords, buildTimeline, buildStill, isFiller, SIZE } from '../renderer/lyriccard/layout.js';

const measure = (text, px) => text.length * px * 0.55;
const LINES = [
  { t: 10, end: 13, text: 'I walked alone under the silver moon' },
  { t: 13, end: 16, text: 'Hold me' },
  { t: 16, end: 18, text: 'tonight' },
  { t: 18, end: 22, text: 'And the whole world fell apart' }
];

test('plain lines share their time across the words by length, in order, and finish before the line does', () => {
  const w = lineWords({ t: 10, end: 14, text: 'a longer word' });
  assert.deepEqual(w.map((x) => x.text), ['a', 'longer', 'word']);
  assert.equal(w[0].t, 10);
  assert.ok(w[1].t > w[0].t && w[2].t > w[1].t);
  assert.ok(w[2].end <= 14 + 1e-9);
  assert.ok(w[1].end - w[1].t > w[0].end - w[0].t, 'a longer word is on screen longer');
});

test('enhanced-LRC word times are kept', () => {
  const w = lineWords({ t: 5, end: 9, text: 'x y', words: [{ t: 5.1, text: 'x ' }, { t: 6.5, text: 'y' }] });
  assert.deepEqual(w.map((x) => [x.text, x.t]), [['x', 5.1], ['y', 6.5]]);
  assert.equal(w[0].end, 6.5);
});

test('filler words stay small and the longest and last words lead', () => {
  const sizes = classify(lineWords({ t: 0, end: 4, text: 'I walked alone under the silver moon' }));
  assert.equal(sizes[0], 'small');
  assert.equal(sizes[4], 'small');
  assert.ok(sizes.includes('big'));
  assert.equal(sizes[6], 'big', 'the last word leads');
  assert.ok(isFiller("I'm") && !isFiller('moon'));
});

test('two short neighbouring lines share a scene; a long line stays alone', () => {
  const scenes = buildScenes(LINES);
  assert.equal(scenes.length, 3);
  assert.equal(scenes[1].words.length, 3, '"Hold me" + "tonight"');
  assert.ok(scenes.every((s, i) => s.t1 > s.t0 && (!scenes[i + 1] || s.t1 <= scenes[i + 1].t0 + 1e-9)));
});

test('every word fits its box, short scenes get bigger type than long ones', () => {
  const box = { x: 86, y: 230, w: 908, h: 1300 };
  const run = (text) => { const w = lineWords({ t: 0, end: 4, text }); return layoutWords(w, classify(w), box, measure).items; };
  for (const text of ['Hold me', 'I walked alone under the silver moon and the whole world fell apart']) {
    for (const it of run(text)) {
      assert.ok(it.x >= box.x - 1e-6 && it.x + it.w <= box.x + box.w + 1e-6, `${it.text} inside the width`);
      assert.ok(it.y <= box.y + box.h + 1e-6 && it.y >= box.y, `${it.text} inside the height`);
    }
  }
  const px = (items) => Math.max(...items.map((i) => i.px));
  assert.ok(px(run('Hold me')) > px(run('I walked alone under the silver moon and the whole world fell apart')));
});

test('big words use the biggest size, mid and small the set ratios', () => {
  const w = lineWords({ t: 0, end: 4, text: 'the silver moon rises slowly' });
  const sizes = classify(w);
  const items = layoutWords(w, sizes, { x: 0, y: 0, w: 900, h: 900 }, measure).items;
  const bigPx = Math.max(...items.map((i) => i.px));
  items.forEach((it) => assert.ok(Math.abs(it.px - bigPx * SIZE[it.size]) < 1e-6));
  assert.ok(items.some((i) => i.size === 'small') && items.some((i) => i.size === 'big'));
});

test('the timeline starts at the lead-in and ends after the last scene', () => {
  const tl = buildTimeline(LINES, FORMATS.story, measure);
  assert.ok(Math.abs(tl.scenes[0].t0 - 0.5) < 1e-9);
  assert.ok(tl.duration > tl.scenes[tl.scenes.length - 1].t1);
  assert.ok(tl.duration < 20);
  assert.ok(tl.scenes.every((s) => s.items.every((it) => it.t >= s.t0 - 1e-9)));
});

test('the layout is the same every time', () => {
  assert.deepEqual(buildTimeline(LINES, FORMATS.square, measure), buildTimeline(LINES, FORMATS.square, measure));
});

test('a still holds every word of the picked lines', () => {
  const words = LINES.reduce((n, l) => n + l.text.split(' ').length, 0);
  assert.equal(buildStill(LINES, FORMATS.portrait, measure).length, words);
});

test('empty lines produce no scenes and no duration', () => {
  const tl = buildTimeline([{ t: 1, end: 2, text: '  ' }], FORMATS.story, measure);
  assert.deepEqual(tl, { scenes: [], duration: 0 });
});
