# Procedural textures for the snow plague field (Pillow only, no downloads):
#   SP_FoliageAtlas.png  2048 RGBA: pine needle tuft | red maple cluster / dry grass clump | dead twig spray
#   SP_Blood.png         1024 RGBA 2x2: pool, splatter, drag trail, drips (alpha = coverage, RGB = wetness/darkness)
#   SP_Cookie_*.png      512 grey light cookies for the stone lanterns (white = light, black = shadow)
#   SP_FogNoise.png      512 tileable grey noise for fog sheets / height fog
# python -I make_textures.py <unity SnowPlague dir>
import sys, os, math, random
from PIL import Image, ImageDraw, ImageFilter, ImageChops

OUT = os.path.join(sys.argv[1], "Textures")
os.makedirs(OUT, exist_ok=True)
S = 4096          # drawn at 2x, downsampled
Q = S // 2


def lerpc(a, b, t): return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))


def needle_tuft(dr, ox, oy, rng):
    """Pine twig with dense needle bunches, base at the bottom middle, growing up (into the card)."""
    bx, by = ox + Q // 2, oy + Q - 40
    pts = [(bx, by)]
    x, y = bx, by
    for i in range(10):
        x += rng.uniform(-30, 30); y -= Q * 0.085; pts.append((x, y))
    dr.line(pts, fill=(48, 32, 24, 255), width=26)
    for k in range(1, len(pts)):
        px, py = pts[k]
        t = k / len(pts)
        for n in range(70):
            a = rng.uniform(-math.pi, 0) + rng.uniform(-0.3, 0.3)
            ln = rng.uniform(140, 300) * (1.05 - t * 0.35)
            ex, ey = px + math.cos(a) * ln, py + math.sin(a) * ln * 0.9
            c = lerpc((22, 34, 24), (58, 74, 48), rng.random() * 0.7 + 0.3 * (1 - t))
            dr.line([(px, py), (ex, ey)], fill=c + (255,), width=rng.choice((7, 8, 10)))
    # side twigs
    for k in range(2, len(pts) - 1, 1):
        px, py = pts[k]
        for side in (-1, 1):
            a = -math.pi / 2 + side * rng.uniform(0.6, 1.0)
            ln2 = 420 * (1.1 - k / len(pts) * 0.5); ex, ey = px + math.cos(a) * ln2, py + math.sin(a) * ln2
            dr.line([(px, py), (ex, ey)], fill=(48, 32, 24, 255), width=14)
            for n in range(90):
                f = rng.random(); qx, qy = px + (ex - px) * f, py + (ey - py) * f
                aa = a + rng.uniform(-1.3, 1.3); ln = rng.uniform(100, 220)
                c = lerpc((22, 34, 24), (58, 74, 48), rng.random())
                dr.line([(qx, qy), (qx + math.cos(aa) * ln, qy + math.sin(aa) * ln)], fill=c + (255,), width=8)


def maple_cluster(dr, ox, oy, rng):
    cx, cy = ox + Q // 2, oy + Q // 2
    # stems
    for i in range(9):
        a = rng.uniform(0, 2 * math.pi)
        dr.line([(cx, cy + Q * 0.35), (cx + math.cos(a) * Q * 0.3, cy + math.sin(a) * Q * 0.3)], fill=(40, 20, 18, 255), width=12)
    for i in range(70):
        r = Q * 0.36 * math.sqrt(rng.random())
        a = rng.uniform(0, 2 * math.pi)
        lx, ly = cx + math.cos(a) * r, cy + math.sin(a) * r
        size = rng.uniform(70, 120)
        rot = rng.uniform(0, 2 * math.pi)
        c = lerpc((95, 12, 12), (176, 28, 20), rng.random())
        c = lerpc(c, (60, 10, 12), (r / (Q * 0.36)) * 0.4)
        poly = []
        for k in range(10):   # 5 lobes
            ang = rot + k * math.pi / 5
            rr = size * (1.0 if k % 2 == 0 else 0.45)
            poly.append((lx + math.cos(ang) * rr, ly + math.sin(ang) * rr))
        dr.polygon(poly, fill=c + (255,))
        dr.line([(lx, ly), (lx + math.cos(rot) * size * 0.8, ly + math.sin(rot) * size * 0.8)], fill=lerpc(c, (30, 5, 5), 0.5) + (255,), width=4)


def grass_clump(dr, ox, oy, rng):
    bx, by = ox + Q // 2, oy + Q - 30
    for i in range(110):
        x0 = bx + rng.uniform(-Q * 0.12, Q * 0.12)
        h = rng.uniform(Q * 0.45, Q * 0.92)
        lean = rng.uniform(-0.55, 0.55)
        pts = []
        for k in range(8):
            t = k / 7
            pts.append((x0 + lean * h * t * t, by - h * t + (h * 0.12 * t * t if abs(lean) > 0.4 else 0)))
        c = lerpc((120, 108, 70), (196, 182, 140), rng.random())
        for k in range(7):
            w = int(18 * (1 - k / 7)) + 3
            dr.line([pts[k], pts[k + 1]], fill=c + (255,), width=w)


def twig_spray(dr, ox, oy, rng):
    def br(x, y, a, ln, w, d):
        if d > 5 or ln < 25: return
        ex, ey = x + math.cos(a) * ln, y + math.sin(a) * ln
        mx, my = (x + ex) / 2 + rng.uniform(-ln, ln) * 0.12, (y + ey) / 2 + rng.uniform(-ln, ln) * 0.12
        c = lerpc((26, 24, 24), (60, 56, 54), d / 5)
        dr.line([(x, y), (mx, my), (ex, ey)], fill=c + (255,), width=max(3, int(w)), joint="curve")
        for k in range(rng.randint(2, 3)):
            br(ex, ey, a + rng.uniform(-0.75, 0.75), ln * rng.uniform(0.55, 0.78), w * 0.68, d + 1)
    br(ox + Q / 2, oy + Q - 20, -math.pi / 2, Q * 0.3, 30, 0)


def bleed(img):
    """Fill transparent RGB with nearby colour so mips/filtering never show dark halos."""
    a = img.split()[3]
    rgb = img.convert("RGB")
    blur = rgb.filter(ImageFilter.GaussianBlur(24))
    bl = Image.new("RGB", img.size, (0, 0, 0))
    m = a.point(lambda v: 255 if v > 8 else 0)
    bl.paste(rgb, (0, 0), m)
    fill = Image.composite(rgb, blur, m)
    out = fill.copy(); out.putalpha(a)
    return out


rng = random.Random(7)
atlas = Image.new("RGBA", (S, S), (0, 0, 0, 0))
dr = ImageDraw.Draw(atlas)
needle_tuft(dr, 0, 0, rng)          # uv (0, .5)-(.5, 1)
maple_cluster(dr, Q, 0, rng)        # uv (.5, .5)-(1, 1)
grass_clump(dr, 0, Q, rng)          # uv (0, 0)-(.5, .5)
twig_spray(dr, Q, Q, rng)           # uv (.5, 0)-(1, .5)
atlas = atlas.resize((2048, 2048), Image.LANCZOS)
bleed(atlas).save(os.path.join(OUT, "SP_FoliageAtlas.png"))


# ---- blood ---------------------------------------------------------------------------------------------------
def blob(dr, cx, cy, r, rng, col, n=22, jag=0.25):
    pts = []
    ph = [rng.uniform(0, 6.28) for _ in range(4)]
    for k in range(n):
        a = k / n * 2 * math.pi
        rr = r * (1 + jag * (math.sin(a * 3 + ph[0]) * 0.5 + math.sin(a * 5 + ph[1]) * 0.3 + rng.uniform(-0.3, 0.3)))
        pts.append((cx + math.cos(a) * rr, cy + math.sin(a) * rr))
    dr.polygon(pts, fill=col)


B = 2048; H = B // 2
blood = Image.new("L", (B, B), 0)
d = ImageDraw.Draw(blood)
rng = random.Random(11)
# pool (top-left)
blob(d, H * 0.5, H * 0.5, H * 0.3, rng, 255, 40, 0.18)
for i in range(14):
    a = rng.uniform(0, 6.28); r = rng.uniform(H * 0.25, H * 0.42)
    blob(d, H * 0.5 + math.cos(a) * r, H * 0.5 + math.sin(a) * r, rng.uniform(12, 50), rng, 255, 14, 0.3)
# splatter (top-right): directional spray
for i in range(90):
    t = rng.random() ** 1.6
    x = H + H * 0.2 + t * H * 0.65; y = H * 0.5 + rng.gauss(0, 1) * H * 0.12 * (0.3 + t)
    blob(d, x, y, (1 - t) * rng.uniform(20, 70) + 6, rng, 255, 12, 0.35)
blob(d, H + H * 0.22, H * 0.5, H * 0.13, rng, 255, 24, 0.3)
# drag trail (bottom-left)
x, y = H * 0.12, H + H * 0.5
for i in range(60):
    x += H * 0.0135; y += rng.uniform(-6, 6)
    w = H * 0.07 * (1 - i / 70) + rng.uniform(-8, 8)
    for s in (-1, 0, 1):
        if rng.random() < 0.8: blob(d, x, y + s * w * 0.6, w * 0.35, rng, 255, 10, 0.4)
# drips (bottom-right)
for i in range(28):
    blob(d, H + rng.uniform(80, H - 80), H + rng.uniform(80, H - 80), rng.uniform(8, 42), rng, 255, 12, 0.3)
blood = blood.filter(ImageFilter.GaussianBlur(3))
# wetness/thickness in RGB: thicker in the middle of each shape
thick = blood.filter(ImageFilter.GaussianBlur(22))
img = Image.merge("RGBA", (thick, thick, thick, blood)).resize((1024, 1024), Image.LANCZOS)
img.save(os.path.join(OUT, "SP_Blood.png"))


# ---- lantern cookies -----------------------------------------------------------------------------------------
def cookie(name, seed, bars=4, branches=True):
    C = 1024
    im = Image.new("L", (C, C), 0)
    d = ImageDraw.Draw(im)
    rng = random.Random(seed)
    d.ellipse([C * 0.06, C * 0.06, C * 0.94, C * 0.94], fill=255)
    # the lantern's own pillars/roof corners: four dark wedges
    for k in range(4):
        a = k * math.pi / 2 + math.pi / 4
        w = 0.16
        d.polygon([(C / 2, C / 2), (C / 2 + math.cos(a - w) * C, C / 2 + math.sin(a - w) * C), (C / 2 + math.cos(a + w) * C, C / 2 + math.sin(a + w) * C)], fill=0)
    # window lattice bars
    for k in range(4):
        a = k * math.pi / 2
        for b in range(1, bars):
            off = (b / bars - 0.5) * 0.5
            a2 = a + off
            d.line([(C / 2, C / 2), (C / 2 + math.cos(a2) * C, C / 2 + math.sin(a2) * C)], fill=0, width=14)
    if branches:
        def br(x, y, a, ln, w, dd):
            if dd > 4: return
            ex, ey = x + math.cos(a) * ln, y + math.sin(a) * ln
            d.line([(x, y), (ex, ey)], fill=0, width=int(w))
            for _ in range(2): br(ex, ey, a + rng.uniform(-0.7, 0.7), ln * 0.65, w * 0.65, dd + 1)
        for _ in range(2):
            a = rng.uniform(0, 6.28)
            br(C / 2 + math.cos(a) * C * 0.6, C / 2 + math.sin(a) * C * 0.6, a + math.pi + rng.uniform(-0.5, 0.5), C * 0.25, 22, 0)
    im = im.filter(ImageFilter.GaussianBlur(10))
    # soft falloff toward the rim
    fall = Image.new("L", (C, C), 0); fd = ImageDraw.Draw(fall)
    for r in range(40):
        t = r / 40
        fd.ellipse([C * (0.06 + 0.4 * t), C * (0.06 + 0.4 * t), C * (0.94 - 0.4 * t), C * (0.94 - 0.4 * t)], fill=int(255 * min(1, 0.25 + t * 1.5)))
    im = ImageChops.multiply(im, fall.filter(ImageFilter.GaussianBlur(30)))
    im.resize((512, 512), Image.LANCZOS).save(os.path.join(OUT, name + ".png"))


cookie("SP_Cookie_Lantern", 1)
cookie("SP_Cookie_LanternBranch", 2, bars=3, branches=True)
cookie("SP_Cookie_Plain", 3, bars=2, branches=False)


# ---- tileable fog noise --------------------------------------------------------------------------------------
N = 512
rng = random.Random(5)
acc = [[0.0] * N for _ in range(N)]
for octave, amp in ((8, 0.5), (16, 0.25), (32, 0.15), (64, 0.1)):
    g = [[rng.random() for _ in range(octave)] for _ in range(octave)]
    for y in range(N):
        fy = y / N * octave; y0 = int(fy); ty = fy - y0; ty = ty * ty * (3 - 2 * ty)
        for x in range(N):
            fx = x / N * octave; x0 = int(fx); tx = fx - x0; tx = tx * tx * (3 - 2 * tx)
            a = g[y0 % octave][x0 % octave]; b = g[y0 % octave][(x0 + 1) % octave]
            c = g[(y0 + 1) % octave][x0 % octave]; e = g[(y0 + 1) % octave][(x0 + 1) % octave]
            acc[y][x] += amp * ((a + (b - a) * tx) + ((c + (e - c) * tx) - (a + (b - a) * tx)) * ty)
fog = Image.new("L", (N, N))
fog.putdata([int(max(0, min(1, v)) * 255) for row in acc for v in row])
fog.save(os.path.join(OUT, "SP_FogNoise.png"))
print("textures done")
