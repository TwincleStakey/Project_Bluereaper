# Reed field kit: 억새 (silver grass) and 갈대 (common reed) clumps plus a broken boardwalk.
# blender -b --factory-startup -P build_reeds.py -- <unity ReedField dir> <work dir>
#
# Every reed clump is 1.0 m tall in the file; ReedField scales instances to the height set in the inspector.
# Vertex data read by ReedField/Reed.shader:
#   UV0   = (across the strand 0..1, along the strand 0..1)
#   UV1   = (height fraction of the vertex in the clump 0..1, part: 0 leaf, 0.5 stem, 1 plume)
#   Color = (per-stem random, stiffness, plume tip fraction, 1)
import bpy, sys, os, math, random
from mathutils import Vector

args = sys.argv[sys.argv.index("--") + 1:]
OUT = os.path.join(args[0], "Models")
WORK = args[1]
os.makedirs(OUT, exist_ok=True); os.makedirs(WORK, exist_ok=True)

for o in list(bpy.data.objects): bpy.data.objects.remove(o, do_unlink=True)


class Kit:
    def __init__(self, name, height=1.0):
        self.name, self.H = name, height
        self.v, self.f, self.uv0, self.uv1, self.col, self.mat = [], [], [], [], [], []
        self.mats = []

    def vert(self, p, a0, a1, c):
        self.v.append(Vector(p)); self.uv0.append(a0); self.uv1.append(a1); self.col.append(c)
        return len(self.v) - 1

    def m(self, name):
        if name not in self.mats: self.mats.append(name)
        return self.mats.index(name)

    # A tapered ribbon along points pts. side(i) gives the across direction, width(t) the half width.
    def ribbon(self, pts, width, side, part, rnd, stiff, mat="Reed", tipcol=None):
        mi = self.m("Reed")  # one submesh: leaf/stem/plume are told apart by UV1, not by material
        n = len(pts); rows = []
        L = [0.0]
        for i in range(1, n): L.append(L[-1] + (pts[i] - pts[i - 1]).length)
        tot = max(L[-1], 1e-5)
        for i, p in enumerate(pts):
            t = L[i] / tot
            s = side(i).normalized() * width(t)
            hf = max(0.0, min(1.0, p.z / self.H))
            tc = tipcol(t) if tipcol else 0.0
            c = (rnd, stiff, tc, 1.0)
            if width(t) < 1e-4:
                rows.append((self.vert(p, (0.5, t), (hf, part), c),))
            else:
                rows.append((self.vert(p - s, (0.0, t), (hf, part), c), self.vert(p + s, (1.0, t), (hf, part), c)))
        for a, b in zip(rows, rows[1:]):
            if len(a) == 2 and len(b) == 2: self.f.append(((a[0], a[1], b[1], b[0]), mi))
            elif len(a) == 2: self.f.append(((a[0], a[1], b[0]), mi))
            elif len(b) == 2: self.f.append(((a[0], b[1], b[0]), mi))

    def build(self):
        me = bpy.data.meshes.new(self.name)
        me.from_pydata([tuple(p) for p in self.v], [], [f[0] for f in self.f])
        me.update()
        u0 = me.uv_layers.new(name="UVMap"); u1 = me.uv_layers.new(name="UVData")
        ca = me.color_attributes.new("Col", 'BYTE_COLOR', 'CORNER')
        for poly, (idx, mi) in zip(me.polygons, self.f):
            poly.material_index = mi; poly.use_smooth = True
            for li, vi in zip(poly.loop_indices, poly.vertices):
                u0.data[li].uv = self.uv0[vi]; u1.data[li].uv = self.uv1[vi]; ca.data[li].color = self.col[vi]
        for n in self.mats: me.materials.append(bpy.data.materials.get(n) or bpy.data.materials.new(n))
        ob = bpy.data.objects.new(self.name, me)
        bpy.context.scene.collection.objects.link(ob)
        return ob


def horiz_perp(d):
    h = Vector((d.x, d.y, 0))
    if h.length < 1e-4: return Vector((1, 0, 0))
    return Vector((-h.y, h.x, 0))


def curve(base, direction, length, droop, segs, lean_dir=None, lean=0.0, wobble=0.0, rnd=None):
    """Points of a strand that leaves base along direction and droops under its own weight."""
    pts = []; d = direction.normalized()
    for k in range(segs + 1):
        t = k / segs
        p = base + d * (length * t)
        p.z -= droop * length * t * t
        if lean_dir is not None: p += lean_dir * (lean * length * t ** 2.2)
        if wobble and rnd: p += Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), 0)) * wobble * t
        pts.append(p)
    return pts


def leaf_blade(K, rnd, base, a, length, up, droop, w, stiff=0.35):
    """Long arching grass leaf. Leaves fan outward from the clump, so they read from above."""
    out = Vector((math.cos(a), math.sin(a), 0))
    d = (out * (1 - up) + Vector((0, 0, up))).normalized()
    pts = curve(base, d, length, droop, 6)
    side = horiz_perp(d)
    if side.length < 1e-3: side = Vector((1, 0, 0))
    K.ribbon(pts, lambda t: w * (1 - t) ** 0.8 * (0.5 + 0.5 * min(1, t * 6)), lambda i: side, 0.0, rnd.random(), stiff, "ReedLeaf")


def stem(K, rnd, base, top, w, r, stiff):
    mid = base.lerp(top, 0.55) + Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), 0)) * 0.02
    pts = [base, base.lerp(mid, 0.5), mid, mid.lerp(top, 0.5), top]
    d = top - base
    s1 = horiz_perp(d) if horiz_perp(d).length > 1e-3 else Vector((1, 0, 0))
    s2 = s1.cross(d.normalized())
    K.ribbon(pts, lambda t: w * (1 - 0.6 * t), lambda i: s1, 0.5, r, stiff, "ReedStem")
    K.ribbon(pts, lambda t: w * (1 - 0.6 * t), lambda i: s2, 0.5, r, stiff, "ReedStem")


def susuki_plume(K, rnd, top, axis, r, stiff, n=26, length=0.26, comb=None):
    """억새 이삭: a fan of thin drooping branchlets at the stem tip, combed a little to one side."""
    for i in range(n):
        a = rnd.uniform(0, 2 * math.pi)
        out = Vector((math.cos(a), math.sin(a), 0))
        if comb is not None: out = (out * 0.55 + comb * 0.6).normalized()
        start = top - axis * (length * 0.8 * (i / n) ** 0.8)
        d = (axis * rnd.uniform(0.55, 0.9) + out * rnd.uniform(0.35, 0.7)).normalized()
        ln = length * rnd.uniform(0.6, 1.1)
        pts = curve(start, d, ln, rnd.uniform(0.5, 0.95), 3, wobble=0.02, rnd=rnd)
        side = horiz_perp(d)
        if side.length < 1e-3: side = Vector((1, 0, 0))
        wd = rnd.uniform(0.022, 0.032)
        K.ribbon(pts, lambda t: wd * (0.35 + 0.65 * math.sin(min(t, 0.999) * math.pi) ** 0.5) * (1 - 0.7 * t), lambda k: side, 1.0, r, stiff, "ReedPlume",
                 tipcol=lambda t: t)


def reed_panicle(K, rnd, top, axis, r, stiff, length=0.3):
    """갈대 이삭: a denser, darker panicle that hangs over to one side like a feather duster."""
    hang = Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), 0)).normalized()
    for i in range(18):
        f = i / 17
        start = top - axis * (length * 0.75 * f)
        a = rnd.uniform(0, 2 * math.pi)
        out = (Vector((math.cos(a), math.sin(a), 0)) * 0.5 + hang * 0.8).normalized()
        d = (axis * (0.6 - 0.3 * f) + out * 0.7).normalized()
        ln = length * (0.45 + 0.4 * (1 - f)) * rnd.uniform(0.8, 1.1)
        pts = curve(start, d, ln, rnd.uniform(0.6, 1.1), 3, wobble=0.015, rnd=rnd)
        side = horiz_perp(d)
        if side.length < 1e-3: side = Vector((1, 0, 0))
        wd = rnd.uniform(0.026, 0.036)
        K.ribbon(pts, lambda t: wd * (0.4 + 0.6 * math.sin(min(t, 0.999) * math.pi)), lambda k: side, 1.0, r, stiff, "ReedPlume", tipcol=lambda t: t)


def susuki_clump(name, seed, leaves=26, stems=7, radius=0.16, broken=0, plume=(14, 18)):
    rnd = random.Random(seed); K = Kit(name)
    comb = Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), 0)).normalized()
    for i in range(leaves):
        a = rnd.uniform(0, 2 * math.pi); rr = rnd.uniform(0, radius)
        base = Vector((math.cos(a) * rr, math.sin(a) * rr, 0))
        leaf_blade(K, rnd, base, a, rnd.uniform(0.45, 0.75), rnd.uniform(0.55, 0.85), rnd.uniform(0.35, 0.7), rnd.uniform(0.022, 0.034))
    for i in range(stems):
        a = rnd.uniform(0, 2 * math.pi); rr = rnd.uniform(0, radius * 0.7)
        base = Vector((math.cos(a) * rr, math.sin(a) * rr, 0))
        h = rnd.uniform(0.78, 1.0)
        lean = Vector((math.cos(a), math.sin(a), 0)) * rnd.uniform(0.04, 0.16) * h
        top = base + Vector((0, 0, h)) + lean
        r = rnd.random()
        if i < broken:   # snapped stem: the top half folds over and hangs (eerie, trampled look)
            kink = base + Vector((0, 0, h * 0.5)) + lean * 0.5
            stem(K, rnd, base, kink, 0.009, r, 0.9)
            d = (Vector((math.cos(a), math.sin(a), 0)) + Vector((0, 0, -0.35))).normalized()
            top2 = kink + d * h * 0.42
            stem(K, rnd, kink, top2, 0.007, r, 0.9)
            susuki_plume(K, rnd, top2, d, r, 0.9, n=16, length=0.2)
            continue
        stem(K, rnd, base, top, 0.009, r, 0.55)
        # a couple of stem leaves
        for k in range(1):
            p = base.lerp(top, rnd.uniform(0.25, 0.55))
            leaf_blade(K, rnd, p, rnd.uniform(0, 2 * math.pi), rnd.uniform(0.2, 0.32), 0.35, 0.8, 0.016, 0.6)
        susuki_plume(K, rnd, top, (top - base).normalized(), r, 0.4, n=rnd.randint(*plume), comb=comb)
    return K


def reed_clump(name, seed, stems=6, radius=0.18):
    rnd = random.Random(seed); K = Kit(name)
    for i in range(stems):
        a = rnd.uniform(0, 2 * math.pi); rr = rnd.uniform(0, radius)
        base = Vector((math.cos(a) * rr, math.sin(a) * rr, 0))
        h = rnd.uniform(0.82, 1.0)
        top = base + Vector((0, 0, h)) + Vector((math.cos(a), math.sin(a), 0)) * rnd.uniform(0.02, 0.1)
        r = rnd.random()
        stem(K, rnd, base, top, 0.011, r, 0.6)
        # 갈대 leaves: alternate up the stem, stick out flat and bend down at the tip
        for k in range(5):
            f = 0.12 + k * 0.15 + rnd.uniform(-0.03, 0.03)
            p = base.lerp(top, f)
            aa = a + k * 2.4 + rnd.uniform(-0.4, 0.4)
            leaf_blade(K, rnd, p, aa, rnd.uniform(0.22, 0.34) * (1.1 - f * 0.5), rnd.uniform(0.15, 0.4), rnd.uniform(0.7, 1.2), rnd.uniform(0.022, 0.03), 0.5)
        reed_panicle(K, rnd, top, (top - base).normalized(), r, 0.45)
    for i in range(8):  # low leaves at the root
        a = rnd.uniform(0, 2 * math.pi)
        leaf_blade(K, rnd, Vector((0, 0, 0)), a, rnd.uniform(0.25, 0.4), 0.5, 0.6, 0.024)
    return K


def low_grass(name, seed, n=20, radius=0.3):
    """Short dry grass under the reeds, so the ground between clumps is not bare."""
    rnd = random.Random(seed); K = Kit(name)
    for i in range(n):
        a = rnd.uniform(0, 2 * math.pi); rr = rnd.uniform(0, radius)
        base = Vector((math.cos(a) * rr, math.sin(a) * rr, 0))
        leaf_blade(K, rnd, base, a, rnd.uniform(0.28, 0.5), rnd.uniform(0.5, 0.85), rnd.uniform(0.4, 0.9), rnd.uniform(0.02, 0.03), 0.3)
    return K


def litter(name, seed, n=14, radius=0.7):
    """Fallen reed stalks, broken plumes and dead leaves lying flat on the ground (paths and clearings)."""
    rnd = random.Random(seed); K = Kit(name)
    for i in range(n):
        a = rnd.uniform(0, 2 * math.pi)
        base = Vector((rnd.uniform(-radius, radius), rnd.uniform(-radius, radius), 0.005))
        d = Vector((math.cos(a), math.sin(a), 0))
        ln = rnd.uniform(0.5, 1.3)
        pts = [base + d * ln * (k / 4) + Vector((0, 0, 0.012 + 0.025 * math.sin(math.pi * k / 4) * rnd.random())) for k in range(5)]
        side = horiz_perp(d)
        kind = rnd.random()
        if kind < 0.5:    # stalk
            K.ribbon(pts, lambda t: 0.012 * (1 - 0.4 * t), lambda k: side, 0.5, rnd.random(), 0.9)
        elif kind < 0.8:  # dead leaf
            K.ribbon(pts, lambda t: 0.03 * (1 - t) ** 0.7, lambda k: side, 0.0, rnd.random(), 0.9)
        else:             # broken plume tuft
            tip = pts[-1]
            for j in range(6):
                aa = a + rnd.uniform(-0.7, 0.7)
                dd = Vector((math.cos(aa), math.sin(aa), 0))
                pp = [tip + dd * 0.22 * (k / 3) + Vector((0, 0, 0.02)) for k in range(4)]
                K.ribbon(pp, lambda t: 0.018 * (1 - 0.6 * t), lambda k: horiz_perp(dd), 1.0, rnd.random(), 0.9, tipcol=lambda t: t)
            K.ribbon(pts, lambda t: 0.011, lambda k: side, 0.5, rnd.random(), 0.9)
    return K


def card_clump(name, seed, stems=9, radius=0.45):
    """Very cheap far clump (about 200 tris): single-ribbon stems with two crossed wide plume cards each.
    Used outside the arena, where the camera only sees the tops from far away."""
    rnd = random.Random(seed); K = Kit(name)
    for i in range(stems):
        a = rnd.uniform(0, 2 * math.pi); rr = rnd.uniform(0, radius)
        base = Vector((math.cos(a) * rr, math.sin(a) * rr, 0))
        h = rnd.uniform(0.8, 1.0)
        top = base + Vector((0, 0, h)) + Vector((math.cos(a), math.sin(a), 0)) * rnd.uniform(0.03, 0.12)
        r = rnd.random()
        side = Vector((-math.sin(a), math.cos(a), 0))
        K.ribbon([base, base.lerp(top, 0.5), top], lambda t: 0.012 * (1 - 0.5 * t), lambda k: side, 0.5, r, 0.6)
        for c in range(2):
            ang = a + c * math.pi / 2 + rnd.uniform(-0.3, 0.3)
            out = Vector((math.cos(ang), math.sin(ang), 0))
            d = (Vector((0, 0, 1)) * 0.8 + out * 0.6).normalized()
            pts = curve(top - Vector((0, 0, 0.18)), d, 0.34, 0.6, 3)
            K.ribbon(pts, lambda t: 0.07 * math.sin(min(t, 0.999) * math.pi) ** 0.6 + 0.01, lambda k: horiz_perp(d) if horiz_perp(d).length > 1e-3 else Vector((1, 0, 0)), 1.0, r, 0.5, tipcol=lambda t: t)
    for i in range(4):
        a = rnd.uniform(0, 2 * math.pi)
        leaf_blade(K, rnd, Vector((0, 0, 0)), a, rnd.uniform(0.4, 0.6), 0.6, 0.6, 0.05, 0.4)
    return K


# ---------------------------------------------------------------- boardwalk (real boxes, with gaps and broken planks)
class Box:
    def __init__(self, name): self.name, self.v, self.f, self.uv, self.fm, self.mats = name, [], [], [], [], []

    def m(self, n):
        if n not in self.mats: self.mats.append(n)
        return self.mats.index(n)

    def box(self, c, s, rot_z=0.0, tilt=(0.0, 0.0), mat="BW_Wood"):
        mi = self.m(mat)
        cz, sz = math.cos(rot_z), math.sin(rot_z)
        hx, hy, hz = s[0] / 2, s[1] / 2, s[2] / 2
        b = len(self.v)
        for z in (-hz, hz):
            for y in (-hy, hy):
                for x in (-hx, hx):
                    zz = z + x * tilt[0] + y * tilt[1]
                    self.v.append(Vector((c[0] + x * cz - y * sz, c[1] + x * sz + y * cz, c[2] + zz)))
        F = [(0, 2, 3, 1), (4, 5, 7, 6), (0, 1, 5, 4), (2, 6, 7, 3), (0, 4, 6, 2), (1, 3, 7, 5)]
        for q in F:
            self.f.append(tuple(b + i for i in q)); self.fm.append(mi)
            # UVs in metres along the longest axis
            self.uv.append([(0, 0), (s[0], 0), (s[0], s[1] + s[2]), (0, s[1] + s[2])])

    def build(self):
        me = bpy.data.meshes.new(self.name)
        me.from_pydata([tuple(p) for p in self.v], [], self.f); me.update()
        u = me.uv_layers.new(name="UVMap")
        for poly, uvs, mi in zip(me.polygons, self.uv, self.fm):
            poly.material_index = mi
            for li, t in zip(poly.loop_indices, uvs): u.data[li].uv = t
        for n in self.mats: me.materials.append(bpy.data.materials.get(n) or bpy.data.materials.new(n))
        ob = bpy.data.objects.new(self.name, me); bpy.context.scene.collection.objects.link(ob)
        return ob


def boardwalk(name, seed, L=4.0, W=1.3, missing=0.12, broken=0.1, sag=0.0):
    """A 4 m boardwalk segment along +X: posts, two stringers and loose planks, some missing or snapped."""
    rnd = random.Random(seed); B = Box(name)
    deck = 0.55
    for x in (0.15, L / 2, L - 0.15):
        for y in (-W / 2 + 0.06, W / 2 - 0.06):
            h = deck + 0.9
            B.box((x, y, deck - h / 2 + 0.02), (0.12, 0.12, h), rnd.uniform(-0.2, 0.2), (rnd.uniform(-0.04, 0.04), rnd.uniform(-0.04, 0.04)), "BW_Post")
    for y in (-W / 2 + 0.12, W / 2 - 0.12):
        B.box((L / 2, y, deck - 0.07), (L, 0.09, 0.11), 0.0, (0.0, 0.0), "BW_Wood")
    x = 0.08
    while x < L - 0.05:
        pw = rnd.uniform(0.17, 0.22)
        if rnd.random() > missing:
            if rnd.random() < broken:   # snapped plank: shorter, tilted down into the reeds
                ln = W * rnd.uniform(0.35, 0.6); yc = (W / 2 - ln / 2) * rnd.choice((-1, 1))
                B.box((x, yc, deck - 0.05), (pw, ln, 0.045), rnd.uniform(-0.25, 0.25), (rnd.uniform(-0.1, 0.1), rnd.uniform(-0.35, 0.35)), "BW_Wood")
            else:
                z = deck - sag * math.sin(math.pi * x / L)
                B.box((x, rnd.uniform(-0.03, 0.03), z), (pw, W + rnd.uniform(-0.08, 0.1), 0.05), rnd.uniform(-0.04, 0.04),
                      (rnd.uniform(-0.02, 0.02), rnd.uniform(-0.02, 0.02)), "BW_Wood")
        x += pw + rnd.uniform(0.015, 0.04)
    # a rope railing post on one side for the first segment variant
    return B


def export(ob, path):
    bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True); bpy.context.view_layer.objects.active = ob
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"MESH"}, apply_scale_options="FBX_SCALE_ALL",
                             bake_space_transform=True, axis_forward="-Z", axis_up="Y", mesh_smooth_type="FACE",
                             colors_type="LINEAR", add_leaf_bones=False, path_mode="STRIP")


ASSETS = {
    "RF_Susuki_A": lambda: susuki_clump("RF_Susuki_A", 11, leaves=14, stems=12, radius=0.26),
    "RF_Susuki_B": lambda: susuki_clump("RF_Susuki_B", 12, leaves=12, stems=10, radius=0.22),
    "RF_Susuki_C": lambda: susuki_clump("RF_Susuki_C", 13, leaves=16, stems=14, radius=0.3),
    "RF_Susuki_Broken": lambda: susuki_clump("RF_Susuki_Broken", 14, leaves=10, stems=7, radius=0.24, broken=3),
    # Cheap wide clump for the banks and hills outside the playable square.
    "RF_Susuki_Far": lambda: susuki_clump("RF_Susuki_Far", 15, leaves=6, stems=12, radius=0.45, plume=(8, 10)),
    "RF_Reed_A": lambda: reed_clump("RF_Reed_A", 21),
    "RF_Reed_B": lambda: reed_clump("RF_Reed_B", 22, stems=4, radius=0.12),
    "RF_LowGrass": lambda: low_grass("RF_LowGrass", 31),
    "RF_Litter": lambda: litter("RF_Litter", 32),
    "RF_Susuki_Card": lambda: card_clump("RF_Susuki_Card", 16),
    "RF_Boardwalk_A": lambda: boardwalk("RF_Boardwalk_A", 41, missing=0.06, broken=0.05),
    "RF_Boardwalk_B": lambda: boardwalk("RF_Boardwalk_B", 42, missing=0.18, broken=0.15, sag=0.05),
    "RF_Boardwalk_C": lambda: boardwalk("RF_Boardwalk_C", 43, missing=0.3, broken=0.3, sag=0.1),
}
stats = []
for n, fn in ASSETS.items():
    ob = fn().build()
    export(ob, os.path.join(OUT, n + ".fbx"))
    stats.append(f"{n}: {len(ob.data.vertices)} verts, {sum(len(p.vertices) - 2 for p in ob.data.polygons)} tris")
# Lay everything out in a row for a quick look and keep the work file outside Assets.
x = 0
for ob in bpy.data.objects:
    ob.location.x = x; x += 1.0 if ob.name.startswith("RF_S") or ob.name.startswith("RF_R") or ob.name.startswith("RF_L") else 5.0
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(WORK, "reed_kit.blend"))
print("\n".join(stats))
