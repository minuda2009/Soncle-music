// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// Service worker: looks lyrics up for the page (the page itself may not call other sites).
import { lookupLyrics } from './shared/lyrics.js';

chrome.runtime.onMessage.addListener((msg, _sender, reply) => {
  if (msg?.type !== 'lyrics' || !msg.track) return false;
  lookupLyrics(msg.track).then((r) => reply(r), () => reply(null));
  return true;   // answer arrives asynchronously
});
