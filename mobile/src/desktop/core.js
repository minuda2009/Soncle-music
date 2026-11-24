// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// @capacitor/core for the Windows app. The shared backend (mobile/src/backend.js) runs its
// desktop path: every request through the host's HTTP relay (host.js), songs from a MediaSource
// (mse.js), and PO tokens from BotGuard in the host's hidden WebView2 — the "Soncle" plugin's
// botguard* calls are answered by uno/heads/Soncle.Windows/Web/WebShell.cs. Other plugin calls
// aren't on Windows yet and reject.
import { post } from './host.js';

export const Capacitor = {
  isNativePlatform: () => false,
  getPlatform: () => 'windows',
};

const HOST_METHODS = new Set(['botguardLoad', 'botguardExec', 'botguardReset']);
const pending = new Map();
let seq = 0;

if (window.chrome?.webview) {
  window.chrome.webview.addEventListener('message', (e) => {
    const m = e.data;
    if (m?.type !== 'result' || !pending.has(m.id)) return;
    const { resolve, reject } = pending.get(m.id);
    pending.delete(m.id);
    if (m.ok) resolve(m.data ?? {});
    else reject(new Error(m.error || 'host call failed'));
  });
}

function hostCall(plugin, method, args) {
  if (!window.chrome?.webview) return Promise.reject(new Error('Not in the Windows app'));
  const id = ++seq;
  return new Promise((resolve, reject) => {
    pending.set(id, { resolve, reject });
    post({ type: 'call', id, plugin, method, args: args ?? {} });
  });
}

export function registerPlugin(name) {
  return new Proxy({}, {
    get: (_, key) => {
      if (key === 'addListener') return () => Promise.resolve({ remove() {} });
      if (name === 'Soncle' && HOST_METHODS.has(key)) return (args) => hostCall(name, key, args);
      return () => Promise.reject(new Error('Not in the Windows app yet'));
    },
  });
}
