# Snow plague architecture helpers (Blender 5.x, background).
# A Piece collects parts (each a small bmesh in its own local frame, UV-projected in metres / texture size, grain
# along a chosen local axis), joins them into one object, then writes the kit vertex colours:
#   R frost (up-facing, patchy), G AO (height + undersides), B per-part random, A 0 (rigid).
import bpy, bmesh, math, random
from mathutils import Vector, Matrix, Euler, noise

FROSTMUL = {"SP_Arch_Deck": 0.5, "SP_Arch_RedWood": 0.7, "SP_LanternGlow": 0.0, "SP_Arch_Timber": 0.8}
# metres of surface per texture repeat, by material slot
TEXSCALE = {"SP_Arch_RedWood": 1.4, "SP_Arch_Timber": 1.4, "SP_Arch_Deck": 2.2, "SP_Arch_RoofTile": 2.0,
            "SP_Arch_WallStone": 2.2, "SP_Stone": 1.6, "SP_StoneLantern": 1.2, "SP_LanternGlow": 1.0, "SP_Iron": 1.0}


def smooth01(a, b, x):
    t = max(0.0, min(1.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


def clear_scene():
    for o in list(bpy.data.objects): bpy.data.objects.remove(o, do_unlink=True)
    for m in list(bpy.data.meshes): bpy.data.meshes.remove(m)


# ---------------------------------------------------------------- primitives (local bmesh, centred)
def box(sx, sy, sz, bev=0.02, segs=1, top=1.0, bot=1.0, jit=0.0, rng=None):
    bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co.x *= sx; v.co.y *= sy; v.co.z *= sz
        s = top if v.co.z > 0 else bot
        v.co.x *= s; v.co.y *= s
        if jit and rng: v.co += Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), rng.uniform(-1, 1))) * jit
    if bev > 0:
        bmesh.ops.bevel(bm, geom=list(bm.edges), offset=min(bev, 0.4 * min(sx, sy, sz)), segments=segs, profile=0.5,
                        affect='EDGES', clamp_overlap=True)
    return bm


def cyl(r1, r2, h, sides=12, bev=0.0):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=sides, radius1=r1, radius2=r2, depth=h)
    if bev > 0:
        ed = [e for e in bm.edges if abs(e.verts[0].co.z - e.verts[1].co.z) < 1e-5]
        bmesh.ops.bevel(bm, geom=ed, offset=bev, segments=1, profile=0.5, affect='EDGES', clamp_overlap=True)
    return bm


def lathe(profile, sides=10):
    """profile: list of (r, z) bottom to top; r=0 ends become poles."""
    bm = bmesh.new(); rings = []
    for r, z in profile:
        if r < 1e-4: rings.append([bm.verts.new((0, 0, z))])
        else: rings.append([bm.verts.new((math.cos(2 * math.pi * k / sides) * r, math.sin(2 * math.pi * k / sides) * r, z)) for k in range(sides)])
    for a, b in zip(rings, rings[1:]):
        for k in range(sides):
            if len(a) > 1 and len(b) > 1: bm.faces.new((a[k], a[(k + 1) % sides], b[(k + 1) % sides], b[k]))
            elif len(a) > 1: bm.faces.new((a[k], a[(k + 1) % sides], b[0]))
            elif len(b) > 1: bm.faces.new((a[0], b[(k + 1) % sides], b[k]))
    if len(rings[0]) > 1: bm.faces.new(list(reversed(rings[0])))
    if len(rings[-1]) > 1: bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def sweep_rect(pts, w, h, up=Vector((0, 0, 1))):
    """Rectangular beam along pts (world-ish coords), w across (horizontal), h vertical; capped."""
    bm = bmesh.new(); rings = []
    for i, p in enumerate(pts):
        t = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
        side = t.cross(up).normalized(); vup = side.cross(t).normalized()
        rings.append([bm.verts.new(p + side * sx * w * 0.5 + vup * sz * h * 0.5) for sx, sz in ((-1, -1), (1, -1), (1, 1), (-1, 1))])
    for a, b in zip(rings, rings[1:]):
        for k in range(4): bm.faces.new((a[k], a[(k + 1) % 4], b[(k + 1) % 4], b[k]))
    bm.faces.new(list(reversed(rings[0]))); bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def grid(nx, ny, fn):
    """Sheet: fn(u, v) -> Vector with u, v in 0..1. Returns bmesh."""
    bm = bmesh.new()
    vs = [[bm.verts.new(fn(i / nx, j / ny)) for i in range(nx + 1)] for j in range(ny + 1)]
    for j in range(ny):
        for i in range(nx): bm.faces.new((vs[j][i], vs[j][i + 1], vs[j + 1][i + 1], vs[j + 1][i]))
    return bm, vs


def merge(*bms):
    out = bmesh.new()
    for b in bms:
        me = bpy.data.meshes.new("_t"); b.to_mesh(me); b.free()
        out.from_mesh(me); bpy.data.meshes.remove(me)
    return out


def displace(bm, amp, freq, seed=0.0, keep=lambda co: True):
    off = Vector((seed * 3.1, seed * 1.7, seed * 2.3))
    bm.normal_update()
    for v in bm.verts:
        if not keep(v.co): continue
        n = noise.noise(v.co * freq + off) + 0.5 * noise.noise(v.co * freq * 2.7 + off)
        v.co += v.normal * n * amp


# ---------------------------------------------------------------- piece
class Piece:
    def __init__(self, name, seed=1):
        self.name = name; self.objs = []; self.rng = random.Random(seed); self.seed = seed

    def add(self, bm, mat, loc=(0, 0, 0), rot=(0, 0, 0), grain=None, smooth=False, sc=None, M=None):
        """bm in local coords. grain: local axis index (0 x, 1 y, 2 z) the texture v runs along."""
        sc = sc or TEXSCALE.get(mat, 1.5)
        uvl = bm.loops.layers.uv.verify()
        ox, oy = self.rng.random() * 5, self.rng.random() * 5
        bm.normal_update()
        for f in bm.faces:
            n = f.normal
            a = max(range(3), key=lambda i: abs(n[i]))
            axes = [i for i in range(3) if i != a]
            if grain is not None and grain in axes: axes = [i for i in axes if i != grain] + [grain]
            for l in f.loops:
                co = l.vert.co
                l[uvl].uv = (co[axes[0]] / sc + ox, co[axes[1]] / sc + oy)
            f.smooth = smooth
        if M is None:
            M = Matrix.Translation(Vector(loc)) @ Euler([math.radians(r) for r in rot], 'XYZ').to_matrix().to_4x4()
        bmesh.ops.transform(bm, matrix=M, verts=bm.verts)
        me = bpy.data.meshes.new(self.name + "_p"); bm.to_mesh(me); bm.free()
        mt = bpy.data.materials.get(mat) or bpy.data.materials.new(mat)
        me.materials.append(mt)
        ca = me.color_attributes.new("Col", 'BYTE_COLOR', 'CORNER')
        rnd = self.rng.random()
        for c in ca.data: c.color = (0, 1, rnd, 0)
        ob = bpy.data.objects.new(self.name + "_p", me)
        bpy.context.scene.collection.objects.link(ob)
        self.objs.append(ob)
        return ob

    def build(self, frost=1.0, ao_height=0.9, frost_bias=0.0):
        bpy.ops.object.select_all(action='DESELECT')
        for o in self.objs: o.select_set(True)
        bpy.context.view_layer.objects.active = self.objs[0]
        bpy.ops.object.join()
        ob = bpy.context.view_layer.objects.active
        ob.name = self.name; ob.data.name = self.name
        me = ob.data
        me.update()
        ca = me.color_attributes["Col"]
        zmin = min(v.co.z for v in me.vertices)
        off = Vector((self.seed * 13.7, self.seed * 5.1, 0))
        for poly in me.polygons:
            n = poly.normal
            fmul = FROSTMUL.get(me.materials[poly.material_index].name, 1.0)
            for li, vi in zip(poly.loop_indices, poly.vertices):
                p = me.vertices[vi].co
                top = smooth01(0.25 - frost_bias, 0.8 - frost_bias, n.z + noise.noise(p * 5.0 + off) * 0.15)
                patch = smooth01(0.3, 0.75, 0.5 + noise.noise(p * 1.2 + off) * 0.55 + noise.noise(p * 4.0 + off) * 0.25)
                fr = top * (0.12 + 0.88 * patch) * frost * fmul
                ao = 0.45 + 0.55 * smooth01(0.0, ao_height, p.z - zmin)
                if n.z < -0.3: ao *= 0.7
                c = ca.data[li].color
                ca.data[li].color = (max(0.0, min(1.0, fr)), ao, c[2], 0.0)
        self.ob = ob
        return ob


def tris(ob):
    return sum(len(p.vertices) - 2 for p in ob.data.polygons)


def export(ob, path):
    bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True); bpy.context.view_layer.objects.active = ob
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"MESH"}, apply_scale_options="FBX_SCALE_ALL",
                             bake_space_transform=True, axis_forward="-Z", axis_up="Y", mesh_smooth_type="FACE",
                             colors_type="LINEAR", add_leaf_bones=False, path_mode="STRIP")


# ---------------------------------------------------------------- roofs
def tile_slope(P, L, D, zr, H, side, upsweep=0.4, flare=0.2, period=0.32, amp=0.07, rows=10, thick=0.14,
               mat="SP_Arch_RoofTile", ends=True, end_mat="SP_Arch_RoofTile", x0=0.0, y0=0.0, exp=1.6, cols_per=3):
    """One concave East Asian tiled slope from the ridge (y=y0, z=zr) out to the eave (|y|=D) on `side` (+1/-1), length
    L along x centred at x0. Corners sweep up (upsweep) and out (flare). Tile rows run down the slope (corrugation)."""
    nx = max(8, int(L / period * cols_per))

    def surf(u, v, corr=True):
        x = (u - 0.5) * L
        e = abs(2 * u - 1) ** 3
        y = side * (y0 + v * D + flare * e * v * v)
        z = zr - H * (1 - (1 - v) ** exp) + upsweep * e * v * v
        if corr:
            z += amp * (0.5 + 0.5 * math.cos(2 * math.pi * x / period)) ** 0.7 * (0.35 + 0.65 * smooth01(0.0, 0.15, v))
        return Vector((x + x0, y, z))

    top, tv = grid(nx, rows, lambda u, v: surf(u, v))
    bot, bv = grid(max(8, nx // 6), 4, lambda u, v: surf(u, v, False) - Vector((0, 0, thick)))
    bmesh.ops.reverse_faces(bot, faces=bot.faces)
    # eave + gable edges (fascia)
    edge = bmesh.new()
    def strip(pts_top, pts_bot):
        a = [edge.verts.new(p) for p in pts_top]; b = [edge.verts.new(p) for p in pts_bot]
        for i in range(len(a) - 1): edge.faces.new((a[i], a[i + 1], b[i + 1], b[i]))
    n2 = max(8, nx // 3)
    strip([surf(i / n2, 1.0) for i in range(n2 + 1)], [surf(i / n2, 1.0, False) - Vector((0, 0, thick)) for i in range(n2 + 1)])
    for u in (0.0, 1.0):
        strip([surf(u, j / 6) for j in range(7)], [surf(u, j / 6, False) - Vector((0, 0, thick)) for j in range(7)])
    bm = merge(top, bot, edge)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    if side < 0: pass
    # make sure the top faces point up
    bm.normal_update()
    up = sum(f.normal.z * f.calc_area() for f in bm.faces)
    if up < 0: bmesh.ops.reverse_faces(bm, faces=bm.faces)
    P.add(bm, mat, grain=1, smooth=True)
    # round end tiles (막새) along the eave, one per cover-tile row
    if ends:
        k = 0
        x = -L / 2 + (L / 2) % period
        while x <= L / 2 + 1e-4:
            u = x / L + 0.5
            if 0.02 < u < 0.98:
                p = surf(u, 1.0, False) + Vector((0, 0, amp * 0.6 - thick * 0.35))
                t = (surf(u, 1.0, False) - surf(u, 0.92, False)).normalized()
                c = cyl(amp * 1.25, amp * 1.25, 0.09, 6)
                M = Matrix.Translation(p) @ Vector((0, 0, 1)).rotation_difference(t).to_matrix().to_4x4()
                P.add(c, end_mat, M=M)
            x += period; k += 1
    return surf


def hip_roof(P, hw, z_eave, rise, thick, upsweep=0.1, exp=1.7, nx=10, ny=6, mat="SP_StoneLantern", top_hw=0.04, loc=Vector((0, 0, 0)), rotz=0.0):
    """Square concave hip roof (lantern cap). hw half width at the eave."""
    def side_pt(s, u, v, under=False):
        # v 0 top (peak) .. 1 eave; u -1..1 across
        w = top_hw + (hw - top_hw) * v
        x = u * w
        y = -w
        z = z_eave + rise * (1 - v) ** exp + upsweep * abs(u) ** 3 * v * v
        if under: z = z_eave - thick + upsweep * abs(u) ** 3 * v * v * 0.8
        p = Vector((x, y, z))
        p.rotate(Matrix.Rotation(s * math.pi / 2 + rotz, 3, 'Z'))
        return p + loc
    parts = []
    for s in range(4):
        g, _ = grid(nx, ny, lambda u, v, s=s: side_pt(s, u * 2 - 1, v))
        parts.append(g)
        e = bmesh.new()
        a = [e.verts.new(side_pt(s, i / nx * 2 - 1, 1.0)) for i in range(nx + 1)]
        b = [e.verts.new(side_pt(s, i / nx * 2 - 1, 1.0, True)) for i in range(nx + 1)]
        for i in range(nx): e.faces.new((a[i], a[i + 1], b[i + 1], b[i]))
        parts.append(e)
    # underside
    u = bmesh.new()
    vs = []
    for s in range(4):
        for i in range(nx):
            vs.append(u.verts.new(side_pt(s, i / nx * 2 - 1, 1.0, True)))
    u.faces.new(vs)
    parts.append(u)
    bm = merge(*parts)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-4)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.normal_update()
    if sum(f.normal.z * f.calc_area() for f in bm.faces if f.calc_center_median().z > z_eave) < 0:
        bmesh.ops.reverse_faces(bm, faces=bm.faces)
    P.add(bm, mat, smooth=False)
