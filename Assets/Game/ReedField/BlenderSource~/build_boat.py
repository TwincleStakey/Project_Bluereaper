# Small Korean river ferry boat (나룻배) for the reed field river (Kido 11-10).
# Flat bottom, flared plank sides, blunt square ends that sweep up (bow higher), two thwarts, floorboards,
# a punting pole (상앗대) lying inside and an iron mooring ring on the bow for the rope.
# Materials (submesh order): 0 RF_BoatWood (hull, thwarts), 1 RF_BoatWoodDark (floor, pole), 2 RF_BoatIron (ring).
# The ring is the only thing on submesh 2: RFBoat.cs ties the rope to its centre.
# Length runs along Blender +Y with the bow at +Y. Origin: centre, on the waterline (the hull sits ~0.18 m deep).
#
# blender -b -P build_boat.py -- <out.fbx> [<out.blend>]
import bpy, bmesh, math, sys
from mathutils import Vector

args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = args[0] if args else "RF_Boat.fbx"
BLEND = args[1] if len(args) > 1 else None

bpy.ops.wm.read_factory_settings(use_empty=True)

L, B, D = 4.6, 1.25, 0.52       # length, beam, side depth (m)
DRAFT = 0.18                     # how deep the bottom sits below the waterline
T = 0.045                        # plank thickness
N = 17                           # stations along the length


def station(t):
    """t in -1..1 (stern..bow): half-width at gunwale, half-width of the flat bottom, bottom z, gunwale z."""
    a = abs(t)
    w = B * 0.5 * (1 - 0.42 * a ** 2.4)
    wb = w * (0.78 - 0.18 * a ** 2)
    rocker = (0.42 if t > 0 else 0.26) * a ** 3.2          # ends sweep up out of the water, bow more
    zb = -DRAFT + rocker
    zt = -DRAFT + D + (0.30 if t > 0 else 0.20) * a ** 2.6  # sheer rises toward the ends
    return w, wb, zb, zt


bm = bmesh.new()
uv_layer = bm.loops.layers.uv.new("UVMap")


def quad(vs, uvs, mat):
    f = bm.faces.new([bm.verts.new(v) for v in vs])
    f.material_index = mat
    for loop, uv in zip(f.loops, uvs):
        loop[uv_layer].uv = uv
    f.smooth = False
    return f


def section_pts(t, inset=0.0):
    w, wb, zb, zt = station(t)
    y = t * L * 0.5
    # outer: gunwale left, chine left, chine right, gunwale right
    return [Vector((-(w - inset), y, zt)), Vector((-(wb - inset), y, zb + inset)),
            Vector((wb - inset, y, zb + inset)), Vector((w - inset, y, zt))]


ts = [-1 + 2 * i / (N - 1) for i in range(N)]
outer = [section_pts(t) for t in ts]
inner = [section_pts(t, T) for t in ts]


def arc_v(pts):
    # v coordinate around the section (metres) so planks run along the length
    acc = [0.0]
    for a, b in zip(pts, pts[1:]):
        acc.append(acc[-1] + (b - a).length)
    return acc


for i in range(N - 1):
    for s in range(3):
        o0, o1 = outer[i], outer[i + 1]
        n0, n1 = inner[i], inner[i + 1]
        va, vb = arc_v(o0), arc_v(o1)
        u0, u1 = o0[0].y / 1.2, o1[0].y / 1.2
        # outside skin (faces outward)
        quad([o0[s], o1[s], o1[s + 1], o0[s + 1]],
             [(u0, va[s] / 1.2), (u1, vb[s] / 1.2), (u1, vb[s + 1] / 1.2), (u0, va[s + 1] / 1.2)], 0)
        # inside skin (faces inward)
        quad([n0[s + 1], n1[s + 1], n1[s], n0[s]],
             [(u0, va[s + 1] / 1.2), (u1, vb[s + 1] / 1.2), (u1, vb[s] / 1.2), (u0, va[s] / 1.2)], 0)
    # gunwale caps
    for s in (0, 3):
        o0, o1, n0, n1 = outer[i][s], outer[i + 1][s], inner[i][s], inner[i + 1][s]
        u0, u1 = o0.y / 1.2, o1.y / 1.2
        if s == 0:
            quad([o0, n0, n1, o1], [(u0, 0), (u0, 0.05), (u1, 0.05), (u1, 0)], 0)
        else:
            quad([o0, o1, n1, n0], [(u0, 0), (u1, 0), (u1, 0.05), (u0, 0.05)], 0)

# transoms (square ends): solid boards closing both ends
for idx, sign in ((0, -1), (N - 1, 1)):
    o, n = outer[idx], inner[idx]
    pts_o = [o[0], o[1], o[2], o[3]]
    pts_n = [p + Vector((0, -sign * T * 1.5, 0)) for p in pts_o]
    uv = [((p.x + 1) / 1.2, (p.z + 1) / 1.2) for p in pts_o]
    if sign > 0:
        quad(pts_o, uv, 0)
        quad(list(reversed(pts_n)), list(reversed(uv)), 0)
    else:
        quad(list(reversed(pts_o)), list(reversed(uv)), 0)
        quad(pts_n, uv, 0)
    for a, b in zip(range(4), [1, 2, 3, 0]):
        quad([pts_o[a], pts_o[b], pts_n[b], pts_n[a]] if sign > 0 else [pts_o[b], pts_o[a], pts_n[a], pts_n[b]],
             [(0, 0), (0.2, 0), (0.2, 0.05), (0, 0.05)], 0)


def box(c, size, mat, rot_z=0.0):
    hx, hy, hz = size[0] / 2, size[1] / 2, size[2] / 2
    corners = [Vector((sx * hx, sy * hy, sz * hz)) for sx in (-1, 1) for sy in (-1, 1) for sz in (-1, 1)]
    cz, sz_ = math.cos(rot_z), math.sin(rot_z)
    corners = [Vector((p.x * cz - p.y * sz_, p.x * sz_ + p.y * cz, p.z)) + Vector(c) for p in corners]
    F = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]
    for f in F:
        vs = [corners[k] for k in f]
        quad(vs, [(0, 0), (size[1] / 1.2, 0), (size[1] / 1.2, size[0] / 1.2), (0, size[0] / 1.2)], mat)


# thwarts (seats) across the boat
for t in (-0.42, 0.30):
    w, wb, zb, zt = station(t)
    box((0, t * L * 0.5, zt - 0.09), (2 * (w - T) - 0.02, 0.26, 0.05), 0)
# floorboards on the flat bottom
for i, t in enumerate([-0.7 + 0.16 * k for k in range(10)]):
    w, wb, zb, zt = station(t)
    box((0, t * L * 0.5, zb + T + 0.015), (2 * (wb - T) - 0.03, 0.2, 0.025), 1)


def cylinder(a, b, r, mat, seg=8):
    a, b = Vector(a), Vector(b)
    axis = (b - a).normalized()
    side = axis.cross(Vector((0, 0, 1)))
    if side.length < 1e-3:
        side = axis.cross(Vector((1, 0, 0)))
    side.normalize()
    up = side.cross(axis)
    ring_a = [a + (side * math.cos(2 * math.pi * k / seg) + up * math.sin(2 * math.pi * k / seg)) * r for k in range(seg)]
    ring_b = [p + (b - a) for p in ring_a]
    ln = (b - a).length
    for k in range(seg):
        k2 = (k + 1) % seg
        quad([ring_a[k], ring_a[k2], ring_b[k2], ring_b[k]], [(0, k / seg), (0, (k + 1) / seg), (ln, (k + 1) / seg), (ln, k / seg)], mat)


# the punting pole lying along the boat, resting on the thwarts
w, wb, zb, zt = station(0.3)
cylinder((-0.28, -1.95, zt - 0.035), (0.22, 2.05, zt + 0.02), 0.03, 1)

# iron mooring ring on the bow transom top (submesh 2 = rope point)
wB, wbB, zbB, ztB = station(1.0)
ring_c = Vector((0, L * 0.5 - 0.02, ztB + 0.07))
seg, R, r = 12, 0.075, 0.016
for i in range(seg):
    for j in range(6):
        def P(ii, jj):
            a = 2 * math.pi * ii / seg
            b = 2 * math.pi * jj / 6
            ce = Vector((0, math.cos(a) * R, math.sin(a) * R))
            n = Vector((0, math.cos(a), math.sin(a)))
            return ring_c + ce + n * math.cos(b) * r + Vector((1, 0, 0)) * math.sin(b) * r
        quad([P(i, j), P(i + 1, j), P(i + 1, j + 1), P(i, j + 1)], [(0, 0), (1, 0), (1, 1), (0, 1)], 2)
# a short bow post the ring hangs from
box((0, L * 0.5 - 0.05, ztB + 0.02), (0.12, 0.1, 0.1), 0)

me = bpy.data.meshes.new("RF_Boat")
bm.normal_update()
bm.to_mesh(me)
bm.free()
for name, col in (("RF_BoatWood", (0.45, 0.33, 0.2, 1)), ("RF_BoatWoodDark", (0.25, 0.18, 0.11, 1)), ("RF_BoatIron", (0.1, 0.1, 0.1, 1))):
    m = bpy.data.materials.new(name)
    m.diffuse_color = col
    me.materials.append(m)
ob = bpy.data.objects.new("RF_Boat", me)
bpy.context.scene.collection.objects.link(ob)
bpy.context.view_layer.objects.active = ob
ob.select_set(True)
bpy.ops.object.mode_set(mode="EDIT")
bpy.ops.mesh.select_all(action="SELECT")
bpy.ops.mesh.remove_doubles(threshold=0.0005)
bpy.ops.mesh.normals_make_consistent(inside=False)
bpy.ops.object.mode_set(mode="OBJECT")

print("[boat] verts", len(me.vertices), "faces", len(me.polygons))
bpy.ops.export_scene.fbx(filepath=OUT, use_selection=True, object_types={"MESH"}, apply_scale_options="FBX_SCALE_ALL",
                         bake_space_transform=True, axis_forward="-Z", axis_up="Y", mesh_smooth_type="FACE",
                         add_leaf_bones=False, path_mode="STRIP")
if BLEND:
    bpy.ops.wm.save_as_mainfile(filepath=BLEND)
print("[boat] wrote", OUT)
