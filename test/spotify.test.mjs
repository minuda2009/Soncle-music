import test from 'node:test';
import assert from 'node:assert/strict';
import { parseSpotifyLink, parseTrackList, scoreCandidate, matchAll, toGid, fetchSpotify } from '../src/spotify.mjs';

test('links', () => {
  assert.deepEqual(parseSpotifyLink('https://open.spotify.com/playlist/37i9dQZF1DX0XUsuxWHRQd?si=abc'), { type: 'playlist', id: '37i9dQZF1DX0XUsuxWHRQd' });
  assert.deepEqual(parseSpotifyLink('https://open.spotify.com/intl-de/album/4aawyAB9vmqN3uQ7FjRGTy'), { type: 'album', id: '4aawyAB9vmqN3uQ7FjRGTy' });
  assert.deepEqual(parseSpotifyLink('spotify:track:4cOdK2wGLETKBW3PvgPWqT'), { type: 'track', id: '4cOdK2wGLETKBW3PvgPWqT' });
  assert.equal(parseSpotifyLink('https://music.youtube.com/watch?v=x'), null);
  assert.equal(toGid('4cOdK2wGLETKBW3PvgPWqT').length, 32);
});

test('Exportify CSV', () => {
  const csv = '﻿"Track URI","Track Name","Artist URI(s)","Artist Name(s)","Album Name","Duration (ms)"\n"spotify:track:1","Hello, World","x","Adele,Someone","25","295000"\n"spotify:track:2","Numb","y","Linkin Park","Meteora","185000"\n';
  const r = parseTrackList(csv);
  assert.equal(r.tracks.length, 2);
  assert.deepEqual(r.tracks[0], { title: 'Hello, World', artists: ['Adele', 'Someone'], album: '25', durationMs: 295000 });
});

test('plain text list', () => {
  const r = parseTrackList('1. Linkin Park - Numb\nAvicii – Wake Me Up\nBohemian Rhapsody');
  assert.deepEqual(r.tracks.map((t) => [t.artists[0], t.title]), [['Linkin Park', 'Numb'], ['Avicii', 'Wake Me Up'], [undefined, 'Bohemian Rhapsody']]);
});

test('scoring prefers right artist, title and duration', () => {
  const want = { title: 'Numb', artists: ['Linkin Park'], durationMs: 185000 };
  const good = scoreCandidate(want, { title: 'Numb', artists: [{ name: 'Linkin Park' }], duration: 186 });
  const cover = scoreCandidate(want, { title: 'Numb (Piano Cover)', artists: [{ name: 'Some Pianist' }], duration: 200 });
  const other = scoreCandidate(want, { title: 'Numb', artists: [{ name: 'Marshmello' }], duration: 150 });
  assert.ok(good > 0.9, String(good));
  assert.ok(good > cover && good > other);
  const rem = scoreCandidate({ title: 'Wonderwall - Remastered 2014', artists: ['Oasis'], durationMs: 258000 }, { title: 'Wonderwall', artists: [{ name: 'Oasis' }], duration: 259 });
  assert.ok(rem > 0.9, String(rem));
});

test('matchAll', async () => {
  const db = [{ id: 'a', title: 'Numb', artists: [{ name: 'Linkin Park' }], duration: 186 }, { id: 'b', title: 'Wake Me Up', artists: [{ name: 'Avicii' }], duration: 247 }];
  const search = async (q) => db.filter((x) => q.toLowerCase().includes(x.title.toLowerCase()));
  const res = await matchAll([{ title: 'Numb', artists: ['Linkin Park'] }, { title: 'Wake Me Up', artists: ['Avicii'] }, { title: 'Nothing Here', artists: ['Nobody'] }], search);
  assert.deepEqual(res.map((r) => r.match?.id ?? null), ['a', 'b', null]);
});

test('fetchSpotify via embed + paging', async () => {
  const mk = (n, off = 0) => Array.from({ length: n }, (_, i) => ({ title: 'T' + (i + off), subtitle: 'A, B', duration: 1000, uri: 'spotify:track:' + 'x'.repeat(22) }));
  const state = { data: { entity: { name: 'Big', coverArt: { sources: [{ url: 'c', width: 300 }] }, trackList: mk(100) } }, settings: { session: { accessToken: 'tok' } } };
  const fetchImpl = async (url) => {
    if (url.includes('/embed/')) return new Response(`<html><script id="__NEXT_DATA__" type="application/json">${JSON.stringify({ props: { pageProps: { state } } })}</script>`);
    if (url.includes('/playlist/v2/')) { const from = Number(new URL(url).searchParams.get('from')); return Response.json({ length: 130, contents: { items: from >= 130 ? [] : Array.from({ length: 30 }, () => ({ uri: 'spotify:track:4cOdK2wGLETKBW3PvgPWqT' })) } }); }
    if (url.includes('/metadata/4/track/')) return Response.json({ name: 'Never Gonna Give You Up', artist: [{ name: 'Rick Astley' }], duration: 213573, album: { name: 'Whenever' } });
    return new Response('', { status: 404 });
  };
  const r = await fetchSpotify('https://open.spotify.com/playlist/37i9dQZF1DX4o1oenSJRJd', fetchImpl);
  assert.equal(r.name, 'Big');
  assert.equal(r.tracks.length, 130);
  assert.deepEqual(r.tracks[0].artists, ['A', 'B']);
  assert.equal(r.tracks[129].title, 'Never Gonna Give You Up');
});
