# Bamboo arena kit: bamboo clumps (culms with nodes, side branches and leaf clusters), a cheap far clump,
# cut stumps, fallen culms, a low stone altar ruin. Clumps are built at real size (culms up to 10 m).
# blender -b --factory-startup -P build_bamboo.py -- <unity BambooArena dir> <work dir>
# Vertex data (same convention as the reeds, read by Bamboo.shader):
#   UV0 = (around/across, along), UV1 = (height fraction 0..1, part: 0 leaf, 0.5 culm), Color = (rnd, stiffness, node mark, 1)
import bpy, sys, os, math, random
from mathutils import Vector, Matrix

args = sys.argv[sys.argv.index("--") + 1:]
OUT = os.path.join(args[0], "Models"); WORK = args[1]
os.makedirs(OUT, exist_ok=True); os.makedirs(WORK, exist_ok=True)
for o in list(bpy.data.objects): bpy.data.objects.remove(o, do_unlink=True)


class Kit:
    def __init__(self, name, H=1.0):
        self.name, self.H = name, H
        self.v, self.f, self.u0, self.u1, self.c, self.fm, self.mats = [], [], [], [], [], [], []

    def m(self, n):
        if n not in self.mats: self.mats.append(n)
        return self.mats.index(n)

    def vert(self, p, a0, a1, c):
        self.v.append(Vector(p)); self.u0.append(a0); self.u1.append(a1); self.c.append(c); return len(self.v) - 1

    def face(self, idx, mat="Bamboo"):
        self.f.append(tuple(idx)); self.fm.append(self.m(mat))

    def build(self):
        me = bpy.data.meshes.new(self.name)
        me.from_pydata([tuple(p) for p in self.v], [], self.f); me.update()
        a = me.uv_layers.new(name="UVMap"); b = me.uv_layers.new(name="UVData")
        ca = me.color_attributes.new("Col", 'BYTE_COLOR', 'CORNER')
        for poly, mi in zip(me.polygons, self.fm):
            poly.material_index = mi; poly.use_smooth = True
            for li, vi in zip(poly.loop_indices, poly.vertices):
                a.data[li].uv = self.u0[vi]; b.data[li].uv = self.u1[vi]; ca.data[li].color = self.c[vi]
        for n in self.mats: me.materials.append(bpy.data.materials.get(n) or bpy.data.materials.new(n))
        ob = bpy.data.objects.new(self.name, me); bpy.context.scene.collection.objects.link(ob); return ob


def culm(K, rnd, base, height, radius, lean, sides=6, segs=9, r=0.0):
    """Hollow-looking culm: a tapered tube made of internodes; every internode end gets a slightly fatter node ring."""
    axis_top = base + Vector((lean.x, lean.y, height))
    pts = []
    for k in range(segs + 1):
        t = k / segs
        # slight droop of the top third
        p = base.lerp(axis_top, t) + Vector((lean.x, lean.y, 0)) * 0.15 * t * t
        pts.append(p)
    rings = []
    for k, p in enumerate(pts):
        t = k / segs
        rad = radius * (1 - 0.55 * t)
        node = 1.0  # node mark for the shader (darker band)
        ring = []
        for s in range(sides + 1):
            a = 2 * math.pi * s / sides
            off = Vector((math.cos(a), math.sin(a), 0)) * rad * (1.12 if k % 1 == 0 else 1)
            ring.append(K.vert(p + off, (s / sides, t * height * 4), (p.z / K.H, 0.5), (r, 0.8, node, 1)))
        rings.append(ring)
    for a, b in zip(rings, rings[1:]):
        for s in range(sides):
            K.face([a[s], a[s + 1], b[s + 1], b[s]])
    return pts


def leaf_cluster(K, rnd, at, out, r, n=7, size=0.11):
    """A spray of long narrow bamboo leaves hanging from a twig end."""
    for i in range(n):
        a = math.atan2(out.y, out.x) + rnd.uniform(-1.3, 1.3)
        d = Vector((math.cos(a), math.sin(a), rnd.uniform(-1.1, -0.15))).normalized()   # hanging, drooping
        ln = size * rnd.uniform(0.8, 1.3)
        w = ln * 0.11
        side = Vector((-d.y, d.x, 0)).normalized()
        tip = at + d * ln + Vector((0, 0, -ln * rnd.uniform(0.3, 0.7)))
        mid = at.lerp(tip, 0.45) + Vector((0, 0, ln * 0.08))
        v0 = K.vert(at, (0.5, 0), (at.z / K.H, 0), (r, 0.3, 0, 1))
        v1 = K.vert(mid - side * w, (0, 0.45), (mid.z / K.H, 0), (r, 0.3, 0, 1))
        v2 = K.vert(mid + side * w, (1, 0.45), (mid.z / K.H, 0), (r, 0.3, 0, 1))
        v3 = K.vert(tip, (0.5, 1), (tip.z / K.H, 0), (r, 0.3, 0, 1))
        K.face([v0, v1, v3, v2], "BambooLeaf")


def bamboo_clump(name, seed, culms=6, radius=0.45, leafy=1.0, far=False, HS=10.0):
    """Clump at real size: culms up to HS metres (BambooField varies it only a little and uniformly, so leaves keep shape)."""
    rnd = random.Random(seed); K = Kit(name, H=HS)
    R, L = 2.5, 3.2   # metres per normalized unit: clump spread, twig size
    LL = 2.5          # leaf size
    for i in range(culms):
        a = rnd.uniform(0, 2 * math.pi); rr = rnd.uniform(0, radius) * R
        base = Vector((math.cos(a) * rr, math.sin(a) * rr, 0))
        h = rnd.uniform(0.75, 1.0) * HS
        lean = Vector((math.cos(a), math.sin(a), 0)) * rnd.uniform(0.02, 0.08) * HS * 0.5
        r = rnd.random()
        pts = culm(K, rnd, base, h, rnd.uniform(0.010, 0.014) * 3.5, lean, sides=4 if far else 6, segs=6 if far else 9, r=r)
        # side branches with leaf sprays on the upper 55 %
        nb = int((8 if far else 16) * leafy)
        for b in range(nb):
            t = rnd.uniform(0.45, 0.98)
            k = min(int(t * (len(pts) - 1)), len(pts) - 2)
            p = pts[k].lerp(pts[k + 1], 0.5)
            ba = rnd.uniform(0, 2 * math.pi)
            out = Vector((math.cos(ba), math.sin(ba), 0))
            tip = p + out * rnd.uniform(0.06, 0.16) * L + Vector((0, 0, rnd.uniform(0.0, 0.05) * L))
            if not far:  # thin twig
                side = Vector((-out.y, out.x, 0)) * 0.008
                t0 = K.vert(p - side, (0, 0), (p.z / K.H, 0.5), (r, 0.6, 0, 1)); t1 = K.vert(p + side, (1, 0), (p.z / K.H, 0.5), (r, 0.6, 0, 1))
                t2 = K.vert(tip + side, (1, 1), (tip.z / K.H, 0.5), (r, 0.6, 0, 1)); t3 = K.vert(tip - side, (0, 1), (tip.z / K.H, 0.5), (r, 0.6, 0, 1))
                K.face([t0, t1, t2, t3])
            leaf_cluster(K, rnd, tip, out, r, n=(6 if far else 9), size=(0.12 if far else 0.10) * LL)
    return K


def stump(name, seed, n=5):
    """Cut bamboo stumps: short culm stubs with slanted cut tops."""
    rnd = random.Random(seed); K = Kit(name)
    for i in range(n):
        a = rnd.uniform(0, 6.28); rr = rnd.uniform(0, 0.35)
        base = Vector((math.cos(a) * rr, math.sin(a) * rr, 0)); rad = rnd.uniform(0.035, 0.05); h = rnd.uniform(0.15, 0.6)
        sides = 8; ring0 = []; ring1 = []
        tilt = Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), 0)).normalized()
        for s in range(sides + 1):
            an = 2 * math.pi * s / sides; off = Vector((math.cos(an), math.sin(an), 0)) * rad
            top = h + off.dot(tilt) * 1.6
            ring0.append(K.vert(base + off, (s / sides, 0), (0, 0.5), (rnd.random(), 1, 0, 1)))
            ring1.append(K.vert(base + off + Vector((0, 0, top)), (s / sides, top * 4), (top, 0.5), (0.5, 1, 1, 1)))
        for s in range(sides): K.face([ring0[s], ring0[s + 1], ring1[s + 1], ring1[s]], "Stump")
        K.face(ring1[:-1][::-1], "StumpCut")
    return K


def fallen(name, seed, n=4):
    """Fallen dry culms lying on the ground with a few dead leaves."""
    rnd = random.Random(seed); K = Kit(name)
    for i in range(n):
        a = rnd.uniform(0, 6.28); d = Vector((math.cos(a), math.sin(a), 0))
        base = Vector((rnd.uniform(-0.8, 0.8), rnd.uniform(-0.8, 0.8), 0.03)); ln = rnd.uniform(1.8, 3.2); rad = rnd.uniform(0.045, 0.065)
        side = Vector((-d.y, d.x, 0)); segs = 6; rings = []
        for k in range(segs + 1):
            t = k / segs; p = base + d * ln * t + Vector((0, 0, rad))
            ring = []
            for s in range(7):
                an = 2 * math.pi * s / 6; off = side * math.cos(an) * rad + Vector((0, 0, 1)) * math.sin(an) * rad
                ring.append(K.vert(p + off, (s / 6, t * ln * 4), (0.02, 0.5), (rnd.random(), 1, 1 if k % 2 == 0 else 0, 1)))
            rings.append(ring)
        for r0, r1 in zip(rings, rings[1:]):
            for s in range(6): K.face([r0[s], r0[s + 1], r1[s + 1], r1[s]], "Fallen")
    return K


def tube(K, rnd, pts, rad, mat, r, sides=6, half=False, taper=0.0):
    """Tube (or open half-tube) along a polyline lying on the ground."""
    rings = []
    for k, p in enumerate(pts):
        t = k / (len(pts) - 1)
        d = (pts[min(k + 1, len(pts) - 1)] - pts[max(k - 1, 0)]).normalized()
        side = Vector((-d.y, d.x, 0)).normalized(); up = d.cross(side).normalized()
        if up.z < 0: up = -up
        rr = rad * (1 - taper * t)
        ring = []
        n = sides if not half else sides // 2
        for s in range(n + 1):
            an = (math.pi * s / n) if half else (2 * math.pi * s / sides)
            off = side * math.cos(an) * rr + up * math.sin(an) * rr
            if half: off = side * math.cos(an) * rr - up * math.sin(an) * rr * 0.9   # trough facing up, split edge on top
            ring.append(K.vert(p + off, (s / max(n, 1), t * 4), (0.02, 0.5), (r, 1, 1 if k % 2 == 0 else 0, 1)))
        rings.append(ring)
    for r0, r1 in zip(rings, rings[1:]):
        for s in range(len(r0) - 1): K.face([r0[s], r0[s + 1], r1[s + 1], r1[s]], mat)
        if half:   # inner face, so the open trough is not see-through
            for s in range(len(r0) - 1): K.face([r1[s], r1[s + 1], r0[s + 1], r0[s]], mat)


def fallen_pile(name, seed, n=7):
    """Dry culms fallen across each other (like a thinned grove), some broken in two."""
    rnd = random.Random(seed); K = Kit(name)
    for i in range(n):
        a = rnd.uniform(-0.6, 0.6) + (math.pi * 0.5 if i % 3 == 0 else 0)
        d = Vector((math.cos(a), math.sin(a), 0)); ln = rnd.uniform(2.2, 3.8); rad = rnd.uniform(0.045, 0.065)
        base = Vector((rnd.uniform(-0.9, 0.9), rnd.uniform(-0.9, 0.9), 0)) - d * ln * 0.5
        z0 = rad + (0.04 if i % 3 == 0 else 0)          # the cross pieces rest on the others
        broken = rnd.random() < 0.4
        pts = [base + d * ln * t + Vector((0, 0, z0 + math.sin(t * math.pi) * 0.02)) for t in [k / 6 for k in range(7)]]
        if broken:   # snapped in the middle: the second half kinks down to the ground
            kink = Vector((-d.y, d.x, 0)) * rnd.uniform(-0.25, 0.25)
            pts = pts[:4] + [p + kink * ((j + 1) / 3) - Vector((0, 0, 0.01 * j)) for j, p in enumerate(pts[4:])]
        tube(K, rnd, pts, rad, "Fallen", rnd.random())
    return K


def split_pieces(name, seed, n=6):
    """Short split bamboo halves and broken lengths lying flat."""
    rnd = random.Random(seed); K = Kit(name)
    for i in range(n):
        a = rnd.uniform(0, 6.28); d = Vector((math.cos(a), math.sin(a), 0)); ln = rnd.uniform(0.45, 1.2); rad = rnd.uniform(0.045, 0.065)
        base = Vector((rnd.uniform(-0.7, 0.7), rnd.uniform(-0.7, 0.7), 0))
        half = rnd.random() < 0.65
        pts = [base + d * ln * t + Vector((0, 0, rad * (0.9 if half else 1.0))) for t in (0, 0.33, 0.66, 1.0)]
        tube(K, rnd, pts, rad, "Fallen", rnd.random(), sides=8, half=half)
    return K


def shoots(name, seed, n=4):
    """Young bamboo shoots (juksun): stacked pointed sheaths, slightly curved, 0.2-0.55 m."""
    rnd = random.Random(seed); K = Kit(name)
    for i in range(n):
        a = rnd.uniform(0, 6.28); rr = rnd.uniform(0, 0.4) if i else 0
        base = Vector((math.cos(a) * rr, math.sin(a) * rr, -0.03)); h = rnd.uniform(0.3, 0.75); rad = h * rnd.uniform(0.16, 0.22)
        bend = Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), 0)) * h * 0.12
        sides, segs = 8, 6
        rings = []
        for k in range(segs + 1):
            t = k / segs
            c = base + Vector((0, 0, h * t)) + bend * t * t
            rr2 = rad * (1 - t) ** 0.8 * (1.08 if k % 2 else 1.0)    # sheath steps
            ring = [K.vert(c + Vector((math.cos(2 * math.pi * s / sides), math.sin(2 * math.pi * s / sides), 0)) * rr2,
                           (s / sides, t * 3), (t, 0.5), (rnd.random(), 1, k % 2, 1)) for s in range(sides + 1)]
            rings.append(ring)
        for r0, r1 in zip(rings, rings[1:]):
            for s in range(sides): K.face([r0[s], r0[s + 1], r1[s + 1], r1[s]], "Shoot")
    return K


def roots(name, seed, n=5):
    """Roots surfacing from the soil: thin crooked tubes, half sunk, branching from one point."""
    rnd = random.Random(seed); K = Kit(name)
    for i in range(n):
        a = rnd.uniform(0, 6.28); ln = rnd.uniform(0.8, 1.8); rad = rnd.uniform(0.03, 0.05)
        p = Vector((0, 0, 0)); pts = []
        for k in range(7):
            t = k / 6
            a += rnd.uniform(-0.35, 0.35)
            pts.append(p + Vector((0, 0, rad * 0.3 - t * rad * 0.8)))
            p = p + Vector((math.cos(a), math.sin(a), 0)) * ln / 6
        tube(K, rnd, pts, rad, "Root", rnd.random(), taper=0.6)
    return K


# ---------------------------------------------------------------- stone altar ruin (boxes)
class Box:
    def __init__(self, name): self.name, self.v, self.f, self.uv, self.fm, self.mats = name, [], [], [], [], []
    def m(self, n):
        if n not in self.mats: self.mats.append(n)
        return self.mats.index(n)
    def box(self, c, s, rz=0.0, mat="Stone", tilt=(0, 0)):
        mi = self.m(mat); cz, sz = math.cos(rz), math.sin(rz); hx, hy, hz = s[0] / 2, s[1] / 2, s[2] / 2; b = len(self.v)
        for z in (-hz, hz):
            for y in (-hy, hy):
                for x in (-hx, hx):
                    zz = z + x * tilt[0] + y * tilt[1]
                    self.v.append(Vector((c[0] + x * cz - y * sz, c[1] + x * sz + y * cz, c[2] + zz)))
        for q in [(0, 2, 3, 1), (4, 5, 7, 6), (0, 1, 5, 4), (2, 6, 7, 3), (0, 4, 6, 2), (1, 3, 7, 5)]:
            self.f.append(tuple(b + i for i in q)); self.fm.append(mi); self.uv.append([(0, 0), (s[0], 0), (s[0], s[1] + s[2]), (0, s[1] + s[2])])
    def build(self):
        me = bpy.data.meshes.new(self.name); me.from_pydata([tuple(p) for p in self.v], [], self.f); me.update()
        u = me.uv_layers.new(name="UVMap")
        for poly, uvs, mi in zip(me.polygons, self.uv, self.fm):
            poly.material_index = mi
            for li, t in zip(poly.loop_indices, uvs): u.data[li].uv = t
        for n in self.mats: me.materials.append(bpy.data.materials.get(n) or bpy.data.materials.new(n))
        ob = bpy.data.objects.new(self.name, me); bpy.context.scene.collection.objects.link(ob); return ob


def altar(name, seed):
    """Low stepped stone platform (old shrine base) with a broken stone pagoda and scattered slabs."""
    rnd = random.Random(seed); B = Box(name)
    for i, (w, h) in enumerate([(7.0, 0.22), (5.4, 0.2), (3.8, 0.2)]):
        z = sum(hh for _, hh in [(7.0, 0.22), (5.4, 0.2), (3.8, 0.2)][:i]) + h / 2
        # each step made of a few slabs with small gaps and jitter
        n = 4
        for sx in range(n):
            for sy in range(n):
                if rnd.random() < 0.08 * (i + 1): continue
                cw = w / n
                B.box((-w / 2 + cw * (sx + 0.5) + rnd.uniform(-0.03, 0.03), -w / 2 + cw * (sy + 0.5) + rnd.uniform(-0.03, 0.03), z - rnd.uniform(0, 0.04)),
                      (cw - 0.04, cw - 0.04, h), rnd.uniform(-0.03, 0.03), "Stone", (rnd.uniform(-0.02, 0.02), rnd.uniform(-0.02, 0.02)))
    top = 0.62
    # stone pagoda: alternating body blocks and wide roof slabs, top tier fallen
    y = top
    for k, (bw, rw) in enumerate([(0.9, 1.5), (0.7, 1.25), (0.55, 1.0)]):
        B.box((0, 0, y + 0.3), (bw, bw, 0.6), 0.0, "Stone2"); y += 0.6
        B.box((0, 0, y + 0.08), (rw, rw, 0.16), 0.0, "Stone2"); y += 0.16
    B.box((1.6, -2.2, 0.12), (1.0, 1.0, 0.18), 0.6, "Stone2", (0.12, 0.05))   # fallen roof slab
    B.box((-2.4, 1.9, 0.1), (0.5, 0.5, 0.55), 0.3, "Stone2", (0.0, 0.5))    # toppled block
    # two short posts at the front step (gate remains)
    for sx in (-1.4, 1.4): B.box((sx, -3.1, 0.6), (0.28, 0.28, 1.2), 0.0, "Stone2")
    return B


def export(ob, path):
    bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True); bpy.context.view_layer.objects.active = ob
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"MESH"}, apply_scale_options="FBX_SCALE_ALL",
                             bake_space_transform=True, axis_forward="-Z", axis_up="Y", mesh_smooth_type="FACE",
                             colors_type="LINEAR", add_leaf_bones=False, path_mode="STRIP")


ASSETS = {
    "BA_Bamboo_A": lambda: bamboo_clump("BA_Bamboo_A", 1, culms=6),
    "BA_Bamboo_B": lambda: bamboo_clump("BA_Bamboo_B", 2, culms=4, radius=0.3),
    "BA_Bamboo_C": lambda: bamboo_clump("BA_Bamboo_C", 3, culms=8, radius=0.6, leafy=1.2),
    "BA_Bamboo_Far": lambda: bamboo_clump("BA_Bamboo_Far", 4, culms=5, radius=0.6, far=True),
    "BA_Stumps": lambda: stump("BA_Stumps", 5),
    "BA_Fallen": lambda: fallen("BA_Fallen", 6),
    "BA_Altar": lambda: altar("BA_Altar", 7),
    "BA_FallenPile": lambda: fallen_pile("BA_FallenPile", 8),
    "BA_Split": lambda: split_pieces("BA_Split", 9),
    "BA_Shoots": lambda: shoots("BA_Shoots", 10),
    "BA_Roots": lambda: roots("BA_Roots", 11),
}
ONLY = args[2].split(",") if len(args) > 2 else None
for n, fn in ASSETS.items():
    if ONLY and n not in ONLY: continue
    ob = fn().build(); export(ob, os.path.join(OUT, n + ".fbx"))
    print(f"{n}: {len(ob.data.vertices)} verts, {sum(len(p.vertices) - 2 for p in ob.data.polygons)} tris")
x = 0
for ob in bpy.data.objects: ob.location.x = x; x += 2.5 if ob.name.startswith("BA_Bamboo") else 9
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(WORK, "bamboo_kit.blend"))
