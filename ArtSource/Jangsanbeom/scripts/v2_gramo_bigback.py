# 장산범 v2 (11-20 Kido: "몸속에 다 파고들자나 예전 큰거 하나로 돌려봐"): back to the single big gramophone on top of the back
# (the 11-19 look, before v2_gramo_embed.py). Only adds the box-projection UVs the Gramophone parts lack (without them the
# rust drew as one flat brown in Unity) and converts the rope/pipe curves to meshes so the FBX export keeps them.
#   blender -b jsb_v2_shell2.blend -P v2_gramo_bigback.py -- <out.blend>
#   (input = jsb_v2_full.blend -> v2_fur_trim.py -> v2_fur_shellnormals.py); then v2_export_model.py
import bpy, sys
OUT = sys.argv[sys.argv.index('--') + 1]
col = bpy.data.collections['Gramophone']
vl = bpy.context.view_layer
curves = [o for o in col.objects if o.type == 'CURVE']
for o in curves:
    bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); vl.objects.active = o
    bpy.ops.object.convert(target='MESH')
n = 0
for o in col.objects:
    if o.type != 'MESH': continue
    me = o.data
    if len(me.uv_layers) > 0: continue
    uv = me.uv_layers.new(name='UVMap')
    for poly in me.polygons:
        nn = poly.normal; ax = max(range(3), key=lambda k: abs(nn[k])); u_ax, v_ax = [(1, 2), (0, 2), (0, 1)][ax]
        for li in poly.loop_indices:
            co = me.vertices[me.loops[li].vertex_index].co; uv.data[li].uv = (co[u_ax] / 0.1, co[v_ax] / 0.1)
    n += 1
# 11-20 Kido ("측음기 없자나", scrape frame): the horn points forward almost level over the head, so when the head goes down
# in the scrape it sinks in front of the face to the floor and from the top camera the back looks bare. Tip the whole
# prop (and the snake coil on it) up about its drum so the bell rises above the shoulders.
import math, sys as _s
from mathutils import Matrix, Vector
_a = _s.argv[_s.argv.index('--') + 1:]
TILT = float(_a[1]) if len(_a) > 1 else 115.0   # 35 pointed it at the front camera (out of view); 115 = bell up and back over the rump
sc = bpy.context.scene
DELTA = Vector(sc['jsb_delta']) if 'jsb_delta' in sc.keys() else Vector((0, 0, 0))
C = Vector((0.02, 0.005, 0.457)) + DELTA
# pivot = where the mount meets the back (lowest point of the prop), so the foot stays planted on the body
vl.update()
low = min(((o.matrix_world @ v.co) for o in col.objects if o.type == 'MESH' for v in o.data.vertices), key=lambda p: p.z)
C = low
R = Matrix.Translation(C) @ Matrix.Rotation(math.radians(-TILT), 4, 'X') @ Matrix.Translation(-C)
print('PIVOT', tuple(round(x, 3) for x in C))
moved = 0
for o in list(col.objects) + [o for o in bpy.data.objects if o.name.startswith('SnakeTop')]:
    # rotate the mesh data, not the object: the objects are bone-parented and a changed object transform did not survive
    # the trip to Unity (the whole prop vanished in game while Blender showed it right)
    if o.type != 'MESH': continue
    L = o.matrix_world.inverted() @ R @ o.matrix_world
    o.data.transform(L); o.data.update(); moved += 1
print('TILT', TILT, 'objects', moved)
# 11-20 found in game (side view of the scrape): the bone-parented prop came out flipped upside down in Unity (the bell hung
# under the belly / under the head), while Blender showed it on top - the FBX bone-parent hop does not survive the Blender ->
# Unity bone axis change. Skin it instead, like the snake: bake every part into armature space and weight it 100 % to
# Spine3 with an Armature modifier, so Unity places it through the same skinning as the body.
arm = bpy.data.objects['AutoRig']
skinned = 0
vl.update()
body = [b for b in bpy.data.objects if b.type == 'MESH' and b.name.startswith('tripo')][0]
bw = body.matrix_world
near = sorted(body.data.vertices, key=lambda v: ((bw @ v.co) - C).length)[:24]   # C = the mount foot
acc = {}
for v in near:
    for ge in v.groups:
        n = body.vertex_groups[ge.group].name; acc[n] = acc.get(n, 0.0) + ge.weight
tot = sum(acc.values()) or 1.0
foot_w = {k: v / tot for k, v in acc.items() if v / tot > 0.02}
t2 = sum(foot_w.values()); foot_w = {k: v / t2 for k, v in foot_w.items()}
print('FOOT WEIGHTS', {k: round(v, 2) for k, v in foot_w.items()})
parts = [o for o in col.objects if o.type == 'MESH']
# bake the bend/solidify modifiers of the cloth strips first (they work in object space, which is about to change)
for o in parts:
    if any(m.type != 'ARMATURE' for m in o.modifiers):
        bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); vl.objects.active = o
        for m in list(o.modifiers):
            if m.type != 'ARMATURE': bpy.ops.object.modifier_apply(modifier=m.name)
# record everything first: some parts are children of other parts, and resetting a parent moved its children
info = {o.name: (o.matrix_world.copy(), o.parent_bone if o.parent_type == 'BONE' and o.parent_bone else None, o.parent) for o in parts}
def bone_of(o):
    mw, b, par = info[o.name]
    while b is None and par is not None and par.name in info: mw2, b, par = info[par.name]
    return GBONE   # 11-20: Spine3 swings the prop under the belly in the pinned scrape clips; see GBONE
GBONE = _a[2] if len(_a) > 2 else 'Spine2'
bones = {o.name: bone_of(o) for o in parts}
for o in parts: o.parent = None
for o in parts:
    mw = info[o.name][0]; bone = bones[o.name]
    o.data.transform(mw); o.matrix_world = Matrix.Identity(4)
    o.parent = arm; o.parent_type = 'OBJECT'; o.matrix_parent_inverse = arm.matrix_world.inverted()
    # 11-20: weighting to one spine bone still flipped it under the belly in the pinned scrapes (their trunk bones are
    # baked in odd orientations that the body's blended weights cancel out). Copy the skin weights of the body at the
    # mount foot instead, so the prop rides that exact patch of skin in every clip.
    for g, w in foot_w.items():
        vg = o.vertex_groups.get(g) or o.vertex_groups.new(name=g)
        vg.add(list(range(len(o.data.vertices))), w, 'REPLACE')
    if not any(m.type == 'ARMATURE' for m in o.modifiers):
        m = o.modifiers.new('Armature', 'ARMATURE'); m.object = arm
    skinned += 1
print('SKINNED', skinned)
print('BIGBACK_OK curves->mesh', len(curves), 'uv added', n)
bpy.ops.wm.save_as_mainfile(filepath=OUT, copy=True)
