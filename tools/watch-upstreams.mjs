#!/usr/bin/env node
// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// Weekly watch on other projects whose fixes matter to Soncle (upstream/watch.json): the YouTube
// Music desktop app, youtubei.js, yt-dlp and the upstream Android app. For each one it lists new
// releases and the commits since last week that touch the paths or mention the keywords Soncle
// cares about, and writes upstream/WATCH.md. Nothing is copied; a person decides what to port.
//
//   node tools/watch-upstreams.mjs           report and remember what was seen
//   node tools/watch-upstreams.mjs --check   report only
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const CFG_PATH = path.join(ROOT, 'upstream/watch.json');
const REPORT_PATH = path.join(ROOT, 'upstream/WATCH.md');
const CHECK = process.argv.includes('--check');

/** Why a commit is relevant: matching watched paths and keywords (empty = not relevant). */
export function relevance(src, message, files = []) {
  const msg = String(message || '').toLowerCase().split('\n')[0];
  if (src.require && !msg.includes(src.require.toLowerCase())) return null;   // e.g. yt-dlp: YouTube only
  const words = (src.keywords || []).filter((k) => msg.includes(k.toLowerCase()));
  const hits = files.filter((f) => (src.paths || []).some((p) => f.startsWith(p)));
  // a keyword alone is enough when the source has no path list, or when it says "fix"
  const ok = hits.length > 0 || (words.length > 0 && (!(src.paths || []).length || words.some((w) => /fix|crash|breaking/.test(w))));
  return ok ? { words, files: hits } : null;
}

async function gh(url) {
  const r = await fetch('https://api.github.com/' + url, { headers: { accept: 'application/vnd.github+json', 'user-agent': 'soncle-watch', ...(process.env.GITHUB_TOKEN ? { authorization: `Bearer ${process.env.GITHUB_TOKEN}` } : {}) } });
  if (!r.ok) throw new Error(`${url}: HTTP ${r.status}`);
  return r.json();
}

async function watch(src, seen) {
  const repo = await gh(`repos/${src.repo}`);
  const out = { src, renamed: repo.full_name.toLowerCase() !== src.repo.toLowerCase() ? repo.full_name : null, releases: [], commits: [], head: null, scanned: 0 };
  const branch = src.branch || repo.default_branch;
  out.head = (await gh(`repos/${repo.full_name}/commits/${branch}`)).sha;
  const rels = await gh(`repos/${repo.full_name}/releases?per_page=10`);
  out.releases = rels.filter((r) => !r.draft && (!seen?.at || r.published_at > seen.at)).map((r) => ({ tag: r.tag_name, at: r.published_at.slice(0, 10), url: r.html_url, pre: r.prerelease }));
  if (!seen?.commit) { out.releases = out.releases.slice(0, 1); return out; }        // first run: baseline only
  if (seen.commit === out.head) return out;
  const cmp = await gh(`repos/${repo.full_name}/compare/${seen.commit}...${out.head}`);
  const files = (cmp.files || []).map((f) => f.filename);
  out.scanned = cmp.total_commits;
  for (const c of (cmp.commits || []).reverse()) {
    // compare lists files for the whole range; per-commit files would cost one request each, so a
    // path hit is attributed to the range and commits are matched on their message
    const why = relevance(src, c.commit.message, []);
    if (why) out.commits.push({ sha: c.sha.slice(0, 7), msg: c.commit.message.split('\n')[0].slice(0, 120), url: c.html_url, why });
  }
  out.pathHits = files.filter((f) => (src.paths || []).some((p) => f.startsWith(p)));
  out.compare = cmp.html_url;
  return out;
}

async function main() {
  const cfg = JSON.parse(fs.readFileSync(CFG_PATH, 'utf8'));
  cfg.seen ||= {};
  const results = [];
  for (const src of cfg.sources) {
    try { results.push(await watch(src, cfg.seen[src.id])); } catch (e) { results.push({ src, error: e.message }); }
  }

  const today = new Date().toISOString().slice(0, 10);
  const L = [`# Upstream watch · ${today}`, '', 'New releases and relevant changes in projects Soncle learns from. Nothing here is applied automatically.', ''];
  let relevant = false;
  for (const r of results) {
    L.push(`## ${r.src.id} (${r.src.repo})`, `_${r.src.why}_`, '');
    if (r.error) { L.push(`- Couldn't check: ${r.error}`, ''); continue; }
    if (r.renamed) { L.push(`- **Moved to ${r.renamed}.** Update \`upstream/watch.json\`.`); relevant = true; }
    for (const x of r.releases) { L.push(`- Release **${x.tag}**${x.pre ? ' (pre-release)' : ''}, ${x.at}: ${x.url}`); relevant = true; }
    if (r.commits.length) {
      relevant = true;
      L.push('', `Relevant commits (${r.commits.length} of ${r.scanned}):`);
      for (const c of r.commits.slice(0, 40)) L.push(`- [\`${c.sha}\`](${c.url}) ${c.msg}${c.why.words.length ? ` _(${c.why.words.join(', ')})_` : ''}`);
      if (r.commits.length > 40) L.push(`- …and ${r.commits.length - 40} more: ${r.compare}`);
    }
    if (r.pathHits?.length) {
      relevant = true;
      L.push('', `Watched files changed (${r.pathHits.length}); full diff: ${r.compare}`);
      for (const f of r.pathHits.slice(0, 25)) L.push(`- \`${f}\``);
      if (r.src.ours?.length) L.push(`- Compare with ours: ${r.src.ours.map((o) => '`' + o + '`').join(', ')}`);
    }
    if (!r.releases.length && !r.commits.length && !r.pathHits?.length && !r.renamed) L.push(cfg.seen[r.src.id]?.commit ? '- Nothing relevant since last week.' : '- First check: baseline recorded.');
    L.push('');
  }
  const report = L.join('\n');
  process.stdout.write(report + '\n');
  if (!CHECK) {
    fs.writeFileSync(REPORT_PATH, report);
    for (const r of results) if (r.head) cfg.seen[r.src.id] = { commit: r.head, at: new Date().toISOString() };
    fs.writeFileSync(CFG_PATH, JSON.stringify(cfg, null, 1) + '\n');
  }
  if (process.env.GITHUB_OUTPUT) fs.appendFileSync(process.env.GITHUB_OUTPUT, `relevant=${relevant}\n`);
}

// run only as a script (tests import relevance())
if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) await main();
