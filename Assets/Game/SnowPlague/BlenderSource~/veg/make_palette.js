// Snow plague vegetation palette (redo 2b): 64 x 16 RGBA, 8 rows x 2 px. u = along the stem (shaded base -> tip),
// v = row (build_veg.py ROW_*; row 0 at the BOTTOM of the image = v 0..1/8). Colours from refs2/NOTES.md:
// straw #a88a5a-#c8a870, shaded stems #6a5440, reed plume #8a7a68, cattail head #4a3020. No white anywhere.
//   node make_palette.js <out.png>
const fs = require('fs'), zlib = require('zlib');
const W = 64, ROWS = 8, RH = 2, H = ROWS * RH;
const hex = h => [parseInt(h.slice(1, 3), 16), parseInt(h.slice(3, 5), 16), parseInt(h.slice(5, 7), 16)];
// three stops per row: base, middle, tip
const rows = [
  ['#5e4a38', '#a88a5a', '#c8a870'],   // 0 straw
  ['#6a5440', '#9c8058', '#b89c6c'],   // 1 duller straw
  ['#5a4e44', '#8a7a68', '#a09080'],   // 2 reed plume grey-brown
  ['#3a2618', '#4a3020', '#5a3c28'],   // 3 cattail head
  ['#3e2a22', '#6a4434', '#86584a'],   // 4 red-brown twigs
  ['#3a332e', '#5e544c', '#776c62'],   // 5 grey-brown twigs
  ['#5a4634', '#7a5e44', '#8e7454'],   // 6 seed heads / weed stems (vg2: near-black heads read as berries)
  ['#4a3c2e', '#6a5440', '#8a7050'],   // 7 dead leaves
].map(r => r.map(hex));
const raw = Buffer.alloc(H * (W * 4 + 1));
for (let y = 0; y < H; y++) {
  const row = rows[ROWS - 1 - Math.floor(y / RH)];   // image top = last row
  raw[y * (W * 4 + 1)] = 0;
  for (let x = 0; x < W; x++) {
    const t = x / (W - 1), k = t < 0.5 ? 0 : 1, f = t < 0.5 ? t * 2 : (t - 0.5) * 2;
    for (let c = 0; c < 3; c++) raw[y * (W * 4 + 1) + 1 + x * 4 + c] = Math.round(row[k][c] + (row[k + 1][c] - row[k][c]) * f);
    raw[y * (W * 4 + 1) + 1 + x * 4 + 3] = 255;
  }
}
const crcT = new Uint32Array(256);
for (let n = 0; n < 256; n++) { let c = n; for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1; crcT[n] = c >>> 0; }
const crc = b => { let c = 0xffffffff; for (const x of b) c = crcT[(c ^ x) & 255] ^ (c >>> 8); return (c ^ 0xffffffff) >>> 0; };
const chunk = (t, d) => { const l = Buffer.alloc(4); l.writeUInt32BE(d.length); const td = Buffer.concat([Buffer.from(t), d]); const c = Buffer.alloc(4); c.writeUInt32BE(crc(td)); return Buffer.concat([l, td, c]); };
const ih = Buffer.alloc(13); ih.writeUInt32BE(W, 0); ih.writeUInt32BE(H, 4); ih[8] = 8; ih[9] = 6;
fs.writeFileSync(process.argv[2], Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), chunk('IHDR', ih), chunk('IDAT', zlib.deflateSync(raw)), chunk('IEND', Buffer.alloc(0))]));
console.log('wrote', process.argv[2]);
