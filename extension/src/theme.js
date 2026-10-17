// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// Soncle's look for music.youtube.com. Points the site's colour variables at Soncle's palette
// (survives markup churn) and softens shapes on stable element names. Recolours and rounds only —
// never hides or blocks anything the site shows (ads included).
import { themeVars, hexToHsl } from './palette.js';

export const ACCENTS = ['#bfc2f0', '#ffb3b5', '#ffb77c', '#e3c46d', '#a6d38d', '#7fd6c9', '#9ecaff', '#d5baff', '#ff8fc7'];
export const DEFAULTS = { on: true, mode: 'dark', accent: ACCENTS[0], dynamic: true };

export const resolveMode = (mode, prefersDark) => (mode === 'system' ? (prefersDark ? 'dark' : 'light') : ['dark', 'black', 'light'].includes(mode) ? mode : 'dark');
export const accentHs = (hex) => { const [h, s] = hexToHsl(/^#[0-9a-f]{6}$/i.test(hex) ? hex : ACCENTS[0]); return [h, s]; };

// Selectors with an id-weight :not() so rules beat the site's, whatever load order.
const SITE = [
  'html:root', 'html', 'body',
  'ytmusic-app', 'ytmusic-app-layout',
  'ytmusic-nav-bar', 'ytmusic-player-bar', 'ytmusic-player-page',
  'ytmusic-browse-response', 'ytmusic-section-list-renderer',
  'ytmusic-search-page', 'ytmusic-library-page',
].map((s) => `${s}:not(#sc)`).join(',\n');

// Site variables → Soncle tokens (from public YTM userstyles + yt-spec surface tokens).
const MAP = {
  // YTM brand / chrome
  '--ytmusic-background': 'var(--sc-bg)',
  '--ytmusic-brand-background-solid': 'var(--sc-bg)',
  '--ytmusic-brand-background-primary': 'var(--sc-bg)',
  '--ytmusic-general-background-a': 'var(--sc-bg)',
  '--ytmusic-general-background-b': 'var(--sc-surface-1)',
  '--ytmusic-general-background-c': 'var(--sc-bg)',
  '--ytmusic-player-bar-background': 'var(--sc-surface-2)',
  '--ytmusic-nav-bar-background': 'var(--sc-bg)',
  '--ytmusic-nav-bar': 'var(--sc-bg)',
  '--ytmusic-search-background': 'var(--sc-surface-3)',
  '--ytmusic-detail-header': 'var(--sc-bg)',
  '--ytmusic-text-primary': 'var(--sc-on-surface)',
  '--ytmusic-text-secondary': 'var(--sc-on-surface-var)',
  // yt-spec
  '--yt-spec-base-background': 'var(--sc-bg)',
  '--yt-spec-raised-background': 'var(--sc-surface-2)',
  '--yt-spec-menu-background': 'var(--sc-surface-3)',
  '--yt-spec-brand-background-primary': 'var(--sc-surface-1)',
  '--yt-spec-general-background-a': 'var(--sc-surface-1)',
  '--yt-spec-general-background-b': 'var(--sc-surface-2)',
  '--yt-spec-general-background-c': 'var(--sc-bg)',
  '--yt-spec-additive-background': 'color-mix(in srgb, var(--sc-on-surface) 8%, transparent)',
  '--yt-spec-10-percent-layer': 'color-mix(in srgb, var(--sc-on-surface) 10%, transparent)',
  '--yt-spec-text-primary': 'var(--sc-on-surface)',
  '--yt-spec-text-secondary': 'var(--sc-on-surface-var)',
  '--yt-spec-text-primary-inverse': 'var(--sc-bg)',
  '--yt-spec-icon-active-other': 'var(--sc-on-surface)',
  '--yt-spec-icon-inactive': 'var(--sc-on-surface-var)',
  '--yt-spec-icon-disabled': 'var(--sc-outline)',
  '--yt-spec-call-to-action': 'var(--sc-primary)',
  '--yt-spec-call-to-action-inverse': 'var(--sc-on-primary)',
  '--yt-spec-brand-link-text': 'var(--sc-primary)',
  '--yt-spec-themed-blue': 'var(--sc-primary)',
  '--yt-spec-themed-green': 'var(--sc-primary)',
  '--yt-spec-badge-chip-background': 'var(--sc-surface-3)',
  '--yt-spec-button-chip-background-hover': 'color-mix(in srgb, var(--sc-on-surface) 12%, transparent)',
  '--yt-spec-outline': 'var(--sc-outline-var)',
  '--yt-spec-static-brand-red': 'var(--sc-primary)',
  // Paper / Polymer controls
  '--paper-slider-active-color': 'var(--sc-primary)',
  '--paper-slider-knob-color': 'var(--sc-primary)',
  '--paper-slider-container-color': 'var(--sc-outline-var)',
  '--paper-slider-secondary-color': 'color-mix(in srgb, var(--sc-primary) 40%, transparent)',
  '--paper-progress-active-color': 'var(--sc-primary)',
  '--paper-progress-secondary-color': 'color-mix(in srgb, var(--sc-primary) 35%, transparent)',
  '--paper-progress-container-color': 'var(--sc-outline-var)',
  '--paper-spinner-color': 'var(--sc-primary)',
  '--paper-toggle-button-checked-bar-color': 'var(--sc-primary)',
  '--paper-toggle-button-checked-button-color': 'var(--sc-primary)',
  '--paper-toggle-button-checked-ink-color': 'var(--sc-primary)',
  '--paper-dialog-background-color': 'var(--sc-surface-2)',
  '--paper-listbox-background-color': 'var(--sc-surface-2)',
  '--paper-item-body-secondary-color': 'var(--sc-on-surface-var)',
  '--iron-icon-fill-color': 'var(--sc-on-surface)',
};

export function cssFor(hue, sat, mode) {
  const vars = themeVars(hue, sat, mode);
  const own = Object.entries(vars).map(([k, v]) => `--sc-${k.slice(2)}: ${v};`).join('\n  ');
  const map = Object.entries(MAP).map(([k, v]) => `${k}: ${v};`).join('\n  ');
  const dark = mode !== 'light';
  return `${SITE} {
  ${own}
  ${map}
  color-scheme: ${dark ? 'dark' : 'light'};
}

/* Page chrome */
html:root:not(#sc), body:not(#sc) {
  background: var(--sc-bg) !important;
  scrollbar-color: var(--sc-outline-var) transparent;
}
ytmusic-app:not(#sc) {
  font-family: "Segoe UI Variable Text", "Segoe UI", Inter, Roboto, system-ui, sans-serif;
  background: var(--sc-bg);
  color: var(--sc-on-surface);
}

/* Nav + search */
ytmusic-nav-bar:not(#sc) {
  background: color-mix(in srgb, var(--sc-bg) 88%, transparent) !important;
  backdrop-filter: blur(16px);
  border-bottom: 1px solid color-mix(in srgb, var(--sc-outline-var) 55%, transparent);
}
ytmusic-search-box:not(#sc) {
  border-radius: 999px;
  background: var(--sc-surface-3) !important;
}

/* Player bar */
ytmusic-player-bar:not(#sc) {
  background: color-mix(in srgb, var(--sc-surface-2) 92%, transparent) !important;
  backdrop-filter: blur(20px);
  border-top: 1px solid color-mix(in srgb, var(--sc-outline-var) 50%, transparent);
}
ytmusic-player-bar:not(#sc) .middle-controls .title,
ytmusic-player-bar:not(#sc) .content-info-wrapper .title {
  color: var(--sc-on-surface) !important;
}
ytmusic-player-bar:not(#sc) .byline,
ytmusic-player-bar:not(#sc) .subtitle {
  color: var(--sc-on-surface-var) !important;
}

/* Full player page */
ytmusic-player-page:not(#sc) {
  background: var(--sc-bg) !important;
}
ytmusic-player-page:not(#sc) #main-panel,
ytmusic-player-page:not(#sc) .side-panel {
  background: transparent !important;
}

/* Side guide / library rail */
#guide-wrapper:not(#sc),
ytmusic-guide-renderer:not(#sc),
tp-yt-app-drawer:not(#sc) {
  background: var(--sc-bg) !important;
  border-inline-end: 1px solid color-mix(in srgb, var(--sc-outline-var) 45%, transparent);
}

/* Cards, list rows, queue */
ytmusic-two-row-item-renderer:not(#sc),
ytmusic-responsive-list-item-renderer:not(#sc),
ytmusic-player-queue-item:not(#sc),
ytmusic-carousel-item-renderer:not(#sc) {
  border-radius: 14px;
}
ytmusic-two-row-item-renderer:not(#sc):hover,
ytmusic-responsive-list-item-renderer:not(#sc):hover,
ytmusic-player-queue-item:not(#sc):hover {
  background: color-mix(in srgb, var(--sc-on-surface) 6%, transparent) !important;
}
ytmusic-thumbnail-renderer:not(#sc),
ytmusic-item-thumbnail-overlay-renderer:not(#sc) {
  border-radius: 14px;
  overflow: hidden;
}

/* Chips / tabs / buttons */
ytmusic-chip-cloud-chip-renderer:not(#sc) {
  border-radius: 999px !important;
  background: var(--sc-surface-3) !important;
}
ytmusic-chip-cloud-chip-renderer:not(#sc)[aria-selected="true"],
ytmusic-chip-cloud-chip-renderer:not(#sc).selected {
  background: var(--sc-primary-container) !important;
  color: var(--sc-on-primary-container) !important;
}
tp-yt-paper-tabs:not(#sc) {
  --paper-tabs-selection-bar-color: var(--sc-primary);
}
tp-yt-paper-button:not(#sc).style-scope,
yt-button-renderer:not(#sc) {
  border-radius: 999px !important;
}
yt-button-renderer:not(#sc)[is-primary],
tp-yt-paper-button:not(#sc).yt-spec-button-shape-next--filled {
  background: var(--sc-primary) !important;
  color: var(--sc-on-primary) !important;
}

/* Menus / dialogs / toasts */
tp-yt-paper-listbox:not(#sc),
tp-yt-paper-dialog:not(#sc),
tp-yt-paper-toast:not(#sc),
ytmusic-popup-container:not(#sc) {
  border-radius: 16px;
  background: var(--sc-surface-2) !important;
  color: var(--sc-on-surface) !important;
}

/* Section headers */
ytmusic-carousel-shelf-renderer:not(#sc) .header .title,
ytmusic-shelf-renderer:not(#sc) .title,
ytmusic-header-renderer:not(#sc) h2,
ytmusic-detail-header-renderer:not(#sc) .title {
  color: var(--sc-on-surface) !important;
  letter-spacing: -0.01em;
}

/* Progress / volume use paper-slider vars already mapped */
#progress-bar:not(#sc),
.volume-slider:not(#sc) {
  --paper-slider-active-color: var(--sc-primary);
  --paper-slider-knob-color: var(--sc-primary);
}

/* Immersive / like / icon buttons */
yt-icon-button:not(#sc) {
  color: var(--sc-on-surface);
}
yt-icon-button:not(#sc):hover {
  background: color-mix(in srgb, var(--sc-on-surface) 8%, transparent);
  border-radius: 999px;
}
`;
}

/** True when CSS only recolors/rounds — used by tests. */
export function isStyleOnly(css) {
  const bad = /display\s*:\s*none|visibility\s*:\s*hidden|opacity\s*:\s*0(?:\.0+)?\b|pointer-events\s*:\s*none|ad[-_]?promo|ytd-ad|masthead-ad/i;
  return !bad.test(css);
}
