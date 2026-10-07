// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import { relevance } from '../tools/watch-upstreams.mjs';

const cfg = JSON.parse(fs.readFileSync(new URL('../upstream/watch.json', import.meta.url), 'utf8'));
const src = (id) => cfg.sources.find((s) => s.id === id);

test('upstream watch picks out fixes that matter and skips noise', () => {
  assert.ok(relevance(src('pear-desktop'), 'fix(crossfade): fade out never finishes'));
  assert.ok(relevance(src('pear-desktop'), 'chore: refactor', ['src/plugins/equalizer/index.ts']));
  assert.equal(relevance(src('pear-desktop'), 'docs: update README'), null);
  assert.equal(relevance(src('pear-desktop'), 'feat: new lyrics theme'), null);   // keyword without a fix or watched file
  assert.ok(relevance(src('youtubei.js'), 'fix(Player): handle new nsig function'));
  assert.ok(relevance(src('yt-dlp'), '[ie/youtube] Fix n-challenge solving'));
  assert.ok(relevance(src('metrolist'), 'Fix crash when playback starts offline'));
  assert.equal(relevance(src('metrolist'), 'Update translations'), null);
  for (const s of cfg.sources) assert.match(s.repo, /^[\w.-]+\/[\w.-]+$/);
});
