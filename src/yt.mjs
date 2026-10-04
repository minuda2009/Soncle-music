// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// YouTube Music service: wraps youtubei.js and turns its node tree into plain JSON the UI can render.
import { Innertube, UniversalCache, Platform, Log, YTMusic, Parser } from 'youtubei.js';

Platform.shim.eval = async (data, env) => {
  const code = `${data.output}\nreturn process(__env.n, __env.sp, __env.sig);`;
  return new Function('__env', code)(env);
};
Log.setLevel(Log.Level.NONE);
// youtubei.js's default parse-error handler reads package.json "bugs.url", which the packaged app
// doesn't ship — so any harmless parser warning became a crash ("reading 'url'") that broke search,
// pages and the TV stream client. Unknown/renamed YouTube nodes are simply skipped instead.
Parser.setParserErrorHandler(() => {});

let cacheDir = null;
let cookie = '';
let prefs = { lang: 'en', location: 'US' };
let ytPromise = null;
let anonPromise = null;

export function configure({ cache, cookie: c, lang, location }) {
  if (cache) cacheDir = cache;
  if (c !== undefined) cookie = c || '';
  if (lang) prefs.lang = lang;
  if (location) prefs.location = location;
  ytPromise = null;
  anonPromise = null;
}

function create(withCookie) {
  return Innertube.create({
    lang: prefs.lang,
    location: prefs.location,
    cookie: withCookie && cookie ? cookie : undefined,
    retrieve_player: true,
    // real visitor data is needed so PO tokens bind to this session
    generate_session_locally: false,
    cache: cacheDir ? new UniversalCache(true, cacheDir) : undefined
  });
}

function yt() {
  if (!ytPromise) ytPromise = create(true).catch((e) => { ytPromise = null; throw e; });
  return ytPromise;
}
function ytAnon() {
  if (!cookie) return yt();
  if (!anonPromise) anonPromise = create(false).catch((e) => { anonPromise = null; throw e; });
  return anonPromise;
}

export const isSignedIn = () => !!cookie;

// Best-effort "is this account YouTube Premium?" — used only to skip the ads line of the
// first-run notice for paying users. YouTube exposes no Premium flag to clients, so we look for
// membership wording in the signed-in account menu. Returns true / false / null (unknown); any
// failure or ambiguity returns null so the notice stays on the safe (full) wording.
export async function premiumStatus() {
  if (!cookie) return false;
  try {
    const y = await yt();
    const r = await y.actions.execute('/account/account_menu', { client: 'YTMUSIC', parse: false });
    const text = JSON.stringify(r?.data ?? r ?? '').toLowerCase();
    if (/get (youtube |music )?premium|try (youtube |music )?premium|upgrade to premium|go premium/.test(text)) return false;
    if (/your premium benefits|premium membership|youtube premium member|music premium member|manage (your )?(membership|premium)/.test(text)) return true;
    return null;
  } catch { return null; }
}

// ---------- helpers ----------
const txt = (t) => {
  if (t == null) return '';
  if (typeof t === 'string') return t;
  if (Array.isArray(t.runs)) return t.runs.map((r) => r?.text ?? '').join('');
  if (typeof t.toString === 'function' && t.toString !== Object.prototype.toString) return t.toString();
  return t.text || '';
};

function pickThumb(list) {
  if (!list) return '';
  if (!Array.isArray(list)) list = list.contents || list.thumbnails || [];
  list = list.filter((x) => x && x.url);
  if (!list.length) return '';
  const best = [...list].sort((a, b) => (b.width || 0) - (a.width || 0))[0];
  return fixThumb(best.url);
}

export function fixThumb(url) {
  if (!url) return '';
  if (url.startsWith('//')) url = 'https:' + url;
  if (/googleusercontent\.com|ggpht\.com/.test(url)) return url.replace(/=w\d+-h\d+.*$/, '=w544-h544-l90-rj').replace(/=s\d+.*$/, '=s544');
  return url;
}

function parseDuration(s) {
  if (!s) return 0;
  const p = String(s).split(':').map(Number);
  if (p.some(isNaN)) return 0;
  return p.reduce((a, v) => a * 60 + v, 0);
}

const pageType = (ep) => ep?.payload?.browseEndpointContextSupportedConfigs?.browseEndpointContextMusicConfig?.pageType || '';

// A text run may carry its navigation endpoint at .endpoint or .navigationEndpoint.
const runEndpoint = (r) => r?.endpoint || r?.navigationEndpoint;

function artistsFromRuns(runs) {
  return (runs || [])
    .filter((r) => {
      const ep = runEndpoint(r);
      return ep?.payload?.browseId?.startsWith('UC') || pageType(ep) === 'MUSIC_PAGE_TYPE_ARTIST' || pageType(ep) === 'MUSIC_PAGE_TYPE_USER_CHANNEL';
    })
    .map((r) => ({ name: r.text, id: runEndpoint(r)?.payload?.browseId }));
}

function isExplicit(node) {
  const b = node?.badges || node?.subtitle_badges;
  try {
    return !!(b && [...b].some((x) => x?.icon_type === 'MUSIC_EXPLICIT_BADGE' || /explicit/i.test(x?.label || '')));
  } catch { return false; }
}

function kindFromPage(pt) {
  switch (pt) {
    case 'MUSIC_PAGE_TYPE_ALBUM':
    case 'MUSIC_PAGE_TYPE_AUDIOBOOK': return 'album';
    case 'MUSIC_PAGE_TYPE_PLAYLIST': return 'playlist';
    case 'MUSIC_PAGE_TYPE_ARTIST':
    case 'MUSIC_PAGE_TYPE_USER_CHANNEL':
    case 'MUSIC_PAGE_TYPE_LIBRARY_ARTIST': return 'artist';
    case 'MUSIC_PAGE_TYPE_PODCAST_SHOW_DETAIL_PAGE': return 'playlist';
    default: return '';
  }
}

// ---------- item normalization ----------
function normTwoRow(n) {
  const ep = n.endpoint;
  const p = ep?.payload || {};
  const title = txt(n.title);
  const subtitle = txt(n.subtitle);
  const thumb = pickThumb(n.thumbnail);
  const pk = kindFromPage(pageType(ep));
  if (pk) {
    let id = p.browseId || p.playlistId;
    return { type: pk, id, title, subtitle, thumb, artists: artistsFromRuns(n.subtitle?.runs), year: n.year, explicit: isExplicit(n) };
  }
  if (p.videoId) {
    const artists = artistsFromRuns(n.subtitle?.runs);
    // Two-row song cards do carry a duration badge; parse it when present.
    let duration = n.duration?.seconds || 0;
    if (!duration) {
      const fixed = txt(n.fixed_columns?.[0]?.title) || (subtitle.match(/(\d+:)?\d+:\d\d/) || [])[0];
      duration = parseDuration(fixed);
    }
    return {
      type: 'song', id: p.videoId, title, subtitle, thumb,
      artists: artists.length ? artists : [{ name: subtitle.split(' • ').find((s) => !/^(Song|Video|Episode)$/i.test(s)) || subtitle }],
      duration, explicit: isExplicit(n), isVideo: n.item_type === 'video'
    };
  }
  if (p.playlistId) return { type: 'radio', id: p.playlistId, params: p.params, title, subtitle, thumb };
  if (p.browseId) return { type: 'browse', id: p.browseId, params: p.params, title, subtitle, thumb };
  return null;
}

function normResponsive(n) {
  const cols = n.flex_columns || [];
  const col = (i) => cols[i]?.title;
  const title = n.title || n.name || txt(col(0));
  const thumb = pickThumb(n.thumbnail?.contents || n.thumbnails);
  const secondRuns = col(1)?.runs || [];
  const subtitle = [1, 2, 3].map((i) => txt(col(i))).filter(Boolean).join(' • ');
  const ep = n.endpoint;
  const pk = kindFromPage(pageType(ep));
  const type = n.item_type;

  if (type === 'artist' || type === 'library_artist' || pk === 'artist') {
    // Search returns an "artist" row whose subtitle is the monthly audience, which
    // is not a subscriber count. Only label it as subscribers when the string says so.
    let sub = subtitle;
    if (n.subscribers) sub = /subscriber/i.test(String(n.subscribers)) ? n.subscribers : `${n.subscribers} subscribers`;
    return { type: 'artist', id: n.id || ep?.payload?.browseId, title, subtitle: sub, thumb };
  }
  if (type === 'album' || pk === 'album') {
    return { type: 'album', id: n.id || ep?.payload?.browseId, title, subtitle, thumb, year: n.year, artists: (n.artists || n.author ? [].concat(n.artists || n.author) : []).map((a) => ({ name: a.name, id: a.channel_id })), explicit: isExplicit(n) };
  }
  if (type === 'playlist' || type === 'podcast_show' || pk === 'playlist') {
    return { type: 'playlist', id: n.id || ep?.payload?.browseId, title, subtitle, thumb };
  }
  // songs / videos / episodes
  let id = n.id || n.overlay?.content?.endpoint?.payload?.videoId || ep?.payload?.videoId;
  if (!id) {
    for (const c of cols) for (const r of c?.title?.runs || []) if (r.endpoint?.payload?.videoId) { id = r.endpoint.payload.videoId; break; }
  }
  if (!id) return null;
  let artists = (n.artists || n.authors || (n.author ? [n.author] : []))
    .map((a) => ({ name: a.name, id: a.channel_id }));
  if (!artists.length) artists = artistsFromRuns(secondRuns);
  if (!artists.length && secondRuns.length) {
    const first = secondRuns.find((r) => !/^(Song|Video|Episode|\s*•\s*)$/.test(r.text));
    if (first) artists = [{ name: first.text }];
  }
  let album = n.album ? { name: n.album.name, id: n.album.id } : null;
  if (!album) {
    for (const c of cols) for (const r of c?.title?.runs || []) {
      const ep = runEndpoint(r);
      if (pageType(ep) === 'MUSIC_PAGE_TYPE_ALBUM') album = { name: r.text, id: ep.payload.browseId };
    }
  }
  let duration = n.duration?.seconds || 0;
  if (!duration) {
    const fixed = txt(n.fixed_columns?.[0]?.title);
    duration = parseDuration(fixed);
    if (!duration) {
      const m = subtitle.match(/(\d+:)?\d+:\d\d/);
      if (m) duration = parseDuration(m[0]);
    }
  }
  return {
    type: 'song', id, title, thumb, artists, album, duration,
    subtitle, explicit: isExplicit(n), isVideo: type === 'video' || type === 'non_music_track',
    plays: n.views || '', setVideoId: undefined
  };
}

function normMultiRow(n) {
  const p = n.on_tap?.payload || {};
  if (!p.videoId) return null;
  return { type: 'song', id: p.videoId, title: txt(n.title), subtitle: txt(n.subtitle), thumb: pickThumb(n.thumbnail?.contents), artists: [{ name: txt(n.subtitle) }], duration: 0, isVideo: true };
}

function normNavButton(n) {
  const ep = n.endpoint;
  const p = ep?.payload || {};
  return { type: 'mood', id: p.browseId, params: p.params, title: txt(n.button_text), color: n.color ? '#' + (n.color >>> 0).toString(16).padStart(8, '0').slice(2) : null };
}

export function normItem(n) {
  if (!n) return null;
  try {
    switch (n.type) {
      case 'MusicTwoRowItem': return normTwoRow(n);
      case 'MusicResponsiveListItem': return normResponsive(n);
      case 'MusicMultiRowListItem': return normMultiRow(n);
      case 'MusicNavigationButton': return normNavButton(n);
      case 'PlaylistPanelVideo': return normPanel(n);
      default: return null;
    }
  } catch {
    return null; // an unfamiliar renderer shape is skipped, not fatal
  }
}

function normPanel(n) {
  if (n.type === 'PlaylistPanelVideoWrapper') n = n.primary;
  if (!n || !n.video_id) return null;
  const artists = n.artists?.length ? n.artists.map((a) => ({ name: a.name, id: a.channel_id })) : [{ name: n.author }];
  return {
    type: 'song', id: n.video_id, title: txt(n.title), thumb: pickThumb(n.thumbnail), artists,
    album: n.album ? { name: n.album.name, id: n.album.id } : null,
    duration: n.duration?.seconds || 0, explicit: isExplicit(n)
  };
}

const safeNorm = (x) => { try { return normItem(x); } catch { return null; } };
const items = (arr) => (arr ? [...arr].map(safeNorm).filter(Boolean) : []);

function normShelf(s) {
  if (!s) return null;
  if (s.type === 'MusicCarouselShelf') {
    const h = s.header;
    const list = items(s.contents);
    if (!list.length) return null;
    const more = h?.more_content?.endpoint?.payload;
    return {
      title: txt(h?.title), strapline: txt(h?.strapline), thumb: pickThumb(h?.thumbnail?.contents),
      layout: list.every((i) => i.type === 'song') && s.contents?.[0]?.type !== 'MusicTwoRowItem' ? 'grid-songs' : list.every((i) => i.type === 'mood') ? 'moods' : 'carousel',
      items: list,
      more: more?.browseId ? { id: more.browseId, params: more.params } : null
    };
  }
  if (s.type === 'MusicShelf') {
    const list = items(s.contents);
    if (!list.length) return null;
    const more = s.bottom_button?.endpoint?.payload || s.endpoint?.payload;
    return { title: txt(s.title), layout: 'list', items: list, more: more?.browseId ? { id: more.browseId, params: more.params } : more?.query ? { query: more.query, params: more.params } : null };
  }
  if (s.type === 'MusicImmersiveCarouselShelf') {
    const list = items(s.contents);
    return list.length ? { title: txt(s.header?.title), layout: 'carousel', items: list } : null;
  }
  if (s.type === 'Grid' || s.type === 'GridRenderer') {
    const list = items(s.items || s.contents);
    const isMood = list.length > 0 && list.every((i) => i.type === 'mood');
    return list.length ? { title: txt(s.header?.title), layout: isMood ? 'moods' : 'grid', items: list } : null;
  }
  if (s.type === 'MusicPlaylistShelf') {
    const list = items(s.contents);
    return list.length ? { title: '', layout: 'list', items: list } : null;
  }
  if (s.type === 'MusicDescriptionShelf') {
    return { title: txt(s.header?.title) || 'About', layout: 'text', text: txt(s.description) };
  }
  if (s.type === 'ItemSection') {
    // Search wraps its result rows directly in an ItemSection, sometimes with a
    // single MusicShelf inside. Handle both, otherwise every result row is lost.
    const inner = [...(s.contents || [])];
    const nested = inner.find((c) => SHELF_TYPES.has(c?.type));
    if (nested) return normShelf(nested);
    const list = items(inner);
    if (list.length) return { title: txt(s.header?.title), layout: 'list', items: list };
    return null;
  }
  if (s.type === 'SectionList') {
    const list = items(s.contents);
    return list.length ? { title: txt(s.header?.title), layout: 'list', items: list } : null;
  }
  return null;
}

const SHELF_TYPES = new Set(['MusicCarouselShelf', 'MusicShelf', 'Grid', 'MusicPlaylistShelf', 'MusicImmersiveCarouselShelf', 'MusicDescriptionShelf']);
function collectShelves(node, out, depth = 0) {
  if (!node || depth > 14) return;
  if (typeof node.item === 'function' && typeof node.array === 'function') {
    try { node = node.is_array ? node.array() : node.item(); } catch { return; }
    if (!node) return;
  }
  if (Array.isArray(node) || (typeof node[Symbol.iterator] === 'function' && typeof node !== 'string')) { for (const n of node) collectShelves(n, out, depth + 1); return; }
  if (typeof node !== 'object') return;
  if (SHELF_TYPES.has(node.type)) { let sh = null; try { sh = normShelf(node); } catch (e) { ylog('shelf skipped: ' + e.message); } if (sh) out.push(sh); return; }
  for (const k of ['contents', 'content', 'tabs', 'sections', 'items', 'secondary_contents']) if (node[k]) collectShelves(node[k], out, depth + 1);
}

const shelves = (arr) => { const out = []; if (arr) collectShelves(arr, out); return out; };

// ---------- API ----------
let homeState = null;

export async function home(chip) {
  const y = await yt();
  let feed = await y.music.getHomeFeed();
  if (chip) feed = await feed.applyFilter(chip);
  homeState = feed;
  return {
    chips: feed.filters || [],
    sections: shelves(feed.sections),
    hasMore: feed.has_continuation
  };
}

export async function homeMore() {
  if (!homeState?.has_continuation) return { sections: [], hasMore: false };
  try {
    homeState = await homeState.getContinuation();
  } catch {
    // YouTube ends the feed with an empty continuation ("Continuation did not have any content")
    homeState = null;
    return { sections: [], hasMore: false };
  }
  return { sections: shelves(homeState.sections), hasMore: !!homeState.has_continuation };
}

export async function explore() {
  const y = await yt();
  const ex = await y.music.getExplore();
  return {
    buttons: (ex.top_buttons || []).map(normNavButton).filter((b) => b.id),
    sections: shelves(ex.sections)
  };
}

export async function browse(browseId, params) {
  const y = await yt();
  const res = await y.actions.execute('/browse', { browseId, params, client: 'YTMUSIC', parse: true });
  let header = null; try { header = res.header?.item?.() || res.header; } catch {}
  let title = txt(header?.title);
  if (!title) { const hd = res.header_memo || res.contents_memo; for (const t of ['MusicHeader', 'MusicImmersiveHeader', 'MusicVisualHeader', 'MusicResponsiveHeader', 'MusicDetailHeader']) { const n = hd?.get(t)?.[0]; if (n) { title = txt(n.title); break; } } }
  const out = [];
  collectShelves(res.contents, out);
  return { title, sections: out };
}

export async function suggestions(q) {
  if (!q) return { queries: [], items: [] };
  const y = await yt();
  const secs = await y.music.getSearchSuggestions(q);
  const queries = [];
  const its = [];
  for (const s of secs || []) {
    for (const c of s.contents || []) {
      if (c.type === 'SearchSuggestion') queries.push(txt(c.suggestion));
      else { const it = normItem(c); if (it) its.push(it); }
    }
  }
  return { queries, items: its.slice(0, 5) };
}

// type:'song' and type:'album' are rejected by the YouTube Music API on some
// responses, so fall back to 'all' (the renderer filters client-side).
const SAFE_SEARCH_TYPES = new Set(['all', 'song', 'video', 'album', 'artist', 'playlist']);
const searchMore_State = new Map();

export async function search(q, type = 'all') {
  const y = await yt();
  if (!SAFE_SEARCH_TYPES.has(type)) type = 'all';
  let s;
  try {
    s = await y.music.search(q, { type });
  } catch (e) {
    if (type !== 'all') { s = await y.music.search(q, { type: 'all' }); type = 'all'; } else throw e;
  }
  const out = { query: q, type, top: null, sections: [], hasMore: false, correction: null };
  try { out.correction = txt(s.did_you_mean?.corrected_query) || null; } catch {}
  for (const c of s.contents || []) {
    if (c.type === 'MusicCardShelf') {
      const ep = c.on_tap || runEndpoint(c.title) || runEndpoint(c.title?.runs?.[0]);
      const p = ep?.payload || {};
      const pk = kindFromPage(pageType(ep));
      let top = null;
      if (p.videoId) top = { type: 'song', id: p.videoId, title: txt(c.title), subtitle: txt(c.subtitle), thumb: pickThumb(c.thumbnail?.contents), artists: artistsFromRuns(c.subtitle?.runs), duration: parseDuration((txt(c.subtitle).match(/\d+:\d\d(:\d\d)?/) || [])[0]) };
      else if (pk) top = { type: pk, id: p.browseId, title: txt(c.title), subtitle: txt(c.subtitle), thumb: pickThumb(c.thumbnail?.contents) };
      else if (p.playlistId) top = { type: 'playlist', id: p.playlistId, title: txt(c.title), subtitle: txt(c.subtitle), thumb: pickThumb(c.thumbnail?.contents) };
      if (top) { top.related = items(c.contents); out.top = top; }
    } else {
      const sh = normShelf(c);
      if (sh) out.sections.push(sh);
    }
  }
  // Last-ditch: deep-scan for shelves if the top-level walk found nothing.
  if (!out.sections.length && !out.top) {
    const deep = [];
    collectShelves(s.contents, deep);
    out.sections = deep;
  }
  // Some filters (song/album) come back as an empty Message section instead of
  // throwing. Retry with 'all' so the renderer can filter client-side.
  if (type !== 'all' && !out.sections.length && !out.top) {
    const fallback = await search(q, 'all');
    const want = type === 'song' ? 'song' : type === 'album' ? 'album' : null;
    let secs = fallback.sections || [];
    if (want) {
      const kept = secs.flatMap((s) => (s.items || []).filter((i) => i.type === want && !(want === 'song' && i.isVideo)));
      secs = [{ title: '', layout: 'list', items: kept }];
    }
    return { ...fallback, type: want || 'all', requestedType: type, fallback: true, sections: secs };
  }
  // The API returns one ItemSection per result row; merge untitled rows so the UI
  // renders a single list instead of dozens of one-item sections.
  out.sections = mergeSections(out.sections);
  if (type !== 'all') {
    if (searchMore_State.size > 20) searchMore_State.delete(searchMore_State.keys().next().value);
    searchMore_State.set(q + '|' + type, s);
    out.hasMore = !!s.has_continuation;
  }
  return out;
}

function mergeSections(sections) {
  const out = [];
  const untitled = [];
  for (const s of sections) {
    if (s.layout === 'list' && !s.title) untitled.push(...(s.items || []));
    else out.push(s);
  }
  if (untitled.length) out.push({ title: '', layout: 'list', items: untitled });
  return out;
}

// Kept for the ItemsList-shaped continuation payloads.
function continuationItems(s) {
  const out = [];
  collectShelves(s.contents, out);
  return out;
}

export async function searchMore(q, type) {
  const key = q + '|' + type;
  const s = searchMore_State.get(key);
  if (!s?.has_continuation) return { items: [], hasMore: false };
  const next = await s.getContinuation();
  searchMore_State.set(key, next);
  const secs = continuationItems(next);
  return { items: secs.flatMap((x) => x.items || []), sections: secs, hasMore: next.has_continuation };
}

function headerInfo(h) {
  if (!h) return {};
  const info = { title: txt(h.title), subtitle: txt(h.subtitle), second: txt(h.second_subtitle), thumb: pickThumb(h.thumbnail?.contents || h.thumbnails || h.thumbnail) };
  const strap = h.strapline_text_one;
  if (strap?.runs) {
    info.artists = artistsFromRuns(strap.runs);
    if (!info.artists.length) info.artists = [{ name: txt(strap) }];
  } else if (h.author) info.artists = [{ name: h.author.name, id: h.author.channel_id }];
  const d = h.description;
  info.description = d ? txt(d.description ?? d) : '';
  return info;
}

export async function album(id) {
  const y = await yt();
  // Album ids come as MPREb_*; youtubei.js expects the raw MPR* browse id.
  let a;
  try { a = await y.music.getAlbum(id); }
  catch { /* fall through to raw browse */ }
  let info = {}, tracks = [], sections = [], playlistId = null;
  if (a) {
    info = headerInfo(a.header);
    tracks = items(a.contents);
    sections = shelves(a.sections);
    try { playlistId = a.page?.microformat?.url_canonical?.match(/list=([\w-]+)/)?.[1] || null; } catch {}
  }
  if (!tracks.length && !info.title) {
    const res = await y.actions.execute('/browse', { browseId: id, client: 'YTMUSIC', parse: true });
    const out = [];
    collectShelves(res.contents, out);
    tracks = out.flatMap((s) => s.items || []);
    sections = [];
    let hdr = null;
    try { hdr = res.header?.item?.() || res.header; } catch {}
    if (!hdr) { const hm = res.header_memo || res.contents_memo; for (const t of ['MusicDetailHeader', 'MusicResponsiveHeader', 'MusicImmersiveHeader', 'MusicVisualHeader', 'MusicHeader']) { const n = hm?.get(t)?.[0]; if (n) { hdr = n; break; } } }
    if (hdr) info = headerInfo(hdr);
    try { playlistId = res.microformat?.url_canonical?.match(/list=([\w-]+)/)?.[1] || null; } catch {}
  }
  tracks = tracks.map((t) => ({
    ...t,
    thumb: t.thumb || info.thumb,
    artists: t.artists?.length && t.artists[0].name ? t.artists : info.artists || [],
    album: { name: info.title, id }
  }));
  if (!tracks.length) throw new Error('Album has no tracks or could not be loaded');
  return { type: 'album', id, playlistId, ...info, tracks, sections };
}

export async function artist(id) {
  const y = await yt();
  let a, rawHeader = null;
  try {
    const res = await y.actions.execute('/browse', { browseId: id, client: 'YTMUSIC' });
    rawHeader = res.data?.header?.musicImmersiveHeaderRenderer || res.data?.header?.musicVisualHeaderRenderer || null;
    a = new YTMusic.Artist(res, y.actions);
  } catch {
    a = await y.music.getArtist(id);
  }
  const h = a.header;
  const rawSubs = rawHeader?.subscriptionButton?.subscribeButtonRenderer?.subscriberCountText?.runs?.map((r) => r.text).join('') || '';
  const rawListeners = rawHeader?.monthlyListenerCount?.runs?.map((r) => r.text).join('') || '';
  const info = {
    title: txt(h?.title),
    description: txt(h?.description),
    thumb: pickThumb(h?.thumbnail?.contents || h?.thumbnail || h?.foreground_thumbnail),
    listeners: rawListeners,
    subscribers: rawSubs || (() => {
      const sb = h?.subscription_button;
      const raw = txt(sb?.subscriber_count) || txt(sb?.subscriber_count_text) || txt(sb?.unsubscribed_text) || txt(sb?.subscribed_text) || '';
      return (raw.match(/[\d.,]+\s*[KMB]?/i) || [''])[0].trim();
    })(),
    radioId: h?.start_radio_button?.endpoint?.payload?.playlistId || null,
    shuffleId: h?.play_button?.endpoint?.payload?.playlistId || null
  };
  const sections = shelves(a.sections);
  // Prefer explicitly-labelled top songs/albums so the renderer can find them.
  const songList = sections.find((s) => s.items?.length && s.items.every((i) => i.type === 'song'));
  const albumGrid = sections.find((s) => s.items?.length && s.items.every((i) => i.type === 'album'));
  info.topSongsId = songList?.more?.id || null;
  info.albumsId = albumGrid?.more?.id || null;
  return { type: 'artist', id, ...info, sections };
}

export async function playlist(id, all = false) {
  const y = await yt();
  const bid = id.startsWith('VL') ? id.slice(2) : id;
  let p = await y.music.getPlaylist(bid);
  const info = headerInfo(p.header);
  let tracks = items(p.items || p.contents);
  let guard = 0;
  while (all && p.has_continuation && guard++ < 25 && tracks.length < 2000) {
    try {
      p = await p.getContinuation();
      tracks = tracks.concat(items(p.items || p.contents));
    } catch { break; }
  }
  return { type: 'playlist', id, ...info, tracks, hasMore: !all && p.has_continuation };
}

export async function upNext(videoId, playlistId) {
  const y = await yt();
  let panel;
  if (playlistId) {
    const res = await y.actions.execute('/next', { videoId, playlistId, client: 'YTMUSIC', parse: true, isAudioOnly: true });
    panel = res.contents_memo?.get('PlaylistPanel')?.[0];
  } else {
    panel = await y.music.getUpNext(videoId, true);
  }
  const list = [...(panel?.contents || [])].map((n) => normPanel(n.type === 'PlaylistPanelVideoWrapper' ? n.primary : n)).filter(Boolean);
  return list;
}

export async function radio(playlistId, params) {
  const y = await yt();
  const res = await y.actions.execute('/next', { playlistId, params, client: 'YTMUSIC', parse: true, isAudioOnly: true });
  const panel = res.contents_memo?.get('PlaylistPanel')?.[0];
  return [...(panel?.contents || [])].map((n) => normPanel(n.type === 'PlaylistPanelVideoWrapper' ? n.primary : n)).filter(Boolean);
}

export async function related(videoId) {
  const y = await yt();
  const r = await y.music.getRelated(videoId);
  return shelves(r?.contents || []);
}

export async function ytLyrics(videoId) {
  const y = await yt();
  const l = await y.music.getLyrics(videoId);
  if (!l) return null;
  return { plain: txt(l.description), source: txt(l.footer) };
}

export async function library() {
  if (!cookie) return null;
  const y = await yt();
  const lib = await y.music.getLibrary();
  const out = [];
  for (const c of lib.contents || []) {
    const sh = normShelf(c);
    if (sh) out.push(sh);
  }
  return { sections: out };
}

// ---------- streaming ----------
// Order matters: clients whose googlevideo URLs cap byte ranges are tried last.
// ANDROID_VR in particular refuses any request whose offset/end crosses ~1 MB,
// which is what truncated songs mid-playback. IOS serves the full file and
// arbitrary offsets, so it leads.
// Stream clients in order. The first uses YouTube Music's web client with BotGuard PO tokens,
// the same approach as the upstream Android app (see THIRD_PARTY_NOTICES.md); the rest need no tokens and act as fallbacks.
const STREAM_SPECS = [
  { client: 'YTMUSIC', pot: true },
  { client: 'IOS' },
  { client: 'ANDROID_VR' },
  { client: 'WEB', pot: true },
  { client: 'TV' },
  { client: 'WEB_EMBEDDED' }
];
const CLIENTS = STREAM_SPECS.map((x) => x.client);
// The Android app has no BotGuard window for PO tokens, so it keeps only the clients that need none.
export function setStreamClients(names) {
  const keep = STREAM_SPECS.filter((x) => names.includes(x.client)).sort((a, b) => names.indexOf(a.client) - names.indexOf(b.client));
  if (!keep.length) return;
  STREAM_SPECS.splice(0, STREAM_SPECS.length, ...keep);
  CLIENTS.splice(0, CLIENTS.length, ...keep.map((x) => x.client));
  streamCache.clear();
}
const streamCache = new Map();
const streamPending = new Map();
const STREAM_CACHE_MAX = 64;
let potProvider = null;
export function setPoTokenProvider(fn) { potProvider = fn; }
let ylog = () => {};
export function setLogger(fn) { ylog = fn || (() => {}); }

function headersFor(client, ua) {
  const h = { 'User-Agent': ua };
  if (/^(WEB|YTMUSIC|MWEB|WEB_EMBEDDED)$/.test(client)) { h.Origin = client === 'YTMUSIC' ? 'https://music.youtube.com' : 'https://www.youtube.com'; h.Referer = h.Origin + '/'; }
  return h;
}

// Some origins (notably ANDROID_VR) reject ranges beyond a small ceiling. Probe a
// mid-file chunk, not just the first two bytes, so we only pick clients that can
// actually seek. `length` may be 0 when the format carries no contentLength.
const MID_PROBE = 1_500_000;
async function clientCanSeek(url, headers, length, fetchImpl) {
  const start = length > MID_PROBE + 100_000 ? MID_PROBE : (length > 200_000 ? Math.floor(length / 2) : 0);
  const end = start + 100_000;
  const probe = await fetchImpl(url + `&range=${start}-${end}`, { headers });
  if (!probe.ok) return `HTTP ${probe.status}`;
  try { const n = (await probe.arrayBuffer()).byteLength; return n > 0 ? true : 'empty body'; } catch { return true; }
}

async function probeLength(url, headers, fetchImpl) {
  try {
    const r = await fetchImpl(url, { headers: { ...headers, Range: 'bytes=0-0' } });
    const m = /\/(\d+)/.exec(r.headers.get('content-range') || '');
    try { await r.arrayBuffer(); } catch {}
    return m ? Number(m[1]) : 0;
  } catch { return 0; }
}

async function tryClient(y, videoId, spec, quality, fetchImpl, swapPot = false) {
  const { client } = spec;
  let playerPot, streamPot;
  if (spec.pot) {
    if (!potProvider) throw new Error('no PO token provider');
    const visitor = y.session.context?.client?.visitorData || '';
    const [a, b] = await Promise.all([potProvider(visitor), potProvider(videoId)]);
    [playerPot, streamPot] = swapPot ? [b, a] : [a, b];
  }
  const info = await y.getBasicInfo(videoId, playerPot ? { client, po_token: playerPot } : { client });
  const st = info.playability_status?.status;
  if (st && st !== 'OK') throw new Error(info.playability_status?.reason || st);
  const fmts = (info.streaming_data?.adaptive_formats || []).filter((f) => f.has_audio && !f.has_video);
  if (!fmts.length) throw new Error('no audio formats');
  // Prefer original-language, non-DRC tracks
  const pool = fmts.filter((f) => !f.is_drc && (f.audio_track ? f.audio_track.audio_is_default : true));
  const cands = (pool.length ? pool : fmts).sort((a, b) => (b.bitrate || 0) - (a.bitrate || 0));
  const fmt = quality === 'low' ? cands[cands.length - 1] : cands[0];
  let url = await fmt.decipher(y.session.player);
  if (!url) throw new Error('no url');
  if (streamPot) { const u = new URL(url); u.searchParams.set('pot', streamPot); url = u.toString(); }
  const ua = uaFor(client);
  const headers = headersFor(client, ua);
  // Total size is essential: the proxy serves byte ranges and Chromium derives the song
  // length from it. Web-client formats often omit contentLength, so fall back to the
  // URL's clen= parameter, then ask googlevideo directly.
  let length = Number(fmt.content_length) || Number(new URL(url).searchParams.get('clen')) || 0;
  if (!length) length = await probeLength(url, headers, fetchImpl);
  const ok = await clientCanSeek(url, headers, length, fetchImpl);
  if (ok !== true) throw new Error(`range probe failed (${ok})`);
  const exp = Number(new URL(url).searchParams.get('expire')) * 1000 || Date.now() + 3 * 3600e3;
  return {
    url, ua, headers, client,
    mime: fmt.mime_type,
    length,
    bitrate: fmt.bitrate,
    loudnessDb: info.player_config?.audio_config?.loudness_db ?? null,
    expires: exp - 10 * 60e3
  };
}

async function resolveInOrder(videoId, specs, quality = 'best', fetchImpl = fetch) {
  const y = await ytAnon();
  const errors = [];
  for (const spec of specs) {
    for (const swap of spec.pot ? [false, true] : [false]) {
      try {
        const out = await tryClient(y, videoId, spec, quality, fetchImpl, swap);
        if (streamCache.size >= STREAM_CACHE_MAX) streamCache.delete(streamCache.keys().next().value);
        streamCache.set(videoId + '|' + quality, out);
        ylog(`stream ${videoId} len=${out.length} via ${out.client}${spec.pot ? (swap ? '+pot(swapped)' : '+pot') : ''}`);
        return out;
      } catch (e) {
        errors.push(`${spec.client}${spec.pot ? '+pot' : ''}: ${e.message}`);
        ylog(`stream ${videoId} ${spec.client}${spec.pot ? '+pot' : ''}${swap ? '(swapped)' : ''} failed: ${e.message}`);
        if (/PO token|BotGuard|provider/i.test(e.message)) break;
      }
    }
  }
  throw new Error('Could not load a playable stream. ' + errors.join(' | '));
}

// Shared with the Android app, where there is no `process`.
const ENV = typeof process !== 'undefined' && process.env ? process.env : {};
export async function resolveStream(videoId, { quality = 'best', force = false, fetchImpl = fetch } = {}) {
  if (ENV.SONCLE_FAKE_REMOTE) { const [url, len] = ENV.SONCLE_FAKE_REMOTE.split('|'); return { url, headers: {}, ua: 'x', client: 'REMOTE', mime: 'audio/webm; codecs="opus"', length: Number(len), loudnessDb: 0, expires: Date.now() + 1e9 }; }
  if (ENV.SONCLE_FAKE_AUDIO) return { url: 'file://' + ENV.SONCLE_FAKE_AUDIO + '?', ua: 'x', client: 'FAKE', mime: 'audio/webm; codecs="opus"', length: (await import('node:fs')).statSync(ENV.SONCLE_FAKE_AUDIO).size, loudnessDb: 2, expires: Date.now() + 1e9 };
  const key = videoId + '|' + quality;
  const cached = streamCache.get(key);
  if (!force && cached && cached.expires > Date.now()) return cached;
  if (!force && streamPending.has(key)) return streamPending.get(key);
  const job = resolveInOrder(videoId, STREAM_SPECS, quality, fetchImpl).finally(() => streamPending.delete(key));
  streamPending.set(key, job);
  return job;
}

// Try to re-resolve with a client different from `exclude` (used when a URL 403s
// mid-playback so the retry does not land on the same capped client).
export async function resolveStreamRotating(videoId, exclude, opts = {}) {
  const order = [...STREAM_SPECS.filter((x) => x.client !== exclude), ...STREAM_SPECS.filter((x) => x.client === exclude)];
  return resolveInOrder(videoId, order, opts.quality, opts.fetchImpl || fetch);
}

export const __stream = { STREAM_SPECS, CLIENTS };

function uaFor(client) {
  switch (client) {
    case 'ANDROID_VR': return 'com.google.android.apps.youtube.vr.oculus/1.65.10 (Linux; U; Android 12L; eureka-user Build/SQ3A.220605.009.A1) gzip';
    case 'IOS': return 'com.google.ios.youtube/20.11.6 (iPhone10,4; U; CPU iOS 16_7_7 like Mac OS X)';
    case 'TV': return 'Mozilla/5.0 (ChromiumStylePlatform) Cobalt/Version';
    default: return 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36';
  }
}

export function invalidateStream(videoId) { for (const k of [...streamCache.keys()]) if (k.startsWith(videoId + '|')) streamCache.delete(k); }

export async function songInfo(videoId) {
  const y = await ytAnon();
  const info = await y.getBasicInfo(videoId, { client: 'ANDROID_VR' });
  const b = info.basic_info || {};
  return { type: 'song', id: videoId, title: b.title, artists: [{ name: (b.author || '').replace(/ - Topic$/, ''), id: b.channel_id }], duration: b.duration || 0, thumb: pickThumb(b.thumbnail) };
}

// test hooks: inject a client double and expose the pure parse helpers
export function __setClient(y) { ytPromise = Promise.resolve(y); anonPromise = ytPromise; }
export const __parse = { normItem, normShelf, shelves, norms: { pickThumb, fixThumb, parseDuration, artistsFromRuns, kindFromPage, pageType } };

export async function rate(videoId, like) {
  if (!cookie) return false;
  const y = await yt();
  if (like) await y.interact.like(videoId); else await y.interact.removeRating(videoId);
  return true;
}
