// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// Generates fixtures/yt/expected/search-request.json: the exact HTTP request youtubei.js 18.1.0
// (the library the JS app uses) sends for `music.search(query, { type })`. The C# catalog client
// (uno/src/Soncle.YouTube/InnerTube/InnerTubeCatalogClient.cs) must produce the same URL, headers
// and body for the same session (minus the visitor id, which is never stored in fixtures/).
// Deterministic (timezone and visitor id are pinned); no network:
// a fake `fetch` records the request and answers with an empty object.
//
//   node tools/fixtures/export-search-request.mjs
process.env.TZ = 'UTC';   // utcOffsetMinutes comes from the machine's zone
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { Innertube } from 'youtubei.js';

const here = path.dirname(fileURLToPath(import.meta.url));
const out = path.join(here, '..', '..', 'fixtures', 'yt', 'expected');
fs.mkdirSync(out, { recursive: true });

const VISITOR = 'TEST-VISITOR';   // fake; fixtures/ never holds a visitor id (test/fixtures.secrets.test.mjs)
const UA = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36';   // youtubei.js picks one at random otherwise
let seen = null;
const fakeFetch = async (input, init) => {
  const req = input instanceof Request ? input : new Request(input, init);
  const headers = {};
  for (const [k, v] of new Headers(init?.headers || req.headers).entries()) headers[k] = v;
  seen = { method: req.method, url: req.url, headers, body: JSON.parse(init?.body ?? (await req.text())) };
  return new Response('{}', { status: 200, headers: { 'content-type': 'application/json' } });
};

const yt = await Innertube.create({
  fetch: fakeFetch, generate_session_locally: true, retrieve_player: false, enable_session_cache: false,
  lang: 'en', location: 'US', timezone: 'UTC', visitor_data: VISITOR, user_agent: UA,
});

const cases = [
  ['daft punk', 'all'], ['daft punk', 'song'], ['daft punk', 'video'], ['daft punk', 'album'],
  ['daft punk', 'artist'], ['daft punk', 'playlist'],
  ['sigur rós – hoppípolla & "more"', 'all'],
];
const requests = [];
for (const [query, type] of cases) {
  seen = null;
  try { await yt.music.search(query, { type }); } catch { /* the empty answer can't be parsed; the request is what we want */ }
  if (!seen) throw new Error('no request captured for ' + query);
  // the visitor id is the one session-bound value: leave it out, the C# test puts the fixed fake one back
  if (seen.body.context.client.visitorData !== VISITOR || seen.headers['x-goog-visitor-id'] !== VISITOR) throw new Error('unexpected visitor id in the request');
  delete seen.body.context.client.visitorData;
  delete seen.headers['x-goog-visitor-id'];
  requests.push({ query, type, ...seen });
}

fs.writeFileSync(path.join(out, 'search-request.json'), JSON.stringify({
  note: 'youtubei.js 18.1.0 music.search request (generate_session_locally, TZ=UTC, fixed visitor data)',
  session: { lang: 'en', location: 'US', timeZone: 'UTC' },
  omitted: 'context.client.visitorData and the x-goog-visitor-id header (both were "TEST-VISITOR"); tests add them back',
  requests,
}, null, 2) + '\n');
console.log('wrote', requests.length, 'requests');
