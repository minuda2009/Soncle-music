// Unit tests for the pure parsing/normalisation helpers in src/yt.mjs.
// These use the exported test hook with a fake client so no network is involved.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { __parse } from '../src/yt.mjs';

test('exposes parse helpers for testing', () => {
  assert.ok(__parse, 'expected a __parse test hook');
});

const { normItem, normShelf, norms } = __parse || {};

test('normItem: MusicResponsiveListItem -> song with artist + duration', () => {
  const node = {
    type: 'MusicResponsiveListItem',
    item_type: 'song',
    id: 'abc12345678',
    flex_columns: [
      { title: { runs: [{ text: 'Title' }] } },
      { title: { runs: [{ text: 'Some Artist', navigationEndpoint: { payload: { browseId: 'UCxxxx' } } }] } },
      { title: { runs: [{ text: 'Album' }] } }
    ],
    title: 'Title',
    fixed_columns: [{ title: { runs: [{ text: '3:45' }] } }]
  };
  const it = normItem(node);
  assert.equal(it.type, 'song');
  assert.equal(it.id, 'abc12345678');
  assert.equal(it.duration, 225);
  assert.equal(it.artists[0].name, 'Some Artist');
});

test('normItem: MusicResponsiveListItem artist keeps monthly-audience subtitle', () => {
  const node = {
    type: 'MusicResponsiveListItem', item_type: 'artist', id: 'UCabc',
    title: 'Daft Punk',
    flex_columns: [{ title: { runs: [{ text: 'Daft Punk' }] } }, { title: { runs: [{ text: 'Artist • 84.7M monthly audience' }] } }]
  };
  const it = normItem(node);
  assert.equal(it.type, 'artist');
  assert.match(it.subtitle, /monthly audience/);
  assert.doesNotMatch(it.subtitle, /subscribers/);
});

test('normShelf: ItemSection wrapping responsive list items becomes a list', () => {
  const s = {
    type: 'ItemSection',
    contents: [
      { type: 'MusicResponsiveListItem', item_type: 'song', id: 'v1', title: 'A', flex_columns: [{ title: { runs: [{ text: 'A' }] } }] },
      { type: 'MusicResponsiveListItem', item_type: 'song', id: 'v2', title: 'B', flex_columns: [{ title: { runs: [{ text: 'B' }] } }] }
    ]
  };
  const sh = normShelf(s);
  assert.equal(sh.layout, 'list');
  assert.equal(sh.items.length, 2);
});

test('normShelf: ItemSection containing a nested MusicShelf is unwrapped', () => {
  const s = {
    type: 'ItemSection',
    contents: [{ type: 'MusicShelf', title: 'Songs', contents: [{ type: 'MusicResponsiveListItem', item_type: 'song', id: 'v1', title: 'A', flex_columns: [{ title: { runs: [{ text: 'A' }] } }] }] }]
  };
  const sh = normShelf(s);
  assert.equal(sh.title, 'Songs');
  assert.equal(sh.items.length, 1);
});

test('normShelf: two-row song cards carry a parsed duration', () => {
  const s = {
    type: 'MusicCarouselShelf',
    header: { title: { runs: [{ text: 'Songs' }] } },
    contents: [{ type: 'MusicTwoRowItem', title: 'Song', subtitle: 'Artist • 4:20', endpoint: { payload: { videoId: 'vid00000001' } }, fixed_columns: [{ title: { runs: [{ text: '4:20' }] } }] }]
  };
  const sh = normShelf(s);
  assert.equal(sh.items[0].duration, 260);
});

test('norms.pickThumb upgrades googleusercontent thumbnails', () => {
  const url = norms.pickThumb([{ url: 'https://lh3.googleusercontent.com/x=w60-h60-l90-rj', width: 60 }]);
  assert.match(url, /w544-h544/);
});
