// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// Builds the Android app's web layer into www/: the desktop renderer (shared, unchanged) plus the
// Android backend bundle and the phone stylesheet.
//   node build.mjs            production build
//   SONCLE_HARNESS=/path/harness.js node build.mjs --test
//                             development only: bundles a harness (offline YouTube double) first
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import * as esbuild from 'esbuild';

const here = path.dirname(fileURLToPath(import.meta.url));
const root = path.join(here, '..');
const www = path.join(here, 'www');
const test = process.argv.includes('--test');

// Node built-ins only appear in desktop-only code paths of the shared modules; on the phone they
// resolve to an empty module.
const noNode = {
  name: 'no-node',
  setup(b) {
    b.onResolve({ filter: /^node:/ }, (a) => ({ path: a.path, namespace: 'no-node' }));
    b.onLoad({ filter: /.*/, namespace: 'no-node' }, () => ({ contents: 'export default {};', loader: 'js' }));
  }
};

fs.rmSync(www, { recursive: true, force: true });
fs.cpSync(path.join(root, 'renderer'), www, { recursive: true });
fs.copyFileSync(path.join(root, 'THIRD_PARTY_NOTICES.md'), path.join(www, 'THIRD_PARTY_NOTICES.md'));
fs.copyFileSync(path.join(here, 'src', 'mobile.css'), path.join(www, 'mobile.css'));

const common = { bundle: true, format: 'iife', platform: 'browser', target: 'chrome100', mainFields: ['browser', 'module', 'main'], conditions: ['browser'], plugins: [noNode], legalComments: 'none', logLevel: 'warning', nodePaths: [path.join(here, 'node_modules')] };
const backend = path.join(here, 'src', 'backend.js');
const entry = test
  ? { stdin: { contents: `import ${JSON.stringify(process.env.SONCLE_HARNESS)};\nimport ${JSON.stringify(backend)};`, resolveDir: here, loader: 'js' } }
  : { entryPoints: [backend] };
await esbuild.build({ ...common, ...entry, outfile: path.join(www, 'backend.js'), minify: !test, loader: { '.json': 'json' } });

let html = fs.readFileSync(path.join(www, 'index.html'), 'utf8');
html = html
  // youtubei.js deciphers stream URLs with generated code (eval), and audio plays from blob: MediaSource URLs
  .replace(/script-src 'self'/, "script-src 'self' 'unsafe-eval'")
  .replace('<meta charset="utf-8" />', '<meta charset="utf-8" />\n  <meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover, interactive-widget=resizes-content" />')
  .replace('<link rel="stylesheet" href="styles.css" />', '<link rel="stylesheet" href="styles.css" />\n  <link rel="stylesheet" href="mobile.css" />')
  .replace('<script type="module" src="app.js"></script>', '<script src="backend.js"></script>\n  <script type="module" src="app.js"></script>');
if (!/unsafe-eval/.test(html) || !/backend\.js/.test(html)) throw new Error('index.html changed shape; update build.mjs');
fs.writeFileSync(path.join(www, 'index.html'), html);
console.log('www/ built' + (test ? ' (with test harness)' : ''));
