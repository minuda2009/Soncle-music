// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// Generates fixtures/yt/expected/cases.json: for each raw renderer node found in fixtures/yt/raw,
// the raw renderer JSON and what src/yt.mjs's normItem/normShelf produce for it. This is the golden
// target for the C# parse port (M06): the C# must walk the same raw renderer JSON and normalise it
// the same way. Deterministic; no network.
//
//   node tools/fixtures/export-yt.mjs
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { Parser } from 'youtubei.js';
import { __parse } from '../../src/yt.mjs';

Parser.setParserErrorHandler(() => {});

const here = path.dirname(fileURLToPath(import.meta.url));
const root = path.join(here, '..', '..');
const rawDir = path.join(root, 'fixtures', 'yt', 'raw');
const outDir = path.join(root, 'fixtures', 'yt', 'expected');
fs.mkdirSync(outDir, { recursive: true });

const ITEM_RENDERERS = {
  musicResponsiveListItemRenderer: 'MusicResponsiveListItem',
  musicTwoRowItemRenderer: 'MusicTwoRowItem',
  musicMultiRowListItemRenderer: 'MusicMultiRowListItem',
  musicNavigationButtonRenderer: 'MusicNavigationButton',
  playlistPanelVideoRenderer: 'PlaylistPanelVideo',
};
const SHELF_RENDERERS = [
  'musicCarouselShelfRenderer', 'musicShelfRenderer', 'gridRenderer',
  'musicPlaylistShelfRenderer', 'musicImmersiveCarouselShelfRenderer', 'musicDescriptionShelfRenderer',
];

// Parse one raw renderer object into a node via youtubei.js, then normalise with yt.mjs.
// Item renderers are wrapped in a carousel (a list container); shelf renderers are top-level.
function normItems(key, raw) {
  const parsed = Parser.parseResponse({ contents: { musicCarouselShelfRenderer: { contents: [{ [key]: raw }] } } });
  const car = parsed.contents?.item?.();
  const contents = car?.contents;
  return Array.isArray(contents) ? contents.filter(Boolean) : [];
}

function normShelfOne(key, raw) {
  const parsed = Parser.parseResponse({ contents: { [key]: raw } });
  const nd = parsed.contents?.item?.() ?? parsed.contents?.array?.();
  const arr = Array.isArray(nd) ? nd : [nd];
  return arr.filter(Boolean);
}

function collectRenderers(node, itemCases, shelfCases, depth = 0) {
  if (!node || depth > 24) return;
  if (Array.isArray(node)) { for (const n of node) collectRenderers(n, itemCases, shelfCases, depth + 1); return; }
  if (typeof node !== 'object') return;
  for (const key of Object.keys(ITEM_RENDERERS)) {
    if (node[key]) {
      try {
        for (const nd of normItems(key, node[key])) {
          const expected = __parse.normItem(nd);
          if (expected) itemCases.push({ renderer: key, raw: node[key], expected });
        }
      } catch { /* skip */ }
    }
  }
  for (const key of SHELF_RENDERERS) {
    if (node[key]) {
      try {
        for (const nd of normShelfOne(key, node[key])) {
          const expected = __parse.normShelf(nd);
          if (expected) shelfCases.push({ renderer: key, raw: node[key], expected });
        }
      } catch { /* skip */ }
    }
  }
  for (const v of Object.values(node)) collectRenderers(v, itemCases, shelfCases, depth + 1);
}

const itemCases = [];
const shelfCases = [];
for (const file of fs.readdirSync(rawDir).filter((f) => f.endsWith('.json'))) {
  if (file.startsWith('player-')) continue;
  const raw = JSON.parse(fs.readFileSync(path.join(rawDir, file), 'utf8'));
  const before = itemCases.length + shelfCases.length;
  collectRenderers(raw, itemCases, shelfCases);
  console.log(file, '+' + (itemCases.length + shelfCases.length - before));
}

const dedupe = (cases) => { const seen = new Set(); const out = []; for (const c of cases) { const k = JSON.stringify(c.raw); if (!seen.has(k)) { seen.add(k); out.push(c); } } return out; };
const uniqItems = dedupe(itemCases);
const uniqShelves = dedupe(shelfCases);

fs.writeFileSync(path.join(outDir, 'cases.json'), JSON.stringify({ items: uniqItems, shelves: uniqShelves }, null, 1) + '\n');
console.log(`wrote fixtures/yt/expected/cases.json (${uniqItems.length} items, ${uniqShelves.length} shelves)`);
