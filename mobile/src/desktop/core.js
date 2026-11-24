// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// @capacitor/core for the Windows app. The shared backend (mobile/src/backend.js) then runs its
// plugin-free path: stream clients that need no PO token, songs from a MediaSource (mse.js), and
// every request through the host's HTTP relay (host.js). The Soncle plugin isn't there (yet).
import './host.js';

export const Capacitor = {
  isNativePlatform: () => false,
  getPlatform: () => 'windows',
};

export function registerPlugin() {
  return new Proxy({}, {
    get: (_, key) => (key === 'addListener'
      ? () => Promise.resolve({ remove() {} })
      : () => Promise.reject(new Error('Not in the Windows app yet'))),
  });
}
