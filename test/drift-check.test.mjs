// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { findDrift, loadMap, staleEntries, LEDGER } from '../tools/drift-check.mjs';

const map = { 'src/autoeq.mjs': ['uno/src/Soncle.Services/AutoEq.cs'], 'src/yt.mjs': ['uno/src/Soncle.YouTube'] };

test('a ported JS change alone is drift', () => {
  assert.deepEqual(findDrift(['src/autoeq.mjs', 'README.md'], map).map((d) => d.js), ['src/autoeq.mjs']);
});

test('changing the C# side or the ledger is enough', () => {
  assert.deepEqual(findDrift(['src/yt.mjs', 'uno/src/Soncle.YouTube/Parsing/Pages.cs'], map), []);
  assert.deepEqual(findDrift(['src/autoeq.mjs', 'uno/src/Soncle.Services/AutoEq.cs'], map), []);
  assert.deepEqual(findDrift(['src/autoeq.mjs', LEDGER], map), []);
});

test('a directory prefix does not match a sibling with a longer name', () => {
  assert.equal(findDrift(['src/yt.mjs', 'uno/src/Soncle.YouTubeExtra/x.cs'], map).length, 1);
});

test('unported files are ignored', () => {
  assert.deepEqual(findDrift(['renderer/app.js', 'src/main.mjs'], map), []);
});

test('every path in tools/port-map.json exists', () => {
  assert.deepEqual(staleEntries(loadMap()), []);
});
