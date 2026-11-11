# Snow plague, redo 2 vegetation: dense dead brush clumps, dry grass tufts, framing pines and gnarled dead trees.
# blender -b --factory-startup -P build_veg.py -- <unity SnowPlague dir> <work dir> [preview]
# Uses ../splib.py (Kit, branch helpers). Vertex colour: R frost/snow (forced to 1 on the snow lumps caught in the
# brush), G AO, B random per stem, A sway. Instanced pieces (brush, grass) have ONE material each (the instancer draws
# submesh 0 only); framing trees have bark + needle/twig cards (placed as GameObjects, not instanced).
# Card UVs: SP_V_CardAtlas.png (make_atlas.js): needles A (0,.5)-(.5,1), needles B (.5,.5)-(1,1), twigs (0,0)-(.5,.5),
# grass (.5,0)-(1,.5).
import bpy, sys, os, math, random, json
from mathutils import Vector, Matrix, noise
sys.path.append(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
from splib import *

args = sys.argv[sys.argv.index("--") + 1:]
OUT = os.path.join(args[0], "Models", "Veg")
WORK = args[1]
PREVIEW = len(args) > 2 and args[2] == "preview"
os.makedirs(OUT, exist_ok=True); os.makedirs(WORK, exist_ok=True)
clear_scene()
REPORT = {}
JOBS = []
NEEDLE_A = (0.0, 0.5, 0.5, 1.0); NEEDLE_B = (0.5, 0.5, 1.0, 1.0); TWIG_UV = (0.0, 0.0, 0.5, 0.5); GRASS_UV = (0.5, 0.0, 1.0, 0.5)


# ---- branch that also records its tips (for twig cards / snow lumps) -------------------------------------------------
def branch2(K, rng, base, d, length, r0, depth, maxdepth, mat, sides, tips, sway0=0.0, child_n=(3, 5), spread=(28, 55),
            step=0.35, wob=0.35, tropism=0.05, tip_r=0.0, broken=0.0, child_len=(0.45, 0.7), seed=0.0, rnd=0.0, forks=None,
            min_sides=3, ribbon_from=99):
    pts = gnarl_path(rng, base, d, length, step, wob, tropism, seed + depth * 3.1)
    snapped = depth < maxdepth and rng.random() < broken
    r_end = r0 * 0.45 if snapped else tip_r
    radius = lambda t: r0 + (r_end - r0) * (t ** 0.8)
    swl = lambda t: min(1.0, sway0 + (1 - sway0) * t * (0.3 + 0.7 * depth / max(1, maxdepth)))
    sd = max(min_sides, sides - depth)
    if depth >= ribbon_from: ribbon(K, pts, radius, mat, swl, rnd)
    else: K.tube(pts, radius, sd, mat, sway=swl, rnd=rnd, cap_end=snapped)
    tips.append((pts[-1].copy(), (pts[-1] - pts[-2]).normalized(), depth, swl(1.0)))
    if depth >= maxdepth: return
    total = sum((pts[i] - pts[i - 1]).length for i in range(1, len(pts)))
    n = rng.randint(*child_n)
    for c in range(n):
        t = rng.uniform(0.25, 0.95) if not snapped else rng.uniform(0.2, 0.7)
        acc = 0; p = pts[-1]; dloc = pts[-1] - pts[-2]
        for i in range(1, len(pts)):
            seg = (pts[i] - pts[i - 1]).length
            if acc + seg >= t * total:
                f = (t * total - acc) / max(seg, 1e-6)
                p = pts[i - 1].lerp(pts[i], f); dloc = pts[i] - pts[i - 1]; break
            acc += seg
        if forks is not None: forks.append((p.copy(), swl(t)))
        cd = rnd_dir(rng, dloc, rng.uniform(*spread), up_bias=0.15)
        cl = length * rng.uniform(*child_len) * (1 - t * 0.35)
        cr = max(radius(t) * 0.62, 0.003)
        branch2(K, rng, p - dloc.normalized() * cr, cd, cl, cr, depth + 1, maxdepth, mat, sides, tips, swl(t), child_n, spread,
                step * 0.75, wob * 1.1, tropism, tip_r, broken, child_len, seed + c * 1.7, rnd, forks, min_sides, ribbon_from)


def ribbon(K, pts, radius, mat, swl, rnd):
    """Fine twig as a flat strip facing up (2 tris per segment instead of 6 for a 3-sided tube). Custom normals lie
    sideways so the strip shades like a round twig and does not take the world-up snow."""
    L = [0.0]
    for i in range(1, len(pts)): L.append(L[-1] + (pts[i] - pts[i - 1]).length)
    tot = max(L[-1], 1e-6); rows = []
    for i, p in enumerate(pts):
        tg = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
        sd = tg.cross(UP)
        sd = sd.normalized() if sd.length > 1e-4 else perp(tg)
        t = L[i] / tot; w = max(radius(t), 0.0025) * 1.3
        nn = (sd + UP * 0.25).normalized()
        a = K.vert(p - sd * w, (0, L[i]), swl(t), rnd); b = K.vert(p + sd * w, (0.05, L[i]), swl(t), rnd)
        K.nrm[a] = -nn + UP * 0.5; K.nrm[b] = nn + UP * 0.5
        rows.append((a, b))
    for r0, r1 in zip(rows, rows[1:]): K.face((r0[0], r0[1], r1[1], r1[0]), mat)


def snow_lump(K, rng, c, r, mat, sway=0.3):
    """Flattened lumpy snow caught on twigs: a squashed low icosphere (forced frost later)."""
    i0 = len(K.v)
    K.blob(c, (r * rng.uniform(1.0, 1.6), r * rng.uniform(0.8, 1.2), r * 0.45), mat, subdiv=1, lumpy=0.3, seed=rng.randint(0, 999),
           rot=Matrix.Rotation(rng.uniform(0, 6.28), 3, 'Z'))
    for i in range(i0, len(K.v)): K.extra[i] = (sway, K.extra[i][1])
    return set(range(i0, len(K.v)))


# ---- dead brush clump: many thin twiggy stems from one root, snow lumps caught in the forks ----------------------------
def brush(name, seed, h, stems=(9, 13), spread_deg=40, maxdepth=2, child_n=(2, 4), lumps=(4, 7), flat=1.0, width=0.25, r=(0.007, 0.012)):
    rng = random.Random(seed); K = Kit(name)
    tips, forks = [], []
    for s in range(rng.randint(*stems)):
        a = rng.uniform(0, 2 * math.pi)
        tilt = rng.uniform(8, spread_deg)
        d = Vector((math.cos(a) * math.sin(math.radians(tilt)) * flat, math.sin(a) * math.sin(math.radians(tilt)) * flat, math.cos(math.radians(tilt))))
        base = Vector((math.cos(a), math.sin(a), 0)) * rng.uniform(0, width * 0.4) + Vector((0, 0, -0.03))
        branch2(K, rng, base, d, h * rng.uniform(0.55, 1.05), rng.uniform(*r), 0, maxdepth, "SP_V_Brush", 3, tips,
                child_n=child_n, spread=(25, 65), step=h * 0.16, wob=0.55, tropism=0.04, broken=0.3, child_len=(0.4, 0.7),
                seed=seed * 3 + s, rnd=rng.random(), forks=forks, ribbon_from=1)
    snow = set()
    cand = [f for f in forks if f[0].z > h * 0.25]
    rng.shuffle(cand)
    for p, sw in cand[:rng.randint(*lumps)]:
        snow |= snow_lump(K, rng, p + Vector((0, 0, 0.008)), h * rng.uniform(0.025, 0.045), "SP_V_Brush", sway=sw)
    # a low snow cushion at the root (snow drifts into the clump)
    snow |= snow_lump(K, rng, Vector((0, 0, -0.01)), width * 0.3, "SP_V_Brush", sway=0.0)
    return K, snow


# ---- dry grass tuft: flat tapered blades, some folded over, some lying on the snow ---------------------------------
def grass(name, seed, n=(70, 90), hgt=(0.2, 0.45), width=0.0065, broken=0.35, lying=0.15, segs=4, rad=0.08):
    rng = random.Random(seed); K = Kit(name)
    for b in range(rng.randint(*n)):
        a = rng.uniform(0, 2 * math.pi)
        out = Vector((math.cos(a), math.sin(a), 0))
        lie = rng.random() < lying
        lean = rng.uniform(0.9, 1.4) if lie else rng.uniform(0.15, 0.8)
        d = (out * lean + UP).normalized()
        L = rng.uniform(*hgt) * (1.2 if lie else 1.0)
        base = out * rng.uniform(0, rad)
        pts = []; kink = rng.random() < broken; kt = rng.uniform(0.35, 0.7)
        for k in range(segs + 1):
            t = k / segs
            p = base + d * (L * t)
            p += out * (L * 0.5 * t * t * lean)
            p.z -= L * 0.3 * t * t * lean
            if kink and t > kt:
                p = base + d * (L * kt) + (out * 0.7 - UP * 0.7).normalized() * (L * (t - kt))
            p.z = max(p.z, 0.01 + 0.005 * k)
            pts.append(p)
        side = Vector((-out.y, out.x, 0)); side = (side + Vector((0, 0, rng.uniform(-0.3, 0.3)))).normalized()
        rr = rng.random()
        rows = []
        for k, p in enumerate(pts):
            t = k / segs
            w = width * (1 - t) ** 0.6 + 0.0015
            rows.append((K.vert(p - side * w, (0, t), t ** 1.4, rr), K.vert(p + side * w, (1, t), t ** 1.4, rr)))
        for r0, r1 in zip(rows, rows[1:]):
            K.face((r0[0], r0[1], r1[1], r1[0]), "SP_V_Grass")
    return K, set()


# ---- framing dead tree: thick gnarled trunk, buttress roots, many forks, twig cards at the tips ---------------------
def frame_dead(name, seed, height, r0=0.45, lean=0.12, children=(7, 10), maxdepth=4, broken=0.35, cards=0.6, lean_dir=None):
    rng = random.Random(seed); K = Kit(name)
    ld = lean_dir or Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), 0)).normalized()
    d = Vector((ld.x * lean, ld.y * lean, 1))
    pts = gnarl_path(rng, Vector((0, 0, -0.3)), d, height * 0.75, 0.5, 0.28, 0.0, seed)
    tot = len(pts) - 1
    jag = lambda i, k, t: (noise.noise(Vector((k * 0.9, i * 0.6, seed))) * 0.18) + 0.45 * (1 - smooth01(0, 0.15, t)) * (0.55 + 0.45 * math.sin(k * 2.3 + seed))
    K.tube(pts, lambda t: r0 * (1 - 0.7 * t) + 0.03, 10, "SP_V_Bark", sway=lambda t: 0.08 * t * t, cap_end=True, jag=jag)
    tips = []
    for k in range(6):   # buttress roots
        a = k * 2 * math.pi / 6 + rng.uniform(-0.3, 0.3)
        o = Vector((math.cos(a), math.sin(a), 0))
        rp = [Vector((0, 0, 0.45)) + o * r0 * 0.4, o * (r0 + 0.3) + Vector((0, 0, 0.1)), o * (r0 + 0.9) + Vector((0, 0, -0.02)), o * (r0 + 1.6) + Vector((0, 0, -0.15))]
        K.tube(rp, lambda t: r0 * 0.42 * (1 - t * 0.85) + 0.01, 6, "SP_V_Bark")
    n = rng.randint(*children)
    for c in range(n):
        f = rng.uniform(0.3, 1.0)
        i = min(tot, int(f * tot))
        p = pts[i]; dl = pts[min(i + 1, tot)] - pts[max(i - 1, 0)]
        cd = rnd_dir(rng, dl, rng.uniform(45, 80), up_bias=0.0)
        cl = height * rng.uniform(0.35, 0.55) * (1.2 - f * 0.5)
        branch2(K, rng, p, cd, cl, r0 * (1 - 0.7 * f) * 0.55, 1, maxdepth, "SP_V_Bark", 7, tips, sway0=0.1 * f, child_n=(2, 4),
                spread=(25, 60), step=0.45, wob=0.45, tropism=-0.01, broken=broken, child_len=(0.4, 0.65), seed=seed + c, rnd=rng.random())
    # twig spray cards at the finest tips: fill the crown with fine silhouette detail
    for p, dirn, depth, sw in tips:
        if depth < maxdepth - 1 or rng.random() > cards: continue
        ax = perp(dirn)
        sz = rng.uniform(0.8, 1.4)
        K.card(p - dirn * sz * 0.15, ax, dirn, sz, sz, TWIG_UV, "SP_V_Twigs", normal=lambda q: (q * Vector((1, 1, 0)) / max(1.0, height) + Vector((0, 0, 0.7))).normalized(),
               sway=(sw, min(1.0, sw + 0.2)), rnd=rng.random())
    return K, set()


# ---- framing pine (적송): S-curved trunk, horizontal limbs, needle pads at the limb ends with snow on top ------------
def pad(K, rng, c, radius, n, uvs, flat=0.3):
    for i in range(n):
        a = rng.uniform(0, 2 * math.pi)
        r = radius * math.sqrt(rng.random()) * 0.7
        out = Vector((math.cos(a), math.sin(a), 0))
        base = c + out * r * 0.45 + Vector((0, 0, rng.uniform(-0.1, 0.12)))
        ay = (out + Vector((0, 0, rng.uniform(-0.1, flat)))).normalized()
        ax = (Vector((-out.y, out.x, 0)) + Vector((0, 0, rng.uniform(-0.3, 0.3)))).normalized()
        L = radius * rng.uniform(0.7, 1.05)
        K.card(base, ax, ay, L * 0.75, L, uvs[rng.randrange(len(uvs))], "SP_V_Needles",
               normal=lambda p, c=c, radius=radius: ((p - c) * Vector((1, 1, 0)) / radius * 0.6 + Vector((0, 0, 1.0))).normalized(),
               sway=(0.5, 0.8), rnd=rng.random())


def frame_pine(name, seed, height, r0=0.4, limbs=(7, 9), pads=(3, 6)):
    rng = random.Random(seed); K = Kit(name)
    # S-curve trunk: lean one way low, back the other way high
    a = rng.uniform(0, 2 * math.pi); o = Vector((math.cos(a), math.sin(a), 0))
    pts = []
    for k in range(14):
        t = k / 13
        s = math.sin(t * math.pi * 1.3) * 0.9 + t * 1.2
        p = o * s * height * 0.14 + Vector((0, 0, -0.3 + t * height))
        p += Vector((noise.noise(Vector((t * 4, seed, 0))), noise.noise(Vector((seed, t * 4, 0))), 0)) * 0.25
        pts.append(p)
    jag = lambda i, k, t: noise.noise(Vector((k * 1.1, i * 0.8, seed))) * 0.12 + 0.35 * (1 - smooth01(0, 0.1, t)) * (0.5 + 0.5 * math.sin(k * 2.0))
    K.tube(pts, lambda t: r0 * (1 - 0.65 * t) + 0.03, 10, "SP_V_PineBark", sway=lambda t: 0.06 * t * t, cap_end=True, jag=jag)
    for k in range(5):
        aa = k * 2 * math.pi / 5 + rng.uniform(-0.3, 0.3)
        oo = Vector((math.cos(aa), math.sin(aa), 0))
        K.tube([Vector((0, 0, 0.5)) + oo * r0 * 0.3, oo * (r0 + 0.2) + Vector((0, 0, 0.08)), oo * (r0 + 0.9) + Vector((0, 0, -0.1))],
               lambda t: r0 * 0.45 * (1 - t) ** 1.2 + 0.01, 6, "SP_V_PineBark")
    tips = []
    n = rng.randint(*limbs)
    for c in range(n):
        f = 0.35 + 0.6 * (c + rng.uniform(0, 0.8)) / n
        i = min(13, int(f * 13)); p = pts[i]
        aa = rng.uniform(0, 2 * math.pi) if c else a + math.pi
        dd = Vector((math.cos(aa), math.sin(aa), rng.uniform(0.05, 0.35))).normalized()
        L = height * rng.uniform(0.25, 0.42) * (1.2 - f * 0.5)
        lt = []
        branch2(K, rng, p, dd, L, r0 * (1 - 0.65 * f) * 0.45, 1, 3, "SP_V_PineBark", 6, lt, sway0=0.1, child_n=(2, 3), spread=(25, 50),
                step=0.5, wob=0.35, tropism=0.06, broken=0.15, child_len=(0.35, 0.6), seed=seed + c * 2.3, rnd=rng.random())
        # umbrella pads at the outer tips of this limb (sparse clumps, gaps between)
        lt = [t for t in lt if t[2] >= 1]
        rng.shuffle(lt)
        for tp, tdir, depth, sw in lt[:rng.randint(*pads)]:
            pr = rng.uniform(1.0, 1.6); PADS.append((tp + Vector((0, 0, 0.1)), pr))
            pad(K, rng, tp + Vector((0, 0, 0.1)), pr, rng.randint(9, 13), [NEEDLE_A, NEEDLE_B])
    return K, set()


# ---- redo 2b (owner: "too many small identical tufts, not enough trees/vegetation"; refs2 dead_vegetation sheet) -------
# Five forms with palette UVs into SP_V_StrawPalette.png (make_palette.js): u = base (shaded #6a5440) .. tip (straw), v =
# row. No white tips and no frost on the stems: snow only as caps/lumps (vertex R forced to 1), and a shallow darker
# dip ring at the base so the stems read as poking THROUGH the snow.
PAL_ROWS = 8
ROW_STRAW, ROW_STRAW2, ROW_PLUME, ROW_CATTAIL, ROW_TWIG_RED, ROW_TWIG_GREY, ROW_SEED, ROW_LEAF = range(8)
PADS = []


def paint(K, i0, row, H, u0=0.04, u1=0.96, i1=None):
    """Palette UVs for verts i0..i1: u by height (dark base -> light tip), v = row centre."""
    v = (row + 0.5) / PAL_ROWS
    for i in range(i0, len(K.v) if i1 is None else i1):
        t = max(0.0, min(1.0, K.v[i].z / max(H, 0.05)))
        K.uv[i] = (u0 + (u1 - u0) * t ** 0.75, v)


def dip_ring(K, force, r, sides=10, mat="SP_V_Grass"):
    """Shallow dip in the snow round the base: snow (R=1), AO dark in the middle -> light at the rim."""
    c = K.vert(Vector((0, 0, -0.012)), (0.5, 0.5))
    ring = [K.vert(Vector((math.cos(a) * r, math.sin(a) * r, 0.004)), (0.5, 0.5)) for a in [2 * math.pi * k / sides for k in range(sides)]]
    for k in range(sides): K.face((c, ring[k], ring[(k + 1) % sides]), mat)
    K.nrm[c] = Vector((0, 0, 1))
    for i in ring: K.nrm[i] = Vector((0, 0, 1))
    force[c] = (1.0, 0.45)
    for i in ring: force[i] = (1.0, 1.0)


def blade(K, rng, base, d, L, width, segs, mat, bend_dir=None, bend=0.0, kink=None, sway_pow=1.4):
    """One flat leaf/blade, side vector horizontal so it shows from above. kink = fraction where it folds down."""
    out = Vector((d.x, d.y, 0)); out = out.normalized() if out.length > 1e-4 else Vector((1, 0, 0))
    bd = bend_dir or out
    pts = []
    for k in range(segs + 1):
        t = k / segs
        p = base + d * (L * t) + bd * (L * bend * t * t)
        p.z -= L * 0.35 * bend * t * t
        if kink is not None and t > kink:
            p = base + d * (L * kink) + bd * (L * bend * kink * kink) + (bd * 0.6 - UP * 0.8).normalized() * (L * (t - kink))
        p.z = max(p.z, 0.006 + 0.004 * k)
        pts.append(p)
    side = Vector((-out.y, out.x, 0)); side = (side + Vector((0, 0, rng.uniform(-0.25, 0.25)))).normalized()
    rr = rng.random(); rows = []
    for k, p in enumerate(pts):
        t = k / segs; w = width * (1 - t) ** 0.6 + 0.0012
        rows.append((K.vert(p - side * w, (0, t), t ** sway_pow, rr), K.vert(p + side * w, (1, t), t ** sway_pow, rr)))
    for r0, r1 in zip(rows, rows[1:]): K.face((r0[0], r0[1], r1[1], r1[0]), mat)


def stem(K, rng, base, top, r, sides, segs, mat, wob=0.04, kink=None, sway0=0.0):
    """Straight-ish vertical stem as a thin tube; kink = (fraction, direction) snaps it over."""
    pts = []
    for k in range(segs + 1):
        t = k / segs
        p = base.lerp(top, t) + Vector((noise.noise(Vector((t * 3, base.x * 9, 0))), noise.noise(Vector((base.y * 9, t * 3, 0))), 0)) * wob * t
        if kink and t > kink[0]:
            kp = base.lerp(top, kink[0])
            p = kp + kink[1] * ((top - base).length * (t - kink[0]))
            p.z = max(p.z, 0.02)
        pts.append(p)
    K.tube(pts, lambda t: r * (1 - 0.5 * t), sides, mat, sway=lambda t: min(1.0, sway0 + t * t))
    return pts


def cattails(name, seed, n=(3, 8), h=(1.0, 1.5), spread=0.35):
    """Upright cattail group: 3-8 stems with dark heads (most with a snow cap), long leaves, a few broken stems."""
    rng = random.Random(seed); K = Kit(name); force = {}
    for s in range(rng.randint(*n)):
        a = rng.uniform(0, 6.28); b = Vector((math.cos(a), math.sin(a), 0)) * rng.uniform(0, spread)
        H = rng.uniform(*h)
        lean = Vector((rng.uniform(-0.12, 0.12), rng.uniform(-0.12, 0.12), 1)).normalized()
        broken = rng.random() < 0.2
        kink = (rng.uniform(0.45, 0.7), (Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), 0)).normalized() * 0.8 - UP * 0.3).normalized()) if broken else None
        i0 = len(K.v)
        pts = stem(K, rng, b, b + lean * H, 0.012, 4, 5, "SP_V_Grass", kink=kink)
        paint(K, i0, ROW_STRAW, H)
        if not broken:
            hz = rng.uniform(0.75, 0.86); hp = pts[0].lerp(pts[-1], hz); hd = (pts[-1] - pts[0]).normalized(); hl = rng.uniform(0.14, 0.22)
            i0 = len(K.v)
            K.tube([hp, hp + hd * hl * 0.5, hp + hd * hl], lambda t: 0.03 * (1 - 0.5 * abs(t - 0.5)) + 0.004, 6, "SP_V_Grass", sway=lambda t: 1.0, cap_start=True, cap_end=True)
            paint(K, i0, ROW_CATTAIL, H * 2.0, 0.3, 0.7)
            if rng.random() < 0.45:
                i0 = len(K.v)
                K.blob(hp + hd * hl + Vector((0, 0, 0.01)), (0.04, 0.037, 0.028), "SP_V_Grass", subdiv=1, lumpy=0.25, seed=rng.randint(0, 99))
                for i in range(i0, len(K.v)): K.extra[i] = (1.0, K.extra[i][1]); force[i] = (1.0, 1.0)
        for l in range(rng.randint(1, 3)):
            aa = rng.uniform(0, 6.28); d = Vector((math.cos(aa) * 0.25, math.sin(aa) * 0.25, 1)).normalized()
            i0 = len(K.v)
            blade(K, rng, b, d, H * rng.uniform(0.5, 0.85), 0.014, 5, "SP_V_Grass", bend=rng.uniform(0.15, 0.5), kink=rng.uniform(0.4, 0.7) if rng.random() < 0.35 else None)
            paint(K, i0, ROW_STRAW2 if rng.random() < 0.5 else ROW_LEAF, H * 0.8)
    for g in range(rng.randint(8, 14)):
        aa = rng.uniform(0, 6.28); d = Vector((math.cos(aa) * 0.7, math.sin(aa) * 0.7, 1)).normalized()
        i0 = len(K.v)
        blade(K, rng, Vector((math.cos(aa), math.sin(aa), 0)) * rng.uniform(0, spread), d, rng.uniform(0.12, 0.3), 0.006, 3, "SP_V_Grass", bend=0.5)
        paint(K, i0, ROW_STRAW, 0.3)
    return K, force


def reed_wall(name, seed, length=2.6, depth=0.7, n=70, h=(1.1, 1.7)):
    """Dense reed stand: a strip of vertical stems with grey-brown plumes; some snapped and leaning, broken bits around it."""
    rng = random.Random(seed); K = Kit(name); force = {}
    wind = Vector((0.25, 0.1, 0))
    for s in range(n):
        x = rng.uniform(-0.5, 0.5) * length; y = rng.gauss(0, depth * 0.3)
        edge = abs(x) / (length * 0.5)
        H = rng.uniform(*h) * (1 - 0.35 * edge ** 2)
        b = Vector((x, y, 0))
        top = b + (UP + wind * rng.uniform(0.3, 1.2) + Vector((rng.uniform(-0.08, 0.08), rng.uniform(-0.08, 0.08), 0))).normalized() * H
        broken = rng.random() < (0.12 + 0.25 * edge)
        kink = (rng.uniform(0.35, 0.7), (Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), 0)).normalized() - UP * rng.uniform(0.1, 0.6)).normalized()) if broken else None
        i0 = len(K.v)
        pts = stem(K, rng, b, top, 0.008, 3, 4, "SP_V_Grass", wob=0.06, kink=kink)
        paint(K, i0, ROW_STRAW if rng.random() < 0.6 else ROW_STRAW2, H)
        if not broken:
            i0 = len(K.v)
            pd = (pts[-1] - pts[-2]).normalized()
            for f in range(rng.randint(3, 5)):
                d = (pd + Vector((rng.uniform(-0.5, 0.5), rng.uniform(-0.5, 0.5), 0)) + wind).normalized()
                blade(K, rng, pts[-1] - pd * 0.04, d, rng.uniform(0.12, 0.22), 0.012, 2, "SP_V_Grass", bend=0.4, sway_pow=0.5)
            paint(K, i0, ROW_PLUME, H * 1.5, 0.4, 0.9)
        if rng.random() < 0.5:
            k = rng.randint(1, 2); aa = rng.uniform(0, 6.28)
            i0 = len(K.v)
            blade(K, rng, pts[k], Vector((math.cos(aa) * 0.8, math.sin(aa) * 0.8, 0.6)).normalized(), rng.uniform(0.25, 0.45), 0.008, 3, "SP_V_Grass", bend=0.6,
                  kink=0.5 if rng.random() < 0.3 else None)
            paint(K, i0, ROW_LEAF, H)
    for s in range(rng.randint(6, 10)):
        a = rng.uniform(0, 6.28); r = rng.uniform(0.6, 1.0)
        b = Vector((math.cos(a) * length * 0.55 * r, math.sin(a) * depth * 0.9 * r, 0.01))
        d = Vector((math.cos(a + rng.uniform(-1, 1)), math.sin(a + rng.uniform(-1, 1)), 0.08)).normalized()
        i0 = len(K.v)
        stem(K, rng, b, b + d * rng.uniform(0.4, 0.9), 0.004, 3, 2, "SP_V_Grass", wob=0.01)
        paint(K, i0, ROW_STRAW2, 1.0, 0.5, 0.8)
    return K, force


def bent_grass(name, seed, n=(60, 90), h=(0.3, 0.65), rad=0.12, width=0.011):
    """Tall grass bunch combed one way by the wind (+X), blades over-arching, a few seed stalks."""
    rng = random.Random(seed); K = Kit(name); force = {}
    wind = Vector((1, rng.uniform(-0.2, 0.2), 0)).normalized()
    for b in range(rng.randint(*n)):
        a = rng.uniform(0, 6.28)
        base = Vector((math.cos(a), math.sin(a), 0)) * rad * math.sqrt(rng.random())
        d = (UP + wind * rng.uniform(0.2, 0.7) + Vector((rng.uniform(-0.25, 0.25), rng.uniform(-0.25, 0.25), 0))).normalized()
        L = rng.uniform(*h)
        i0 = len(K.v)
        blade(K, rng, base, d, L, width * rng.uniform(0.7, 1.3), 4, "SP_V_Grass", bend_dir=wind, bend=rng.uniform(0.3, 0.9),
              kink=rng.uniform(0.45, 0.75) if rng.random() < 0.15 else None)
        paint(K, i0, [ROW_STRAW, ROW_STRAW, ROW_STRAW2, ROW_LEAF][rng.randrange(4)], L)
    for s in range(rng.randint(3, 7)):
        a = rng.uniform(0, 6.28); base = Vector((math.cos(a), math.sin(a), 0)) * rad * 0.5
        H = rng.uniform(h[1], h[1] * 1.5)
        i0 = len(K.v)
        pts = stem(K, rng, base, base + (UP + wind * 0.35).normalized() * H, 0.003, 3, 3, "SP_V_Grass")
        for f in range(3):
            blade(K, rng, pts[-1], (UP + wind * 0.8 + Vector((rng.uniform(-0.4, 0.4), rng.uniform(-0.4, 0.4), 0))).normalized(), 0.1, 0.008, 2, "SP_V_Grass", sway_pow=0.5)
        paint(K, i0, ROW_PLUME, H)
    return K, force


def seed_weed(name, seed, stems=(3, 6), h=(0.5, 0.95)):
    """Teasel/thistle-like weed: branching stems, a seed head at each tip, most with a snow cap."""
    rng = random.Random(seed); K = Kit(name); force = {}
    for s in range(rng.randint(*stems)):
        a = rng.uniform(0, 6.28); base = Vector((math.cos(a), math.sin(a), 0)) * rng.uniform(0, 0.12)
        H = rng.uniform(*h)
        tips = []
        i0 = len(K.v)
        branch2(K, rng, base, Vector((rng.uniform(-0.15, 0.15), rng.uniform(-0.15, 0.15), 1)), H, 0.009, 0, 2, "SP_V_Grass", 4, tips,
                child_n=(2, 3), spread=(25, 50), step=H * 0.3, wob=0.25, tropism=0.08, broken=0.1, child_len=(0.3, 0.5), seed=seed + s, rnd=rng.random())
        paint(K, i0, ROW_SEED if rng.random() < 0.6 else ROW_STRAW2, H)
        for p, d, depth, sw in tips:
            if rng.random() < 0.15: continue
            r = rng.uniform(0.03, 0.045) * (1.0 if depth == 0 else 0.75)
            i0 = len(K.v)
            K.blob(p + d * r * 0.8, (r, r, r * 1.5), "SP_V_Grass", subdiv=1, lumpy=0.35, seed=rng.randint(0, 99), rot=rot_to(d))
            for i in range(i0, len(K.v)): K.extra[i] = (sw, K.extra[i][1])
            paint(K, i0, ROW_SEED, 10.0, 0.2, 0.35)
            if rng.random() < 0.35:
                i0 = len(K.v)
                K.blob(p + d * r * 1.4 + Vector((0, 0, r * 0.7)), (r * 1.1, r * 1.0, r * 0.4), "SP_V_Grass", subdiv=1, lumpy=0.3, seed=rng.randint(0, 99))
                for i in range(i0, len(K.v)): K.extra[i] = (sw, K.extra[i][1]); force[i] = (1.0, 1.0)
    for g in range(rng.randint(5, 9)):
        aa = rng.uniform(0, 6.28); i0 = len(K.v)
        blade(K, rng, Vector((0, 0, 0)), Vector((math.cos(aa), math.sin(aa), 0.5)).normalized(), rng.uniform(0.12, 0.25), 0.015, 3, "SP_V_Grass", bend=0.6)
        paint(K, i0, ROW_LEAF, 0.3)
    return K, force


def shrub(name, seed, h, row, stems=(9, 13), spread_deg=40, maxdepth=3, child_n=(3, 4), lumps=(4, 7), flat=1.0, width=0.25, r=(0.008, 0.013), dome=False, cushion=True):
    """Twiggy dead shrub / low dome (dome=True: stems fan out into a hemisphere 10-30 cm). Palette row per stem."""
    rng = random.Random(seed); K = Kit(name); force = {}
    tips, forks = [], []
    for s in range(rng.randint(*stems)):
        a = rng.uniform(0, 2 * math.pi)
        tilt = rng.uniform(25, 75) if dome else rng.uniform(8, spread_deg)
        d = Vector((math.cos(a) * math.sin(math.radians(tilt)) * flat, math.sin(a) * math.sin(math.radians(tilt)) * flat, math.cos(math.radians(tilt))))
        base = Vector((math.cos(a), math.sin(a), 0)) * rng.uniform(0, width * 0.3) + Vector((0, 0, -0.03))
        i0 = len(K.v)
        branch2(K, rng, base, d, h * rng.uniform(0.7, 1.15) * (1.3 if dome else 1.0), rng.uniform(*r), 0, maxdepth, "SP_V_Brush", 3, tips,
                child_n=child_n, spread=(25, 65), step=h * (0.22 if dome else 0.16), wob=0.55, tropism=-0.04 if dome else 0.04, broken=0.25, child_len=(0.4, 0.7),
                seed=seed * 3 + s, rnd=rng.random(), forks=forks, ribbon_from=1)
        paint(K, i0, row if rng.random() < 0.75 else ROW_TWIG_GREY, h)
    if dome:
        for i, p in enumerate(K.v):
            rr = math.hypot(p.x, p.y)
            K.v[i] = Vector((p.x, p.y, min(p.z, h * math.sqrt(max(0.0, 1 - (rr / (h * 1.8)) ** 2)) + 0.02)))
        forks = [(Vector((p.x, p.y, min(p.z, h))), sw) for p, sw in forks]
    cand = [f for f in forks if f[0].z > h * 0.35]
    rng.shuffle(cand)
    for p, sw in cand[:rng.randint(*lumps)]:
        for i in snow_lump(K, rng, p + Vector((0, 0, 0.008)), h * rng.uniform(0.03, 0.05) * (1.6 if dome else 1.0), "SP_V_Brush", sway=sw): force[i] = (1.0, 0.9)
    if cushion:
        for i in snow_lump(K, rng, Vector((0, 0, -0.015)), width * 0.3, "SP_V_Brush", sway=0.0): force[i] = (1.0, 0.7)
    return K, force


def snowy_pine(name, seed, height, r0=0.4, limbs=(7, 9), pads=(3, 6)):
    """Red pine (적송) with a snow cushion on top of every needle pad (refs2 snowy_pines: white over, dark under)."""
    PADS.clear()
    K, _ = frame_pine(name, seed, height, r0, limbs, pads)
    rng = random.Random(seed + 1); force = {}
    # pn1: the separate cushion geometry read as flat white slabs; the snow is now shaded on the needle cards and limbs
    # (SPVegSurface _SnowClump: world-up x noise), so no extra geometry
    PADS.clear()
    for c, rad in list(PADS):
        # vg3: one big cushion per pad read as a grey rock slab; 2-4 small lumpy clumps instead
        for q in range(rng.randint(2, 4)):
            i0 = len(K.v); rr = rad * rng.uniform(0.2, 0.34); aa = rng.uniform(0, 6.28); dd = rad * rng.uniform(0.0, 0.45)
            K.blob(c + Vector((math.cos(aa) * dd, math.sin(aa) * dd, 0.2)), (rr * 1.3, rr, rr * 0.45), "SP_V_PineBark", subdiv=2, lumpy=0.55,
                   seed=rng.randint(0, 999), rot=Matrix.Rotation(rng.uniform(0, 6.28), 3, 'Z'))
            for i in range(i0, len(K.v)): K.extra[i] = (0.6, K.extra[i][1]); force[i] = (1.0, 1.0)
    PADS.clear()
    return K, force


def job(res, frost=1.0, bias=0.0, ao=0.6, fn=0.35):
    K, force = res
    ob = K.build(frost=frost, frost_bias=bias, ao_height=ao, seed=hash(K.name) % 97, frost_noise=fn)
    if force:
        ca = ob.data.color_attributes["Col"]
        for poly in ob.data.polygons:
            for li, vi in zip(poly.loop_indices, poly.vertices):
                if vi in force:
                    c = ca.data[li].color; r, g = force[vi]
                    ca.data[li].color = (r, g, c[2], c[3])
    JOBS.append(ob)
    REPORT[ob.name] = {"tris": tris(ob), "mats": [m.name for m in ob.data.materials],
                       "size": [round(ob.dimensions.x, 2), round(ob.dimensions.y, 2), round(ob.dimensions.z, 2)]}
    print("built", ob.name, REPORT[ob.name])
    return ob


# low dome shrubs 10-30 cm (red-brown / grey twigs, snow lumps on top)
job(shrub("SP_V_Dome_A", 401, 0.22, ROW_TWIG_RED, stems=(9, 11), dome=True, maxdepth=3, child_n=(3, 4), lumps=(5, 8), width=0.4), frost=0, ao=0.2)
job(shrub("SP_V_Dome_B", 402, 0.15, ROW_TWIG_GREY, stems=(10, 13), dome=True, maxdepth=2, child_n=(3, 4), lumps=(3, 6), width=0.3), frost=0, ao=0.15)
# twiggy shrubs and thickets
job(shrub("SP_V_Brush_A", 311, 0.55, ROW_TWIG_GREY, stems=(9, 11), lumps=(6, 10), r=(0.011, 0.016)), frost=0, ao=0.45)
job(shrub("SP_V_Brush_B", 312, 0.7, ROW_TWIG_RED, stems=(7, 9), spread_deg=30, lumps=(6, 10), width=0.2, r=(0.012, 0.017)), frost=0, ao=0.5)
job(shrub("SP_V_Thicket_A", 321, 1.15, ROW_TWIG_GREY, stems=(12, 14), spread_deg=38, maxdepth=4, child_n=(2, 3), lumps=(10, 14), width=0.45, r=(0.016, 0.024)), frost=0, ao=0.8)
job(shrub("SP_V_Thicket_B", 322, 1.6, ROW_TWIG_RED, stems=(14, 17), spread_deg=48, maxdepth=4, child_n=(2, 3), lumps=(12, 16), flat=1.4, width=1.0, r=(0.018, 0.028)), frost=0, ao=1.0)
# upright cattail / reed groups, reed walls
job(cattails("SP_V_Cattails_A", 411), frost=0, ao=0.4)
job(cattails("SP_V_Cattails_B", 412, n=(5, 8), h=(1.1, 1.5), spread=0.45), frost=0, ao=0.4)
job(cattails("SP_V_Cattails_C", 413, n=(3, 4), h=(0.9, 1.2), spread=0.25), frost=0, ao=0.3)
job(reed_wall("SP_V_ReedWall_A", 421), frost=0, ao=0.6)
job(reed_wall("SP_V_ReedWall_B", 422, length=1.8, depth=0.6, n=50, h=(1.0, 1.5)), frost=0, ao=0.6)
# wind-bent grass bunches
job(bent_grass("SP_V_BentGrass_A", 431), frost=0, ao=0.3)
job(bent_grass("SP_V_BentGrass_B", 432, n=(40, 55), h=(0.22, 0.4), rad=0.09), frost=0, ao=0.25)
job(bent_grass("SP_V_BentGrass_C", 433, n=(90, 120), h=(0.45, 0.85), rad=0.18, width=0.012), frost=0, ao=0.4)
# seed-head weeds with snow caps
job(seed_weed("SP_V_SeedWeed_A", 441), frost=0, ao=0.3)
job(seed_weed("SP_V_SeedWeed_B", 442, stems=(5, 8), h=(0.35, 0.7)), frost=0, ao=0.25)
# framing trees
job(frame_dead("SP_V_FrameDead_A", 341, 9.0), ao=2.5)
job(frame_dead("SP_V_FrameDead_B", 342, 7.5, r0=0.38, lean=0.25, children=(6, 8), broken=0.5), ao=2.2)
job(frame_dead("SP_V_FrameDead_C", 343, 11.0, r0=0.55, lean=0.08, children=(8, 11)), ao=3.0)
job(frame_dead("SP_V_FrameDead_D", 344, 6.0, r0=0.32, lean=0.35, children=(5, 7), broken=0.55), ao=2.0)
job(snowy_pine("SP_V_FramePine_A", 351, 10.0), ao=2.5)
job(snowy_pine("SP_V_FramePine_B", 352, 8.0, r0=0.34, limbs=(6, 8)), ao=2.2)
job(snowy_pine("SP_V_FramePine_C", 353, 12.0, r0=0.5, limbs=(8, 10), pads=(4, 6)), ao=3.0)

for ob in JOBS:
    export(ob, os.path.join(OUT, ob.name + ".fbx"))
with open(os.path.join(WORK, "veg_report.json"), "w") as f: json.dump(REPORT, f, indent=1)

if PREVIEW:
    # lineup + a 50 deg camera, workbench, for a quick shape check
    cols = {"SP_V_Brush": (0.32, 0.27, 0.22, 1), "SP_V_Grass": (0.6, 0.53, 0.35, 1), "SP_V_Bark": (0.15, 0.13, 0.12, 1),
            "SP_V_PineBark": (0.25, 0.15, 0.12, 1), "SP_V_Needles": (0.15, 0.22, 0.15, 1), "SP_V_Twigs": (0.2, 0.18, 0.16, 1)}
    for m in bpy.data.materials:
        m.diffuse_color = cols.get(m.name, (0.5, 0.5, 0.5, 1))
    small = [o for o in JOBS if o.dimensions.z < 2]
    big = [o for o in JOBS if o.dimensions.z >= 2]
    x = 0
    for o in small: o.location = (x, 0, 0); x += max(o.dimensions.x, 0.6) + 0.3
    x2 = 0
    for o in big: o.location = (x2, 7, 0); x2 += 8
    bpy.ops.mesh.primitive_plane_add(size=200, location=(0, 0, 0))
    plane = bpy.context.active_object
    pm = bpy.data.materials.new("snowplane"); pm.diffuse_color = (0.8, 0.82, 0.85, 1); plane.data.materials.append(pm)
    sc = bpy.context.scene
    try: sc.render.engine = 'BLENDER_EEVEE'
    except TypeError: sc.render.engine = 'BLENDER_EEVEE_NEXT'
    atlas = bpy.data.images.load(os.path.join(args[3], 'Textures', 'Veg', 'SP_V_CardAtlas.png')) if len(args) > 3 else None
    PAL = bpy.data.images.load(os.path.join(args[3], 'Textures', 'Veg', 'SP_V_StrawPalette.png')) if len(args) > 3 else None
    for m in bpy.data.materials:
        m.use_nodes = True
        bsdf = next(n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
        bsdf.inputs['Base Color'].default_value = m.diffuse_color
        if atlas and m.name in ('SP_V_Needles', 'SP_V_Twigs'):
            tx = m.node_tree.nodes.new('ShaderNodeTexImage'); tx.image = atlas
            m.node_tree.links.new(tx.outputs['Color'], bsdf.inputs['Base Color']); m.node_tree.links.new(tx.outputs['Alpha'], bsdf.inputs['Alpha'])
        elif atlas and m.name in ('SP_V_Brush', 'SP_V_Grass'):
            tx = m.node_tree.nodes.new('ShaderNodeTexImage'); tx.image = PAL; tx.interpolation = 'Closest'
            m.node_tree.links.new(tx.outputs['Color'], bsdf.inputs['Base Color'])
    sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN')); sc.collection.objects.link(sun); sun.rotation_euler = (math.radians(55), 0, math.radians(30)); sun.data.energy = 4
    sc.world = bpy.data.worlds.new('w'); sc.world.color = (0.4, 0.42, 0.45)
    sc.display.shading.light = 'STUDIO'; sc.display.shading.color_type = 'MATERIAL'
    sc.display.shading.show_shadows = True; sc.display.shading.show_cavity = True
    sc.render.resolution_x, sc.render.resolution_y = 1600, 900
    cam = bpy.data.cameras.new("c"); cam.lens_unit = 'FOV'; cam.angle = math.radians(30)
    co = bpy.data.objects.new("c", cam); sc.collection.objects.link(co); sc.camera = co
    for nm, loc, look in (("small", (x * 0.5, -14.0, 15.0), Vector((x * 0.5, 0.3, 0.2))), ("big", (x2 * 0.5 - 4, -26, 30), Vector((x2 * 0.5 - 4, 7, 3)))):
        co.location = loc
        co.rotation_euler = (look - Vector(loc)).to_track_quat('-Z', 'Y').to_euler()
        sc.render.filepath = os.path.join(WORK, "veg_preview_%s.png" % nm)
        bpy.ops.render.render(write_still=True)
print("DONE", len(JOBS))
