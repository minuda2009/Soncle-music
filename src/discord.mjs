// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// Minimal Discord Rich Presence over the local IPC pipe (no dependencies).
import net from 'node:net';
import path from 'node:path';
import crypto from 'node:crypto';

// Discord application id. This used to be a hard-coded id belonging to the
// upstream Android project, which is not this app. Override it with your own
// application id via SONCLE_DISCORD_CLIENT_ID, or leave unset to disable RPC.
// Soncle has no Discord application registered yet, so Rich Presence stays off until one is
// configured (set SONCLE_DISCORD_CLIENT_ID, or fill in the id here once it exists).
const CLIENT_ID = process.env.SONCLE_DISCORD_CLIENT_ID || '';
export const DISCORD_AVAILABLE = !!CLIENT_ID;

function pipePath(i) {
  if (process.platform === 'win32') return `\\\\?\\pipe\\discord-ipc-${i}`;
  const base = process.env.XDG_RUNTIME_DIR || process.env.TMPDIR || process.env.TMP || '/tmp';
  return path.join(base, `discord-ipc-${i}`);
}

function encode(op, data) {
  const json = Buffer.from(JSON.stringify(data));
  const header = Buffer.alloc(8);
  header.writeInt32LE(op, 0);
  header.writeInt32LE(json.length, 4);
  return Buffer.concat([header, json]);
}

export class DiscordRPC {
  constructor() {
    this.sock = null;
    this.ready = false;
    this.enabled = false;
    this.pending = null;
    this.retry = null;
  }

  setEnabled(on) {
    if (on && !CLIENT_ID) { this.enabled = false; return; } // no app id configured
    this.enabled = on;
    if (on) this.connect();
    else { this.clear(); setTimeout(() => this.destroy(), 300); }
  }

  destroy() {
    clearTimeout(this.retry);
    this.ready = false;
    try { this.sock?.destroy(); } catch {}
    this.sock = null;
  }

  async connect(i = 0) {
    if (!this.enabled || this.sock) return;
    if (i > 9) { this.#scheduleRetry(); return; }
    const sock = net.createConnection(pipePath(i));
    let buf = Buffer.alloc(0);
    sock.once('connect', () => {
      this.sock = sock;
      sock.write(encode(0, { v: 1, client_id: CLIENT_ID }));
    });
    sock.on('data', (chunk) => {
      buf = Buffer.concat([buf, chunk]);
      while (buf.length >= 8) {
        const len = buf.readInt32LE(4);
        if (buf.length < 8 + len) break;
        const op = buf.readInt32LE(0);
        let msg = null;
        try { msg = JSON.parse(buf.subarray(8, 8 + len).toString()); } catch {}
        buf = buf.subarray(8 + len);
        if (op === 1 && msg?.evt === 'READY') {
          this.ready = true;
          if (this.pending !== undefined) this.#send(this.pending);
        } else if (op === 2) { this.destroy(); this.#scheduleRetry(); }
      }
    });
    sock.once('error', () => {
      sock.destroy();
      if (this.sock === sock) { this.sock = null; this.ready = false; this.#scheduleRetry(); }
      else if (!this.sock) this.connect(i + 1);
    });
    sock.once('close', () => {
      if (this.sock === sock) { this.sock = null; this.ready = false; this.#scheduleRetry(); }
    });
  }

  #scheduleRetry() {
    clearTimeout(this.retry);
    if (this.enabled) this.retry = setTimeout(() => this.connect(), 20000);
  }

  #send(activity) {
    if (!this.sock || !this.ready) return;
    try {
      this.sock.write(encode(1, { cmd: 'SET_ACTIVITY', args: { pid: process.pid, activity }, nonce: crypto.randomUUID() }));
    } catch {}
  }

  update(state) {
    if (!this.enabled) return;
    let activity = null;
    const t = state?.track;
    if (t && state.playing) {
      const now = Date.now();
      const start = now - (state.position || 0) * 1000;
      activity = {
        type: 2,
        details: (t.title || 'Unknown').slice(0, 127),
        state: ((t.artists || []).map((a) => a.name).join(', ') || 'Soncle').slice(0, 127),
        timestamps: state.duration ? { start: Math.round(start), end: Math.round(start + state.duration * 1000) } : { start: Math.round(start) },
        assets: {
          // Only pass artwork through when it is a stable YouTube image host, so
          // Discord never fetches an arbitrary URL supplied by a track payload.
          large_image: t.thumb && /^https:\/\/(i\.ytimg\.com|lh\d\.googleusercontent\.com|yt\d\.ggpht\.com)\//.test(t.thumb) ? t.thumb : 'soncle',
          large_text: (t.album?.name || t.title || '').slice(0, 127) || undefined,
          small_text: 'Soncle'
        },
        buttons: [{ label: 'Listen on YouTube Music', url: `https://music.youtube.com/watch?v=${t.id}` }],
        instance: false
      };
    }
    this.pending = activity;
    if (!this.sock) this.connect();
    else this.#send(activity);
  }

  clear() { this.pending = null; this.#send(null); }
}
