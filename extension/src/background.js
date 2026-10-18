// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// Service worker: looks lyrics up for the page (the page itself may not call other sites) and keeps
// the small local lyrics cache. Nothing leaves the browser except the LRCLIB request.
import { cachedLookup } from './shared/lyrics.js';

const store = {
  async get() { return (await chrome.storage.local.get('lyrcache')).lyrcache || {}; },
  async set(v) { await chrome.storage.local.set({ lyrcache: v }); }
};

chrome.runtime.onMessage.addListener((msg, _sender, reply) => {
  if (msg?.type === 'lyrics' && msg.track) {
    cachedLookup(msg.track, store).then((r) => reply(r), () => reply(null));
    return true;   // answer arrives asynchronously
  }
  if (msg?.type === 'clearCache') {
    store.get().then(async (all) => { const n = Object.keys(all).length; await chrome.storage.local.remove('lyrcache'); reply({ cleared: n }); }, () => reply({ cleared: 0 }));
    return true;
  }
  return false;
});
