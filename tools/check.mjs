#!/usr/bin/env node
// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// One command that runs every check an agent can run on this machine before pushing, in the same
// order CI runs them, and prints one line per step. It is deliberately forgiving about the parts a
// sandbox may not have (the .NET SDK, the Android SDK): it says what it skipped and why instead of
// failing, so an agent never wastes a turn on a missing tool.
//
//   node tools/check.mjs            # everything this machine can do
//   node tools/check.mjs --js       # JS only (npm test, lint, drift)
//   node tools/check.mjs --csharp   # C# only (build -warnaserror, test)
//
// Exit code is non-zero only when a check that *ran* failed. See AGENTS.md.
import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const argv = process.argv.slice(2);
const onlyJs = argv.includes('--js');
const onlyCs = argv.includes('--csharp');

const results = [];
const skip = (name, why) => { results.push({ name, status: 'skipped', why }); console.log(`  skip   ${name} — ${why}`); };
const run = (name, cmd, args, opts = {}) => {
  console.log(`  run    ${name}`);
  const r = spawnSync(cmd, args, { cwd: opts.cwd ?? ROOT, stdio: 'inherit', shell: process.platform === 'win32', env: { ...process.env, ...opts.env } });
  const ok = r.status === 0;
  results.push({ name, status: ok ? 'ok' : 'FAILED' });
  if (!ok) console.log(`  FAIL   ${name} (exit ${r.status})`);
  return ok;
};
const has = (cmd) => spawnSync(cmd, ['--version'], { stdio: 'ignore' }).status === 0;

console.log('Soncle checks\n');

// ---- JS: what CI's `CI` workflow runs on every push ----
if (!onlyCs) {
  console.log('JS:');
  if (!fs.existsSync(path.join(ROOT, 'node_modules'))) {
    skip('npm test', 'node_modules missing — run: ELECTRON_SKIP_BINARY_DOWNLOAD=1 npm ci --ignore-scripts');
    skip('npm run lint', 'node_modules missing');
    skip('drift check', 'node_modules missing');
  } else {
    run('npm test', 'npm', ['test']);
    run('npm run lint', 'npm', ['run', 'lint']);
    run('drift check (JS change needs its C# port)', 'node', ['tools/drift-check.mjs']);
  }
  console.log('');
}

// ---- C#: what CI's `Uno / C#` workflow runs (the libraries; Linux-capable) ----
if (!onlyJs) {
  console.log('C# (uno/, libraries — builds on Linux):');
  const dotnet = has('dotnet');
  if (!dotnet) {
    skip('dotnet build/test', 'no .NET SDK on PATH — install .NET 10 (uno/global.json); see AGENTS.md');
  } else {
    // .NET needs libicu: String.Normalize is unavailable under invariant globalization, which the
    // lyrics engine's Norm() needs. Probe by actually starting dotnet rather than guessing paths,
    // and honour an LD_LIBRARY_PATH the caller already set. Say what is missing instead of dumping
    // a stack trace.
    const probe = spawnSync('dotnet', ['--version'], { stdio: 'pipe', encoding: 'utf8' });
    const icuBroken = probe.status !== 0 && `${probe.stdout}${probe.stderr}`.includes('libicu');
    if (icuBroken) {
      skip('dotnet build/test', 'dotnet cannot start (libicu missing). Debian: apt-get install libicu76, or unpack the .deb and export LD_LIBRARY_PATH');
    } else {
      const uno = path.join(ROOT, 'uno');
      const built = run('dotnet build -warnaserror', 'dotnet', ['build', 'Soncle.sln', '-warnaserror'], { cwd: uno });
      if (built) run('dotnet test', 'dotnet', ['test', 'Soncle.sln'], { cwd: uno });
      else skip('dotnet test', 'build failed');
    }
  }
  console.log('  note   the Windows head (uno/heads/Soncle.Windows) and the Android head only build on');
  console.log('         Windows / with the Android SDK; CI builds them. Do not try them on Linux (UNOB0014).');
  console.log('');
}

// ---- summary ----
const failed = results.filter((r) => r.status === 'FAILED');
console.log('Summary:');
for (const r of results) console.log(`  ${r.status === 'ok' ? 'ok     ' : r.status === 'skipped' ? 'skip   ' : 'FAILED '} ${r.name}${r.why ? ' — ' + r.why : ''}`);
if (failed.length) {
  console.log(`\n${failed.length} check(s) failed. Fix them before opening the pull request.`);
  process.exit(1);
}
console.log('\nAll checks that could run passed.');
