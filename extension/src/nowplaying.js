// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// Soncle Now Playing for the extension: a full-screen player drawn by Soncle (big artwork, the synced
// lyrics beside it, a rail, Soncle's controls) on top of music.youtube.com. It only drives the page's own
// player (the <video> element and the site's own buttons) and hides nothing: whatever the site plays,
// ads included, still plays, and Esc brings the site back. It never reads or records the audio.

export const fmtTime = (s) => (isFinite(s) && s > 0 ? `${Math.floor(s / 60)}:${String(Math.floor(s % 60)).padStart(2, '0')}` : '0:00');

// The rail follows docs/DESIGN.md §3.1. `site` = the label of the site's own tab that opens the same place.
export const RAIL = [
  { id: 'home', label: 'Home', icon: '⌂', site: ['Home'] },
  { id: 'search', label: 'Search', icon: '⌕', site: [] },
  { id: 'library', label: 'Library', icon: '☰', site: ['Library'] },
  { id: 'sound', label: 'Sound', icon: '♫', soon: true },
  { id: 'settings', label: 'Settings', icon: '⚙', settings: true }
];

export const CSS = `
  .np { position: fixed; inset: 0; z-index: 5; display: grid; grid-template-columns: 76px 1fr; grid-template-rows: 1fr auto; color: var(--on-surface, #e4e1e9); background: rgb(var(--surface-rgb, 27 27 33)); overflow: hidden; }
  .np[hidden] { display: none; }
  .np .wash { position: absolute; inset: -10%; background-size: cover; background-position: center; filter: blur(70px) saturate(1.4); opacity: .55; transition: background-image .6s; pointer-events: none; }
  .np .wash::after { content: ''; position: absolute; inset: 0; background: rgb(var(--surface-rgb, 27 27 33) / .55); }
  .np > * { position: relative; }
  .np .rail { grid-row: 1 / 3; display: flex; flex-direction: column; align-items: center; gap: 6px; padding: 18px 0; background: rgb(var(--surface-rgb, 27 27 33) / .6); }
  .np .rail button { width: 64px; background: none; border: 0; border-radius: 16px; padding: 8px 0 6px; display: flex; flex-direction: column; align-items: center; gap: 2px; font-size: 11px; }
  .np .rail button b { font-size: 20px; width: 52px; height: 30px; line-height: 30px; border-radius: 15px; font-weight: 500; }
  .np .rail button:hover b, .np .rail button.on b { background: var(--primary-container, #3b3d63); }
  .np .rail .soonb { opacity: .5; } .np .rail .sp { flex: 1; }
  .np .stage { display: grid; grid-template-columns: minmax(240px, 42%) 1fr; gap: clamp(20px, 4vw, 56px); align-items: center; padding: clamp(16px, 4vw, 48px); min-height: 0; }
  .np .art { display: flex; flex-direction: column; gap: 18px; align-items: flex-start; min-width: 0; }
  .np .art img { width: 100%; max-width: min(46vh, 440px); aspect-ratio: 1; object-fit: cover; border-radius: 28px; box-shadow: 0 18px 60px #0009; background: #0004; }
  .np .meta b { display: block; font-size: clamp(20px, 3vw, 30px); font-weight: 650; line-height: 1.2; } .np .meta span { color: var(--on-surface-var, #c8c5d0); font-size: clamp(14px, 1.8vw, 18px); }
  .np .lyr { height: 100%; min-height: 0; display: flex; flex-direction: column; } .np .lyr .lines { padding: 8px 0 24px; }
  .np .bar { grid-column: 2; padding: 6px clamp(16px, 4vw, 48px) 20px; display: grid; grid-template-columns: 1fr auto 1fr; gap: 14px; align-items: center; }
  .np .seek { grid-column: 1 / 4; display: flex; align-items: center; gap: 10px; font-size: 12px; color: var(--on-surface-var, #c8c5d0); }
  .np input[type=range] { accent-color: var(--primary, #bfc2f0); } .np .seek input { flex: 1; }
  .np .ctl { display: flex; gap: 10px; align-items: center; justify-content: center; }
  .np .ctl button { width: 48px; height: 48px; border-radius: 50%; border: 0; background: var(--primary-container, #3b3d63); font-size: 18px; }
  .np .ctl .pp { width: 64px; height: 64px; font-size: 24px; background: var(--primary, #bfc2f0); color: var(--on-primary, #1b1b21); }
  .np .side { display: flex; align-items: center; gap: 8px; } .np .side.r { justify-content: flex-end; } .np .side input { width: 90px; }
  .np .note { grid-column: 1 / 4; font-size: 11px; color: var(--on-surface-var, #c8c5d0); text-align: center; }
  @media (max-width: 760px) {
    .np { grid-template-columns: 1fr; grid-template-rows: 1fr auto auto; }
    .np .rail { grid-row: 3; flex-direction: row; justify-content: space-around; padding: 6px 0 10px; }
    .np .rail .sp { display: none; } .np .rail button { width: 56px; }
    .np .stage { grid-template-columns: 1fr; grid-auto-rows: min-content; align-content: start; overflow-y: auto; padding: 16px; }
    .np .art { flex-direction: row; align-items: center; } .np .art img { width: 96px; border-radius: 16px; } .np .lyr { height: 46vh; }
    .np .bar { grid-column: 1; grid-template-columns: 1fr; } .np .seek, .np .note { grid-column: 1; } .np .side.r, .np .side.l { display: none; }
  }`;

// ctx: { root (shadow root), linesBox, restoreLines() (puts linesBox back in the panel), video(), siteClick(labels), openSettings(), openCard(), toast(msg) }
export function createNowPlaying(ctx) {
  const { root, linesBox } = ctx;
  const mk = (tag, props = {}, ...kids) => { const e = Object.assign(document.createElement(tag), props); e.append(...kids.filter(Boolean)); return e; };
  const el = mk('div', { className: 'np', hidden: true, role: 'dialog', 'aria-label': 'Soncle Now Playing' });
  const wash = mk('div', { className: 'wash' });
  const img = mk('img', { alt: '' }), title = mk('b'), artist = mk('span');
  const lyr = mk('div', { className: 'lyr' });
  const seek = mk('input', { type: 'range', min: 0, max: 1000, value: 0, 'aria-label': 'Position' });
  const cur = mk('span', { textContent: '0:00' }), len = mk('span', { textContent: '0:00' });
  const pp = mk('button', { className: 'pp', title: 'Play / pause (Space)', 'aria-label': 'Play or pause' });
  const vol = mk('input', { type: 'range', min: 0, max: 100, value: 100, 'aria-label': 'Volume' });
  const side = (cls, ...k) => mk('div', { className: 'side ' + cls }, ...k);
  const prev = mk('button', { textContent: '⏮', title: 'Previous', 'aria-label': 'Previous song' }), next = mk('button', { textContent: '⏭', title: 'Next', 'aria-label': 'Next song' });
  const rail = mk('div', { className: 'rail' }, ...RAIL.map((r) => {
    const b = mk('button', { className: r.soon ? 'soonb' : '', title: r.soon ? r.label + ' (coming soon)' : r.label, onclick: () => {
      if (r.soon) return ctx.toast(`${r.label} is coming soon`);
      if (r.settings) { close(); return ctx.openSettings(); }
      close();
      if (r.id === 'search') { const i = document.querySelector('ytmusic-search-box input'); if (i) i.focus(); else ctx.toast('Use YouTube Music’s own search box'); return; }
      if (!ctx.siteClick(r.site)) ctx.toast(`Use YouTube Music’s own ${r.label} tab`);
    } }, mk('b', { textContent: r.icon }), r.label);
    return b;
  }), mk('div', { className: 'sp' }), mk('button', { textContent: '', title: 'Close (Esc)', onclick: () => close() }, mk('b', { textContent: '✕' }), 'Close'));
  el.append(wash, rail,
    mk('div', { className: 'stage' }, mk('div', { className: 'art' }, img, mk('div', { className: 'meta' }, title, artist)), lyr),
    mk('div', { className: 'bar' },
      mk('div', { className: 'seek' }, cur, seek, len),
      side('l', mk('button', { className: 'chip', textContent: 'Lyric card', onclick: () => ctx.openCard() })),
      mk('div', { className: 'ctl' }, prev, pp, next),
      side('r', mk('span', { textContent: '🔊' }), vol),
      mk('div', { className: 'note', textContent: 'Soncle draws this view; YouTube Music still plays the song, so ads and artist payments work as usual.' })));
  root.append(mk('style', { textContent: CSS }), el);

  const v = () => ctx.video();
  pp.onclick = () => { const x = v(); if (x) (x.paused ? x.play() : x.pause()); };
  const press = (sel) => document.querySelector(sel)?.click();
  prev.onclick = () => { const x = v(); if (x && x.currentTime > 5) x.currentTime = 0; else press('ytmusic-player-bar .previous-button'); };
  next.onclick = () => press('ytmusic-player-bar .next-button');
  let seeking = false;
  seek.oninput = () => { seeking = true; const x = v(); if (x && isFinite(x.duration)) cur.textContent = fmtTime(seek.value / 1000 * x.duration); };
  seek.onchange = () => { const x = v(); if (x && isFinite(x.duration)) x.currentTime = seek.value / 1000 * x.duration; seeking = false; };
  vol.oninput = () => { const x = v(); if (x) x.volume = vol.value / 100; };

  function tick() {
    if (el.hidden) return;
    const x = v();
    if (x) {
      pp.textContent = x.paused ? '▶' : '❚❚';
      if (!seeking && isFinite(x.duration) && x.duration > 0) { seek.value = Math.round(x.currentTime / x.duration * 1000); cur.textContent = fmtTime(x.currentTime); len.textContent = fmtTime(x.duration); }
      if (document.activeElement !== vol) vol.value = Math.round(x.volume * 100);
    }
    requestAnimationFrame(tick);
  }
  function update(t) {
    if (!t) return;
    title.textContent = t.title; artist.textContent = t.artists.map((a) => a.name).join(', ');
    if (t.thumb) { img.src = t.thumb; wash.style.backgroundImage = `url("${t.thumb}")`; }
  }
  function open(t) {
    update(t); lyr.replaceChildren(linesBox); linesBox.hidden = false; el.hidden = false; tick(); pp.focus();
  }
  function close() {
    el.hidden = true; ctx.restoreLines();
  }
  return { el, open, close, update, toggle: (t) => (el.hidden ? open(t) : close()), get isOpen() { return !el.hidden; } };
}
