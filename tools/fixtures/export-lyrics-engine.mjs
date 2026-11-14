// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// Generates fixtures/lyrics/engine.json: the results the C# port (M09d, Soncle.Services.LyricsEngine)
// must match. Every case is the JS engine's own output, so the port is checked against it rather
// than against a hand-written expectation. Deterministic; no network (fetchImpl is a fake).
//
//   node tools/fixtures/export-lyrics-engine.mjs
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { cleanTitle, norm, flagsOf, titleSimilarity, scoreCandidate, rankCandidates, isConfident, syncIssues, variantOf, findCandidates, getLyrics } from '../../src/lyrics-engine.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const out = path.join(here, '..', '..', 'fixtures', 'lyrics');
fs.mkdirSync(out, { recursive: true });

// ---- text helpers ----
const titles = ['Numb (Official Video)', 'Café (Live) – Remastered', 'Amber Harbour [Audio]', 'Song (feat. X) ft. Y', '', 'A  B\tC'];
const norms = ['Café (Live) – Remastered', 'AC/DC & Friends', '  Hello, World!  ', 'Übermensch (Deluxe)'];
const flagSongs = ['Song (Live at the Hall)', 'Track - Remix', 'Thing (Sped Up)', 'Plain Song', 'A Cover of X (cover)', 'Radio Edit'];
const simPairs = [['Amber Harbour', 'amber harbour (Official Video)'], ['Amber Harbour', 'Harbour Lights'], ['Numb', 'Numb'], ['', 'Numb'], ['One Two Three', 'One Two Four']];

// ---- a made-up song and a small LRCLIB catalogue (no real lyrics) ----
const VOC = 'amber harbour lantern meadow quiet river copper thunder velvet orchard marble whisper candle summit ribbon canyon willow ember pebble violet saffron glacier nectar compass drizzle falcon hollow juniper kettle lagoon mosaic nimbus opal prism quartz raven timber umber vortex wander yonder zephyr'.split(' ');
const lines = (seed, n = 20, t0 = 8, step = 6) => Array.from({ length: n }, (_, i) => [0, 1, 2, 3].map((k) => VOC[(seed * 7 + i * 5 + k * 11 + (i * i) % 7) % VOC.length]).join(' '));
const lrc = (ls, t0 = 8, step = 6) => ls.map((l, i) => `[${String(Math.floor((t0 + i * step) / 60)).padStart(2, '0')}:${String((t0 + i * step) % 60).padStart(2, '0')}.00] ${l}`).join('\n');

const song = { title: 'Amber Harbour', artists: [{ name: 'The Quiet Band' }], album: { name: 'Meadow' }, duration: 190 };
const rec = (o) => ({ id: 1, trackName: 'Amber Harbour', artistName: 'The Quiet Band', albumName: 'Meadow', duration: 190, instrumental: false, plainLyrics: 'x', syncedLyrics: lrc(lines(1)), ...o });

// ---- candidates and ranking ----
const catalogue = [
  rec({ id: 1 }),                                                                         // the studio song: should win
  rec({ id: 2, trackName: 'Amber Harbour (Live)', duration: 243 }),                       // a live cut: should lose
  rec({ id: 3, artistName: 'Someone Else', duration: 200 }),                              // wrong artist
  rec({ id: 4, trackName: 'Amber Harbour', syncedLyrics: null, plainLyrics: 'words' }),   // text only
  rec({ id: 5, trackName: 'Amber Harbour', duration: 190, instrumental: true }),          // instrumental
  rec({ id: 6, trackName: 'Amber Harbour (Sped Up)', duration: 175 }),                    // sped up
];
const rankCases = [
  { name: 'studio-beats-live', song, list: [catalogue[1], catalogue[2], catalogue[0]] },
  { name: 'all-versions', song, list: catalogue },
  { name: 'text-only-only', song, list: [catalogue[3]] },
  { name: 'poor-match', song, list: [rec({ id: 9, trackName: 'Other Thing', artistName: 'Nobody', duration: 400 })] },
  { name: 'empty', song, list: [] },
  { name: 'close-call', song, list: [rec({ id: 1, duration: 190 }), rec({ id: 2, duration: 191, albumName: 'Meadow (Deluxe)' })] },
];
const ranked = rankCases.map((c) => {
  const r = rankCandidates(c.song, c.list);
  return { name: c.name, song: c.song, list: c.list, ranked: r.map((x) => ({ id: x.c.id, score: x.score })), confident: isConfident(r), variants: r.slice(0, 8).map(variantOf) };
});

// ---- sync issues ----
const syncCases = [
  { synced: lrc(lines(2), 8, 6), duration: 190 },                       // fine
  { synced: lrc(lines(2, 20), 8, 12), duration: 190 },                  // runs past the end
  { synced: lrc(lines(2, 4), 8, 6), duration: 190 },                    // ends early
  { synced: null, duration: 190 },
  { synced: lrc(lines(2, 4), 8, 6), duration: 60 },                     // short song: no ends-early
];
const syncOut = syncCases.map((c) => ({ input: c, issues: syncIssues(c.synced, c.duration) }));

// ---- findCandidates + getLyrics against a fake fetch ----
const jsonRes = (body) => ({ ok: true, status: 200, json: async () => body });
const fakeFetch = (routes) => async (url) => {
  for (const [frag, body] of Object.entries(routes)) if (url.includes(frag)) return jsonRes(body);
  return { ok: false, status: 404, json: async () => null };
};
const never = async () => { throw new Error('no wait in fixtures'); };

const findCases = [
  {
    name: 'exact-plus-searches',
    song,
    routes: {
      '/api/get?': rec({ id: 1 }),
      'search?track_name': [rec({ id: 1 }), rec({ id: 2, trackName: 'Amber Harbour (Live)', duration: 243 })],
      'search?q=': [rec({ id: 3, artistName: 'Someone Else' })],
    },
  },
  { name: 'no-title', song: { title: '', artists: [] }, routes: {} },
  { name: 'nothing-found', song, routes: { '/api/get?': {}, 'search?track_name': [], 'search?q=': [] } },
];

const getCases = [
  { name: 'picks-best-synced', song, routes: { '/api/get?': rec({ id: 1 }), 'search?track_name': [rec({ id: 2, trackName: 'Amber Harbour (Live)', duration: 243 })], 'search?q=': [] } },
  { name: 'text-only-falls-through', song, routes: { '/api/get?': rec({ id: 4, syncedLyrics: null, plainLyrics: 'words' }), 'search?track_name': [], 'search?q=': [] } },
  { name: 'nothing', song, routes: { '/api/get?': {}, 'search?track_name': [], 'search?q=': [] } },
  { name: 'instrumental', song, routes: { '/api/get?': rec({ id: 5, instrumental: true, syncedLyrics: null, plainLyrics: null }), 'search?track_name': [], 'search?q=': [] } },
];

const found = [];
for (const c of findCases) found.push({ name: c.name, song: c.song, routes: c.routes, candidates: (await findCandidates(c.song, { fetchImpl: fakeFetch(c.routes), wait: never })).map((x) => x.id ?? null) });
const got = [];
for (const c of getCases) {
  const r = await getLyrics(c.song, { fetchImpl: fakeFetch(c.routes), wait: never });
  got.push({ name: c.name, song: c.song, routes: c.routes, result: r });
}
// preferId: the person chose version 2, which is not the top-ranked one
{
  const c = { name: 'prefer-id-fetched', song, preferId: 2, routes: { '/api/get?': rec({ id: 1 }), 'search?track_name': [rec({ id: 2, trackName: 'Amber Harbour (Live)', duration: 243 })], 'search?q=': [], '/api/get/2': rec({ id: 2, trackName: 'Amber Harbour (Live)', duration: 243 }) } };
  got.push({ name: c.name, song, preferId: c.preferId, routes: c.routes, result: await getLyrics(c.song, { fetchImpl: fakeFetch(c.routes), wait: never, preferId: 2 }) });
}

const data = {
  cleanTitle: titles.map((t) => ({ input: t, out: cleanTitle(t) })),
  norm: norms.map((t) => ({ input: t, out: norm(t) })),
  flagsOf: flagSongs.map((t) => ({ input: t, out: flagsOf(t) })),
  titleSimilarity: simPairs.map(([a, b]) => ({ a, b, out: titleSimilarity(a, b) })),
  score: catalogue.map((c) => ({ song, candidate: c, score: scoreCandidate(song, c) })),
  rank: ranked,
  sync: syncOut,
  find: found,
  get: got,
};
fs.writeFileSync(path.join(out, 'engine.json'), JSON.stringify(data, null, 1) + '\n');
console.log('wrote fixtures/lyrics/engine.json');
