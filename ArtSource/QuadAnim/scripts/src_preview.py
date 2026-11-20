# Render the SOURCE clip (for comparing with the retarget): blender -b -P src_preview.py -- <file> <action> <outdir> <f0> <f1> <step>
import bpy, sys, os
from mathutils import Vector
a = sys.argv[sys.argv.index('--')+1:]; p, act, od = a[0], a[1], a[2]; f0, f1, st = int(a[3]), int(a[4]), int(a[5])
for o in list(bpy.data.objects): bpy.data.objects.remove(o)
if p.endswith('.glb'): bpy.ops.import_scene.gltf(filepath=p)
else: bpy.ops.import_scene.fbx(filepath=p)
sc = bpy.context.scene
for o in sc.objects:
    if o.type == 'ARMATURE' and o.animation_data is not None or (o.type=='ARMATURE' and act.split('|')[0] in o.name):
        A = bpy.data.actions.get(act)
        if A and (o.name == act.split('|')[0] or len(act.split('|')) < 2 or act.split('|')[0]==o.name):
            o.animation_data_create(); o.animation_data.action = A
            try: o.animation_data.action_slot = A.slots[0]
            except Exception: pass
sc.render.engine = 'BLENDER_WORKBENCH'; sc.render.resolution_x, sc.render.resolution_y = 480, 300
sc.world = sc.world or bpy.data.worlds.new('W')
pts = [o.matrix_world @ Vector(c) for o in sc.objects if o.type == 'MESH' for c in o.bound_box]
mn = Vector((min(v.x for v in pts), min(v.y for v in pts), min(v.z for v in pts))); mx = Vector((max(v.x for v in pts), max(v.y for v in pts), max(v.z for v in pts)))
c = (mn + mx) / 2; s = (mx - mn).length
cd = bpy.data.cameras.new('C'); cam = bpy.data.objects.new('C', cd); sc.collection.objects.link(cam); sc.camera = cam
for vn, d in (('side', Vector((1.0, 0, 0.25))), ('q34', Vector((0.7, -0.75, 0.5)))):
    cam.location = c + d.normalized() * s * 1.6; cam.rotation_euler = (c - cam.location).to_track_quat('-Z', 'Y').to_euler()
    for f in range(f0, f1 + 1, st):
        sc.frame_set(f); sc.render.filepath = os.path.join(od, '%s_%04d.png' % (vn, f)); bpy.ops.render.render(write_still=True)
