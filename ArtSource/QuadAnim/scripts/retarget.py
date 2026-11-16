# Retarget one clip from a downloaded quadruped rig onto the 장산범 AutoRig quadruped (or any AutoRig boss with the
# same bone names). Run on the boss .blend:
#   blender -b werewolf_snake_v3.blend -P retarget.py -- <profile> <source file> <action name> <out base> [speed] [fps] [src f0 f1]
# Writes <out base>.fbx (Unity) and <out base>.blend (review). See ../README.md.
#
# Method (per output frame, 30 fps by default):
#  1. Aim: every mapped target bone points along the source direction (from_point -> to_point); roll follows the
#     source bone's rotation change from its rest pose. Target bone lengths stay the target's own.
#  2. Hips: translation = source pelvis offset from its rest * body scale (hip-height ratio).
#  3. Legs: paw (toe base) goal = neutral target paw + source paw offset from its neutral * leg scale, so a planted
#     source paw stays planted on the target; hock = goal - paw direction * paw length; analytic 2-bone IK
#     (bend plane from step 1) puts thigh/shin (upper arm/forearm) there.
import bpy, sys, os, math
from mathutils import Vector, Matrix, Quaternion

a = sys.argv[sys.argv.index('--') + 1:]
prof_name, src_path, act_name, out_base = a[0], a[1], a[2], a[3]
speed = float(a[4]) if len(a) > 4 else 1.0
OUT_FPS = int(a[5]) if len(a) > 5 else 30
TRIM = (float(a[6]), float(a[7])) if len(a) > 7 else None   # source frame range to keep
TRAVEL = float(os.environ.get('QA_TRAVEL', '1'))   # keep this share of the source's horizontal body travel (knock-back deaths)
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from profiles import PROFILES, TARGET_LEGS
P = PROFILES[prof_name]
LAT = P.get('lateral', 1.0)
DELTA = set(P['aim']) - set(P.get('absolute', []))   # bones that copy the source direction outright

sc = bpy.context.scene
tgt = bpy.data.objects['AutoRig']
if tgt.animation_data: tgt.animation_data.action = None
before = set(bpy.data.objects)
before_act = set(bpy.data.actions)
ext = os.path.splitext(src_path)[1].lower()
sc.render.fps = OUT_FPS; sc.render.fps_base = 1.0
if ext in ('.glb', '.gltf'):
    bpy.ops.import_scene.gltf(filepath=src_path)
else:
    bpy.ops.import_scene.fbx(filepath=src_path, automatic_bone_orientation=False, use_anim=True)
src_fps = sc.render.fps / sc.render.fps_base
new_objs = [o for o in bpy.data.objects if o not in before]
src = [o for o in new_objs if o.type == 'ARMATURE' and o.name.split('.')[0] == P['armature']]
assert src, 'source armature %s not found in %s' % (P['armature'], [o.name for o in new_objs if o.type == 'ARMATURE'])
src = src[0]
for o in new_objs:                       # keep only the source armature
    if o is not src: bpy.data.objects.remove(o, do_unlink=True)
act = bpy.data.actions.get(act_name)
assert act, 'action %s not in %s' % (act_name, [x.name for x in bpy.data.actions if x not in before_act])
src.animation_data_create(); src.animation_data.action = act
if hasattr(src.animation_data, 'action_slot') and len(getattr(act, 'slots', [])):
    src.animation_data.action_slot = act.slots[0]
f0, f1 = act.frame_range
if TRIM: f0, f1 = TRIM
sc.render.fps = OUT_FPS
n_out = int(math.floor((f1 - f0) / src_fps * OUT_FPS / speed)) + 1
print('SRC', src.name, 'act', act_name, 'range', f0, f1, 'src_fps', src_fps, '-> frames', n_out)

mw_s = src.matrix_world.copy()
mt_inv = tgt.matrix_world.inverted()
def s_point(pt, rest=False):
    if rest: return REF['p'][pt].copy()
    b, w = pt
    pb = src.pose.bones[b]; v = pb.head if w == 'h' else pb.tail
    return mt_inv @ (mw_s @ v)
def s_rot(b, rest=False):
    if rest: return REF['q'][b].copy()
    m = (mw_s @ src.pose.bones[b].matrix)
    return (mt_inv.to_3x3() @ m.to_3x3()).normalized().to_quaternion()
# reference ("neutral standing") pose of the source: profile 'ref' = (action, frame); default = rest pose
REF = {'p': {}, 'q': {}}
ref = P.get('ref')
if ref:
    ra = bpy.data.actions[ref[0]]; src.animation_data.action = ra
    if hasattr(src.animation_data, 'action_slot') and len(getattr(ra, 'slots', [])): src.animation_data.action_slot = ra.slots[0]
    sc.frame_set(int(ref[1]))
else:
    src.animation_data.action = None
    for pb in src.pose.bones: pb.matrix_basis.identity()
    bpy.context.view_layer.update()
for pb in src.pose.bones:
    for w in 'ht': REF['p'][(pb.name, w)] = s_point((pb.name, w))
    REF['q'][pb.name] = s_rot(pb.name)
src.animation_data.action = act
if hasattr(src.animation_data, 'action_slot') and len(getattr(act, 'slots', [])): src.animation_data.action_slot = act.slots[0]

missing = set()
for tb, (pa, pb_, tw) in P['aim'].items():
    for n in (pa[0], pb_[0], tw):
        if n not in src.data.bones: missing.add(n)
    if tb not in tgt.data.bones: missing.add('target:' + tb)
assert not missing, 'missing bones: %s' % sorted(missing)

TB = tgt.data.bones
order = []
def walk(b):
    order.append(b.name)
    for c in b.children: walk(c)
for b in TB:
    if b.parent is None: walk(b)
Y = Vector((0, 1, 0))

# ---- rest measurements
def tgt_head(n): return TB[n].head_local.copy()
def neutral_pair(pts, cx):
    L, R = pts
    hx = (abs(L.x - cx) + abs(R.x - cx)) / 2
    y = (L.y + R.y) / 2; z = (L.z + R.z) / 2
    return Vector((cx + hx, y, z)), Vector((cx - hx, y, z))
t_cx = tgt_head('Hips').x
s_cx = s_point((P['pelvis'], 'h'), True).x
t_toe = {k: tgt_head(v[3]) for k, v in TARGET_LEGS.items()}
s_toe = {k: s_point(P['legs'][k][1], True) for k in TARGET_LEGS}
t_n, s_n = {}, {}
for fr in ('hind', 'front'):
    t_n[fr + '.L'], t_n[fr + '.R'] = neutral_pair((t_toe[fr + '.L'], t_toe[fr + '.R']), t_cx)
    s_n[fr + '.L'], s_n[fr + '.R'] = neutral_pair((s_toe[fr + '.L'], s_toe[fr + '.R']), s_cx)
scale = {}
for k in TARGET_LEGS:
    th = tgt_head(TARGET_LEGS[k][0]).z - t_n[k].z
    sh = s_point(P['legs'][k][0], True).z - s_n[k].z
    scale[k] = th / sh
s_body = sum(scale.values()) / 4
print('SCALES', {k: round(v, 4) for k, v in scale.items()}, 'body', round(s_body, 4))
pel_rest = s_point((P['pelvis'], 'h'), True)
s_rest_q = {b: s_rot(b, True) for b in {v[2] for v in P['aim'].values()}}
t_rest_q = {n: TB[n].matrix_local.to_quaternion() for n in order}
ground = min(v.z for v in t_n.values())

def swing(q, d_from, d_to):
    return d_from.rotation_difference(d_to) @ q

def solve_frame():
    Pm = {}
    dv = (s_point((P['pelvis'], 'h')) - pel_rest) * s_body
    drift = Vector((dv.x, dv.y, 0)) * (1 - TRAVEL)          # armature-space pose matrices (no scale)
    def place(n, head, q):
        Pm[n] = Matrix.Translation(head) @ q.to_matrix().to_4x4()
    for n in order:
        b = TB[n]
        if b.parent is None:
            head = b.head_local.copy()
        else:
            pp = Pm[b.parent.name]
            head = pp @ (TB[b.parent.name].matrix_local.inverted() @ b.head_local)
        if n == 'Hips':
            head = b.head_local + (s_point((P['pelvis'], 'h')) - pel_rest) * s_body - drift
        if n in P['aim']:
            pa, pb_, tw = P['aim'][n]
            d = (s_point(pb_) - s_point(pa)).normalized()
            qc = s_rot(tw) @ s_rest_q[tw].inverted() @ t_rest_q[n]
            if n in DELTA:   # torso: keep the target's own rest shape, add the source's change from its rest
                d0 = (s_point(pb_, True) - s_point(pa, True)).normalized()
                d = d0.rotation_difference(d) @ (t_rest_q[n] @ Y)
            q = swing(qc, qc @ Y, d)
        elif b.parent is not None:
            q = Pm[b.parent.name].to_quaternion() @ (TB[b.parent.name].matrix_local.inverted() @ b.matrix_local).to_quaternion()
        else:
            q = t_rest_q[n]
        place(n, head, q)
    # ---- leg IK
    for k, (up, lo, paw, toes) in TARGET_LEGS.items():
        off = s_point(P['legs'][k][1]) - s_n[k]
        off.x *= LAT
        G = t_n[k] + off * scale[k] - drift
        G.z = max(G.z, ground)
        qpaw = Pm[paw].to_quaternion(); dp = (qpaw @ Y).normalized()
        C = G - dp * TB[paw].length
        A = Pm[up].translation.copy()
        l1, l2 = TB[up].length, TB[lo].length
        axis = C - A; dist = axis.length
        dist_c = min(max(dist, abs(l1 - l2) + 1e-4), (l1 + l2) * 0.999)
        axis.normalize()
        Kfk = Pm[lo].translation
        bend = (Kfk - A); bend = bend - axis * bend.dot(axis)
        if bend.length < 1e-6: bend = (Pm[up].to_quaternion() @ Vector((0, 0, 1)))
        bend.normalize()
        ca = (l1 * l1 + dist_c * dist_c - l2 * l2) / (2 * l1 * dist_c); ca = max(-1, min(1, ca))
        K = A + axis * (l1 * ca) + bend * (l1 * math.sqrt(1 - ca * ca))
        Cr = A + axis * dist_c
        qu = Pm[up].to_quaternion(); qu = swing(qu, qu @ Y, (K - A).normalized())
        ql = Pm[lo].to_quaternion(); ql = swing(ql, ql @ Y, (Cr - K).normalized())
        place(up, A, qu); place(lo, K, ql); place(paw, Cr, qpaw)
        place(toes, Cr + dp * TB[paw].length, Pm[toes].to_quaternion())
    return Pm

pbs = tgt.pose.bones
for pb in pbs:
    pb.rotation_mode = 'QUATERNION'; pb.matrix_basis.identity()
tgt.animation_data_create()
out_act = bpy.data.actions.new(os.path.basename(out_base))
tgt.animation_data.action = out_act
prev_q = {}
for i in range(n_out):
    fs = f0 + i * speed * src_fps / OUT_FPS
    sc.frame_set(int(math.floor(fs)), subframe=fs - math.floor(fs))
    Pm = solve_frame()
    for n in order:
        b = TB[n]
        if b.parent is None: rest_chain = b.matrix_local
        else: rest_chain = Pm[b.parent.name] @ (TB[b.parent.name].matrix_local.inverted() @ b.matrix_local)
        basis = rest_chain.inverted() @ Pm[n]
        loc, q, _ = basis.decompose()
        if n in prev_q and prev_q[n].dot(q) < 0: q.negate()
        prev_q[n] = q
        pb = pbs[n]; pb.location = loc; pb.rotation_quaternion = q
        pb.keyframe_insert('rotation_quaternion', frame=i)
        if n in ('Hips', 'Root'): pb.keyframe_insert('location', frame=i)
sc.frame_start, sc.frame_end = 0, n_out - 1
bpy.data.objects.remove(src, do_unlink=True)
sc.frame_set(0)

# contact report: hind/front toe height range
lows = []
for i in range(0, n_out, max(1, n_out // 12)):
    sc.frame_set(i)
    lows.append(min((tgt.matrix_world @ pbs[v[3]].head).z for v in TARGET_LEGS.values()))
print('TOE_MIN_Z', [round(v, 3) for v in lows])
bpy.ops.wm.save_as_mainfile(filepath=out_base + '.blend', copy=True)
bpy.ops.object.select_all(action='DESELECT')
for o in sc.objects:
    if o.type in ('ARMATURE', 'MESH') and not o.name.startswith('Plane') and o.visible_get(): o.select_set(True)
bpy.ops.export_scene.fbx(filepath=out_base + '.fbx', use_selection=True, apply_scale_options='FBX_SCALE_ALL', bake_anim=True,
                         bake_anim_use_all_actions=False, bake_anim_use_nla_strips=False, add_leaf_bones=False,
                         path_mode='STRIP', embed_textures=False)   # textures come from the boss materials in Unity
print('FBX_OK', out_base + '.fbx', 'frames', n_out)
