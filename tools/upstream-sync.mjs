#!/usr/bin/env node
// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// Keep in step with the upstream Android app this was originally ported from (see UPSTREAM.md;
// the repository is configured in upstream/upstream.json).
//
//   node tools/upstream-sync.mjs                 download upstream's latest main branch and sync
//   node tools/upstream-sync.mjs --from <dir>    use a local checkout instead
//   node tools/upstream-sync.mjs --check         only report; change nothing
//   node tools/upstream-sync.mjs --init          record the current upstream as the baseline
//   node tools/upstream-sync.mjs --repo o/name   follow another repository (saved on the next sync)
//
// Automatic (safe to apply without a human):
//   - files used verbatim (po_token.html) are copied;
// Reported (someone has to port them, because Kotlin can't run here):
//   - every changed upstream file this app was ported from, grouped by the file here that
//     mirrors it, with a link to the upstream diff. Written to upstream/REPORT.md.
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import crypto from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const CFG_PATH = path.join(ROOT, 'upstream/upstream.json');
const REPORT_PATH = path.join(ROOT, 'upstream/REPORT.md');
const args = process.argv.slice(2);
const flag = (f) => args.includes(f);
const opt = (f) => { const i = args.indexOf(f); return i >= 0 ? args[i + 1] : null; };
const CHECK = flag('--check'), INIT = flag('--init');
// --repo owner/name: follow a different upstream repository (e.g. if the KMP app lives in its own repo)

const cfg = JSON.parse(fs.readFileSync(CFG_PATH, 'utf8'));
if (opt('--repo')) cfg.repo = opt('--repo');
const sha = (buf) => crypto.createHash('sha256').update(buf).digest('hex').slice(0, 16);
const out = (s) => process.stdout.write(s + '\n');

async function getUpstream() {
  const local = opt('--from');
  if (local) return { dir: path.resolve(local), commit: opt('--commit'), ref: opt('--ref') || 'local' };
  const ref = opt('--ref') || cfg.branch;
  let commit = null;
  try {
    const r = await fetch(`https://api.github.com/repos/${cfg.repo}/commits/${ref}`, { headers: { accept: 'application/vnd.github+json', ...(process.env.GITHUB_TOKEN ? { authorization: `Bearer ${process.env.GITHUB_TOKEN}` } : {}) } });
    if (r.ok) commit = (await r.json()).sha;
  } catch { /* offline: the tarball below will fail too and say so */ }
  const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'soncle-upstream-'));
  const r = await fetch(`https://codeload.github.com/${cfg.repo}/tar.gz/${commit || ref}`);
  if (!r.ok) throw new Error(`download failed: HTTP ${r.status}`);
  fs.writeFileSync(path.join(tmp, 'src.tgz'), Buffer.from(await r.arrayBuffer()));
  execFileSync('tar', ['-xzf', 'src.tgz', '--strip-components=1'], { cwd: tmp });
  return { dir: tmp, commit, ref };
}

// every file under the watched paths → short hash
function snapshot(dir) {
  const files = {};
  const walk = (rel) => {
    const abs = path.join(dir, rel);
    if (!fs.existsSync(abs)) return;
    if (fs.statSync(abs).isDirectory()) { for (const n of fs.readdirSync(abs).sort()) walk(path.posix.join(rel, n)); return; }
    files[rel] = sha(fs.readFileSync(abs));
  };
  for (const w of cfg.watch) for (const p of w.paths) walk(p.replace(/\/$/, ''));
  for (const c of cfg.copy) walk(c.from);
  return files;
}

function syncCopies(dir) {
  const changed = [];
  for (const { from, to } of cfg.copy) {
    const src = path.join(dir, from), dst = path.join(ROOT, to);
    if (!fs.existsSync(src)) { changed.push(`⚠️ \`${from}\` no longer exists upstream (used by \`${to}\`)`); continue; }
    const a = fs.readFileSync(src).toString('utf8').replace(/\r\n/g, '\n');
    const b = fs.existsSync(dst) ? fs.readFileSync(dst, 'utf8') : '';
    if (a !== b) { if (!CHECK) fs.writeFileSync(dst, a); changed.push(`Copied \`${from}\` → \`${to}\``); }
  }
  return changed;
}


function diffWatched(now) {
  const before = cfg.hashes || {};
  const groups = [], taken = new Set();   // a file is listed under the first group that watches it
  for (const w of cfg.watch) {
    const inGroup = (f) => !taken.has(f) && w.paths.some((p) => (p.endsWith('/') ? f.startsWith(p) : f === p));
    const keys = new Set([...Object.keys(before), ...Object.keys(now)].filter(inGroup));
    const files = [];
    for (const f of [...keys].sort()) {
      if (!(f in before)) files.push(['added', f]);
      else if (!(f in now)) files.push(['removed', f]);
      else if (before[f] !== now[f]) files.push(['changed', f]);
    }
    for (const [, f] of files) taken.add(f);
    if (files.length) groups.push({ ...w, files });
  }
  return groups;
}

// ---------- following upstream when it reorganises (e.g. the move to Kotlin Multiplatform) ----------
// Android app code lives in app/src/main/kotlin/…; after a KMP migration the same files turn up under
// shared/src/commonMain/kotlin/…, composeApp/src/androidMain/…, and drawables under
// composeResources/drawable/ (same vector XML format). When a watched path disappears, find where the
// same files went (by file name, and for folders by how many of their files match), and re-point to it.
function listTree(dir) {
  const files = [];
  const walk = (rel) => {
    for (const e of fs.readdirSync(path.join(dir, rel), { withFileTypes: true })) {
      if (e.name === '.git' || e.name === 'build' || e.name === 'node_modules' || e.name.startsWith('.')) continue;
      const r = rel ? rel + '/' + e.name : e.name;
      if (e.isDirectory()) walk(r); else files.push(r);
    }
  };
  walk('');
  return files;
}
function relocate(dir, notes) {
  let tree = null;
  const all = () => (tree ||= listTree(dir));
  const exists = (p) => fs.existsSync(path.join(dir, p.replace(/\/$/, '')));
  const oldFilesUnder = (p) => Object.keys(cfg.hashes || {}).filter((f) => f.startsWith(p)).map((f) => f.slice(p.length));
  const findFile = (p) => {
    const base = p.split('/').pop();
    const hits = all().filter((f) => f.split('/').pop() === base);
    if (hits.length === 1) return hits[0];
    // several candidates: prefer the one whose path shares the most trailing folders
    const tail = (f) => { const a = f.split('/').reverse(), b = p.split('/').reverse(); let n = 0; while (n < a.length && a[n] === b[n]) n++; return n; };
    return hits.sort((a, b) => tail(b) - tail(a) || (/commonMain|androidMain/.test(b) ? 1 : 0) - (/commonMain|androidMain/.test(a) ? 1 : 0))[0] || null;
  };
  const findDir = (p, names) => {
    if (!names.length) return null;
    const counts = new Map();
    for (const f of all()) {
      const i = f.lastIndexOf('/'), d = f.slice(0, i + 1), n = f.slice(i + 1);
      if (names.includes(n)) counts.set(d, (counts.get(d) || 0) + 1);
    }
    const best = [...counts].sort((a, b) => b[1] - a[1])[0];
    return best && best[1] >= Math.max(1, Math.ceil(names.length * 0.4)) ? best[0] : null;
  };
  const moved = {};   // old path prefix → new path prefix (for re-keying fingerprints)
  const note = (from, to) => { moved[from] = to; notes.push(`Upstream moved \`${from}\` → \`${to}\`; following it.`); };
  for (const c of cfg.copy) if (!exists(c.from)) { const to = findFile(c.from); if (to) { note(c.from, to); c.from = to; } }
  for (const w of cfg.watch) {
    w.paths = w.paths.map((p) => {
      if (exists(p)) return p;
      const to = p.endsWith('/') ? findDir(p, oldFilesUnder(p).map((f) => f.split('/').pop())) : findFile(p);
      if (!to) { notes.push(`⚠️ \`${p}\` (${w.what}) is gone upstream and no new home was found: check by hand.`); return p; }
      note(p, to);
      return to;
    });
  }
  // carry fingerprints over to the new locations, so a move alone doesn't look like a change
  const h = cfg.hashes || {};
  for (const [from, to] of Object.entries(moved)) {
    for (const f of Object.keys(h)) {
      if (f === from) { h[to] = h[f]; delete h[f]; }
      else if (from.endsWith('/') && f.startsWith(from)) {
        const name = f.slice(from.length), cand = to + name;
        const target = fs.existsSync(path.join(dir, cand)) ? cand : findFile(cand) || cand;
        h[target] = h[f]; delete h[f];
      }
    }
  }
}
// New Kotlin Multiplatform / desktop code upstream is worth a look: it solves the same problems.
function kmpReport(dir) {
  const files = listTree(dir);
  const desktop = files.filter((f) => /\/src\/(desktopMain|jvmMain)\//.test(f) && /\.kt$/.test(f));
  const common = files.filter((f) => /\/src\/commonMain\//.test(f) && /\.kt$/.test(f));
  if (!desktop.length && !common.length) return null;
  const known = new Set(cfg.kmpSeen || []);
  const fresh = desktop.filter((f) => !known.has(f));
  if (!CHECK) cfg.kmpSeen = desktop;
  return { desktop, common: common.length, fresh };
}

const up = await getUpstream();
const moves = [];
relocate(up.dir, moves);
const now = snapshot(up.dir);
if (!Object.keys(now).length) throw new Error('none of the watched upstream paths exist; is this the right repository?');

if (INIT) {
  cfg.hashes = now;
  cfg.synced = { ref: up.ref, commit: up.commit || cfg.synced?.commit || null, at: new Date().toISOString().slice(0, 10) };
  fs.writeFileSync(CFG_PATH, JSON.stringify(cfg, null, 1) + '\n');
  out(`Baseline recorded: ${Object.keys(now).length} upstream files at ${up.commit || up.ref}.`);
  process.exit(0);
}

const auto = [...moves, ...syncCopies(up.dir)];
const kmp = kmpReport(up.dir);
const toPort = diffWatched(now);
const base = cfg.synced?.commit || cfg.synced?.ref;
const compare = base && up.commit ? `https://github.com/${cfg.repo}/compare/${base}...${up.commit}` : null;
const lines = [
  `# Upstream sync: ${up.commit ? up.commit.slice(0, 7) : up.ref}`,
  '',
  `Compared with the last sync (${cfg.synced?.commit?.slice(0, 7) || cfg.synced?.ref || 'none'}, ${cfg.synced?.at || 'date unknown'}).` + (compare ? ` [Full upstream diff](${compare})` : ''),
  '',
  '## Applied automatically',
  auto.length ? auto.map((s) => '- ' + s).join('\n') : '- Nothing: shared files are already up to date.',
  '',
  '## Needs porting to this app',
  toPort.length ? '' : '- Nothing: none of the Kotlin files this app mirrors changed.'
];
for (const g of toPort) {
  lines.push(`### ${g.what}`, `Mirror in: ${g.ours.map((o) => '`' + o + '`').join(', ')}`, '');
  for (const [kind, f] of g.files) lines.push(`- ${kind}: [\`${f}\`](https://github.com/${cfg.repo}/blob/${up.commit || cfg.branch}/${f})`);
  lines.push('');
}
if (kmp) {
  const nf = (k, w) => `${k} ${w}${k === 1 ? '' : 's'}`;
  if (lines.at(-1) !== '') lines.push('');
  lines.push('## Kotlin Multiplatform', `Upstream has ${nf(kmp.common, 'shared (commonMain) Kotlin file')} and ${nf(kmp.desktop.length, 'desktop (desktopMain/jvmMain) file')}.`);
  if (kmp.fresh.length) {
    lines.push('New desktop code since the last sync. Worth reading: it may solve the same problems this app does.', '');
    for (const f of kmp.fresh.slice(0, 60)) lines.push(`- [\`${f}\`](https://github.com/${cfg.repo}/blob/${up.commit || cfg.branch}/${f})`);
    if (kmp.fresh.length > 60) lines.push(`- …and ${kmp.fresh.length - 60} more`);
  }
  lines.push('');
}
if (toPort.length) lines.push('Open the diff, find what changed (a fix, a new client version, a timing), and make the same change in the file listed. Then run `npm test`.');
const report = lines.join('\n') + '\n';

if (!CHECK) {
  fs.writeFileSync(REPORT_PATH, report);
  cfg.hashes = now;
  cfg.synced = { ref: up.ref, commit: up.commit || null, at: new Date().toISOString().slice(0, 10) };
  fs.writeFileSync(CFG_PATH, JSON.stringify(cfg, null, 1) + '\n');
}
out(report);
const changed = auto.length > 0 || toPort.length > 0 || !!kmp?.fresh.length;
if (process.env.GITHUB_OUTPUT) fs.appendFileSync(process.env.GITHUB_OUTPUT, `changed=${changed}\nupstream=${(up.commit || up.ref).slice(0, 7)}\n`);
