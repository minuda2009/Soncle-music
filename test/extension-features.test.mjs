// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { GROUPS, ABOUT, STATUS_LABEL, allItems } from '../extension/src/features.js';
import { lookupLyrics, getJson, cachedLookup, cacheKey, TTL, MAX_ENTRIES } from '../extension/src/lyrics.js';

const root = path.join(path.dirname(fileURLToPath(import.meta.url)), '..');
const read = (f) => fs.readFileSync(path.join(root, f), 'utf8');
// theme.js imports ./palette.js, which only sits beside it in a real build
const out = fs.mkdtempSync(path.join(os.tmpdir(), 'soncle-ext-features-'));
execFileSync('node', [path.join(root, 'extension', 'build.mjs'), out], { stdio: 'pipe' });
const { DEFAULTS } = await import(path.join(out, 'shared', 'theme.js'));

test('the settings page covers every settings group of the Soncle app', () => {
  const appGroups = [...read('renderer/app.js').matchAll(/group\('([^']+)'/g)].map((m) => m[1]);
  assert.ok(appGroups.length >= 12);
  const covered = new Set(GROUPS.flatMap((g) => g.from));
  for (const g of appGroups) assert.ok(covered.has(g), `"${g}" is covered by a group in features.js`);
});

test('every item has a known status; "on" items have a control whose setting has a default', () => {
  const ids = new Set();
  for (const i of allItems()) {
    assert.ok(!ids.has(i.id), 'unique id ' + i.id); ids.add(i.id);
    assert.ok(['on', 'soon', 'app', 'site'].includes(i.status), i.id);
    if (i.status === 'on') {
      assert.ok(i.control, i.id + ' has a control');
      if (i.control.key) assert.ok(i.control.key in DEFAULTS, `${i.control.key} has a default`);
    } else {
      assert.ok(STATUS_LABEL[i.status], i.id);
      assert.ok(!i.control, i.id + ' has no control while it is not available');
    }
    if (i.status === 'app') assert.ok(i.why, i.id + ' says why it stays in the app');
  }
  assert.ok(allItems().filter((i) => i.status === 'soon').length >= 10);
});

test('the built extension carries the features list the page imports', () => {
  assert.ok(fs.existsSync(path.join(out, 'shared', 'features.js')));
  assert.match(fs.readFileSync(path.join(out, 'content.js'), 'utf8'), /shared\/features\.js/);
});

test('the extension says, in the page and the docs, that it is strictly not an ad blocker', () => {
  assert.match(ABOUT.lead, /not an ad blocker/i);
  assert.match(ABOUT.body, /artists are paid/i);
  assert.match(ABOUT.body, /never reads or saves the audio stream/i);
  assert.match(read('extension/README.md'), /not an ad blocker/i);
  assert.match(read('extension/src/content.js'), /Strictly not an ad blocker/);
});

test('nothing in the list or the page offers to block, hide or skip ads, or to capture audio', () => {
  const text = JSON.stringify(GROUPS).toLowerCase();
  for (const bad of ['block ads', 'skip ads', 'remove ads', 'hide ads', 'ad-free', 'premium', 'download audio']) assert.ok(!text.includes(bad), bad);
  const src = read('extension/src/content.js');
  for (const bad of ['captureStream(', 'MediaRecorder', 'AudioContext', 'getUserMedia']) assert.ok(!src.includes(bad), bad);
  // the lyric video stays silent: its recorder lives in the shared module and records a canvas only
  assert.ok(!read('renderer/lyriccard/export.js').includes('addTrack'));
});

test('a failing request is tried again, 404 is final', async () => {
  const waits = []; let n = 0;
  const flaky = async () => { if (++n < 3) return { ok: false, status: 503 }; return { ok: true, json: async () => ({ a: 1 }) }; };
  assert.deepEqual(await getJson('u', flaky, async (ms) => waits.push(ms)), { a: 1 });
  assert.deepEqual(waits, [400, 1200]);
  n = 0;
  assert.equal(await getJson('u', async () => { n++; return { ok: false, status: 404 }; }, async () => assert.fail('no wait')), null);
  assert.equal(n, 1);
  const st = {}; n = 0;
  assert.equal(await getJson('u', async () => { n++; throw new Error('offline'); }, async () => {}, st), null);
  assert.equal(n, 3); assert.equal(st.failed, true);
});

const T = { title: 'Song', artists: [{ name: 'Band' }], duration: 200 };
const memStore = (init = {}) => { let d = init; return { get: async () => d, set: async (v) => { d = v; }, peek: () => d }; };
const hit = async () => ({ ok: true, json: async () => ({ syncedLyrics: '[00:01.00] hi', plainLyrics: 'hi' }) });

test('cache: a second lookup comes from the store without a request', async () => {
  const store = memStore(); let calls = 0;
  const f = async (...a) => { calls++; return hit(...a); };
  const a = await cachedLookup(T, store, { fetchImpl: f });
  const b = await cachedLookup(T, store, { fetchImpl: f });
  assert.equal(calls, 1);
  assert.deepEqual([a.found, a.cached, b.found, b.cached, b.synced], [true, false, true, true, '[00:01.00] hi']);
});

test('cache: entries expire (30 days for lyrics, 1 day for "none found")', async () => {
  const store = memStore(); let t = 1e12, calls = 0;
  const now = () => t;
  await cachedLookup(T, store, { fetchImpl: async () => { calls++; return hit(); }, now });
  t += TTL.hit - 1000; await cachedLookup(T, store, { fetchImpl: hit, now }); assert.equal(calls, 1);
  t += 2000; await cachedLookup(T, store, { fetchImpl: async () => { calls++; return hit(); }, now }); assert.equal(calls, 2);
  const miss = memStore(); const none = async () => { calls++; return { ok: false, status: 404 }; };
  calls = 0; t = 1e12;
  assert.equal((await cachedLookup(T, miss, { fetchImpl: none, now })).found, false);
  t += TTL.miss - 1000; await cachedLookup(T, miss, { fetchImpl: none, now }); assert.equal(calls, 2);   // get + search, once
  t += 2000; await cachedLookup(T, miss, { fetchImpl: none, now }); assert.equal(calls, 4);
});

test('cache: being offline is not remembered as "no lyrics"', async () => {
  const store = memStore();
  const r = await cachedLookup(T, store, { fetchImpl: async () => { throw new Error('offline'); }, wait: async () => {} });
  assert.deepEqual([r.found, r.failed], [false, true]);
  assert.deepEqual(store.peek(), {});
});

test('cache: it never holds more than the limit and drops the oldest first', async () => {
  const old = Object.fromEntries(Array.from({ length: MAX_ENTRIES }, (_, i) => ['k' + i, { at: i, v: null }]));
  const store = memStore(old);
  await cachedLookup(T, store, { fetchImpl: hit, now: () => 1e12 });
  const keys = Object.keys(store.peek());
  assert.equal(keys.length, MAX_ENTRIES);
  assert.ok(!keys.includes('k0') && keys.includes(cacheKey(T)));
});

test('lookups still return null for an empty title', async () => {
  assert.equal(await lookupLyrics({ title: ' ', artists: [] }, async () => assert.fail('no request')), null);
});

test('Soncle Now Playing: formats time, has the four destinations, and only drives the page\'s own player', async () => {
  const { fmtTime, RAIL } = await import('../extension/src/nowplaying.js');
  assert.deepEqual([fmtTime(0), fmtTime(65), fmtTime(3601), fmtTime(NaN)], ['0:00', '1:05', '60:01', '0:00']);
  assert.deepEqual(['home', 'library', 'sound', 'settings'].filter((id) => !RAIL.some((r) => r.id === id)), []);   // docs/DESIGN.md §3.1
  const src = read('extension/src/nowplaying.js');
  for (const bad of ['captureStream', 'MediaRecorder', 'AudioContext', 'createMediaElementSource', 'document.cookie', 'ytmusic-ad', 'skip']) assert.ok(!src.includes(bad), bad);
  // its stylesheet only styles its own elements, never the site's
  const { CSS } = await import('../extension/src/nowplaying.js');
  const selectors = [...CSS.matchAll(/(^|[}{])\s*([^{}@]+)\{/g)].map((m) => m[2].trim()).filter(Boolean);
  assert.ok(selectors.length > 20);
  for (const sel of selectors) assert.ok(sel.split(',').every((x) => x.trim().startsWith('.np')), sel);
  assert.match(src, /hides nothing/);
});
