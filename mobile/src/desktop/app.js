// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// @capacitor/app for the Windows app. "pause" (save now) fires when the window is about to close
// (the host posts it) or is minimised/hidden; there is no back button.
const listeners = new Map();

export function emitApp(event, data) {
  for (const fn of listeners.get(event) || []) {
    try { fn(data); } catch { /* one listener must not stop the others */ }
  }
}

export const App = {
  addListener(event, fn) {
    if (!listeners.has(event)) listeners.set(event, new Set());
    listeners.get(event).add(fn);
    return Promise.resolve({ remove: () => listeners.get(event)?.delete(fn) });
  },
  minimizeApp: async () => {},
};

if (window.chrome?.webview) {
  window.chrome.webview.addEventListener('message', (e) => {
    if (e.data?.type === 'pause') emitApp('pause');
  });
  document.addEventListener('visibilitychange', () => {
    if (document.visibilityState === 'hidden') emitApp('pause');
  });
}
