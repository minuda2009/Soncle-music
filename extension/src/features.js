// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// Every Soncle function and where it stands in the extension, as plain data: the settings page is drawn
// from it and a test checks it still covers every settings group of the app.
//
// status:  'on'   works now (has a control)
//          'soon' planned for the extension
//          'app'  stays in the Soncle app, with the reason (usually: it needs the audio stream, which the
//                 extension never reads, or it is a desktop or phone feature)
//          'site' YouTube Music already has its own setting for it
// `from` names the settings groups of the Soncle app a group covers (test: none may be left out).

const onOff = (key) => ({ type: 'seg', key, options: [[true, 'On'], [false, 'Off']] });
const STREAM = 'Needs the audio stream. The extension never reads or alters it, so this stays in the Soncle app.';

export const GROUPS = [
  { id: 'appearance', title: 'Appearance', from: ['Appearance'], items: [
    { id: 'style', title: 'Soncle style on the page', desc: 'Recolours YouTube Music with Soncle’s palette and softer shapes. It only recolours: it hides and blocks nothing.', status: 'on', control: onOff('on') },
    { id: 'theme', title: 'Theme', desc: 'Dark, pure black for OLED, light, or follow the system.', status: 'on', control: { type: 'seg', key: 'mode', options: [['dark', 'Dark'], ['black', 'Black'], ['light', 'Light'], ['system', 'System']] } },
    { id: 'accent', title: 'Accent colour', desc: 'Used when colours from the cover are off or nothing is playing.', status: 'on', control: { type: 'swatches', key: 'accent' } },
    { id: 'dynamic', title: 'Colours from the cover', desc: 'Tint the page with the colours of the current song’s artwork.', status: 'on', control: onOff('dynamic') },
    { id: 'motion', title: 'Motion style', desc: 'Classic (smooth and glowy) or expressive Material 3 motion.', status: 'soon' },
    { id: 'density', title: 'List density', desc: 'Compact or comfortable rows.', status: 'soon' },
    { id: 'nowplaying', title: 'Soncle Now Playing page', desc: 'Large artwork with the lyrics beside it, like the app.', status: 'soon' },
    { id: 'mica', title: 'Window backdrop (Mica)', desc: 'A Windows window effect.', status: 'app', why: 'Browsers do not offer it to pages.' }
  ] },
  { id: 'lyrics', title: 'Lyrics', from: ['Lyrics'], items: [
    { id: 'size', title: 'Text size', desc: 'Size of the synced lyrics in the panel.', status: 'on', control: { type: 'range', key: 'lySize', min: 16, max: 34, step: 1, unit: ' px' } },
    { id: 'align', title: 'Alignment', status: 'on', control: { type: 'seg', key: 'lyAlign', options: [['left', 'Left'], ['center', 'Centre']] } },
    { id: 'autoscroll', title: 'Auto-scroll', desc: 'Follow the current line.', status: 'on', control: onOff('lyAuto') },
    { id: 'glow', title: 'Glow effect', desc: 'A soft glow on the active line.', status: 'on', control: onOff('lyGlow') },
    { id: 'delay', title: 'Lyrics delay (all songs)', desc: 'Shifts every song’s lyrics, for example to match Bluetooth audio delay. Each song also has its own nudge in the panel.', status: 'on', control: { type: 'range', key: 'lyDelay', min: 0, max: 500, step: 10, unit: ' ms' } },
    { id: 'card', title: 'Lyric card', desc: 'A picture or a short silent video of the words in mixed sizes, for Reels and Stories.', status: 'on', control: { type: 'action', action: 'card', label: 'Make a card' } },
    { id: 'cache', title: 'Lyrics cache', desc: 'Lyrics are kept in this browser for 30 days so they show instantly. Nothing is sent anywhere else.', status: 'on', control: { type: 'action', action: 'clearCache', label: 'Clear cache' } },
    { id: 'cardaudio', title: 'Lyric video with the song’s sound', status: 'app', why: 'It would copy the song’s audio. The extension never reads or saves the stream, and artists should be paid: add the song in Instagram’s music picker instead.' },
    { id: 'karaoke', title: 'Word-by-word lyrics', desc: 'Fill each word as it is sung, where the lyrics have word timing.', status: 'soon' },
    { id: 'ytlyrics', title: 'Lyrics from YouTube Music', desc: 'A fallback when LRCLIB has none.', status: 'soon' }
  ] },
  { id: 'sleep', title: 'Sleep timer', from: ['Sleep timer'], items: [
    { id: 'sleeptimer', title: 'Pause after', desc: 'Pauses the page’s player when the time is up.', status: 'on', control: { type: 'sleep', options: [[0, 'Off'], [15, '15 min'], [30, '30 min'], [45, '45 min'], [60, '60 min']] } },
    { id: 'sleepfade', title: 'Fade out before stopping', status: 'soon' }
  ] },
  { id: 'shortcuts', title: 'Keyboard shortcuts', from: [], items: [
    { id: 'keys', title: 'Shortcuts', desc: 'Alt+L opens or closes the panel. Alt+[ and Alt+] move the lyrics 0.1 s earlier or later (add Shift for 0.5 s). Esc closes the panel.', status: 'on', control: onOff('keys') },
    { id: 'palette', title: 'Command bar (Ctrl+K)', status: 'soon' }
  ] },
  { id: 'sound', title: 'Sound', from: ['Player & audio', 'Audio output & devices'], items: [
    { id: 'eq', title: 'Equaliser', desc: 'Ten bands and presets, on the page’s own player.', status: 'soon' },
    { id: 'autoeq', title: 'Headphone correction (AutoEq)', desc: 'Pick your headphones and correct their response.', status: 'soon' },
    { id: 'normalize', title: 'Audio normalisation', desc: 'Even out loudness between songs.', status: 'soon' },
    { id: 'ducking', title: 'Smart ducking', desc: 'Lower the music when something else needs to be heard.', status: 'soon' },
    { id: 'tempo', title: 'Tempo & pitch', status: 'soon' },
    { id: 'mono', title: 'Mono audio and balance', status: 'soon' },
    { id: 'silence', title: 'Skip silence', status: 'soon' },
    { id: 'crossfade', title: 'Crossfade and gapless albums', desc: 'Blend songs into each other.', status: 'soon' },
    { id: 'profiles', title: 'Per-device sound profiles', desc: 'Remember your sound settings for each output device.', status: 'soon' },
    { id: 'output', title: 'Output device', desc: 'Choose where the sound plays.', status: 'soon' }
  ] },
  { id: 'queue', title: 'Queue and playback', from: ['Queue'], items: [
    { id: 'dupes', title: 'Prevent duplicate songs in the queue', status: 'soon' },
    { id: 'remember', title: 'Remember shuffle and repeat', status: 'soon' },
    { id: 'skipfail', title: 'Skip songs that fail to play', status: 'soon' },
    { id: 'headphones', title: 'Pause when headphones disconnect', status: 'soon' },
    { id: 'focus', title: 'Focus mode', desc: 'Just the music, nothing else on screen.', status: 'soon' },
    { id: 'mini', title: 'Mini player', status: 'soon' }
  ] },
  { id: 'smart', title: 'Smart features', from: [], items: [
    { id: 'flow', title: 'Flow radio', desc: 'A radio that learns how songs mix.', status: 'app', why: STREAM },
    { id: 'smartfade', title: 'Smart crossfade by how songs mix', status: 'app', why: STREAM },
    { id: 'analysis', title: 'Tempo and key (Camelot) analysis', status: 'app', why: STREAM }
  ] },
  { id: 'library', title: 'Library and data', from: ['Storage & downloads', 'Backup & restore'], items: [
    { id: 'downloads', title: 'Downloads and offline songs', status: 'app', why: STREAM },
    { id: 'local', title: 'Local files', desc: 'Play music from folders on your computer.', status: 'app', why: 'The app reads your folders; a page extension cannot.' },
    { id: 'stats', title: 'Listening stats and history', desc: 'What you played, and when.', status: 'soon' },
    { id: 'backup', title: 'Back up and restore settings', status: 'soon' }
  ] },
  { id: 'integrations', title: 'Integrations', from: ['Integrations'], items: [
    { id: 'spotify', title: 'Import Spotify playlists', status: 'soon' },
    { id: 'discord', title: 'Discord Rich Presence', status: 'app', why: 'Needs the desktop app.' }
  ] },
  { id: 'account', title: 'Account and content', from: ['Account', 'Content'], items: [
    { id: 'signin', title: 'Sign-in and likes', desc: 'You are signed in on YouTube Music itself. The extension never sees your account.', status: 'site' },
    { id: 'country', title: 'Content country, language and explicit filter', status: 'site' }
  ] },
  { id: 'about', title: 'About', from: ['About', 'Privacy', 'System'], items: [
    { id: 'tray', title: 'Tray, start-up and window options', status: 'app', why: 'Desktop app settings.' }
  ] }
];

export const ABOUT = {
  lead: 'Not an ad blocker. Strictly. It never blocks, hides or skips ads, and it never will.',
  body: 'Soncle Companion changes how YouTube Music looks and adds features beside it: lyrics, lyric cards and, later, sound controls. Artists are paid as usual because everything plays the way YouTube Music plays it. It never reads or saves the audio stream. Unofficial: not affiliated with YouTube or Google.',
  credit: 'Soncle · by minuda2009 · GPL-3.0-or-later · written with AI coding agents'
};

export const STATUS_LABEL = { soon: 'Coming soon', app: 'In the Soncle app', site: 'On YouTube Music' };
export const allItems = () => GROUPS.flatMap((g) => g.items.map((i) => ({ ...i, group: g.id })));
