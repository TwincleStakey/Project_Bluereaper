# Converts the downloaded Poly Haven glTF models (CC0) to FBX for Unity and copies their diffuse/normal maps.
# blender -b -P convert_ph.py -- <downloads dir> <unity AbandonedVillage dir>
import bpy, sys, os, shutil, json

args = sys.argv[sys.argv.index("--") + 1:]
SRC, DST = args[0], args[1]
MODELS = ["dead_tree_trunk", "dead_tree_trunk_02", "dry_branches_medium_01", "tree_stump_01", "tree_stump_02",
          "wooden_barrels_01", "Barrel_01", "barrel_03", "wooden_bucket_01", "wooden_bucket_02", "wicker_basket_01",
          "wooden_ladder", "wooden_crate_01", "old_military_crate", "ceramic_pot", "planter_pot_clay",
          "rock_moss_set_01", "rock_moss_set_02", "stone_01", "namaqualand_stones_01", "rock_07", "rock_09",
          "grass_medium_01", "grass_medium_02", "shrub_04", "fern_02", "wooden_lantern_01", "Lantern_01",
          "stone_fire_pit", "wooden_stool_01", "wooden_broom"]
report = {}
for name in MODELS:
    d = os.path.join(SRC, "models", name)
    gl = [f for f in os.listdir(d) if f.endswith(".gltf")] if os.path.isdir(d) else []
    if not gl:
        print("SKIP", name); continue
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=os.path.join(d, gl[0]))
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    tris = sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in meshes)
    mats = sorted({s.material.name for o in meshes for s in o.material_slots if s.material})
    xs, ys, zs = [], [], []
    for o in meshes:
        for c in o.bound_box:
            w = o.matrix_world @ __import__("mathutils").Vector(c); xs.append(w.x); ys.append(w.y); zs.append(w.z)
    out = os.path.join(DST, "ThirdParty", "PolyHaven", name)
    os.makedirs(out, exist_ok=True)
    bpy.ops.export_scene.fbx(filepath=os.path.join(out, name + ".fbx"), use_selection=False, object_types={"MESH", "EMPTY"},
                             apply_scale_options="FBX_SCALE_ALL", bake_space_transform=True, axis_forward="-Z", axis_up="Y",
                             path_mode="STRIP", embed_textures=False, mesh_smooth_type="FACE", add_leaf_bones=False)
    tex = os.path.join(d, "textures")
    for f in os.listdir(tex) if os.path.isdir(tex) else []:
        if "_diff_" in f or "_nor_gl_" in f or "_alpha_" in f:
            shutil.copy(os.path.join(tex, f), os.path.join(out, f))
    report[name] = {"tris": tris, "mats": mats, "size": [round(max(xs) - min(xs), 2), round(max(ys) - min(ys), 2), round(max(zs) - min(zs), 2)],
                    "tex": sorted(os.listdir(tex)) if os.path.isdir(tex) else []}
    print("OK", name, report[name])
with open(os.path.join(SRC, "..", "ph_report.json"), "w") as f:
    json.dump(report, f, indent=1)
