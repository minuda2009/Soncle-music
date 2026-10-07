// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// The platform-neutral half of PO token generation, shared by the desktop app (src/potoken.mjs,
// hidden Electron window) and the Android app (mobile/src/potoken.js, hidden Android WebView).
// Both run src/po_token.html with a youtube.com origin; this file talks to the BotGuard endpoints.

// Not secrets: public identifiers that YouTube's own web player sends from every browser for its
// BotGuard attestation calls, copied unchanged from the upstream PoTokenWebView.kt (REQUEST_KEY /
// GOOGLE_API_KEY). No account or billing is tied to them.
export const REQUEST_KEY = 'O43z0dpjhgX20SCx4KAo';
export const API_KEY = 'AIzaSyDyT5W0Jh49F30Pqqtyfdf7pDLFKLJoAnw';
export const CREATE_URL = 'https://www.youtube.com/api/jnn/v1/Create';
export const GENERATE_IT_URL = 'https://www.youtube.com/api/jnn/v1/GenerateIT';

/** Headers for the two BotGuard POST requests. */
export const bgHeaders = (ua) => ({ 'User-Agent': ua, Accept: 'application/json', 'Content-Type': 'application/json+protobuf', 'x-goog-api-key': API_KEY, 'x-user-agent': 'grpc-web-javascript/0.1' });

/** base64 / base64url (with "." padding) → bytes */
export function b64bytes(s) {
  const bin = globalThis.atob(String(s).replace(/-/g, '+').replace(/_/g, '/').replace(/\./g, '='));
  const out = new Uint8Array(bin.length);
  for (let i = 0; i < bin.length; i++) out[i] = bin.charCodeAt(i);
  return out;
}
/** bytes → base64url, the form PO tokens are sent in */
export function tokenFromBytes(bytes) {
  let bin = '';
  for (const b of bytes) bin += String.fromCharCode(b);
  return globalThis.btoa(bin).replace(/\+/g, '-').replace(/\//g, '_');
}
/** The response body of a BotGuard request, which some HTTP layers hand back double-encoded. */
export function parseBody(text) {
  let j = JSON.parse(text);
  if (typeof j === 'string') j = JSON.parse(j);
  return j;
}

/** The /Create response → the challenge object runBotGuard() in po_token.html expects. */
export function parseChallenge(raw) {
  let cd;
  if (raw.length > 1 && typeof raw[1] === 'string') {
    const buf = b64bytes(raw[1]);
    for (let i = 0; i < buf.length; i++) buf[i] = (buf[i] + 97) & 0xff;
    cd = JSON.parse(new globalThis.TextDecoder().decode(buf));
  } else cd = raw[0];
  const str = (x) => (Array.isArray(x) ? x.find((v) => typeof v === 'string') : null) ?? null;
  return {
    messageId: cd[0],
    interpreterJavascript: { privateDoNotAccessOrElseSafeScriptWrappedValue: str(cd[1]), privateDoNotAccessOrElseTrustedResourceUrlWrappedValue: str(cd[2]) },
    interpreterHash: cd[3], program: cd[4], globalName: cd[5], clientExperimentsStateBlob: cd[7]
  };
}
