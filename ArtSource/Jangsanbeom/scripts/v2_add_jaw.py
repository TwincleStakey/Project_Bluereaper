# 장산범 v2: open the jaw in clips that came from rigs without a jaw (v1 howl, tiger/predator roars).
#   blender -b out/<clip>.blend -P v2_add_jaw.py -- <mode> <max deg> <out base>
# mode "head": the jaw opens as the head lifts above its first-frame pitch (a howl at the sky);
# mode "mid":  the jaw opens over the middle of the clip (a roar straight ahead).
# Writes <out>.blend and <out>.fbx (same export as retarget.py).
import bpy, sys, math
from mathutils import Vector, Matrix
a = sys.argv[sys.argv.index('--') + 1:]
mode, maxdeg, out = a[0], float(a[1]), a[2]
sc = bpy.context.scene
arm = bpy.data.objects['AutoRig']
act = arm.animation_data.action
f0, f1 = int(act.frame_range[0]), int(act.frame_range[1])
pb_head, pb_jaw = arm.pose.bones['Head'], arm.pose.bones['Jaw']
pb_jaw.rotation_mode = 'QUATERNION'
def head_pitch():
    d = (arm.matrix_world @ pb_head.tail) - (arm.matrix_world @ pb_head.head)
    return math.degrees(math.atan2(d.z, math.hypot(d.x, d.y)))
sc.frame_set(f0); p0 = head_pitch()
pitches = []
for f in range(f0, f1 + 1): sc.frame_set(f); pitches.append(head_pitch())
pmax = max(pitches)
print('JAW head pitch first %.1f max %.1f' % (p0, pmax))
def amount(i, f):
    if mode == 'head':
        rise = pitches[i] - p0; full = max(8.0, (pmax - p0) * 0.6)
        x = max(0.0, min(1.0, (rise - 4.0) / full))
    else:
        u = (f - f0) / max(1, f1 - f0)
        x = max(0.0, min(1.0, min((u - 0.12) / 0.15, (0.9 - u) / 0.15)))
    return x * x * (3 - 2 * x)
# jaw hinge = the head's sideways axis; the jaw opens downward
for i, f in enumerate(range(f0, f1 + 1)):
    sc.frame_set(f)
    k = amount(i, f)
    base = pb_jaw.rotation_quaternion.copy()
    hm = (arm.matrix_world @ pb_head.matrix).to_3x3()
    side = hm @ Vector((0, 0, 1))
    if side.x < 0: side = -side
    jm = arm.matrix_world @ pb_jaw.matrix
    R = Matrix.Translation(jm.translation) @ Matrix.Rotation(math.radians(maxdeg * k), 4, side) @ Matrix.Translation(-jm.translation)
    pb_jaw.matrix = arm.matrix_world.inverted() @ R @ jm
    bpy.context.view_layer.update()
    pb_jaw.keyframe_insert('rotation_quaternion', frame=f)
print('JAW keys', f1 - f0 + 1, 'max', maxdeg)
bpy.ops.wm.save_as_mainfile(filepath=out + '.blend', copy=True)
bpy.ops.object.select_all(action='DESELECT')
for o in sc.objects:
    if o.type in ('ARMATURE', 'MESH') and not o.name.startswith('Plane') and o.visible_get(): o.select_set(True)
bpy.ops.export_scene.fbx(filepath=out + '.fbx', use_selection=True, apply_scale_options='FBX_SCALE_ALL', bake_anim=True,
                         bake_anim_use_all_actions=False, bake_anim_use_nla_strips=False, add_leaf_bones=False, path_mode='COPY', embed_textures=False)
print('JAW_OK', out)
