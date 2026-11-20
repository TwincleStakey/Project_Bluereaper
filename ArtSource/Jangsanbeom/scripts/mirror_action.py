# mirror a quadruped action left<->right (armature X axis) and save as a new blend
import bpy, sys
from mathutils import Matrix
a = sys.argv[sys.argv.index('--')+1:]; out = a[0]
sc = bpy.context.scene
arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
act = arm.animation_data.action
f0, f1 = int(act.frame_range[0]), int(act.frame_range[1])
S = Matrix.Diagonal((-1, 1, 1, 1))
def mir(n):
    for x, y in (('.L', '.R'), ('.R', '.L'), ('_L', '_R'), ('_R', '_L')):
        if n.endswith(x): return n[:-2] + y
    return n
pbs = arm.pose.bones
order = sorted(pbs, key=lambda p: len(p.parent_recursive))
frames = {}
for f in range(f0, f1 + 1):
    sc.frame_set(f); frames[f] = {p.name: p.matrix.copy() for p in pbs}
new = bpy.data.actions.new(act.name + '_mirror'); arm.animation_data.action = new
for p in pbs: p.rotation_mode = 'QUATERNION'
for f in range(f0, f1 + 1):
    for p in order:
        src = mir(p.name)
        if src not in frames[f]: src = p.name
        p.matrix = S @ frames[f][src] @ S
        bpy.context.view_layer.update()
    for p in pbs:
        p.keyframe_insert('location', frame=f); p.keyframe_insert('rotation_quaternion', frame=f); p.keyframe_insert('scale', frame=f)
sc.frame_start, sc.frame_end = f0, f1
print('MIRROR_OK', f0, f1, len(pbs))
bpy.ops.wm.save_as_mainfile(filepath=out)
