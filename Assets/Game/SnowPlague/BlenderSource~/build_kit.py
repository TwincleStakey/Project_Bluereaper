# Snow plague kit: dead trees, dead shrubs, frosted grass, stumps, foreground branches, straw mats over the dead,
# a broken handcart and wheel, stakes and fences, sacks, rags, clay jars and fallen twigs.
# blender -b --factory-startup -P build_kit.py -- <unity SnowPlague dir> <work dir>
# All sizes in metres. Frost/AO/sway are baked into vertex colours (see splib.py).
import bpy, sys, os, math, random, json
from mathutils import Vector, Matrix, noise
sys.path.append(os.path.dirname(os.path.abspath(__file__)))
from splib import *

args = sys.argv[sys.argv.index("--") + 1:]
OUT = os.path.join(args[0], "Models")
WORK = args[1]
os.makedirs(OUT, exist_ok=True); os.makedirs(WORK, exist_ok=True)
clear_scene()
REPORT = {}
UP = Vector((0, 0, 1))


# ---------------------------------------------------------------------------------------------------------------
def dead_tree(name, seed, height, lean=0.08, maxdepth=4, broken=0.3, spread=(40, 70), children=(6, 9), r0=0.22, top_snap=False):
    rng = random.Random(seed); K = Kit(name)
    d = Vector((rng.uniform(-lean, lean), rng.uniform(-lean, lean), 1))
    pts = gnarl_path(rng, Vector((0, 0, -0.25)), d, height * (0.55 if top_snap else 1.0), 0.6, 0.18, 0.02, seed)
    tot = len(pts) - 1
    jag = lambda i, k, t: (noise.noise(Vector((k * 0.9, i * 0.7, seed))) * 0.12) + (0.35 * (1 - smooth01(0, 0.12, t)) * (0.6 + 0.4 * math.sin(k * 2.1)))
    K.tube(pts, lambda t: r0 * (1 - 0.75 * t) + 0.015, 8, "SP_Bark", sway=lambda t: 0.15 * t * t, cap_end=True, jag=jag, uscale=1.0)
    # splintered top on snags
    if top_snap:
        top = pts[-1]
        for k in range(5):
            a = k * 2 * math.pi / 5 + rng.uniform(-0.3, 0.3)
            b = top + Vector((math.cos(a), math.sin(a), 0)) * r0 * 0.25
            K.tube([b - Vector((0, 0, 0.1)), b + Vector((math.cos(a) * 0.03, math.sin(a) * 0.03, rng.uniform(0.25, 0.6)))],
                   lambda t: r0 * 0.12 * (1 - t), 3, "SP_Bark")
    # roots
    for k in range(5):
        a = k * 2 * math.pi / 5 + rng.uniform(-0.4, 0.4)
        o = Vector((math.cos(a), math.sin(a), 0))
        rp = [Vector((0, 0, 0.45)) + o * r0 * 0.3, o * (r0 + 0.2) + Vector((0, 0, 0.06)), o * (r0 + 0.55) + Vector((0, 0, -0.02)), o * (r0 + 0.95) + Vector((0, 0, -0.12))]
        K.tube(rp, lambda t: r0 * 0.45 * (1 - t) ** 1.3 + 0.008, 5, "SP_Bark")
    # main branches from the upper 2/3 of the trunk
    n = rng.randint(*children)
    for c in range(n):
        f = rng.uniform(0.35, 0.95)
        i = int(f * tot)
        p = pts[i]; dl = pts[min(i + 1, tot)] - pts[max(i - 1, 0)]
        cd = rnd_dir(rng, dl, rng.uniform(*spread), up_bias=0.05)
        cl = height * rng.uniform(0.38, 0.6) * (1.15 - f * 0.55)
        branch(K, rng, p, cd, cl, r0 * (1 - 0.75 * f) * 0.6, 1, maxdepth, "SP_Bark", 6, sway0=0.15 * f, child_n=(3, 5),
               spread=(30, 62), step=0.45, wob=0.4, tropism=0.06, broken=broken, seed=seed + c, rnd=rng.random())
    return K


def dead_shrub(name, seed, height, stems=(6, 9), spread_deg=35, maxdepth=2, flat=1.0, broken=0.25):
    rng = random.Random(seed); K = Kit(name)
    for s in range(rng.randint(*stems)):
        a = rng.uniform(0, 2 * math.pi)
        tilt = rng.uniform(5, spread_deg)
        d = Vector((math.cos(a) * math.sin(math.radians(tilt)) * flat, math.sin(a) * math.sin(math.radians(tilt)) * flat, math.cos(math.radians(tilt))))
        base = Vector((math.cos(a) * 0.05, math.sin(a) * 0.05, -0.05))
        branch(K, rng, base, d, height * rng.uniform(0.6, 1.1), rng.uniform(0.014, 0.022), 0, maxdepth, "SP_Bark", 3,
               child_n=(3, 5), spread=(25, 60), step=0.34, wob=0.45, tropism=0.08, broken=broken, child_len=(0.35, 0.6),
               seed=seed * 3 + s, rnd=rng.random())
    return K


def grass_tuft(name, seed, n=(26, 38), hgt=(0.22, 0.5), width=0.011, broken=0.3):
    rng = random.Random(seed); K = Kit(name)
    for b in range(rng.randint(*n)):
        a = rng.uniform(0, 2 * math.pi)
        out = Vector((math.cos(a), math.sin(a), 0))
        lean = rng.uniform(0.15, 0.75)
        d = (out * lean + UP).normalized()
        L = rng.uniform(*hgt)
        base = out * rng.uniform(0, 0.07)
        pts = []; kink = rng.random() < broken; kt = rng.uniform(0.4, 0.7)
        for k in range(6):
            t = k / 5
            p = base + d * (L * t)
            p += out * (L * 0.5 * t * t * lean)
            p.z -= L * 0.25 * t * t * lean
            if kink and t > kt:   # broken blade folds down
                p = base + d * (L * kt) + (out * 0.7 - UP * 0.7).normalized() * (L * (t - kt))
            pts.append(p)
        side = Vector((-out.y, out.x, 0))
        rr = rng.random()
        rows = []
        for k, p in enumerate(pts):
            t = k / 5
            w = width * (1 - t) ** 0.7
            rows.append((K.vert(p - side * w, (0, t), t ** 1.4, rr), K.vert(p + side * w, (1, t), t ** 1.4, rr)))
        for r0, r1 in zip(rows, rows[1:]):
            K.face((r0[0], r0[1], r1[1], r1[0]), "SP_Grass")
    return K


def stump(name, seed, h=0.7, r=0.3, cut=False):
    rng = random.Random(seed); K = Kit(name)
    sides = 9
    jag = lambda i, k, t: noise.noise(Vector((k * 0.8, i * 0.5, seed))) * 0.1 + 0.3 * (1 - smooth01(0, 0.25, t)) * (0.5 + 0.5 * math.sin(k * 1.9))
    pts = [Vector((0, 0, -0.15)), Vector((0, 0, h * 0.4)), Vector((rng.uniform(-0.04, 0.04), rng.uniform(-0.04, 0.04), h))]
    rings = K.tube(pts, lambda t: r * (1 - 0.15 * t), sides, "SP_Bark", jag=jag)
    top = rings[-1][:sides]
    if cut:
        c = K.vert(pts[-1] + Vector((0, 0, 0.01)), (0, 0))
        for k in range(sides): K.face((top[k], top[(k + 1) % sides], c), "SP_Wood")
    else:
        # splinters: a jagged crown of shards
        cen = pts[-1]
        tipz = [h + rng.uniform(0.05, 0.45) * (1 if rng.random() > 0.3 else 0.2) for _ in range(sides)]
        inner = []
        for k in range(sides):
            p = K.v[top[k]]
            inner.append(K.vert(cen.lerp(p, 0.55) + Vector((0, 0, rng.uniform(-0.05, 0.05))), (k * 0.2, 0)))
        for k in range(sides):
            p0, p1 = K.v[top[k]], K.v[top[(k + 1) % sides]]
            tip = p0.lerp(p1, 0.5).lerp(cen, 0.25); tip.z = tipz[k]
            t = K.vert(tip, (k * 0.2, tipz[k] - h))
            K.face((top[k], top[(k + 1) % sides], t), "SP_Bark")
            K.face((inner[(k + 1) % sides], inner[k], t), "SP_Wood")
        c = K.vert(cen + Vector((0, 0, -0.04)), (0, 0))
        for k in range(sides): K.face((inner[k], inner[(k + 1) % sides], c), "SP_Wood")
    for k in range(5):
        a = k * 2 * math.pi / 5 + rng.uniform(-0.4, 0.4)
        o = Vector((math.cos(a), math.sin(a), 0))
        rp = [o * r * 0.5 + Vector((0, 0, h * 0.35)), o * (r + 0.12) + Vector((0, 0, 0.04)), o * (r + rng.uniform(0.35, 0.6)) + Vector((0, 0, -0.06))]
        K.tube(rp, lambda t: r * 0.38 * (1 - t) ** 1.3 + 0.008, 5, "SP_Bark")
    return K


def fg_branch(name, seed, length=5.0):
    """Long dead branch for the screen-edge silhouettes: lies roughly in the XY plane, grows along +X."""
    rng = random.Random(seed); K = Kit(name)
    branch(K, rng, Vector((0, 0, 0)), Vector((1, rng.uniform(-0.2, 0.2), 0.05)), length, 0.09, 0, 3, "SP_Bark", 6,
           child_n=(3, 5), spread=(30, 60), step=0.4, wob=0.35, tropism=-0.02, broken=0.2, child_len=(0.35, 0.6), seed=seed)
    # flatten the crown toward the plane so it reads as a silhouette from above
    for i, p in enumerate(K.v): K.v[i] = Vector((p.x, p.y, p.z * 0.35))
    return K


def twigs(name, seed, n=7):
    rng = random.Random(seed); K = Kit(name)
    for s in range(n):
        a = rng.uniform(0, 2 * math.pi); base = Vector((rng.uniform(-0.8, 0.8), rng.uniform(-0.8, 0.8), 0.02))
        d = Vector((math.cos(a), math.sin(a), 0.02))
        branch(K, rng, base, d, rng.uniform(0.4, 1.1), rng.uniform(0.012, 0.03), 0, 1, "SP_Bark", 5, child_n=(1, 3),
               spread=(25, 50), step=0.2, wob=0.25, tropism=-0.05, broken=0.4, seed=seed + s)
    for i, p in enumerate(K.v): K.v[i] = Vector((p.x, p.y, max(0.0, p.z * 0.25) + 0.015))
    return K


# ---------------------------------------------------------------------------------------------------------------
def ellipsoid_top(parts, x, y):
    z = 0.0
    for (c, r) in parts:
        dx, dy = (x - c[0]) / r[0], (y - c[1]) / r[1]
        q = 1 - dx * dx - dy * dy
        if q > 0: z = max(z, c[2] + r[2] * math.sqrt(q))
    return z


def straw_mat(name, seed, bodies, size=(2.1, 1.25), res=(34, 22), rot_deg=0.0, feet=None, ropes=(-0.45, 0.35), pulled=0.0):
    """거적 over the dead: a straw mat draped (relaxed height field) over lumps, frayed edges, rope ties.
    bodies: list of (centre, radii) ellipsoids in mat-local metres (x along the mat)."""
    rng = random.Random(seed); K = Kit(name)
    nx, ny = res; sx, sy = size
    S = [[0.0] * (ny + 1) for _ in range(nx + 1)]
    for i in range(nx + 1):
        for j in range(ny + 1):
            S[i][j] = ellipsoid_top(bodies, (i / nx - 0.5) * sx, (j / ny - 0.5) * sy)
    Z = [[S[i][j] for j in range(ny + 1)] for i in range(nx + 1)]
    for it in range(60):
        N = [[0.0] * (ny + 1) for _ in range(nx + 1)]
        for i in range(nx + 1):
            for j in range(ny + 1):
                nb = [Z[a][b] for a, b in ((i - 1, j), (i + 1, j), (i, j - 1), (i, j + 1)) if 0 <= a <= nx and 0 <= b <= ny]
                avg = sum(nb) / len(nb)
                N[i][j] = max(S[i][j] + 0.02, 0.012, Z[i][j] * 0.4 + avg * 0.6 - 0.004)
        Z = N
    # weave ripples and stiff straw creases
    def h(u, v):
        i, j = round(u * nx), round(v * ny)
        z = Z[i][j]
        z += math.sin(u * sx * 9.0 + noise.noise(Vector((u * 3, v * 3, seed))) * 2) * 0.006 * min(1, z * 8)
        z += noise.noise(Vector((u * 6, v * 6, seed + 3))) * 0.015 * min(1, z * 6)
        # edges curl up a little where they rest on the ground
        e = min(u, 1 - u, v, 1 - v)
        if z < 0.05: z += max(0, 0.06 - e * 1.2) * 0.4
        return z
    def keep(i, j):
        u, v = (i + 0.5) / nx, (j + 0.5) / ny
        e = min(u, 1 - u, v, 1 - v)
        fr = noise.noise(Vector((u * 9, v * 9, seed + 7))) * 0.035 + 0.02
        if e < fr: return False
        if pulled > 0 and u > 1 - pulled + noise.noise(Vector((v * 5, 0, seed))) * 0.08: return False
        return rng.random() > 0.004
    rot = Matrix.Rotation(math.radians(rot_deg), 3, 'Z')
    K.grid(nx, ny, sx, sy, h, "SP_Straw", keep=keep, uvscale=1.0, rot=rot)
    # rope ties across the mat
    for rx in ropes:
        pts = []
        for k in range(15):
            v = k / 14
            j = min(ny, max(0, round(v * ny))); i = min(nx, max(0, round((rx / sx + 0.5) * nx)))
            pts.append(rot @ Vector((rx, (v - 0.5) * sy * 0.92, Z[i][j] + 0.018)))
        K.tube(pts, lambda t: 0.014, 4, "SP_Rope")
    # what shows past the mat: bare feet / bundle ends wrapped in rags (kept abstract)
    for (c, r) in (feet or []):
        K.blob(rot @ Vector(c), r, "SP_Cloth", subdiv=2, lumpy=0.12, flatten_below=0.0, seed=seed + c[0], rot=rot)
    # the hidden body under a pulled-back mat: only a rag bundle shows
    if pulled > 0:
        for (c, r) in bodies:
            if c[0] + r[0] > sx * (0.5 - pulled):
                K.blob(rot @ Vector(c), (r[0], r[1], r[2] * 1.05), "SP_Cloth", subdiv=2, lumpy=0.18, flatten_below=0.0, seed=seed + 5, rot=rot)
    return K


def body_parts(x0=0.0, curl=0.0, scale=1.0, y0=0.0):
    s = scale
    return [((x0 + 0.66 * s, y0 + curl * 0.15, 0.0), (0.14 * s, 0.13 * s, 0.17 * s)),     # head
            ((x0 + 0.38 * s, y0 + curl * 0.05, 0.0), (0.12 * s, 0.25 * s, 0.2 * s)),        # shoulders
            ((x0 + 0.12 * s, y0, 0.0), (0.34 * s, 0.21 * s, 0.22 * s)),                     # torso
            ((x0 - 0.38 * s, y0 + curl * 0.25 + 0.09 * s, 0.0), (0.34 * s, 0.09 * s, 0.13 * s)),   # legs
            ((x0 - 0.38 * s, y0 + curl * 0.3 - 0.09 * s, 0.0), (0.34 * s, 0.09 * s, 0.12 * s))]


def handcart(name, seed):
    """Broken 손수레: bed with planks (one missing, one snapped), long handles resting on the ground, one wheel left
    on the axle; the other wheel is gone (SP_CartWheel lies separately), so the cart rolls over onto that corner."""
    rng = random.Random(seed); K = Kit(name)
    W, Lb, Lh = 0.95, 1.5, 1.1      # width, bed length, handle length
    R = 0.48                          # wheel radius
    beam = 0.08
    for side in (-1, 1):
        K.box((0.0 - Lh * 0.5, side * W * 0.5, R), (Lb + Lh, beam, beam), "SP_Wood", jitter=0.008, rng=rng)
    xs = [-Lb * 0.5 + 0.08 + k * (Lb - 0.16) / 6 for k in range(7)]
    for k, x in enumerate(xs):
        if k == 2: continue                       # missing plank
        w = W + 0.12
        yo = 0.0
        if k == 4: w = W * 0.55; yo = -W * 0.25   # snapped plank
        K.box((x, yo, R + beam * 0.5 + 0.02),
              (0.17, w, 0.035), "SP_Wood", rot=Matrix.Rotation(rng.uniform(-0.04, 0.04), 3, 'Z'), jitter=0.006, rng=rng)
    # side rail on one side only: stakes and a rail
    for x in (-Lb * 0.45, 0.0, Lb * 0.45):
        K.box((x, -W * 0.5, R + 0.22), (0.05, 0.05, 0.4), "SP_Wood", jitter=0.008, rng=rng)
    K.box((0.0, -W * 0.5, R + 0.4), (Lb, 0.05, 0.06), "SP_Wood", jitter=0.008, rng=rng)
    # a broken stake on the other side
    K.box((Lb * 0.3, W * 0.5, R + 0.12), (0.05, 0.05, 0.22), "SP_Wood")
    # axle and the one remaining wheel
    K.tube([Vector((0.15, -W * 0.5 - 0.2, R)), Vector((0.15, W * 0.5 + 0.15, R))], lambda t: 0.035, 6, "SP_Iron", cap_start=True, cap_end=True)
    wheel(K, Vector((0.15, -W * 0.5 - 0.12, R)), R, rng)
    # roll the cart over toward +y (wheel missing there) and let the handles rest on the ground
    pitch = math.atan2(R, Lh + Lb * 0.5) * 0.85
    roll = math.atan2(R * 0.75, W)
    M = Matrix.Rotation(-roll, 3, 'X') @ Matrix.Rotation(-pitch, 3, 'Y')
    for i, p in enumerate(K.v): K.v[i] = M @ p
    zmin = min(p.z for p in K.v)
    for i, p in enumerate(K.v): K.v[i] = p - Vector((0, 0, zmin + 0.01))
    return K


def wheel(K, c, R, rng, spokes=8):
    """Spoked wheel, axle along Y, centred on c."""
    ring = [c + Vector((math.cos(a) * R, 0, math.sin(a) * R)) for a in [2 * math.pi * k / 20 for k in range(21)]]
    K.tube(ring, lambda t: 0.04, 4, "SP_Wood")
    ring2 = [c + Vector((math.cos(a) * (R + 0.025), 0, math.sin(a) * (R + 0.025))) for a in [2 * math.pi * k / 20 for k in range(21)]]
    K.tube(ring2, lambda t: 0.018, 4, "SP_Iron")
    K.lathe([(0.0, -0.09), (0.08, -0.09), (0.09, 0.0), (0.08, 0.09), (0.0, 0.09)], 8, "SP_Wood", center=c,
            rot=Matrix.Rotation(math.radians(90), 3, 'X'))
    for k in range(spokes):
        a = 2 * math.pi * k / spokes + rng.uniform(-0.05, 0.05)
        if rng.random() < 0.12: continue   # a missing spoke
        d = Vector((math.cos(a), 0, math.sin(a)))
        K.box(c + d * (R * 0.5 + 0.03), (0.04, 0.035, R - 0.07), "SP_Wood", rot=rot_to(d))


def cart_wheel(name, seed):
    rng = random.Random(seed); K = Kit(name)
    wheel(K, Vector((0, 0, 0)), 0.48, rng)
    # lying flat, tilted a little where it rests on a stone
    M = Matrix.Rotation(math.radians(84), 3, 'X') @ Matrix.Identity(3)
    M = Matrix.Rotation(math.radians(6), 3, 'Y') @ M
    for i, p in enumerate(K.v): K.v[i] = M @ p
    zmin = min(p.z for p in K.v)
    for i, p in enumerate(K.v): K.v[i] = p - Vector((0, 0, zmin))
    return K


def stake(K, rng, base, h, r, lean, mat="SP_Wood", sharp=True, broken=False):
    d = Vector((lean[0], lean[1], 1)).normalized()
    top = base + d * h
    tip = h * (0.82 if sharp and not broken else 1.0)
    pts = [base - d * 0.15, base + d * tip]
    jag = (lambda i, k, t: (0.25 * (1 if k % 2 else -0.4)) if t > 0.99 and broken else 0.0)
    K.tube(pts, lambda t: r * (1 - 0.06 * t), 6, mat, jag=jag, cap_end=not sharp or broken)
    if sharp and not broken:
        K.tube([base + d * tip, top], lambda t: r * (1 - t), 6, mat)
    return top


def stakes_row(name, seed, n=6, spacing=0.45, rails=True, broken=0.25):
    rng = random.Random(seed); K = Kit(name)
    tops = []
    for k in range(n):
        x = (k - (n - 1) / 2) * spacing + rng.uniform(-0.05, 0.05)
        h = rng.uniform(1.1, 1.6)
        b = rng.random() < broken
        if b: h *= rng.uniform(0.35, 0.6)
        tops.append((x, stake(K, rng, Vector((x, rng.uniform(-0.04, 0.04), 0)), h, rng.uniform(0.045, 0.06),
                              (rng.uniform(-0.1, 0.1), rng.uniform(-0.12, 0.12)), sharp=True, broken=b)))
    if rails:
        for z in (0.45, 0.95):
            pts = [Vector((tops[0][0] - 0.1, 0, z + rng.uniform(-0.05, 0.05)))]
            for k in range(1, 4): pts.append(Vector((tops[0][0] + (tops[-1][0] - tops[0][0]) * k / 3, rng.uniform(-0.03, 0.03), z + rng.uniform(-0.05, 0.05))))
            pts.append(Vector((tops[-1][0] + 0.1, 0, z)))
            if z > 0.9 and broken > 0:   # the upper rail hangs off on one end
                pts[-1].z = 0.05; pts[-2].z = z * 0.6
            K.tube(pts, lambda t: 0.035, 5, "SP_Wood", cap_start=True, cap_end=True)
            for (x, t) in tops:      # rope lashings
                if abs(x - pts[0].x) > 0.01:
                    K.tube([Vector((x - 0.05, 0.06, z - 0.03)), Vector((x + 0.05, 0.06, z + 0.03))], lambda t: 0.012, 4, "SP_Rope")
    return K


def sack(K, rng, c, scale, seed, lying=False):
    rot = Matrix.Rotation(math.radians(rng.uniform(70, 85)), 3, 'Y') @ Matrix.Rotation(rng.uniform(0, 6.28), 3, 'Z') if lying else Matrix.Rotation(rng.uniform(0, 6.28), 3, 'Z')
    r = (0.3 * scale, 0.24 * scale, 0.36 * scale)
    K.blob(c + Vector((0, 0, r[2] * (0.55 if not lying else 0.6))), r, "SP_Cloth", subdiv=2, lumpy=0.16,
           flatten_below=-r[2] * 0.5, seed=seed, rot=rot, rnd=rng.random())
    neck = c + rot @ Vector((0, 0, r[2] * 1.35)) + Vector((0, 0, r[2] * (0.55 if not lying else 0.6) - r[2] * 0.35))
    K.tube([neck - rot @ Vector((0, 0, 0.08 * scale)), neck + rot @ Vector((0.02, 0, 0.1 * scale))], lambda t: 0.06 * scale * (1 - 0.4 * t), 6, "SP_Cloth")
    K.tube([neck + rot @ Vector((0, 0, 0.1 * scale)), neck + rot @ Vector((0.05, 0.02, 0.18 * scale))], lambda t: 0.07 * scale * (1 - t), 6, "SP_Cloth")
    K.tube([neck - rot @ Vector((0, 0, 0.02 * scale))] + [neck + rot @ Vector((math.cos(a) * 0.065 * scale, math.sin(a) * 0.065 * scale, 0)) for a in [k * math.pi / 3 for k in range(7)]],
           lambda t: 0.012, 4, "SP_Rope")


def sacks(name, seed, layout):
    rng = random.Random(seed); K = Kit(name)
    for k, (x, y, s, lying) in enumerate(layout):
        sack(K, rng, Vector((x, y, 0)), s, seed + k * 5, lying)
    return K


def rags(name, seed, pieces=3, spread=0.7):
    rng = random.Random(seed); K = Kit(name)
    for p in range(pieces):
        sx, sy = rng.uniform(0.5, 1.0), rng.uniform(0.35, 0.7)
        cx, cy = rng.uniform(-spread, spread), rng.uniform(-spread, spread) * 0.6
        rot = Matrix.Rotation(rng.uniform(0, 6.28), 3, 'Z')
        sd = seed + p * 11
        def h(u, v, sd=sd, sx=sx, sy=sy):
            z = 0.012 + max(0, noise.noise(Vector((u * 3, v * 3, sd)))) * 0.09 + noise.noise(Vector((u * 9, v * 9, sd + 1))) * 0.012
            return z * (0.4 + 0.6 * math.sin(u * math.pi) * math.sin(v * math.pi))
        def keep(i, j, sd=sd):
            u, v = (i + 0.5) / 10, (j + 0.5) / 8
            e = min(u, 1 - u, v, 1 - v)
            return e > noise.noise(Vector((u * 6, v * 6, sd + 2))) * 0.12 + 0.02 and noise.noise(Vector((u * 4, v * 4, sd + 9))) < 0.42
        K.grid(10, 8, sx, sy, h, "SP_Cloth", keep=keep, center=Vector((cx, cy, 0)), rot=rot)
    return K


JAR = [(0.0, 0.0), (0.15, 0.0), (0.2, 0.03), (0.29, 0.18), (0.33, 0.36), (0.31, 0.5), (0.24, 0.62), (0.17, 0.68), (0.165, 0.72),
       (0.18, 0.745), (0.165, 0.75), (0.145, 0.72), (0.15, 0.66), (0.2, 0.6)]


def jar(name, seed, scale=1.0, broken=False, lid=False, tipped=False):
    rng = random.Random(seed); K = Kit(name)
    prof = [(r * scale, z * scale) for r, z in JAR]
    cut = None
    if broken:
        a0 = rng.uniform(0, 6.28)
        cut = lambda a, z: z > scale * (0.42 + 0.15 * math.cos(a - a0) + noise.noise(Vector((a * 2, 0, seed))) * 0.06) and math.cos(a - a0) > -0.2
    K.lathe(prof, 14, "SP_Clay", cut=cut, wobble=0.03, rng=rng)
    if lid:
        K.lathe([(0.0, 0.79 * scale), (0.21 * scale, 0.75 * scale), (0.2 * scale, 0.73 * scale), (0.0, 0.76 * scale)], 14, "SP_Clay")
    if broken:   # shards on the ground
        for s in range(4):
            a = rng.uniform(0, 6.28); d = rng.uniform(0.35, 0.6) * scale
            K.box((math.cos(a) * d, math.sin(a) * d, 0.012), (rng.uniform(0.08, 0.16) * scale, rng.uniform(0.06, 0.12) * scale, 0.02),
                  "SP_Clay", rot=Matrix.Rotation(rng.uniform(0, 6.28), 3, 'Z') @ Matrix.Rotation(rng.uniform(-0.3, 0.3), 3, 'X'))
    if tipped:
        M = Matrix.Rotation(math.radians(rng.uniform(70, 80)), 3, 'Y')
        for i, p in enumerate(K.v): K.v[i] = M @ p
        zmin = min(p.z for p in K.v)
        for i, p in enumerate(K.v): K.v[i] = p - Vector((0, 0, zmin + 0.02))
    return K


# ---------------------------------------------------------------------------------------------------------------
JOBS = []
def job(K, frost=1.0, bias=0.0, ao=0.6, fn=0.35, export_name=None):
    ob = K.build(frost=frost, frost_bias=bias, ao_height=ao, seed=hash(K.name) % 97, frost_noise=fn)
    JOBS.append(ob)
    REPORT[ob.name] = {"tris": tris(ob), "mats": [m.name for m in ob.data.materials],
                       "size": [round(ob.dimensions.x, 2), round(ob.dimensions.y, 2), round(ob.dimensions.z, 2)]}
    print("built", ob.name, REPORT[ob.name])
    return ob


job(dead_tree("SP_DeadTree_A", 11, 8.5), ao=2.5)
job(dead_tree("SP_DeadTree_B", 23, 7.0, lean=0.18, broken=0.45, spread=(35, 65), r0=0.26), ao=2.5)
job(dead_tree("SP_DeadTree_C", 37, 5.5, top_snap=True, children=(2, 4), r0=0.3, broken=0.6), ao=2.0)
job(dead_tree("SP_DeadTree_Far", 41, 8.0, maxdepth=2, children=(4, 5)), ao=2.5)
job(dead_shrub("SP_DeadShrub_A", 5, 1.2), ao=0.6)
job(dead_shrub("SP_DeadShrub_B", 6, 0.7, stems=(8, 10), spread_deg=55), ao=0.4)
job(dead_shrub("SP_DeadShrub_C", 7, 1.5, stems=(5, 7), spread_deg=60, flat=1.4, broken=0.5), ao=0.6)
job(grass_tuft("SP_GrassTuft_A", 3), bias=0.2, ao=0.35, fn=0.6)
job(grass_tuft("SP_GrassTuft_B", 4, n=(16, 22), hgt=(0.15, 0.32), broken=0.5), bias=0.2, ao=0.25, fn=0.6)
job(grass_tuft("SP_GrassTuft_C", 8, n=(36, 48), hgt=(0.3, 0.6), width=0.013), bias=0.2, ao=0.4, fn=0.6)
job(stump("SP_Stump_A", 9, 0.75, 0.32), ao=0.5)
job(stump("SP_Stump_B", 10, 0.45, 0.24), ao=0.4)
job(stump("SP_Stump_Cut", 12, 0.35, 0.28, cut=True), ao=0.3)
job(fg_branch("SP_FGBranch_A", 51, 5.0), ao=0.01)
job(fg_branch("SP_FGBranch_B", 52, 3.5), ao=0.01)
job(twigs("SP_Twigs", 61), ao=0.05)
job(straw_mat("SP_StrawMat_A", 71, body_parts(), feet=[((-0.98, 0.07, 0.0), (0.12, 0.05, 0.06)), ((-0.96, -0.08, 0.0), (0.12, 0.05, 0.06))]), ao=0.25)
job(straw_mat("SP_StrawMat_B", 72, body_parts(curl=0.6, scale=0.92, x0=0.05), size=(1.9, 1.3), rot_deg=0, ropes=(-0.2,)), ao=0.25)
job(straw_mat("SP_StrawMat_Two", 73, body_parts(y0=-0.32) + body_parts(y0=0.33, x0=0.08, scale=0.95), size=(2.2, 1.9), res=(36, 30), ropes=(-0.5, 0.15, 0.6)), ao=0.25)
job(straw_mat("SP_StrawMat_Pulled", 74, body_parts(), pulled=0.38, ropes=(-0.5,)), ao=0.25)
job(handcart("SP_Handcart_Broken", 81), ao=0.8)
job(cart_wheel("SP_CartWheel", 82), ao=0.2)
job(stakes_row("SP_Stakes_Row", 91), ao=1.0)
job(stakes_row("SP_Stakes_Broken", 92, n=4, broken=0.6), ao=1.0)
job(stakes_row("SP_Stakes_Bare", 93, n=3, rails=False, spacing=0.6, broken=0.3), ao=1.0)
job(sacks("SP_Sack_A", 101, [(0, 0, 1.0, False)]), ao=0.4)
job(sacks("SP_Sack_Pile", 102, [(0, 0, 1.0, False), (0.55, 0.15, 0.9, True), (-0.2, 0.5, 0.85, True)]), ao=0.4)
job(rags("SP_Rags_A", 111), ao=0.1, bias=0.1)
job(rags("SP_Rags_B", 112, pieces=2, spread=0.4), ao=0.1, bias=0.1)
job(jar("SP_Jar_A", 121), ao=0.5)
job(jar("SP_Jar_Lid", 122, scale=0.75, lid=True), ao=0.4)
job(jar("SP_Jar_Broken", 123, broken=True), ao=0.5)
job(jar("SP_Jar_Tipped", 124, scale=0.8, tipped=True), ao=0.3)

# lay everything out in rows for the work file / preview
x = 0.0; row = 0; y = 0.0
for ob in JOBS:
    w = max(ob.dimensions.x, 1.0)
    if x > 24: x = 0.0; y -= 10.0
    ob.location = (x + w * 0.5, y, 0); x += w + 1.0
for ob in JOBS:
    loc = ob.location.copy(); ob.location = (0, 0, 0)
    export(ob, os.path.join(OUT, ob.name + ".fbx"))
    ob.location = loc
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(WORK, "sp_kit.blend"))
with open(os.path.join(WORK, "kit_report.json"), "w") as f: json.dump(REPORT, f, indent=1)
print("DONE", len(JOBS))
