import test from 'node:test';
import assert from 'node:assert/strict';
import { proxyStream, parseRange } from '../src/streamproxy.mjs';

const DATA = new Uint8Array(3_500_000).map((_, i) => (i * 7) & 255);
function origin({ failAt = null, capPerRequest = Infinity } = {}) {
  const calls = [];
  const fetchImpl = async (url) => {
    const u = new URL(url);
    const [a, b] = u.searchParams.get('range').split('-').map((x) => (x === '' ? null : Number(x)));
    calls.push({ client: u.searchParams.get('c'), a, b });
    if (failAt != null && u.searchParams.get('c') === 'A' && a >= failAt) return new Response('no', { status: 403 });
    const end = Math.min(b ?? DATA.length - 1, a + capPerRequest - 1, DATA.length - 1);
    return new Response(DATA.slice(a, end + 1), { status: 200 });
  };
  return { fetchImpl, calls };
}
const stream = (c) => ({ url: `http://gv/x?c=${c}`, headers: {}, mime: 'audio/webm; codecs="opus"', length: DATA.length, client: c });

test('serves the whole requested range as one response, across many chunks', async () => {
  const { fetchImpl, calls } = origin();
  const res = await proxyStream({ stream: stream('A'), range: 'bytes=0-', fetchImpl, reresolve: async () => stream('B'), chunk: 256 * 1024 });
  assert.equal(res.status, 206);
  assert.equal(res.headers.get('content-range'), `bytes 0-${DATA.length - 1}/${DATA.length}`);
  assert.equal(Number(res.headers.get('content-length')), DATA.length);
  const got = new Uint8Array(await res.arrayBuffer());
  assert.equal(got.length, DATA.length);
  assert.deepEqual(got, DATA);
  assert.ok(calls.length >= 14);
});

test('switches client mid-stream on 403 and continues from the same byte', async () => {
  const { fetchImpl } = origin({ failAt: 1_200_000 });
  const logs = [];
  const res = await proxyStream({ stream: stream('A'), range: 'bytes=100-', fetchImpl, reresolve: async () => stream('B'), chunk: 512 * 1024, log: (m) => logs.push(m) });
  const got = new Uint8Array(await res.arrayBuffer());
  assert.deepEqual(got, DATA.slice(100));
  assert.ok(logs.some((l) => /403/.test(l)));
});

test('origin returning short chunks is handled', async () => {
  const { fetchImpl } = origin({ capPerRequest: 100_000 });
  const res = await proxyStream({ stream: stream('A'), range: 'bytes=1000-2000999', fetchImpl, reresolve: async () => stream('A') });
  const got = new Uint8Array(await res.arrayBuffer());
  assert.deepEqual(got, DATA.slice(1000, 2001000));
});

test('range past end → 416; suffix ranges', async () => {
  const { fetchImpl } = origin();
  const res = await proxyStream({ stream: stream('A'), range: `bytes=${DATA.length}-`, fetchImpl, reresolve: async () => stream('A') });
  assert.equal(res.status, 416);
  assert.deepEqual(parseRange('bytes=-500', 1000), { start: 500, end: 999 });
});

test('chunk cache serves the head and middle of the playing song without refetching', async () => {
  const { makeChunkCache } = await import('../src/streamproxy.mjs');
  const cache = makeChunkCache();
  const { fetchImpl, calls } = origin();
  const s = stream('A');
  const r1 = await proxyStream({ stream: s, range: 'bytes=0-', fetchImpl, reresolve: async () => s, cache, cacheId: 'v1' });
  await r1.arrayBuffer();
  const before = calls.length;
  const mid = Math.floor(DATA.length * 0.3);
  const r2 = await proxyStream({ stream: s, range: `bytes=${mid}-${mid + 400000}`, fetchImpl, reresolve: async () => s, cache, cacheId: 'v1' });
  assert.deepEqual(new Uint8Array(await r2.arrayBuffer()), DATA.slice(mid, mid + 400001));
  const r3 = await proxyStream({ stream: s, range: 'bytes=0-65535', fetchImpl, reresolve: async () => s, cache, cacheId: 'v1' });
  assert.deepEqual(new Uint8Array(await r3.arrayBuffer()), DATA.slice(0, 65536));
  assert.equal(calls.length, before);          // no new network requests
  assert.ok(cache.size() < DATA.length * 0.7); // only head + middle are kept
});
