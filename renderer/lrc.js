// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// Synced-lyrics (LRC) parser, shared by the desktop and phone apps and the browser extension.

export function parseLrc(lrc) {
  const out = [];
  const ts = (m, s) => Number(m) * 60 + Number(s);
  for (const line of lrc.split(/\r?\n/)) {
    const tags = [...line.matchAll(/\[(\d+):(\d+(?:\.\d+)?)\]/g)];
    if (!tags.length) continue;
    const body = line.replace(/\[[^\]]*\]/g, '');
    // enhanced LRC: <mm:ss.xx>word <mm:ss.xx>word …
    let words = null;
    if (/<\d+:\d+(?:\.\d+)?>/.test(body)) {
      words = [];
      const re = /<(\d+):(\d+(?:\.\d+)?)>([^<]*)/g;
      let m;
      while ((m = re.exec(body))) { const w = m[3]; if (w.trim()) words.push({ t: ts(m[1], m[2]), text: w }); else if (words.length) words[words.length - 1].end = ts(m[1], m[2]); }
      if (!words.length) words = null;
    }
    const text = body.replace(/<\d+:\d+(\.\d+)?>/g, '').replace(/\s+/g, ' ').trim();
    for (const m of tags) out.push({ t: ts(m[1], m[2]), text, words });
  }
  out.sort((a, b) => a.t - b.t);
  for (let i = 0; i < out.length; i++) out[i].end = out[i + 1] ? out[i + 1].t : out[i].t + 6;
  return out;
}
