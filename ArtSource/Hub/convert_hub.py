# 거점(HUB) 11-20: downloaded Sketchfab / Poly Haven models -> decimated FBX + per-slot base colour / normal (<= 1K)
# + a workbench preview PNG per model. Copy of PalaceCourt/convert_palace_court.py.
# blender -b --factory-startup -P convert_hub.py -- <download dir> <unity out dir> <preview dir> [names...]
import bpy, sys, os, math
args = sys.argv[sys.argv.index("--") + 1:]
SRC, DST, PREV = args[0], args[1], args[2]
ONLY = args[3:]
# out name: (source file relative to SRC, max triangles)
MODELS = {
    "HB_Bagua": ("sketchfab/bagua/scene.gltf", 60000),
    "HB_Dais": ("sketchfab/round_stone_stepsplatform.glb", 4000),
    "HB_Smithy": ("sketchfab/old_smithy.glb", 6000),
    "HB_Anvil": ("sketchfab/anvil_nature_morte.glb", 6000),
    "HB_ToolRack": ("sketchfab/tools.glb", 4000),
    "HB_WeaponRack": ("sketchfab/weapon_rack.glb", 2000),
    "HB_Gear": ("sketchfab/gear_low-poly_free.glb", 2000),
    "HB_Drum": ("sketchfab/kyobanggo.glb", 8000),
    "HB_Scroll": ("sketchfab/old__ancient_scroll.glb", 1500),
    "HB_Books": ("sketchfab/medieval_book_stack.glb", 3000),
    "HB_Chain": ("sketchfab/iron_chain.glb", 6000),
    "HB_Desk": ("polyhaven/chinese_console_table/chinese_console_table_1k.gltf", 6000),
    "HB_Chair": ("polyhaven/chinese_armchair/chinese_armchair_1k.gltf", 5000),
    "HB_Hammer": ("polyhaven/cross_pein_hammer/cross_pein_hammer_1k.gltf", 1500),
    "HB_Shimenawa": ("sketchfab/shimenawa.glb", 30000),   # 11-20 ik-models, Sketchfab Free Standard
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
    if socket_names == ["Base Color"]:
        for n in mat.node_tree.nodes:
            if n.type == "TEX_IMAGE" and n.image: return n.image
    return None

def preview(ob, path):
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"
    sc.display.shading.light = "STUDIO"
    sc.display.shading.color_type = "TEXTURE" if any(find_tex(s.material, ["Base Color"]) for s in ob.material_slots) else "VERTEX"
    sc.render.resolution_x = sc.render.resolution_y = 512
    sc.render.film_transparent = False
    d = ob.dimensions; r = max(d) * 1.6 + 0.01
    c = sum((ob.matrix_world @ __import__("mathutils").Vector(b) for b in ob.bound_box), __import__("mathutils").Vector()) / 8
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); sc.collection.objects.link(cam); sc.camera = cam
    # same feel as the game camera: 49.5 deg down, from the south (-Y in Blender)
    p = math.radians(49.5)
    cam.location = (c.x, c.y - r * math.cos(p), c.z + r * math.sin(p))
    cam.rotation_euler = (math.radians(90) - p, 0, 0)
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)

os.makedirs(DST, exist_ok=True); os.makedirs(PREV, exist_ok=True)
for name, (src, budget) in MODELS.items():
    if ONLY and name not in ONLY: continue
    p = os.path.join(SRC, src)
    if not os.path.exists(p): print("SKIP", name, p); continue
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=p)
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    bpy.ops.object.select_all(action="DESELECT")
    for o in meshes: o.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1: bpy.ops.object.join()
    ob = bpy.context.view_layer.objects.active
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
    preview(ob, os.path.join(PREV, name + ".png"))
    out = os.path.join(DST, name); os.makedirs(out, exist_ok=True)
    mats = [s.material for s in ob.material_slots]
    info = []
    for i, mt in enumerate(mats):
        alb, nrm = find_tex(mt, ["Base Color"]), find_tex(mt, ["Normal"])
        a_ok = save_img(alb, os.path.join(out, f"{name}_{i}_albedo.jpg"))
        n_ok = save_img(nrm, os.path.join(out, f"{name}_{i}_normal.jpg"))
        col = list(mt.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value)[:3] if mt and mt.use_nodes and "Principled BSDF" in mt.node_tree.nodes else [1, 1, 1]
        info.append(f"{i}:{mt.name if mt else None} alb={a_ok} nrm={n_ok} col={[round(c, 2) for c in col]}")
        nm = bpy.data.materials.new(f"{name}_{i}")
        ob.material_slots[i].material = nm
    vcol = len(ob.data.color_attributes) > 0
    ob.name = name
    d = ob.dimensions
    bpy.ops.export_scene.fbx(filepath=os.path.join(out, name + ".fbx"), use_selection=True, apply_unit_scale=True,
                             axis_forward="-Z", axis_up="Y", bake_space_transform=True, mesh_smooth_type="FACE",
                             path_mode="STRIP", add_leaf_bones=False, colors_type="SRGB")
    print(f"OK {name}: tris {t0} -> {t1}, size {d.x:.2f} x {d.y:.2f} x {d.z:.2f} m (x,y=depth,z=up), vcol {vcol}, mats {len(mats)} | " + " | ".join(info))
