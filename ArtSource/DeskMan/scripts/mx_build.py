# Mixamo-rigged scholar: Writing mocap + brush in the right hand + paper/inkstone at the brush tip.
# blender -b -P mx_build.py -- Scholar_Writing.fbx out.blend out.fbx
import bpy, sys, math
from mathutils import Vector, Matrix
argv = sys.argv[sys.argv.index('--') + 1:]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=argv[0])
sc = bpy.context.scene; sc.render.fps = 30
ar = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]; ar.name = 'ScholarMx'
body = [o for o in bpy.data.objects if o.type == 'MESH'][0]; body.name = 'Scholar_Body'
act = ar.animation_data.action; act.name = 'Sit_Write'; act.use_fake_user = True
P = ar.pose.bones
def W(n, t='head'): return ar.matrix_world @ getattr(P['mixamorig:' + n], t)
def upd(): bpy.context.view_layer.update()

def mat(name, col, rough=0.7):
    m = bpy.data.materials.new(name); m.use_nodes = True
    b = m.node_tree.nodes['Principled BSDF']; b.inputs['Base Color'].default_value = (*col, 1); b.inputs['Roughness'].default_value = rough
    return m
def mesh(name, vs, fs, m):
    me = bpy.data.meshes.new(name); me.from_pydata(vs, [], fs); me.update()
    o = bpy.data.objects.new(name, me); sc.collection.objects.link(o); me.materials.append(m); return o
def cyl(name, r0, r1, z0, h, m, seg=10):
    vs, fs = [], []
    for k in range(seg):
        a = 2 * math.pi * k / seg
        vs += [(r0 * math.cos(a), r0 * math.sin(a), z0), (r1 * math.cos(a), r1 * math.sin(a), z0 + h)]
    for k in range(seg):
        a, b = 2 * k, 2 * ((k + 1) % seg); fs.append((a, b, b + 1, a + 1))
    vs += [(0, 0, z0), (0, 0, z0 + h)]; c0, c1 = len(vs) - 2, len(vs) - 1
    for k in range(seg):
        a, b = 2 * k, 2 * ((k + 1) % seg); fs += [(b, a, c0), (a + 1, b + 1, c1)]
    return mesh(name, vs, fs, m)
def box(name, sx, sy, sz, m):
    x, y, z = sx / 2, sy / 2, sz / 2
    vs = [(-x,-y,-z),(x,-y,-z),(x,y,-z),(-x,y,-z),(-x,-y,z),(x,-y,z),(x,y,z),(-x,y,z)]
    return mesh(name, vs, [(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)], m)

m_bamboo = mat('SCH_Bamboo', (0.42, 0.30, 0.16)); m_hair = mat('SCH_BrushHair', (0.03, 0.03, 0.03), 0.5)
m_paper = mat('SCH_Paper', (0.86, 0.80, 0.66), 0.95); m_stone = mat('SCH_Inkstone', (0.06, 0.06, 0.07), 0.35)
m_ink = mat('SCH_Ink', (0.01, 0.01, 0.012), 0.1)

# --- brush in the pen grip (frame 1): through the thumb/index tips, leaning like the mocap pen ---
sc.frame_set(1); upd()
grip = (W('RightHandThumb3', 'tail') + W('RightHandIndex3', 'tail')) * 0.5
knuck = W('RightHandIndex1')
TIP_BELOW = float(argv[3]) if len(argv) > 3 else 0.115
# 11-20 Kido: match a real calligraphy hold (ref: Wikimedia 'East Asian calligraphy scheme 04', brush hold): the brush stands
# straight up and down, held around the middle of the shaft between thumb and index/middle, the hand well above the paper.
axis = Vector((0, 0, -1))
brush = cyl('Brush', 0.0062, 0.0055, 0.03, 0.24, m_bamboo); tipm = cyl('BrushTip', 0.0005, 0.007, 0.0, 0.03, m_hair); tipm.parent = brush
tip_pos = grip + axis * TIP_BELOW
rot = (-axis).to_track_quat('Z', 'Y').to_matrix().to_4x4()     # brush +Z = up along the handle
brush.matrix_world = Matrix.Translation(tip_pos) @ rot
upd()
mw = brush.matrix_world.copy()
brush.parent = ar; brush.parent_type = 'BONE'; brush.parent_bone = 'mixamorig:RightHand'; upd()
brush.matrix_world = mw; upd()
# lowest point the brush tip reaches over the clip = paper surface (the mocap pen lifts up to ~5 cm between strokes)
zs = []
for f in range(1, int(act.frame_range[1]) + 1, 2):
    sc.frame_set(f); upd(); zs.append((brush.matrix_world @ Vector((0, 0, 0))).z)
sc.frame_set(1); upd()
desk_z = min(zs)
paper = box('Desk_Paper', 0.30, 0.28, 0.002, m_paper); paper.location = (tip_pos.x + 0.02, tip_pos.y + 0.02, desk_z - 0.001)
board = box('Desk_Board', 0.36, 0.32, 0.02, m_stone); board.location = (paper.location.x, paper.location.y, desk_z - 0.012)
pw = box('Desk_PaperWeight', 0.30, 0.03, 0.012, m_stone); pw.location = (paper.location.x, paper.location.y - 0.155, desk_z + 0.006)
ink = Vector((tip_pos.x - 0.30, tip_pos.y + 0.08, desk_z))
st = box('Desk_Inkstone', 0.13, 0.20, 0.03, m_stone); st.location = ink + Vector((0, 0, -0.005))
wl = box('Desk_InkWell', 0.10, 0.07, 0.004, m_ink); wl.location = ink + Vector((0, -0.04, 0.0115))
# brush tip travel over the clip (how far the mocap pen strays from the paper plane)
zs = []
for f in range(1, int(act.frame_range[1]) + 1, 5):
    sc.frame_set(f); upd(); zs.append((brush.matrix_world @ Vector((0, 0, 0))).z)
hips = W('Hips'); lf = W('LeftFoot'); rf = W('RightFoot')
print('MEASURE tip', tuple(round(x, 3) for x in tip_pos), 'tipz range', round(min(zs), 3), round(max(zs), 3), 'hips', tuple(round(x, 3) for x in hips),
      'feet z', round(lf.z, 3), round(rf.z, 3), 'grip', tuple(round(x, 3) for x in grip))
sc.frame_set(1)
bpy.ops.wm.save_as_mainfile(filepath=argv[1])
bpy.ops.object.select_all(action='DESELECT')
for o in sc.objects:
    if o.type in ('ARMATURE', 'MESH'): o.select_set(True)
bpy.ops.export_scene.fbx(filepath=argv[2], use_selection=True, apply_scale_options='FBX_SCALE_ALL', bake_anim=True, bake_anim_use_all_actions=True,
                         bake_anim_use_nla_strips=False, add_leaf_bones=False, path_mode='COPY', embed_textures=False, bake_anim_simplify_factor=0.0)
print('MX_OK')
