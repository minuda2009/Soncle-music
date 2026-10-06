// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// Generates fixtures/audio/policy.json: transitionPlan and lufsFor cases from renderer/app.js.
// transitionPlan depends on UI settings; the cases fix the settings and the features, so the C#
// port (M10) can be checked exactly. Deterministic.
//
//   node tools/fixtures/export-policy.mjs
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { transition } from '../../renderer/flow/flow.js';

const here = path.dirname(fileURLToPath(import.meta.url));
const out = path.join(here, '..', '..', 'fixtures', 'audio');
fs.mkdirSync(out, { recursive: true });

const lufsFor = (info, dbLufs, id) => (info?.lufs != null ? info.lufs : dbLufs?.[id] != null ? dbLufs[id] : null);

const albumOf = (x) => x?.album?.id || (x?.album?.name ? x.album.name + '|' + (x.albumArtist || (x.artists || []).map((a) => a.name).join(', ')) : null);

// pure version of transitionPlan with explicit settings/features
function transitionPlan(cur, nt, S, feat) {
  const xf = S.crossfade || 0;
  const sameAlbum = albumOf(cur) && albumOf(nt) === albumOf(cur);
  if ((sameAlbum && S.crossfadeGapless !== false) || xf === 0) return S.gapless === false ? null : { kind: 'gapless' };
  let v = { kind: 'fade', dur: xf };
  const fa = feat(cur.id), fb = feat(nt.id);
  if (fa && fb && S.flowXf !== false) {
    const tr = transition(cur, fa, nt, fb);
    if (tr.sure > 0.5 && tr.score < 0.45) v.dur = Math.min(xf, Math.max(3, xf * 0.6));
    else if (S.xfStyle !== 'classic' && tr.sure > 0.5 && tr.score >= 0.6 && fa.bpm > 60) v = { kind: 'mix', dur: xf, bpm: fa.bpm };
  }
  return v;
}

const T = (id, album, artist) => ({ id, title: id, album: album ? { id: album, name: album } : null, artists: artist ? [{ name: artist }] : [] });
const cases = [];

// gapless: same album
cases.push({ name: 'same-album', S: { crossfade: 5, crossfadeGapless: true, gapless: true, xfStyle: 'smart', flowXf: true },
  cur: T('a', 'alb1'), nt: T('b', 'alb1'), feat: {}, expected: { kind: 'gapless' } });
// crossfade off
cases.push({ name: 'crossfade-off', S: { crossfade: 0, crossfadeGapless: true, gapless: true, xfStyle: 'smart', flowXf: true },
  cur: T('a', 'alb1'), nt: T('b', 'alb2'), feat: {}, expected: { kind: 'gapless' } });
// plain fade, no features
cases.push({ name: 'fade-no-features', S: { crossfade: 6, crossfadeGapless: true, gapless: true, xfStyle: 'smart', flowXf: true },
  cur: T('a', 'alb1'), nt: T('b', 'alb2'), feat: {}, expected: { kind: 'fade', dur: 6 } });
// mix: good match
const F = { a: { bpm: 124, bpmConf: 0.9, camelot: '8A', keyConf: 0.8, energy: 0.6 }, b: { bpm: 126, bpmConf: 0.9, camelot: '9A', keyConf: 0.8, energy: 0.62 } };
cases.push({ name: 'mix-good-match', S: { crossfade: 6, crossfadeGapless: true, gapless: true, xfStyle: 'smart', flowXf: true },
  cur: T('a', 'alb1'), nt: T('b', 'alb2'), feat: F, expected: transitionPlan(T('a', 'alb1'), T('b', 'alb2'), { crossfade: 6, crossfadeGapless: true, gapless: true, xfStyle: 'smart', flowXf: true }, (id) => F[id]) });
// clash: shorter blend
const C = { a: { bpm: 124, bpmConf: 0.9, camelot: '8A', keyConf: 0.8, energy: 0.6 }, b: { bpm: 174, bpmConf: 0.9, camelot: '3B', keyConf: 0.8, energy: 0.95 } };
cases.push({ name: 'clash-shorter', S: { crossfade: 10, crossfadeGapless: true, gapless: true, xfStyle: 'smart', flowXf: true },
  cur: T('a', 'alb1'), nt: T('b', 'alb2'), feat: C, expected: transitionPlan(T('a', 'alb1'), T('b', 'alb2'), { crossfade: 10, crossfadeGapless: true, gapless: true, xfStyle: 'smart', flowXf: true }, (id) => C[id]) });

fs.writeFileSync(path.join(out, 'policy.json'), JSON.stringify({ transitionPlan: cases }, null, 1) + '\n');
console.log('wrote fixtures/audio/policy.json');
