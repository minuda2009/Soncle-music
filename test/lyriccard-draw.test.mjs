// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// Smoke tests for the lyric card render path (renderer/lyriccard/draw.js) against a stub 2D context that
// records what would be drawn: every motion style, the start cue pill, the countdown, line pulses and
// timecode, the karaoke highlight and the typewriter. No real canvas is needed.
import test from 'node:test';
import assert from 'node:assert/strict';

// A 2D context that accepts any call and property and logs fillText / clip / fillRect calls.
function stubContext(log = []) {
  const state = {};
  const special = {
    measureText: (s) => ({ width: String(s).length * (parseFloat(/(\d+(?:\.\d+)?)px/.exec(state.font || '')?.[1]) || 10) * 0.55 }),
    createLinearGradient: () => ({ addColorStop() {} }),
    fillText: (text, x, y) => { log.push({ op: 'fillText', text: String(text), x, y, alpha: state.globalAlpha ?? 1, font: state.font }); },
    clip: () => { log.push({ op: 'clip' }); },
    fillRect: (...a) => { log.push({ op: 'fillRect', a }); }
  };
  return new Proxy(state, {
    get: (t, k) => (k in special ? special[k] : k in t ? t[k] : () => {}),
    set: (t, k, v) => { t[k] = v; return true; }
  });
}
globalThis.OffscreenCanvas = class { constructor(w, h) { this.width = w; this.height = h; } getContext() { return stubContext(); } };

const { createCard } = await import('../renderer/lyriccard/draw.js');
const { MOTIONS } = await import('../renderer/lyriccard/motion.js');

const LINES = [
  { t: 10, end: 13, text: 'I walked alone under the silver moon' },
  { t: 13, end: 16, text: 'Hold me' },
  { t: 16, end: 18, text: 'tonight' }
];
const base = { format: 'story', background: 'black', title: 'Song', artist: 'Artist', showTitle: true };
const frame = (card, t) => { const log = []; card.render(stubContext(log), t, 0.5); return log; };
const texts = (log) => log.filter((e) => e.op === 'fillText').map((e) => e.text);

test('every motion style renders every frame of the video without throwing', () => {
  for (const [motion] of MOTIONS) {
    const card = createCard({ ...base, lines: LINES, motion, startCue: true, syncCues: true, leadIn: 3.5 }, stubContext());
    assert.ok(card.duration > 0);
    for (let t = 0; t <= card.duration; t += 0.1) frame(card, t);
    const end = texts(frame(card, card.timeline.scenes[0].t1 - 0.5));
    assert.ok(end.includes('WALKED') || end.includes('walked'), `${motion}: the first line is drawn`);
  }
});

test('the start cue, countdown, pulse and timecode are drawn when asked for', () => {
  const card = createCard({ ...base, lines: LINES, startCue: true, syncCues: true, leadIn: 3.5 }, stubContext());
  assert.equal(card.timeline.origin, 6);
  assert.equal(card.countdown, true);
  const t1 = texts(frame(card, 1.5));
  assert.ok(t1.includes('Start the song at 0:06'), 'start cue pill');
  assert.ok(t1.includes('3'), 'countdown');
  assert.ok(t1.includes('0:07.5'), 'song timecode = origin + t');
  assert.ok(!texts(frame(card, 4)).includes('Start the song at 0:06'), 'the start cue goes after ~3 s');
  const first = card.timeline.lineStarts[0];
  assert.ok(frame(card, first + 0.05).some((e) => e.op === 'fillRect'), 'a pulse on the line start');
  assert.ok(!frame(card, first + 0.5).some((e) => e.op === 'fillRect'), 'and gone shortly after');
  const plain = createCard({ ...base, lines: LINES }, stubContext());
  const p = texts(frame(plain, 1.5));
  assert.ok(!p.some((s) => s.startsWith('Start the song') || /^\d:\d\d\.\d$/.test(s) || s === '3'), 'no cues unless asked for');
  assert.equal(plain.countdown, false);
});

test('a clip that opens on a line has no countdown, and the card says so', () => {
  const card = createCard({ ...base, lines: LINES, startAt: 10, endAt: 18, syncCues: true }, stubContext());
  assert.equal(card.countdown, false);
  assert.ok(texts(frame(card, 0.2)).includes('0:10.2'));
});

test('karaoke lights the sung part with a clip and typewriter shows a part of the word', () => {
  const kar = createCard({ ...base, lines: [{ t: 10, end: 14, text: 'Moonlight' }], motion: 'karaoke' }, stubContext());
  const it = kar.timeline.scenes[0].items[0];
  assert.ok(frame(kar, it.t + (it.end - it.t) * 0.3).some((e) => e.op === 'clip'));
  const typ = createCard({ ...base, lines: [{ t: 10, end: 14, text: 'Moonlight' }], motion: 'type' }, stubContext());
  const ti = typ.timeline.scenes[0].items[0];
  const shown = texts(frame(typ, ti.t + 0.1)).find((s) => 'MOONLIGHT'.startsWith(s) && s.length > 0 && s.length < 9);
  assert.ok(shown, 'a partial word is drawn mid-typing');
});

test('the song name sits inside the safe area on 9:16 and a still renders', () => {
  const card = createCard({ ...base, lines: LINES }, stubContext());
  const name = frame(card, 1).find((e) => e.op === 'fillText' && e.text === 'Song');
  assert.ok(name.y < 1920 * (1 - 0.22) && name.x >= 1080 * 0.06 - 1e-6);
  const still = createCard({ ...base, lines: LINES, still: true }, stubContext());
  assert.ok(texts(frame(still, 0)).length > 10);
});
