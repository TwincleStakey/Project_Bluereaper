# Probe a Mixamo clip (Y Bot, mixamorig): right-arm directions per frame in the character frame + a 3/4 sheet.
#   blender -b --python mx_probe.py -- <fbx> <outdir>
import bpy, sys, os, math
from mathutils import Vector
argv = sys.argv[sys.argv.index("--") + 1:]
FBX, OUT = argv[0], argv[1]
os.makedirs(OUT, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=FBX)
sc = bpy.context.scene
rig = [o for o in bpy.data.objects if o.type == "ARMATURE"][0]
act = rig.animation_data.action
f0, f1 = [int(x) for x in act.frame_range]
P = rig.pose.bones
def B(n): return P["mixamorig:" + n]
# character frame from the rest pose: Y Bot faces -Y? measure from hips->toes at frame f0
sc.frame_set(f0)
W_ = rig.matrix_world
toe = (W_ @ B("RightToeBase").head) - (W_ @ B("RightFoot").head); toe.z = 0; fwd = toe.normalized()
right = fwd.cross(Vector((0, 0, 1))).normalized()   # character's right
def C(v):   # world -> character frame (x right, y up, z forward)
    return (round(v.dot(right), 3), round(v.z, 3), round(v.dot(fwd), 3))
rows = []
for f in range(f0, f1 + 1):
    sc.frame_set(f); bpy.context.view_layer.update()
    sh = W_ @ B("RightArm").head; el = W_ @ B("RightForeArm").head; wr = W_ @ B("RightHand").head
    hd = W_ @ B("Head").head; hip = W_ @ B("Hips").head
    hand_y = (W_.to_3x3() @ B("RightHand").matrix.to_3x3().col[1]).normalized()
    hand_z = (W_.to_3x3() @ B("RightHand").matrix.to_3x3().col[2]).normalized()
    lsh = W_ @ B("LeftArm").head; lel = W_ @ B("LeftForeArm").head; lwr = W_ @ B("LeftHand").head
    lhy = (W_.to_3x3() @ B("LeftHand").matrix.to_3x3().col[1]).normalized()
    sp = (W_ @ B("Spine2").head) - (W_ @ B("Hips").head); shl = (W_ @ B("LeftArm").head) - (W_ @ B("RightArm").head)
    chest_yaw = math.degrees(math.atan2(-shl.dot(fwd), -shl.dot(right))) if shl.length > 0 else 0
    rows.append(dict(f=f, up=C((el - sh).normalized()), fo=C((wr - el).normalized()), hy=C(hand_y),
                     lup=C((lel - lsh).normalized()), lfo=C((lwr - lel).normalized()), lhy=C(lhy),
                     lreach=round((lwr - lsh).length, 3), lwr_fwd=round((lwr - hip).dot(fwd), 3), chest_yaw=round(chest_yaw, 1),
                     wrist_rel_head=C(wr - hd), wrist_h=round(wr.z - hip.z, 3)))
with open(os.path.join(OUT, "probe.txt"), "w") as fh:
    fh.write(f"frames {f0}-{f1}\n")
    for r in rows: fh.write(str(r) + "\n")
print("frames", f0, f1)
# 3/4 front-right render sheet (the right arm side) every 4 frames
cam = bpy.data.objects.new("C", bpy.data.cameras.new("C")); sc.collection.objects.link(cam); sc.camera = cam
cam.data.type = "ORTHO"; cam.data.ortho_scale = 2.6
c0 = W_ @ B("Hips").head
cam.location = c0 + fwd * 3.5 + right * 3.0 + Vector((0, 0, 1.0))
cam.rotation_euler = ((c0 + Vector((0, 0, 0.4))) - cam.location).to_track_quat("-Z", "Y").to_euler()
sc.render.engine = "BLENDER_WORKBENCH"; sc.render.resolution_x = sc.render.resolution_y = 300
for f in range(f0, f1 + 1, 3):
    sc.frame_set(f); sc.render.filepath = os.path.join(OUT, f"f_{f:03d}.png"); bpy.ops.render.render(write_still=True)
