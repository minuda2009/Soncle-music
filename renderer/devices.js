// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// Identify what kind of audio output is connected, from its Windows / Chromium name.
const RX = {
  handsFree: /hands-?free|\bhf\b|ag audio|headset \(/i,
  car: /\bcar\b|carplay|android auto|\bsync\b|toyota|honda|suzuki|maruti|hyundai|\bkia\b|nissan|mazda|\bbmw\b|\baudi\b|\bvw\b|volkswagen|mercedes|ford|tesla|pioneer|kenwood|\bjvc\b|alpine|\bcd-|media ?nav|uconnect|mylink|entune/i,
  tv: /hdmi|\btv\b|display audio|nvidia high definition|amd high definition|intel\(r\) display|monitor|dell u|lg (ultra|tv)|samsung (tv|q)|bravia|roku|chromecast/i,
  earbuds: /buds|airpods(?! max)|\bwf-|earbud|\btws\b|airdopes|freebuds|liberty|\bear ?\(|nothing ear|pixel buds|galaxy buds|elite \d|jabra elite|soundcore (a|p|life p|space a)|enco|redmi ?buds|oneplus ?buds|realme ?buds|cmf ?buds|in-?ear|earphones?|powerbeats|beats (fit|studio buds|flex)|momentum true|qc ?earbuds|ultra open/i,
  headphones: /headphones?|airpods max|\bwh-|\bqc ?\d|quietcomfort|momentum|rockerz|\bjbl (tune|live)\b|beats (solo|studio)|\bsolo ?\d|\bath-|\bmdr-|\bhd ?\d{3}|over-?ear|on-?ear|arctis|hyperx|cloud|kraken|blackshark|g pro|astro|space one|q30|q35|q45|life q/i,
  speaker: /speaker|\bflip ?\d|\bcharge ?\d|\bclip ?\d|\bgo ?\d|\bboom|megaboom|wonderboom|soundlink|partybox|xtreme|\bsrs-|marshall|\bue |echo|homepod|nest|sonos|soundbar|stone ?\d|motion\+?|soundcore (motion|flare|boom)|bose (home|smart)|mi portable|tribit|zebronics|\bpulse\b/i,
  laptop: /realtek|conexant|intel\(r\) smart sound|smart sound|cirrus|synaptics|internal|built-?in|macbook|high definition audio device/i,
  wireless: /\bwh-|\bwf-|airpods|buds|airdopes|rockerz|\bflip ?\d|\bcharge ?\d|soundlink|\bjbl\b|\bboat\b|soundcore|beats|\bbose\b|marshall|stone ?\d|tribit|\bue |\bsrs-|life q|space one|enco|nothing ear|\bmi |redmi|realme|oneplus|jabra|skullcandy|sennheiser|momentum|\bqc ?\d|quietcomfort|galaxy|pixel/i,
  usb: /\busb\b|dac|focusrite|scarlett|audient|behringer|motu|fiio|topping|schiit|interface/i
};

export function classifyOutput(label, btNames = []) {
  const raw = String(label || '').replace(/^(Default|Communications) - /i, '').trim();
  // Peel trailing "(...)" groups, respecting nesting: "Headphones (LP-V53) (Bluetooth)",
  // "Speakers (USB Audio) (0d8c:0014)", "Headphones (Realtek(R) Audio)"
  const tags = [];
  let rest = raw;
  while (rest.endsWith(')')) {
    let depth = 0, i = rest.length - 1;
    for (; i >= 0; i--) {
      if (rest[i] === ')') depth++;
      else if (rest[i] === '(' && --depth === 0) break;
    }
    if (i <= 0) break;
    tags.unshift(rest.slice(i + 1, -1).trim());
    rest = rest.slice(0, i).trim();
  }
  const btTag = tags.some((t) => /^bluetooth$/i.test(t));
  const named = tags.filter((t) => !/^(bluetooth|stereo|[0-9a-f]{4}:[0-9a-f]{4})$/i.test(t));
  const endpoint = tags.length ? rest : '';           // e.g. "Headphones", "Speakers", "Headset"
  const model = (named[0] || (tags.length ? rest : raw)).replace(/\s*(Stereo|Hands-Free.*|AG Audio)$/i, '').trim() || raw;
  const lower = raw.toLowerCase();
  const wiredHint = /realtek|conexant|cirrus|usb|high definition|hdmi|focusrite|scarlett|dac/i.test(raw);
  const bluetooth = btTag || !wiredHint && (/bluetooth|\bbt\b|hands-?free|ag audio/i.test(raw) || /^headset$/i.test(endpoint) || btNames.some((n) => n && (lower.includes(n.toLowerCase()) || model.toLowerCase().includes(n.toLowerCase()))) || RX.wireless.test(model));
  const handsFree = bluetooth && RX.handsFree.test(raw);
  let type = 'unknown';
  const test = (k) => RX[k].test(model) || RX[k].test(raw);
  if (test('car')) type = 'car';
  else if (test('earbuds')) type = 'earbuds';
  else if (test('tv')) type = 'tv';
  else if (/^headphones?$/i.test(endpoint) && !bluetooth && /realtek|conexant|high definition|cirrus/i.test(model)) type = 'wired';
  else if (test('headphones') || (bluetooth && /^head(phones?|set)$/i.test(endpoint))) type = 'headphones';
  else if (test('speaker') && (bluetooth || !RX.laptop.test(model))) type = bluetooth ? 'btspeaker' : 'speaker';
  else if (test('usb')) type = 'usb';
  else if (RX.laptop.test(model) || /^speakers?$/i.test(endpoint)) type = 'laptop';
  else if (bluetooth) type = 'headphones';
  return { label: raw, model: model || raw || 'System default', endpoint, type, bluetooth, handsFree };
}

// Android reports the active output as { id, type, name } (SonclePlugin.java). Turn that into a
// label the classifier above already understands, so the desktop device-profile path is reused.
export function androidOutputLabel({ type, name, bluetooth } = {}) {
  const product = String(name || '').trim();
  switch (type) {
    case 'speaker': return 'Speakers (Phone speaker)';
    case 'wired': return 'Headphones (Wired headphones)';
    case 'usb': return 'USB Audio (USB Audio)';
    case 'hdmi': return 'HDMI (TV)';
    case 'car': return product ? `Car audio (${product})` : 'Car audio';
    case 'bluetooth': return product ? `Bluetooth audio (${product})` : 'Bluetooth audio (Bluetooth)';
    default: return bluetooth && product ? `Bluetooth audio (${product})` : product || 'Audio output';
  }
}

export const DEVICE_INFO = {
  earbuds: { name: 'Earbuds', icon: 'earbuds', preset: 'Earbuds' },
  headphones: { name: 'Headphones', icon: 'headphones', preset: 'Headphones' },
  wired: { name: 'Wired headphones', icon: 'headphones', preset: 'Headphones' },
  btspeaker: { name: 'Bluetooth speaker', icon: 'speaker', preset: 'Bluetooth speaker' },
  speaker: { name: 'Speakers', icon: 'speaker', preset: 'Flat' },
  laptop: { name: 'Built-in speakers', icon: 'laptop', preset: 'Laptop speakers' },
  car: { name: 'Car audio', icon: 'car', preset: 'Car' },
  tv: { name: 'TV / monitor', icon: 'tv', preset: 'TV / HDMI' },
  usb: { name: 'USB audio / DAC', icon: 'usb', preset: 'Flat' },
  unknown: { name: 'Audio output', icon: 'speaker', preset: 'Flat' }
};
