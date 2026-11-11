// Hero pine (Sketchfab "Japanese Black Pine", CC BY) needle texture -> alpha-cut card texture.
// The source alpha is a faint blend mask (max ~35/255: drawn alpha-blended/hashed on Sketchfab), so a 0.5 cutoff removed
// every needle. Alpha is rescaled (x10), dilated 2 px (the faint mask is mostly needle outlines) so needles survive the mips, RGB bled into the transparent area.
//   node hero_needles.js <src.png> <out.png>
const fs = require('fs'), zlib = require('zlib');
function readPng(p) {
  const b = fs.readFileSync(p); let o = 8, idat = [], w, h, ct, bd, il;
  while (o < b.length) { const l = b.readUInt32BE(o), t = b.toString('ascii', o + 4, o + 8), d = b.slice(o + 8, o + 8 + l);
    if (t === 'IHDR') { w = d.readUInt32BE(0); h = d.readUInt32BE(4); bd = d[8]; ct = d[9]; il = d[12]; }
    if (t === 'IDAT') idat.push(d); o += 12 + l; }
  if (bd !== 8 || ct !== 6 || il) throw new Error('need 8-bit RGBA non-interlaced');
  const raw = zlib.inflateSync(Buffer.concat(idat)), st = w * 4, out = Buffer.alloc(w * h * 4);
  for (let y = 0; y < h; y++) { const f = raw[y * (st + 1)];
    for (let x = 0; x < st; x++) { const a = x >= 4 ? out[y * st + x - 4] : 0, up = y ? out[(y - 1) * st + x] : 0, c = (x >= 4 && y) ? out[(y - 1) * st + x - 4] : 0;
      let v = raw[y * (st + 1) + 1 + x];
      if (f === 1) v += a; else if (f === 2) v += up; else if (f === 3) v += (a + up) >> 1;
      else if (f === 4) { const p = a + up - c, pa = Math.abs(p - a), pb = Math.abs(p - up), pc = Math.abs(p - c); v += pa <= pb && pa <= pc ? a : pb <= pc ? up : c; }
      out[y * st + x] = v & 255; } }
  return { w, h, px: out };
}
function writePng(p, w, h, px) {
  const raw = Buffer.alloc(h * (w * 4 + 1));
  for (let y = 0; y < h; y++) { raw[y * (w * 4 + 1)] = 0; px.copy(raw, y * (w * 4 + 1) + 1, y * w * 4, (y + 1) * w * 4); }
  const T = new Uint32Array(256); for (let n = 0; n < 256; n++) { let c = n; for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1; T[n] = c >>> 0; }
  const crc = b => { let c = 0xffffffff; for (const x of b) c = T[(c ^ x) & 255] ^ (c >>> 8); return (c ^ 0xffffffff) >>> 0; };
  const ch = (t, d) => { const l = Buffer.alloc(4); l.writeUInt32BE(d.length); const td = Buffer.concat([Buffer.from(t), d]); const c = Buffer.alloc(4); c.writeUInt32BE(crc(td)); return Buffer.concat([l, td, c]); };
  const ih = Buffer.alloc(13); ih.writeUInt32BE(w, 0); ih.writeUInt32BE(h, 4); ih[8] = 8; ih[9] = 6;
  fs.writeFileSync(p, Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), ch('IHDR', ih), ch('IDAT', zlib.deflateSync(raw, { level: 9 })), ch('IEND', Buffer.alloc(0))]));
}
const { w, h, px } = readPng(process.argv[2]);
const A = new Float32Array(w * h);
for (let i = 0; i < w * h; i++) A[i] = Math.min(1, px[i * 4 + 3] / 255 * 14);
const D = new Float32Array(w * h);   // 3x3 max, a little softer than the centre
for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) {
  let m = A[y * w + x];
  for (let dy = -2; dy <= 2; dy++) for (let dx = -2; dx <= 2; dx++) { if (dx * dx + dy * dy > 5) continue; const xx = x + dx, yy = y + dy; if (xx >= 0 && yy >= 0 && xx < w && yy < h) m = Math.max(m, A[yy * w + xx] * (dx * dx + dy * dy > 2 ? 0.75 : 0.95)); }
  D[y * w + x] = m;
}
let r = 0, g = 0, b = 0, n = 0;
for (let i = 0; i < w * h; i++) if (A[i] > 0.3) { r += px[i * 4]; g += px[i * 4 + 1]; b += px[i * 4 + 2]; n++; }
r /= n || 1; g /= n || 1; b /= n || 1;
const out = Buffer.alloc(w * h * 4);
for (let i = 0; i < w * h; i++) {
  const k = Math.min(1, A[i] * 2);
  out[i * 4] = Math.round(r + (px[i * 4] - r) * k); out[i * 4 + 1] = Math.round(g + (px[i * 4 + 1] - g) * k); out[i * 4 + 2] = Math.round(b + (px[i * 4 + 2] - b) * k);
  out[i * 4 + 3] = Math.round(D[i] * 255);
}
writePng(process.argv[3], w, h, out);
console.log('wrote', process.argv[3], 'opaque>0.5:', (D.filter(v => v > 0.5).length / (w * h)).toFixed(3));
