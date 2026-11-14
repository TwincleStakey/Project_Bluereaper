# 저승 피안화: CC0 Sketchfab scan (ffish.asia / floraZia, see Assets/Underworld/CREDITS.txt) -> game-ready mesh.
# Run headless:  blender -b --factory-startup -P build_spider_lily.py -- <src.glb> <out.fbx> <out_albedo.png> [tris]
# Drops the Sketchfab pedestal cube, joins the 4 split chunks, welds, puts the stem base at the origin, scales the plant
# to 0.7 m tall (knee height, head ~0.65 m across like the old procedural lily), decimates (collapse) to ~4000 tris.
import bpy, sys, mathutils
argv = sys.argv[sys.argv.index("--") + 1:]
src, out_fbx, out_png = argv[0], argv[1], argv[2]
target = int(argv[3]) if len(argv) > 3 else 4000
H = 0.7
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
for o in list(bpy.data.objects):
    if o.type == 'MESH' and o.data.materials and o.data.materials[0].name.startswith('Material.001'):
        bpy.data.objects.remove(o)
ms = [o for o in bpy.data.objects if o.type == 'MESH']
bpy.ops.object.select_all(action='DESELECT')
for o in ms: o.select_set(True)
bpy.context.view_layer.objects.active = ms[0]
bpy.ops.object.parent_clear(type='CLEAR_KEEP_TRANSFORM')
bpy.ops.object.join()
o = bpy.context.view_layer.objects.active
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
for e in [x for x in bpy.data.objects if x.type == 'EMPTY']: bpy.data.objects.remove(e)
o.name = 'UW_SpiderLily'; o.data.name = 'UW_SpiderLily'
me = o.data
bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT'); bpy.ops.mesh.remove_doubles(threshold=0.0001); bpy.ops.object.mode_set(mode='OBJECT')
zs = [v.co.z for v in me.vertices]; zmin, zmax = min(zs), max(zs)
s = H / (zmax - zmin)
low = [v.co.copy() for v in me.vertices if v.co.z < zmin + 0.02 * (zmax - zmin)]
cx = sum(c.x for c in low) / len(low); cy = sum(c.y for c in low) / len(low)
for v in me.vertices: v.co = mathutils.Vector(((v.co.x - cx) * s, (v.co.y - cy) * s, (v.co.z - zmin) * s))
tris = sum(len(p.vertices) - 2 for p in me.polygons)
m = o.modifiers.new('d', 'DECIMATE'); m.ratio = min(1.0, target / tris); m.use_collapse_triangulate = True
bpy.ops.object.modifier_apply(modifier='d')
for p in me.polygons: p.use_smooth = True
mat = me.materials[0]; mat.name = 'UW_SpiderLily'
img = next(n.image for n in mat.node_tree.nodes if n.type == 'TEX_IMAGE')
img.filepath_raw = out_png; img.file_format = 'PNG'; img.save()
bpy.ops.object.select_all(action='DESELECT'); o.select_set(True)
bpy.ops.export_scene.fbx(filepath=out_fbx, use_selection=True, apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
                         bake_space_transform=True, object_types={"MESH"}, mesh_smooth_type="FACE", add_leaf_bones=False, path_mode='STRIP')
print("RESULT tris", sum(len(p.vertices) - 2 for p in me.polygons), "verts", len(me.vertices), "dims", tuple(round(x, 3) for x in o.dimensions))
