# Shared mesh helpers for the snow plague kit (Blender 5.x, run in background).
# Every model is built from code into a Kit, then finished with frost/AO vertex colours and exported as FBX.
#
# Vertex data read by the SnowPlague shaders:
#   UV0   = texture coordinates in metres (tiling textures, 1 unit = 1 m of surface)
#   Color = R frost/snow mask (top faces, noise broken), G ambient occlusion (dark near the ground and inside),
#           B per-part random, A sway weight (0 rigid .. 1 free tip) for the vegetation wind
import bpy, bmesh, math, random
from mathutils import Vector, Matrix, noise


def clear_scene():
    for o in list(bpy.data.objects): bpy.data.objects.remove(o, do_unlink=True)
    for m in list(bpy.data.meshes): bpy.data.meshes.remove(m)


def smooth01(a, b, x):
    t = max(0.0, min(1.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


def perp(d):
    d = d.normalized()
    a = Vector((0, 0, 1)) if abs(d.z) < 0.9 else Vector((1, 0, 0))
    return d.cross(a).normalized()


class Kit:
    def __init__(self, name):
        self.name = name
        self.v, self.uv, self.col, self.f, self.mats = [], [], [], [], []
        self.extra = []   # per vertex: (sway, rnd)
        self.nrm = {}     # custom normals (cards get the normal of the clump they belong to)

    def m(self, name):
        if name not in self.mats: self.mats.append(name)
        return self.mats.index(name)

    def vert(self, p, uv, sway=0.0, rnd=0.0):
        self.v.append(Vector(p)); self.uv.append(tuple(uv)); self.extra.append((sway, rnd))
        return len(self.v) - 1

    def face(self, idx, mat):
        self.f.append((tuple(idx), self.m(mat)))

    def card(self, c, ax, ay, w, h, uvrect, mat, normal=None, sway=(0.0, 1.0), rnd=0.0, pivot=0.0):
        """Alpha card: centre c, width along ax, height along ay (pivot 0 = c is the bottom edge middle).
        uvrect = (u0, v0, u1, v1) in the atlas. normal: custom normal for all four corners (clump normal)."""
        ax = ax.normalized(); ay = ay.normalized()
        base = c - ay * (h * pivot)
        u0, v0, u1, v1 = uvrect
        ids = [self.vert(base - ax * w * 0.5, (u0, v0), sway[0], rnd), self.vert(base + ax * w * 0.5, (u1, v0), sway[0], rnd),
               self.vert(base + ax * w * 0.5 + ay * h, (u1, v1), sway[1], rnd), self.vert(base - ax * w * 0.5 + ay * h, (u0, v1), sway[1], rnd)]
        if normal is not None:
            for i in ids: self.nrm[i] = normal(self.v[i]) if callable(normal) else normal
        self.face(ids, mat)
        return ids

    # ---- primitives -------------------------------------------------------------------------------------------
    def tube(self, pts, radius, sides, mat, sway=None, rnd=0.0, cap_start=False, cap_end=False, uscale=1.0, jag=None, v0=0.0):
        """Tube along pts. radius(t) and sway(t) take the length fraction. jag(i, t) adds per-vertex radial noise."""
        n = len(pts)
        L = [0.0]
        for i in range(1, n): L.append(L[-1] + (pts[i] - pts[i - 1]).length)
        tot = max(L[-1], 1e-6)
        # parallel transport frame
        t0 = (pts[1] - pts[0]).normalized()
        nrm = perp(t0)
        rings = []
        for i in range(n):
            if i == 0: tng = t0
            elif i == n - 1: tng = (pts[i] - pts[i - 1]).normalized()
            else: tng = (pts[i + 1] - pts[i - 1]).normalized()
            nrm = (nrm - tng * nrm.dot(tng))
            if nrm.length < 1e-5: nrm = perp(tng)
            nrm.normalize()
            bin_ = tng.cross(nrm)
            t = L[i] / tot
            r = radius(t)
            sw = sway(t) if sway else 0.0
            ring = []
            circ = 2 * math.pi * max(r, 0.01)
            if r < 1e-4:
                ring = [self.vert(pts[i], (0.5 * uscale, v0 + L[i]), sw, rnd)]
            else:
                for k in range(sides + 1):
                    a = 2 * math.pi * k / sides
                    rr = r * (1 + (jag(i, k % sides, t) if jag else 0.0))
                    p = pts[i] + (nrm * math.cos(a) + bin_ * math.sin(a)) * rr
                    if k == sides:
                        p = self.v[ring[0]].copy()
                    ring.append(self.vert(p, (k / sides * circ * uscale, v0 + L[i]), sw, rnd))
            rings.append(ring)
        for a, b in zip(rings, rings[1:]):
            if len(a) > 1 and len(b) > 1:
                for k in range(sides): self.face((a[k], a[k + 1], b[k + 1], b[k]), mat)
            elif len(a) > 1:
                for k in range(sides): self.face((a[k], a[k + 1], b[0]), mat)
            elif len(b) > 1:
                for k in range(sides): self.face((a[0], b[k + 1], b[k]), mat)
        if cap_start and len(rings[0]) > 1:
            self.face(tuple(reversed(rings[0][:sides])), mat)
        if cap_end and len(rings[-1]) > 1:
            self.face(tuple(rings[-1][:sides]), mat)
        return rings

    def box(self, center, size, mat, rot=Matrix.Identity(3), jitter=0.0, rng=None, sway=0.0, rnd=0.0):
        hx, hy, hz = size[0] / 2, size[1] / 2, size[2] / 2
        c = Vector(center)
        corners = []
        for z in (-hz, hz):
            for y in (-hy, hy):
                for x in (-hx, hx):
                    p = Vector((x, y, z))
                    if jitter and rng: p += Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), rng.uniform(-1, 1))) * jitter
                    corners.append(c + rot @ p)
        # faces (each with its own verts so UVs are planar per face, in metres)
        quads = [(0, 2, 3, 1, 'z'), (4, 5, 7, 6, 'z'), (0, 1, 5, 4, 'y'), (2, 6, 7, 3, 'y'), (0, 4, 6, 2, 'x'), (1, 3, 7, 5, 'x')]
        for q in quads:
            ids = []
            for ci in q[:4]:
                lp = rot.transposed() @ (corners[ci] - c)
                if q[4] == 'z': uv = (lp.x, lp.y)
                elif q[4] == 'y': uv = (lp.x, lp.z)
                else: uv = (lp.y, lp.z)
                ids.append(self.vert(corners[ci], uv, sway, rnd))
            self.face(ids, mat)

    def lathe(self, profile, sides, mat, center=Vector((0, 0, 0)), rot=Matrix.Identity(3), cut=None, rnd=0.0, wobble=0.0, rng=None):
        """profile: list of (radius, z). cut(a, z) -> True removes that face (broken jars)."""
        L = [0.0]
        for i in range(1, len(profile)):
            L.append(L[-1] + math.hypot(profile[i][0] - profile[i - 1][0], profile[i][1] - profile[i - 1][1]))
        rows = []
        wob = [rng.uniform(-1, 1) * wobble if rng else 0 for _ in range(sides)]
        for i, (r, z) in enumerate(profile):
            row = []
            circ = 2 * math.pi * max(r, 0.05)
            for k in range(sides + 1):
                a = 2 * math.pi * k / sides
                rr = r * (1 + wob[k % sides])
                p = center + rot @ Vector((math.cos(a) * rr, math.sin(a) * rr, z))
                row.append(self.vert(p, (k / sides * circ, L[i]), 0.0, rnd))
            rows.append(row)
        for i in range(len(rows) - 1):
            for k in range(sides):
                a = 2 * math.pi * (k + 0.5) / sides
                z = (profile[i][1] + profile[i + 1][1]) * 0.5
                if cut and cut(a, z): continue
                self.face((rows[i][k], rows[i][k + 1], rows[i + 1][k + 1], rows[i + 1][k]), mat)
        return rows

    def grid(self, nx, ny, sx, sy, height, mat, keep=None, uvscale=1.0, center=Vector((0, 0, 0)), rot=Matrix.Identity(3), sway=None):
        """Height-field sheet. height(u, v) -> world offset vector (or z). keep(i, j) -> False drops the quad."""
        ids = {}
        def vid(i, j):
            if (i, j) not in ids:
                u, v = i / nx, j / ny
                h = height(u, v)
                p = Vector(((u - 0.5) * sx, (v - 0.5) * sy, 0))
                p = center + rot @ (p + (h if isinstance(h, Vector) else Vector((0, 0, h))))
                ids[(i, j)] = self.vert(p, (u * sx * uvscale, v * sy * uvscale), sway(u, v) if sway else 0.0)
            return ids[(i, j)]
        for j in range(ny):
            for i in range(nx):
                if keep and not keep(i, j): continue
                self.face((vid(i, j), vid(i + 1, j), vid(i + 1, j + 1), vid(i, j + 1)), mat)

    def blob(self, center, radii, mat, subdiv=2, rng=None, lumpy=0.15, flatten_below=None, rot=Matrix.Identity(3), rnd=0.0, seed=0):
        """Lumpy ellipsoid (sacks, rocks, bodies). flatten_below squashes everything under that local z."""
        bm = bmesh.new()
        bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=1.0)
        off = Vector((seed * 7.1, seed * 3.3, seed * 1.7))
        base = len(self.v)
        for v in bm.verts:
            p = v.co.copy()
            n = noise.noise(p * 1.6 + off) * lumpy + noise.noise(p * 4.0 + off) * lumpy * 0.35
            p = p * (1 + n)
            p = Vector((p.x * radii[0], p.y * radii[1], p.z * radii[2]))
            if flatten_below is not None and p.z < flatten_below: p.z = flatten_below + (p.z - flatten_below) * 0.15
            w = center + rot @ p
            self.vert(w, (w.x + w.z * 0.5, w.y + w.z * 0.5), 0.0, rnd)
        for f in bm.faces:
            self.face([base + v.index for v in f.verts], mat)
        bm.free()

    # ---- finish -----------------------------------------------------------------------------------------------
    def build(self, frost=1.0, frost_bias=0.0, ao_height=0.6, seed=1, frost_noise=0.35, smooth=True, sway_override=None):
        me = bpy.data.meshes.new(self.name)
        me.from_pydata([tuple(p) for p in self.v], [], [f[0] for f in self.f])
        me.validate(clean_customdata=False)
        me.update()
        uvl = me.uv_layers.new(name="UVMap")
        ca = me.color_attributes.new("Col", 'BYTE_COLOR', 'CORNER')
        # vertex normals from the mesh (smooth), used for the frost mask
        vn = [self.nrm.get(i, v.normal).normalized() for i, v in enumerate(me.vertices)]
        zmin = min(p.z for p in self.v); zmax = max(p.z for p in self.v)
        off = Vector((seed * 13.7, seed * 5.1, 0))
        cols = []
        for i, p in enumerate(self.v):
            n = vn[i]
            nz = n.z
            # top-facing, broken into patches: snow sits in drifts, bare surface shows between them
            top = smooth01(0.2 - frost_bias, 0.85 - frost_bias, nz + noise.noise(p * 7.0 + off) * 0.2)
            patch = smooth01(0.3, 0.7, 0.5 + noise.noise(p * 1.4 + off) * 0.55 + noise.noise(p * 5.0 + off) * 0.25 * frost_noise / 0.35)
            fr = top * (0.18 + 0.82 * patch) * frost
            h = p.z - zmin
            ao = 0.45 + 0.55 * smooth01(0.0, ao_height, h)
            sway, rnd = self.extra[i]
            if sway_override: sway = sway_override(p)
            cols.append((max(0.0, min(1.0, fr)), ao, rnd, sway))
        for poly, (idx, mi) in zip(me.polygons, self.f):
            poly.material_index = mi; poly.use_smooth = smooth
            for li, vi in zip(poly.loop_indices, poly.vertices):
                uvl.data[li].uv = self.uv[vi]; ca.data[li].color = cols[vi]
        if self.nrm:
            me.normals_split_custom_set_from_vertices([tuple(n) for n in vn])
        for n in self.mats: me.materials.append(bpy.data.materials.get(n) or bpy.data.materials.new(n))
        ob = bpy.data.objects.new(self.name, me)
        bpy.context.scene.collection.objects.link(ob)
        return ob


def tris(ob):
    return sum(len(p.vertices) - 2 for p in ob.data.polygons)


def export(ob, path):
    bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True); bpy.context.view_layer.objects.active = ob
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"MESH"}, apply_scale_options="FBX_SCALE_ALL",
                             bake_space_transform=True, axis_forward="-Z", axis_up="Y", mesh_smooth_type="FACE",
                             colors_type="LINEAR", add_leaf_bones=False, path_mode="STRIP")


# ---- branching (trees, shrubs) ------------------------------------------------------------------------------
UP = Vector((0, 0, 1))


def rot_to(d):
    """Matrix that turns +Z onto d."""
    return UP.rotation_difference(d.normalized()).to_matrix()


def rnd_dir(rng, base, spread, up_bias=0.0):
    p = perp(base); q = base.normalized().cross(p)
    a = rng.uniform(0, 2 * math.pi)
    s = math.radians(spread)
    d = base.normalized() * math.cos(s) + (p * math.cos(a) + q * math.sin(a)) * math.sin(s)
    d.z += up_bias
    return d.normalized()


def gnarl_path(rng, base, d, length, step, wob, tropism, seed):
    pts = [base.copy()]
    n = max(2, int(length / step))
    cur = base.copy(); dd = d.normalized()
    for i in range(n):
        t = (i + 1) / n
        w = Vector((noise.noise(cur * 1.3 + Vector((seed, 0, 0))), noise.noise(cur * 1.3 + Vector((0, seed, 0))),
                    noise.noise(cur * 1.3 + Vector((0, 0, seed))) * 0.5))
        dd = (dd + w * wob + Vector((0, 0, tropism))).normalized()
        cur = cur + dd * (length / n)
        pts.append(cur.copy())
    return pts


def branch(K, rng, base, d, length, r0, depth, maxdepth, mat, sides, sway0=0.0, child_n=(3, 5), spread=(28, 55),
           step=0.35, wob=0.35, tropism=0.05, tip_r=0.0, broken=0.0, child_len=(0.45, 0.7), seed=0.0, rnd=0.0):
    pts = gnarl_path(rng, base, d, length, step, wob, tropism, seed + depth * 3.1)
    snapped = depth < maxdepth and rng.random() < broken
    r_end = r0 * 0.45 if snapped else tip_r
    radius = lambda t: r0 + (r_end - r0) * (t ** 0.8)
    swl = lambda t: min(1.0, sway0 + (1 - sway0) * t * (0.3 + 0.7 * depth / max(1, maxdepth)))
    sd = max(3, sides - depth)
    K.tube(pts, radius, sd, mat, sway=swl, rnd=rnd, cap_end=snapped)
    if depth >= maxdepth: return
    total = sum((pts[i] - pts[i - 1]).length for i in range(1, len(pts)))
    n = rng.randint(*child_n)
    for c in range(n):
        t = rng.uniform(0.25, 0.95) if not snapped else rng.uniform(0.2, 0.7)
        # point at t along the path
        acc = 0; p = pts[-1]; dloc = pts[-1] - pts[-2]
        for i in range(1, len(pts)):
            seg = (pts[i] - pts[i - 1]).length
            if acc + seg >= t * total:
                f = (t * total - acc) / max(seg, 1e-6)
                p = pts[i - 1].lerp(pts[i], f); dloc = pts[i] - pts[i - 1]; break
            acc += seg
        cd = rnd_dir(rng, dloc, rng.uniform(*spread), up_bias=0.15)
        cl = length * rng.uniform(*child_len) * (1 - t * 0.35)
        cr = max(radius(t) * 0.62, 0.004)
        branch(K, rng, p - dloc.normalized() * cr, cd, cl, cr, depth + 1, maxdepth, mat, sides, swl(t), child_n, spread,
               step * 0.75, wob * 1.1, tropism, tip_r, broken, child_len, seed + c * 1.7, rnd)


