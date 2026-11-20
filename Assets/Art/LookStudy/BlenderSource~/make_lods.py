# Lighter copies of the heavy LookStudy foliage models for distance drawing (Kido 11-10, "200 fps on a 4060").
# Same shape, UVs and pivot; only the triangle count drops (Blender Decimate, collapse). The flowers are 4,000 triangles
# each but only ~25 px wide from the RoomRun camera. LookStudyInstancer swaps to these (Resources/LookStudyLOD) unless
# the camera is close, and only if the bounds match the original.
# blender -b -P make_lods.py -- <Models/Real dir> <Resources/LookStudyLOD dir>
import bpy, sys, os
src, out = sys.argv[sys.argv.index("--") + 1:][:2]
targets = {"Flower_Yellow": 0.1, "Flower_White": 0.1, "Flower_Rose": 0.1, "Plant_Ivy": 0.3, "Plant_Vines": 0.35, "Bush_Leafy": 0.5}
for name, ratio in targets.items():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=os.path.join(src, name + ".fbx"))
    meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    before = after = 0
    for o in meshes:
        before += sum(len(p.vertices) - 2 for p in o.data.polygons)
        m = o.modifiers.new("lod", 'DECIMATE'); m.decimate_type = 'COLLAPSE'; m.ratio = ratio; m.use_collapse_triangulate = True
        bpy.context.view_layer.objects.active = o
        bpy.ops.object.modifier_apply(modifier="lod")
        after += len(o.data.polygons)
        o.name = o.name + "_LOD"; o.data.name = o.data.name + "_LOD"
    for o in bpy.context.scene.objects: o.select_set(o.type == 'MESH')
    path = os.path.join(out, name + "_LOD.fbx")
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"MESH"}, add_leaf_bones=False, path_mode="STRIP")
    print("LOD", name, before, "->", after, [o.name for o in meshes])
