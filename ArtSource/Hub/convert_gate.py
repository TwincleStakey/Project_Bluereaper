# 거점 gates (11-20): KCISA 경선문 glb (already downloaded for 대전 앞, CC BY) -> HB_Gate (frame + roof, no door leaves)
# and HB_DoorL / HB_DoorR (the gate's own door panel + its metal fittings, split at the centre line, pivot on the hinge
# edge) so the sealed portal gate can swing its doors open. Same texture export as convert_hub.py.
# blender -b --factory-startup -P convert_gate.py -- <gyeongseonmun.glb> <unity out dir>
import bpy, sys, os, bmesh, mathutils
args = sys.argv[sys.argv.index("--") + 1:]
GLB, DST = args

def save_img(img, path, size=1024):
    if img is None: return False
    im = img.copy(); im.name = os.path.basename(path)
    if max(im.size[0], im.size[1]) > size: im.scale(min(size, im.size[0]), min(size, im.size[1]))
    im.filepath_raw = path; im.file_format = "JPEG"; bpy.context.scene.render.image_settings.quality = 88; im.save()
    return True

def find_tex(mat, sockets):
    if not mat or not mat.use_nodes: return None
    for n in mat.node_tree.nodes:
        if n.type == "BSDF_PRINCIPLED":
            for s in sockets:
                inp = n.inputs.get(s)
                if inp and inp.is_linked:
                    node = inp.links[0].from_node
                    for _ in range(4):
                        if node.type == "TEX_IMAGE": return node.image
                        ins = [i for i in node.inputs if i.is_linked]
                        if not ins: break
                        node = ins[-1].links[0].from_node if node.type == "NORMAL_MAP" else ins[0].links[0].from_node
    return None

def export(obs, name, pivot):
    bpy.ops.object.select_all(action="DESELECT")
    for o in obs: o.select_set(True)
    bpy.context.view_layer.objects.active = obs[0]
    if len(obs) > 1: bpy.ops.object.join()
    ob = bpy.context.view_layer.objects.active
    bpy.context.scene.cursor.location = pivot
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
    # keep the hinge as the node position: instanced at the gate transform the leaf sits in its frame, pivoted on the hinge
    out = os.path.join(DST, name); os.makedirs(out, exist_ok=True)
    info = []
    for i, s in enumerate(ob.material_slots):
        mt = s.material
        a = save_img(find_tex(mt, ["Base Color"]), os.path.join(out, f"{name}_{i}_albedo.jpg"))
        n = save_img(find_tex(mt, ["Normal"]), os.path.join(out, f"{name}_{i}_normal.jpg"))
        info.append(f"{i}:{mt.name if mt else None} a={a} n={n}")
        s.material = bpy.data.materials.new(f"{name}_{i}")
    ob.name = name
    bpy.ops.object.select_all(action="DESELECT"); ob.select_set(True)
    if pivot != (0, 0, 0):
        # an empty root at the gate origin, the leaf a child at the hinge: Unity keeps the hinge as the child's position
        ob.name = name + "_Leaf"
        e = bpy.data.objects.new(name, None); bpy.context.scene.collection.objects.link(e)
        ob.parent = e
        e.select_set(True)
    bpy.ops.export_scene.fbx(filepath=os.path.join(out, name + ".fbx"), use_selection=True, apply_unit_scale=True,
                             axis_forward="-Z", axis_up="Y", bake_space_transform=True, mesh_smooth_type="FACE",
                             path_mode="STRIP", add_leaf_bones=False)
    d = ob.dimensions
    print(f"OK {name}: {d.x:.2f} x {d.y:.2f} x {d.z:.2f}, tris {sum(len(p.vertices) - 2 for p in ob.data.polygons)} | " + " | ".join(info))

def split_x(ob, keep_neg):
    # keep one side of x = 0 (the door panel's centre seam)
    bm = bmesh.new(); bm.from_mesh(ob.data)
    geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
    bmesh.ops.bisect_plane(bm, geom=geom, plane_co=(0, 0, 0), plane_no=(1, 0, 0), clear_outer=keep_neg, clear_inner=not keep_neg)
    bm.to_mesh(ob.data); bm.free()

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=GLB)
obs = [o for o in bpy.context.scene.objects if o.type == "MESH"]
for o in obs:
    bpy.ops.object.select_all(action="DESELECT"); o.select_set(True); bpy.context.view_layer.objects.active = o
    bpy.ops.object.parent_clear(type="CLEAR_KEEP_TRANSFORM")
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
door = [o for o in obs if "KoreanWood_5" in o.name or "Metal" in o.name]
frame = [o for o in obs if o not in door]
# metal fittings: 18k tris of rivets -> decimate
for o in door:
    if "Metal" in o.name:
        m = o.modifiers.new("dec", "DECIMATE"); m.ratio = 0.15
        bpy.context.view_layer.objects.active = o; bpy.ops.object.modifier_apply(modifier="dec")
# door leaves face -Y (glTF +Z forward -> Blender -Y); hinge edges at the panel's x extremes
lo = min(min((o.matrix_world @ mathutils.Vector(b)).x for b in o.bound_box) for o in door if "KoreanWood_5" in o.name)
hi = max(max((o.matrix_world @ mathutils.Vector(b)).x for b in o.bound_box) for o in door if "KoreanWood_5" in o.name)
yc = 0.045
right = []
for o in door:
    c = o.copy(); c.data = o.data.copy(); bpy.context.scene.collection.objects.link(c)
    split_x(o, keep_neg=True); split_x(c, keep_neg=False); right.append(c)
export(frame, "HB_Gate", (0, 0, 0))
export(door, "HB_DoorL", (lo, yc, 0))
export(right, "HB_DoorR", (hi, yc, 0))
print(f"HINGES x {lo:.3f} / {hi:.3f}, y {yc}")
