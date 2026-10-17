// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// Runs on music.youtube.com. Adds a lyrics panel and lyric cards next to the page's own player. It
// only reads the song title and the player's clock; it does not touch the audio or the ads.
(async () => {
  if (window.__soncleCompanion) return;
  window.__soncleCompanion = true;
  const url = (p) => chrome.runtime.getURL(p);
  const [{ parseLrc }, { createCard, BACKGROUNDS }, { renderPng, recordVideo, saveBlob, videoType }, { FORMATS, untimed }] = await Promise.all([
    import(url('shared/lrc.js')), import(url('shared/lyriccard/draw.js')), import(url('shared/lyriccard/export.js')), import(url('shared/lyriccard/layout.js'))
  ]);

  // ---------- reading the page ----------
  const $ = (s, r = document) => r.querySelector(s);
  function readTrack() {
    const bar = $('ytmusic-player-bar');
    const title = $('.title', bar || document)?.textContent?.trim();
    if (!bar || !title) return null;
    const parts = ($('.byline', bar)?.textContent || '').split('•').map((x) => x.trim()).filter(Boolean);
    const artists = (parts[0] || '').split(/\s*(?:,|&)\s*/).filter(Boolean).map((name) => ({ name }));
    const album = parts[1] && !/^\d{4}$/.test(parts[1]) ? { name: parts[1] } : null;
    const v = $('video');
    return { key: `${title}|${artists.map((a) => a.name).join(',')}`, title, artists, album, duration: v && isFinite(v.duration) ? v.duration : 0, thumb: $('img', bar)?.src || '' };
  }
  const names = (t) => t.artists.map((a) => a.name).join(', ');

  // ---------- per-song timing offsets ----------
  const store = {
    async get() { try { return (await chrome.storage.local.get('offsets')).offsets || {}; } catch { return {}; } },
    async set(o) { try { await chrome.storage.local.set({ offsets: o }); } catch { /* storage unavailable */ } }
  };
  let offsets = await store.get();

  // ---------- UI (shadow DOM so the page's styles cannot reach it) ----------
  const host = document.createElement('div');
  host.id = 'soncle-companion';
  host.style.cssText = 'position:fixed;z-index:2147483000;inset:auto 16px 96px auto;';
  const root = host.attachShadow({ mode: 'open' });
  root.innerHTML = `<style>
    :host { all: initial; font-family: "Segoe UI", Roboto, system-ui, sans-serif; color: #e4e1e9; }
    * { box-sizing: border-box; }
    button { font: inherit; color: inherit; cursor: pointer; }
    .fab { background: #3b3d63; border: 0; border-radius: 20px; padding: 10px 16px; font-weight: 650; box-shadow: 0 4px 16px #0006; }
    .panel { position: fixed; right: 16px; bottom: 150px; width: 380px; max-width: calc(100vw - 32px); height: min(560px, 70vh); background: #1b1b21f2; backdrop-filter: blur(18px); border-radius: 24px; box-shadow: 0 12px 48px #000a; display: flex; flex-direction: column; overflow: hidden; }
    .panel[hidden], .modal[hidden] { display: none; }
    header { padding: 16px 18px 8px; display: flex; gap: 8px; align-items: flex-start; }
    header .t { flex: 1; min-width: 0; } header b { display: block; font-size: 16px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; } header span { color: #c8c5d0; font-size: 13px; }
    .lines { flex: 1; overflow-y: auto; padding: 8px 18px 24px; scroll-behavior: smooth; }
    .ln { font-size: 22px; font-weight: 700; line-height: 1.3; padding: 7px 0; opacity: .32; cursor: pointer; transition: opacity .3s, transform .3s; transform-origin: left; }
    .ln.on { opacity: 1; transform: scale(1.04); } .ln.past { opacity: .5; }
    .plain .ln { font-size: 16px; font-weight: 500; opacity: .85; cursor: default; }
    .msg { padding: 28px 18px; color: #c8c5d0; }
    footer { display: flex; align-items: center; gap: 6px; padding: 8px 14px 14px; font-size: 12px; color: #c8c5d0; flex-wrap: wrap; }
    footer .sp { flex: 1; }
    .chip { background: transparent; border: 1px solid #47464f; border-radius: 14px; padding: 4px 10px; font-size: 12px; }
    .chip:hover, .x:hover { background: #ffffff1a; }
    .x { background: none; border: 0; border-radius: 50%; width: 32px; height: 32px; font-size: 18px; }
    .modal { position: fixed; inset: 0; background: #0009; display: flex; align-items: center; justify-content: center; }
    .dlg { background: #2a292f; border-radius: 28px; padding: 22px; width: 640px; max-width: 94vw; max-height: 94vh; overflow: auto; }
    .dlg h3 { margin: 0 0 14px; font-weight: 500; font-size: 22px; }
    .body { display: flex; gap: 16px; align-items: flex-start; } .body canvas { flex: none; background: #000; border-radius: 12px; max-height: 52vh; }
    .opts { flex: 1; min-width: 0; display: flex; flex-direction: column; gap: 9px; }
    .row { display: flex; align-items: center; gap: 6px; flex-wrap: wrap; } .row label { width: 84px; color: #c8c5d0; font-size: 13px; }
    .row .chip.on { background: #3b3d63; border-color: #3b3d63; font-weight: 650; }
    .pick { max-height: 140px; overflow: auto; border: 1px solid #47464f; border-radius: 12px; }
    .pick div { padding: 6px 12px; font-size: 13px; cursor: pointer; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; } .pick .on { background: #3b3d63; }
    .st { margin-top: 10px; font-size: 12.5px; color: #c8c5d0; min-height: 18px; }
    .act { display: flex; justify-content: flex-end; gap: 8px; margin-top: 16px; }
    .btn { border: 0; border-radius: 20px; padding: 10px 18px; font-weight: 650; background: #3b3d63; } .btn.main { background: #bfc2f0; color: #1b1b21; } .btn:disabled { opacity: .4; pointer-events: none; } .btn.text { background: none; color: #bfc2f0; }
    @media (max-width: 560px) { .body { flex-direction: column; align-items: center; } .body canvas { max-height: 26vh; } }
  </style>
  <button class="fab" title="Lyrics and lyric cards">Soncle</button>
  <div class="panel" hidden><header><div class="t"><b class="ti"></b><span class="ar"></span></div><button class="x" title="Close">×</button></header><div class="lines"></div>
    <footer><span class="src"></span><span class="sp"></span><button class="chip card">Lyric card</button><span class="adj"></span></footer></div>
  <div class="modal" hidden></div>`;
  document.documentElement.append(host);
  const fab = $('.fab', root), panel = $('.panel', root), linesBox = $('.lines', root), modal = $('.modal', root);
  fab.onclick = () => { panel.hidden = !panel.hidden; if (!panel.hidden) { follow(true); } };
  $('.x', root).onclick = () => { panel.hidden = true; };
  const adj = $('.adj', root);
  for (const [label, d] of [['−0.5', -0.5], ['−0.1', -0.1], ['+0.1', 0.1], ['+0.5', 0.5]]) {
    const b = Object.assign(document.createElement('button'), { className: 'chip', textContent: label, title: 'Show lyrics ' + (d < 0 ? 'earlier' : 'later') });
    b.onclick = () => nudge(d); adj.append(b);
  }

  // ---------- lyrics ----------
  let track = null, lyr = null, lineEls = [], active = -2, userScroll = 0;
  const off = () => (track ? offsets[track.key] || 0 : 0);
  async function nudge(d) {
    if (!track) return;
    const v = Math.round(((offsets[track.key] || 0) + d) * 10) / 10;
    if (Math.abs(v) < 0.05) delete offsets[track.key]; else offsets[track.key] = Math.max(-10, Math.min(10, v));
    await store.set(offsets); active = -2; follow(true);
  }
  function show(msg) { linesBox.replaceChildren(Object.assign(document.createElement('div'), { className: 'msg', textContent: msg })); lineEls = []; }
  function render() {
    lineEls = [];
    if (!lyr) return show('No lyrics found for this song.');
    if (!lyr.lines?.length) {
      linesBox.className = 'lines plain';
      linesBox.replaceChildren(...lyr.plain.split(/\n/).map((x) => Object.assign(document.createElement('div'), { className: 'ln', textContent: x || ' ' })));
      return;
    }
    linesBox.className = 'lines';
    lineEls = lyr.lines.map((l, i) => {
      const el = Object.assign(document.createElement('div'), { className: 'ln', textContent: l.text || '♪' });
      el.onclick = () => { const v = $('video'); if (v) v.currentTime = Math.max(0, l.t + off() + 0.01); };
      return el;
    });
    linesBox.replaceChildren(...lineEls);
  }
  async function load(t) {
    track = t; lyr = null; active = -2; $('.ti', root).textContent = t.title; $('.ar', root).textContent = names(t); $('.src', root).textContent = '';
    show('Looking for lyrics…');
    let res = null;
    try { res = await chrome.runtime.sendMessage({ type: 'lyrics', track: { title: t.title, artists: t.artists, album: t.album, duration: t.duration } }); } catch { /* worker asleep or gone */ }
    if (track?.key !== t.key) return;
    if (res && (res.synced || res.plain)) { lyr = { lines: res.synced ? parseLrc(res.synced) : null, plain: res.plain, source: res.source }; $('.src', root).textContent = 'Lyrics from ' + res.source; }
    render();
  }
  function follow(force) {
    const v = $('video');
    if (!v || !lyr?.lines?.length || panel.hidden && !force) return;
    const now = v.currentTime - off();
    let i = -1;
    for (let k = 0; k < lyr.lines.length && lyr.lines[k].t <= now; k++) i = k;
    if (i === active && !force) return;
    active = i;
    lineEls.forEach((el, k) => { el.classList.toggle('on', k === i); el.classList.toggle('past', k < i); });
    if (i >= 0 && Date.now() - userScroll > 4000) lineEls[i].scrollIntoView({ block: 'center', behavior: force ? 'auto' : 'smooth' });
  }
  linesBox.addEventListener('wheel', () => { userScroll = Date.now(); }, { passive: true });
  linesBox.addEventListener('touchmove', () => { userScroll = Date.now(); }, { passive: true });

  // ---------- lyric card ----------
  $('.card', root).onclick = () => cardDialog();
  async function cardDialog() {
    const synced = !!lyr?.lines?.length;
    const rows = synced ? lyr.lines.filter((l) => l.text) : (lyr?.plain || '').split(/\n/).map((x) => x.trim()).filter(Boolean).map((text) => ({ text }));
    if (!track || !rows.length) return;
    const st = { start: Math.max(0, rows.indexOf(lyr.lines?.[Math.max(0, active)])), count: 3, format: 'story', background: 'cover', align: 'center' };
    const cover = await new Promise((res) => { if (!track.thumb) return res(null); const im = new Image(); im.crossOrigin = 'anonymous'; im.onload = () => res(im); im.onerror = () => res(null); im.src = track.thumb; });
    const canRecord = synced && !!videoType(), MAX_S = 60;
    const mctx = document.createElement('canvas').getContext('2d'), pv = document.createElement('canvas'), pctx = pv.getContext('2d');
    let card = null, t0 = performance.now(), busy = null;
    const picked = () => rows.slice(st.start, st.start + st.count);
    const base = () => ({ format: st.format, background: st.background, align: st.align, title: track.title, artist: names(track), cover });
    const still = () => createCard({ lines: synced ? picked() : untimed(picked().map((r) => r.text)), still: true, ...base() }, mctx);
    const el = (tag, props = {}, ...kids) => { const e = Object.assign(document.createElement(tag), props); e.append(...kids.filter(Boolean)); return e; };
    const status = el('div', { className: 'st' });
    const saveImg = el('button', { className: 'btn', textContent: 'Save image', onclick: async () => { try { await saveBlob(await renderPng(still()), file('png')); } catch { status.textContent = 'Could not save the image'; } } });
    const saveVid = el('button', { className: 'btn main', textContent: 'Save video', onclick: () => video() });
    const file = (ext) => `${(track.title || 'lyrics').replace(/[^\p{L}\p{N}]+/gu, '-').replace(/^-|-$/g, '').slice(0, 50) || 'lyrics'}-lyrics.${ext}`;
    const rebuild = () => {
      if (busy) return;
      const f = FORMATS[st.format]; pv.width = Math.round(f.w * 0.2); pv.height = Math.round(f.h * 0.2);
      card = synced ? createCard({ lines: picked(), ...base() }, mctx) : still(); t0 = performance.now();
      const long = synced && card.duration > MAX_S;
      saveVid.hidden = !canRecord; saveVid.disabled = long || !card.duration;
      status.textContent = !synced ? 'These lyrics have no timing, so only the image is available.' : !canRecord ? 'This browser cannot record video; the image still works.' : long ? `That is ${Math.round(card.duration)} s: pick fewer lines (up to ${MAX_S} s).` : `Video: ${card.duration.toFixed(1)} s, silent. Add the song in Instagram’s music picker so the artist is still paid.`;
    };
    async function video() {
      if (busy) { busy.abort(); return; }
      busy = new AbortController(); saveImg.disabled = true; saveVid.textContent = 'Cancel';
      try { const b = await recordVideo(card, { signal: busy.signal, onProgress: (p) => { status.textContent = `Recording ${Math.round(p * 100)}%…`; } }); await saveBlob(b, file(b.type.includes('mp4') ? 'mp4' : 'webm')); }
      catch (e) { if (e.message !== 'Cancelled') status.textContent = 'Could not make the video'; }
      busy = null; saveImg.disabled = false; saveVid.textContent = 'Save video'; rebuild();
    }
    const chips = (key, options) => { const box = el('div', { className: 'row' }); const draw = () => box.replaceChildren(...options.map(([v, l]) => el('button', { className: 'chip' + (st[key] === v ? ' on' : ''), textContent: l, onclick: () => { if (busy) return; st[key] = v; draw(); rebuild(); } }))); draw(); return box; };
    const list = el('div', { className: 'pick' });
    const drawList = () => list.replaceChildren(...rows.map((r, i) => el('div', { className: i >= st.start && i < st.start + st.count ? 'on' : '', textContent: r.text, onclick: () => { if (busy) return; st.start = i; drawList(); rebuild(); } })));
    const count = el('span', { textContent: String(st.count) });
    const step = (d) => { if (busy) return; st.count = Math.max(1, Math.min(10, st.count + d)); count.textContent = st.count; drawList(); rebuild(); };
    const close = () => { busy?.abort(); modal.hidden = true; modal.replaceChildren(); };
    drawList(); rebuild();
    modal.replaceChildren(el('div', { className: 'dlg' }, el('h3', { textContent: 'Lyric card' }),
      el('div', { className: 'body' }, pv, el('div', { className: 'opts' },
        el('div', { className: 'row' }, el('label', { textContent: 'Lines' }), el('button', { className: 'chip', textContent: '−', onclick: () => step(-1) }), count, el('button', { className: 'chip', textContent: '+', onclick: () => step(1) })),
        list,
        el('div', { className: 'row' }, el('label', { textContent: 'Size' }), chips('format', [['story', '9:16'], ['portrait', '4:5'], ['square', '1:1']])),
        el('div', { className: 'row' }, el('label', { textContent: 'Background' }), chips('background', BACKGROUNDS)),
        el('div', { className: 'row' }, el('label', { textContent: 'Text' }), chips('align', [['center', 'Centre'], ['left', 'Left']])))),
      status, el('div', { className: 'act' }, el('button', { className: 'btn text', textContent: 'Close', onclick: close }), saveImg, saveVid)));
    modal.hidden = false;
    modal.onmousedown = (e) => { if (e.target === modal) close(); };
    const loop = () => { if (modal.hidden) return; if (card && !busy) card.render(pctx, synced ? ((performance.now() - t0) / 1000) % (card.duration + 1.2) : 0, 0.2); requestAnimationFrame(loop); };
    requestAnimationFrame(loop);
  }

  // ---------- main loop ----------
  setInterval(() => { const t = readTrack(); if (t && t.key !== track?.key) load(t); else if (t && track && !track.duration && t.duration) track.duration = t.duration; }, 700);
  (function tick() { follow(false); requestAnimationFrame(tick); })();
})();
