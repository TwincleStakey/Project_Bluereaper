# 장산범 v2 (11-19 Kido: "축음기 색깔이 아니었는데, 텍스처랑 모양 원래 모델대로. 등 쪽 여기저기 7~8개 심어봐, 기생충처럼").
# 1) The rusty look was lost in Unity because the horn/drum/bands had NO UV layer (Blender textured them from object
#    coordinates), so the rust texture drew as one flat colour. Every gramophone mesh without UVs gets a box-projected UV.
# 2) Seven smaller gramophones are planted over the back like parasites breaking out: each is a joined copy of the whole
#    prop, scaled 0.28-0.55, turned and tilted at random, its mount sunk into the skin found by a ray cast, bone-parented to
#    the nearest spine bone.
#   blender -b jsb_v2_full.blend -P v2_gramo_multi.py -- <out.blend> [count 7] [seed 11]
import bpy, sys, math, random
from mathutils import Vector, Matrix, Euler
import mathutils
a = sys.argv[sys.argv.index('--') + 1:]
OUT = a[0]; COUNT = int(a[1]) if len(a) > 1 else 7; random.seed(int(a[2]) if len(a) > 2 else 11)
sc = bpy.context.scene
arm = bpy.data.objects['AutoRig']
body = [o for o in bpy.data.objects if o.type == 'MESH' and o.name.startswith('tripo')][0]
col = bpy.data.collections['Gramophone']
objs = [o for o in col.objects if o.type == 'MESH']
vl = bpy.context.view_layer

# ---- 1) UVs: box projection in object space, 1 UV unit = 0.1 m, so the rust has the same grain on every part
nuv = 0
for o in objs:
    me = o.data
    if len(me.uv_layers) > 0: continue
    uv = me.uv_layers.new(name='UVMap'); nuv += 1
    for poly in me.polygons:
        n = poly.normal; ax = max(range(3), key=lambda k: abs(n[k]))
        u_ax, v_ax = [(1, 2), (0, 2), (0, 1)][ax]
        for li in poly.loop_indices:
            co = me.vertices[me.loops[li].vertex_index].co
            uv.data[li].uv = (co[u_ax] / 0.1, co[v_ax] / 0.1)
print('UV added to', nuv, 'of', len(objs), 'gramophone meshes')

# ---- 2) parasite copies
vl.update()
M0 = Vector(sc['jsb_m0']) if 'jsb_m0' in sc.keys() else Vector((0.0, -0.19, 0.40))
dg = bpy.context.evaluated_depsgraph_get()
bvh = mathutils.bvhtree.BVHTree.FromObject(body, dg)
def skin(x, y):
    # the back under the loose fur: lower quartile of a little patch of rays
    zs = []
    for dx in (-0.012, 0, 0.012):
        for dy in (-0.012, 0, 0.012):
            h = bvh.ray_cast(Vector((x + dx, y + dy, 2.0)), Vector((0, 0, -1)))
            if h[0] is not None: zs.append(h[0].z)
    zs.sort(); return zs[len(zs) // 4] if zs else None
spine = ['Hips', 'Spine1', 'Spine2', 'Spine3', 'Neck1']
segs = {b: (arm.matrix_world @ arm.data.bones[b].head_local, arm.matrix_world @ arm.data.bones[b].tail_local) for b in spine if b in arm.data.bones}
def nearest_bone(p):
    best, bd = None, 1e9
    for b, (h, t) in segs.items():
        ab = t - h; u = max(0.0, min(1.0, (p - h).dot(ab) / max(1e-9, ab.dot(ab)))); d = (h + ab * u - p).length
        if d < bd: best, bd = b, d
    return best
y0 = min(s[0].y for s in segs.values()); y1 = max(s[1].y for s in segs.values()); ylo, yhi = min(y0, y1), max(y0, y1)
ys = [min(v.y for v in (s[0], s[1])) for s in segs.values()] + [max(v.y for v in (s[0], s[1])) for s in segs.values()]
ylo, yhi = min(ys) + 0.03, max(ys) - 0.02
print('back runs y %.3f..%.3f, main mount %s' % (ylo, yhi, tuple(round(v, 3) for v in M0)))
spots = []
tries = 0
while len(spots) < COUNT and tries < 4000:
    tries += 1
    x = random.uniform(-0.085, 0.085); y = random.uniform(ylo, yhi)
    if (Vector((x, y)) - Vector((M0.x, M0.y))).length < 0.10: continue          # clear of the big one
    if any((Vector((x, y)) - Vector((s.x, s.y))).length < 0.075 for s in spots): continue
    z = skin(x, y)
    if z is None or z < 0.2: continue
    spots.append(Vector((x, y, z)))
world = {o.name: o.matrix_world.copy() for o in objs}
made = []
for k, p in enumerate(spots):
    s = random.uniform(0.28, 0.55)
    side = 1.0 if p.x >= 0 else -1.0
    # lean out to its own side, point anywhere but mostly forward/up, like something that grew there
    rot = Euler((math.radians(random.uniform(-25, 30)), math.radians(side * random.uniform(8, 38)), math.radians(random.uniform(-75, 75))), 'XYZ').to_matrix().to_4x4()
    T = Matrix.Translation(p - Vector((0, 0, 0.015 * s))) @ rot @ Matrix.Scale(s, 4) @ Matrix.Translation(-M0)
    dup = []
    for o in objs:
        d = o.copy(); d.data = o.data.copy(); col.objects.link(d)
        d.parent = None; d.matrix_world = T @ world[o.name]; dup.append(d)
    vl.update()
    bpy.ops.object.select_all(action='DESELECT')
    for d in dup: d.select_set(True)
    vl.objects.active = dup[0]
    bpy.ops.object.join()
    j = vl.objects.active; j.name = 'GramoParasite%d' % k; j.data.name = j.name
    bone = nearest_bone(p)
    mw = j.matrix_world.copy(); j.parent = arm; j.parent_type = 'BONE'; j.parent_bone = bone; j.matrix_world = mw
    made.append((j.name, bone, round(s, 2), tuple(round(v, 3) for v in p)))
vl.update()
for m in made: print('PARASITE', m)
bpy.ops.wm.save_as_mainfile(filepath=OUT, copy=True)
print('GRAMO_MULTI_OK', OUT, len(made))
