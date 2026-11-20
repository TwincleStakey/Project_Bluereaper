# Print each mesh part of an FBX with its world bounds (Blender axes: x, y = depth, z = up), tris and materials.
import bpy, sys, mathutils
f = sys.argv[sys.argv.index("--") + 1]
bpy.ops.wm.read_factory_settings(use_empty=True); bpy.ops.import_scene.fbx(filepath=f)
for o in bpy.context.scene.objects:
    if o.type != "MESH": continue
    ws = [o.matrix_world @ mathutils.Vector(b) for b in o.bound_box]
    lo = [round(min(w[i] for w in ws), 2) for i in range(3)]; hi = [round(max(w[i] for w in ws), 2) for i in range(3)]
    print("PART", o.name, lo, hi, sum(len(p.vertices) - 2 for p in o.data.polygons), [s.material.name for s in o.material_slots if s.material])
