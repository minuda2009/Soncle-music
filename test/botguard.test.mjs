// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
import test from 'node:test';
import assert from 'node:assert/strict';
import { b64bytes, tokenFromBytes, parseChallenge, parseBody } from '../src/botguard.mjs';

test('BotGuard helpers: base64url round trip, challenge decoding, double-encoded bodies', () => {
  const bytes = Uint8Array.from([0, 1, 250, 251, 255, 62, 63]);
  const tok = tokenFromBytes(bytes);
  assert.doesNotMatch(tok, /[+/]/);
  assert.deepEqual([...b64bytes(tok)], [...bytes]);
  assert.equal(tok, Buffer.from(bytes).toString('base64').replace(/\+/g, '-').replace(/\//g, '_'));   // same as the old desktop code
  // /Create answers with the challenge shifted by -97 and base64-encoded
  const cd = ['msg', ['js'], null, 'hash', 'prog', 'gName', null, 'blob'];
  const shifted = Buffer.from(JSON.stringify(cd)).map((b) => (b - 97) & 0xff);
  const ch = parseChallenge(['x', Buffer.from(shifted).toString('base64')]);
  assert.deepEqual([ch.messageId, ch.interpreterJavascript.privateDoNotAccessOrElseSafeScriptWrappedValue, ch.program, ch.globalName, ch.clientExperimentsStateBlob], ['msg', 'js', 'prog', 'gName', 'blob']);
  assert.deepEqual(parseBody(JSON.stringify(JSON.stringify([1, 'a']))), [1, 'a']);
});
