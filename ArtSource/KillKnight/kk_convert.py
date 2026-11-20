# 심연 고원 v3: downloaded Sketchfab models -> decimated FBX + 1K albedo/normal jpg
# blender -b --factory-startup -P kk_convert.py -- <download dir> <unity out dir>
import bpy, sys, os
args = sys.argv[sys.argv.index("--") + 1:]
SRC, DST = args[0], args[1]
MODELS = {  # out: (file, tri budget, albedo override, normal override)
    "SKF_DragonSkull": ("dragon_skull_2.glb", 20000, None, None),
    "SKF_BoneDemon": ("faceless_boney_horror_demon.glb", 40000, None, None),
    "SKF_GatesOfHell": ("goh/source/Gates.fbx.fbx", 40000, "goh/textures/Gates-of-Hell.jpeg", None),
    "SKF_AqueductRuin": ("aq/source/tripo_convert_5f703562-179e-46cc-bd64-f0f73a9ff39a.fbx", 40000, "aq/textures/Zyklus15_Zeile7_basecolor.jpeg", "aq/textures/Zyklus15_Zeile7_normal.jpeg"),
}
def save_img(img, path, size=1024):
    if img is None: return False
    im = img.copy(); im.name = os.path.basename(path)
    if max(im.size[0], im.size[1]) > size: im.scale(min(size, im.size[0]), min(size, im.size[1]))
    im.filepath_raw = path; im.file_format = "JPEG"; im.save()
    return True
def find_tex(mat, sock):
    if not mat or not mat.use_nodes: return None
    for n in mat.node_tree.nodes:
        if n.type == "BSDF_PRINCIPLED":
            inp = n.inputs.get(sock)
            if inp and inp.is_linked:
                node = inp.links[0].from_node
                for _ in range(4):
                    if node.type == "TEX_IMAGE": return node.image
                    ins = [i for i in node.inputs if i.is_linked]
                    if not ins: break
                    node = ins[-1].links[0].from_node if node.type == "NORMAL_MAP" else ins[0].links[0].from_node
    return None
os.makedirs(DST, exist_ok=True)
for name, (src, budget, aov, nov) in MODELS.items():
    p = os.path.join(SRC, src)
    if not os.path.exists(p): print("SKIP", name, p); continue
    bpy.ops.wm.read_factory_settings(use_empty=True)
    if p.lower().endswith(".fbx"): bpy.ops.import_scene.fbx(filepath=p)
    else: bpy.ops.import_scene.gltf(filepath=p)
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    bpy.ops.object.select_all(action="DESELECT")
    for o in meshes: o.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    mat0 = meshes[0].material_slots[0].material if meshes[0].material_slots else None
    alb = find_tex(mat0, "Base Color"); nrm = find_tex(mat0, "Normal")
    if len(meshes) > 1: bpy.ops.object.join()
    ob = bpy.context.view_layer.objects.active
    bpy.ops.object.parent_clear(type="CLEAR_KEEP_TRANSFORM")
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    t0 = sum(len(f.vertices) - 2 for f in ob.data.polygons)
    for it in range(4):
        tn = sum(len(f.vertices) - 2 for f in ob.data.polygons)
        if tn <= budget * 1.05: break
        m = ob.modifiers.new("dec", "DECIMATE"); m.ratio = budget / tn; m.use_collapse_triangulate = True
        bpy.ops.object.modifier_apply(modifier="dec")
    t1 = sum(len(f.vertices) - 2 for f in ob.data.polygons)
    out = os.path.join(DST, name); os.makedirs(out, exist_ok=True)
    if aov: alb = bpy.data.images.load(os.path.join(SRC, aov))
    if nov: nrm = bpy.data.images.load(os.path.join(SRC, nov))
    a_ok = save_img(alb, os.path.join(out, f"{name}_albedo.jpg"))
    n_ok = save_img(nrm, os.path.join(out, f"{name}_normal.jpg"))
    for s in ob.material_slots: s.material = None
    while len(ob.material_slots) > 1: bpy.ops.object.material_slot_remove()
    nm = bpy.data.materials.new(name)
    if ob.material_slots: ob.material_slots[0].material = nm
    else: ob.data.materials.append(nm)
    ob.name = name; dims = ob.dimensions
    bpy.ops.export_scene.fbx(filepath=os.path.join(out, name + ".fbx"), use_selection=True, apply_unit_scale=True,
                             axis_forward="-Z", axis_up="Y", bake_space_transform=True, mesh_smooth_type="FACE", path_mode="STRIP", add_leaf_bones=False)
    print(f"OK {name}: tris {t0} -> {t1}, size {dims.x:.2f} x {dims.y:.2f} x {dims.z:.2f}, albedo {a_ok}, normal {n_ok}")
