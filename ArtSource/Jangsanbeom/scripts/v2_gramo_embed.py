# 장산범 v2 (11-19 Kido: "축음기를 뒤쪽을 몸에 박힌 뱀처럼, 예전에 했던 것처럼 여러 개 박아볼까, 위에 큰 거 하나 빼고").
# The big gramophone on the back goes. Instead eight gramophone horns grow OUT of the body like the parasite snake does:
# only the horn (with its rope coils) is kept, its narrow end sunk into the flesh, and where it breaks the skin there is
# the same swollen torn lip the snake's holes have (material HoleLip). Sizes 0.45-0.95, each pointing its own way, spread
# over the neck, shoulders, back, flanks and rump, bone-parented to the nearest spine bone. Short stems = little leverage,
# so they swing far less than the tall bracket did.
#   blender -b jsb_v2_shell.blend -P v2_gramo_embed.py -- <out.blend> [count 7] [seed 5] [talismans 11]
#   (input = jsb_v2_full.blend after v2_fur_shellnormals.py)
import bpy, sys, math, random
from mathutils import Vector, Matrix, Quaternion
import mathutils
a = sys.argv[sys.argv.index('--') + 1:]
OUT = a[0]; COUNT = int(a[1]) if len(a) > 1 else 8; random.seed(int(a[2]) if len(a) > 2 else 5)
sc = bpy.context.scene; vl = bpy.context.view_layer
arm = bpy.data.objects['AutoRig']
body = [o for o in bpy.data.objects if o.type == 'MESH' and o.name.startswith('tripo')][0]
col = bpy.data.collections['Gramophone']
objs = [o for o in col.objects if o.type == 'MESH']

# ---- UVs for the parts that had none (the rust drew as one flat colour in Unity)
for o in objs:
    me = o.data
    if len(me.uv_layers) > 0: continue
    uv = me.uv_layers.new(name='UVMap')
    for poly in me.polygons:
        n = poly.normal; ax = max(range(3), key=lambda k: abs(n[k])); u_ax, v_ax = [(1, 2), (0, 2), (0, 1)][ax]
        for li in poly.loop_indices:
            co = me.vertices[me.loops[li].vertex_index].co; uv.data[li].uv = (co[u_ax] / 0.1, co[v_ax] / 0.1)

# ---- the horn in the v2 file: axis from the drum centre (values of gramo.py, shifted by the v2 mount delta)
DELTA = Vector(sc['jsb_delta']) if 'jsb_delta' in sc.keys() else Vector((0, 0, 0))
C = Vector((0.02, 0.005, 0.457)) + DELTA
D = Vector((0, -1, 0.21)).normalized()
DRUM_L, HORN_L, BELL_R, NECK_R = 0.075, 0.29, 0.082, 0.017
vl.update()
def axial(o):
    c = sum((o.matrix_world @ Vector(b) for b in o.bound_box), Vector()) / 8.0
    return (c - C).dot(D), ((c - C) - D * (c - C).dot(D)).length
horn_objs = []
for o in objs:
    z, r = axial(o)
    if o.name.startswith('Horn'): horn_objs.append(o)          # the horn only (bell, inside, rim): no drum, bracket, bands or loose rope
print('HORN parts', len(horn_objs), 'of', len(objs), sorted({o.name.split('.')[0] for o in horn_objs}))
# where the horn's narrow end really is along the axis (measured, so the bell sits right on its pipe)
Z0 = min(((o.matrix_world @ v.co) - C).dot(D) for o in horn_objs for v in o.data.vertices) + 0.004
print('HORN narrow end at %.3f along the axis' % Z0)
world = {o.name: o.matrix_world.copy() for o in horn_objs}

# ---- where: around the spine, on the skin under the fur
dg = bpy.context.evaluated_depsgraph_get()
bvh = mathutils.bvhtree.BVHTree.FromObject(body, dg)
chain = [b for b in ('Neck1', 'Spine3', 'Spine2', 'Spine1', 'Hips') if b in arm.data.bones]
segs = {b: (arm.matrix_world @ arm.data.bones[b].head_local, arm.matrix_world @ arm.data.bones[b].tail_local) for b in chain}
def nearest(p):
    best, bd, bp = None, 1e9, None
    for b, (h, t) in segs.items():
        ab = t - h; u = max(0.0, min(1.0, (p - h).dot(ab) / max(1e-9, ab.dot(ab)))); q = h + ab * u; d = (q - p).length
        if d < bd: best, bd, bp = b, d, q
    return best, bp
ys = [v.y for s in segs.values() for v in s]; ylo, yhi = min(ys) + 0.02, max(ys) + 0.12     # a little past the hips onto the rump
zs = [v.z for s in segs.values() for v in s]; zmid = sum(zs) / len(zs)
spots = []
# 11-20 Kido ("한쪽에 너무 몰려 있어, 몸 여기저기 잘 보이게"): even stations from the neck to the rump, alternating sides
for k in range(COUNT):
    y = ylo + (yhi - ylo) * (k + 0.5) / COUNT + random.uniform(-0.015, 0.015)
    side = 1 if k % 2 == 0 else -1
    th = math.radians(side * random.uniform(8, 30))
    axisP = Vector((0, y, zmid - 0.03))
    radial = Vector((math.sin(th), 0, math.cos(th)))
    depth = [(h[0] - axisP).length for h in (bvh.ray_cast(axisP + radial * 1.0 + Vector((dx, dy, 0)), -radial) for dx in (-0.01, 0, 0.01) for dy in (-0.01, 0, 0.01)) if h[0] is not None]
    if not depth: continue
    depth.sort(); p = axisP + radial * depth[len(depth) // 4]
    spots.append((p, radial))
print('SPOTS', len(spots), 'y range %.2f..%.2f' % (ylo, yhi))

mat_lip = bpy.data.materials.get('HoleLip'); made = []; biggest = None
# 11-20 Kido ("앞쪽이 왜 죄다 타원형"): at 0.42-0.75 the bells were wider than the body and from the game camera the back
# read as a heap of big ovals. Smaller, so the fur body stays the main shape (mockup B proportions).
sizes = sorted([random.uniform(0.28, 0.46) for _ in spots], reverse=True)
for k, (p, radial) in enumerate(spots):
    s = sizes[k]
    # mockup B, many times over: a rusty neck pipe climbs out of a torn hole in the back and curves over into the bell,
    # which faces forward/outward. The pipe starts inside the body, so nothing floats.
    up = radial.normalized()
    # the bells face every which way - up, out to their side, back, forward - so from above they read as round mouths,
    # not a row of ellipses all looking ahead (11-20 Kido: "앞쪽이 왜 죄다 타원형")
    sideV = Vector((math.copysign(1.0, p.x if abs(p.x) > 1e-4 else 1.0), 0, 0))
    # no bell looks straight up (a full disc to the top-down camera): out to its side, back, or forward-out, all tipped up
    look = [sideV + Vector((0, 0.2, 0.55)), Vector((0, 0.9, 0.6)) + sideV * 0.3, sideV + Vector((0, -0.5, 0.5)), sideV * 0.8 + Vector((0, 0.6, 0.45))][k % 4]
    n = (look.normalized() + Vector((random.uniform(-0.2, 0.2), random.uniform(-0.2, 0.2), random.uniform(0, 0.2)))).normalized()
    L = 0.11 * s
    # 11-20 Kido ("나팔들 좀 몸속으로 박아"): no long neck in the air any more - the bell's narrow end sits just under the fur,
    # so the horn grows straight out of the torn hole; only a stub of pipe inside the body ties it in
    # "훨씬 몸속에 박혀야돼": the bell itself goes in up to ~half its length (SINK), only the flared mouth sticks out
    SINK = 0.5
    P0 = p - up * 0.03; P3 = p - n * (HORN_L * s * SINK)
    H0 = P0 - up * L * 0.1; H1 = P3 - n * L * 0.1
    cu = bpy.data.curves.new('GramoPipe%d' % k, 'CURVE'); cu.dimensions = '3D'; cu.bevel_depth = NECK_R * s * 0.85; cu.bevel_resolution = 3; cu.resolution_u = 14
    sp = cu.splines.new('BEZIER'); sp.bezier_points.add(1)
    a0, a1 = sp.bezier_points[0], sp.bezier_points[1]
    a0.co = P0; a0.handle_left = P0 - (H0 - P0); a0.handle_right = H0
    a1.co = P3; a1.handle_left = H1; a1.handle_right = P3 + (P3 - H1)
    po = bpy.data.objects.new('GramoPipe%d' % k, cu); col.objects.link(po); vl.update()
    bpy.ops.object.select_all(action='DESELECT'); po.select_set(True); vl.objects.active = po
    bpy.ops.object.convert(target='MESH'); po = vl.objects.active
    mi = bpy.data.materials.get('GramoIron')
    if mi: po.data.materials.clear(); po.data.materials.append(mi)
    me2 = po.data
    if len(me2.uv_layers) == 0:
        uvl2 = me2.uv_layers.new(name='UVMap')
        for poly in me2.polygons:
            nn = poly.normal; ax = max(range(3), key=lambda q: abs(nn[q])); u_ax, v_ax = [(1, 2), (0, 2), (0, 1)][ax]
            for li in poly.loop_indices:
                co = me2.vertices[me2.loops[li].vertex_index].co; uvl2.data[li].uv = (co[u_ax] / 0.1, co[v_ax] / 0.1)
    R = D.rotation_difference(n).to_matrix().to_4x4() @ Matrix.Rotation(random.uniform(0, 6.28), 4, D)
    T = Matrix.Translation(P3) @ R @ Matrix.Scale(s, 4) @ Matrix.Translation(-(C + D * Z0))
    dup = [po]
    for o in horn_objs:
        d = o.copy(); d.data = o.data.copy(); col.objects.link(d); d.parent = None; d.matrix_world = T @ world[o.name]; dup.append(d)
    n = up
    # the torn lip where it breaks the skin (the horn is ~NECK_R wide there, a bit more after the sink)
    r_here = NECK_R * s * 0.95
    bpy.ops.mesh.primitive_torus_add(major_radius=r_here * 1.9, minor_radius=r_here * 0.85, major_segments=20, minor_segments=8, location=p + n * 0.002)
    lip = vl.objects.active; lip.rotation_mode = 'QUATERNION'; lip.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(n)
    for v in lip.data.vertices: v.co *= random.uniform(0.86, 1.16)          # ragged, not a machined ring
    lip.data.materials.clear()
    if mat_lip: lip.data.materials.append(mat_lip)
    for c2 in list(lip.users_collection): c2.objects.unlink(lip)
    col.objects.link(lip); dup.append(lip)
    vl.update()
    bpy.ops.object.select_all(action='DESELECT')
    for d in dup: d.select_set(True)
    vl.objects.active = dup[0]; bpy.ops.object.join()
    j = vl.objects.active; j.name = 'GramoEmbed%d' % k; j.data.name = j.name
    bone, _ = nearest(p)
    mw = j.matrix_world.copy(); j.parent = arm; j.parent_type = 'BONE'; j.parent_bone = bone; j.matrix_world = mw
    made.append((j.name, bone, round(s, 2), tuple(round(v, 3) for v in p)))
    if biggest is None: biggest = (p, n, s, bone)
vl.update()
for m in made: print('EMBED', m)

# ---- yellow talismans (부적) stuck into the back between the horns, as in mockups/backprops_A_vs_B.png: paper strips
# standing out of the fur, leaning back toward the tail, a little curled. Material "TalismanV2" (Unity: JSB_Gramo_Talisman).
import os, bmesh
TEX = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'textures', 'talisman.png')
mt = bpy.data.materials.get('TalismanV2') or bpy.data.materials.new('TalismanV2'); mt.use_nodes = True
nt = mt.node_tree; bsdf = nt.nodes.get('Principled BSDF')
if bsdf and os.path.exists(TEX):
    tn = nt.nodes.new('ShaderNodeTexImage'); tn.image = bpy.data.images.load(TEX, check_existing=True)
    nt.links.new(tn.outputs['Color'], bsdf.inputs['Base Color']); bsdf.inputs['Roughness'].default_value = 0.8
TAL = int(a[3]) if len(a) > 3 else 0      # 11-20 Kido: "부적은 없어도 돼" (pass a count to bring them back)
for k in range(TAL):
    y = ylo + (yhi - ylo) * (k + 0.25 + random.uniform(-0.12, 0.12)) / TAL
    side = 1 if k % 2 == 1 else -1
    th = math.radians(side * random.uniform(4, 40))
    axisP = Vector((0, y, zmid - 0.03)); radial = Vector((math.sin(th), 0, math.cos(th)))
    hit = bvh.ray_cast(axisP + radial * 1.0, -radial)
    if hit[0] is None: continue
    root = hit[0] - radial * 0.006                       # sunk a little into the fur shell
    w, h = random.uniform(0.020, 0.028), random.uniform(0.055, 0.085)
    lean = math.radians(random.uniform(25, 45)); twist = random.uniform(-0.6, 0.6)
    upv = (radial * math.cos(lean) + Vector((0, 1, 0)) * math.sin(lean)).normalized()      # out of the skin, leaning to the tail
    across = upv.cross(Vector((0, 1, 0))); across = (across if across.length > 1e-4 else Vector((1, 0, 0))).normalized()
    across = (across * math.cos(twist) + Vector((0, 1, 0)) * math.sin(twist)); across = (across - upv * across.dot(upv)).normalized()
    face = across.cross(upv).normalized(); curl = random.uniform(0.15, 0.35)
    bm2 = bmesh.new(); N = 6; rows = []
    for i in range(N + 1):
        t = i / N; c = root + upv * (h * t) + face * (curl * h * t * t)            # curls over like paper
        rows.append([bm2.verts.new(c + across * (sx * w / 2)) for sx in (-1, 1)])
    for r0, r1 in zip(rows, rows[1:]): bm2.faces.new((r0[0], r0[1], r1[1], r1[0]))
    me3 = bpy.data.meshes.new('Talisman%d' % k); bm2.to_mesh(me3); bm2.free()
    uv3 = me3.uv_layers.new(name='UVMap')
    for poly in me3.polygons:
        for li in poly.loop_indices:
            vi = me3.loops[li].vertex_index; uv3.data[li].uv = (vi % 2, (vi // 2) / N)
    me3.materials.append(mt)
    to = bpy.data.objects.new('Talisman%d' % k, me3); col.objects.link(to)
    to.modifiers.new('thick', 'SOLIDIFY').thickness = 0.0008
    vl.update(); bpy.ops.object.select_all(action='DESELECT'); to.select_set(True); vl.objects.active = to
    bpy.ops.object.modifier_apply(modifier='thick')
    bone, _ = nearest(root)
    mw = to.matrix_world.copy(); to.parent = arm; to.parent_type = 'BONE'; to.parent_bone = bone; to.matrix_world = mw
    print('TALISMAN', k, bone, tuple(round(v, 3) for v in root))

# ---- the big gramophone and the snake coil that climbed it go
removed = 0
for o in list(col.objects):                         # every part of the old prop, curves (rope coils, pipes) included
    if o.name.startswith(('GramoEmbed', 'Talisman')): continue
    bpy.data.objects.remove(o, do_unlink=True); removed += 1
for o in list(bpy.data.objects):
    if o.type == 'MESH' and o.name.startswith('SnakeTop'): bpy.data.objects.remove(o, do_unlink=True); removed += 1
print('REMOVED', removed, 'objects (big gramophone + SnakeTop)')

# ---- markers the howl FX looks for (rings start between the mouth and "HornRim", along Drum -> HornRim): the biggest horn
def marker(name, pos, bone):
    me = bpy.data.meshes.new(name); me.from_pydata([pos, pos + Vector((1e-4, 0, 0)), pos + Vector((0, 1e-4, 0))], [], [(0, 1, 2)])
    o = bpy.data.objects.new(name, me); col.objects.link(o); vl.update()
    mw = o.matrix_world.copy(); o.parent = arm; o.parent_type = 'BONE'; o.parent_bone = bone; o.matrix_world = mw
p, n, s, bone = biggest
marker('Drum', p, bone); marker('HornRim', p + n * (HORN_L * s * 0.82) + Vector((0, 0, 0.06 * s)), bone)
vl.update()
bpy.ops.wm.save_as_mainfile(filepath=OUT, copy=True)
print('GRAMO_EMBED_OK', OUT, len(made))
