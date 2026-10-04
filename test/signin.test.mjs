// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// Real-browser sign-in. Needs a Chromium-based browser and a display, so it only runs when
// SONCLE_SIGNIN_BROWSER points at one, e.g.
//   SONCLE_SIGNIN_BROWSER=/path/to/chrome xvfb-run -a npm test
import test from 'node:test';
import assert from 'node:assert/strict';
import http from 'node:http';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { browserSignIn } from '../src/browser-signin.mjs';

const exe = process.env.SONCLE_SIGNIN_BROWSER;
const extraArgs = process.getuid?.() === 0 ? ['--no-sandbox'] : [];

test('sign-in page sees an ordinary browser, cookies are picked up, the profile is removed', { skip: !exe && 'set SONCLE_SIGNIN_BROWSER to run' }, async () => {
  let seen = null;
  const srv = http.createServer((req, res) => {
    if (req.url.startsWith('/report?')) {
      seen = JSON.parse(decodeURIComponent(req.url.slice(8)));
      res.writeHead(200, { 'set-cookie': 'SAPISID_TEST=abc; Path=/' });
      return res.end('ok');
    }
    res.writeHead(200, { 'content-type': 'text/html' });
    res.end("<script>fetch('/report?' + encodeURIComponent(JSON.stringify({ webdriver: navigator.webdriver, ua: navigator.userAgent })))</script>");
  }).listen(0, '127.0.0.1');
  await new Promise((r) => srv.on('listening', r));
  const dir = path.join(os.tmpdir(), 'soncle-signin-test-' + process.pid);
  const cookies = await browserSignIn({ exe, url: `http://127.0.0.1:${srv.address().port}/`, profileDir: dir, isDone: (cs) => cs.some((c) => c.name === 'SAPISID_TEST'), pollMs: 400, extraArgs, timeoutMs: 30000 });
  srv.close();
  assert.equal(cookies.find((c) => c.name === 'SAPISID_TEST')?.value, 'abc');
  assert.equal(seen.webdriver, false);
  assert.doesNotMatch(seen.ua, /Electron|Headless/);
  await new Promise((r) => setTimeout(r, 3000));
  assert.ok(!fs.existsSync(dir), 'temporary profile removed');
});
