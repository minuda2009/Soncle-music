export const defaultSettings = {
  volume: 0.8, dynamicColor: true, accent: '#bfc2f0', theme: 'system',
  eq: { on: false, preamp: 0, bands: Array(10).fill(0) }, limiter: true, loudness: 'normal',
  crossfade: 0, gapless: true, mono: false, balance: 0, outputDevice: 'default',
  lyricsSize: 30, lyricsAlign: 'left', lyricsGlow: false, lyricsAutoScroll: true,
  autoDownloadLiked: false, density: 'comfortable', mica: true, motionStyle: 'classic',
  sleepTimer: 0, btDelay: 0
};
export const defaults = {
  liked: [], playlists: [], savedAlbums: [], savedPlaylists: [], followedArtists: [], history: [], searchHistory: [],
  downloads: {}, settings: defaultSettings, cookie: '', session: null,
  lyricsOffsets: {}, lyricsChoice: {}, eqPresets: [], deviceProfiles: {}, skips: {}
};
