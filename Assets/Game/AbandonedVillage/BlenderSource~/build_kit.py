# Abandoned Joseon village kit. Builds every model with real geometry and exports one FBX per asset.
# blender -b --factory-startup -P build_kit.py -- <unity AbandonedVillage dir> <work dir> [only_name ...]
import bpy, sys, os, math, random
from mathutils import Vector, noise
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import avlib
from avlib import Mesh

args = sys.argv[sys.argv.index("--") + 1:]
OUT = os.path.join(args[0], "Models")
WORK = args[1]
ONLY = set(args[2:])
os.makedirs(OUT, exist_ok=True); os.makedirs(WORK, exist_ok=True)


def smooth(t): t = max(0.0, min(1.0, t)); return t * t * (3 - 2 * t)


def nz(x, y, z=0.0, s=1.0): return noise.noise(Vector((x * s, y * s, z)))


# ---------------------------------------------------------------- roofs
def tile_roof(M, W, D, zr, over=1.25, rise=0.6, seed=0, hole=0.0, hole_scale=0.33, mat="AV_Tile", under="AV_Wood",
              corner_up=0.3, step=0.085, bumps=True, ridge=True, gable=0.45):
    """팔작 roof: hip slopes below, gable (합각) above, concave slopes, gently raised corners and real 수키와 ridges.
    gable=0 gives a plain hip roof. Returns z(x, y) of the top surface."""
    Xe, Ye = W / 2 + over, D / 2 + over
    Ry = Ye
    H = Ry * rise
    zg = zr + gable * H
    tg = gable ** (1 / 1.35) if gable > 0 else 1.0
    Xg = Xe - tg * Ry if gable > 0 else -1.0
    if Xg < 0.3: Xg, gable = -1.0, 0.0
    yg = Ye - tg * Ry

    def base(x, y):
        ex, ey = Xe - abs(x), Ye - abs(y)
        front = ey <= ex or abs(x) <= Xg
        t = max(0.0, min(1.0, (ey if front else ex) / Ry))
        s = (abs(x) / Xe) ** 4 if front else (abs(y) / Ye) ** 4
        return zr + H * (t ** 1.35) + corner_up * (1 - t) ** 2 * s, t, front

    def gable_gap(x, y, st):
        return gable > 0 and abs(abs(x) - Xg) < st * 0.8 and abs(y) < Ye - tg * Ry + 0.02

    def top(x, y):
        z, t, front = base(x, y)
        if bumps:
            u = x if front else y
            f = (u / 0.30) % 1.0 - 0.5
            z += 0.065 * math.sqrt(max(0.0, 1 - (f * 2.4) ** 2))
            # tile courses: small steps down the slope
            v = (Ye - abs(y)) if front else (Xe - abs(x))
            z += 0.018 * ((v / 0.36) % 1.0)
        return z

    def holed(x, y):
        if hole <= 0: return True
        _, t, _ = base(x, y)
        if t < 0.12 or t > 0.9: return True
        n = nz(x, y, seed * 1.7, hole_scale) + 0.35 * nz(x, y, seed + 5, hole_scale * 3)
        return n < 1.0 - hole * 1.6

    nx = int(2 * Xe / step); ny = int(2 * Ye / step)
    nxu, nyu = max(8, nx // 4), max(8, ny // 4)
    M.grid(mat, -Xe, Xe, -Ye, Ye, nx, ny, top, keep=lambda x, y: holed(x, y) and not gable_gap(x, y, 2 * Xe / nx), smooth=True, uvs=0.5)
    M.grid(under, -Xe, Xe, -Ye, Ye, nxu, nyu, lambda x, y: base(x, y)[0] - 0.16,
           keep=lambda x, y: holed(x, y) and not gable_gap(x, y, 2 * Xe / nxu), smooth=True, uvs=0.5, flip=True)
    if gable > 0:
        # 합각 gable boards and 박공 barge ridges
        for sx in (-1, 1):
            xg = sx * Xg
            n = 16; prev = None
            for i in range(n + 1):
                y = -yg + 2 * yg * i / n
                zt_ = base(xg * 0.999, y)[0] + 0.08
                a = M.add_v((xg, y, zg - 0.22)); b = M.add_v((xg, y, zt_))
                if prev:
                    pa, pb, py = prev
                    f = [pa, a, b, pb] if sx > 0 else [pa, pb, b, a]
                    uv = [(py, 0), (y, 0), (y, 1), (py, 1)] if sx > 0 else [(py, 0), (py, 1), (y, 1), (y, 0)]
                    M.face(f, uv, "AV_Plank")
                prev = (a, b, y)
            pts = [(xg + sx * 0.08, -yg + 2 * yg * i / 12, base(xg * 0.999, -yg + 2 * yg * i / 12)[0] + 0.1) for i in range(13)]
            M.tube(mat, pts, [0.1] * len(pts), seg=5)
    # eave fascia around the edge
    n = 60
    edge = []
    for i in range(n): edge.append((-Xe + 2 * Xe * i / n, -Ye))
    for i in range(n): edge.append((Xe, -Ye + 2 * Ye * i / n))
    for i in range(n): edge.append((Xe - 2 * Xe * i / n, Ye))
    for i in range(n): edge.append((-Xe, Ye - 2 * Ye * i / n))
    edge.append(edge[0])
    for (x0, y0), (x1, y1) in zip(edge, edge[1:]):
        z0, z1 = base(x0, y0)[0], base(x1, y1)[0]
        a = M.add_v((x0, y0, z0 + 0.06)); b = M.add_v((x1, y1, z1 + 0.06)); c = M.add_v((x1, y1, z1 - 0.17)); d = M.add_v((x0, y0, z0 - 0.17))
        L = math.hypot(x1 - x0, y1 - y0)
        M.face([a, d, c, b], [(0, 0), (0, 0.23), (L, 0.23), (L, 0)], mat)
    if ridge:
        rl = 2 * Xg if gable > 0 else max(0.4, W - D)
        zt = zr + H + 0.06
        pts = [(-rl / 2 - 0.35, 0, zt + 0.3), (-rl / 2, 0, zt + 0.14)] + [(-rl / 2 + rl * i / 6, 0, zt + 0.1) for i in range(1, 6)] + [(rl / 2, 0, zt + 0.14), (rl / 2 + 0.35, 0, zt + 0.3)]
        M.tube(mat, pts, [0.17] * len(pts), seg=6)
        # hip ridges (추녀마루) from the gable foot (or ridge end) to the corners
        for sx in (-1, 1):
            for sy in (-1, 1):
                pts = []
                x0, y0 = (Xg, yg) if gable > 0 else (rl / 2, 0)
                for i in range(7):
                    k = i / 6
                    x = sx * (x0 + (Xe - 0.15 - x0) * k); y = sy * (y0 + (Ye - 0.15 - y0) * k)
                    pts.append((x, y, base(x, y)[0] + 0.12))
                M.tube(mat, pts, [0.11] * len(pts), seg=5)
    return lambda x, y: base(x, y)[0]


def thatch_roof(M, W, D, zr, over=0.7, H=1.7, seed=0, hole=0.0, collapse=0.0, mat="AV_Thatch"):
    Xe, Ye = W / 2 + over, D / 2 + over
    p = 3.2

    def dist(x, y): return ((abs(x) / Xe) ** p + (abs(y) / Ye) ** p) ** (1 / p)

    def caved(x, y):
        if collapse <= 0: return 0.0
        n = smooth((nz(x, y, seed, 0.28) + 0.2) * 2.2)
        return collapse * n

    def top(x, y):
        d = dist(x, y)
        if d > 1.0: return None
        z = zr + H * (1 - d ** 2.3) + 0.3 * smooth((1 - d) * 6)  # thick rounded eave
        z += 0.05 * nz(x, y, seed, 2.5) + 0.02 * nz(x, y, seed, 9)
        return z - caved(x, y) * (1 - d) * 2.2

    def bot(x, y):
        d = dist(x, y)
        if d > 1.0: return None
        return zr - 0.05 + (H - 0.5) * (1 - d ** 2.3) - caved(x, y) * (1 - d) * 2.2

    def keep(x, y):
        if hole <= 0: return True
        d = dist(x, y)
        if d > 0.85 or d < 0.15: return True
        return nz(x, y, seed + 3, 0.5) < 1 - hole * 1.8

    n = 70
    M.grid(mat, -Xe, Xe, -Ye, Ye, n, int(n * Ye / Xe), top, keep=keep, uvs=0.6)
    M.grid(mat, -Xe, Xe, -Ye, Ye, 24, int(24 * Ye / Xe), bot, keep=keep, uvs=0.6, flip=True)
    # closing skirt along the rim
    ring = []
    for i in range(96):
        a = 2 * math.pi * i / 96
        c, s = math.cos(a), math.sin(a)
        r = 1.0 / ((abs(c) ** p + abs(s) ** p) ** (1 / p))
        ring.append((c * r * Xe * 0.995, s * r * Ye * 0.995))
    ring.append(ring[0])
    for (x0, y0), (x1, y1) in zip(ring, ring[1:]):
        t0, t1, b0, b1 = top(x0, y0), top(x1, y1), bot(x0, y0), bot(x1, y1)
        if None in (t0, t1, b0, b1): continue
        a = M.add_v((x0, y0, t0)); b = M.add_v((x1, y1, t1)); c = M.add_v((x1, y1, b1)); d = M.add_v((x0, y0, b0))
        M.face([a, d, c, b], [(x0, 0), (x0, 0.5), (x1, 0.5), (x1, 0)], mat, True)
    # 새끼줄 rope net and ridge roll
    for i in range(-6, 7):
        x = i * Xe / 7
        pts = [(x, -Ye + Ye * 2 * k / 20, 0) for k in range(21)]
        pts = [(px, py, (top(px, py) or zr) + 0.03) for px, py, _ in pts if top(px, py) is not None]
        if len(pts) > 3: M.tube("AV_Rope", pts, [0.012] * len(pts), seg=4)
    for j in range(-3, 4):
        y = j * Ye / 4
        pts = [(-Xe + Xe * 2 * k / 24, y, 0) for k in range(25)]
        pts = [(px, py, (top(px, py) or zr) + 0.03) for px, py, _ in pts if top(px, py) is not None]
        if len(pts) > 3: M.tube("AV_Rope", pts, [0.012] * len(pts), seg=4)
    pts = [(x, 0, top(x, 0) + 0.08) for x in [(-Xe * 0.75) + (Xe * 1.5) * k / 10 for k in range(11)]]
    M.tube(mat, pts, [0.2] * len(pts), seg=8)
    return top


# ---------------------------------------------------------------- walls and openings
def lattice_door(M, x0, x1, z0, z1, y, torn=0.4, glow=False, seed=0):
    """띠살문: frame, muntins and paper with torn cells. Plane at y, spanning x0..x1."""
    rnd = random.Random(seed)
    w = 0.06
    M.box("AV_Wood", ((x0 + x1) / 2, y, z0 + w / 2), (x1 - x0, 0.07, w))
    M.box("AV_Wood", ((x0 + x1) / 2, y, z1 - w / 2), (x1 - x0, 0.07, w))
    M.box("AV_Wood", (x0 + w / 2, y, (z0 + z1) / 2), (w, 0.07, z1 - z0))
    M.box("AV_Wood", (x1 - w / 2, y, (z0 + z1) / 2), (w, 0.07, z1 - z0))
    nx = max(2, int((x1 - x0) / 0.22)); nz_ = max(3, int((z1 - z0) / 0.3))
    for i in range(1, nx):
        x = x0 + (x1 - x0) * i / nx
        M.box("AV_Wood", (x, y, (z0 + z1) / 2), (0.025, 0.04, z1 - z0 - 2 * w))
    for j in range(1, nz_):
        z = z0 + (z1 - z0) * j / nz_
        if rnd.random() < 0.08: continue
        M.box("AV_Wood", ((x0 + x1) / 2, y, z), (x1 - x0 - 2 * w, 0.04, 0.025))
    paper = "AV_PaperGlow" if glow else "AV_Paper"
    for i in range(nx):
        for j in range(nz_):
            cx0 = x0 + (x1 - x0) * i / nx; cx1 = x0 + (x1 - x0) * (i + 1) / nx
            cz0 = z0 + (z1 - z0) * j / nz_; cz1 = z0 + (z1 - z0) * (j + 1) / nz_
            if nz((cx0 + cx1) / 2, (cz0 + cz1) / 2, seed * 0.37, 2.2) + rnd.uniform(-0.12, 0.12) > 0.3 - torn * 0.9: continue
            a = M.add_v((cx0, y + 0.03, cz0)); b = M.add_v((cx1, y + 0.03, cz0)); c = M.add_v((cx1, y + 0.03, cz1)); d = M.add_v((cx0, y + 0.03, cz1))
            M.face([a, b, c, d], [(cx0, cz0), (cx1, cz0), (cx1, cz1), (cx0, cz1)], paper)
            M.face([a, d, c, b], [(cx0, cz0), (cx0, cz1), (cx1, cz1), (cx1, cz0)], paper)


def plank_wall(M, x0, x1, z0, z1, y, seed=0, missing=0.12, mat="AV_Plank", horiz=False):
    rnd = random.Random(seed)
    if horiz:
        z = z0
        while z < z1 - 0.05:
            h = rnd.uniform(0.18, 0.26); h = min(h, z1 - z)
            if rnd.random() > missing:
                M.box(mat, ((x0 + x1) / 2, y + rnd.uniform(-0.015, 0.015), z + h / 2), (x1 - x0, 0.045, h - 0.012), rot=(0, rnd.uniform(-1.2, 1.2), 0))
            z += h
        return
    x = x0
    while x < x1 - 0.05:
        bw = rnd.uniform(0.18, 0.28); bw = min(bw, x1 - x)
        if rnd.random() > missing:
            top = z1 if rnd.random() > 0.15 else rnd.uniform(z0 + (z1 - z0) * 0.4, z1)
            M.box(mat, (x + bw / 2, y + rnd.uniform(-0.02, 0.02), (z0 + top) / 2), (bw - 0.015, 0.045, top - z0), rot=(rnd.uniform(-1, 1), 0, rnd.uniform(-1.5, 1.5)))
        x += bw


def mud_panel(M, x0, x1, z0, z1, y, seed=0, hole=0.0):
    """흙벽 panel; a hole shows the woven lath inside."""
    rnd = random.Random(seed)
    if hole <= 0 or rnd.random() > hole:
        M.box("AV_MudWall", ((x0 + x1) / 2, y, (z0 + z1) / 2), (x1 - x0, 0.16, z1 - z0), jitter=0.01)
        return
    hz = rnd.uniform(z0 + 0.3, z1 - 0.8)
    M.box("AV_MudWall", ((x0 + x1) / 2, y, (z0 + hz) / 2), (x1 - x0, 0.16, hz - z0), jitter=0.02)
    M.box("AV_MudWall", ((x0 + x1) / 2, y, z1 - 0.15), (x1 - x0, 0.16, 0.3), jitter=0.02)
    for i in range(int((x1 - x0) / 0.12)):
        x = x0 + 0.06 + i * 0.12
        M.beam("AV_Twig", (x, y, hz - 0.05), (x + rnd.uniform(-0.05, 0.05), y, z1 - 0.3), 0.025)
    for z in (hz + 0.25, hz + 0.55):
        if z < z1 - 0.35: M.beam("AV_Twig", (x0, y, z), (x1, y, z), 0.025)


def railing(M, pts, z0, h=0.95, seed=0, broken=0.2):
    rnd = random.Random(seed)
    for (ax, ay), (bx, by) in zip(pts, pts[1:]):
        L = math.hypot(bx - ax, by - ay); n = max(1, int(L / 1.3))
        for i in range(n + 1):
            t = i / n
            M.box("AV_Wood", (ax + (bx - ax) * t, ay + (by - ay) * t, z0 + h / 2), (0.1, 0.1, h))
        segs = 10
        for i in range(segs):
            t0, t1 = i / segs, (i + 1) / segs
            if rnd.random() < broken: continue
            p0 = (ax + (bx - ax) * t0, ay + (by - ay) * t0); p1 = (ax + (bx - ax) * t1, ay + (by - ay) * t1)
            M.beam("AV_Wood", (p0[0], p0[1], z0 + h), (p1[0], p1[1], z0 + h + rnd.uniform(-0.03, 0.03)), 0.08)
            M.beam("AV_Wood", (p0[0], p0[1], z0 + 0.35), (p1[0], p1[1], z0 + 0.35), 0.05)
            # 난간 balusters
            for k in range(3):
                if rnd.random() < broken * 0.5: continue
                t = t0 + (t1 - t0) * (k + 0.5) / 3
                M.box("AV_Wood", (ax + (bx - ax) * t, ay + (by - ay) * t, z0 + 0.35 + (h - 0.35) / 2), (0.035, 0.035, h - 0.35))


# ---------------------------------------------------------------- buildings
def hanok2f(name, W=8.4, D=5.6, seed=1, lean=0.0, sag=0.0, hole=0.25, glow_bays=(), roof="tile"):
    """Two-storey wooden house with a front balcony, lattice doors, plank and mud walls."""
    random.seed(seed); rnd = random.Random(seed)
    M = Mesh(name)
    # 기단: stacked stone blocks
    bw, bd, bh = W + 1.4, D + 1.4, 0.5
    M.box("AV_Stone", (0, 0, bh / 2 - 0.05), (bw - 0.2, bd - 0.2, bh - 0.1))
    for side in (-1, 1):
        x = -bw / 2
        while x < bw / 2:
            L = rnd.uniform(0.5, 1.1); L = min(L, bw / 2 - x)
            M.box("AV_Stone", (x + L / 2, side * bd / 2, bh / 2), (L - 0.03, 0.3, bh), rot=(rnd.uniform(-2, 2), 0, rnd.uniform(-2, 2)), jitter=0.03)
            x += L
    for side in (-1, 1):
        y = -bd / 2 + 0.15
        while y < bd / 2 - 0.15:
            L = rnd.uniform(0.5, 1.1); L = min(L, bd / 2 - 0.15 - y)
            M.box("AV_Stone", (side * bw / 2, y + L / 2, bh / 2), (0.3, L - 0.03, bh), jitter=0.03)
            y += L
    # steps up to the 기단 in front
    for i in range(2):
        M.box("AV_Stone", (0, -bd / 2 - 0.35 - i * 0.3, (2 - i) * bh / 3 - bh / 6), (1.8, 0.35, (2 - i) * bh / 3), jitter=0.02)
    z0 = bh + 0.12
    nx = max(2, round(W / 2.4)); ny = max(1, round(D / 2.6))
    xs = [-W / 2 + W * i / nx for i in range(nx + 1)]
    ys = [-D / 2 + D * j / ny for j in range(ny + 1)]
    z1 = z0 + 3.0; z2 = z1 + 2.8
    perim = [(x, ys[0]) for x in xs] + [(x, ys[-1]) for x in xs] + [(xs[0], y) for y in ys[1:-1]] + [(xs[-1], y) for y in ys[1:-1]]
    for (x, y) in perim:
        M.box("AV_Stone", (x, y, bh + 0.06), (0.45, 0.45, 0.22), jitter=0.04)
        M.box("AV_Wood", (x, y, (z0 + z2) / 2), (0.26, 0.26, z2 - z0), jitter=0.01)
    # 2F floor and top beams
    for zz, s in ((z1, 0.24), (z2, 0.28), (z0 + 0.3, 0.16)):
        for y in (ys[0], ys[-1]): M.box("AV_Wood", (0, y, zz), (W + 0.4, 0.22, s))
        for x in (xs[0], xs[-1]): M.box("AV_Wood", (x, 0, zz), (0.22, D + 0.4, s))
    for x in xs[1:-1]: M.box("AV_Wood", (x, 0, z2 + 0.08), (0.22, D + 0.8, 0.26))
    # 1F bays
    yf, yb = ys[0], ys[-1]
    for i in range(nx):
        a, b = xs[i] + 0.13, xs[i + 1] - 0.13
        kind = rnd.choice(["door", "door", "plank", "mud"]) if i not in (nx // 2,) else "door"
        if kind == "door":
            mid = (a + b) / 2
            lattice_door(M, a, mid, z0 + 0.38, z1 - 0.35, yf, torn=rnd.uniform(0.25, 0.6), glow=(i in glow_bays), seed=seed * 10 + i)
            if rnd.random() < 0.6: lattice_door(M, mid, b, z0 + 0.38, z1 - 0.35, yf, torn=rnd.uniform(0.3, 0.7), glow=(i in glow_bays), seed=seed * 10 + i + 50)
            M.box("AV_Plank", ((a + b) / 2, yf, z1 - 0.18), (b - a, 0.06, 0.3))
        elif kind == "plank":
            plank_wall(M, a, b, z0 + 0.38, z1 - 0.12, yf, seed=seed * 7 + i, missing=0.2)
        else:
            mud_panel(M, a, b, z0 + 0.38, z1 - 0.12, yf, seed=seed * 5 + i, hole=0.7)
        plank_wall(M, a, b, z0 + 0.25, z1 - 0.12, yb, seed=seed * 3 + i, missing=0.1, horiz=True)
    # side walls (rotated planks): build in a temp mesh and rotate into place
    for x, sgn in ((xs[0], -1), (xs[-1], 1)):
        start = len(M.v)
        for j in range(ny):
            a, b = ys[j] + 0.13, ys[j + 1] - 0.13
            plank_wall(M, a, b, z0 + 0.25, z1 - 0.12, 0, seed=seed * 11 + j + int(x), missing=0.12, horiz=rnd.random() < 0.5)
            plank_wall(M, a, b, z1 + 0.15, z2 - 0.15, 0, seed=seed * 13 + j + int(x), missing=0.18)
        M.transform(lambda p, x=x: (x - p.y, p.x, p.z), start)
    # 2F front: set-back wall with windows, balcony on brackets
    for i in range(nx):
        a, b = xs[i] + 0.13, xs[i + 1] - 0.13
        mid = (a + b) / 2
        plank_wall(M, a, b, z1 + 0.15, z2 - 0.15, yf, seed=seed * 17 + i, missing=0.1)
        if rnd.random() < 0.7:
            lattice_door(M, mid - 0.55, mid + 0.55, z1 + 0.7, z2 - 0.45, yf - 0.05, torn=0.5, glow=(i in glow_bays), seed=seed * 19 + i)
        plank_wall(M, a, b, z1 + 0.15, z2 - 0.15, yb, seed=seed * 23 + i, missing=0.1, horiz=True)
    by = yf - 1.0
    for x in xs:
        M.beam("AV_Wood", (x, yf, z1 - 0.5), (x, by + 0.05, z1 - 0.05), 0.14)
        M.beam("AV_Wood", (x, yf, z1), (x, by - 0.05, z1), 0.18)
    k = W + 0.2; x = -k / 2
    while x < k / 2:
        bw_ = rnd.uniform(0.2, 0.3)
        if rnd.random() > 0.1:
            M.box("AV_Plank", (x + bw_ / 2, (yf + by) / 2, z1 + 0.12), (bw_ - 0.02, abs(by - yf) + 0.1, 0.06), rot=(rnd.uniform(-2, 2), rnd.uniform(-1, 1), 0))
        x += bw_
    railing(M, [(-W / 2 - 0.1, by), (W / 2 + 0.1, by)], z1 + 0.15, seed=seed, broken=0.25)
    # rafters 서까래 under the eaves (front and back)
    over = 1.3
    rf = tile_roof if roof == "tile" else None
    zr = z2 + 0.3
    zf = tile_roof(M, W, D, zr, over=over, rise=0.62, seed=seed, hole=hole)
    for sy in (-1, 1):
        x = -W / 2 - 0.4
        while x < W / 2 + 0.4:
            yend = sy * (D / 2 + over - 0.15)
            M.tube("AV_Wood", [(x, sy * (D / 2 - 0.3), z2 + 0.25), (x, yend, zf(x, yend) - 0.2)], [0.06, 0.055], seg=6)
            x += 0.36
    # inner floor of the 2F so holes in the roof show depth
    M.box("AV_Plank", (0, 0, z1 + 0.1), (W, D, 0.06))
    # deform: sag the middle and lean the whole frame
    def deform(p):
        h = max(0.0, p.z - z0)
        x = p.x + lean * h / 8.0
        mid = 1 - min(1.0, (2 * p.x / (W + 2 * over)) ** 2)
        z = p.z - sag * mid * smooth(h / 7.0)
        return (x, p.y, z)
    M.transform(deform)
    return M


def choga(name, W=6.8, D=4.2, seed=2, hole=0.2, collapse=0.0, lean=0.0, glow_bays=()):
    rnd = random.Random(seed); random.seed(seed)
    M = Mesh(name)
    bh = 0.35
    M.box("AV_Stone", (0, 0, bh / 2), (W + 0.8, D + 0.8, bh), jitter=0.03)
    for i in range(int((W + 0.8) / 0.6)):
        x = -(W + 0.8) / 2 + 0.3 + i * 0.6
        M.box("AV_Stone", (x, -(D + 0.8) / 2, bh / 2), (0.55, 0.25, bh + 0.04), jitter=0.04)
    z0 = bh + 0.08
    nx = max(2, round(W / 2.2)); xs = [-W / 2 + W * i / nx for i in range(nx + 1)]
    ys = [-D / 2, D / 2]
    zt = z0 + 2.25
    for x in xs:
        for y in ys:
            M.box("AV_Stone", (x, y, bh + 0.05), (0.35, 0.35, 0.16), jitter=0.04)
            M.tube("AV_Wood", [(x, y, z0), (x + rnd.uniform(-0.03, 0.03), y, zt)], [0.12, 0.11], seg=7)
    for y in ys: M.tube("AV_Wood", [(xs[0] - 0.3, y, zt), (0, y, zt + 0.03), (xs[-1] + 0.3, y, zt)], [0.13] * 3, seg=7)
    for x in (xs[0], xs[-1]): M.tube("AV_Wood", [(x, -D / 2 - 0.3, zt), (x, D / 2 + 0.3, zt)], [0.11] * 2, seg=7)
    for i in range(nx):
        a, b = xs[i] + 0.12, xs[i + 1] - 0.12
        if i == nx // 2 or (i in glow_bays):
            lattice_door(M, a + 0.15, b - 0.15, z0 + 0.25, zt - 0.3, ys[0], torn=rnd.uniform(0.3, 0.7), glow=(i in glow_bays), seed=seed * 3 + i)
            M.box("AV_MudWall", ((a + b) / 2, ys[0], zt - 0.15), (b - a, 0.16, 0.3))
            M.box("AV_MudWall", (a + 0.075, ys[0], (z0 + zt) / 2), (0.15, 0.16, zt - z0))
            M.box("AV_MudWall", (b - 0.075, ys[0], (z0 + zt) / 2), (0.15, 0.16, zt - z0))
        else:
            mud_panel(M, a, b, z0, zt - 0.06, ys[0], seed=seed * 7 + i, hole=0.6)
        mud_panel(M, a, b, z0, zt - 0.06, ys[1], seed=seed * 9 + i, hole=0.3)
    for x in (xs[0], xs[-1]):
        start = len(M.v)
        mud_panel(M, -D / 2 + 0.12, D / 2 - 0.12, z0, zt - 0.06, 0, seed=seed + int(x * 10), hole=0.5)
        M.transform(lambda p, x=x: (x - p.y, p.x, p.z), start)
    # 툇마루 wooden porch
    k = -W / 2
    while k < W / 2:
        w = rnd.uniform(0.22, 0.3)
        if rnd.random() > 0.15: M.box("AV_Plank", (k + w / 2, ys[0] - 0.45, z0 + 0.12), (w - 0.02, 0.8, 0.06), rot=(rnd.uniform(-3, 3), 0, 0))
        k += w
    thatch_roof(M, W, D, zt + 0.1, over=0.75, H=1.75, seed=seed, hole=hole, collapse=collapse)
    M.transform(lambda p: (p.x + lean * max(0, p.z) / 6, p.y, p.z))
    return M


def watchtower(name, seed=3):
    rnd = random.Random(seed); random.seed(seed)
    M = Mesh(name)
    zp = 7.0
    legs = []
    for sx in (-1, 1):
        for sy in (-1, 1):
            a = Vector((sx * 1.7, sy * 1.7, 0)); b = Vector((sx * 1.25, sy * 1.25, zp + 2.6))
            legs.append((a, b))
            M.box("AV_Stone", (a.x, a.y, 0.15), (0.6, 0.6, 0.35), jitter=0.05)
            M.beam("AV_Wood", a, b, 0.26, jitter=0.01)
    def at(i, z):
        a, b = legs[i]; t = z / (zp + 2.6); return a.lerp(b, t)
    sides = [(0, 1), (1, 3), (3, 2), (2, 0)]
    levels = [0.5, 3.7, zp - 0.15]
    for i, j in sides:
        for k in range(2):
            za, zb = levels[k], levels[k + 1]
            if rnd.random() < 0.12 and k == 0: continue
            M.beam("AV_Wood", at(i, za), at(j, zb), 0.14)
            if not (k == 1 and (i, j) == (0, 1) and rnd.random() < 0.5):
                M.beam("AV_Wood", at(j, za), at(i, zb), 0.14)
        for z in levels[1:]:
            M.beam("AV_Wood", at(i, z), at(j, z), 0.18)
    # platform
    x = -2.0
    while x < 2.0:
        w = rnd.uniform(0.2, 0.28)
        if rnd.random() > 0.1:
            M.box("AV_Plank", (x + w / 2, 0, zp + 0.05), (w - 0.02, 4.0, 0.07), rot=(rnd.uniform(-1.5, 1.5), 0, 0))
        x += w
    for y in (-2.0, 2.0): M.box("AV_Wood", (0, y, zp - 0.08), (4.2, 0.2, 0.2))
    railing(M, [(-2.0, -2.0), (2.0, -2.0), (2.0, 2.0), (-2.0, 2.0), (-2.0, -2.0)], zp + 0.08, h=1.0, seed=seed, broken=0.22)
    for sx in (-1, 1):
        for sy in (-1, 1):
            M.box("AV_Wood", (sx * 1.6, sy * 1.6, zp + 1.3), (0.16, 0.16, 2.6))
    for sy in (-1, 1): M.box("AV_Wood", (0, sy * 1.6, zp + 2.55), (3.6, 0.18, 0.18))
    for sx in (-1, 1): M.box("AV_Wood", (sx * 1.6, 0, zp + 2.55), (0.18, 3.6, 0.18))
    start = len(M.v)
    tile_roof(M, 3.2, 3.2, zp + 2.75, over=1.0, rise=0.7, seed=seed, hole=0.18, corner_up=0.45)
    # ladder
    a0, a1 = Vector((0.35, -3.6, 0)), Vector((0.35, -2.05, zp + 0.1))
    for dx in (-0.25, 0.25):
        M.beam("AV_Wood", a0 + Vector((dx, 0, 0)), a1 + Vector((dx, 0, 0)), 0.07)
    for i in range(1, 22):
        if i in (9, 15): continue
        p = a0.lerp(a1, i / 22)
        M.beam("AV_Wood", p + Vector((-0.27, 0, 0)), p + Vector((0.27, 0, 0)), 0.045)
    M.transform(lambda p: (p.x + 0.025 * p.z, p.y, p.z))
    return M


# ---------------------------------------------------------------- fences, walls, steps
def ssari_fence(name, L=3.0, seed=4, broken=0.0):
    rnd = random.Random(seed); M = Mesh(name)
    for x in (-L / 2, L / 2):
        M.tube("AV_Wood", [(x, 0, -0.1), (x + rnd.uniform(-0.05, 0.05), 0, 1.35)], [0.06, 0.05], seg=6)
    for z in (0.35, 0.95):
        M.tube("AV_Twig", [(-L / 2, 0.04, z), (0, 0.05, z + rnd.uniform(-0.03, 0.03)), (L / 2, 0.04, z)], [0.03] * 3, seg=5)
    n = int(L / 0.045)
    for i in range(n):
        x = -L / 2 + L * (i + 0.5) / n + rnd.uniform(-0.01, 0.01)
        if rnd.random() < 0.08 + broken * 0.4: continue
        h = rnd.uniform(1.05, 1.45)
        if rnd.random() < broken * 0.5: h *= rnd.uniform(0.3, 0.7)
        lean = rnd.uniform(-0.06, 0.06)
        M.tube("AV_Twig", [(x, 0, -0.05), (x + lean * 0.5, rnd.uniform(-0.02, 0.02), h * 0.5), (x + lean, rnd.uniform(-0.03, 0.03), h)], [0.014, 0.012, 0.006], seg=4, cap=False)
    if broken > 0:
        mid = len(M.v) // 2
        M.transform(lambda p: (p.x, p.y + max(0.0, p.x) / L * 0.4 * p.z, p.z * (1 - broken * 0.6 * max(0.0, p.x) / (L / 2))))
    return M


def plank_fence(name, L=3.0, seed=5, broken=0.0):
    rnd = random.Random(seed); M = Mesh(name)
    for x in (-L / 2, L / 2):
        M.box("AV_Wood", (x, 0.06, 0.8), (0.14, 0.14, 1.8), rot=(rnd.uniform(-3, 3), rnd.uniform(-3, 3), 0))
    for z in (0.4, 1.3): M.box("AV_Wood", (0, 0.06, z), (L, 0.07, 0.1), rot=(0, rnd.uniform(-1.5, 1.5), 0))
    x = -L / 2
    while x < L / 2 - 0.05:
        w = rnd.uniform(0.14, 0.24)
        if rnd.random() > 0.1 + broken * 0.3:
            h = rnd.uniform(1.45, 1.8) * (rnd.uniform(0.4, 0.8) if rnd.random() < broken else 1)
            tilt = rnd.uniform(-4, 4) + (rnd.uniform(5, 18) if rnd.random() < broken else 0)
            M.box("AV_Plank", (x + w / 2, 0, h / 2), (w - 0.025, 0.035, h), rot=(0, tilt, rnd.uniform(-2, 2)))
        x += w
    return M


def stone_wall(name, L=4.0, H=1.4, seed=6, broken=0.0, cap=True, big=False):
    """돌담: coursed field stones in mud, tile coping on top."""
    rnd = random.Random(seed); M = Mesh(name)
    T = 0.7 if not big else 1.1
    s0 = 0.28 if not big else 0.5
    M.box("AV_MudWall", (0, 0, H / 2 - 0.05), (L - 0.1, T - 0.2, H - 0.1))
    def hmax(x):
        if broken <= 0: return H
        return H * (1 - broken * smooth((x / L + 0.5 - 0.35) * 3))
    z = 0
    row = 0
    while z < H - 0.05:
        sh = rnd.uniform(0.8, 1.2) * s0 * 0.75
        x = -L / 2 + (s0 * 0.5 if row % 2 else 0)
        while x < L / 2:
            sw = rnd.uniform(0.8, 1.6) * s0; sw = min(sw, L / 2 - x)
            if sw < 0.08: break
            cx = x + sw / 2
            if z + sh * 0.5 < hmax(cx):
                for side in (-1, 1):
                    M.box("AV_StoneWall", (cx, side * (T / 2 - 0.1), z + sh / 2), (sw - 0.03, rnd.uniform(0.22, 0.32), sh - 0.02),
                          rot=(rnd.uniform(-4, 4), rnd.uniform(-4, 4), rnd.uniform(-4, 4)), jitter=0.03)
            x += sw
        z += sh; row += 1
    if cap and broken < 0.2:
        start = len(M.v)
        tile_roof(M, L - 0.4, 0.2, 0, over=0.35, rise=1.1, seed=seed, corner_up=0.08, step=0.09, ridge=True, gable=0)
        M.transform(lambda p: (p.x, p.y, p.z + H), start)
    for i in range(int(broken * 14)):
        x = rnd.uniform(0, L / 2); y = rnd.uniform(-1.2, 1.2)
        s = rnd.uniform(0.6, 1.3) * s0
        M.box("AV_StoneWall", (x, y, s * 0.3), (s, s * 0.8, s * 0.6), rot=(rnd.uniform(-20, 20), rnd.uniform(-20, 20), rnd.uniform(0, 180)), jitter=0.04)
    return M


def stone_steps(name, W=4.2, n=8, rise=0.32, run=0.45, seed=7):
    rnd = random.Random(seed); M = Mesh(name)
    for i in range(n):
        z = (i + 1) * rise; y = i * run
        x = -W / 2
        while x < W / 2 - 0.05:
            w = rnd.uniform(0.9, 1.6); w = min(w, W / 2 - x)
            M.box("AV_Stone", (x + w / 2, y + run / 2, z - rise / 2 - 0.15), (w - 0.03, run + 0.12, rise + 0.3),
                  rot=(rnd.uniform(-1.5, 1.5), rnd.uniform(-2, 2), rnd.uniform(-2, 2)), jitter=0.035)
            x += w
    for sx in (-1, 1):
        for i in range(n):
            z = (i + 1) * rise; y = i * run
            M.box("AV_StoneWall", (sx * (W / 2 + 0.35), y + run / 2, z / 2 + 0.1), (0.7, run + 0.05, z + 0.2), jitter=0.04)
    return M


# ---------------------------------------------------------------- props
def stone_lantern(name, seed=8):
    M = Mesh(name)
    M.lathe("AV_Stone", (0, 0, 0), [(0.0, 0.0), (0.42, 0.0), (0.42, 0.18), (0.3, 0.26), (0.0, 0.26)], seg=8, smooth=False)
    M.lathe("AV_Stone", (0, 0, 0), [(0.13, 0.25), (0.11, 0.95), (0.13, 1.0)], seg=8)
    M.lathe("AV_Stone", (0, 0, 0), [(0.0, 1.0), (0.32, 1.0), (0.34, 1.12), (0.0, 1.12)], seg=8, smooth=False)
    for k in range(4):
        a = math.pi / 4 + k * math.pi / 2
        M.box("AV_Stone", (math.cos(a) * 0.2, math.sin(a) * 0.2, 1.36), (0.11, 0.11, 0.48), rot=(0, 0, math.degrees(a)))
    M.lathe("AV_Glow", (0, 0, 0), [(0.0, 1.16), (0.12, 1.2), (0.13, 1.36), (0.08, 1.52), (0.0, 1.55)], seg=8)
    M.lathe("AV_Stone", (0, 0, 0), [(0.0, 1.6), (0.52, 1.6), (0.55, 1.66), (0.3, 1.86), (0.12, 1.92), (0.0, 1.93)], seg=8, smooth=False)
    M.lathe("AV_Stone", (0, 0, 0), [(0.0, 1.92), (0.1, 1.94), (0.12, 2.04), (0.05, 2.14), (0.0, 2.16)], seg=8)
    return M


def hanging_lantern(name):
    """등롱: paper body with wooden caps and hook."""
    M = Mesh(name)
    prof = [(0.0, -0.32), (0.1, -0.31), (0.17, -0.22), (0.2, -0.1), (0.2, 0.06), (0.17, 0.18), (0.1, 0.27), (0.0, 0.28)]
    M.lathe("AV_PaperGlow", (0, 0, 0), prof, seg=14)
    for z in (-0.33, 0.27):
        M.lathe("AV_Wood", (0, 0, 0), [(0.0, z), (0.12, z), (0.12, z + 0.06), (0.0, z + 0.06)], seg=10, smooth=False)
    for k in range(7):
        a = 2 * math.pi * k / 7
        pts = [(math.cos(a) * r * 1.01, math.sin(a) * r * 1.01, z) for r, z in prof[1:-1]]
        M.tube("AV_Wood", pts, [0.006] * len(pts), seg=3, cap=False)
    M.tube("AV_Rope", [(0, 0, 0.33), (0, 0, 0.6)], [0.01, 0.01], seg=4)
    M.tube("AV_Rope", [(0, 0, -0.33), (0.02, 0, -0.55)], [0.025, 0.01], seg=5)
    return M


def lantern_post(name, seed=9):
    """Wooden pole with an arm for a hanging lantern; lantern hangs at local (0.75, 0, 2.15)."""
    rnd = random.Random(seed); M = Mesh(name)
    M.box("AV_Stone", (0, 0, 0.12), (0.45, 0.45, 0.3), jitter=0.04)
    M.tube("AV_Wood", [(0, 0, 0), (0.03, 0, 1.4), (0.06, 0, 2.9)], [0.08, 0.075, 0.065], seg=7)
    M.beam("AV_Wood", (0.0, 0, 2.75), (0.95, 0, 2.8), 0.07)
    M.beam("AV_Wood", (0.05, 0, 2.35), (0.5, 0, 2.75), 0.05)
    M.tube("AV_Rope", [(0.75, 0, 2.78), (0.75, 0, 2.48)], [0.01, 0.01], seg=4)
    return M


def onggi(name, scale=1.0, seed=10, broken=False, lid=True):
    rnd = random.Random(seed); M = Mesh(name)
    s = scale
    prof = [(0.0, 0.0), (0.18 * s, 0.0), (0.24 * s, 0.08 * s), (0.36 * s, 0.35 * s), (0.4 * s, 0.55 * s), (0.36 * s, 0.75 * s),
            (0.25 * s, 0.86 * s), (0.22 * s, 0.9 * s), (0.24 * s, 0.94 * s), (0.2 * s, 0.94 * s), (0.18 * s, 0.85 * s), (0.18 * s, 0.2 * s), (0.0, 0.2 * s)]
    hole = (lambda i, k: broken and i in (3, 4, 5, 6, 7, 8, 9) and 2 <= k <= 5 + (i % 2)) if broken else None
    M.lathe("AV_Onggi", (0, 0, 0), prof, seg=18, skip=hole)
    if lid and not broken:
        M.lathe("AV_Onggi", (0, 0, 0), [(0.0, 0.92 * s), (0.3 * s, 0.92 * s), (0.31 * s, 0.95 * s), (0.2 * s, 1.02 * s), (0.05 * s, 1.05 * s), (0.06 * s, 1.09 * s), (0.0, 1.1 * s)], seg=18)
    if broken:
        for i in range(5):
            a = rnd.uniform(0, 6.28); r = rnd.uniform(0.45, 0.8) * s
            M.box("AV_Onggi", (math.cos(a) * r, math.sin(a) * r, 0.02), (0.18 * s, 0.12 * s, 0.025), rot=(rnd.uniform(-10, 10), rnd.uniform(-10, 10), rnd.uniform(0, 180)))
    return M


def jangdokdae(name, seed=11):
    """장독대: stone platform with jars."""
    rnd = random.Random(seed); M = Mesh(name)
    M.box("AV_Stone", (0, 0, 0.18), (3.2, 1.8, 0.36), jitter=0.04)
    for i in range(7):
        x = -1.2 + (i % 4) * 0.8 + (0.4 if i >= 4 else 0); y = -0.4 if i < 4 else 0.45
        s = rnd.uniform(0.75, 1.15) * (0.8 if i >= 4 else 1.0)
        start = len(M.v)
        sub = onggi("j", s, seed=seed + i, broken=(i in (2, 5)), lid=rnd.random() < 0.6)
        base = len(M.v)
        for p in sub.v: M.v.append(p + Vector((x, y, 0.36)))
        for idx, uvs, mi, sm in sub.f: M.f.append((tuple(j + base for j in idx), uvs, M.m(sub.mats[mi]), sm))
    return M


def cart(name, seed=12):
    rnd = random.Random(seed); M = Mesh(name)
    for sy in (-0.75, 0.75):
        rim = []
        for k in range(17):
            a = 2 * math.pi * k / 16
            rim.append((0.0 + math.cos(a) * 0.6, sy, 0.62 + math.sin(a) * 0.6))
        M.tube("AV_Wood", rim, [0.06] * len(rim), seg=5, cap=False)
        for k in range(8):
            a = math.pi * k / 8
            M.beam("AV_Wood", (math.cos(a) * 0.58, sy, 0.62 + math.sin(a) * 0.58), (-math.cos(a) * 0.58, sy, 0.62 - math.sin(a) * 0.58), 0.04)
    M.beam("AV_Wood", (0, -0.9, 0.62), (0, 0.9, 0.62), 0.1)
    for sy in (-0.55, 0.55): M.beam("AV_Wood", (-1.3, sy, 0.82), (2.6, sy * 0.4, 0.05), 0.1)
    x = -1.2
    while x < 1.0:
        w = rnd.uniform(0.18, 0.26)
        if rnd.random() > 0.2: M.box("AV_Plank", (x + w / 2, 0, 0.9 - (x + 1.2) * 0.03), (w - 0.02, 1.25, 0.05), rot=(0, 3, 0))
        x += w
    M.transform(lambda p: (p.x, p.y, p.z + (0.25 if p.y > 0 else 0) * 0))
    return M


def well(name, seed=13):
    rnd = random.Random(seed); M = Mesh(name)
    for k in range(5):
        z = k * 0.16 + 0.08
        for i in range(10):
            a = 2 * math.pi * (i + (0.5 if k % 2 else 0)) / 10
            M.box("AV_StoneWall", (math.cos(a) * 0.75, math.sin(a) * 0.75, z), (0.45, 0.28, 0.15), rot=(0, 0, math.degrees(a) + 90), jitter=0.03)
    for sx in (-1, 1):
        M.box("AV_Wood", (sx * 0.95, 0, 1.0), (0.14, 0.14, 2.0), rot=(0, sx * 2, 0))
    M.beam("AV_Wood", (-1.05, 0, 1.85), (1.05, 0, 1.9), 0.11)
    M.tube("AV_Rope", [(0.1, 0, 1.85), (0.12, 0, 0.9)], [0.012, 0.012], seg=4)
    tile_roof  # (open well, no roof)
    return M


def jangseung(name, seed=14):
    """Weathered 장승 pole pair stand-in: carved cylinder with face rings and a tilt."""
    rnd = random.Random(seed); M = Mesh(name)
    prof = [(0.22, 0), (0.22, 1.7), (0.26, 1.75), (0.26, 2.2), (0.2, 2.35), (0.27, 2.5), (0.27, 2.75), (0.15, 2.95), (0.0, 3.0)]
    M.lathe("AV_Bark", (0, 0, 0), [(0.0, 0.0)] + prof, seg=10)
    M.box("AV_Wood", (0, -0.24, 2.6), (0.32, 0.08, 0.08))
    M.box("AV_Wood", (0, -0.25, 2.3), (0.22, 0.06, 0.05))
    M.transform(lambda p: (p.x + 0.08 * p.z, p.y, p.z))
    return M


def sotdae(name, seed=15):
    rnd = random.Random(seed); M = Mesh(name)
    M.tube("AV_Wood", [(0, 0, 0), (0.05, 0, 2.5), (0.12, 0.03, 4.6)], [0.06, 0.05, 0.035], seg=6)
    for k in range(3):
        a = k * 2.1
        bx, by, bz = 0.12 + math.cos(a) * 0.05, 0.03 + math.sin(a) * 0.05, 4.6
        M.tube("AV_Wood", [(0.12, 0.03, 4.4), (bx + math.cos(a) * 0.3, by + math.sin(a) * 0.3, bz + 0.1)], [0.02, 0.02], seg=4)
        c = Vector((bx + math.cos(a) * 0.32, by + math.sin(a) * 0.32, bz + 0.16))
        M.box("AV_Wood", c, (0.32, 0.06, 0.08), rot=(0, 0, math.degrees(a)))
    return M


# ---------------------------------------------------------------- nature
def dead_tree(name, seed=16, height=9.0, spread=1.0, lean=0.0):
    rnd = random.Random(seed); M = Mesh(name)
    count = [0]

    def branch(p, d, length, r, level):
        if level > 5 or r < 0.008: return
        segs = 6 if level < 2 else 4
        sides = 8 if level == 0 else (6 if level < 3 else 4)
        pts, rad = [p.copy()], [r]
        q = p.copy(); dd = d.copy()
        for i in range(segs):
            jitter = Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), rnd.uniform(-0.6, 0.6))) * (0.28 + level * 0.08)
            keep = 0.55 if level == 0 else 0.3
            dd = (dd * (1 - keep) + d * keep + jitter * (0.6 if level == 0 else 0.9)).normalized()
            if level >= 2: dd.z -= 0.05 * level  # droop
            dd.normalize()
            q = q + dd * (length / segs)
            pts.append(q.copy()); rad.append(r * (1 - 0.55 * (i + 1) / segs))
        M.tube("AV_Bark", pts, rad, seg=sides, cap=level == 0, twist_noise=0.12 if level == 0 else 0.0, uscale=0.6)
        count[0] += 1
        nkids = 2 if level == 0 else rnd.choice([2, 2, 3, 3, 4])
        for k in range(nkids):
            t = rnd.uniform(0.45, 1.0) if level > 0 else rnd.uniform(0.55, 0.85)
            idx = min(len(pts) - 1, max(1, int(t * (len(pts) - 1))))
            bp = pts[idx]; br = rad[idx] * rnd.uniform(0.6, 0.8)
            ang = rnd.uniform(0, 2 * math.pi)
            side = Vector((math.cos(ang), math.sin(ang), 0))
            nd = (dd * 0.5 + side * spread * rnd.uniform(0.6, 1.1) + Vector((0, 0, rnd.uniform(0.1, 0.6)))).normalized()
            branch(bp, nd, length * rnd.uniform(0.55, 0.75), br, level + 1)

    base_r = height * 0.035
    branch(Vector((0, 0, -0.2)), Vector((lean, 0, 1)).normalized(), height * 0.48, base_r, 0)
    for k in range(5):
        a = k * 2 * math.pi / 5 + rnd.uniform(-0.3, 0.3)
        d = Vector((math.cos(a), math.sin(a), 0))
        M.tube("AV_Bark", [(d.x * 0.05, d.y * 0.05, 0.6), d * (base_r * 1.6) + Vector((0, 0, 0.1)), d * (base_r * 3.0) + Vector((0, 0, -0.12))],
               [base_r * 0.6, base_r * 0.3, base_r * 0.08], seg=6, uscale=0.6)
    print("tree", name, "branches", count[0])
    return M


def dry_grass(name, seed=17, n=36, h=1.0, reeds=4, r=0.35):
    """Tall dry grass tuft. UV.y = height (0..1) so a vertical gradient texture shades base to tip."""
    rnd = random.Random(seed); M = Mesh(name)
    for i in range(n):
        a = rnd.uniform(0, 2 * math.pi); rr = rnd.uniform(0, r)
        base = Vector((math.cos(a) * rr, math.sin(a) * rr, -0.02))
        hh = h * rnd.uniform(0.55, 1.15)
        out = Vector((math.cos(a), math.sin(a), 0)) * rnd.uniform(0.15, 0.5) * hh
        w = rnd.uniform(0.02, 0.035)
        side = Vector((-math.sin(a + rnd.uniform(-0.6, 0.6)), math.cos(a + rnd.uniform(-0.6, 0.6)), 0))
        segs = 4; idx = []
        for k in range(segs + 1):
            t = k / segs
            c = base + Vector((0, 0, hh * t)) + out * (t * t) - Vector((0, 0, 0.25 * hh * t ** 3 * rnd.random()))
            ww = w * (1 - t * 0.95)
            idx.append((M.add_v(c - side * ww), M.add_v(c + side * ww), t))
        for (a0, b0, t0), (a1, b1, t1) in zip(idx, idx[1:]):
            M.face([a0, b0, b1, a1], [(0.2, t0), (0.8, t0), (0.8, t1), (0.2, t1)], "AV_DryGrass", True)
    for i in range(reeds):
        a = rnd.uniform(0, 2 * math.pi); rr = rnd.uniform(0, r * 0.6)
        b = Vector((math.cos(a) * rr, math.sin(a) * rr, 0))
        hh = h * rnd.uniform(1.4, 1.9)
        tip = b + Vector((math.cos(a) * 0.25, math.sin(a) * 0.25, hh))
        M.tube("AV_DryGrass", [b, b.lerp(tip, 0.5) + Vector((0, 0, 0.05)), tip], [0.008, 0.007, 0.005], seg=3, cap=False)
        start = len(M.v)
        M.lathe("AV_Reed", (0, 0, 0), [(0.0, 0.0), (0.035, 0.05), (0.045, 0.16), (0.03, 0.26), (0.0, 0.32)], seg=6)
        d = (tip - b).normalized()
        M.transform(lambda p, tip=tip, d=d: tip + Vector((p.x, p.y, 0)) + d * p.z - Vector((0, 0, 0.02)), start)
    return M


def crow(name, flying=False):
    M = Mesh(name)
    body = [(0.0, -0.14), (0.04, -0.12), (0.065, -0.05), (0.07, 0.03), (0.055, 0.09), (0.045, 0.12), (0.05, 0.16), (0.04, 0.2), (0.0, 0.215)]
    start = len(M.v)
    M.lathe("AV_Crow", (0, 0, 0), body, seg=8)
    M.transform(lambda p: (p.x, p.z, -p.y), start)  # lathe axis Z -> Y (forward)
    M.box("AV_Crow", (0, 0.24, 0.0), (0.02, 0.06, 0.02), rot=(-10, 0, 0))
    M.box("AV_Crow", (0, -0.22, 0.01), (0.07, 0.12, 0.01), rot=(8, 0, 0))
    if flying:
        for sx in (-1, 1):
            a = M.add_v((sx * 0.05, 0.06, 0.0)); b = M.add_v((sx * 0.05, -0.06, 0.0)); c = M.add_v((sx * 0.32, -0.04, 0.06)); d = M.add_v((sx * 0.3, 0.06, 0.05))
            e = M.add_v((sx * 0.45, -0.08, 0.02))
            f = [a, b, c, d] if sx > 0 else [a, d, c, b]
            M.face(f, [(0, 0), (0, 1), (1, 1), (1, 0)], "AV_Crow")
            M.face(f[::-1], [(0, 0), (1, 0), (1, 1), (0, 1)], "AV_Crow")
            M.face([d, c, e] if sx > 0 else [e, c, d], [(0, 0), (1, 0), (1, 1)], "AV_Crow")
            M.face([e, c, d] if sx > 0 else [d, c, e], [(0, 0), (1, 0), (1, 1)], "AV_Crow")
    else:
        for sx in (-1, 1):
            M.box("AV_Crow", (sx * 0.06, -0.02, 0.02), (0.02, 0.2, 0.07), rot=(10, 0, sx * 6))
        for sx in (-1, 1): M.beam("AV_Crow", (sx * 0.025, 0, -0.06), (sx * 0.025, 0.01, -0.14), 0.008)
        M.transform(lambda p: (p.x, p.y, p.z + 0.14))
    return M


def tile_rubble(name, seed=18):
    rnd = random.Random(seed); M = Mesh(name)
    for i in range(40):
        a = rnd.uniform(0, 6.28); r = rnd.uniform(0, 1.6) ** 1.3
        M.box("AV_Tile", (math.cos(a) * r, math.sin(a) * r, 0.03 + rnd.uniform(0, 0.12) * (1.6 - r)), (rnd.uniform(0.18, 0.3), rnd.uniform(0.2, 0.32), 0.03),
              rot=(rnd.uniform(-25, 25), rnd.uniform(-25, 25), rnd.uniform(0, 180)))
    for i in range(6):
        a = rnd.uniform(0, 6.28); r = rnd.uniform(0.2, 1.2)
        M.beam("AV_Wood", (math.cos(a) * r, math.sin(a) * r, 0.05), (math.cos(a) * r + rnd.uniform(-1.5, 1.5), math.sin(a) * r + rnd.uniform(-1.5, 1.5), rnd.uniform(0.05, 0.6)), rnd.uniform(0.08, 0.14))
    return M


def collapsed_house(name, seed=19):
    """A choga that fell in: low walls, roof slumped to the ground, beams poking out."""
    rnd = random.Random(seed); M = Mesh(name)
    W, D = 6.0, 4.0
    M.box("AV_Stone", (0, 0, 0.15), (W + 0.6, D + 0.6, 0.3), jitter=0.04)
    for i, (x0, x1) in enumerate(((-W / 2, -0.6), (0.9, W / 2))):
        mud_panel(M, x0, x1, 0.3, 0.3 + rnd.uniform(0.6, 1.3), D / 2, seed=seed + i)
    start = len(M.v)
    mud_panel(M, -D / 2, D / 2, 0.3, 1.1, 0, seed=seed + 9)
    M.transform(lambda p: (-W / 2 - p.y, p.x, p.z), start)
    for i in range(7):
        x = rnd.uniform(-W / 2, W / 2)
        M.tube("AV_Wood", [(x, -D / 2 - 0.5, rnd.uniform(0.2, 0.5)), (x + rnd.uniform(-0.8, 0.8), D / 2, rnd.uniform(1.2, 2.2))], [0.08, 0.07], seg=6)
    start = len(M.v)
    thatch_roof(M, W, D, 0.5, over=0.6, H=1.4, seed=seed, hole=0.35, collapse=0.5)
    M.transform(lambda p: (p.x, p.y, p.z - 0.25 * max(0, p.x + 1.5) - 0.0), start)
    return M


def pent_roof(M, x0, x1, y0, depth, z, drop=0.55, seed=0, hole=0.0, mat="AV_Tile"):
    """차양: a single tile slope sticking out from a wall face at y0 toward -Y."""
    def top(x, y):
        t = (y0 - y) / depth
        z_ = z - drop * t ** 1.2 + 0.12 * (abs((x - (x0 + x1) / 2) / ((x1 - x0) / 2))) ** 4 * t
        f = (x / 0.30) % 1.0 - 0.5
        return z_ + 0.06 * math.sqrt(max(0.0, 1 - (f * 2.4) ** 2))
    def keep(x, y):
        return hole <= 0 or nz(x, y, seed, 0.6) < 1 - hole * 1.8
    n = int((x1 - x0) / 0.09)
    M.grid(mat, x0, x1, y0 - depth, y0, n, 8, top, keep=keep, uvs=0.5)
    M.grid("AV_Wood", x0, x1, y0 - depth, y0, max(4, n // 4), 3, lambda x, y: top(x, y) - 0.14, keep=keep, uvs=0.5, flip=True)
    M.box(mat, ((x0 + x1) / 2, y0 - depth, z - drop - 0.05), (x1 - x0, 0.1, 0.2))
    x = x0 + 0.1
    while x < x1:
        M.beam("AV_Wood", (x, y0, z - 0.1), (x, y0 - depth + 0.05, z - drop - 0.12), 0.05)
        x += 0.45


def tall_house(name, W=6.4, D=5.2, floors=3, seed=60, lean=0.0, twist=0.0, collapse=0.0, hole=0.3, glow=()):
    """Narrow 2-3 storey wooden house: plank and lattice walls per floor, 차양 over every floor, balcony, 팔작 roof.
    lean shears the frame sideways (honglin Tan style), collapse slumps one roof corner and drops the top floor walls."""
    rnd = random.Random(seed); random.seed(seed)
    M = Mesh(name)
    bh = 0.45
    M.box("AV_Stone", (0, 0, bh / 2), (W + 0.9, D + 0.9, bh), jitter=0.04)
    for i in range(int((W + 0.9) / 0.7)):
        M.box("AV_Stone", (-(W + 0.9) / 2 + 0.35 + i * 0.7, -(D + 0.9) / 2, bh / 2), (0.65, 0.3, bh + 0.05), jitter=0.04)
    z0 = bh + 0.1
    fh = 2.75
    nx = max(2, round(W / 2.2)); xs = [-W / 2 + W * i / nx for i in range(nx + 1)]
    ys = [-D / 2, D / 2]
    zt = z0 + fh * floors
    for x in xs:
        for y in ys:
            M.box("AV_Wood", (x, y, (z0 + zt) / 2), (0.24, 0.24, zt - z0), jitter=0.01)
    yf, yb = ys
    for f in range(floors):
        za = z0 + f * fh; zb = za + fh
        top_floor = f == floors - 1
        for zz in (za + 0.2, zb):
            for y in ys: M.box("AV_Wood", (0, y, zz), (W + 0.3, 0.2, 0.2))
            for x in (xs[0], xs[-1]): M.box("AV_Wood", (x, 0, zz), (0.2, D + 0.3, 0.2))
        M.box("AV_Plank", (0, 0, za + 0.25), (W, D, 0.06))
        for i in range(nx):
            a, b = xs[i] + 0.12, xs[i + 1] - 0.12
            gone = top_floor and collapse > 0 and i == nx - 1
            if gone:
                for k in range(3):
                    M.beam("AV_Plank", (a + rnd.uniform(0, b - a), yf, za + 0.3), (a + rnd.uniform(0, b - a), yf - rnd.uniform(0.2, 1.2), za + rnd.uniform(0.6, 1.8)), 0.06, 0.2)
                continue
            k = rnd.random()
            if k < 0.45:
                lattice_door(M, a + 0.1, b - 0.1, za + 0.45, zb - 0.45, yf, torn=rnd.uniform(0.3, 0.75), glow=((f, i) in glow), seed=seed * 31 + f * 7 + i)
                M.box("AV_Plank", ((a + b) / 2, yf, zb - 0.22), (b - a, 0.05, 0.4))
                M.box("AV_Plank", ((a + b) / 2, yf, za + 0.35), (b - a, 0.05, 0.25))
            else:
                plank_wall(M, a, b, za + 0.25, zb - 0.1, yf, seed=seed * 13 + f * 5 + i, missing=0.12 + 0.1 * f, horiz=k > 0.8)
            plank_wall(M, a, b, za + 0.25, zb - 0.1, yb, seed=seed * 17 + f * 5 + i, missing=0.1, horiz=True)
        for x in (xs[0], xs[-1]):
            start = len(M.v)
            if not (top_floor and collapse > 0 and x > 0):
                plank_wall(M, -D / 2 + 0.12, D / 2 - 0.12, za + 0.25, zb - 0.1, 0, seed=seed * 19 + f * 3 + int(x), missing=0.15, horiz=rnd.random() < 0.6)
            M.transform(lambda p, x=x: (x - p.y, p.x, p.z), start)
        if not top_floor:
            pent_roof(M, -W / 2 - 0.45, W / 2 + 0.45, yf, 1.0 if f == 0 else 0.8, zb + 0.05, seed=seed + f, hole=hole * 0.6)


    zf = tile_roof(M, W, D, zt + 0.25, over=1.0, rise=0.66, seed=seed, hole=hole, gable=0.5)

    def deform(p):
        h = max(0.0, p.z - z0)
        x = p.x + lean * h * (1 + 0.04 * h)
        y = p.y + twist * h
        z = p.z
        if collapse > 0:
            # one corner of the top slumps down
            c = smooth((p.x / (W / 2 + 1) + 0.15) * 1.4) * smooth((h - fh * (floors - 1) + 0.5) / 2.5)
            z -= collapse * 2.2 * c
            x += collapse * 0.6 * c
        return (x, y, z)
    M.transform(deform)
    return M


def street_stones(name, seed=50, L=4.0):
    """Sunken flat stepping stones along a mud street, local +Y is the walking direction."""
    rnd = random.Random(seed); M = Mesh(name)
    y = -L / 2
    while y < L / 2:
        s = rnd.uniform(0.45, 0.8)
        for k in range(rnd.choice([1, 2, 2])):
            x = rnd.uniform(-0.7, 0.7)
            if rnd.random() < 0.2: continue
            M.box("AV_Stone", (x, y + s / 2, 0.01), (s * rnd.uniform(0.9, 1.4), s, 0.12), rot=(rnd.uniform(-3, 3), rnd.uniform(-3, 3), rnd.uniform(0, 180)), jitter=0.05)
        y += s + rnd.uniform(0.1, 0.35)
    return M


def plank_pile(name, seed=51):
    rnd = random.Random(seed); M = Mesh(name)
    for i in range(14):
        a = rnd.uniform(0, 180); L = rnd.uniform(1.2, 2.8)
        c = (rnd.uniform(-0.8, 0.8), rnd.uniform(-0.8, 0.8), 0.04 + i * 0.025)
        M.box("AV_Plank" if i % 3 else "AV_Wood", c, (L, rnd.uniform(0.16, 0.26), 0.05), rot=(rnd.uniform(-8, 8), rnd.uniform(-12, 12), a))
    for i in range(3):
        a = rnd.uniform(0, 6.3)
        M.beam("AV_Wood", (math.cos(a) * 0.4, math.sin(a) * 0.4, 0.05), (math.cos(a) * 2.4, math.sin(a) * 2.4, rnd.uniform(0.3, 0.9)), 0.16)
    return M


def straw_heap(name, seed=52):
    """Fallen thatch / rotting straw heap."""
    rnd = random.Random(seed); M = Mesh(name)
    def top(x, y):
        d = math.hypot(x / 1.6, y / 1.1)
        if d > 1: return None
        return 0.55 * (1 - d ** 1.6) + 0.08 * nz(x, y, seed, 2.0) - 0.03
    M.grid("AV_Thatch", -1.6, 1.6, -1.1, 1.1, 24, 16, top, uvs=0.6)
    for i in range(30):
        a = rnd.uniform(0, 6.3); r = rnd.uniform(0.6, 1.5)
        p = Vector((math.cos(a) * r, math.sin(a) * r * 0.7, 0.02))
        q = p + Vector((rnd.uniform(-0.5, 0.5), rnd.uniform(-0.5, 0.5), rnd.uniform(0.0, 0.3)))
        M.tube("AV_DryGrass", [p, q], [0.012, 0.006], seg=3, cap=False)
    return M


# ---------------------------------------------------------------- export
ASSETS = {
    "AV_Hanok2F_A": lambda: hanok2f("AV_Hanok2F_A", 9.6, 6.0, seed=1, lean=0.25, sag=0.35, hole=0.3, glow_bays=(1,)),
    "AV_Hanok2F_B": lambda: hanok2f("AV_Hanok2F_B", 7.2, 5.4, seed=2, lean=-0.45, sag=0.55, hole=0.45),
    "AV_Hanok2F_C": lambda: hanok2f("AV_Hanok2F_C", 8.4, 5.6, seed=3, lean=0.15, sag=0.25, hole=0.2, glow_bays=(2,)),
    "AV_Choga_A": lambda: choga("AV_Choga_A", 6.8, 4.2, seed=4, hole=0.25, glow_bays=(0,)),
    "AV_Choga_B": lambda: choga("AV_Choga_B", 5.6, 4.0, seed=5, hole=0.4, collapse=0.45, lean=0.12),
    "AV_Choga_C": lambda: choga("AV_Choga_C", 7.4, 4.4, seed=6, hole=0.15, collapse=0.2, lean=-0.08),
    "AV_ChogaCollapsed": lambda: collapsed_house("AV_ChogaCollapsed", 7),
    "AV_Watchtower": lambda: watchtower("AV_Watchtower", 8),
    "AV_FenceSsari": lambda: ssari_fence("AV_FenceSsari", 3.0, 9),
    "AV_FenceSsariBroken": lambda: ssari_fence("AV_FenceSsariBroken", 3.0, 10, broken=0.8),
    "AV_FencePlank": lambda: plank_fence("AV_FencePlank", 3.0, 11),
    "AV_FencePlankBroken": lambda: plank_fence("AV_FencePlankBroken", 3.0, 12, broken=0.6),
    "AV_StoneWall": lambda: stone_wall("AV_StoneWall", 4.0, 1.5, 13),
    "AV_StoneWallBroken": lambda: stone_wall("AV_StoneWallBroken", 4.0, 1.5, 14, broken=0.75),
    "AV_RetainingWall": lambda: stone_wall("AV_RetainingWall", 6.0, 2.7, 15, cap=False, big=True),
    "AV_StoneSteps": lambda: stone_steps("AV_StoneSteps", 4.2, 8, 0.32, 0.45, 16),
    "AV_StoneLantern": lambda: stone_lantern("AV_StoneLantern"),
    "AV_HangingLantern": lambda: hanging_lantern("AV_HangingLantern"),
    "AV_LanternPost": lambda: lantern_post("AV_LanternPost"),
    "AV_Onggi": lambda: onggi("AV_Onggi", 1.0, 20),
    "AV_OnggiBroken": lambda: onggi("AV_OnggiBroken", 0.9, 21, broken=True),
    "AV_Jangdokdae": lambda: jangdokdae("AV_Jangdokdae", 22),
    "AV_Cart": lambda: cart("AV_Cart", 23),
    "AV_Well": lambda: well("AV_Well", 24),
    "AV_Jangseung": lambda: jangseung("AV_Jangseung", 25),
    "AV_Sotdae": lambda: sotdae("AV_Sotdae", 26),
    "AV_TileRubble": lambda: tile_rubble("AV_TileRubble", 27),
    "AV_Tall3F_A": lambda: tall_house("AV_Tall3F_A", 6.4, 5.2, 3, 61, lean=0.0, hole=0.25, glow=((1, 0),)),
    "AV_Tall3F_Lean": lambda: tall_house("AV_Tall3F_Lean", 6.0, 5.0, 3, 62, lean=0.085, twist=0.02, hole=0.45),
    "AV_Tall3F_Broken": lambda: tall_house("AV_Tall3F_Broken", 6.8, 5.4, 3, 63, lean=-0.03, collapse=1.0, hole=0.6),
    "AV_Tall2F_A": lambda: tall_house("AV_Tall2F_A", 5.6, 4.8, 2, 64, lean=-0.02, hole=0.3, glow=((0, 1),)),
    "AV_Tall2F_Lean": lambda: tall_house("AV_Tall2F_Lean", 7.2, 5.0, 2, 65, lean=-0.11, hole=0.5),
    "AV_Tall2F_Broken": lambda: tall_house("AV_Tall2F_Broken", 6.0, 5.0, 2, 66, lean=0.05, collapse=0.8, hole=0.55),
    "AV_StreetStones": lambda: street_stones("AV_StreetStones"),
    "AV_PlankPile": lambda: plank_pile("AV_PlankPile"),
    "AV_StrawHeap": lambda: straw_heap("AV_StrawHeap"),
    "AV_DeadTree_A": lambda: dead_tree("AV_DeadTree_A", 30, 11.0, 1.1, 0.15),
    "AV_DeadTree_B": lambda: dead_tree("AV_DeadTree_B", 31, 8.0, 1.3, -0.25),
    "AV_DeadTree_C": lambda: dead_tree("AV_DeadTree_C", 32, 6.0, 0.9, 0.35),
    "AV_DryGrass_A": lambda: dry_grass("AV_DryGrass_A", 40, 34, 0.9, 4),
    "AV_DryGrass_B": lambda: dry_grass("AV_DryGrass_B", 41, 26, 0.6, 1, 0.25),
    "AV_DryGrass_C": lambda: dry_grass("AV_DryGrass_C", 42, 48, 1.25, 7, 0.45),
    "AV_Crow": lambda: crow("AV_Crow"),
    "AV_CrowFly": lambda: crow("AV_CrowFly", True),
}

for name, fn in ASSETS.items():
    if ONLY and name not in ONLY: continue
    bpy.ops.wm.read_factory_settings(use_empty=True)
    M = fn()
    ob = M.build()
    tris = sum(len(p.vertices) - 2 for p in ob.data.polygons)
    avlib.export([ob], os.path.join(OUT, name + ".fbx"))
    dims = ob.dimensions
    print("EXPORT %s tris=%d size=%.1fx%.1fx%.1f mats=%s" % (name, tris, dims.x, dims.y, dims.z, ",".join(M.mats)))
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(WORK, name + ".blend"))
