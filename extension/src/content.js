// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// Runs on music.youtube.com. Adds Soncle features next to the real player.
// Strictly not an ad blocker: never hides ads, promos or pays walls; never touches the stream.
(async () => {
  if (window.__soncleCompanion) return;
  window.__soncleCompanion = true;
  const url = (p) => chrome.runtime.getURL(p);
  const [{ parseLrc }, { createCard, BACKGROUNDS }, { renderPng, recordVideo, saveBlob, videoType, canShareFiles }, { FORMATS, TEXT_SIZES, untimed, clipLines }, { MOTIONS }, { themeVars, dominantHue }, { cssFor, resolveMode, accentHs, ACCENTS, DEFAULTS }, { GROUPS, ABOUT, STATUS_LABEL }, { createNowPlaying }] = await Promise.all([
    import(url('shared/lrc.js')), import(url('shared/lyriccard/draw.js')), import(url('shared/lyriccard/export.js')), import(url('shared/lyriccard/layout.js')), import(url('shared/lyriccard/motion.js')),
    import(url('shared/palette.js')), import(url('shared/theme.js')), import(url('shared/features.js')), import(url('shared/nowplaying.js'))
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
  const prefs = {
    async get() { try { return { ...DEFAULTS, ...((await chrome.storage.local.get('settings')).settings || {}) }; } catch { return { ...DEFAULTS }; } },
    async set(v) { try { await chrome.storage.local.set({ settings: v }); } catch { /* storage unavailable */ } }
  };
  let settings = await prefs.get();

  // ---------- UI (shadow DOM so the page's styles cannot reach it) ----------
  const host = document.createElement('div');
  host.id = 'soncle-companion';
  host.style.cssText = 'position:fixed;z-index:2147483000;inset:auto 16px 96px auto;';
  const root = host.attachShadow({ mode: 'open' });
  root.innerHTML = `<style>
    :host { all: initial; font-family: "Segoe UI", Roboto, system-ui, sans-serif; color: var(--on-surface, #e4e1e9); }
    * { box-sizing: border-box; }
    button { font: inherit; color: inherit; cursor: pointer; }
    .fab { background: var(--primary-container, #3b3d63); color: var(--on-primary-container, #e4e1e9); border: 0; border-radius: 20px; padding: 10px 16px; font-weight: 650; box-shadow: 0 4px 16px #0006; }
    .panel { position: fixed; right: 16px; bottom: 150px; width: 380px; max-width: calc(100vw - 32px); height: min(560px, 70vh); background: rgb(var(--surface-rgb, 27 27 33) / .95); backdrop-filter: blur(18px); border-radius: 24px; box-shadow: 0 12px 48px #000a; display: flex; flex-direction: column; overflow: hidden; }
    .panel[hidden], .modal[hidden] { display: none; }
    header { padding: 16px 18px 8px; display: flex; gap: 8px; align-items: flex-start; }
    header .t { flex: 1; min-width: 0; } header b { display: block; font-size: 16px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; } header span { color: var(--on-surface-var, #c8c5d0); font-size: 13px; }
    .lines { flex: 1; overflow-y: auto; padding: 8px 18px 24px; scroll-behavior: smooth; }
    .lines { text-align: var(--ly-align, left); } .ln { font-size: var(--ly-size, 22px); font-weight: 700; line-height: 1.3; padding: 7px 0; opacity: .32; cursor: pointer; transition: opacity .3s, transform .3s; transform-origin: var(--ly-origin, left); }
    .glow .ln.on { text-shadow: 0 0 18px var(--primary, #bfc2f0); }
    .ln.on { opacity: 1; transform: scale(1.04); } .ln.past { opacity: .5; }
    .plain .ln { font-size: calc(var(--ly-size, 22px) * .73); font-weight: 500; opacity: .85; cursor: default; }
    .set { flex: 1; overflow-y: auto; padding: 0 14px 14px; } .set[hidden] { display: none; }
    .banner { margin: 4px 0 10px; padding: 10px 12px; border-radius: 14px; background: var(--primary-container, #3b3d63); color: var(--on-primary-container, #e4e1e9); font-size: 12.5px; line-height: 1.4; }
    .banner b { display: block; font-size: 13.5px; }
    details { border-top: 1px solid var(--outline-var, #47464f); } details > summary { cursor: pointer; padding: 11px 4px; font-weight: 650; font-size: 14px; list-style: none; display: flex; justify-content: space-between; }
    details > summary::after { content: '▾'; color: var(--on-surface-var, #c8c5d0); } details:not([open]) > summary::after { content: '▸'; }
    .item { padding: 8px 4px 10px; display: flex; flex-direction: column; gap: 6px; }
    .item .ih { display: flex; gap: 8px; align-items: center; flex-wrap: wrap; font-size: 13.5px; }
    .item.soon, .item.app, .item.site { opacity: .7; }
    .hint { font-size: 11.5px; color: var(--on-surface-var, #c8c5d0); line-height: 1.4; }
    .tag { font-size: 10px; font-weight: 700; letter-spacing: .04em; text-transform: uppercase; color: var(--primary, #bfc2f0); border: 1px solid var(--outline-var, #47464f); border-radius: 999px; padding: 2px 8px; white-space: nowrap; }
    .item input[type=range] { flex: 1; min-width: 120px; accent-color: var(--primary, #bfc2f0); } .val { font-size: 12px; min-width: 52px; color: var(--on-surface-var, #c8c5d0); }
    .toast { z-index: 20; position: fixed; left: 50%; bottom: 110px; transform: translateX(-50%) translateY(8px); background: var(--surface-3, #2a292f); color: var(--on-surface, #e4e1e9); padding: 10px 18px; border-radius: 16px; font-size: 13px; box-shadow: 0 6px 24px #0008; opacity: 0; pointer-events: none; transition: opacity .2s, transform .2s; max-width: 90vw; }
    .toast.show { opacity: 1; transform: translateX(-50%); }
    button:focus-visible, input:focus-visible, summary:focus-visible { outline: 2px solid var(--primary, #bfc2f0); outline-offset: 2px; }
    .sw { width: 22px; height: 22px; border-radius: 50%; border: 2px solid transparent; padding: 0; } .sw.on { border-color: var(--on-surface, #fff); }
    .msg { display: flex; flex-direction: column; align-items: flex-start; gap: 10px; padding: 28px 18px; color: var(--on-surface-var, #c8c5d0); }
    footer { display: flex; align-items: center; gap: 6px; padding: 8px 14px 14px; font-size: 12px; color: var(--on-surface-var, #c8c5d0); flex-wrap: wrap; }
    footer .sp { flex: 1; }
    .chip { background: transparent; border: 1px solid var(--outline-var, #47464f); border-radius: 14px; padding: 4px 10px; font-size: 12px; }
    .chip:hover, .x:hover { background: rgb(var(--ov, 255 255 255) / .1); }
    .x { background: none; border: 0; border-radius: 50%; width: 32px; height: 32px; font-size: 18px; }
    .modal { position: fixed; z-index: 10; inset: 0; background: #0009; display: flex; align-items: center; justify-content: center; }
    .dlg { background: var(--surface-3, #2a292f); border-radius: 28px; padding: 22px; width: 640px; max-width: 94vw; max-height: 94vh; overflow: auto; }
    .dlg h3 { margin: 0 0 14px; font-weight: 500; font-size: 22px; }
    .body { display: flex; gap: 16px; align-items: flex-start; } .body canvas { flex: none; background: #000; border-radius: 12px; max-height: 52vh; }
    .opts { flex: 1; min-width: 0; display: flex; flex-direction: column; gap: 9px; }
    .row { display: flex; align-items: center; gap: 6px; flex-wrap: wrap; } .row label { width: 84px; color: var(--on-surface-var, #c8c5d0); font-size: 13px; } .row[hidden] { display: none; }
    .row .chip.on { background: var(--primary-container, #3b3d63); border-color: var(--primary-container, #3b3d63); font-weight: 650; }
    .pick { max-height: 140px; overflow: auto; border: 1px solid var(--outline-var, #47464f); border-radius: 12px; }
    .pick div { padding: 6px 12px; font-size: 13px; cursor: pointer; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; } .pick .on { background: var(--primary-container, #3b3d63); }
    .st { margin-top: 10px; font-size: 12.5px; color: var(--on-surface-var, #c8c5d0); min-height: 18px; }
    .act { display: flex; justify-content: flex-end; gap: 8px; margin-top: 16px; }
    .btn { border: 0; border-radius: 20px; padding: 10px 18px; font-weight: 650; background: var(--primary-container, #3b3d63); } .btn.main { background: var(--primary, #bfc2f0); color: var(--on-primary, #1b1b21); } .btn:disabled { opacity: .4; pointer-events: none; } .btn.text { background: none; color: var(--primary, #bfc2f0); }
    @media (max-width: 560px) { .body { flex-direction: column; align-items: center; } .body canvas { max-height: 26vh; } }
  </style>
  <button class="fab" title="Lyrics and lyric cards">Soncle</button>
  <div class="panel" hidden><header><div class="t"><b class="ti"></b><span class="ar"></span></div><button class="x" title="Close" aria-label="Close">×</button></header><div class="set" hidden></div><div class="lines"></div>
    <footer><span class="src"></span><span class="sp"></span><button class="chip look" title="Settings and every Soncle feature">Settings</button><button class="chip full" title="Full-screen Soncle Now Playing (Alt+N)">Full screen</button><button class="chip card">Lyric card</button><span class="adj"></span></footer></div>
  <div class="modal" hidden></div><div class="toast" role="status" aria-live="polite"></div>`;
  document.documentElement.append(host);
  const fab = $('.fab', root), panel = $('.panel', root), linesBox = $('.lines', root), modal = $('.modal', root), toastEl = $('.toast', root);
  let toastTimer = 0;
  const toast = (msg) => { toastEl.textContent = msg; toastEl.classList.add('show'); clearTimeout(toastTimer); toastTimer = setTimeout(() => toastEl.classList.remove('show'), 2600); };
  fab.onclick = () => { panel.hidden = !panel.hidden; if (!panel.hidden) { follow(true); } };
  $('.x', root).onclick = () => { panel.hidden = true; };
  const adj = $('.adj', root);
  for (const [label, d] of [['−0.5', -0.5], ['−0.1', -0.1], ['+0.1', 0.1], ['+0.5', 0.5]]) {
    const b = Object.assign(document.createElement('button'), { className: 'chip', textContent: label, title: 'Show lyrics ' + (d < 0 ? 'earlier' : 'later') });
    b.onclick = () => nudge(d); adj.append(b);
  }

  // ---------- Soncle style on the page ----------
  const styleEl = Object.assign(document.createElement('style'), { id: 'soncle-companion-theme' });
  let coverHs = null;
  const dark = matchMedia('(prefers-color-scheme: dark)');
  function applyStyle() {
    const mode = resolveMode(settings.mode, dark.matches);
    let [h, s] = accentHs(settings.accent);
    if (settings.dynamic && coverHs) [h, s] = [coverHs[0], Math.max(coverHs[1], 25)];
    for (const [k, v] of Object.entries(themeVars(h, s, mode))) host.style.setProperty(k, v);   // the panel always wears it
    styleEl.textContent = settings.on ? cssFor(h, s, mode) : '';
    if (!styleEl.isConnected) (document.head || document.documentElement).append(styleEl);
  }
  dark.addEventListener?.('change', applyStyle);
  async function coverColour(t) {
    coverHs = null;
    if (t.thumb) {
      try {
        const im = new Image(); im.crossOrigin = 'anonymous'; im.src = t.thumb; await im.decode();
        const c = document.createElement('canvas'); c.width = c.height = 48;
        const x = c.getContext('2d', { willReadFrequently: true }); x.drawImage(im, 0, 0, 48, 48);
        if (track?.key === t.key) coverHs = dominantHue(x.getImageData(0, 0, 48, 48).data);
      } catch { /* cover not readable: keep the accent */ }
    }
    applyStyle();
  }
  // ---------- settings: drawn from features.js, so every Soncle function is listed with where it stands ----------
  const setBox = $('.set', root), openGroups = new Set(['appearance', 'lyrics']);
  let sleepMin = 0, sleepTimer = 0;
  const setPref = async (k, v) => { settings = { ...settings, [k]: v }; await prefs.set(settings); applyStyle(); applyLook(); drawSettings(); };
  function applyLook() {
    linesBox.style.setProperty('--ly-size', settings.lySize + 'px');
    linesBox.style.setProperty('--ly-align', settings.lyAlign);
    linesBox.style.setProperty('--ly-origin', settings.lyAlign === 'center' ? 'center' : 'left');
    linesBox.classList.toggle('glow', !!settings.lyGlow);
  }
  function setSleep(min) {
    clearTimeout(sleepTimer); sleepMin = min;
    if (min) sleepTimer = setTimeout(() => { $('video')?.pause(); sleepMin = 0; drawSettings(); toast('Sleep timer: paused'); }, min * 60000);
    toast(min ? `Sleep timer: pausing in ${min} min` : 'Sleep timer off'); drawSettings();
  }
  async function clearCache() {
    let r = null; try { r = await chrome.runtime.sendMessage({ type: 'clearCache' }); } catch { /* worker asleep */ }
    toast(r ? `Lyrics cache cleared (${r.cleared})` : 'Could not clear the cache');
  }
  const ACTIONS = { card: () => { panel.hidden = false; cardDialog(); }, clearCache, nowplaying: () => np.open(track) };
  function drawSettings() {
    const mk = (tag, props, ...kids) => { const e = Object.assign(document.createElement(tag), props); e.append(...kids.filter(Boolean)); return e; };
    const seg = (cur, opts, pick) => mk('div', { className: 'row' }, ...opts.map(([v, l]) => mk('button', { className: 'chip' + (cur === v ? ' on' : ''), textContent: l, onclick: () => pick(v) })));
    const control = (c) => {
      if (c.type === 'seg') return seg(settings[c.key], c.options, (v) => setPref(c.key, v));
      if (c.type === 'sleep') return seg(sleepMin, c.options, setSleep);
      if (c.type === 'swatches') return mk('div', { className: 'row' }, ...ACCENTS.map((hex) => mk('button', { className: 'sw' + (settings.accent === hex ? ' on' : ''), title: hex, 'aria-label': 'Accent ' + hex, style: `background:${hex}`, onclick: () => setPref('accent', hex) })));
      if (c.type === 'action') return mk('div', { className: 'row' }, mk('button', { className: 'chip', textContent: c.label, onclick: ACTIONS[c.action] }));
      if (c.type === 'range') {
        const out = mk('span', { className: 'val', textContent: settings[c.key] + c.unit });
        const inp = mk('input', { type: 'range', min: c.min, max: c.max, step: c.step, value: settings[c.key], 'aria-label': c.key });
        inp.oninput = () => { settings = { ...settings, [c.key]: Number(inp.value) }; out.textContent = inp.value + c.unit; applyLook(); };
        inp.onchange = () => prefs.set(settings);
        return mk('div', { className: 'row' }, inp, out);
      }
      return null;
    };
    const item = (i) => mk('div', { className: 'item ' + i.status },
      mk('div', { className: 'ih' }, mk('b', { textContent: i.title }), i.status !== 'on' && mk('span', { className: 'tag', textContent: STATUS_LABEL[i.status] })),
      i.desc && mk('div', { className: 'hint', textContent: i.desc }),
      i.why && mk('div', { className: 'hint', textContent: i.why }),
      i.control && control(i.control));
    const group = (g) => {
      const d = mk('details', { open: openGroups.has(g.id) }, mk('summary', { textContent: g.title }), ...g.items.map(item));
      if (g.id === 'about') d.append(mk('div', { className: 'hint', textContent: ABOUT.body }), mk('div', { className: 'hint', textContent: ABOUT.credit }));
      d.ontoggle = () => { if (d.open) openGroups.add(g.id); else openGroups.delete(g.id); };
      return d;
    };
    const keep = setBox.scrollTop;
    setBox.replaceChildren(mk('div', { className: 'banner' }, mk('b', { textContent: ABOUT.lead }), 'Ads stay and artists are paid as usual. Unofficial, and not affiliated with YouTube or Google.'), ...GROUPS.map(group));
    setBox.scrollTop = keep;
  }
  function toggleSettings(force) {
    setBox.hidden = force ?? !setBox.hidden; linesBox.hidden = !setBox.hidden;
    $('.look', root).textContent = setBox.hidden ? 'Settings' : 'Lyrics';
    if (!setBox.hidden) drawSettings(); else follow(true);
  }
  $('.look', root).onclick = () => toggleSettings();
  applyLook(); applyStyle();

  // ---------- Soncle Now Playing (full screen, drives the page's own player) ----------
  const np = createNowPlaying({
    root, linesBox, video: () => $('video'), toast,
    restoreLines: () => { setBox.after(linesBox); linesBox.hidden = !setBox.hidden; follow(true); },
    openSettings: () => { panel.hidden = false; toggleSettings(true); },
    openCard: () => cardDialog(),
    siteClick: (labels) => {   // the site's own tab bar (Home / Explore / Library), found by its label
      const tab = [...document.querySelectorAll('ytmusic-pivot-bar-item-renderer')].find((e) => labels?.includes(e.textContent.trim()));
      if (tab) { tab.click(); return true; } return false;
    }
  });
  $('.full', root).onclick = () => np.toggle(track);

  // ---------- lyrics ----------
  let track = null, lyr = null, lineEls = [], active = -2, userScroll = 0;
  const off = () => (track ? offsets[track.key] || 0 : 0) + (settings.lyDelay || 0) / 1000;   // per-song nudge + the all-songs delay
  async function nudge(d) {
    if (!track) return;
    const v = Math.round(((offsets[track.key] || 0) + d) * 10) / 10;
    if (Math.abs(v) < 0.05) delete offsets[track.key]; else offsets[track.key] = Math.max(-10, Math.min(10, v));
    await store.set(offsets); active = -2; follow(true);
  }
  function show(msg, retry) {
    const box = Object.assign(document.createElement('div'), { className: 'msg', textContent: msg });
    if (retry) box.append(Object.assign(document.createElement('button'), { className: 'chip', textContent: 'Try again', onclick: retry }));
    linesBox.replaceChildren(box); lineEls = [];
  }
  function render() {
    lineEls = [];
    if (!lyr) return show('No lyrics found for this song.');
    if (!lyr.lines?.length) {
      linesBox.classList.add('plain');
      linesBox.replaceChildren(...lyr.plain.split(/\n/).map((x) => Object.assign(document.createElement('div'), { className: 'ln', textContent: x || ' ' })));
      return;
    }
    linesBox.classList.remove('plain');
    lineEls = lyr.lines.map((l, i) => {
      const el = Object.assign(document.createElement('div'), { className: 'ln', textContent: l.text || '♪' });
      el.onclick = () => { const v = $('video'); if (v) v.currentTime = Math.max(0, l.t + off() + 0.01); };
      return el;
    });
    linesBox.replaceChildren(...lineEls);
  }
  async function load(t) {
    track = t; lyr = null; active = -2; coverColour(t); if (np.isOpen) np.update(t); $('.ti', root).textContent = t.title; $('.ar', root).textContent = names(t); $('.src', root).textContent = '';
    show('Looking for lyrics…');
    let res = null;
    try { res = await chrome.runtime.sendMessage({ type: 'lyrics', track: { title: t.title, artists: t.artists, album: t.album, duration: t.duration } }); } catch { /* worker asleep or gone */ }
    if (track?.key !== t.key) return;
    if (res?.found && (res.synced || res.plain)) { lyr = { lines: res.synced ? parseLrc(res.synced) : null, plain: res.plain, source: res.source }; $('.src', root).textContent = 'Lyrics from ' + (res.source || 'LRCLIB') + (res.cached ? ' (saved)' : ''); }
    else if (!res || res.failed) return show('Could not reach the lyrics service. Check your connection.', () => load(t));
    render();
  }
  function follow(force) {
    const v = $('video');
    if (!v || !lyr?.lines?.length || (panel.hidden && !np.isOpen && !force)) return;
    const now = v.currentTime - off();
    let i = -1;
    for (let k = 0; k < lyr.lines.length && lyr.lines[k].t <= now; k++) i = k;
    if (i === active && !force) return;
    active = i;
    lineEls.forEach((el, k) => { el.classList.toggle('on', k === i); el.classList.toggle('past', k < i); });
    if (i >= 0 && settings.lyAuto && Date.now() - userScroll > 4000) lineEls[i].scrollIntoView({ block: 'center', behavior: force ? 'auto' : 'smooth' });
  }
  linesBox.addEventListener('wheel', () => { userScroll = Date.now(); }, { passive: true });
  linesBox.addEventListener('touchmove', () => { userScroll = Date.now(); }, { passive: true });

  // ---------- lyric card ----------
  $('.card', root).onclick = () => cardDialog();
  async function cardDialog() {
    const synced = !!lyr?.lines?.length;
    const rows = synced ? lyr.lines.filter((l) => l.text) : (lyr?.plain || '').split(/\n/).map((x) => x.trim()).filter(Boolean).map((text) => ({ text }));
    if (!track || !rows.length) return;
    const st = { start: Math.max(0, rows.indexOf(lyr.lines?.[Math.max(0, active)])), count: 3, format: 'story', background: 'cover', align: 'center', showTitle: true, mode: 'lines', startAt: 0, len: 30, safe: true, textSize: 'normal', motion: 'pop', startCue: false, syncCues: false };
    const cover = await new Promise((res) => { if (!track.thumb) return res(null); const im = new Image(); im.crossOrigin = 'anonymous'; im.onload = () => res(im); im.onerror = () => res(null); im.src = track.thumb; });
    const canRecord = synced && !!videoType(), MAX_S = 60, MAX_CLIP_S = 360;
    const songLen = Math.round(track.duration || (synced ? (lyr.lines[lyr.lines.length - 1].end || lyr.lines[lyr.lines.length - 1].t + 5) : 0));
    if (synced) st.startAt = Math.max(0, Math.floor(rows[st.start]?.t ?? 0));
    const mmss = (x) => `${Math.floor(x / 60)}:${String(Math.floor(x % 60)).padStart(2, '0')}`;
    const clipEnd = () => Math.min(songLen, st.len ? st.startAt + st.len : songLen);
    const clipRows = [];
    const mctx = document.createElement('canvas').getContext('2d'), pv = document.createElement('canvas'), pctx = pv.getContext('2d');
    let card = null, t0 = performance.now(), busy = null;
    const picked = () => rows.slice(st.start, st.start + st.count);
    const base = () => ({ format: st.format, background: st.background, align: st.align, showTitle: st.showTitle, title: track.title, artist: names(track), cover,
      safe: st.safe, textSize: st.textSize, motion: st.motion, startCue: st.startCue, syncCues: st.syncCues });
    const still = () => createCard({ lines: synced ? picked() : untimed(picked().map((r) => r.text)), still: true, ...base() }, mctx);
    const el = (tag, props = {}, ...kids) => { const e = Object.assign(document.createElement(tag), props); e.append(...kids.filter(Boolean)); return e; };
    const status = el('div', { className: 'st' });
    const saveImg = el('button', { className: 'btn', textContent: 'Save image', onclick: async () => { try { await saveBlob(await renderPng(still()), file('png')); toast('Image saved'); } catch { status.textContent = 'Could not save the image'; } } });
    const saveVid = el('button', { className: 'btn main', textContent: 'Save video', onclick: () => video() });
    const file = (ext) => `${(track.title || 'lyrics').replace(/[^\p{L}\p{N}]+/gu, '-').replace(/^-|-$/g, '').slice(0, 50) || 'lyrics'}-lyrics.${ext}`;
    const rebuild = () => {
      if (busy) return;
      const f = FORMATS[st.format]; pv.width = Math.round(f.w * 0.2); pv.height = Math.round(f.h * 0.2);
      const clip = synced && st.mode === 'clip';
      clipRows.forEach((e) => { e.hidden = !clip; });
      safeRow.hidden = !f.safe;
      card = !synced ? still() : clip ? createCard({ lines: clipLines(lyr.lines, st.startAt, st.len), startAt: st.startAt, endAt: clipEnd(), ...base() }, mctx) : createCard({ lines: picked(), leadIn: st.syncCues ? 3.5 : 0.5, ...base() }, mctx);
      const from = mmss(Math.max(0, card.timeline?.origin ?? st.startAt));
      const cues = st.syncCues ? ' The countdown and timecode help you line the song up by hand in a video editor; the video itself has no sound.' : '';
      t0 = performance.now();
      const long = synced && card.duration > (clip ? MAX_CLIP_S : MAX_S);
      saveVid.hidden = !canRecord; saveVid.textContent = canShareFiles() ? 'Share video' : 'Save video'; saveVid.disabled = long || !card.duration;
      status.textContent = !synced ? 'These lyrics have no timing, so only the image is available.' : !canRecord ? 'This browser cannot record video; the image still works.'
        : long ? `That is ${Math.round(card.duration)} s: ${clip ? `pick a shorter length (up to ${MAX_CLIP_S / 60} min)` : `pick fewer lines (up to ${MAX_S} s)`}.`
        : !card.duration ? 'There are no lyrics in that part of the song.'
        : clip ? `Video: ${mmss(card.duration)}, silent, from ${mmss(st.startAt)} of the song. It records in real time, so keep this tab open. In the music picker of Instagram, TikTok or YouTube Shorts, start the song at ${from} so the words match; the artist is paid through the platform.${cues}${videoType().includes('mp4') ? '' : ' This browser saves WebM; some apps want MP4.'}`
        : `Video: ${card.duration.toFixed(1)} s, silent. Add the song in the platform’s music picker and start it at ${from} so the words match; the artist is still paid.${cues}`;
    };
    async function video() {
      if (busy) { busy.abort(); return; }
      busy = new AbortController(); saveImg.disabled = true; saveVid.textContent = 'Cancel';
      try { const b = await recordVideo(card, { signal: busy.signal, onProgress: (p) => { status.textContent = `Recording ${Math.round(p * 100)}%…`; } }); await saveBlob(b, file(b.type.includes('mp4') ? 'mp4' : 'webm')); toast('Video saved'); }
      catch (e) { if (e.message !== 'Cancelled') status.textContent = 'Could not make the video'; }
      busy = null; saveImg.disabled = false; rebuild();
    }
    const chips = (key, options) => { const box = el('div', { className: 'row' }); const draw = () => box.replaceChildren(...options.map(([v, l]) => el('button', { className: 'chip' + (st[key] === v ? ' on' : ''), textContent: l, onclick: () => { if (busy) return; st[key] = v; draw(); rebuild(); } }))); draw(); return box; };
    const safeRow = el('div', { className: 'row', title: 'Keeps the words clear of the Instagram Reels buttons and caption' }, el('label', { textContent: 'Reels safe area' }), chips('safe', [[true, 'On'], [false, 'Off']]));
    const startOut = el('span', { textContent: mmss(st.startAt) });
    const startRange = el('input', { type: 'range', min: 0, max: Math.max(1, songLen - 1), step: 1, value: st.startAt, 'aria-label': 'Start of the video in the song', oninput: () => { st.startAt = Number(startRange.value); startOut.textContent = mmss(st.startAt); }, onchange: () => rebuild() });
    startRange.style.flex = '1';
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
        synced && el('div', { className: 'row' }, el('label', { textContent: 'Video from' }), chips('mode', [['lines', 'Chosen lines'], ['clip', 'Part of the song']])),
        synced && clipRows[clipRows.push(el('div', { className: 'row' }, el('label', { textContent: 'Starts at' }), startRange, startOut)) - 1],
        synced && clipRows[clipRows.push(el('div', { className: 'row' }, el('label', { textContent: 'Length' }), chips('len', [[15, '15 s'], [30, '30 s'], [60, '60 s'], [0, 'Whole song']]))) - 1],
        el('div', { className: 'row' }, el('label', { textContent: 'Size' }), chips('format', [['story', '9:16'], ['portrait', '4:5'], ['square', '1:1']])),
        safeRow,
        el('div', { className: 'row' }, el('label', { textContent: 'Background' }), chips('background', BACKGROUNDS)),
        el('div', { className: 'row' }, el('label', { textContent: 'Text' }), chips('align', [['center', 'Centre'], ['left', 'Left']])),
        el('div', { className: 'row' }, el('label', { textContent: 'Text size' }), chips('textSize', TEXT_SIZES)),
        synced && el('div', { className: 'row' }, el('label', { textContent: 'Animation' }), chips('motion', MOTIONS)),
        el('div', { className: 'row' }, el('label', { textContent: 'Song name' }), chips('showTitle', [[true, 'Show'], [false, 'Hide']])),
        synced && el('div', { className: 'row', title: 'Shows “Start the song at m:ss” for the first seconds of the video' }, el('label', { textContent: 'Start cue' }), chips('startCue', [[true, 'Show'], [false, 'Hide']])),
        synced && el('div', { className: 'row', title: 'A 3-2-1 countdown, a pulse on each line and the song time, for lining the song up by hand in a video editor' }, el('label', { textContent: 'Sync cues' }), chips('syncCues', [[true, 'Show'], [false, 'Hide']])))),
      status, el('div', { className: 'act' }, el('button', { className: 'btn text', textContent: 'Close', onclick: close }), saveImg, saveVid)));
    modal.hidden = false;
    modal.onmousedown = (e) => { if (e.target === modal) close(); };
    const loop = () => { if (modal.hidden) return; if (card && !busy) card.render(pctx, synced ? ((performance.now() - t0) / 1000) % (card.duration + 1.2) : 0, 0.2); requestAnimationFrame(loop); };
    requestAnimationFrame(loop);
  }

  // ---------- keyboard ----------
  window.addEventListener('keydown', (e) => {
    if (!settings.keys) return;
    const el = e.composedPath()[0];
    if (el?.isContentEditable || /^(input|textarea|select)$/i.test(el?.tagName || '')) return;
    if (e.key === 'Escape') { if (!modal.hidden) { modal.hidden = true; modal.replaceChildren(); } else if (np.isOpen) np.close(); else if (!panel.hidden) panel.hidden = true; return; }
    if (!e.altKey || e.ctrlKey || e.metaKey) return;
    const step = e.shiftKey ? 0.5 : 0.1;
    if (e.code === 'KeyL') { fab.click(); e.preventDefault(); }
    else if (e.code === 'KeyN') { np.toggle(track); e.preventDefault(); }
    else if (e.code === 'BracketLeft') { nudge(-step); toast(`Lyrics ${step} s earlier`); e.preventDefault(); }
    else if (e.code === 'BracketRight') { nudge(step); toast(`Lyrics ${step} s later`); e.preventDefault(); }
  });

  // ---------- main loop: react to the page's changes instead of polling ----------
  let mo = null, moBar = null, pending = 0;
  function check() {
    pending = 0;
    const t = readTrack();
    if (t && t.key !== track?.key) load(t); else if (t && track && !track.duration && t.duration) track.duration = t.duration;
  }
  function watch() {
    const bar = $('ytmusic-player-bar');
    if (bar === moBar) return;
    mo?.disconnect(); moBar = bar;
    if (!bar) return;
    mo = new window.MutationObserver(() => { if (!pending) pending = setTimeout(check, 150); });
    mo.observe(bar, { subtree: true, childList: true, characterData: true, attributes: true, attributeFilter: ['src', 'title'] });
    check();
  }
  document.addEventListener('loadedmetadata', () => { if (!pending) pending = setTimeout(check, 150); }, true);   // the song's length is known
  watch(); setInterval(watch, 4000);   // only re-attaches if the page replaces its player bar
  (function tick() { follow(false); requestAnimationFrame(tick); })();
})();
