# 대전 앞 court (11-19): downloaded Sketchfab KCISA / KHS / Arqueomodel3D glb -> decimated FBX + base colour / normal (<= 1K). Copy of PalaceProps/convert_palace_props.py.
# blender -b --factory-startup -P convert_palace_court.py -- <download dir> <unity out dir>
import bpy, sys, os
args = sys.argv[sys.argv.index("--") + 1:]
SRC, DST = args[0], args[1]
# out name: (source file, max triangles)
MODELS = {
    "PC_Hall": ("deugjungjeong.glb", 30000),
    "PC_Gate": ("sosul_samun_gate.glb", 9000),
    "PC_Stairs": ("changdeokgung_stairs.glb", 60000),
    "PC_Lion": ("stone_lion_001.glb", 5000),
    "PC_RankStone": ("rankstone_a.glb", 1200),
    "PC_Pillar": ("pillar_crop.glb", 4000),
    "PC_GateS": ("gyeongseonmun.glb", 9000),
    "PC_GateA": ("gaeomun.glb", 9000),
}
_OLD = {
    "PP_Dumu": ("gyeongbokgung_geunjeongjeon_dumu.glb", 4000),
    "PP_IncenseB": ("gyeongbokgung_geunjeongjeon_incenseburnerb.glb", 5000),
    "PP_IncenseC": ("gyeongbokgung_geunjeongjeon_incensebunnerc.glb", 6000),
    "PP_Haetae": ("gwanghwamun_gate_haetaea.glb", 9000),
    "PP_Brazier": ("brazer.glb", 2000),
    "PP_Flag": ("flag.glb", 2000),
    "PP_Hwacha": ("hwacha.glb", 5000),
    "PP_Spear": ("spear.glb", 600),
    "PP_Hwando": ("hwandoa.glb", 800),
    "PP_Woldo": ("woldo.glb", 800),
    "PP_Beam": ("beam/Old_Wooden_Beam_wggnehx_Mid.fbx", 1500),
}
def save_img(img, path, size=1024):
    if img is None: return False
    im = img.copy(); im.name = os.path.basename(path)
    if max(im.size[0], im.size[1]) > size: im.scale(min(size, im.size[0]), min(size, im.size[1]))
    im.filepath_raw = path; im.file_format = "JPEG"; bpy.context.scene.render.image_settings.quality = 88; im.save()
    return True
def find_tex(mat, socket_names):
    if not mat or not mat.use_nodes: return None
    for n in mat.node_tree.nodes:
        if n.type == "BSDF_PRINCIPLED":
            for s in socket_names:
                inp = n.inputs.get(s)
                if inp and inp.is_linked:
                    node = inp.links[0].from_node
                    for _ in range(4):
                        if node.type == "TEX_IMAGE": return node.image
                        ins = [i for i in node.inputs if i.is_linked]
                        if not ins: break
                        node = ins[-1].links[0].from_node if node.type == "NORMAL_MAP" else ins[0].links[0].from_node
    # unlit scans (Emission <- Image): any image in the material
    if socket_names == ["Base Color"]:
        for n in mat.node_tree.nodes:
            if n.type == "TEX_IMAGE" and n.image: return n.image
    return None
os.makedirs(DST, exist_ok=True)
ONLY = [a for a in args[2:]]
for name, (src, budget) in MODELS.items():
    if ONLY and name not in ONLY: continue
    p = os.path.join(SRC, src)
    if not os.path.exists(p): print("SKIP", name); continue
    bpy.ops.wm.read_factory_settings(use_empty=True)
    if p.endswith(".fbx"): bpy.ops.import_scene.fbx(filepath=p)
    else: bpy.ops.import_scene.gltf(filepath=p)
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    bpy.ops.object.select_all(action="DESELECT")
    for o in meshes: o.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1: bpy.ops.object.join()
    ob = bpy.context.view_layer.objects.active
    # drop parents (glTF root nodes) keeping the world transform, then bake it
    bpy.ops.object.parent_clear(type="CLEAR_KEEP_TRANSFORM")
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    t0 = sum(len(f.vertices) - 2 for f in ob.data.polygons)
    for it in range(4):
        tn = sum(len(f.vertices) - 2 for f in ob.data.polygons)
        if tn <= budget * 1.05: break
        m = ob.modifiers.new("dec", "DECIMATE"); m.ratio = budget / tn; m.use_collapse_triangulate = True; m.delimit = {"UV"}
        bpy.ops.object.modifier_apply(modifier="dec")
        if it == 1:
            bpy.ops.object.mode_set(mode="EDIT"); bpy.ops.mesh.select_all(action="SELECT")
            bpy.ops.mesh.remove_doubles(threshold=0.0005); bpy.ops.object.mode_set(mode="OBJECT")
    t1 = sum(len(f.vertices) - 2 for f in ob.data.polygons)
    # textures: one albedo / normal per material slot; slots renamed <name>_<i> (Unity assigns its own materials by slot)
    out = os.path.join(DST, name); os.makedirs(out, exist_ok=True)
    mats = [s.material for s in ob.material_slots]
    info = []
    for i, mt in enumerate(mats):
        alb, nrm = find_tex(mt, ["Base Color"]), find_tex(mt, ["Normal"])
        if name == "PP_Beam":
            d = os.path.join(SRC, "beam")
            alb = bpy.data.images.load(os.path.join(d, "Old_Wooden_Beam_wggnehx_Mid_2K_BaseColor.jpg"))
            nrm = bpy.data.images.load(os.path.join(d, "Old_Wooden_Beam_wggnehx_Mid_2K_Normal.jpg"))
        a_ok = save_img(alb, os.path.join(out, f"{name}_{i}_albedo.jpg"))
        n_ok = save_img(nrm, os.path.join(out, f"{name}_{i}_normal.jpg"))
        col = list(mt.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value)[:3] if mt and mt.use_nodes and "Principled BSDF" in mt.node_tree.nodes else [1,1,1]
        info.append(f"{i}:{mt.name if mt else None} alb={a_ok} nrm={n_ok} col={[round(c,2) for c in col]}")
        nm = bpy.data.materials.new(f"{name}_{i}")
        ob.material_slots[i].material = nm
    a_ok = n_ok = " | ".join(info)
    ob.name = name
    dims = ob.dimensions
    bpy.ops.export_scene.fbx(filepath=os.path.join(out, name + ".fbx"), use_selection=True, apply_unit_scale=True,
                             axis_forward="-Z", axis_up="Y", bake_space_transform=True, mesh_smooth_type="FACE", path_mode="STRIP", add_leaf_bones=False)
    print(f"OK {name}: tris {t0} -> {t1}, size {dims.x:.2f} x {dims.y:.2f} x {dims.z:.2f} m, mats {len(mats)}, albedo {a_ok}, normal {n_ok}")
