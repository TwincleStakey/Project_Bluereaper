# 장산범 v2: bring the rusty gramophone over from the v1 prop file (werewolf_gramophone_v4.blend) onto the v2 rig.
# Run on video2rig/projects/jsb_v2/autorig/rigged.blend:
#   blender -b rigged.blend -P v2_gramo.py -- <out.blend>
# The gramophone keeps its v1 layout relative to its mount point M0; it is shifted so M0 lands on the v2 back above the
# front shoulders, and bone-parented to Spine3. Prints M0_V2 and DELTA for the snake script (v2_snake.py reads them from
# the custom properties it stores on the scene).
import bpy, sys, math
from mathutils import Vector, Matrix
import mathutils
OUT = sys.argv[sys.argv.index('--') + 1]
SRC = r"C:/Users/PC/video2rig/projects/werewolf/props/werewolf_gramophone_v4.blend"
M0_V1 = Vector((0.02, -0.175, 0.372))
sc = bpy.context.scene
arm = bpy.data.objects['AutoRig']
body = [o for o in bpy.data.objects if o.type == 'MESH' and o.name.startswith('tripo')][0]

with bpy.data.libraries.load(SRC, link=False) as (df, dt):
    dt.collections = [c for c in df.collections if c == 'Gramophone']
col = dt.collections[0]
sc.collection.children.link(col)
bpy.context.view_layer.update()
objs = list(col.objects)
old_parents = {o.parent for o in objs if o.parent}
print('GRAMO objs', len(objs), 'v1 parent bone', {o.parent_bone for o in objs})
world = {o.name: o.matrix_world.copy() for o in objs}

# where the mount goes on the v2 body: on the spine line above the front shoulders, at the skin under the mane tips
dg = bpy.context.evaluated_depsgraph_get()
bvh = mathutils.bvhtree.BVHTree.FromObject(body, dg)
def top_z(x, y):
    hit = bvh.ray_cast(Vector((x, y, 2.0)), Vector((0, 0, -1)))
    return hit[0].z if hit[0] is not None else None
zs = sorted(z for z in (top_z(dx, -0.19 + dy) for dx in (-0.02, -0.01, 0, 0.01, 0.02) for dy in (-0.02, -0.01, 0, 0.01, 0.02)) if z is not None)
z_mount = zs[len(zs) // 4]                       # lower quartile: the back under the loose fur tufts
M0_V2 = Vector((0.0, -0.19, z_mount - 0.012))
DELTA = M0_V2 - M0_V1
print('MOUNT_Z samples %.3f..%.3f -> M0_V2 %s DELTA %s' % (zs[0], zs[-1], tuple(round(v, 4) for v in M0_V2), tuple(round(v, 4) for v in DELTA)))

T = Matrix.Translation(DELTA)
for o in objs:
    o.parent = None
    o.matrix_world = T @ world[o.name]
bpy.context.view_layer.update()
for p in old_parents:                              # the v1 armature (and anything else that came along) is not wanted
    for ch in list(p.children):
        if ch not in objs: bpy.data.objects.remove(ch, do_unlink=True)
    bpy.data.objects.remove(p, do_unlink=True)
for o in list(bpy.data.objects):
    if o.type == 'MESH' and o.name.startswith('tripo') and o is not body: bpy.data.objects.remove(o, do_unlink=True)
bone = arm.pose.bones['Spine3']
for o in objs:
    mw = o.matrix_world.copy()
    o.parent = arm; o.parent_type = 'BONE'; o.parent_bone = 'Spine3'
    o.matrix_world = mw
bpy.context.view_layer.update()
sc['jsb_m0'] = list(M0_V2); sc['jsb_delta'] = list(DELTA)
print('GRAMO_OK', OUT, [o.name for o in objs][:6])
bpy.ops.wm.save_as_mainfile(filepath=OUT, copy=True)
