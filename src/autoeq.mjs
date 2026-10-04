// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// Headphone correction from the AutoEq project (https://github.com/jaakkopasanen/AutoEq, MIT):
// measured parametric EQ for ~9,000 headphones and earphones. Fetched on demand — the index only
// when the picker is first opened, one small profile per chosen headphone — and cached on disk.
// Storage is pluggable so the Android app can share this file: desktop passes its data folder,
// the Android app passes { read(name), write(name, text) }.

const RAW = 'https://raw.githubusercontent.com/jaakkopasanen/AutoEq/master/results/';
const MAX_AGE = 30 * 24 * 3600 * 1000;
// Measurement sources in AutoEq's own order of preference
const RANK = { oratory1990: 0, crinacle: 1, Rtings: 2, Innerfidelity: 3, 'Super Review': 4, Headphone_com: 5 };

let store = null, fetchImpl = null, log = () => {};
let index = null, loading = null;

function folderStore(dataDir) {
  const io = import('node:fs').then(async (fs) => ({ fs, path: await import('node:path') }));
  return {
    async read(name) { const { fs, path } = await io; return fs.readFileSync(path.join(dataDir, 'autoeq', name), 'utf8'); },
    async write(name, text) { const { fs, path } = await io; fs.mkdirSync(path.join(dataDir, 'autoeq'), { recursive: true }); fs.writeFileSync(path.join(dataDir, 'autoeq', name), text); }
  };
}
export function init(dataDirOrStore, fetcher, logger) {
  store = typeof dataDirOrStore === 'string' ? folderStore(dataDirOrStore) : dataDirOrStore;
  fetchImpl = fetcher;
  if (logger) log = logger;
}
async function sha1(text) {
  const d = await globalThis.crypto.subtle.digest('SHA-1', new globalThis.TextEncoder().encode(text));
  return [...new Uint8Array(d)].map((b) => b.toString(16).padStart(2, '0')).join('');
}

const norm = (s) => String(s || '').toLowerCase().normalize('NFKD').replace(/[^a-z0-9]+/g, ' ').trim();

/** Parse AutoEq's results/INDEX.md: "- [Name](./source/rig/Name) by Source on Rig" */
export function parseIndex(md) {
  const out = [];
  for (const line of md.split('\n')) {
    const m = /^- \[(.+?)\]\(\.\/(.+)\) by (.+?)(?: on (.+))?$/.exec(line.trim());
    if (!m) continue;
    let p; try { p = decodeURIComponent(m[2]); } catch { continue; }
    const type = /in-ear/i.test(p) ? 'in-ear' : /earbud/i.test(p) ? 'earbud' : 'over-ear';
    out.push({ n: m[1], p, s: m[3], r: m[4] || '', t: type, k: norm(m[1]) });
  }
  return out;
}

/** Parse "<name> ParametricEQ.txt" into { preamp, filters: [{ type, fc, gain, q }] } */
export function parseProfile(txt) {
  const pre = /Preamp:\s*(-?[\d.]+)\s*dB/i.exec(txt);
  const filters = [];
  for (const m of txt.matchAll(/Filter\s+\d+:\s*ON\s+(PK|LSC|HSC|LS|HS)\s+Fc\s+([\d.]+)\s*Hz\s+Gain\s+(-?[\d.]+)\s*dB(?:\s+Q\s+([\d.]+))?/gi)) {
    const t = m[1].toUpperCase();
    filters.push({ type: t === 'LS' ? 'LSC' : t === 'HS' ? 'HSC' : t, fc: +m[2], gain: +m[3], q: m[4] ? +m[4] : 0.707 });
  }
  if (!filters.length) throw new Error('no filters in profile');
  return { preamp: pre ? +pre[1] : Math.min(0, -Math.max(...filters.map((f) => f.gain))), filters };
}

async function getText(url) {
  const r = await fetchImpl(url);
  if (!r.ok) throw new Error(`AutoEq: HTTP ${r.status}`);
  return r.text();
}

async function loadIndex() {
  if (index) return index;
  if (loading) return loading;
  loading = (async () => {
    let cached = null;
    try { cached = JSON.parse(await store.read('index.json')); } catch { /* first use */ }
    if (cached && Date.now() - cached.at < MAX_AGE) return (index = cached.items);
    try {
      const items = parseIndex(await getText(RAW + 'INDEX.md'));
      if (items.length < 100) throw new Error('index looks incomplete');
      await store.write('index.json', JSON.stringify({ at: Date.now(), items })).catch(() => {});
      log(`autoeq: index of ${items.length} profiles`);
      return (index = items);
    } catch (e) {
      if (cached) return (index = cached.items);   // offline: an old list is fine
      throw e;
    }
  })().finally(() => { loading = null; });
  return loading;
}

function score(item, words) {
  let s = (RANK[item.s] ?? 8) + item.k.length / 40;
  if (item.k.startsWith(words[0])) s -= 2;
  return s;
}

/** Headphones whose name contains every word of the query (best measurement source first). */
export async function search(q, limit = 40) {
  const items = await loadIndex();
  const words = norm(q).split(' ').filter(Boolean);
  if (!words.length) return [];
  const hits = items.filter((it) => words.every((w) => it.k.includes(w)));
  hits.sort((a, b) => score(a, words) - score(b, words) || a.n.localeCompare(b.n));
  return hits.slice(0, limit).map(({ n, p, s, r, t }) => ({ name: n, path: p, source: s, rig: r, type: t }));
}

/**
 * The best profile for an output device name reported by Windows, e.g. "WH-1000XM4" or
 * "Galaxy Buds2 Pro". Only confident matches: every word of a model-like name has to be there.
 */
export async function match(model) {
  const k = norm(model).replace(/\b(stereo|hands free|handsfree|headset|headphones|audio|bluetooth|le|hd)\b/g, ' ').replace(/\s+/g, ' ').trim();
  if (k.length < 4 || !/\d/.test(k) && k.split(' ').length < 2) return null;
  const items = await loadIndex();
  const words = k.split(' ');
  const hits = items.filter((it) => words.every((w) => it.k.includes(w)) && it.k.length <= k.length + 18);
  if (!hits.length) return null;
  hits.sort((a, b) => score(a, words) - score(b, words));
  const { n, p, s, r, t } = hits[0];
  return { name: n, path: p, source: s, rig: r, type: t };
}

export async function profile(p) {
  if (typeof p !== 'string' || p.includes('..')) throw new Error('bad profile path');
  const file = (await sha1(p)).slice(0, 20) + '.json';
  try { return JSON.parse(await store.read(file)); } catch { /* not cached yet */ }
  const name = p.split('/').pop();
  const url = RAW + p.split('/').map(encodeURIComponent).join('/') + '/' + encodeURIComponent(name + ' ParametricEQ.txt');
  const prof = { name, path: p, ...parseProfile(await getText(url)) };
  await store.write(file, JSON.stringify(prof)).catch(() => {});
  return prof;
}
