// Snow plague, redo 2 vegetation: procedural alpha-cut card atlas (2048, RGBA PNG, no dependencies).
//   node make_atlas.js <out.png>
// Quadrants (UV, v up):  pine needles A (0,.5)-(.5,1) | pine needles B (.5,.5)-(1,1)
//                        twig spray     (0,0)-(.5,.5) | dry grass      (.5,0)-(1,.5)
// Strokes are drawn thick enough (>= 5 px at 2048) that alpha survives the mip chain; the importer also turns on
// "preserve coverage". No solid blocks anywhere (the old grass quadrant had a solid base that read as a white
// rectangle once the thin blades were lost in the mips). Transparent RGB is flood-filled with the quadrant's mean
// colour so bilinear/mip filtering never pulls in black or white.
const fs = require('fs'), zlib = require('zlib');
const S = 2048, Q = S / 2;
const R = new Float32Array(S * S), G = new Float32Array(S * S), B = new Float32Array(S * S), A = new Float32Array(S * S);

let seed = 7;
function rnd() { seed = (seed * 1664525 + 1013904223) >>> 0; return seed / 4294967296; }
function U(a, b) { return a + rnd() * (b - a); }
function lerp(a, b, t) { return a + (b - a) * t; }
function lerpc(a, b, t) { return [lerp(a[0], b[0], t), lerp(a[1], b[1], t), lerp(a[2], b[2], t)]; }

// anti-aliased capsule from (x0,y0,w0) to (x1,y1,w1) (pixels), colour 0..255, clipped to a quadrant box
function seg(x0, y0, x1, y1, w0, w1, col, box) {
  const pad = Math.max(w0, w1) / 2 + 1.5;
  let xa = Math.floor(Math.min(x0, x1) - pad), xb = Math.ceil(Math.max(x0, x1) + pad);
  let ya = Math.floor(Math.min(y0, y1) - pad), yb = Math.ceil(Math.max(y0, y1) + pad);
  xa = Math.max(xa, box[0] + 2); ya = Math.max(ya, box[1] + 2); xb = Math.min(xb, box[2] - 3); yb = Math.min(yb, box[3] - 3);
  const dx = x1 - x0, dy = y1 - y0, L2 = dx * dx + dy * dy || 1e-6;
  for (let y = ya; y <= yb; y++) for (let x = xa; x <= xb; x++) {
    const px = x + 0.5 - x0, py = y + 0.5 - y0;
    const t = Math.max(0, Math.min(1, (px * dx + py * dy) / L2));
    const ex = px - dx * t, ey = py - dy * t;
    const d = Math.sqrt(ex * ex + ey * ey), r = lerp(w0, w1, t) / 2;
    const a = Math.max(0, Math.min(1, r - d + 0.5));
    if (a <= 0) continue;
    const k = y * S + x;
    // over-composite, colour slightly darker along the edges (stroke shading)
    const edge = 1 - 0.25 * Math.min(1, d / Math.max(r, 0.5));
    const na = a + A[k] * (1 - a);
    R[k] = (col[0] * edge * a + R[k] * A[k] * (1 - a)) / na;
    G[k] = (col[1] * edge * a + G[k] * A[k] * (1 - a)) / na;
    B[k] = (col[2] * edge * a + B[k] * A[k] * (1 - a)) / na;
    A[k] = na;
  }
}

// polyline with tapering width
function stroke(pts, w0, w1, col, box) {
  for (let i = 0; i + 1 < pts.length; i++) {
    const t0 = i / (pts.length - 1), t1 = (i + 1) / (pts.length - 1);
    seg(pts[i][0], pts[i][1], pts[i + 1][0], pts[i + 1][1], lerp(w0, w1, t0), lerp(w0, w1, t1), col, box);
  }
}

// ---- pine needle clump: a twig along the card's up axis with needle fascicles fanning out, denser in the middle
function needles(ox, oy, variant) {
  const box = [ox, oy, ox + Q, oy + Q];
  const bx = ox + Q / 2, by = oy + Q - 30, top = oy + 60;
  // twig
  const twig = [];
  for (let i = 0; i <= 10; i++) { const t = i / 10; twig.push([bx + Math.sin(t * 3 + variant) * 18, lerp(by, top, t)]); }
  stroke(twig, 16, 6, [48, 36, 30], box);
  const n = variant ? 230 : 190;
  for (let i = 0; i < n; i++) {
    const t = Math.pow(rnd(), 0.8) * 0.95;
    const p = twig[Math.min(10, Math.floor(t * 10))];
    const side = rnd() < 0.5 ? -1 : 1;
    const spread = lerp(0.25, 1.25, Math.sin(t * Math.PI));   // fuller in the middle of the twig
    const ang = -Math.PI / 2 + side * U(0.35, 1.25) * spread + U(-0.15, 0.15);
    const len = Q * U(0.17, 0.3) * (0.55 + 0.45 * Math.sin(Math.min(1, t + 0.15) * Math.PI));
    const c = lerpc([38, 52, 40], [86, 98, 74], rnd());
    // a fascicle of 2-3 needles from the same point
    for (let k = 0; k < 3; k++) {
      const a = ang + U(-0.12, 0.12);
      const ex = p[0] + Math.cos(a) * len, ey = p[1] + Math.sin(a) * len * 0.85;
      const mx = (p[0] + ex) / 2 + U(-6, 6), my = (p[1] + ey) / 2 + U(-6, 6);
      stroke([[p[0], p[1]], [mx, my], [ex, ey]], 7, 4.5, c, box);
    }
  }
}

// ---- twig spray: dark grey-brown, recursive forks
function twigs(ox, oy) {
  const box = [ox, oy, ox + Q, oy + Q];
  function br(x, y, a, ln, w, d) {
    if (d > 6 || ln < 18) return;
    const ex = x + Math.cos(a) * ln, ey = y + Math.sin(a) * ln;
    const mx = (x + ex) / 2 + U(-ln, ln) * 0.14, my = (y + ey) / 2 + U(-ln, ln) * 0.14;
    const c = lerpc([40, 34, 30], [92, 84, 78], d / 6);
    stroke([[x, y], [mx, my], [ex, ey]], Math.max(5, w), Math.max(5, w * 0.8), c, box);
    const kids = d < 2 ? 3 : (rnd() < 0.6 ? 2 : 3);
    for (let k = 0; k < kids; k++) br(ex, ey, a + U(-0.8, 0.8), ln * U(0.55, 0.8), w * 0.7, d + 1);
  }
  br(ox + Q / 2, oy + Q - 16, -Math.PI / 2, Q * 0.26, 26, 0);
  br(ox + Q / 2 - 30, oy + Q - 16, -Math.PI / 2 - 0.5, Q * 0.18, 16, 1);
  br(ox + Q / 2 + 30, oy + Q - 16, -Math.PI / 2 + 0.55, Q * 0.18, 16, 1);
}

// ---- dry grass: separate tapered strands rooted along a short line, none merged into a block
function grass(ox, oy) {
  const box = [ox, oy, ox + Q, oy + Q];
  const by = oy + Q - 12;
  for (let i = 0; i < 70; i++) {
    const x0 = ox + Q / 2 + U(-Q * 0.22, Q * 0.22);
    const h = U(Q * 0.4, Q * 0.92), lean = U(-0.5, 0.5) + (x0 - ox - Q / 2) / Q * 0.8;
    const kink = rnd() < 0.3, kt = U(0.45, 0.75);
    const pts = [];
    for (let k = 0; k <= 8; k++) {
      const t = k / 8;
      let x = x0 + lean * h * t * t, y = by - h * t;
      if (kink && t > kt) { const f = t - kt; x = x0 + lean * h * kt * kt + Math.sign(lean || 1) * h * f * 0.9; y = by - h * kt + h * f * 0.4; }
      pts.push([x, y]);
    }
    const c = lerpc([112, 98, 66], [186, 170, 128], rnd());
    stroke(pts, 11, 4.5, c, box);
    // a seed head on some
    if (rnd() < 0.25) { const p = pts[8]; stroke([[p[0], p[1]], [p[0] + lean * 20, p[1] - 30]], 9, 5, lerpc(c, [200, 190, 160], 0.4), box); }
  }
}

needles(0, 0, 0);       // image top-left  = UV (0,.5)-(.5,1)
needles(Q, 0, 1);       // image top-right = UV (.5,.5)-(1,1)
twigs(0, Q);            // image bottom-left  = UV (0,0)-(.5,.5)
grass(Q, Q);            // image bottom-right = UV (.5,0)-(1,.5)

// bleed: transparent texels take the quadrant's mean opaque colour
for (const [qx, qy] of [[0, 0], [Q, 0], [0, Q], [Q, Q]]) {
  let r = 0, g = 0, b = 0, n = 0;
  for (let y = qy; y < qy + Q; y++) for (let x = qx; x < qx + Q; x++) { const k = y * S + x; if (A[k] > 0.5) { r += R[k]; g += G[k]; b += B[k]; n++; } }
  r /= n || 1; g /= n || 1; b /= n || 1;
  for (let y = qy; y < qy + Q; y++) for (let x = qx; x < qx + Q; x++) {
    const k = y * S + x, a = A[k];
    R[k] = lerp(r, R[k], Math.min(1, a * 2)); G[k] = lerp(g, G[k], Math.min(1, a * 2)); B[k] = lerp(b, B[k], Math.min(1, a * 2));
  }
}

// ---- PNG
const raw = Buffer.alloc(S * (S * 4 + 1));
for (let y = 0; y < S; y++) {
  raw[y * (S * 4 + 1)] = 0;
  for (let x = 0; x < S; x++) {
    const k = y * S + x, o = y * (S * 4 + 1) + 1 + x * 4;
    raw[o] = Math.round(Math.min(255, R[k])); raw[o + 1] = Math.round(Math.min(255, G[k])); raw[o + 2] = Math.round(Math.min(255, B[k]));
    raw[o + 3] = Math.round(Math.min(1, A[k]) * 255);
  }
}
const crcT = new Uint32Array(256);
for (let n = 0; n < 256; n++) { let c = n; for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1; crcT[n] = c >>> 0; }
function crc(buf) { let c = 0xffffffff; for (const b of buf) c = crcT[(c ^ b) & 255] ^ (c >>> 8); return (c ^ 0xffffffff) >>> 0; }
function chunk(type, data) {
  const len = Buffer.alloc(4); len.writeUInt32BE(data.length);
  const td = Buffer.concat([Buffer.from(type), data]); const c = Buffer.alloc(4); c.writeUInt32BE(crc(td));
  return Buffer.concat([len, td, c]);
}
const ihdr = Buffer.alloc(13); ihdr.writeUInt32BE(S, 0); ihdr.writeUInt32BE(S, 4); ihdr[8] = 8; ihdr[9] = 6; ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
const png = Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), chunk('IHDR', ihdr), chunk('IDAT', zlib.deflateSync(raw, { level: 9 })), chunk('IEND', Buffer.alloc(0))]);
fs.writeFileSync(process.argv[2], png);
console.log('wrote', process.argv[2]);
