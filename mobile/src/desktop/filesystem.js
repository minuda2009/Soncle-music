// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// @capacitor/filesystem for the Windows app: the app's own files (library.json, auth.json,
// flow-features.json, AutoEq cache) live in the WebView's IndexedDB, inside the app's own
// profile folder. A file written to Documents (a backup) is offered as a download instead.
export const Directory = { Data: 'DATA', Documents: 'DOCUMENTS', Cache: 'CACHE' };
export const Encoding = { UTF8: 'utf8' };

let dbp = null;
function db() {
  if (!dbp) {
    dbp = new Promise((resolve, reject) => {
      const r = globalThis.indexedDB.open('soncle-files', 1);
      r.onupgradeneeded = () => r.result.createObjectStore('files');
      r.onsuccess = () => resolve(r.result);
      r.onerror = () => reject(r.error);
    });
  }
  return dbp;
}
async function tx(mode, fn) {
  const d = await db();
  return new Promise((resolve, reject) => {
    const t = d.transaction('files', mode);
    const req = fn(t.objectStore('files'));
    t.oncomplete = () => resolve(req?.result);
    t.onerror = () => reject(t.error);
    t.onabort = () => reject(t.error);
  });
}
const key = (o) => (o.directory || 'DATA') + '/' + o.path;

export const Filesystem = {
  async readFile(o) {
    const data = await tx('readonly', (s) => s.get(key(o)));
    if (data === undefined) throw new Error('File does not exist.');
    return { data };
  },
  async writeFile(o) {
    if (o.directory === Directory.Documents) {
      const a = document.createElement('a');
      a.href = URL.createObjectURL(new Blob([o.data], { type: 'application/json' }));
      a.download = o.path.split('/').pop();
      a.click();
      setTimeout(() => URL.revokeObjectURL(a.href), 10000);
      return { uri: o.path };
    }
    await tx('readwrite', (s) => s.put(o.data, key(o)));
    return { uri: key(o) };
  },
  async deleteFile(o) { await tx('readwrite', (s) => s.delete(key(o))); },
};
