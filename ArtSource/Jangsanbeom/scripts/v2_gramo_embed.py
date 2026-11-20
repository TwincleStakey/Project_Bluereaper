# 장산범 v2 (11-19 Kido: "축음기를 뒤쪽을 몸에 박힌 뱀처럼, 예전에 했던 것처럼 여러 개 박아볼까, 위에 큰 거 하나 빼고").
# The big gramophone on the back goes. Instead eight gramophone horns grow OUT of the body like the parasite snake does:
# only the horn (with its rope coils) is kept, its narrow end sunk into the flesh, and where it breaks the skin there is
# the same swollen torn lip the snake's holes have (material HoleLip). Sizes 0.45-0.95, each pointing its own way, spread
# over the neck, shoulders, back, flanks and rump, bone-parented to the nearest spine bone. Short stems = little leverage,
# so they swing far less than the tall bracket did.
#   blender -b jsb_v2_full.blend -P v2_gramo_embed.py -- <out.blend> [count 8] [seed 5]
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
Z0 = DRUM_L * 0.5                      # the horn's narrow end starts here on the axis
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
tries = 0
while len(spots) < COUNT and tries < 6000:
    tries += 1
    y = random.uniform(ylo, yhi); th = math.radians(random.uniform(-72, 72))        # angle from straight up, round the body
    if abs(th) < math.radians(12) and random.random() < 0.5: continue               # not all on the ridge
    axisP = Vector((0, y, zmid - 0.03))
    radial = Vector((math.sin(th), 0, math.cos(th)))
    hit = bvh.ray_cast(axisP + radial * 1.0, -radial)
    if hit[0] is None: continue
    # the fur tufts stand off the skin: take the deepest of a few rays as the skin
    depth = [(h[0] - axisP).length for h in (bvh.ray_cast(axisP + radial * 1.0 + Vector((dx, dy, 0)), -radial) for dx in (-0.01, 0, 0.01) for dy in (-0.01, 0, 0.01)) if h[0] is not None]
    depth.sort(); p = axisP + radial * depth[len(depth) // 4]
    if any((p - s[0]).length < 0.085 for s in spots): continue
    spots.append((p, radial))
print('SPOTS', len(spots), 'after', tries, 'tries; y range %.2f..%.2f' % (ylo, yhi))

mat_lip = bpy.data.materials.get('HoleLip'); made = []; biggest = None
sizes = sorted([random.uniform(0.34, 0.72) for _ in spots], reverse=True)
for k, (p, radial) in enumerate(spots):
    s = sizes[k]
    # points out of the body, leaning forward and up a little, each its own way
    n = (radial + Vector((0, -0.45, 0.25)) + Vector((random.uniform(-0.35, 0.35), random.uniform(-0.3, 0.3), random.uniform(-0.1, 0.3)))).normalized()
    R = D.rotation_difference(n).to_matrix().to_4x4() @ Matrix.Rotation(random.uniform(0, 6.28), 4, D)
    sink = 0.18 * HORN_L * s
    T = Matrix.Translation(p - n * sink) @ R @ Matrix.Scale(s, 4) @ Matrix.Translation(-(C + D * Z0))
    dup = []
    for o in horn_objs:
        d = o.copy(); d.data = o.data.copy(); col.objects.link(d); d.parent = None; d.matrix_world = T @ world[o.name]; dup.append(d)
    # the torn lip where it breaks the skin (the horn is ~NECK_R wide there, a bit more after the sink)
    r_here = (NECK_R + (BELL_R - NECK_R) * 0.18 ** 2) * s
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

# ---- the big gramophone and the snake coil that climbed it go
removed = 0
for o in list(col.objects):                         # every part of the old prop, curves (rope coils, pipes) included
    if o.name.startswith('GramoEmbed'): continue
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
marker('Drum', p, bone); marker('HornRim', p + n * (HORN_L * s * 0.82), bone)
vl.update()
bpy.ops.wm.save_as_mainfile(filepath=OUT, copy=True)
print('GRAMO_EMBED_OK', OUT, len(made))
