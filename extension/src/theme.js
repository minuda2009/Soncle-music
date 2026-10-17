// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// Soncle's look for music.youtube.com. Mostly done by pointing the site's own colour variables at
// Soncle's palette (so it survives the site changing its markup), plus a few soft shapes on elements
// that have kept their names. Nothing here hides or removes anything the site shows (ads included).
import { themeVars, hexToHsl } from './palette.js';

export const ACCENTS = ['#bfc2f0', '#ffb3b5', '#ffb77c', '#e3c46d', '#a6d38d', '#7fd6c9', '#9ecaff', '#d5baff', '#ff8fc7'];
export const DEFAULTS = { on: true, mode: 'dark', accent: ACCENTS[0], dynamic: true };

export const resolveMode = (mode, prefersDark) => (mode === 'system' ? (prefersDark ? 'dark' : 'light') : ['dark', 'black', 'light'].includes(mode) ? mode : 'dark');
export const accentHs = (hex) => { const [h, s] = hexToHsl(/^#[0-9a-f]{6}$/i.test(hex) ? hex : ACCENTS[0]); return [h, s]; };

// Selectors with an id-weight :not() so the rules beat the site's own, whatever order they load in.
const SITE = ['html:root', 'ytmusic-app', 'ytmusic-app-layout', 'ytmusic-nav-bar', 'ytmusic-player-bar', 'ytmusic-player-page'].map((s) => `${s}:not(#sc)`).join(',\n');

// The map from the site's variables to Soncle's (sources: the variables public YouTube Music
// userstyles set: general-background a/b/c, brand-background-solid, player-bar-background, text colours).
const MAP = {
  '--ytmusic-brand-background-solid': 'var(--sc-bg)',
  '--ytmusic-general-background-a': 'var(--sc-bg)',
  '--ytmusic-general-background-b': 'var(--sc-surface-1)',
  '--ytmusic-general-background-c': 'var(--sc-bg)',
  '--ytmusic-player-bar-background': 'var(--sc-surface-2)',
  '--ytmusic-nav-bar-background': 'var(--sc-bg)',
  '--ytmusic-search-background': 'var(--sc-surface-3)',
  '--yt-spec-base-background': 'var(--sc-bg)',
  '--yt-spec-raised-background': 'var(--sc-surface-2)',
  '--yt-spec-menu-background': 'var(--sc-surface-3)',
  '--yt-spec-text-primary': 'var(--sc-on-surface)',
  '--yt-spec-text-secondary': 'var(--sc-on-surface-var)',
  '--yt-spec-icon-active-other': 'var(--sc-on-surface)',
  '--yt-spec-call-to-action': 'var(--sc-primary)',
  '--paper-slider-active-color': 'var(--sc-primary)',
  '--paper-slider-knob-color': 'var(--sc-primary)'
};

export function cssFor(hue, sat, mode) {
  const vars = themeVars(hue, sat, mode);
  const own = Object.entries(vars).map(([k, v]) => `--sc-${k.slice(2)}: ${v};`).join('\n  ');
  const map = Object.entries(MAP).map(([k, v]) => `${k}: ${v};`).join('\n  ');
  return `${SITE} {
  ${own}
  ${map}
  color-scheme: ${mode === 'light' ? 'light' : 'dark'};
}
html:root:not(#sc), body:not(#sc) { background: var(--sc-bg) !important; scrollbar-color: var(--sc-outline-var) transparent; }
ytmusic-app:not(#sc) { font-family: "Segoe UI Variable Text", "Segoe UI", Inter, Roboto, system-ui, sans-serif; }
ytmusic-two-row-item-renderer:not(#sc), ytmusic-responsive-list-item-renderer:not(#sc), ytmusic-player-queue-item:not(#sc) { border-radius: 14px; }
ytmusic-thumbnail-renderer:not(#sc) { border-radius: 14px; overflow: hidden; }
ytmusic-player-bar:not(#sc) { backdrop-filter: blur(18px); }
tp-yt-paper-listbox:not(#sc), tp-yt-paper-toast:not(#sc) { border-radius: 16px; }
`;
}
