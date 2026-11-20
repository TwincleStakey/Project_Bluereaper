# 장산범 v2 bite (11-18 Kido: "머리 앞으로 살짝 기울이면서 빼고, 딱 물고, 강아지가 뭐 물면 좌우로 막 머리 흔드는 것처럼",
# biting the player character). Procedural keys on the v2 AutoRig (video2rig/projects/jsb_v2/autorig/rigged.blend):
#   wind-up (head tilts down and pulls back) -> strike down to the player on the ground -> jaws snap shut ->
#   lift with the prey -> dog-style shake left/right -> toss, back to rest.
# Front paws stay planted (two-bone IK) while the chest dips. A capsule stands in for the player (1.8 m at boss scale x25).
#   blender -b rigged.blend -P v2_bite.py -- <out base>      writes <out>.blend, <out>.fbx, <out>.json (grab timing + mouth offset)
import bpy, sys, math, json
from mathutils import Vector, Matrix, Quaternion

out = sys.argv[sys.argv.index('--') + 1]
sc = bpy.context.scene; sc.render.fps = 30
arm = bpy.data.objects['AutoRig']
for o in bpy.context.view_layer.objects: o.select_set(False)
arm.select_set(True); bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode='POSE')
pbs = arm.pose.bones
for p in pbs: p.rotation_mode = 'QUATERNION'
AW = arm.matrix_world
X, Y, Z = Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1))

def upd(): bpy.context.view_layer.update()
def head_w(n): return AW @ pbs[n].head
def tail_w(n): return AW @ pbs[n].tail
def rot_w(n, axis, deg):
    """turn pose bone n about a world axis through its head"""
    if abs(deg) < 1e-6: return
    pb = pbs[n]; h = AW @ pb.head
    R = Matrix.Translation(h) @ Matrix.Rotation(math.radians(deg), 4, axis) @ Matrix.Translation(-h)
    pb.matrix = AW.inverted() @ R @ AW @ pb.matrix; upd()
def reset():
    for p in pbs: p.matrix_basis = Matrix()
    upd()

reset()
REST = {n: (head_w(n).copy(), tail_w(n).copy()) for n in pbs.keys()}
# mouth point = between the snout tip and the jaw tip, kept in the Head bone's space
mouth_rest = (REST['Head'][1] + REST['Jaw'][1]) * 0.5
mouth_local = (AW @ pbs['Head'].matrix).inverted() @ mouth_rest
def mouth(): return (AW @ pbs['Head'].matrix) @ mouth_local

def solve_leg(a, b, c_name, target):
    """two-bone IK: bones a (upper) and b (lower); the head of bone c_name must reach target"""
    pa, pb_, pc = head_w(a), head_w(b), head_w(c_name)
    lab, lcb = (pb_ - pa).length, (pc - pb_).length
    lat = max(0.001, min((target - pa).length, lab + lcb - 1e-4))
    def ang(u, v): return math.acos(max(-1, min(1, u.normalized().dot(v.normalized()))))
    a0, b0 = ang(pc - pa, pb_ - pa), ang(pa - pb_, pc - pb_)
    a1 = math.acos(max(-1, min(1, (lcb * lcb - lab * lab - lat * lat) / (-2 * lab * lat))))
    b1 = math.acos(max(-1, min(1, (lat * lat - lab * lab - lcb * lcb) / (-2 * lab * lcb))))
    axis = (pc - pa).cross(pb_ - pa)
    if axis.length < 1e-9: return
    axis.normalize()
    rot_w(a, axis, math.degrees(a1 - a0)); rot_w(b, axis, math.degrees(b1 - b0))
    pa, pc = head_w(a), head_w(c_name)
    q = (pc - pa).rotation_difference(target - pa)
    ax, an = q.to_axis_angle()
    rot_w(a, ax, math.degrees(an))

def apply(pt, pn, ph, yaw, roll, jaw, tailyaw=0.0, byaw=0.0, sway=0.0, ik=True):
    """pt trunk pitch, pn neck pitch, ph head pitch (+ = nose down), yaw (+ = to the boss's left), roll, jaw (+ = open)"""
    reset()
    paw_m = {s: (AW @ pbs['FrontPaw' + s].matrix).copy() for s in ('.L', '.R')}
    hpaw_m = {s: (AW @ pbs['HindPaw' + s].matrix).copy() for s in ('.L', '.R')}
    # 11-18 Kido: the whole body shakes with the head. The hips swing sideways against the head, the spine whips in a
    # wave (byaw lags the head), the chest rolls; all four paws stay planted by IK
    if abs(sway) > 1e-6 or abs(byaw) > 1e-6:
        hp = pbs['Hips']
        hp.matrix = AW.inverted() @ Matrix.Translation(X * sway) @ AW @ hp.matrix; upd()
        rot_w('Hips', Z, -byaw * 0.45)
        rot_w('Spine1', Z, byaw * 0.30); rot_w('Spine2', Z, byaw * 0.40); rot_w('Spine3', Z, byaw * 0.45)
        rot_w('Spine2', Y, -byaw * 0.35)
    for n, w in (('Spine1', 0.3), ('Spine2', 0.3), ('Spine3', 0.4)): rot_w(n, X, pt * w)
    for n, w in (('Neck1', 0.5), ('Neck2', 0.5)): rot_w(n, X, pn * w)
    for n, w in (('Neck1', 0.25), ('Neck2', 0.30), ('Head', 0.45)): rot_w(n, Z, yaw * w)
    rot_w('Head', X, ph)
    fw = (tail_w('Head') - head_w('Head')).normalized()
    rot_w('Neck2', fw, roll * 0.4); rot_w('Head', fw, roll * 0.6)
    # jaw hinge = the head's sideways axis
    side = (AW.to_3x3() @ pbs['Head'].matrix.to_3x3()) @ Vector((0, 0, 1))
    if side.dot(X) < 0: side = -side
    rot_w('Jaw', side, jaw)
    for i, n in enumerate(('Tail1', 'Tail2', 'Tail3', 'Tail4')): rot_w(n, Z, tailyaw * (0.5 + 0.25 * i))
    # front paws stay where they stand
    for s in (('.L', '.R') if ik else ()):
        solve_leg('UpperArm' + s, 'Forearm' + s, 'FrontPaw' + s, REST['FrontPaw' + s][0])
        pbs['FrontPaw' + s].matrix = AW.inverted() @ paw_m[s]; upd()
        solve_leg('Thigh' + s, 'Shin' + s, 'HindPaw' + s, REST['HindPaw' + s][0])
        pbs['HindPaw' + s].matrix = AW.inverted() @ hpaw_m[s]; upd()

def ease(x): x = max(0.0, min(1.0, x)); return x * x * (3 - 2 * x)
def lerp(a, b, t): return a + (b - a) * t

# the player stands in front of the boss, on the ground; find how far the head must come down to reach
PLAYER_H = 1.8 / 25.0
player_pos = Vector((0.0, REST['Head'][1].y - 0.11, 0.0))
grab_at = player_pos + Z * (PLAYER_H * 0.55)
best = None
for pt_ in range(0, 61, 5):
    for pn_ in range(-30, 71, 10):
        for ph_ in range(-70, 31, 10):
            apply(pt_, pn_, ph_, 0, 0, 30, ik=False)
            m = mouth(); d = abs(m.z - grab_at.z) + max(0.0, m.y + 0.43) * 2 + pt_ * 0.0006   # low enough to take the player, still in front
            if best is None or d < best[0]: best = (d, (pt_, pn_, ph_), m.copy())
print('STRIKE pose pt/pn/ph %s miss %.3f mouth %s target %s' % (best[1], best[0], tuple(round(v, 3) for v in best[2]), tuple(round(v, 3) for v in grab_at)))
STRIKE = tuple(float(v) for v in best[1])
# move the player to where the mouth actually lands (keeps the bite exact)
player_pos = Vector((best[2].x, best[2].y, 0.0))

F_WIND, F_HOLD, F_STRIKE, F_SNAP, F_LIFT, F_SHAKE_END, F_TOSS, F_END = 10, 14, 18, 20, 26, 58, 64, 80
SHAKE_HZ = 4.5
def pose_at(f):
    """returns (pt, pn, ph, yaw, roll, jaw, tailyaw, byaw, sway)"""
    wind = (-4.0, -18.0, 28.0)          # chest up a touch, neck back, head tilted down: the wind-up
    strike = STRIKE
    carry = (5.0, 2.0, 6.0)             # head back up with the prey
    yaw = roll = tail = byaw = sway = 0.0
    if f <= F_WIND:
        t = ease(f / F_WIND); p = [lerp(0, w, t) for w in wind]; jaw = lerp(0, 8, t)
    elif f <= F_HOLD:
        p = list(wind); jaw = 8 + 6 * (f - F_WIND) / (F_HOLD - F_WIND)
    elif f <= F_STRIKE:
        t = ((f - F_HOLD) / (F_STRIKE - F_HOLD)) ** 0.6   # fast out of the hold
        p = [lerp(w, s_, t) for w, s_ in zip(wind, strike)]; jaw = lerp(14, 32, min(1, t * 1.5))
    elif f <= F_SNAP:
        p = list(strike); jaw = lerp(32, -17, (f - F_STRIKE) / (F_SNAP - F_STRIKE))
    elif f <= F_LIFT:
        t = ease((f - F_SNAP) / (F_LIFT - F_SNAP)); p = [lerp(s_, c, t) for s_, c in zip(strike, carry)]; jaw = -17
    elif f <= F_SHAKE_END:
        u = (f - F_LIFT) / 30.0; n = (f - F_LIFT) / (F_SHAKE_END - F_LIFT)
        env = ease(min(1, n * 6)) * (1.0 - 0.35 * n) * ease(min(1, (1 - n) * 5))
        ph_ = 2 * math.pi * SHAKE_HZ * u
        yaw = 40.0 * env * math.sin(ph_); roll = 14.0 * env * math.sin(ph_ + 0.9)
        p = [carry[0], carry[1] + 4 * env * math.sin(ph_ * 2), carry[2]]; jaw = -17
        tail = -34.0 * env * math.sin(ph_ - 1.4)
        byaw = 16.0 * env * math.sin(ph_ - 0.7)          # the body follows the head a beat later
        sway = -0.022 * env * math.sin(ph_ - 0.7)        # hips thrown the other way (model units; x25 = 0.55 m)
    elif f <= F_TOSS:
        t = ease((f - F_SHAKE_END) / (F_TOSS - F_SHAKE_END)); p = [lerp(c, w, t) for c, w in zip(carry, (-3, -10, -12))]
        yaw = lerp(0, 18, t); jaw = lerp(-17, 22, t)
    else:
        t = ease((f - F_TOSS) / (F_END - F_TOSS)); p = [lerp(w, 0, t) for w in (-3, -10, -12)]; yaw = lerp(18, 0, t); jaw = lerp(22, 0, t)
    return p[0], p[1], p[2], yaw, roll, jaw, tail, byaw, sway

# player stand-in
bpy.ops.object.mode_set(mode='OBJECT')
bpy.ops.mesh.primitive_cylinder_add(radius=PLAYER_H * 0.16, depth=PLAYER_H, location=player_pos + Z * PLAYER_H * 0.5)
player = bpy.context.object; player.name = 'PlayerProxy'
pm = bpy.data.materials.new('PlayerProxy'); pm.diffuse_color = (0.1, 0.35, 0.9, 1); player.data.materials.append(pm)
player.rotation_mode = 'QUATERNION'
bpy.context.view_layer.objects.active = arm; arm.select_set(True); player.select_set(False)
bpy.ops.object.mode_set(mode='POSE')

act = bpy.data.actions.new('v2_bite'); arm.animation_data_create(); arm.animation_data.action = act
sc.frame_start, sc.frame_end = 0, F_END
held = None; track = []
stand = Matrix.Translation(player_pos + Z * PLAYER_H * 0.5)
prev = {}
for f in range(0, F_END + 1):
    apply(*pose_at(f))
    for p in pbs:
        q = p.rotation_quaternion.copy()
        if p.name in prev and q.dot(prev[p.name]) < 0: q.negate(); p.rotation_quaternion = q
        prev[p.name] = q
        p.keyframe_insert('rotation_quaternion', frame=f); p.keyframe_insert('location', frame=f)
    hm = AW @ pbs['Head'].matrix
    if f == F_SNAP: held = hm.inverted() @ stand          # the jaws close on the player: from here it rides the head
    if held is not None and f <= F_TOSS: pmx = hm @ held
    elif held is not None:
        # tossed: a short arc to the boss's left, then lying on the ground
        t = (f - F_TOSS) / max(1, (F_END - F_TOSS) * 0.6); t = min(1, t)
        a0 = (AW @ pbs['Head'].matrix @ held).translation if f == F_TOSS + 1 else toss_from
        if f == F_TOSS + 1: toss_from = a0
        land = Vector((toss_from.x + 0.22, toss_from.y - 0.05, PLAYER_H * 0.16))
        pos = toss_from.lerp(land, t) + Z * 0.08 * math.sin(math.pi * t)
        pmx = Matrix.Translation(pos) @ Matrix.Rotation(math.radians(90 * t + 40), 4, Y)
    else: pmx = stand
    if f == F_TOSS: toss_from = pmx.translation.copy()
    player.matrix_world = pmx; upd()
    player.keyframe_insert('location', frame=f); player.keyframe_insert('rotation_quaternion', frame=f)
    track.append([round(v, 5) for v in pmx.translation] + [round(v, 5) for v in pmx.to_quaternion()])
bpy.ops.object.mode_set(mode='OBJECT')

mh = (AW @ pbs['Head'].matrix)
json.dump({'fps': 30, 'frames': F_END + 1, 'wind': F_WIND, 'strike': F_STRIKE, 'snap': F_SNAP, 'lift': F_LIFT, 'shake_end': F_SHAKE_END,
           'toss': F_TOSS, 'player_height': PLAYER_H, 'player_stand': list(player_pos), 'held_in_head': [list(r) for r in held],
           'player_track': track, 'note': 'model units (boss scale x25 in Unity), Blender axes: -Y forward, Z up'}, open(out + '.json', 'w'))
bpy.ops.wm.save_as_mainfile(filepath=out + '.blend', copy=True)
bpy.ops.object.select_all(action='DESELECT')
for o in sc.objects:
    if o.type in ('ARMATURE', 'MESH') and o.name != 'PlayerProxy' and not o.name.startswith('Plane') and o.visible_get(): o.select_set(True)
bpy.ops.export_scene.fbx(filepath=out + '.fbx', use_selection=True, apply_scale_options='FBX_SCALE_ALL', bake_anim=True,
                         bake_anim_use_all_actions=False, bake_anim_use_nla_strips=False, add_leaf_bones=False, path_mode='COPY', embed_textures=False)
print('BITE_OK', out, 'frames', F_END + 1)
