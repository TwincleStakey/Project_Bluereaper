# Usage: blender -b [file.blend] -P inspect.py -- [import_file] ; prints armature bone tree + actions
import bpy, sys, os
a = sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
if a:
    p = a[0]; ext = os.path.splitext(p)[1].lower()
    for o in list(bpy.data.objects): bpy.data.objects.remove(o)
    if ext in ('.glb','.gltf'): bpy.ops.import_scene.gltf(filepath=p)
    else: bpy.ops.import_scene.fbx(filepath=p, automatic_bone_orientation=False)
for arm in [o for o in bpy.data.objects if o.type=='ARMATURE']:
    print('ARM', arm.name, 'scale', tuple(round(v,3) for v in arm.scale), 'rot', tuple(round(v,3) for v in arm.rotation_euler), 'nbones', len(arm.data.bones))
    def walk(b, d):
        h = arm.matrix_world @ b.head_local; t = arm.matrix_world @ b.tail_local
        print('  '*d + b.name, tuple(round(v,3) for v in h), '->', tuple(round(v,3) for v in t), 'def' if b.use_deform else 'nodef')
        for c in b.children: walk(c, d+1)
    for b in arm.data.bones:
        if b.parent is None: walk(b, 1)
for act in bpy.data.actions:
    print('ACT', act.name, tuple(act.frame_range), 'users', act.users)
print('FPS', bpy.context.scene.render.fps)
for o in bpy.data.objects:
    if o.type=='MESH': print('MESH', o.name, len(o.data.polygons), [m.name for m in o.data.materials][:4])
