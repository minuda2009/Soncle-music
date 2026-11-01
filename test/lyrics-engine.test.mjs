// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
import test from 'node:test';
import assert from 'node:assert/strict';
import { getLyrics, scoreCandidate, rankCandidates, isConfident, syncIssues, titleSimilarity, flagsOf, norm, findCandidates } from '../src/lyrics-engine.mjs';
import { alignWords, alignLines, fitTiming, applyTiming, coverageOf, identifyQuery, transcriptToLines, improveLyrics, toLrc, wordsOf } from '../src/lyrics-align.mjs';
import { parseLrc } from '../renderer/lrc.js';

// ---- made-up songs (no real lyrics): 3 words per line from a small vocabulary ----
const VOC = 'amber harbour lantern meadow quiet river copper thunder velvet orchard marble whisper candle summit ribbon canyon willow ember pebble violet saffron glacier nectar compass drizzle falcon hollow juniper kettle lagoon mosaic nimbus opal prism quartz raven timber umber vortex wander yonder zephyr'.split(' ');
const songText = (seed, n = 24) => Array.from({ length: n }, (_, i) => [0, 1, 2, 3].map((k) => VOC[(seed * 7 + i * 5 + k * 11 + (i * i) % 7) % VOC.length]).join(' '));
const lrcOf = (text, t0 = 8, step = 6) => text.map((l, i) => `[${String(Math.floor((t0 + i * step) / 60)).padStart(2, '0')}:${String((t0 + i * step) % 60).padStart(2, '0')}.00] ${l}`).join('\n');
// what a speech model would give: each word at its place in the song, shifted by `off` and stretched by `k`
const heardOf = (text, { t0 = 8, step = 6, off = 0, k = 1, drop = 0 } = {}) => text.flatMap((l, i) => l.split(' ').map((w, j) => ({ t0: k * (t0 + i * step + j * 1.2) + off, t1: k * (t0 + i * step + j * 1.2) + off + 0.5, text: w }))).filter((_, i) => !drop || i % drop !== 0);

const T = { title: 'Amber Harbour', artists: [{ name: 'The Quiet Band' }], album: { name: 'Meadow' }, duration: 190 };
const rec = (o) => ({ id: 1, trackName: 'Amber Harbour', artistName: 'The Quiet Band', albumName: 'Meadow', duration: 190, instrumental: false, plainLyrics: 'x', syncedLyrics: '[00:01.00] x\n[02:50.00] y', ...o });

test('titles are compared without accents, case, brackets or punctuation', () => {
  assert.equal(norm('Café (Live) – Remastered!'), 'cafe remastered');
  assert.equal(titleSimilarity('Amber Harbour', 'amber harbour (Official Video)'), 1);
  assert.ok(titleSimilarity('Amber Harbour', 'Harbour Lights') < 0.6);
  assert.deepEqual(flagsOf('Song (Live at the Hall) - Remix'), ['live', 'remix']);
});

test('the right version of a song scores best: same length, artist and album beat a live or longer cut', () => {
  const good = rec({ id: 1 }), live = rec({ id: 2, trackName: 'Amber Harbour (Live)', duration: 243 }), other = rec({ id: 3, artistName: 'Someone Else', duration: 200 });
  const ranked = rankCandidates(T, [live, other, good]);
  assert.equal(ranked[0].c.id, 1);
  assert.ok(ranked[0].score >= 90);
  assert.ok(ranked[0].score - ranked[1].score >= 8);
  assert.equal(isConfident(ranked), true);
});

test('close calls are not confident, and nothing is when only a poor match exists', () => {
  const a = rec({ id: 1, duration: 190 }), b = rec({ id: 2, duration: 191, albumName: 'Meadow (Deluxe)' });
  assert.equal(isConfident(rankCandidates(T, [a, b])), false);
  assert.equal(isConfident(rankCandidates(T, [rec({ id: 9, trackName: 'Other Thing', artistName: 'Nobody', duration: 400 })])), false);
  assert.equal(isConfident([]), false);
});

test('synced timing that runs past the end of the song marks a different version', () => {
  const long = rec({ id: 5, syncedLyrics: '[03:40.00] last line' });
  assert.ok(scoreCandidate(T, long) < scoreCandidate(T, rec({ id: 6 })) - 20);
  assert.deepEqual(syncIssues('[03:40.00] x', 190), ['runs-past-end']);
  assert.deepEqual(syncIssues('[00:20.00] x', 300), ['ends-early']);
  assert.deepEqual(syncIssues('[02:50.00] x', 190), []);
});

const mockFetch = (routes) => async (u) => {
  const url = new URL(u); const key = url.pathname + (url.searchParams.has('q') ? '?q' : '');
  const v = routes[key] ?? routes[url.pathname];
  if (v === undefined) return { ok: false, status: 404, json: async () => null };
  return { ok: true, json: async () => v };
};

test('getLyrics merges exact and search results, picks the best and lists the other versions', async () => {
  const f = mockFetch({ '/api/get': rec({ id: 1 }), '/api/search': [rec({ id: 2, trackName: 'Amber Harbour (Live)', duration: 243 }), rec({ id: 3, duration: 188, albumName: 'Singles' })], '/api/search?q': [rec({ id: 1 })] });
  const r = await getLyrics(T, { fetchImpl: f, wait: async () => {} });
  assert.equal(r.id, 1);
  assert.equal(r.confident, true);
  assert.deepEqual(r.variants.map((v) => v.id).sort(), [1, 2, 3]);   // 1 appears once
  assert.deepEqual(r.issues, []);
});

test('the version the person chose wins, even if the ranking liked another', async () => {
  const f = mockFetch({ '/api/get': rec({ id: 1 }), '/api/search': [rec({ id: 2, trackName: 'Amber Harbour (Live)', duration: 243, syncedLyrics: '[00:02.00] live' })] });
  const r = await getLyrics(T, { fetchImpl: f, wait: async () => {}, preferId: 2 });
  assert.equal(r.id, 2); assert.equal(r.chosen, true); assert.equal(r.confident, true);
  // a chosen id the search never returned is fetched by id
  const g = mockFetch({ '/api/get': rec({ id: 1 }), '/api/get/77': rec({ id: 77, syncedLyrics: '[00:03.00] special' }) });
  assert.equal((await getLyrics(T, { fetchImpl: g, wait: async () => {}, preferId: 77 })).id, 77);
});

test('no lyrics, an instrumental, or an unreachable service give null; the service is retried first', async () => {
  assert.equal(await getLyrics(T, { fetchImpl: mockFetch({}), wait: async () => {} }), null);
  assert.equal(await getLyrics(T, { fetchImpl: mockFetch({ '/api/get': rec({ instrumental: true, plainLyrics: null, syncedLyrics: null }) }), wait: async () => {} }), null);
  let calls = 0;
  const down = async () => { calls++; throw new Error('offline'); };
  assert.equal(await getLyrics(T, { fetchImpl: down, wait: async () => {} }), null);
  assert.equal(calls, 9);   // three requests, each tried three times
  assert.deepEqual(await findCandidates({ title: ' ', artists: [] }, { fetchImpl: async () => assert.fail('no request') }), []);
});

// ---- alignment with what a speech model heard ----
test('word alignment survives misheard and missing words', () => {
  const lw = wordsOf('amber harbour lantern meadow quiet river');
  const sw = ['uh', 'amber', 'harbor', 'lantern', 'quiet', 'river', 'oh'];   // "harbor" misheard, "meadow" missed
  const m = alignWords(lw, sw);
  assert.deepEqual(m, [1, 2, 3, -1, 4, 5]);
});

test('a lyric file that is 2.5 seconds late is corrected from the recording', () => {
  const text = songText(3);
  const lines = parseLrc(lrcOf(text));
  const a = alignLines(lines, heardOf(text, { off: 2.5, drop: 9 }));
  assert.ok(a.coverage > 0.85);
  const fit = fitTiming(lines, a.times);
  assert.ok(fit.ok);
  assert.ok(Math.abs(fit.b - 2.5) < 0.2, String(fit.b));
  assert.equal(fit.a, 1);
  const fixed = applyTiming(lines, fit);
  assert.ok(Math.abs(fixed[5].t - (lines[5].t + 2.5)) < 0.2);
});

test('a version that is a little slower is found as drift, but only when the evidence is clear', () => {
  const text = songText(4, 30);
  const lines = parseLrc(lrcOf(text));
  const fit = fitTiming(lines, alignLines(lines, heardOf(text, { k: 1.02 })).times);
  assert.ok(fit.ok && Math.abs(fit.a - 1.02) < 0.004, JSON.stringify(fit));
  const flat = fitTiming(lines, alignLines(lines, heardOf(text, { off: 0.1 })).times);
  assert.equal(flat.a, 1);
});

test('the version that matches the recording is chosen over one with other words', () => {
  const mine = songText(5), other = songText(9);
  const heard = heardOf(mine);
  const a = coverageOf({ synced: lrcOf(mine) }, heard).coverage, b = coverageOf({ synced: lrcOf(other) }, heard).coverage;
  assert.ok(a > 0.9 && a - b > 0.4, `${a} ${b}`);   // chance overlap exists: this made-up vocabulary has only 40 words
});

test('improveLyrics: unsure between versions, it switches to the one that matches and fixes the timing', async () => {
  const mine = songText(5), other = songText(9);
  const current = { synced: lrcOf(other), plain: null, source: 'LRCLIB', id: 1, variants: [{ id: 1 }, { id: 2 }] };
  const r = await improveLyrics(T, current, {
    transcribe: async () => heardOf(mine, { off: 1.5 }),
    fetchVariant: async (id) => ({ id, syncedLyrics: lrcOf(mine), plainLyrics: null })
  });
  assert.equal(r.how, 'switched');
  assert.equal(r.lyrics.id, 2);
  assert.ok(Math.abs(r.offset - 1.5) < 0.2);
  assert.ok(Math.abs(parseLrc(r.lyrics.synced)[0].t - 9.5) < 0.2);
});

test('improveLyrics: the right version with the right timing is left alone', async () => {
  const mine = songText(5);
  const r = await improveLyrics(T, { synced: lrcOf(mine), id: 1, source: 'LRCLIB', variants: [{ id: 1 }] }, { transcribe: async () => heardOf(mine, { off: 0.1 }) });
  assert.equal(r.how, 'unchanged');
  assert.equal(r.lyrics.synced, lrcOf(mine));
});

test('improveLyrics: with no lyrics, the song is identified from a few heard words and synced', async () => {
  const mine = songText(6), decoy = songText(11);
  let asked = '';
  const r = await improveLyrics(T, null, {
    transcribe: async () => heardOf(mine, { off: -1 }),
    search: async (q) => { asked = q; return [{ id: 8, syncedLyrics: lrcOf(decoy), plainLyrics: null }, { id: 9, syncedLyrics: lrcOf(mine), plainLyrics: null }]; }
  });
  assert.equal(r.how, 'identified'); assert.equal(r.lyrics.id, 9);
  assert.ok(wordsOf(asked).length >= 5 && mine.join(' ').includes(asked));
  assert.ok(Math.abs(r.offset + 1) < 0.2);
});

test('improveLyrics: nothing found anywhere gives the heard words, labelled as automatic', async () => {
  const mine = songText(6);
  const r = await improveLyrics(T, null, { transcribe: async () => heardOf(mine), search: async () => [] });
  assert.equal(r.how, 'transcribed'); assert.equal(r.lyrics.machine, true);
  assert.match(r.lyrics.source, /automatic/i);
  assert.ok(parseLrc(r.lyrics.synced).length > 10);
  assert.equal((await improveLyrics(T, null, { transcribe: async () => heardOf(mine).slice(0, 3), search: async () => [] })).how, 'nothing-heard');
});

test('transcript lines break at pauses and at eight words; the LRC round-trips', () => {
  const lines = transcriptToLines([...'a b c d e f g h i j'.split(' ').map((x, i) => ({ t0: i * 0.4, t1: i * 0.4 + 0.3, text: x })), { t0: 9, t1: 9.4, text: 'k' }]);
  assert.deepEqual(lines.map((l) => l.text.split(' ').length), [8, 2, 1]);
  assert.equal(parseLrc(toLrc(lines)).length, 3);
  assert.equal(identifyQuery([]).length, 0);
});
