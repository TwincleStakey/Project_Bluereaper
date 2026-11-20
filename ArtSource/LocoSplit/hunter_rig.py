# Rebuilds the DDConcept Hunter (李⑺샇媛묒궗) on a Mixamo-named humanoid skeleton fitted to its chibi proportions.
# Gun and gun-holding arms are removed, new T-pose sleeves/hands are added, rigid (per-part) weights,
# the skirt is split off as its own mesh (simulated with Unity Cloth later).
# blender -b -P hunter_rig.py -- <Hunter.fbx> <out.fbx> <preview_dir>
import bpy, bmesh, sys, os, math
from mathutils import Vector

src, out, prev = sys.argv[-3], sys.argv[-2], sys.argv[-1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=src)
body = [o for o in bpy.context.scene.objects if o.type == 'MESH'][0]
bpy.context.view_layer.objects.active = body; body.select_set(True)
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
body.name = 'Hunter_Body'
mats = {m.name: i for i, m in enumerate(body.data.materials)}

SKIRT_TOP = float(os.environ.get('SKIRT_TOP', '0.60'))
ARM_Y = float(os.environ.get('ARM_Y', '-0.17'))

# ---------- 1. delete the gun, the floor ring and the forward arms ----------
bm = bmesh.new(); bm.from_mesh(body.data)
gun = {'DD_Wood', 'DD_Metal', 'DD_Ember', 'DD_White', 'DD_RingPlayer'}
keep_always = {'DD_Tiger', 'DD_Red', 'DD_Gourd', 'DD_Paper'}
dele = []
for f in bm.faces:
    mname = body.data.materials[f.material_index].name
    c = f.calc_center_median()
    if mname in gun: dele.append(f); continue
    if mname in keep_always: continue
    if mname == 'DD_Gold' and c.y < -0.25: dele.append(f); continue
    # arms: forward of the chest, between the skirt top and the shoulders
    if SKIRT_TOP + 0.02 < c.z < 0.99 and c.y < ARM_Y: dele.append(f); continue
    if 0.62 < c.z < 0.9 and c.x < -0.17 and c.y < -0.04 and mname in ('DD_Indigo', 'DD_Skin', 'DD_Navy'): dele.append(f); continue
    # hanging sleeves at the sides
    if 0.62 < c.z < 0.95 and abs(c.x) > float(os.environ.get('SIDE_X','0.24')) and mname in ('DD_Indigo', 'DD_Navy', 'DD_Skin'): dele.append(f); continue
bmesh.ops.delete(bm, geom=dele, context='FACES')
bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')
bm.to_mesh(body.data); bm.free()

# ---------- 2. split the skirt (cloth) ----------
bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='DESELECT'); bpy.ops.object.mode_set(mode='OBJECT')
for p in body.data.polygons:
    mname = body.data.materials[p.material_index].name
    p.select = p.center.z < SKIRT_TOP and mname in ('DD_Indigo', 'DD_Navy')
bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.separate(type='SELECTED'); bpy.ops.object.mode_set(mode='OBJECT')
skirt = [o for o in bpy.context.scene.objects if o.type == 'MESH' and o != body][0]
skirt.name = 'Hunter_Skirt'
# denser for the cloth solver
bpy.ops.object.select_all(action='DESELECT'); skirt.select_set(True); bpy.context.view_layer.objects.active = skirt
bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.mesh.subdivide(number_cuts=2)
bpy.ops.object.mode_set(mode='OBJECT')

# ---------- 3. new T-pose sleeves and hands ----------
def tube(name, a, b, r0, r1, mat, sides=6):
    bm = bmesh.new()
    axis = (b - a).normalized()
    up = Vector((0, 0, 1)) if abs(axis.z) < 0.9 else Vector((0, 1, 0))
    u = axis.cross(up).normalized(); v = axis.cross(u).normalized()
    rings = []
    for p, r in ((a, r0), (b, r1)):
        rings.append([bm.verts.new(p + (u * math.cos(t) + v * math.sin(t)) * r)
                      for t in [i / sides * 2 * math.pi for i in range(sides)]])
    for i in range(sides):
        bm.faces.new([rings[0][i], rings[0][(i + 1) % sides], rings[1][(i + 1) % sides], rings[1][i]])
    bm.faces.new(list(reversed(rings[0]))); bm.faces.new(rings[1])
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(o)
    o.data.materials.append(body.data.materials[mats[mat]])
    return o

SH_Z = 0.90
parts = []
for side in (1, -1):   # +x = character's left (it faces -y)
    s = 'Left' if side > 0 else 'Right'
    parts.append((tube('Sleeve' + s, Vector((0.17 * side, 0.02, SH_Z)), Vector((0.36 * side, 0.02, SH_Z)), 0.085, 0.075, 'DD_Indigo'), s + 'Arm'))
    parts.append((tube('Fore' + s, Vector((0.36 * side, 0.02, SH_Z)), Vector((0.50 * side, 0.02, SH_Z)), 0.075, 0.085, 'DD_Indigo'), s + 'ForeArm'))
    parts.append((tube('Cuff' + s, Vector((0.49 * side, 0.02, SH_Z)), Vector((0.52 * side, 0.02, SH_Z)), 0.088, 0.088, 'DD_Navy'), s + 'ForeArm'))
    parts.append((tube('Hand' + s, Vector((0.52 * side, 0.02, SH_Z)), Vector((0.62 * side, 0.02, SH_Z)), 0.05, 0.045, 'DD_Skin', sides=4), s + 'Hand'))

# ---------- 4. skeleton (Mixamo names, T-pose) ----------
arm_data = bpy.data.armatures.new('Armature')
rig = bpy.data.objects.new('Armature', arm_data); bpy.context.scene.collection.objects.link(rig)
bpy.context.view_layer.objects.active = rig
bpy.ops.object.mode_set(mode='EDIT')
B = {}
def bone(n, h, t, parent=None):
    b = arm_data.edit_bones.new('mixamorig:' + n); b.head = Vector(h); b.tail = Vector(t)
    if parent: b.parent = B[parent]; b.use_connect = False
    B[n] = b
bone('Hips', (0, 0, 0.48), (0, 0, 0.58))
bone('Spine', (0, 0, 0.58), (0, 0, 0.68), 'Hips')
bone('Spine1', (0, 0, 0.68), (0, 0, 0.78), 'Spine')
bone('Spine2', (0, 0, 0.78), (0, 0, 0.92), 'Spine1')
bone('Neck', (0, 0, 0.92), (0, 0, 0.99), 'Spine2')
bone('Head', (0, 0, 0.99), (0, 0, 1.45), 'Neck')
for side in (1, -1):
    s = 'Left' if side > 0 else 'Right'
    bone(s + 'Shoulder', (0.05 * side, 0, SH_Z), (0.17 * side, 0.02, SH_Z), 'Spine2')
    bone(s + 'Arm', (0.17 * side, 0.02, SH_Z), (0.36 * side, 0.02, SH_Z), s + 'Shoulder')
    bone(s + 'ForeArm', (0.36 * side, 0.02, SH_Z), (0.52 * side, 0.02, SH_Z), s + 'Arm')
    bone(s + 'Hand', (0.52 * side, 0.02, SH_Z), (0.62 * side, 0.02, SH_Z), s + 'ForeArm')
    bone(s + 'UpLeg', (0.09 * side, 0, 0.45), (0.09 * side, 0, 0.27), 'Hips')
    bone(s + 'Leg', (0.09 * side, 0, 0.27), (0.09 * side, 0, 0.07), s + 'UpLeg')
    bone(s + 'Foot', (0.09 * side, 0, 0.07), (0.09 * side, -0.09, 0.02), s + 'Leg')
    bone(s + 'ToeBase', (0.09 * side, -0.09, 0.02), (0.09 * side, -0.15, 0.02), s + 'Foot')
bpy.ops.object.mode_set(mode='OBJECT')

# ---------- 5. rigid weights ----------
def bind(o):
    o.parent = rig
    mod = o.modifiers.new('Armature', 'ARMATURE'); mod.object = rig
    return o
def group(o, n):
    return o.vertex_groups.get('mixamorig:' + n) or o.vertex_groups.new(name='mixamorig:' + n)

bind(body)
for v in body.data.vertices:
    p = v.co
    if p.z > 0.97: n = 'Head'
    elif p.z > 0.84: n = 'Spine2'
    elif p.z > 0.72: n = 'Spine1'
    elif p.z > 0.58: n = 'Spine'
    elif p.z > 0.16: n = 'Hips'
    else: n = ('LeftFoot' if p.x > 0 else 'RightFoot')
    group(body, n).add([v.index], 1.0, 'REPLACE')
bind(skirt)
for v in skirt.data.vertices: group(skirt, 'Hips').add([v.index], 1.0, 'REPLACE')
for o, n in parts:
    bind(o)
    group(o, n).add([v.index for v in o.data.vertices], 1.0, 'REPLACE')
# join arm parts into the body mesh
bpy.ops.object.select_all(action='DESELECT')
for o, _ in parts: o.select_set(True)
body.select_set(True); bpy.context.view_layer.objects.active = body
bpy.ops.object.join()

# ---------- 6. preview + export ----------
scn = bpy.context.scene
cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); scn.collection.objects.link(cam); scn.camera = cam
cam.data.type = 'ORTHO'; cam.data.ortho_scale = 2.0
scn.render.engine = 'BLENDER_WORKBENCH'; scn.display.shading.color_type = 'MATERIAL'; scn.display.shading.light = 'FLAT'
cols = {'DD_Black': (0.02,0.02,0.02), 'DD_Indigo': (0.35,0.45,0.75), 'DD_Red': (0.9,0.05,0.05), 'DD_Tiger': (1,0.5,0), 'DD_Skin': (1,0.8,0.6),
        'DD_Gold': (1,0.85,0), 'DD_TealGlow': (0,1,0.9), 'DD_Navy': (0.1,0.12,0.4), 'DD_Amber': (0.6,0.3,0.1), 'DD_Gourd': (0.5,0.8,0.2), 'DD_Paper': (1,1,0.7)}
for m in bpy.data.materials:
    if m.name in cols: m.diffuse_color = (*cols[m.name], 1)
scn.render.resolution_x = 600; scn.render.resolution_y = 700
for name, loc, rot in [('front', (0, -5, 0.8), (1.5708, 0, 0)), ('side', (5, 0, 0.8), (1.5708, 0, 1.5708))]:
    cam.location = loc; cam.rotation_euler = rot
    scn.render.filepath = os.path.join(prev, 'hrig_' + name + '.png'); bpy.ops.render.render(write_still=True)

bpy.ops.object.select_all(action='DESELECT')
for o in (rig, body, skirt): o.select_set(True)
bpy.ops.export_scene.fbx(filepath=out, use_selection=True, add_leaf_bones=False, bake_anim=False,
                         apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y', mesh_smooth_type='FACE')
print('EXPORTED', out, len(body.data.vertices), len(skirt.data.vertices))


