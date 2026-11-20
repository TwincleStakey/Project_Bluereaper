# Small mesh builder used by build_kit.py: boxes, tapered cylinders, grids and lathes with explicit UVs in meters.
import bpy, math, random
from mathutils import Vector, Matrix, noise


class Mesh:
    def __init__(self, name):
        self.name = name
        self.v = []      # Vector
        self.f = []      # (idx tuple, uv tuple, mat index, smooth)
        self.col = {}    # vertex index -> rgba (optional)
        self.mats = []

    def m(self, mat):
        if mat not in self.mats: self.mats.append(mat)
        return self.mats.index(mat)

    def add_v(self, p):
        self.v.append(Vector(p)); return len(self.v) - 1

    def face(self, idx, uvs, mat, smooth=False):
        self.f.append((tuple(idx), tuple(uvs), self.m(mat), smooth))

    # Box from a local frame: center c, size s, rotation (euler degrees) and optional extra matrix.
    def box(self, mat, c, s, rot=(0, 0, 0), jitter=0.0, skip=()):
        R = Matrix.Rotation(math.radians(rot[2]), 4, 'Z') @ Matrix.Rotation(math.radians(rot[1]), 4, 'Y') @ Matrix.Rotation(math.radians(rot[0]), 4, 'X')
        hx, hy, hz = s[0] / 2, s[1] / 2, s[2] / 2
        base = len(self.v)
        for z in (-hz, hz):
            for y in (-hy, hy):
                for x in (-hx, hx):
                    p = Vector((x, y, z))
                    if jitter: p += Vector((random.uniform(-1, 1), random.uniform(-1, 1), random.uniform(-1, 1))) * jitter
                    self.v.append((R @ p) + Vector(c))
        # faces with (axis u, axis v) sizes to keep texel density and long-axis alignment
        F = {
            '-z': ((0, 2, 3, 1), s[0], s[1]), '+z': ((4, 5, 7, 6), s[0], s[1]),
            '-y': ((0, 1, 5, 4), s[0], s[2]), '+y': ((2, 6, 7, 3), s[0], s[2]),
            '-x': ((0, 4, 6, 2), s[1], s[2]), '+x': ((1, 3, 7, 5), s[1], s[2]),
        }
        o = random.random() * 4, random.random() * 4
        for k, (q, a, b) in F.items():
            if k in skip: continue
            if k in ('-y', '+y', '-x', '+x'):
                uv = [(0, 0), (a, 0), (a, b), (0, b)]
                if k in ('+y', '-x'): uv = [(0, 0), (0, b), (a, b), (a, 0)]
                if k == '-x': uv = [(0, 0), (0, b), (a, b), (a, 0)]
            else:
                uv = [(0, 0), (0, b), (a, b), (a, 0)] if k == '-z' else [(0, 0), (a, 0), (a, b), (0, b)]
            # long axis along u so plank textures follow board length
            if b > a * 1.5: uv = [(y, x) for x, y in uv]
            uv = [(x + o[0], y + o[1]) for x, y in uv]
            self.face([base + i for i in q], uv, mat)
        return base

    # Box spanning two points (a beam) with square section w x h; up hint picks the roll.
    def beam(self, mat, p0, p1, w, h=None, jitter=0.0, roll=0.0):
        h = h or w
        p0, p1 = Vector(p0), Vector(p1)
        d = p1 - p0; L = d.length
        z = d.normalized()
        up = Vector((0, 0, 1)) if abs(z.z) < 0.95 else Vector((1, 0, 0))
        x = up.cross(z).normalized(); y = z.cross(x)
        if roll:
            q = Matrix.Rotation(math.radians(roll), 3, z); x = q @ x; y = q @ y
        base = len(self.v)
        corners = []
        for t in (0, 1):
            for sy in (-1, 1):
                for sx in (-1, 1):
                    p = p0 + d * t + x * (sx * w / 2) + y * (sy * h / 2)
                    if jitter: p += Vector((random.uniform(-1, 1), random.uniform(-1, 1), random.uniform(-1, 1))) * jitter
                    self.v.append(p)
        o = random.random() * 4
        sides = [((0, 1, 5, 4), w), ((1, 3, 7, 5), h), ((3, 2, 6, 7), w), ((2, 0, 4, 6), h)]
        acc = 0
        for q, a in sides:
            self.face([base + i for i in q], [(o, acc), (o, acc + a), (o + L, acc + a), (o + L, acc)], mat)
            acc += a
        self.face([base + i for i in (0, 2, 3, 1)], [(0, 0), (0, h), (w, h), (w, 0)], mat)
        self.face([base + i for i in (4, 5, 7, 6)], [(0, 0), (w, 0), (w, h), (0, h)], mat)
        return base

    # Tapered cylinder along a polyline (list of points and radii), smooth sides, optional caps.
    def tube(self, mat, pts, radii, seg=8, cap=True, uscale=1.0, twist_noise=0.0):
        pts = [Vector(p) for p in pts]
        rings = []
        prev_x = None
        vlen = 0.0
        for i, p in enumerate(pts):
            if i == 0: t = (pts[1] - pts[0]).normalized()
            elif i == len(pts) - 1: t = (pts[-1] - pts[-2]).normalized()
            else: t = ((pts[i + 1] - pts[i]).normalized() + (pts[i] - pts[i - 1]).normalized()).normalized()
            if prev_x is None:
                up = Vector((0, 0, 1)) if abs(t.z) < 0.9 else Vector((1, 0, 0))
                x = up.cross(t).normalized()
            else:
                x = (prev_x - t * prev_x.dot(t)).normalized()
            y = t.cross(x); prev_x = x
            if i > 0: vlen += (pts[i] - pts[i - 1]).length
            ring = []
            for k in range(seg):
                a = 2 * math.pi * k / seg
                r = radii[i] * (1 + twist_noise * noise.noise(p * 3 + Vector((k, 0, 0))))
                ring.append(self.add_v(p + (x * math.cos(a) + y * math.sin(a)) * r))
            rings.append((ring, vlen, radii[i]))
        for i in range(len(rings) - 1):
            (r0, v0, rad0), (r1, v1, rad1) = rings[i], rings[i + 1]
            circ = 2 * math.pi * max(rad0, 0.05) * uscale
            for k in range(seg):
                k1 = (k + 1) % seg
                u0, u1 = circ * k / seg, circ * (k + 1) / seg
                self.face([r0[k], r0[k1], r1[k1], r1[k]], [(u0, v0), (u1, v0), (u1, v1), (u0, v1)], mat, smooth=True)
        if cap:
            for ring, flip in ((rings[0][0], True), (rings[-1][0], False)):
                idx = list(reversed(ring)) if flip else ring
                c = sum((self.v[i] for i in idx), Vector()) / len(idx)
                self.face(idx, [((self.v[i] - c).x, (self.v[i] - c).y) for i in idx], mat)

    # Lathe profile [(r, z)] around Z at center c. Returns ring vertex index lists.
    def lathe(self, mat, c, prof, seg=16, smooth=True, skip=None):
        c = Vector(c); rings = []
        vlen = [0.0]
        for i in range(1, len(prof)):
            vlen.append(vlen[-1] + math.hypot(prof[i][0] - prof[i - 1][0], prof[i][1] - prof[i - 1][1]))
        for r, z in prof:
            rings.append([self.add_v(c + Vector((math.cos(2 * math.pi * k / seg) * r, math.sin(2 * math.pi * k / seg) * r, z))) for k in range(seg)])
        maxr = max(p[0] for p in prof)
        for i in range(len(rings) - 1):
            for k in range(seg):
                if skip and skip(i, k): continue
                k1 = (k + 1) % seg
                u0, u1 = 2 * math.pi * maxr * k / seg, 2 * math.pi * maxr * (k + 1) / seg
                self.face([rings[i][k], rings[i][k1], rings[i + 1][k1], rings[i + 1][k]], [(u0, vlen[i]), (u1, vlen[i]), (u1, vlen[i + 1]), (u0, vlen[i + 1])], mat, smooth)
        return rings

    # Height-field grid over [x0,x1]x[y0,y1]; fz(x,y)->z or None to drop the vertex's faces.
    def grid(self, mat, x0, x1, y0, y1, nx, ny, fz, keep=None, smooth=True, uvs=1.0, colf=None, flip=False):
        idx = {}
        for j in range(ny + 1):
            for i in range(nx + 1):
                x = x0 + (x1 - x0) * i / nx; y = y0 + (y1 - y0) * j / ny
                z = fz(x, y)
                if z is None: continue
                idx[i, j] = self.add_v((x, y, z))
                if colf: self.col[idx[i, j]] = colf(x, y, z)
        for j in range(ny):
            for i in range(nx):
                q = [(i, j), (i + 1, j), (i + 1, j + 1), (i, j + 1)]
                if any(k not in idx for k in q): continue
                cx = x0 + (x1 - x0) * (i + 0.5) / nx; cy = y0 + (y1 - y0) * (j + 0.5) / ny
                if keep and not keep(cx, cy): continue
                vs = [idx[k] for k in q]
                uv = [(self.v[v].x * uvs, self.v[v].y * uvs) for v in vs]
                if flip: vs = vs[::-1]; uv = uv[::-1]
                self.face(vs, uv, mat, smooth)
        return idx

    def transform(self, fn, start=0):
        for i in range(start, len(self.v)): self.v[i] = Vector(fn(self.v[i]))

    def build(self):
        me = bpy.data.meshes.new(self.name)
        me.from_pydata([tuple(p) for p in self.v], [], [f[0] for f in self.f])
        me.update()
        uv = me.uv_layers.new(name="UVMap")
        for poly, (idx, uvs, mi, sm) in zip(me.polygons, self.f):
            poly.material_index = mi; poly.use_smooth = sm
            for li, t in zip(poly.loop_indices, uvs): uv.data[li].uv = t
        if self.col:
            ca = me.color_attributes.new("Col", 'BYTE_COLOR', 'CORNER')
            for poly in me.polygons:
                for li, vi in zip(poly.loop_indices, poly.vertices):
                    ca.data[li].color = self.col.get(vi, (1, 1, 1, 1))
        for name in self.mats:
            mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
            me.materials.append(mat)
        ob = bpy.data.objects.new(self.name, me)
        bpy.context.scene.collection.objects.link(ob)
        return ob


def export(objs, path):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"MESH"}, apply_scale_options="FBX_SCALE_ALL",
                             bake_space_transform=True, axis_forward="-Z", axis_up="Y", mesh_smooth_type="FACE",
                             colors_type="LINEAR", add_leaf_bones=False, path_mode="STRIP")
