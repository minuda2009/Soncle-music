// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// Google sign-in in a real browser.
//
// Google refuses sign-in inside embedded browser windows ("This browser or app may not be secure"),
// and no user-agent trick fixes that reliably. So Soncle opens the user's installed Chrome / Edge /
// Brave with a separate, temporary profile, lets them sign in there like on any website, reads the
// YouTube cookies once they are signed in, closes that window and deletes the temporary profile.
//
// - The browser is controlled over --remote-debugging-pipe (stdin-like file handles), not a network
//   port, so no other program can connect to it.
// - Nothing is injected into the pages: Soncle only asks the browser for its cookie jar every
//   second and a half (Storage.getCookies at the browser level), so the sign-in page sees an
//   ordinary browser.
// - The user's normal browser profile is never touched.
import { spawn } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';

function candidates() {
  const env = process.env, list = [];
  if (env.SONCLE_SIGNIN_BROWSER) list.push(env.SONCLE_SIGNIN_BROWSER);
  if (process.platform === 'win32') {
    const pf = env.ProgramFiles || 'C:\\Program Files', pf86 = env['ProgramFiles(x86)'] || 'C:\\Program Files (x86)', local = env.LOCALAPPDATA || '';
    for (const base of [pf, pf86, local]) {
      if (!base) continue;
      list.push(path.join(base, 'Google', 'Chrome', 'Application', 'chrome.exe'));
    }
    for (const base of [pf86, pf]) list.push(path.join(base, 'Microsoft', 'Edge', 'Application', 'msedge.exe'));
    for (const base of [pf, pf86, local]) if (base) list.push(path.join(base, 'BraveSoftware', 'Brave-Browser', 'Application', 'brave.exe'));
  } else if (process.platform === 'darwin') {
    list.push('/Applications/Google Chrome.app/Contents/MacOS/Google Chrome', '/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge', '/Applications/Brave Browser.app/Contents/MacOS/Brave Browser');
  } else {
    for (const d of (env.PATH || '').split(':')) for (const n of ['google-chrome', 'google-chrome-stable', 'microsoft-edge', 'brave-browser', 'chromium', 'chromium-browser']) list.push(path.join(d, n));
  }
  return list;
}
export function findBrowser() {
  for (const p of candidates()) { try { if (p && fs.statSync(p).isFile()) return p; } catch { /* not installed */ } }
  return null;
}
export const browserName = (exe) => (/msedge/i.test(exe) ? 'Microsoft Edge' : /brave/i.test(exe) ? 'Brave' : /chrom/i.test(exe) ? (/chromium/i.test(exe) ? 'Chromium' : 'Google Chrome') : 'your browser');

/**
 * Open `url` in a real browser with a throw-away profile and wait until `isDone(cookies)`.
 * Resolves with the cookie list, or null if the user closed the window / it timed out.
 */
export function browserSignIn({ exe = findBrowser(), url, profileDir, isDone, log = () => {}, timeoutMs = 15 * 60 * 1000, pollMs = 1500, extraArgs = [] }) {
  if (!exe) return Promise.reject(new Error('no supported browser found'));
  const cleanup = () => { for (let i = 0; i < 5; i++) { try { fs.rmSync(profileDir, { recursive: true, force: true }); return; } catch { /* still locked */ } } };
  cleanup();
  fs.mkdirSync(profileDir, { recursive: true });
  const args = [
    `--user-data-dir=${profileDir}`, '--remote-debugging-pipe', '--no-first-run', '--no-default-browser-check',
    // Remote control makes Chrome report navigator.webdriver = true, which sign-in pages treat as a
    // bot. Soncle never drives the page, so tell Chrome not to flag it.
    '--disable-blink-features=AutomationControlled',
    '--disable-sync', '--disable-features=Translate,SigninInterception', '--window-size=560,780', ...extraArgs, '--new-window', url
  ];
  const child = spawn(exe, args, { stdio: ['ignore', 'ignore', 'ignore', 'pipe', 'pipe'], windowsHide: false });
  const toBrowser = child.stdio[3], fromBrowser = child.stdio[4];
  let seq = 0, buf = '';
  const waiting = new Map();
  fromBrowser.setEncoding('utf8');
  fromBrowser.on('data', (d) => {
    buf += d;
    let i;
    while ((i = buf.indexOf('\0')) >= 0) {
      const msg = buf.slice(0, i); buf = buf.slice(i + 1);
      try { const m = JSON.parse(msg); if (m.id && waiting.has(m.id)) { waiting.get(m.id)(m); waiting.delete(m.id); } } catch { /* events we don't use */ }
    }
  });
  const send = (method, params = {}) => new Promise((resolve) => {
    const id = ++seq;
    waiting.set(id, resolve);
    try { toBrowser.write(JSON.stringify({ id, method, params }) + '\0'); } catch { waiting.delete(id); resolve({ error: { message: 'pipe closed' } }); }
    setTimeout(() => { if (waiting.delete(id)) resolve({ error: { message: 'timeout' } }); }, 5000);
  });
  return new Promise((resolve) => {
    let finished = false, timer = null;
    const finish = (result) => {
      if (finished) return;
      finished = true;
      clearInterval(timer); clearTimeout(limit);
      send('Browser.close').finally(() => setTimeout(() => { try { child.kill(); } catch { /* gone */ } }, 1500));
      resolve(result);
    };
    child.on('error', (e) => { log('browser sign-in: ' + e.message); finish(null); });
    child.on('exit', () => { if (!finished) { finished = true; clearInterval(timer); clearTimeout(limit); resolve(null); } setTimeout(cleanup, 800); });
    const limit = setTimeout(() => { log('browser sign-in timed out'); finish(null); }, timeoutMs);
    timer = setInterval(async () => {
      if (finished) return;
      const r = await send('Storage.getCookies');
      const cookies = r.result?.cookies;
      if (cookies && isDone(cookies)) finish(cookies);
    }, pollMs);
  });
}
