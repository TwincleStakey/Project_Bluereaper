# Scholar seated clips. blender -b -P anim.py -- scholar_rig.blend out.blend out.fbx
# Blender coords: front = -Y, +X = character's left, floor z=0 under the chair centre.
# Prefab pivot = seat (z 0.48). Desk top z 0.78, desk near edge y -0.30.
import bpy, sys, math, random
from mathutils import Vector, Matrix, Quaternion, Euler

argv = sys.argv[sys.argv.index('--') + 1:]
bpy.ops.wm.open_mainfile(filepath=argv[0])
sc = bpy.context.scene
sc.render.fps = 30
ar = bpy.data.objects['ScholarRig']
body = bpy.data.objects['Scholar_Body']
S = 1.9
DESK = 0.78
PAPER = Vector((-0.06, -0.46, DESK + 0.003))   # paper centre
INK = Vector((-0.33, -0.42, DESK + 0.035))     # inkstone well

# ---------- fix unweighted verts (nearest bone) ----------
bones = ar.data.bones
for v in body.data.vertices:
    if not any(g.weight > 0.01 for g in v.groups):
        best, bd = None, 9
        for b in bones:
            a, c = b.head_local, b.tail_local
            ab = c - a
            t = max(0, min(1, (v.co - a).dot(ab) / ab.length_squared))
            d = (a + ab * t - v.co).length
            if d < bd: best, bd = b.name, d
        body.vertex_groups[best].add([v.index], 1.0, 'REPLACE')

# ---------- props ----------
def mat(name, col, rough=0.7):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes['Principled BSDF']
    bsdf.inputs['Base Color'].default_value = (*col, 1)
    bsdf.inputs['Roughness'].default_value = rough
    return m

def add_mesh(name, verts, faces, m):
    me = bpy.data.meshes.new(name); me.from_pydata(verts, [], faces); me.update()
    o = bpy.data.objects.new(name, me); sc.collection.objects.link(o)
    me.materials.append(m)
    return o

def cyl(name, r0, r1, h, seg, m, z0=0.0):
    vs, fs = [], []
    for k in range(seg):
        a = 2 * math.pi * k / seg
        vs.append((r0 * math.cos(a), r0 * math.sin(a), z0))
        vs.append((r1 * math.cos(a), r1 * math.sin(a), z0 + h))
    for k in range(seg):
        a, b = 2 * k, 2 * ((k + 1) % seg)
        fs.append((a, b, b + 1, a + 1))
    vs.append((0, 0, z0)); vs.append((0, 0, z0 + h))
    c0, c1 = len(vs) - 2, len(vs) - 1
    for k in range(seg):
        a, b = 2 * k, 2 * ((k + 1) % seg)
        fs.append((b, a, c0)); fs.append((a + 1, b + 1, c1))
    return add_mesh(name, vs, fs, m)

def box(name, sx, sy, sz, m):
    x, y, z = sx / 2, sy / 2, sz / 2
    vs = [(-x,-y,-z),(x,-y,-z),(x,y,-z),(-x,y,-z),(-x,-y,z),(x,-y,z),(x,y,z),(-x,y,z)]
    fs = [(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)]
    return add_mesh(name, vs, fs, m)

for o in list(sc.objects):
    if o.name.startswith(('Brush', 'Desk_')): bpy.data.objects.remove(o)

m_bamboo = mat('SCH_Bamboo', (0.42, 0.30, 0.16))
m_hair = mat('SCH_BrushHair', (0.03, 0.03, 0.03), 0.5)
m_paper = mat('SCH_Paper', (0.86, 0.80, 0.66), 0.95)
m_stone = mat('SCH_Inkstone', (0.06, 0.06, 0.07), 0.35)
m_ink = mat('SCH_Ink', (0.01, 0.01, 0.012), 0.1)

# brush: tip at origin, pointing down (-Z). total 0.24
brush = cyl('Brush', 0.0065, 0.0055, 0.20, 10, m_bamboo, 0.035)
tip = cyl('BrushTip', 0.0005, 0.0075, 0.035, 10, m_hair, 0.0)
tip.parent = brush
# desk set (static, exported in the same fbx, parented to nothing)
paper = box('Desk_Paper', 0.30, 0.28, 0.002, m_paper); paper.location = PAPER - Vector((0, 0, 0.001))
under = box('Desk_PaperWeight', 0.30, 0.03, 0.012, m_stone); under.location = PAPER + Vector((0, -0.155, 0.006))
stone = box('Desk_Inkstone', 0.13, 0.20, 0.03, m_stone); stone.location = INK + Vector((0, 0.02, -0.02))
well = box('Desk_InkWell', 0.10, 0.07, 0.004, m_ink); well.location = INK + Vector((0, -0.04, -0.003))
stick = box('Desk_InkStick', 0.025, 0.09, 0.012, m_stone); stick.location = INK + Vector((0.09, 0.05, -0.028))
stick.rotation_euler = (0, 0, 0.3)
# brush stand (rest) not needed: brush stays in hand

# ---------- rig helpers ----------
P = ar.pose.bones
for pb in P: pb.rotation_mode = 'QUATERNION'
bpy.context.view_layer.objects.active = ar

def upd(): bpy.context.view_layer.update()

def set_act(act):
    ar.animation_data.action = act
    if hasattr(ar.animation_data, 'action_slot') and act.slots:
        ar.animation_data.action_slot = act.slots[0]
    upd()

def rest_all():
    for pb in P:
        pb.location = (0, 0, 0); pb.rotation_quaternion = (1, 0, 0, 0); pb.scale = (1, 1, 1)

def rotw(name, axis, deg):
    pb = P[name]; M = pb.matrix.copy(); piv = M.translation.copy()
    R = Matrix.Rotation(math.radians(deg), 4, Vector(axis))
    pb.matrix = Matrix.Translation(piv) @ R @ Matrix.Translation(-piv) @ M
    upd()

def movew(name, d):
    pb = P[name]; M = pb.matrix.copy(); M.translation += Vector(d); pb.matrix = M; upd()

def curl(side, amt, thumb):
    for n, k in (('Fingers1', 1.0), ('Fingers2', 0.8)):
        # 11-20 fix: positive X curled the fingers UP (back of the hand) = the twisted hands Kido saw
        P[n + side].rotation_quaternion = Quaternion((1, 0, 0), math.radians(-amt * k))
    P['Thumb1' + side].rotation_quaternion = Quaternion((1, 0, 0), math.radians(-thumb))
    P['Thumb2' + side].rotation_quaternion = Quaternion((1, 0, 0), math.radians(-thumb * 0.8))

BODY = ['Hips', 'Spine', 'Chest', 'Neck', 'Head', 'UpperLeg.L', 'UpperLeg.R', 'LowerLeg.L', 'LowerLeg.R',
        'Foot.L', 'Foot.R', 'Shoulder.L', 'Shoulder.R']
FING = [n + s for n in ('Fingers1', 'Fingers2', 'Thumb1', 'Thumb2') for s in ('.L', '.R')]

def body_pose(lean=1.0, head_dn=18, head_yaw=0, head_roll=0, sway=0.0, breath=0.0, sh_r=0.0):
    rest_all(); upd()
    movew('Hips', (0, 0.02, -0.39))
    rotw('Hips', (1, 0, 0), 6 * lean)
    for s, sx in (('.L', 1), ('.R', -1)):
        rotw('UpperLeg' + s, (1, 0, 0), -70 - 6 * lean)
        rotw('UpperLeg' + s, (0, 0, 1), 7 * sx)
        rotw('LowerLeg' + s, (1, 0, 0), 110)
        rotw('Foot' + s, (1, 0, 0), -38)
    rotw('Spine', (1, 0, 0), 12 * lean + breath * 0.6)
    rotw('Spine', (0, 1, 0), sway)
    rotw('Chest', (1, 0, 0), 15 * lean - breath)
    rotw('Shoulder.R', (1, 0, 0), sh_r)
    rotw('Neck', (1, 0, 0), 6 * lean)
    rotw('Head', (1, 0, 0), head_dn)
    rotw('Head', (0, 0, 1), head_yaw)
    rotw('Head', (0, 1, 0), head_roll)

# ---------- IK targets ----------
for n in ('TipR', 'WristR', 'WristL'):
    if n in bpy.data.objects: bpy.data.objects.remove(bpy.data.objects[n])
def empty(n):
    e = bpy.data.objects.new(n, None); sc.collection.objects.link(e); e.rotation_mode = 'QUATERNION'; return e
tipR, wristR, wristL = empty('TipR'), empty('WristR'), empty('WristL')

rest_all(); upd()
def rest_rot(name): return (ar.matrix_world @ P[name].matrix).to_quaternion()
# right hand: point forward/down 35deg, palm facing left-down (brush held upright between thumb & fingers)
# brush grip = 'handshake' fist: knuckles forward and a little down, palm facing inward (+X), brush upright through it
qR = Quaternion((0, 0, 1), math.radians(90)) @ rest_rot('Hand.R')          # -X -> -Y (forward)
qR = Quaternion((0, 1, 0), math.radians(-80)) @ qR                           # back of hand -> outward (-X)
qR = Quaternion((1, 0, 0), math.radians(18)) @ qR                            # knuckles a little down
qL = Quaternion((0, 0, 1), math.radians(-70)) @ rest_rot('Hand.L')           # +X -> forward-inward
qL = Quaternion((1, 0, 0), math.radians(8)) @ qL
tipR.rotation_quaternion = qR
wristL.rotation_quaternion = qL
# brush grip offset in hand frame: tip sits ahead (bone +Y) and below the wrist
handLen = (P['Hand.R'].tail - P['Hand.R'].head).length
GRIP = Vector((0.0, handLen * 1.15, 0.0))       # along the hand, in hand-local space (bone Y)
def wrist_from_tip(tip_pos, q):
    grip = tip_pos + Vector((0, 0, 0.115))          # fist ~11.5cm above the tip
    # wrist = grip back along the hand and away from the palm (palm = local -Z, so +Z offset)
    return grip - q @ Vector((0, 1, 0)) * (handLen * 0.75) + q @ Vector((0, 0, 1)) * 0.03

def add_c(pbn, target, chain):
    pb = P[pbn]
    for c in list(pb.constraints): pb.constraints.remove(c)
    if chain:
        c = pb.constraints.new('IK'); c.target = target; c.chain_count = chain; c.use_tail = True
        return c
    c = pb.constraints.new('COPY_ROTATION'); c.target = target; return c
for c in list(P['Hand.R'].constraints) + list(P['Hand.L'].constraints): pass
add_c('LowerArm.R', wristR, 2); add_c('Hand.R', tipR, 0)
add_c('LowerArm.L', wristL, 2); add_c('Hand.L', wristL, 0)
# pole targets keep elbows out/down
for s, sx in (('.R', -1), ('.L', 1)):
    pn = 'Pole' + s
    if pn in bpy.data.objects: bpy.data.objects.remove(bpy.data.objects[pn])
    pole = empty(pn); pole.location = (sx * 0.40, 0.30, 0.35)
    ik = P['LowerArm' + s].constraints[0]; ik.pole_target = pole; ik.pole_angle = math.radians(-90 if s == '.R' else -90)

# ---------- clip authoring ----------
def smooth(t): return t * t * (3 - 2 * t)

class Clip:
    def __init__(s, name, n):
        s.name, s.n = name, n
        s.tip, s.wl, s.bd, s.cr = {}, {}, {}, {}
    def path(s, store, keys):   # keys: list of (frame, value) piecewise-smooth interpolation
        keys.sort(key=lambda k: k[0])
        for f in range(s.n + 1):
            for i in range(len(keys) - 1):
                if keys[i][0] <= f <= keys[i + 1][0]:
                    a, b = keys[i], keys[i + 1]
                    t = (f - a[0]) / max(1, b[0] - a[0]); t = smooth(t)
                    store[f] = a[1] + (b[1] - a[1]) * t if not isinstance(a[1], dict) else {k: a[1][k] + (b[1][k] - a[1][k]) * t for k in a[1]}
                    break
            else:
                store[f] = keys[-1][1] if f > keys[-1][0] else keys[0][1]

def bake(clip):
    act_tmp = bpy.data.actions.new('tmp_' + clip.name)
    ar.animation_data_create(); ar.animation_data.action = act_tmp
    for f in range(clip.n + 1):
        sc.frame_set(f)
        bd = clip.bd[f]
        body_pose(**bd)
        curl('.R', clip.cr[f]['r'], 30); curl('.L', clip.cr[f]['l'], 5)
        for n in BODY + FING: P[n].keyframe_insert('rotation_quaternion', frame=f)
        P['Hips'].keyframe_insert('location', frame=f)
        tp = clip.tip[f]
        tipR.location = tp; wristR.location = wrist_from_tip(tp, qR)
        wristL.location = clip.wl[f]
        for e in (tipR, wristR, wristL): e.keyframe_insert('location', frame=f)
    sc.frame_start, sc.frame_end = 0, clip.n
    # bake visual pose to final action
    bpy.ops.object.select_all(action='DESELECT'); ar.select_set(True); bpy.context.view_layer.objects.active = ar
    bpy.ops.object.mode_set(mode='POSE'); bpy.ops.pose.select_all(action='SELECT')
    bpy.ops.nla.bake(frame_start=0, frame_end=clip.n, only_selected=False, visual_keying=True,
                     clear_constraints=False, use_current_action=False, bake_types={'POSE'})
    bpy.ops.object.mode_set(mode='OBJECT')
    act = ar.animation_data.action; act.name = clip.name; act.use_fake_user = True
    # record brush tip relative to hand on frame 0 for brush parenting check
    bpy.data.actions.remove(act_tmp)
    for e in (tipR, wristR, wristL): e.animation_data_clear()
    print('BAKED', clip.name, clip.n)
    return act

R = random.Random(7)
WL_REST = PAPER + Vector((0.20, 0.07, 0.035))     # left hand flat beside the paper, holding it
BD0 = dict(lean=1.0, head_dn=18, head_yaw=0, head_roll=0, sway=0.0, breath=0.0, sh_r=0.0)
UP = 0.022

def breath_keys(c, period=120, amp=1.2):
    out = []
    for f in range(0, c.n + 1, 15):
        out.append(f)
    return [(f, amp * math.sin(2 * math.pi * f / period)) for f in range(0, c.n + 1)]

def body_track(c, extra):  # extra: list of (frame, dict of overrides)
    keys = []
    for f, d in extra:
        dd = dict(BD0); dd.update(d); keys.append((f, dd))
    c.path(c.bd, keys)
    br = breath_keys(c)
    for f, v in br: c.bd[f]['breath'] = c.bd[f]['breath'] + v

def write_clip():
    c = Clip('Sit_Write', 300)
    start = PAPER + Vector((-0.08, -0.12, UP))
    keys = [(0, start.copy())]
    f = 0
    col_x = -0.08; y0 = -0.085
    chars = 0
    head = [(0, dict(head_dn=18, head_yaw=-4))]
    for col in range(2):
        y = y0
        for ch in range(4 if col == 0 else 3):
            cx = col_x + R.uniform(-0.006, 0.006)
            for st in range(R.choice((2, 3, 3, 4))):
                p0 = PAPER + Vector((cx + R.uniform(-0.018, 0.018), y + R.uniform(-0.012, 0.012), 0))
                d = R.choice([Vector((0.03, 0, 0)), Vector((0, 0.032, 0)), Vector((0.022, 0.022, 0)), Vector((-0.02, 0.02, 0)), Vector((0.012, 0.03, 0))])
                f += R.randint(5, 8); keys.append((f, p0 + Vector((0, 0, 0.012))))
                f += 3; keys.append((f, p0 + Vector((0, 0, 0.0))))                 # touch down
                f += R.randint(5, 9); keys.append((f, p0 + d + Vector((0, 0, -0.001))))  # stroke (press)
                f += 3; keys.append((f, p0 + d + Vector((0, 0, 0.015))))           # lift
            y += 0.045
            head.append((f, dict(head_dn=18 + R.uniform(-3, 4), head_yaw=-4 + (cx * 60), sway=R.uniform(-1.5, 1.5))))
        col_x += 0.07
        f += 10; keys.append((f, PAPER + Vector((col_x, y0 - 0.02, 0.04))))
    # look over the written columns, then return to start
    f += 16; keys.append((f, PAPER + Vector((0.0, -0.05, 0.06))))
    head.append((f, dict(head_dn=14, head_yaw=2, head_roll=3)))
    c.n = max(c.n, f + 30)
    keys.append((c.n, start.copy()))
    head.append((c.n, dict(head_dn=18, head_yaw=-4)))
    c.path(c.tip, keys)
    c.path(c.wl, [(0, WL_REST), (c.n // 2, WL_REST + Vector((0.0, -0.02, 0))), (c.n, WL_REST)])
    body_track(c, head)
    c.path(c.cr, [(0, {'r': 95, 'l': 12}), (c.n, {'r': 95, 'l': 12})])
    return c

def dip_clip():
    c = Clip('Sit_Dip', 150)
    start = PAPER + Vector((-0.08, -0.12, UP))
    above = INK + Vector((0, -0.03, 0.10))
    keys = [(0, start), (22, above), (34, INK + Vector((0, -0.04, -0.002))), (46, INK + Vector((0, -0.01, 0.006))),
            (56, INK + Vector((0, -0.05, -0.002))), (68, INK + Vector((0, 0.03, 0.012))), (78, INK + Vector((0.02, 0.05, 0.02))),
            (88, INK + Vector((0.0, 0.03, 0.012))), (100, above), (126, start + Vector((0, 0, 0.03))), (150, start)]
    c.path(c.tip, keys)
    c.path(c.wl, [(0, WL_REST), (150, WL_REST)])
    body_track(c, [(0, dict(head_dn=18, head_yaw=-4)), (26, dict(head_dn=20, head_yaw=-22, sway=-3)),
                   (96, dict(head_dn=20, head_yaw=-20, sway=-3)), (130, dict(head_dn=18, head_yaw=-4)), (150, dict(head_dn=18, head_yaw=-4))])
    c.path(c.cr, [(0, {'r': 95, 'l': 12}), (150, {'r': 95, 'l': 12})])
    return c

def pause_clip():
    c = Clip('Sit_Pause', 180)
    start = PAPER + Vector((-0.08, -0.12, UP))
    held = PAPER + Vector((-0.14, 0.08, 0.13))
    keys = [(0, start), (24, held), (150, held + Vector((0.01, 0.0, 0.0))), (180, start)]
    c.path(c.tip, keys)
    chin = Vector((0.03, -0.19, 1.10))
    c.path(c.wl, [(0, WL_REST), (40, chin), (60, chin + Vector((0, 0, -0.03))), (80, chin), (100, chin + Vector((0, 0, -0.03))),
                  (130, chin), (165, WL_REST), (180, WL_REST)])
    body_track(c, [(0, dict(head_dn=18, head_yaw=-4)), (30, dict(lean=0.55, head_dn=4, head_yaw=6, head_roll=-4)),
                   (90, dict(lean=0.5, head_dn=-2, head_yaw=14, head_roll=-6)), (140, dict(lean=0.55, head_dn=6, head_yaw=4, head_roll=-2)),
                   (180, dict(head_dn=18, head_yaw=-4))])
    c.path(c.cr, [(0, {'r': 95, 'l': 12}), (40, {'r': 95, 'l': 45}), (130, {'r': 95, 'l': 45}), (165, {'r': 95, 'l': 12}), (180, {'r': 95, 'l': 12})])
    return c

def attend_clip():
    c = Clip('Sit_Attend', 120)
    held = PAPER + Vector((-0.10, 0.10, 0.07))
    c.path(c.tip, [(0, held), (60, held + Vector((0, 0, 0.006))), (120, held)])
    wl = WL_REST + Vector((0.0, 0.06, 0.0))
    c.path(c.wl, [(0, wl), (120, wl)])
    body_track(c, [(0, dict(lean=0.3, head_dn=-2)), (120, dict(lean=0.3, head_dn=-2))])
    c.path(c.cr, [(0, {'r': 95, 'l': 15}), (120, {'r': 95, 'l': 15})])
    return c

clips = [write_clip(), dip_clip(), pause_clip(), attend_clip()]
acts = [bake(c) for c in clips]

for pb in P:
    for c in list(pb.constraints): pb.constraints.remove(c)
for n in ('TipR', 'WristR', 'WristL', 'Pole.R', 'Pole.L'):
    bpy.data.objects.remove(bpy.data.objects[n])
upd()
# brush follows Hand.R: compute hand->brush transform on frame 0 of Sit_Write
set_act(acts[0])
sc.frame_set(0)
for c in list(P['Hand.R'].constraints) + list(P['LowerArm.R'].constraints) + list(P['Hand.L'].constraints) + list(P['LowerArm.L'].constraints):
    pass
tip_world = Matrix.Translation(clips[0].tip[0]) @ qR.to_matrix().to_4x4()
# brush mesh axis is world-vertical; hand frame is qR -> brush local rotation = qR^-1 (upright brush)
brush.rotation_mode = 'QUATERNION'
hand_w = ar.matrix_world @ P['Hand.R'].matrix
brush_w = Matrix.Translation(clips[0].tip[0])
brush.parent = ar; brush.parent_type = 'BONE'; brush.parent_bone = 'Hand.R'
upd()
brush.matrix_parent_inverse = Matrix.Identity(4)
upd()
brush.matrix_world = brush_w
upd()
upd()
for it in range(4):
    sc.frame_set(1); sc.frame_set(0); upd()
    e = clips[0].tip[0] - brush.matrix_world.translation
    print('BRUSHFIX', it, round(e.length, 4))
    M = brush.matrix_world.copy(); M.translation += e; brush.matrix_world = M; upd()
# remove constraints & helper empties (baked)
# error check: brush tip vs intended tip
for ci, c in enumerate(clips):
    set_act(acts[ci])
    err = 0
    for f in range(0, c.n + 1, 5):
        sc.frame_set(f)
        err = max(err, (brush.matrix_world.translation - c.tip[f]).length)
    print('TIPERR', c.name, round(err, 4))
set_act(acts[0])
sc.frame_set(0)
bpy.ops.wm.save_as_mainfile(filepath=argv[1])

for a_ in list(bpy.data.actions):
    if not a_.name.startswith('Sit_'): bpy.data.actions.remove(a_)
# export fbx: rig + body + brush + desk props, all actions
bpy.ops.object.select_all(action='DESELECT')
for o in sc.objects:
    if o.type in ('ARMATURE', 'MESH'): o.select_set(True)
bpy.ops.export_scene.fbx(filepath=argv[2], use_selection=True, apply_scale_options='FBX_SCALE_ALL', bake_anim=True,
                         bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False, add_leaf_bones=False,
                         path_mode='COPY', embed_textures=False, bake_anim_simplify_factor=0.0)
print('ANIM_OK')
