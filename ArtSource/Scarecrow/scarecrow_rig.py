# Scarecrow training dummy: import Tripo GLB, scale to 1.8 m, auto-rig (spine chain + arms + hanging talismans), export FBX + texture.
# blender -b --python scarecrow_rig.py -- <in.glb> <out.fbx> <out_tex_dir>
import bpy, sys, os, math
from mathutils import Vector
a = sys.argv[sys.argv.index("--")+1:]
src, out_fbx, tex_dir = [os.path.abspath(p) for p in a[:3]]
H = 1.8
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
mesh = [o for o in bpy.data.objects if o.type == 'MESH'][0]
mesh.parent = None if False else mesh.parent
# bake world transform into the mesh, drop the root empty
mw = mesh.matrix_world.copy()
mesh.data.transform(mw); mesh.parent = None; mesh.matrix_world.identity()
for o in list(bpy.data.objects):
    if o.type != 'MESH': bpy.data.objects.remove(o)
me = mesh.data
zs = [v.co.z for v in me.vertices]; z0, z1 = min(zs), max(zs)
s = H / (z1 - z0)
for v in me.vertices: v.co = Vector((v.co.x * s, v.co.y * s, (v.co.z - z0) * s))
# which side is the mask on? head verts protrude toward the face
head = [v.co for v in me.vertices if v.co.z > 0.86 * H]
ymin, ymax = min(c.y for c in head), max(c.y for c in head)
body = [v.co.y for v in me.vertices if 0.4 * H < v.co.z < 0.6 * H]
print("HEAD y", round(ymin,3), round(ymax,3), "BODY y", round(min(body),3), round(max(body),3))
if False:   # Tripo export already has the face at +Y (checked in prev_side_tex); Unity import turns it to face +Z
    for v in me.vertices: v.co = Vector((-v.co.x, -v.co.y, v.co.z))
    print("rotated 180 so the face is -Y")
me.update()
xs = [abs(v.co.x) for v in me.vertices]; span = max(xs)
print("half span", round(span,3))
# ---- armature
arm = bpy.data.armatures.new("ScarecrowRig"); rig = bpy.data.objects.new("Scarecrow", arm)
bpy.context.scene.collection.objects.link(rig)
bpy.context.view_layer.objects.active = rig
bpy.ops.object.mode_set(mode='EDIT')
def bone(n, h, t, parent=None):
    b = arm.edit_bones.new(n); b.head = Vector(h); b.tail = Vector(t)
    if parent: b.parent = arm.edit_bones[parent]
    return b
J1, J2, J3 = 0.42 * H, 0.66 * H, 0.82 * H
AZ = 0.775 * H
bone("Root", (0, 0, 0), (0, 0, 0.05 * H))
bone("Legs", (0, 0, 0.05 * H), (0, 0, J1), "Root")
bone("Spine", (0, 0, J1), (0, 0, J2), "Legs")
bone("Chest", (0, 0, J2), (0, 0, J3), "Spine")
bone("Head", (0, 0, J3), (0, 0, H), "Chest")
for sd, sx in (("L", 1), ("R", -1)):
    bone("Arm" + sd, (sx * 0.09 * H, 0, AZ), (sx * span, 0, AZ), "Chest")
    bone("Tal" + sd, (sx * 0.25 * H, 0, 0.70 * H), (sx * 0.25 * H, 0, 0.44 * H), "Arm" + sd)
bpy.ops.object.mode_set(mode='OBJECT')
# ---- paper mask from the base colour (bright yellow)
import numpy as np
img = [i for i in bpy.data.images if i.size[0] > 0][0]
W, Hh = img.size; px = np.empty(W * Hh * 4, np.float32); img.pixels.foreach_get(px); px = px.reshape(Hh, W, 4)
uvl = me.uv_layers.active.data
vuv = [None] * len(me.vertices)
for poly in me.polygons:
    for li in poly.loop_indices:
        vi = me.loops[li].vertex_index
        if vuv[vi] is None: vuv[vi] = uvl[li].uv.copy()
paper = []
for vi, uv in enumerate(vuv):
    u = min(W - 1, max(0, int(uv.x * W))); t = min(Hh - 1, max(0, int(uv.y * Hh)))
    r, g, b = px[t, u, :3]
    paper.append(bool(r > 0.62 and g > 0.48 and b < 0.55 * g and r + g > 1.25))
print("paper verts", sum(paper))
# ---- weights
def ss(e0, e1, x):
    t = min(1, max(0, (x - e0) / (e1 - e0))); return t * t * (3 - 2 * t)
groups = {n: mesh.vertex_groups.new(name=n) for n in ("Root", "Legs", "Spine", "Chest", "Head", "ArmL", "ArmR", "TalL", "TalR")}
counts = {}
for v in me.vertices:
    x, y, z = v.co; ax = abs(x); sd = "L" if x > 0 else "R"
    w = {}
    # chain along height with soft joints
    bw = 0.05 * H
    b1, b2, b3 = ss(J1 - bw, J1 + bw, z), ss(J2 - bw, J2 + bw, z), ss(J3 - bw, J3 + bw, z)
    feet = 1 - ss(0.03 * H, 0.09 * H, z)
    chain = {"Root": feet, "Legs": (1 - feet) * (1 - b1), "Spine": b1 * (1 - b2), "Chest": b2 * (1 - b3), "Head": b3}
    tal = 0.0; armw = 0.0
    if 0.38 * H < z < 0.72 * H:   # paper strips hanging under the arms, outside the body silhouette
        tal = ss(0.205, 0.225, ax) * (1 - ss(0.66 * H, 0.71 * H, z))
    if z > 0.64 * H:
        armw = ss(0.16, 0.26, ax) * (1 - tal)
    rest = 1 - tal - armw
    for k, c in chain.items():
        if c * rest > 1e-3: w[k] = c * rest
    if armw > 1e-3: w["Arm" + sd] = armw
    if tal > 1e-3: w["Tal" + sd] = tal
    for k, val in w.items():
        groups[k].add([v.index], val, 'REPLACE'); counts[k] = counts.get(k, 0) + (val > 0.5)
print("WEIGHTS", counts)
mesh.parent = rig
mod = mesh.modifiers.new("Armature", 'ARMATURE'); mod.object = rig
mesh.name = "ScarecrowMesh"
# ---- texture out
os.makedirs(tex_dir, exist_ok=True)
for img in bpy.data.images:
    if img.size[0] > 0:
        p = os.path.join(tex_dir, "Scarecrow_Base.jpg")
        img.filepath_raw = p; img.file_format = 'JPEG'; img.save(); print("TEX", p, img.size[:])
# ---- export
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.fbx(filepath=out_fbx, use_selection=True, apply_scale_options='FBX_SCALE_ALL', bake_space_transform=False,
    axis_forward='-Z', axis_up='Y', add_leaf_bones=False, bake_anim=False, path_mode='STRIP', mesh_smooth_type='FACE', armature_nodetype='NULL')
bpy.ops.wm.save_as_mainfile(filepath=os.path.splitext(src)[0] + "_rig.blend")
print("DONE", out_fbx)
# debug: colour verts by bone and render front/side workbench previews
if os.environ.get("SC_PREVIEW"):
    import bmesh
    cols = {"Root":(0.3,0.3,0.3),"Legs":(0.2,0.4,1),"Spine":(0.2,0.9,0.3),"Chest":(1,0.8,0.1),"Head":(1,1,1),"ArmL":(1,0.2,0.2),"ArmR":(0.8,0.2,1),"TalL":(0,1,1),"TalR":(0,1,1)}
    ca = me.color_attributes.new("dbg", 'FLOAT_COLOR', 'POINT')
    gi = {g.index: g.name for g in mesh.vertex_groups}
    for v in me.vertices:
        best = max(v.groups, key=lambda g: g.weight) if len(v.groups) else None
        c = cols[gi[best.group]] if best else (0,0,0)
        ca.data[v.index].color = (*c, 1)
    sc = bpy.context.scene; sc.render.engine = 'BLENDER_WORKBENCH'; sc.render.resolution_x = 640; sc.render.resolution_y = 900
    sh = sc.display.shading
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); sc.collection.objects.link(cam); sc.camera = cam
    cam.data.type = 'ORTHO'; cam.data.ortho_scale = 2.1
    outd = os.path.dirname(os.path.abspath(src))
    for name, loc, rot, mode in (("front_tex", (0,-5,0.9), (90,0,0), 'TEXTURE'), ("side_tex", (5,0,0.9), (90,0,90), 'TEXTURE'), ("front_w", (0,-5,0.9), (90,0,0), 'VERTEX'), ("side_w", (5,0,0.9), (90,0,90), 'VERTEX')):
        cam.location = loc; cam.rotation_euler = [math.radians(a) for a in rot]
        sh.light = 'FLAT' if mode == 'VERTEX' else 'STUDIO'; sh.color_type = mode
        sc.render.filepath = os.path.join(outd, "prev_" + name + ".png"); bpy.ops.render.render(write_still=True)

