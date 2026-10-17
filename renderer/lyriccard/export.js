// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// Turns a lyric card into a PNG or a silent video, and hands the file to the person. The video has
// no audio on purpose: add the song in Instagram's own music picker, so the artist is still paid.

export function renderPng(card) {
  const c = Object.assign(document.createElement('canvas'), { width: card.W, height: card.H });
  card.render(c.getContext('2d'), 0, 1);
  return new Promise((res, rej) => c.toBlob((b) => (b ? res(b) : rej(new Error('Could not make the image'))), 'image/png'));
}

const VIDEO_TYPES = ['video/mp4;codecs=avc1.42E01E', 'video/mp4', 'video/webm;codecs=vp9', 'video/webm;codecs=vp8', 'video/webm'];
export const videoType = () => (typeof MediaRecorder === 'undefined' ? '' : VIDEO_TYPES.find((t) => MediaRecorder.isTypeSupported(t)) || '');

// Plays the card in real time onto a canvas and records it. Resolves with a Blob.
export function recordVideo(card, { fps = 30, onProgress, signal } = {}) {
  const type = videoType();
  if (!type) return Promise.reject(new Error('This device cannot record video'));
  const c = Object.assign(document.createElement('canvas'), { width: card.W, height: card.H });
  const ctx = c.getContext('2d');
  card.render(ctx, 0, 1);
  const stream = c.captureStream(fps);
  const rec = new MediaRecorder(stream, { mimeType: type, videoBitsPerSecond: 6_000_000 });
  const chunks = [];
  rec.ondataavailable = (e) => { if (e.data.size) chunks.push(e.data); };
  return new Promise((resolve, reject) => {
    rec.onerror = (e) => reject(e.error || new Error('Recording failed'));
    rec.onstop = () => (signal?.aborted ? reject(new Error('Cancelled')) : resolve(new Blob(chunks, { type: type.split(';')[0] })));
    rec.start(250);
    const t0 = performance.now();
    const tick = () => {
      const t = (performance.now() - t0) / 1000;
      if (signal?.aborted || t >= card.duration) { rec.stop(); return; }
      card.render(ctx, t, 1);
      onProgress?.(t / card.duration);
      requestAnimationFrame(tick);
    };
    requestAnimationFrame(tick);
  });
}

const b64 = (blob) => new Promise((res, rej) => { const r = new FileReader(); r.onload = () => res(String(r.result).split(',')[1]); r.onerror = rej; r.readAsDataURL(blob); });

// Phone: share sheet when the system offers one for files (Instagram shows up there), otherwise the
// Documents folder. Desktop: a normal download, which the app turns into a save dialog.
export async function saveBlob(blob, name) {
  const file = new File([blob], name, { type: blob.type });
  try { if (navigator.canShare?.({ files: [file] })) { await navigator.share({ files: [file] }); return 'shared'; } } catch (e) { if (e?.name === 'AbortError') return 'cancelled'; }
  const fsys = globalThis.Capacitor?.isNativePlatform?.() ? globalThis.Capacitor.Plugins?.Filesystem : null;
  if (fsys) { await fsys.writeFile({ path: name, directory: 'DOCUMENTS', data: await b64(blob), recursive: true }); return 'saved-documents'; }
  const url = URL.createObjectURL(blob);
  const a = Object.assign(document.createElement('a'), { href: url, download: name });
  document.body.append(a); a.click(); a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 60_000);
  return 'downloaded';
}
