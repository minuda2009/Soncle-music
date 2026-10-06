// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// Records raw InnerTube responses into fixtures/yt/raw for the C# YouTube port (M06/M07).
// Anonymous (no cookie, no sign-in), sanitised: no cookies, no visitorData, no tracking params.
// Deterministic request bodies; the responses are the service's, so this is run once and committed.
//
//   node tools/fixtures/record-yt.mjs
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const out = path.join(here, '..', '..', 'fixtures', 'yt', 'raw');
fs.mkdirSync(out, { recursive: true });

const KEY = 'AIzaSyAO_FJ2SlqU8Q4STEHLGCilw_Y9_11qcW8';   // YouTube's public web key
const WEB_REMIX = { clientName: 'WEB_REMIX', clientVersion: '1.20240101.01.00' };
const IOS = { clientName: 'IOS', clientVersion: '19.45.4', deviceModel: 'iPhone16,2' };
const ANDROID_VR = { clientName: 'ANDROID_VR', clientVersion: '1.60.19', deviceModel: 'Quest 3' };

const ctx = (client) => ({ client: { ...client, hl: 'en', gl: 'US' } });

async function post(endpoint, body, { client = WEB_REMIX, query = '' } = {}) {
  const url = `https://music.youtube.com/youtubei/v1/${endpoint}?key=${KEY}&prettyPrint=false${query}`;
  const r = await fetch(url, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ context: ctx(client), ...body }) });
  if (!r.ok) throw new Error(`${endpoint} HTTP ${r.status}`);
  return r.json();
}

// best-effort record: a page that the service no longer serves just isn't written
async function tryWrite(name, fn) {
  try { write(name, await fn()); }
  catch (e) { console.warn('  skipped', name, '-', e.message); }
}

// strip anything session/tracking related so no cookie or visitor id is ever committed
function sanitize(node) {
  if (Array.isArray(node)) return node.map(sanitize);
  if (node && typeof node === 'object') {
    const out = {};
    for (const [k, v] of Object.entries(node)) {
      if (/^(visitorData|clickTrackingParams|trackingParams|consentId|cookie|delegatedSessionId)$/i.test(k)) continue;
      out[k] = sanitize(v);
    }
    return out;
  }
  return node;
}

function write(name, json) {
  fs.writeFileSync(path.join(out, name), JSON.stringify(sanitize(json), null, 1) + '\n');
  console.log('  ', name);
}

// ---- home / explore / moods ----
console.log('recording pages…');
await tryWrite('home.json', () => post('browse', { browseId: 'FEmusic_home' }));
await tryWrite('explore.json', () => post('browse', { browseId: 'FEmusic_explore' }));
await tryWrite('moods.json', () => post('browse', { browseId: 'FEmusic_moods_and_genres' }));

// ---- search ----
console.log('recording search…');
const search = (q, params = null) => post('search', { query: q, ...(params ? { params } : {}) });
await tryWrite('search-all.json', () => search('daft punk'));
await tryWrite('search-songs.json', () => search('daft punk', 'EgWKAQIIAWoKEAkQBRAKEAMQBA%3D%3D'));
await tryWrite('search-albums.json', () => search('daft punk', 'EgWKAQIYAWoKEAkQChAFEAMQBA%3D%3D'));

// find a song, album, artist and playlist id from a search response
function walk(node, fn) {
  if (Array.isArray(node)) { node.forEach((n) => walk(n, fn)); return; }
  if (node && typeof node === 'object') { fn(node); for (const v of Object.values(node)) walk(v, fn); }
}
async function firstId(query, params, pred) {
  const res = await search(query, params);
  let id = null;
  walk(res, (n) => { if (!id && pred(n)) id = n.videoId || n.browseId; });
  return id;
}
const songId = await firstId('daft punk', null, (n) => n.videoId);
const albumId = await firstId('daft punk', 'EgWKAQIYAWoKEAkQChAFEAMQBA%3D%3D', (n) => n.browseId && String(n.browseId).startsWith('MPRE'));
const artistId = await firstId('daft punk', null, (n) => n.browseId && String(n.browseId).startsWith('UC'));
const playlistId = await firstId('daft punk playlist', 'EgWKAQIoAWoKEAkQChAFEAMQBA%3D%3D', (n) => n.browseId && /^(VL|PL|RD)/.test(String(n.browseId)));
console.log('ids:', { songId, albumId, artistId, playlistId });

// ---- album / artist / playlist / related / upnext / lyrics ----
if (albumId) await tryWrite('album.json', () => post('browse', { browseId: albumId }));
if (artistId) await tryWrite('artist.json', () => post('browse', { browseId: artistId }));
if (playlistId) await tryWrite('playlist.json', () => post('browse', { browseId: playlistId }));
if (songId) {
  await tryWrite('related.json', () => post('next', { videoId: songId, isAudioOnly: true }));
  if (playlistId) await tryWrite('upnext.json', () => post('next', { videoId: songId, playlistId }));
  await tryWrite('lyrics.json', () => post('browse', { browseId: 'FEmusic_lyrics', params: Buffer.from(`\n\x0b${songId}`).toString('base64') }));
  // player responses for the two token-free clients the resolver tries
  await tryWrite('player-ios.json', () => post('player', { videoId: songId, contentCheckOk: true, racyCheckOk: true }, { client: IOS }));
  await tryWrite('player-android-vr.json', () => post('player', { videoId: songId, contentCheckOk: true, racyCheckOk: true }, { client: ANDROID_VR }));
}

console.log('wrote fixtures/yt/raw');
