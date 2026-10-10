#!/usr/bin/env node
// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// Fails when a JS file that already has a C# port changes without its C# counterpart or a ledger
// note in docs/WINUI_PLAN.md (docs/DESIGN.md §7.3). The map is tools/port-map.json.
//   node tools/drift-check.mjs [base-ref]      (default: origin/main)
import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
export const LEDGER = 'docs/WINUI_PLAN.md';

export function loadMap(root = ROOT) {
  const raw = JSON.parse(fs.readFileSync(path.join(root, 'tools/port-map.json'), 'utf8'));
  return Object.fromEntries(Object.entries(raw).filter(([k]) => !k.startsWith('_')));
}

const under = (file, p) => file === p || file.startsWith(p.replace(/\/$/, '') + '/');

/** Returns the ported JS files in `changed` whose C# side and the ledger were both left alone. */
export function findDrift(changed, map) {
  if (changed.includes(LEDGER)) return [];
  return Object.entries(map)
    .filter(([js, cs]) => changed.includes(js) && !changed.some((f) => cs.some((p) => under(f, p))))
    .map(([js, cs]) => ({ js, cs }));
}

/** Map entries that point at files that no longer exist (renames). */
export function staleEntries(map, root = ROOT) {
  return Object.entries(map).flatMap(([js, cs]) => [js, ...cs]).filter((p) => !fs.existsSync(path.join(root, p)));
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const base = process.argv[2] || 'origin/main';
  const map = loadMap();
  const stale = staleEntries(map);
  if (stale.length) {
    console.error('tools/port-map.json points at missing paths:\n  ' + stale.join('\n  '));
    process.exit(1);
  }
  const changed = execFileSync('git', ['diff', '--name-only', `${base}...HEAD`], { cwd: ROOT, encoding: 'utf8' })
    .split('\n').filter(Boolean);
  const drift = findDrift(changed, map);
  if (!drift.length) {
    console.log(`drift check: ok (${changed.length} changed files)`);
  } else {
    console.error('These JS files have a C# port, but this change leaves the C# alone:');
    for (const d of drift) console.error(`  ${d.js}  →  ${d.cs.join(', ')}`);
    console.error(`Port the change in the same PR, or add a note to the ledger in ${LEDGER} §12.5.`);
    process.exit(1);
  }
}
