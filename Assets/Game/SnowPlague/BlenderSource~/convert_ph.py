# Converts downloaded Poly Haven glTF models (CC0) to FBX for the snow plague field: decimated to a game budget,
# frost/AO baked into vertex colours like the rest of the kit (splib.py), diffuse + normal maps copied alongside.
# blender -b --factory-startup -P convert_ph.py -- <unity SnowPlague dir> <download dir> [<download dir> ...]
import bpy, sys, os, shutil, json, math
from mathutils import Vector, noise
sys.path.append(os.path.dirname(os.path.abspath(__file__)))
from splib import smooth01

args = sys.argv[sys.argv.index("--") + 1:]
DST, SRCS = args[0], args[1:]
# name: max triangles
MODELS = {"boulder_01": 2500, "rock_07": 1500, "rock_09": 1500, "stone_01": 800, "namaqualand_rocks_01": 3000,
          "namaqualand_stones_01": 2500, "root_cluster_01": 3000, "root_cluster_02": 3000, "dead_quiver_branch_01": 2500,
          "dry_branches_medium_01": 3000, "bark_debris_01": 2000, "dead_tree_trunk": 3000, "tree_stump_01": 2500,
          "tree_stump_02": 2500}
report = {}
for name, budget in MODELS.items():
    d = next((os.path.join(s, "models", name) for s in SRCS if os.path.isdir(os.path.join(s, "models", name))), None)
    gl = [f for f in os.listdir(d) if f.endswith(".gltf")] if d else []
    if not gl: print("SKIP", name); continue
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=os.path.join(d, gl[0]))
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    bpy.ops.object.select_all(action='DESELECT')
    for o in meshes: o.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1: bpy.ops.object.join()
    ob = bpy.context.view_layer.objects.active
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    t0 = sum(len(p.vertices) - 2 for p in ob.data.polygons)
    for it in range(4):   # collapse can stop short on scans with seams; repeat until under budget
        tn = sum(len(p.vertices) - 2 for p in ob.data.polygons)
        if tn <= budget * 1.05: break
        m = ob.modifiers.new("dec", "DECIMATE"); m.ratio = budget / tn; m.use_collapse_triangulate = True
        if it > 0: m.use_symmetry = False
        bpy.ops.object.modifier_apply(modifier="dec")
        if it == 1:   # merge seam duplicates so collapse can proceed
            bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
            bpy.ops.mesh.remove_doubles(threshold=0.0005); bpy.ops.object.mode_set(mode='OBJECT')
    me = ob.data
    for a in [a.name for a in me.color_attributes]: me.color_attributes.remove(me.color_attributes[a])
    ca = me.color_attributes.new("Col", 'BYTE_COLOR', 'CORNER')
    zmin = min(v.co.z for v in me.vertices); zmax = max(v.co.z for v in me.vertices)
    off = Vector((hash(name) % 50, 3, 0))
    for poly in me.polygons:
        for li, vi in zip(poly.loop_indices, poly.vertices):
            v = me.vertices[vi]; p = v.co; nz = v.normal.z
            top = smooth01(0.2, 0.85, nz + noise.noise(p * 7.0 + off) * 0.2)
            patch = smooth01(0.3, 0.7, 0.5 + noise.noise(p * 1.4 + off) * 0.55 + noise.noise(p * 5.0 + off) * 0.25)
            ao = 0.45 + 0.55 * smooth01(0.0, max(0.3, (zmax - zmin) * 0.5), p.z - zmin)
            ca.data[li].color = (top * (0.18 + 0.82 * patch), ao, 0.5, 0.0)
    ob.name = "PH_" + name; me.name = "PH_" + name
    for s in me.materials: pass
    out = os.path.join(DST, "ThirdParty", "PolyHaven", name)
    os.makedirs(out, exist_ok=True)
    bpy.ops.export_scene.fbx(filepath=os.path.join(out, name + ".fbx"), use_selection=False, object_types={"MESH"},
                             apply_scale_options="FBX_SCALE_ALL", bake_space_transform=True, axis_forward="-Z", axis_up="Y",
                             path_mode="STRIP", embed_textures=False, mesh_smooth_type="FACE", colors_type="LINEAR", add_leaf_bones=False)
    tex = os.path.join(d, "textures")
    for f in os.listdir(tex) if os.path.isdir(tex) else []:
        if "_diff_" in f or "_nor_gl_" in f or "_alpha_" in f:
            shutil.copy(os.path.join(tex, f), os.path.join(out, f))
    report[name] = {"tris_src": t0, "tris": sum(len(p.vertices) - 2 for p in me.polygons), "mats": [m.name for m in me.materials if m],
                    "size": [round(x, 2) for x in ob.dimensions]}
    print("OK", name, report[name])
json.dump(report, open(os.path.join(SRCS[0], "..", "ph_report.json"), "w"), indent=1)
