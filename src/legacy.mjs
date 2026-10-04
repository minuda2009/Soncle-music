// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// Carry-over from the app's previous name. This is the only file that knows the old identifiers:
// the old settings folder, the old backup marker and one old setting value. It exists so that
// people who used earlier builds keep their library, downloads and settings after the rename.
import fs from 'node:fs';
import path from 'node:path';

export const LEGACY = {
  dirName: 'Metrolist',                 // old %APPDATA% folder (app.setName before the rename)
  backupMarkers: ['metrolist-desktop'], // "app" field of backup files written by earlier builds
  xfCurve: 'metrolist'                  // old id of the "smooth" crossfade curve
};
export const BACKUP_MARKER = 'soncle';
export const isBackupMarker = (m) => m === BACKUP_MARKER || LEGACY.backupMarkers.includes(m);

const MARKER_FILE = '.carried-over.json';
// What gets copied from the old folder. Copy, never move: the old data stays where it was.
const ITEMS = ['downloads', 'yt-cache-v2', 'flow-features.json', 'local-library.json', 'autoeq'];

/** Settings saved by earlier builds, updated to current ids. */
export function upgradeSettings(settings) {
  if (settings && settings.xfCurve === LEGACY.xfCurve) settings.xfCurve = 'smooth';
  return settings;
}

/**
 * One-time copy of the previous build's user data into this app's folder. Call before anything
 * reads the store. Returns a short report, or null when there was nothing to do.
 *   appDataDir  app.getPath('appData')      newDir  app.getPath('userData')
 */
export function carryOver(appDataDir, newDir, log = () => {}) {
  const markerPath = path.join(newDir, MARKER_FILE);
  if (fs.existsSync(markerPath)) return null;
  const oldDir = path.join(appDataDir, LEGACY.dirName);
  const done = (report) => {
    try { fs.mkdirSync(newDir, { recursive: true }); fs.writeFileSync(markerPath, JSON.stringify({ at: new Date().toISOString(), ...report }, null, 1)); } catch (e) { log('carry-over marker: ' + e.message); }
    return report;
  };
  if (path.resolve(oldDir).toLowerCase() === path.resolve(newDir).toLowerCase() || !fs.existsSync(path.join(oldDir, 'library.json'))) return done({ copied: [] });
  // never overwrite a library that already exists here
  if (fs.existsSync(path.join(newDir, 'library.json'))) return done({ copied: [], skipped: 'library already present' });
  const copied = [];
  try {
    fs.mkdirSync(newDir, { recursive: true });
    // the store: same content, with download paths pointing at the new folder
    const raw = JSON.parse(fs.readFileSync(path.join(oldDir, 'library.json'), 'utf8'));
    const oldPrefix = path.resolve(oldDir);
    for (const d of Object.values(raw.downloads || {})) {
      if (d && typeof d.file === 'string' && path.resolve(d.file).toLowerCase().startsWith(oldPrefix.toLowerCase())) d.file = path.join(newDir, path.relative(oldPrefix, path.resolve(d.file)));
    }
    upgradeSettings(raw.settings);
    fs.writeFileSync(path.join(newDir, 'library.json'), JSON.stringify(raw));
    copied.push('library.json');
    for (const item of ITEMS) {
      const src = path.join(oldDir, item);
      if (!fs.existsSync(src)) continue;
      try { fs.cpSync(src, path.join(newDir, item), { recursive: true, force: false, errorOnExist: false }); copied.push(item); }
      catch (e) { log(`carry-over: ${item}: ${e.message}`); }
    }
    // Chromium keeps the key that encrypts the saved sign-in in "Local State". Copying it (only if
    // this folder has none yet) keeps you signed in; if it can't be used you simply sign in again.
    const ls = path.join(oldDir, 'Local State'), lsNew = path.join(newDir, 'Local State');
    if (fs.existsSync(ls) && !fs.existsSync(lsNew)) { try { fs.copyFileSync(ls, lsNew); copied.push('Local State'); } catch { /* optional */ } }
  } catch (e) {
    log('carry-over failed: ' + e.message);
    return { copied, error: e.message };   // no marker: try again next start
  }
  log(`carried over ${copied.join(', ')} from the previous version`);
  return done({ from: oldDir, copied });
}
