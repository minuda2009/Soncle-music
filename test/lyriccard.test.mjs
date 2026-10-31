// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
import test from 'node:test';
import assert from 'node:assert/strict';
import { FORMATS, lineWords, classify, buildScenes, layoutWords, buildTimeline, buildStill, isFiller, SIZE, TEXT_SIZES, textBox, safeRect } from '../renderer/lyriccard/layout.js';
import { MOTIONS, wordState, countdownAt, startCueText, mmss } from '../renderer/lyriccard/motion.js';

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
  assert.ok(Math.abs(tl.scenes[0].t0 - 1) < 1e-9, 'first line at 10 s, 0.5 s lead-in snapped down to the whole second 9');
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

// A word's top edge (the layout puts the baseline at 0.8 of the row height).
const top = (it) => it.y - it.px * 0.8;
const inside = (it, r) => top(it) >= r.y - 1e-6 && it.y <= r.y + r.h + 1e-6 && it.x >= r.x - 1e-6 && it.x + it.w <= r.x + r.w + 1e-6;

test('on a 9:16 Reel every word stays clear of the Instagram overlays', () => {
  const { w: W, h: H } = FORMATS.story, zone = safeRect(FORMATS.story);
  assert.deepEqual([zone.x, zone.y, zone.x + zone.w, zone.y + zone.h].map((v) => Math.round(v)), [65, 230, 929, 1498]);
  for (const textSize of ['normal', 'large', 'huge']) {
    for (const it of buildTimeline(LINES, FORMATS.story, measure, { textSize }).scenes.flatMap((s) => s.items)) {
      assert.ok(inside(it, zone), `${it.text} (${textSize}) inside the safe zone`);
      assert.ok(top(it) >= 0.12 * H - 1e-6 && it.y <= H * (1 - 0.22) + 1e-6 && it.x + it.w <= W * (1 - 0.14) + 1e-6);
    }
    for (const it of buildStill(LINES, FORMATS.story, measure, { textSize })) {
      assert.ok(inside(it, safeRect(FORMATS.story, true, true)), `${it.text} (${textSize}) inside the still's safe zone`);
      assert.ok(top(it) >= 0.12 * H - 1e-6 && it.y <= H * (1 - 0.22) + 1e-6 && it.x + it.w <= W * (1 - 0.06) + 1e-6);
    }
  }
});

test('a 9:16 still keeps the top and bottom insets but stays centred (no Reels button column on a picture)', () => {
  const { w: W } = FORMATS.story, r = safeRect(FORMATS.story, true, true), b = textBox(FORMATS.story, { still: true });
  assert.ok(Math.abs(r.x - (W - r.x - r.w)) < 1e-6 && Math.abs(b.x - (W - b.x - b.w)) < 1e-6);
  assert.deepEqual([r.y, r.h], [safeRect(FORMATS.story).y, safeRect(FORMATS.story).h]);
  const items = buildStill([{ t: 0, end: 2, text: 'moon' }], FORMATS.story, measure, { align: 'center' });
  assert.ok(Math.abs(items[0].x + items[0].w / 2 - W / 2) < 1e-6, 'a centred word sits on the middle of the frame');
});

test('with the safe area off (and on formats without one) the text box is the classic one', () => {
  const { w: W, h: H } = FORMATS.story;
  const same = (a, b) => assert.deepEqual(Object.values(a).map((v) => v.toFixed(6)), Object.values(b).map((v) => v.toFixed(6)));
  same(textBox(FORMATS.story, { safe: false }), { x: W * 0.08, y: H * 0.12, w: W * 0.84, h: H * 0.68 });
  same(textBox(FORMATS.story, { safe: false, still: true }), { x: W * 0.08, y: H * 0.1, w: W * 0.84, h: H * 0.72 });
  const P = FORMATS.portrait;
  same(textBox(P), { x: P.w * 0.08, y: P.h * 0.12, w: P.w * 0.84, h: P.h * 0.68 });
  const off = buildTimeline(LINES, FORMATS.story, measure, { safe: false }).scenes.flatMap((s) => s.items);
  assert.ok(off.some((it) => it.x + it.w > W * (1 - 0.14)), 'the classic box uses the full width');
  for (const it of off) assert.ok(inside(it, textBox(FORMATS.story, { safe: false })));
});

test('large and huge text really are bigger and still fit', () => {
  assert.deepEqual(TEXT_SIZES.map(([id]) => id), ['normal', 'large', 'huge']);
  const firstLine = (textSize) => buildTimeline(LINES, FORMATS.story, measure, { textSize }).scenes.filter((s) => s.t0 < 3.5).flatMap((s) => s.items);
  const maxPx = (items) => Math.max(...items.map((i) => i.px));
  const mean = (textSize) => { const a = buildTimeline(LINES, FORMATS.story, measure, { textSize }).scenes.flatMap((s) => s.items); return a.reduce((n, i) => n + i.px, 0) / a.length; };
  assert.ok(maxPx(firstLine('large')) > maxPx(firstLine('normal')));
  assert.ok(maxPx(firstLine('huge')) > maxPx(firstLine('normal')));
  assert.ok(mean('normal') < mean('large') && mean('large') < mean('huge'));
  assert.equal(firstLine('huge').length, 7, 'a long line is split over scenes, no word lost');
  assert.ok(buildTimeline(LINES, FORMATS.story, measure, { textSize: 'huge' }).scenes.length > buildTimeline(LINES, FORMATS.story, measure).scenes.length);
  const box = textBox(FORMATS.portrait);
  for (const textSize of ['large', 'huge']) for (const it of buildTimeline(LINES, FORMATS.portrait, measure, { textSize }).scenes.flatMap((s) => s.items)) assert.ok(inside(it, box));
});

test('every motion style rests fully shown once the word is in, and hides (or dims) it before', () => {
  const it = { text: 'moon', px: 200, t: 2, end: 2.6 };
  assert.deepEqual(MOTIONS.map(([id]) => id), ['pop', 'fade', 'slide', 'type', 'karaoke']);
  for (const [style] of MOTIONS) {
    const s = wordState(style, it.t + 5, it);
    assert.deepEqual([s.alpha, s.scale, s.dx, s.dy, s.highlight, s.chars], [1, 1, 0, 0, 1, 4], style);
    const b = wordState(style, it.t - 0.5, it);
    if (style === 'karaoke') assert.ok(b.alpha > 0 && b.alpha < 1 && b.highlight === 0);
    else assert.ok(b.alpha === 0 || b.chars === 0, style);
  }
  // pop is the original animation
  const p = 0.4, easeOut = (x) => 1 - (1 - x) ** 3, easeBack = (x) => 1 + 2.9 * (x - 1) ** 3 + 1.9 * (x - 1) ** 2;
  const pop = wordState('pop', it.t + p * 0.24, it);
  assert.ok(Math.abs(pop.alpha - easeOut(p)) < 1e-9 && Math.abs(pop.scale - (0.86 + 0.14 * easeBack(p))) < 1e-9 && Math.abs(pop.dy - (1 - easeOut(p)) * 200 * 0.14) < 1e-9);
  const slide = wordState('slide', it.t, it), type = wordState('type', it.t + 0.2, it), kar = wordState('karaoke', it.t + 0.3, it);
  assert.ok(Math.abs(slide.dy - 100) < 1e-9);
  assert.ok(type.chars > 0 && type.chars < 4 && type.alpha === 1);
  assert.ok(kar.highlight > 0.4 && kar.highlight < 0.6);
  assert.ok(wordState('fade', it.t + 0.25, it).alpha < wordState('pop', it.t + 0.12, it).alpha + 0.5);
});

test('the timeline reports its song origin, line starts and honours the lead-in', () => {
  const tl = buildTimeline(LINES, FORMATS.story, measure);
  assert.equal(tl.origin, 9);
  assert.deepEqual(tl.lineStarts, [1, 4, 7, 9]);
  const clip = buildTimeline(LINES, FORMATS.story, measure, { startAt: 12, endAt: 20 });
  assert.equal(clip.origin, 12);
  assert.deepEqual(clip.lineStarts, [1, 4, 6]);
  const lead = buildTimeline(LINES, FORMATS.story, measure, { leadIn: 3.5 });
  assert.ok(Math.abs(lead.scenes[0].t0 - 4) < 1e-9);
  assert.equal(lead.origin, 6);
  assert.equal(buildTimeline(LINES, FORMATS.story, measure, { startAt: 12, leadIn: 3.5 }).origin, 12, 'lead-in only applies to chosen lines');
});

test('chosen lines start on a whole song second, never before the song', () => {
  const at = (t, leadIn) => buildTimeline([{ t, end: t + 3, text: 'Hold me tonight' }], FORMATS.story, measure, leadIn === undefined ? {} : { leadIn });
  const frac = at(10.37, 3.5);
  assert.equal(frac.origin, 6, 'origin snapped down to a whole second');
  assert.ok(Math.abs(frac.scenes[0].t0 - 4.37) < 1e-9, 'the lead-in only grows');
  assert.equal(startCueText(frac.origin), 'Start the song at 0:06');
  assert.equal(mmss(frac.origin, true), '0:06.0', 'the timecode at video 0 agrees with the start cue');
  assert.ok(Math.abs(frac.scenes[0].items[0].t - (10.37 - frac.origin)) < 1e-9, 'a word is drawn when the song (started at origin) sings it');
  assert.equal(at(9.5).origin, 9);
  assert.equal(at(9).origin, 8);
  const early = at(2.2, 3.5);
  assert.equal(early.origin, 0, 'a line near the song start does not push the origin below 0:00');
  assert.ok(Math.abs(early.scenes[0].t0 - 2.2) < 1e-9);
  assert.equal(countdownAt(0.5, early.scenes[0].t0), null, 'the countdown drops out when it no longer fits');
});

test('countdown, start cue and timecode helpers', () => {
  assert.deepEqual([0.5, 1.2, 1.6, 2.6, 3.4, 3.5, 5].map((t) => countdownAt(t, 3.5)), [3, 3, 2, 1, 1, null, null]);
  assert.equal(countdownAt(0.2, 3.5), null, 'nothing before the countdown starts');
  assert.equal(countdownAt(0.1, 0.5), null, 'no countdown without enough lead-in');
  assert.equal(countdownAt(0, 3), 3);
  assert.equal(startCueText(83), 'Start the song at 1:23');
  assert.equal(startCueText(0), 'Start the song at 0:00');
  assert.equal(mmss(605.27, true), '10:05.2');
  assert.equal(mmss(-2), '0:00');
});
