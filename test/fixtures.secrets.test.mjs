// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// Guards fixtures/: no cookies, visitor ids or credentials may ever be committed (AGENTS.md).
import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const root = path.join(here, '..');
const fixtures = path.join(root, 'fixtures');

// Real secrets/identifiers that must never be committed. These match JSON keys or cookie names,
// not the harmless service labels like "serviceTrackingParams".
const FORBIDDEN = [
  /SAPISID/i, /__Secure-/i,                        // YouTube session cookie names
  /"visitorData"\s*:/i,                            // per-session visitor id
  /"(?:click)?trackingParams"\s*:/i,               // per-request tracking tokens
  /"cookie"\s*:\s*"[^"]+"/i,                       // a non-empty cookie value
  /AIza[0-9A-Za-z_-]{35}/,                         // Google API keys
  /"Authorization"\s*:/i,
];

function files(dir) {
  if (!fs.existsSync(dir)) return [];
  return fs.readdirSync(dir, { withFileTypes: true }).flatMap((e) =>
    e.isDirectory() ? files(path.join(dir, e.name)) : [path.join(dir, e.name)]);
}

test('no cookies, visitor ids or credentials in fixtures/', () => {
  const offenders = [];
  for (const file of files(fixtures)) {
    // binary PCM fixtures have no text to leak
    if (/\.f32$/.test(file)) continue;
    const text = fs.readFileSync(file, 'utf8');
    for (const rx of FORBIDDEN) if (rx.test(text)) offenders.push(`${path.relative(root, file)} matches ${rx}`);
  }
  assert.deepEqual(offenders, []);
});
