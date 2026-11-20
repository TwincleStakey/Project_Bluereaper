# Underworld courtyard props. Run: blender -b -P build_props.py
# Blender axes: X = Unity X, Y = Unity Z (forward / up-screen), Z = up.
import bpy, bmesh, math, random, os
from mathutils import Vector, Matrix, Euler, noise

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.normpath(os.path.join(HERE, "..", "..", "Assets", "Underworld", "Models"))
# UW_ONLY_GROUND=1 rebuilds and exports only the ground slabs (plaza, platform, terrace, steps)
ONLY_GROUND = os.environ.get("UW_ONLY_GROUND") == "1"
os.makedirs(OUT, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)

COLORS = {
    "M_Stone": (0.24, 0.22, 0.27), "M_Rock": (0.16, 0.14, 0.18), "M_Red": (0.43, 0.14, 0.12),
    "M_Dark": (0.05, 0.04, 0.05), "M_Wood": (0.17, 0.12, 0.10), "M_Emit": (1.0, 0.55, 0.25),
    "M_Metal": (0.12, 0.11, 0.13), "M_Cloth": (0.72, 0.69, 0.77), "M_Roof": (0.08, 0.07, 0.09),
    "M_Leaf": (0.20, 0.08, 0.09), "M_Tal": (0.85, 0.78, 0.6), "M_Paper": (0.9, 0.25, 0.15),
}
MATS = {}
for n, c in COLORS.items():
    m = bpy.data.materials.new(n)
    m.diffuse_color = (*c, 1)
    MATS[n] = m


def tri_matrix(loc, rot=(0, 0, 0), scale=(1, 1, 1)):
    return Matrix.Translation(Vector(loc)) @ Euler(rot).to_matrix().to_4x4() @ Matrix.Diagonal((*scale, 1))


class B:
    def __init__(self, name, mats):
        self.name, self.mats, self.bm = name, mats, bmesh.new()

    def mi(self, m):
        return self.mats.index(m)

    def _fin(self, verts, m, M):
        bmesh.ops.transform(self.bm, matrix=M, verts=verts)
        for f in {f for v in verts for f in v.link_faces}:
            f.material_index = self.mi(m)
        return verts

    def box(self, size, loc, rot=(0, 0, 0), m=None):
        r = bmesh.ops.create_cube(self.bm, size=1.0)
        return self._fin(r["verts"], m or self.mats[0], tri_matrix(loc, rot, size))

    def cyl(self, r1, r2, h, loc, seg=12, rot=(0, 0, 0), m=None, cap=True, scale=(1, 1, 1)):
        r = bmesh.ops.create_cone(self.bm, cap_ends=cap, cap_tris=False, segments=seg, radius1=r1, radius2=r2, depth=h)
        return self._fin(r["verts"], m or self.mats[0], tri_matrix(loc, rot, scale))

    def ico(self, rad, loc, scale=(1, 1, 1), sub=2, m=None, rot=(0, 0, 0)):
        r = bmesh.ops.create_icosphere(self.bm, subdivisions=sub, radius=rad)
        return self._fin(r["verts"], m or self.mats[0], tri_matrix(loc, rot, scale))

    def seg(self, p0, p1, r0, r1, sides=6, m=None):
        p0, p1 = Vector(p0), Vector(p1)
        d = p1 - p0
        q = Vector((0, 0, 1)).rotation_difference(d.normalized())
        r = bmesh.ops.create_cone(self.bm, cap_ends=True, cap_tris=False, segments=sides, radius1=r0, radius2=r1, depth=d.length)
        M = Matrix.Translation((p0 + p1) / 2) @ q.to_matrix().to_4x4()
        return self._fin(r["verts"], m or self.mats[0], M)

    def torus(self, R, r, M, m=None, su=10, sv=6):
        grid = []
        for i in range(su):
            a = 2 * math.pi * i / su
            row = []
            for j in range(sv):
                b = 2 * math.pi * j / sv
                p = Vector(((R + r * math.cos(b)) * math.cos(a), (R + r * math.cos(b)) * math.sin(a) * 1.6, r * math.sin(b)))
                row.append(self.bm.verts.new(p))
            grid.append(row)
        for i in range(su):
            for j in range(sv):
                self.bm.faces.new((grid[i][j], grid[(i + 1) % su][j], grid[(i + 1) % su][(j + 1) % sv], grid[i][(j + 1) % sv]))
        vs = [v for row in grid for v in row]
        return self._fin(vs, m or self.mats[0], M)

    def build(self, smooth=False):
        bmesh.ops.recalc_face_normals(self.bm, faces=self.bm.faces)
        me = bpy.data.meshes.new(self.name)
        self.bm.to_mesh(me)
        self.bm.free()
        for m in self.mats:
            me.materials.append(MATS[m])
        for p in me.polygons:
            p.use_smooth = smooth
        ob = bpy.data.objects.new(self.name, me)
        bpy.context.scene.collection.objects.link(ob)
        return ob


def displace(verts, amp, freq, seed):
    off = Vector((seed * 7.1, seed * 3.3, seed * 1.7))
    for v in verts:
        n = v.co.normalized() if v.co.length > 1e-5 else Vector((0, 0, 1))
        v.co += n * amp * noise.fractal(v.co * freq + off, 0.6, 2.0, 4)


def rock_into(b, rad, loc, scale, seed, m="M_Rock", amp=0.35, sub=3, rot=(0, 0, 0)):
    r = bmesh.ops.create_icosphere(b.bm, subdivisions=sub, radius=rad)
    vs = r["verts"]
    displace(vs, amp * rad, 1.4 / rad, seed)
    # flatten a few facets for a chiseled look
    for v in vs:
        v.co.z = max(v.co.z, -rad * 0.75)
    return b._fin(vs, m, tri_matrix(loc, rot, scale))


# ---------------------------------------------------------------- gate (original hongsal-style shrine gate)
def make_gate():
    b = B("UW_Gate", ["M_Red", "M_Stone", "M_Roof", "M_Dark", "M_Tal"])
    for x in (-3.0, 3.0):
        b.cyl(0.55, 0.5, 0.9, (x, 0, 0.45), seg=8, m="M_Stone")
        b.cyl(0.62, 0.62, 0.12, (x, 0, 0.06), seg=8, m="M_Stone")
        b.cyl(0.24, 0.22, 5.4, (x, 0, 0.9 + 2.7), seg=14, m="M_Red")
        b.cyl(0.3, 0.3, 0.25, (x, 0, 1.05), seg=14, m="M_Dark")  # iron collar
    b.box((7.4, 0.32, 0.36), (0, 0, 5.1), m="M_Red")
    b.box((6.6, 0.28, 0.28), (0, 0, 6.05), m="M_Red")
    for i in range(13):  # vertical slats (hongsal)
        x = -2.7 + 5.4 * i / 12
        b.cyl(0.045, 0.03, 1.2, (x, 0, 5.6), seg=6, m="M_Red")
        b.cyl(0.0, 0.06, 0.28, (x, 0, 6.33), seg=6, m="M_Red", rot=(math.pi, 0, 0))
    b.cyl(0.36, 0.36, 0.08, (0, -0.2, 5.6), seg=20, rot=(math.pi / 2, 0, 0), m="M_Dark")
    b.cyl(0.26, 0.26, 0.1, (0, -0.22, 5.6), seg=20, rot=(math.pi / 2, 0, 0), m="M_Red")
    # dark upturned capping beam
    n = 16
    for i in range(n):
        x0, x1 = -4.4 + 8.8 * i / n, -4.4 + 8.8 * (i + 1) / n
        z0 = 6.55 + 0.45 * (abs(x0) / 4.4) ** 2.6
        z1 = 6.55 + 0.45 * (abs(x1) / 4.4) ** 2.6
        b.seg((x0, 0, z0), (x1, 0, z1), 0.3, 0.3, sides=4, m="M_Roof")
    b.box((8.2, 0.55, 0.12), (0, 0, 6.38), m="M_Roof")
    # sagging straw rope under the lintel with paper talismans
    pts = []
    for s_ in range(13):
        t = s_ / 12
        pts.append(Vector((-2.8 + 5.6 * t, -0.25, 4.75 - 0.55 * 4 * t * (1 - t))))
    for s_ in range(12):
        b.seg(pts[s_], pts[s_ + 1], 0.07, 0.07, sides=6, m="M_Dark")
    random.seed(13)
    for s_ in range(1, 12, 1):
        p = pts[s_]
        ln = random.uniform(0.5, 0.9)
        b.box((0.16, 0.015, ln), (p.x, p.y, p.z - ln / 2 - 0.05), rot=(random.uniform(-0.15, 0.15), 0, random.uniform(-0.1, 0.1)), m="M_Tal")
    return b.build()


# ---------------------------------------------------------------- lanterns
def make_lantern_tall():
    b = B("UW_LanternTall", ["M_Stone", "M_Emit", "M_Roof"])
    b.cyl(0.48, 0.44, 0.25, (0, 0, 0.125), seg=6)
    b.cyl(0.32, 0.3, 0.15, (0, 0, 0.32), seg=6)
    b.cyl(0.15, 0.13, 1.1, (0, 0, 0.95), seg=8)
    b.cyl(0.38, 0.32, 0.14, (0, 0, 1.56), seg=6)
    for sx in (-1, 1):
        for sy in (-1, 1):
            b.box((0.08, 0.08, 0.46), (sx * 0.18, sy * 0.18, 1.86))
    b.box((0.27, 0.27, 0.4), (0, 0, 1.86), m="M_Emit")
    b.cyl(0.55, 0.06, 0.38, (0, 0, 2.28), seg=6)
    b.ico(0.08, (0, 0, 2.52), sub=1)
    return b.build()


def make_lantern_low():
    b = B("UW_LanternLow", ["M_Stone", "M_Emit"])
    b.cyl(0.3, 0.26, 0.32, (0, 0, 0.16), seg=8)
    for a in range(4):
        ang = a * math.pi / 2 + math.pi / 4
        b.box((0.07, 0.07, 0.3), (math.cos(ang) * 0.19, math.sin(ang) * 0.19, 0.47))
    b.box((0.26, 0.26, 0.26), (0, 0, 0.47), m="M_Emit")
    b.cyl(0.48, 0.08, 0.24, (0, 0, 0.74), seg=8)
    b.ico(0.06, (0, 0, 0.9), sub=1)
    return b.build()


def make_lantern_pole():
    b = B("UW_LanternPole", ["M_Wood", "M_Paper", "M_Dark"])
    b.box((0.16, 0.16, 3.4), (0, 0, 1.7))
    b.box((1.3, 0.12, 0.12), (0.5, 0, 3.25))
    b.seg((1.0, 0, 3.2), (1.0, 0, 2.7), 0.01, 0.01, sides=4, m="M_Dark")
    b.cyl(0.24, 0.24, 0.5, (1.0, 0, 2.4), seg=12, m="M_Paper", scale=(1, 1, 1))
    for z in (2.13, 2.67):
        b.cyl(0.17, 0.17, 0.05, (1.0, 0, z), seg=12, m="M_Dark")
    b.ico(0.27, (1.0, 0, 2.4), scale=(1, 1, 0.95), sub=2, m="M_Paper")
    return b.build()


# ---------------------------------------------------------------- walls, fences, chains
def make_wall(i):
    random.seed(100 + i)
    b = B("UW_Wall_%d" % i, ["M_Stone", "M_Dark"])
    rows = 3
    for r in range(rows):
        x = -1.0 + (0.25 if r % 2 else 0)
        while x < 1.0 - 0.05:
            w = min(random.uniform(0.38, 0.6), 1.0 - x)
            keep = True
            if r == rows - 1 and random.random() < 0.3 + 0.15 * i:
                keep = False
            if keep:
                jit = (random.uniform(-0.03, 0.03), random.uniform(-0.04, 0.04), random.uniform(-0.03, 0.03))
                b.box((w - 0.03, 0.4 + jit[1], 0.28), (x + w / 2 + jit[0], jit[1], 0.15 + r * 0.3 + jit[2]),
                      rot=(random.uniform(-0.04, 0.04), random.uniform(-0.05, 0.05), random.uniform(-0.04, 0.04)))
            x += w
    b.box((0.34, 0.5, 1.15), (1.0, 0, 0.575))
    b.box((0.44, 0.6, 0.1), (1.0, 0, 1.18))
    return b.build()


def make_fence():
    random.seed(21)
    b = B("UW_Fence", ["M_Wood", "M_Dark"])
    posts = []
    for k in range(5):
        x = k * 2.0
        tilt = (random.uniform(-0.08, 0.08), random.uniform(-0.12, 0.12), 0)
        b.box((0.13, 0.13, 1.25), (x, 0, 0.55), rot=tilt)
        posts.append(Vector((x, 0, 1.0)))
    for k in range(4):
        for hz, sag in ((0.0, 0.3), (-0.4, 0.22)):
            p0, p1 = posts[k] + Vector((0, 0, hz)), posts[k + 1] + Vector((0, 0, hz))
            pts = []
            for s in range(9):
                t = s / 8
                p = p0.lerp(p1, t)
                p.z -= sag * 4 * t * (1 - t)
                pts.append(p)
            for s in range(8):
                b.seg(pts[s], pts[s + 1], 0.025, 0.025, sides=5, m="M_Dark")
    return b.build()


def make_chain(name, length, sag, link=0.32):
    b = B(name, ["M_Metal"])
    n = int(length / (link * 0.78))
    for i in range(n):
        t = (i + 0.5) / n
        x = t * length
        z = -sag * 4 * t * (1 - t)
        dz = -sag * 4 * (1 - 2 * t) / length
        ang = math.atan2(dz, 1)
        M = Matrix.Translation((x, 0, z)) @ Euler((0, -ang, 0)).to_matrix().to_4x4() @ \
            Euler((math.pi / 2 * (i % 2), 0, 0)).to_matrix().to_4x4() @ Euler((0, 0, math.pi / 2)).to_matrix().to_4x4()
        b.torus(link * 0.28, link * 0.07, M @ Matrix.Diagonal((1, 1, 1, 1)), su=10, sv=5)
    return b.build(smooth=True)


# ---------------------------------------------------------------- rocks, portal frame, pillars
def make_rock(i):
    b = B("UW_Rock_%d" % i, ["M_Rock"])
    rock_into(b, 1.0, (0, 0, 0.3), (1.0 + 0.3 * (i % 2), 0.9, 0.7 + 0.2 * (i % 3)), seed=i * 3 + 1)
    return b.build(smooth=True)


def make_pillar():
    b = B("UW_RockPillar", ["M_Rock", "M_Metal"])
    rock_into(b, 1.0, (0, 0, 3.8), (1.9, 1.7, 4.8), seed=11, amp=0.4)
    rock_into(b, 1.0, (0.6, 0.4, 8.4), (1.2, 1.1, 1.0), seed=12, amp=0.5)
    # iron bands wrapped around
    for z, rr in ((2.0, 1.95), (5.4, 1.8)):
        M = tri_matrix((0, 0, z), (0, 0, 0), (1, 1, 1))
        b.cyl(rr, rr, 0.22, (0, 0, z), seg=20, m="M_Metal", cap=False)
    return b.build(smooth=True)


def make_portal_frame():
    random.seed(5)
    b = B("UW_PortalFrame", ["M_Rock", "M_Stone"])
    R = 3.2
    for k in range(18):
        a = math.pi * 2 * k / 18
        x, z = math.cos(a) * R, math.sin(a) * R + 3.8
        s = 0.85 + 0.35 * random.random() + (0.5 if z < 3 else 0)
        rock_into(b, 1.0, (x, 0.2 * random.random(), z), (s, s * 0.9, s * 1.1), seed=40 + k, sub=2, amp=0.45,
                  rot=(random.random(), random.random(), random.random()))
    # massive carved crown above the ring
    rock_into(b, 1.0, (0, 1.2, 8.0), (4.2, 2.2, 2.4), seed=77, amp=0.5)
    rock_into(b, 1.0, (-3.2, 1.4, 6.6), (2.0, 2.0, 2.8), seed=78, amp=0.5)
    rock_into(b, 1.0, (3.3, 1.4, 6.3), (2.2, 2.0, 3.0), seed=79, amp=0.5)
    # horn-like spikes
    for sx in (-1, 1):
        b.seg((sx * 3.0, 1.0, 9.2), (sx * 4.6, 1.0, 11.6), 0.65, 0.05, sides=7)
    # base dais
    b.cyl(4.6, 4.9, 0.6, (0, 0.6, 0.3), seg=10, m="M_Stone")
    return b.build(smooth=True)


# ---------------------------------------------------------------- hooded figures
def robe(b, h, r1, r2, z0, m, folds=7, hem=0.0, seed=0, seg=24):
    random.seed(seed)
    r = bmesh.ops.create_cone(b.bm, cap_ends=False, cap_tris=False, segments=seg, radius1=r1, radius2=r2, depth=h)
    vs = r["verts"]
    for v in vs:
        a = math.atan2(v.co.y, v.co.x)
        t = (v.co.z + h / 2) / h  # 0 bottom .. 1 top
        rad = 1 + 0.09 * math.sin(a * folds + seed) * (1 - t)
        v.co.x *= rad
        v.co.y *= rad
        if t < 0.01 and hem > 0:
            v.co.z += random.uniform(0, hem)
            v.co.x *= 1 + random.uniform(-0.08, 0.12)
            v.co.y *= 1 + random.uniform(-0.08, 0.12)
    # loop cut for smoother folds
    b._fin(vs, m, Matrix.Translation((0, 0, z0 + h / 2)))
    return vs


def hooded(name, h, r1, mats, cloth, hem, seed, statue=False):
    b = B(name, mats)
    z0 = 0.0
    if statue:
        b.box((2.4, 2.4, 1.0), (0, 0, 0.5), m="M_Stone")
        b.box((2.7, 2.7, 0.2), (0, 0, 0.1), m="M_Stone")
        z0 = 1.0
    robe(b, h * 0.78, r1, r1 * 0.32, z0, cloth, hem=hem, seed=seed)
    # shoulders / hood
    b.ico(r1 * 0.5, (0, 0.04 * h, z0 + h * 0.84), scale=(0.95, 1.0, 1.3), sub=3, m=cloth)
    # veil down the back
    b.cyl(r1 * 0.45, r1 * 0.7, h * 0.7, (0, 0.18 * r1, z0 + h * 0.5), seg=12, m=cloth, cap=False, scale=(1, 0.6, 1))
    # face hollow
    b.ico(r1 * 0.28, (0, -r1 * 0.38, z0 + h * 0.82), scale=(0.85, 0.45, 1.05), sub=2, m="M_Dark")
    # sleeves hanging forward, hands together
    for sx in (-1, 1):
        b.seg((sx * r1 * 0.45, -r1 * 0.05, z0 + h * 0.7), (sx * r1 * 0.18, -r1 * 0.55, z0 + h * 0.42), r1 * 0.16, r1 * 0.24, sides=8, m=cloth)
    return b.build(smooth=True)


# ---------------------------------------------------------------- floating pagoda
def make_pagoda():
    random.seed(9)
    b = B("UW_Pagoda", ["M_Stone", "M_Roof", "M_Rock", "M_Red"])
    z = 0.0
    for k, w in enumerate((2.2, 1.7, 1.25)):
        b.box((w, w, 0.9), (0, 0, z + 0.45), m="M_Red" if k == 0 else "M_Stone")
        b.cyl(w * 1.05, w * 0.35, 0.45, (0, 0, z + 1.12), seg=4, rot=(0, 0, math.pi / 4), m="M_Roof")
        z += 1.35
    b.cyl(0.06, 0.02, 1.2, (0, 0, z + 0.4), seg=6, m="M_Roof")
    rock_into(b, 1.0, (0, 0, -0.9), (1.8, 1.7, 1.3), seed=91, amp=0.5)
    rock_into(b, 1.0, (0.3, 0.2, -2.1), (0.8, 0.8, 1.2), seed=92, amp=0.6)
    return b.build(smooth=False)


# ---------------------------------------------------------------- dead tree, shrubs, rubble
def make_tree(i):
    random.seed(300 + i)
    b = B("UW_DeadTree_%d" % i, ["M_Wood"])

    def branch(p, d, length, r, depth):
        q = p.copy()
        steps = 3
        for s in range(steps):
            d = (d + Vector((random.uniform(-0.25, 0.25), random.uniform(-0.25, 0.25), random.uniform(-0.05, 0.15)))).normalized()
            n = q + d * length / steps
            r1 = r * (1 - 0.6 * (s + 1) / steps)
            b.seg(q, n, r * (1 - 0.6 * s / steps), r1, sides=6)
            q = n
            if depth > 0 and s >= 1:
                for c in range(random.randint(1, 2)):
                    nd = (d + Vector((random.uniform(-1, 1), random.uniform(-1, 1), random.uniform(0.0, 0.6)))).normalized()
                    branch(q, nd, length * random.uniform(0.45, 0.65), r1 * 0.75, depth - 1)
    branch(Vector((0, 0, -0.2)), Vector((0, 0, 1)), 4.2 + i * 0.6, 0.22, 3)
    for k in range(4):
        a = k * math.pi / 2 + random.random()
        b.seg((0, 0, 0.3), (math.cos(a) * 0.9, math.sin(a) * 0.9, -0.15), 0.12, 0.03, sides=5)
    return b.build()


def make_shrub(i):
    random.seed(400 + i)
    b = B("UW_Shrub_%d" % i, ["M_Leaf"])
    for k in range(7 + i):
        a = random.random() * math.tau
        d = random.uniform(0, 0.7)
        rr = random.uniform(0.3, 0.6)
        r = bmesh.ops.create_icosphere(b.bm, subdivisions=1, radius=rr)
        displace(r["verts"], rr * 0.4, 2.5, k + i * 10)
        b._fin(r["verts"], "M_Leaf", tri_matrix((math.cos(a) * d, math.sin(a) * d, rr * 0.7 + random.uniform(0, 0.4))))
    return b.build()


def make_rubble(i):
    random.seed(500 + i)
    b = B("UW_Rubble_%d" % i, ["M_Stone"])
    for k in range(3 + i):
        s = (random.uniform(0.3, 1.1), random.uniform(0.25, 0.6), random.uniform(0.12, 0.35))
        vs = b.box(s, (random.uniform(-0.8, 0.8), random.uniform(-0.6, 0.6), s[2] * 0.3),
                   rot=(random.uniform(-0.3, 0.3), random.uniform(-0.3, 0.3), random.uniform(0, 3.1)))
        for v in vs:
            v.co += Vector((random.uniform(-0.05, 0.05), random.uniform(-0.05, 0.05), random.uniform(-0.04, 0.04)))
    return b.build()


# ---------------------------------------------------------------- ground: plaza + raised platforms with cliff skirts
def slab(name, outline, top, depth, jitter, seed, undercut=0.0):
    random.seed(seed)
    b = B(name, ["M_Stone", "M_Rock"])
    bm = b.bm
    # densify outline
    pts = []
    for k in range(len(outline)):
        a, c = Vector(outline[k]), Vector(outline[(k + 1) % len(outline)])
        n = max(1, int((c - a).length / 0.7))
        for s in range(n):
            pts.append(a.lerp(c, s / n))
    cx = sum(p.x for p in pts) / len(pts)
    cy = sum(p.y for p in pts) / len(pts)
    levels = [0.0, -0.18, -0.6, -1.4, -depth]
    rings = []
    for li, dz in enumerate(levels):
        ring = []
        for p in pts:
            dirv = Vector((p.x - cx, p.y - cy)).normalized()
            j = 0.0 if li == 0 else jitter * (0.4 + li * 0.35) * (noise.noise(Vector((p.x * 0.7, p.y * 0.7, li * 1.3 + seed))) + 0.2)
            inward = undercut * (li / (len(levels) - 1)) ** 1.5
            q = Vector((p.x + dirv.x * (j - inward), p.y + dirv.y * (j - inward), top + dz + (random.uniform(-0.1, 0.1) if li > 1 else 0)))
            ring.append(bm.verts.new(q))
        rings.append(ring)
    n = len(pts)
    topf = bm.faces.new(rings[0])
    topf.material_index = 0
    for li in range(len(levels) - 1):
        for k in range(n):
            f = bm.faces.new((rings[li][k], rings[li][(k + 1) % n], rings[li + 1][(k + 1) % n], rings[li + 1][k]))
            f.material_index = 0 if li == 0 else 1
    bot = bm.faces.new(list(reversed(rings[-1])))
    bot.material_index = 1
    bmesh.ops.triangulate(bm, faces=[topf, bot])
    return b.build()


def make_ground():
    # 11-10: 88 x 56 m plaza (2x per side, was 44 x 28). The portal platform moves +10/+14 and the gate terrace
    # -22/-6 as whole pieces (same size), matching UWBuilder's B() and L() shifts.
    slab("UW_Plaza", [(-44, -21), (-36, -29), (44, -29), (44, 21), (36, 27), (-44, 27)], 0.0, 7.0, 0.5, 1, undercut=2.5)
    slab("UW_PlatformBack", [(12, 23), (28, 23), (28.5, 31.5), (11.5, 31.5)], 1.2, 1.6, 0.15, 2)
    slab("UW_GateTerrace", [(-44, -16), (-36, -16), (-36, -5.5), (-44, -5.5)], 0.9, 1.3, 0.15, 3)
    b = B("UW_Steps", ["M_Stone"])
    for k in (1, 2, 3):
        b.box((6.0, 0.45, 1.2 - 0.3 * k), (20.0, 23 - 0.45 * k + 0.225, (1.2 - 0.3 * k) / 2))
    for k in (1, 2):
        b.box((0.45, 3.4, 0.9 - 0.3 * k), (-36 + 0.45 * k - 0.225, -10.75, (0.9 - 0.3 * k) / 2))
    b.build()


def make_bridge():
    # wooden plank bridge, spans local +X from 0 to 20 m, deck top at z = 0
    random.seed(61)
    b = B("UW_Bridge", ["M_Wood", "M_Dark"])
    L, W = 20.0, 2.6
    x = 0.0
    while x < L:
        gone = x > 11 and random.random() < 0.12 + 0.02 * (x - 11)
        if not gone:
            b.box((0.27, W + random.uniform(-0.15, 0.15), 0.08), (x + 0.15, random.uniform(-0.06, 0.06), -0.04),
                  rot=(random.uniform(-0.04, 0.04), random.uniform(-0.03, 0.03), random.uniform(-0.05, 0.05)))
        x += 0.32
    for y in (-1.1, 1.1):
        b.box((L, 0.18, 0.26), (L / 2, y, -0.21))
    posts = {-1: [], 1: []}
    for k in range(9):
        px = k * 2.5
        for sy in (-1, 1):
            lean = (random.uniform(-0.1, 0.1), random.uniform(-0.08, 0.08), 0)
            b.box((0.14, 0.14, 1.35), (px, sy * 1.36, 0.5), rot=lean)
            posts[sy].append(Vector((px, sy * 1.36, 1.05)))
            if k % 2 == 0:
                b.seg((px, sy * 1.15, -0.2), (px + random.uniform(-0.3, 0.3), sy * 1.4, -6.5), 0.16, 0.12, sides=6)
    for sy in (-1, 1):
        ps = posts[sy]
        for k in range(len(ps) - 1):
            if k == 6 and sy == 1:
                continue  # snapped rope
            for hz, sag in ((0.0, 0.32), (-0.45, 0.25)):
                p0, p1 = ps[k] + Vector((0, 0, hz)), ps[k + 1] + Vector((0, 0, hz))
                pts = []
                for s_ in range(7):
                    t = s_ / 6
                    p = p0.lerp(p1, t)
                    p.z -= sag * 4 * t * (1 - t)
                    pts.append(p)
                for s_ in range(6):
                    b.seg(pts[s_], pts[s_ + 1], 0.025, 0.025, sides=5, m="M_Dark")
    return b.build()


def make_bridge_wide():
    # wide plank walkway (reference bottom-left): local +X 0..30 m, 7 m wide, deck top z = 0.
    # rope fence full length on -Y (outer, abyss side); +Y (arena side) only past x = 22 where the arena ends
    random.seed(77)
    b = B("UW_BridgeWide", ["M_Wood", "M_Dark"])
    L, W = 30.0, 7.0
    x = 0.0
    while x < L:
        gone = x > 24 and random.random() < 0.15
        if not gone:
            b.box((0.36, W + random.uniform(-0.2, 0.2), 0.1), (x + 0.2, random.uniform(-0.08, 0.08), -0.05),
                  rot=(random.uniform(-0.03, 0.03), random.uniform(-0.02, 0.02), random.uniform(-0.03, 0.03)))
        x += 0.42
    for y in (-3.2, -1.1, 1.1, 3.2):
        b.box((L, 0.22, 0.3), (L / 2, y, -0.25))
    for k in range(11):
        px = k * 3.0
        for y in (-3.3, 3.3):
            b.seg((px, y, -0.25), (px + random.uniform(-0.3, 0.3), y * 1.05, -8.0), 0.2, 0.15, sides=6)
    def fence(y, x0, x1):
        posts = []
        n = int((x1 - x0) / 2.5)
        for k in range(n + 1):
            px = x0 + (x1 - x0) * k / n
            b.box((0.16, 0.16, 1.4), (px, y, 0.6), rot=(random.uniform(-0.08, 0.08), random.uniform(-0.08, 0.08), 0))
            posts.append(Vector((px, y, 1.15)))
        for k in range(len(posts) - 1):
            for hz, sag in ((0.0, 0.3), (-0.5, 0.24)):
                p0, p1 = posts[k] + Vector((0, 0, hz)), posts[k + 1] + Vector((0, 0, hz))
                pts = [p0.lerp(p1, t / 6) - Vector((0, 0, sag * 4 * (t / 6) * (1 - t / 6))) for t in range(7)]
                for t in range(6):
                    b.seg(pts[t], pts[t + 1], 0.03, 0.03, sides=5, m="M_Dark")
    fence(-3.45, 0.5, L)
    fence(3.45, 22.5, L)
    return b.build()


# ---------------------------------------------------------------- run
if not ONLY_GROUND:
    make_gate(); make_bridge(); make_bridge_wide(); make_lantern_tall(); make_lantern_low(); make_lantern_pole()
    for i in range(3):
        make_wall(i)
    make_fence()
    make_chain("UW_Chain", 10.0, 1.6)
    make_chain("UW_ChainShort", 4.0, 0.5)
    for i in range(5):
        make_rock(i)
    make_pillar(); make_portal_frame()
    hooded("UW_Statue", 4.2, 1.15, ["M_Stone", "M_Dark"], "M_Stone", 0.0, 3, statue=True)
    hooded("UW_Ghost", 1.85, 0.5, ["M_Cloth", "M_Dark"], "M_Cloth", 0.35, 8)
    hooded("UW_GhostTall", 2.2, 0.42, ["M_Cloth", "M_Dark"], "M_Cloth", 0.5, 12)
    make_pagoda()
    for i in range(3):
        make_tree(i)
    for i in range(3):
        make_shrub(i)
    for i in range(4):
        make_rubble(i)
make_ground()

for ob in list(bpy.context.scene.objects):
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, ob.name + ".fbx"), use_selection=True,
                             apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
                             bake_space_transform=True, object_types={"MESH"}, mesh_smooth_type="FACE",
                             use_mesh_modifiers=True, add_leaf_bones=False)
if not ONLY_GROUND:
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, "underworld_props.blend"))
print("EXPORTED", len(bpy.context.scene.objects))
