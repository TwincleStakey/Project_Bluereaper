# 이기어검 (flying-sword command) body clip for the GS hero (11-18). Headless:
#   blender -b --python eg_anim.py -- <outdir> [render] [export]
# Imports Assets/Game/GSHero/Art/GS_Hero.fbx, keys the pose table below (FK, arm directions aimed in the chest frame,
# legs through temporary IK targets), bakes to plain FK and exports Assets/Game/GSHero/Art/EG_Command.fbx (armature +
# mesh + action "EG_Command"). Reference (ArtSource/Eogeom/ref): 검협정연3 순양 chunyang_27.75 (two-finger 검결 raised
# beside the face = the command pose), chunyang_27.25 / dr_1 (arm thrust straight at the target / floating sword),
# chunyang_18.50-20.25 (left hand held forward while the swords fly), xj_2 (검결 held at the chest), Yondu board (body
# nearly still, the projectile does the work). Right hand keeps the katana hilt low and back.
# The rig has NO finger bones (GS_Hero.fbx: hand = one bone), so the 검결 hand shape cannot be keyed: the arm line carries it.
import bpy, sys, os, math
from mathutils import Vector, Matrix

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = argv[0] if argv else "C:/UnityProject/BulletHeavenArt/ArtSource/Eogeom/blender/out"
RENDER = "render" in argv
EXPORT = "export" in argv
os.makedirs(OUT, exist_ok=True)
ROOT = "C:/UnityProject/BulletHeavenArt"

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=ROOT + "/Assets/Game/GSHero/Art/GS_Hero.fbx")
sc = bpy.context.scene; sc.name = "GS_Char"; sc.render.fps = 30
rig = bpy.data.objects["GS_Rig"]
P = rig.pose.bones
def B(n): return P["mixamorig:" + n]
for pb in P: pb.rotation_mode = "QUATERNION"

# character frame -> world (character faces -Y, its right is -X, up +Z)
def W(x, y, z): return Vector((-x, -z, y)).normalized()
RW = rig.matrix_world.to_3x3()

def upd(): bpy.context.view_layer.update()

# v6 (Kido: "칼 뒤로 보낼때 너무 많이 휘어지는거같은데 ... 다리 안뚫고가게", annotated stroke: blade from the outside of the right hip
# straight out to the lower-left of the screen, low behind the body, clear of the legs): the katana direction is keyed
# directly (blade_w, aim frame), the wrist turns only as much as needed, and every frame is checked against the leg bones.
import numpy as np
SW = bpy.data.objects["GS_Sword"]
_v = np.array([v.co[:] for v in SW.data.vertices]); _c = _v.mean(0); _w, _V = np.linalg.eigh((_v - _c).T @ (_v - _c))
SW_AX = Vector(_V[:, 2]); SW_FLAT = Vector(_V[:, 0]); SW_C = Vector(_c)
def sword_world():
    upd()
    m = SW.matrix_world
    ax = (m.to_3x3() @ SW_AX).normalized(); hand = rig.matrix_world @ B("RightHand").head
    c = m @ SW_C
    if (c - hand).dot(ax) < 0: ax = -ax
    proj = np.array([(m @ Vector(v)).dot(ax) for v in _v[::25]])
    tip = c + ax * (proj.max() - c.dot(ax)); base = c + ax * (proj.min() - c.dot(ax))
    return ax, (m.to_3x3() @ SW_FLAT).normalized(), base, tip
def seg_dist(a0, a1, b0, b1):
    best = 9.0
    for i in range(21):
        p = a0.lerp(a1, i / 20)
        d = b1 - b0; t = max(0.0, min(1.0, (p - b0).dot(d) / max(d.length_squared, 1e-9)))
        best = min(best, (p - (b0 + d * t)).length)
    return best
def leg_clear():
    ax, fl, base, tip = sword_world()
    out = 9.0
    for side in ("Left", "Right"):
        hip = rig.matrix_world @ B(side + "UpLeg").head; knee = rig.matrix_world @ B(side + "Leg").head
        ank = rig.matrix_world @ B(side + "Foot").head
        out = min(out, seg_dist(base, tip, hip, knee), seg_dist(base, tip, knee, ank))
    return out

# v9 (Kido: "칼 어깨에 한번 올려봐 대검 몸통 머리 뚫지않게"): blade line vs head / neck / spine / shoulder / left arm, per frame
def body_clear():
    ax, fl, base, tip = sword_world()
    W_ = rig.matrix_world
    hd = W_ @ B("Head").head
    parts = {
        "head": (hd, hd + Vector((0, 0, 0.22))),
        "neck": (W_ @ B("Neck").head, hd),
        "chest": (W_ @ B("Spine1").head, W_ @ B("Neck").head),
        "belly": (W_ @ B("Hips").head, W_ @ B("Spine1").head),
        "Rshoulder": (W_ @ B("RightShoulder").head, W_ @ B("RightArm").head),
        "Lshoulder": (W_ @ B("LeftShoulder").head, W_ @ B("LeftArm").head),
        "Lupper": (W_ @ B("LeftArm").head, W_ @ B("LeftForeArm").head),
        "Lfore": (W_ @ B("LeftForeArm").head, W_ @ B("LeftHand").head),
    }
    out = {k: seg_dist(base, tip, a, b) for k, (a, b) in parts.items()}
    # the wide straw hat (gat): brim ~0.32 m round the head axis, ~0.17 m above the head bone; blade points under the brim
    # area must stay at least 0.04 m below it (negative = through the brim)
    brimZ = hd.z + 0.17; worst = 9.0
    for i in range(41):
        pnt = base.lerp(tip, i / 40)
        r = math.hypot(pnt.x - hd.x, pnt.y - hd.y)
        if r < 0.34: worst = min(worst, brimZ - pnt.z)
    out["hatBrim"] = worst
    return out

def rot_bone_world(pb, axis_w, deg):
    """rotate a pose bone about a world axis through its head"""
    upd()
    a = (RW.inverted() @ axis_w).normalized()
    M = pb.matrix.copy(); t = M.translation.copy()
    R = Matrix.Rotation(math.radians(deg), 4, a)
    pb.matrix = Matrix.Translation(t) @ R @ Matrix.Translation(-t) @ M
    upd()

def aim_bone(pb, d_world):
    """swing a pose bone (about its head) so its Y axis points along d_world"""
    upd()
    d = (RW.inverted() @ d_world).normalized()
    M = pb.matrix.copy(); y = M.to_3x3().col[1].normalized(); t = M.translation.copy()
    R = y.rotation_difference(d).to_matrix().to_4x4()
    pb.matrix = Matrix.Translation(t) @ R @ Matrix.Translation(-t) @ M
    upd()

# ---------------------------------------------------------------- leg IK (temporary, baked away)
def empty(name, loc):
    o = bpy.data.objects.new(name, None); sc.collection.objects.link(o); o.location = loc; return o
FL = empty("EG_FootL", rig.matrix_world @ B("LeftFoot").head)
FR = empty("EG_FootR", rig.matrix_world @ B("RightFoot").head)
KL = empty("EG_KneeL", Vector((0.2, -1.0, 0.5)))
KR = empty("EG_KneeR", Vector((-0.2, -1.0, 0.5)))
ANK = FL.location.z
for side, tgt, pole in (("Left", FL, KL), ("Right", FR, KR)):
    c = B(side + "Leg").constraints.new("IK"); c.name = "egIK"; c.target = tgt; c.pole_target = pole
    c.chain_count = 2; c.pole_angle = math.radians(90)
    B(side + "Leg").lock_ik_y = B(side + "Leg").lock_ik_z = True

# ---------------------------------------------------------------- pose table
# body: turn = chest turn to the RIGHT in degrees (+ = left shoulder forward), split hips 30 % / spine 70 %;
#       lean = forward lean (deg), dz = hips drop (m); feet: (x right, z forward) per foot in metres, foot yaw deg.
# arms: direction of upper arm / forearm / hand in the CHEST frame (x right, y up, z forward) unless *_w (world/aim frame).
READY = dict(turn=0, lean=3, dz=-0.03,
             lf=(-0.13, 0.04, 0), rf=(0.14, -0.06, 0),
             lu=(-0.18, -0.97, 0.02), lfo=(-0.08, -0.85, 0.5), lh=(-0.05, -0.8, 0.6),
             ru=(0.25, -0.92, -0.2), rfo=(0.12, -0.75, 0.62), rh=(0.1, -0.6, 0.8))
# anticipation = the 검결 command pose (chunyang_27.75): elbow out at shoulder height, forearm folded up, hand beside the face,
# fingers up; the chest coils a little the other way (left shoulder back) before the thrust (v3: -18 -> -24)
CMD = dict(turn=-24, lean=0, dz=-0.05,
           lf=(-0.13, 0.08, -10), rf=(0.15, -0.10, 0),
           lu=(-0.55, -0.12, 0.83), lfo=(0.50, 0.84, 0.20), lh=(0.15, 1.0, 0.1),
           ru=(0.3, -0.9, -0.3), rfo=(0.2, -0.8, 0.5), rh=(0.15, -0.6, 0.75))
# thrust (chunyang_27.25, dr_1, chunyang_19.25): left arm dead straight at the target at shoulder height, left shoulder
# forward (torso turned right ~40), short step in with the left foot, weight forward; right hand stays low and back
# v3 (read at game zoom 1.6): bigger twist (left shoulder well forward, 62), right shoulder + hilt pulled back, more lean,
# a longer step in with the left foot, the arm dead straight on the aim. The snap overshoots a little (f5) and settles (f8).
THRUST_V2 = dict(turn=40, lean=9, dz=-0.10,
              lf=(-0.10, 0.30, 25), rf=(0.18, -0.20, 30),
              lu_w=(0.0, 0.10, 1.0), lfo_w=(0.0, 0.10, 1.0), lh_w=(0.0, 0.12, 1.0),
              ru=(0.35, -0.85, -0.38), rfo=(0.3, -0.92, -0.05), rh=(0.25, -0.8, 0.4))
THRUST = dict(turn=66, lean=15, dz=-0.13,
              lf=(-0.07, 0.44, 32), rf=(0.21, -0.28, 38),
              lu_w=(0.0, 0.05, 1.0), lfo_w=(0.0, 0.05, 1.0), lh_w=(0.0, 0.06, 1.0),
              ru=(0.35, -0.72, -0.60), rfo=(0.28, -0.86, -0.42), rh=(0.2, -0.85, 0.1))
# v6 pull-back: right upper arm down and a little back/out, forearm hanging toward the blade line (no hard wrist break),
# blade from the outside of the right hip to the left-rear, low (tip ~0.25 m off the ground), behind both legs
BLADE_BACK = (-0.92, -0.32, -0.14)   # v6b: flatter on screen (~15 deg below horizontal, Kido's actual stroke), still low behind the legs
# idle / recall: blade hangs down and out to the right-rear, away from both legs (it used to hang between the feet)
BLADE_REST = (0.50, -0.70, -0.50)
THRUST.update(ru=(0.42, -0.86, -0.28), rfo_w=(-0.10, -0.85, -0.52), blade_w=BLADE_BACK)
SETTLE = dict(THRUST, turn=62, lean=13, dz=-0.12)
HOLD = dict(SETTLE, turn=60, lean=12, lu_w=(0.0, 0.08, 1.0), lfo_w=(0.0, 0.08, 1.0), lh_w=(0.0, 0.1, 1.0))
# recall (xj_2: 검결 held at the chest while the sword turns): hand pulled back in front of the chest, fingers up/forward
RECALL = dict(turn=14, lean=4, dz=-0.06,
              lf=(-0.11, 0.20, 15), rf=(0.16, -0.14, 15),
              lu=(-0.45, -0.55, 0.45), lfo=(0.62, 0.35, 0.62), lh=(0.25, 0.85, 0.45),
              ru=(0.3, -0.9, -0.3), rfo=(0.2, -0.85, 0.45), rh=(0.15, -0.6, 0.75))

# v3: snap 검결 -> thrust in 2 frames (was 3), extended pose held f5-f8, hold frame stays f11 (Unity holds there)
READY["blade_w"] = BLADE_REST; CMD["blade_w"] = BLADE_REST; RECALL["blade_w"] = (0.20, -0.62, -0.76)
# v9 shoulder rest (EG_SHOULDER=1 -> EG_CommandShoulder.fbx; ref ArtSource/Eogeom/ref/shoulder/ref_A_shoulder_hold_front.jpg):
# right fist up beside the right side of the head, slightly in front; the blade lies across the top of the right shoulder,
# flat side down, sloping back and down behind the shoulder; left arm relaxed. Rest, 검결 raise and the end of the recall use it.
SHOULDER = os.environ.get("EG_SHOULDER") == "1"
if SHOULDER:
    def _env(n, d): return tuple(float(x) for x in os.environ[n].split(",")) if os.environ.get(n) else d
    # picked from 4 tries (out_v9t*): fist in front of the right collarbone, blade on the shoulder top sloping back-down (side view = ref)
    # v9b (coordinator's game ref: Metal Gear Rising, Jetstream Sam's katana on the shoulder): fist beside the right ear at shoulder height,
    # elbow forward-out, blade flat on top of the shoulder running straight back, tip ~7 deg UP (kept low for the 2x blade and the hat)
    SH_RU = _env("EG_SH_RU", (0.55, -0.55, 0.62)); SH_RFO = _env("EG_SH_RFO", (-0.42, 0.82, -0.40)); SH_BLADE = _env("EG_SH_BLADE", (0.18, 0.14, -0.97))   # v10: lifted 2.4 cm off the shoulder
    # v10 (Kido: "공격모션 손 앞으로 할때 올리라고 보통때는 원래대로"): rest / walk / 검결 raise / recall stay as v8; ONLY the thrust,
    # settle and hold (left hand forward, also the whole interceptor hold) carry the sword on the right shoulder. The blade
    # direction is keyed in the CHEST frame there (blade_c), so it stays on the shoulder through the 62 deg twist and the lean.
    # v11b (Kido via coordinator: FF14 Dark Knight first, then MGR Sam; NOT across the middle of the back like the Mixamo axe):
    # the end direction is keyed in the AIM frame (screen space for the top camera): right-rear, out from the body
    # v13 (Kido: "어깨에 칼 올리라니깐 반대로 들고 있으면"): FF14 DRK carry = blade ON the shoulder going UP and back ~30 deg,
    # a little out to the right-rear (picked from out_v13a/b/c, top + right-side views); v12 was (0.60, 0.06, -0.80)
    SH_BLADE_W = _env("EG_SH_BLADE_W", (0.40, 0.50, -0.77))   # picked from out_v11f1..f6: right-rear, out from the body
    SH_RU = _env("EG_SH_RU", (0.75, -0.58, 0.32)); SH_RFO = _env("EG_SH_RFO", (-0.50, 0.70, 0.50))   # fist at the front of the shoulder (FF14 DRK)
    for K in (THRUST, SETTLE, HOLD):
        K.update(ru=SH_RU, rfo=SH_RFO)
        if SH_BLADE_W: K["blade_w"] = SH_BLADE_W; K.pop("blade_c", None)
        else: K["blade_c"] = SH_BLADE; K.pop("blade_w", None)
        for kk in ("rh", "rfo_w"): K.pop(kk, None)
KEYS = [(0, READY), (3, CMD), (5, THRUST), (8, SETTLE), (11, HOLD), (17, RECALL), (24, READY)]
# v11 (Kido: "자연스럽게 어깨 올리는 래퍼런스 ... 믹사모같은거부터"): the right-arm lift onto the shoulder follows Mixamo
# "Standing Disarm Over Shoulder" (ArtSource/Eogeom/ref/shoulder/mixamo, probe in blender/mx_Standing_Disarm_Over_Shoulder):
# the elbow leads up and forward, the fist rises past the side of the head with the blade swinging up, overshoots a little
# behind the shoulder, then settles onto it. Mixamo f9 / f13 / f17 arm directions (character frame) -> our f4 / f5 / f6,
# overshoot at f6, settled MGR rest at f8. The left arm keeps the 검결 thrust (only the right arm keys change).
# v12 (Kido: base stance = FF14 Dark Knight shoulder carry): EG_FF14BASE=1 keeps the sword on the right shoulder in EVERY key
# (rest, raise, thrust, hold, recall); in the chest frame on the twisted keys so it rides the shoulder. Exported to
# EG_CommandFF14.fbx. The left arm / torso of the attack come from Mixamo clips in Unity (EogeomCombat.MxLayer).
FF14BASE = SHOULDER and os.environ.get("EG_FF14BASE") == "1"
if FF14BASE:
    for K in (READY, CMD, THRUST, SETTLE, HOLD, RECALL):
        K.update(ru=SH_RU, rfo=SH_RFO)
        for kk in ("rh", "rfo_w", "blade_w", "blade_c"): K.pop(kk, None)
        K["blade_w"] = SH_BLADE_W   # aim frame on every key: right-rear on screen even through the 62 deg twist (chest frame went across the back)
# v14 (Kido: "래퍼런스랑 너무 다르자나", ref ArtSource/Eogeom/ref/shoulder/ref_ff14_drk_idle_xivmodarchive.jpg = FF14 DRK
# weapon-drawn idle): elbow out to the side and a little down, forearm straight up, fist BESIDE the right ear a little
# forward; the blade lies over the top of the right shoulder / trapezius behind the neck and runs back (and a little left)
# almost level, slightly descending. Chest frame (blade_c) so it stays on the shoulder through the attack twist.
# Torso upright with a slight lean back at rest, feet wider.
V14 = FF14BASE and os.environ.get("EG_V14") == "1"
if V14:
    V14_RU = _env("EG_V14_RU", (0.75, -0.28, 0.35)); V14_RFO = _env("EG_V14_RFO", (-0.30, 0.95, 0.05))   # v14g: hand a bit higher, the Unity retarget narrows the shoulder gap ~6 cm
    V14_BLADE = _env("EG_V14_BLADE", (-0.55, -0.14, -0.82))   # picked from out_v14a..e vs the ref (q34 view)
    for K in (READY, CMD, THRUST, SETTLE, HOLD, RECALL):
        K.update(ru=V14_RU, rfo=V14_RFO)
        for kk in ("rh", "rfo_w", "blade_w", "blade_c"): K.pop(kk, None)
        K["blade_c"] = V14_BLADE
    READY.update(lean=-3, lf=(-0.20, 0.04, -8), rf=(0.21, -0.06, 12))
# v16 (Kido's wuxia frame + Byakuya "Scatter"): the command is given fully side-on: left shoulder at the target (turn 90, half
# of it in the pelvis so the chest stays within 45 deg of the pelvis), waist leaning back, chin up, left arm dead straight
# at the target at shoulder height, feet in a wide side stance; the sword stays on the right shoulder (chest frame).
V16 = V14 and os.environ.get("EG_V16") == "1"
if V16:
    V16_TURN = float(os.environ.get("EG_V16_TURN", "90")); V16_LEAN = float(os.environ.get("EG_V16_LEAN", "-20"))
    for K, extra in ((THRUST, 0), (SETTLE, -3), (HOLD, -4)):
        K.update(turn=V16_TURN + extra, hips=0.5, lean=V16_LEAN, chin=-10, dz=-0.04,
                 lf=(-0.06, 0.30, 70), rf=(0.10, -0.30, 80),
                 lu_w=(0.0, 0.06, 1.0), lfo_w=(0.0, 0.05, 1.0), lh_w=(0.0, 0.02, 1.0))
    CMD.update(hips=0.5)
if SHOULDER and not FF14BASE and os.environ.get("EG_MXLIFT", "1") == "1":
    LIFT1 = dict(CMD); LIFT1.update(turn=CMD["turn"] * 0.3 + THRUST["turn"] * 0.7 * 0.5)
    MX = {4: ((0.77, -0.36, 0.53), (-0.23, 0.03, 0.97), (-0.47, 0.14, 0.87)),
          5: ((0.80, 0.13, 0.58), (-0.57, 0.61, 0.55), (-0.34, 0.94, 0.07)),
          6: ((0.80, 0.40, 0.42), (-0.72, 0.66, 0.18), None)}
    # f4: the blade still trails low behind (it pointed forward when the hand copied Mixamo's empty-hand angle)
    k4 = dict(LIFT1); k4.update(ru=MX[4][0], rfo=MX[4][1], blade_c=(0.40, -0.55, -0.73)); [k4.pop(x, None) for x in ("blade_w", "rh", "rfo_w")]
    k5 = dict(THRUST); k5.update(ru=MX[5][0], rfo=MX[5][1], rh=MX[5][2]); [k5.pop(x, None) for x in ("blade_w", "blade_c", "rfo_w")]
    k6 = dict(THRUST, turn=64, lean=14); k6.update(ru=(0.76, -0.35, 0.40), rfo=(-0.55, 0.75, 0.35), blade_w=(0.70, 0.32, -0.62))   # overshoot goes OUT and up, not behind the head (Mixamo's hand-behind-head came within 0.054 m)   # small overshoot up/out before it settles (aim frame like the rest)
    KEYS = [(0, READY), (3, CMD), (4, k4), (5, k5), (6, k6), (8, SETTLE), (11, HOLD), (17, RECALL), (24, READY)]
LENGTH = 24
HIT = 5   # thrust frame = the blade segments start to leave

def reset():
    for pb in P:
        pb.location = (0, 0, 0); pb.rotation_quaternion = (1, 0, 0, 0)
    upd()

def chest_dir(v, turn):
    """character-frame vector turned with the chest (turn right = clockwise from above)"""
    return Matrix.Rotation(math.radians(-turn), 3, "Z") @ W(*v)

def pose(k, f):
    reset()
    turn, lean, dz = k["turn"], k["lean"], k["dz"]
    up = Vector((0, 0, 1)); right = W(1, 0, 0)
    # hips: drop + 30 % of the turn + a third of the lean
    B("Hips").location = B("Hips").bone.matrix_local.to_3x3().inverted() @ (RW.inverted() @ Vector((0, 0, dz)))
    upd()
    hs = k.get("hips", 0.3)   # v16: share of the turn taken by the pelvis
    rot_bone_world(B("Hips"), up, -turn * hs)
    rot_bone_world(B("Hips"), right, -lean * 0.35)    # lean forward = rotate about the right axis (top goes to -Y)
    tr = -turn * (1 - hs) / 3
    rt = Matrix.Rotation(math.radians(-turn), 3, "Z") @ right
    for n in ("Spine", "Spine1", "Spine2"):
        rot_bone_world(B(n), up, tr)
        rot_bone_world(B(n), rt, -lean * 0.65 / 3)
    # head back on the aim (neck 40 / head 60) and level
    for n, w in (("Neck", 0.4), ("Head", 0.6)):
        rot_bone_world(B(n), up, turn * w)
        rot_bone_world(B(n), right, lean * w + k.get("chin", 0.0) * w)   # v16: chin < 0 = head tipped back (chin up)
    # arms
    for side, pre in (("Left", "l"), ("Right", "r")):
        for bone, key in (("Arm", pre + "u"), ("ForeArm", pre + "fo"), ("Hand", pre + "h")):
            if key + "_w" in k: d = W(*k[key + "_w"])
            elif key in k: d = chest_dir(k[key], turn)
            else: continue
            aim_bone(B(side + bone), d)
        if side == "Right" and ("blade_w" in k or "blade_c" in k):
            want = chest_dir(k["blade_c"], turn).normalized() if "blade_c" in k else W(*k["blade_w"])
            ax, fl, base, tip = sword_world()
            q = ax.rotation_difference(want)
            hm = B("RightHand"); upd(); t = hm.matrix.translation.copy()
            Rq = (RW.inverted() @ q.to_matrix() @ RW).to_4x4()
            hm.matrix = Matrix.Translation(t) @ Rq @ Matrix.Translation(-t) @ hm.matrix; upd()
            # roll about the blade so its flat faces up (reads from the top camera)
            ax, fl, base, tip = sword_world()
            upv = Vector((0, 0, 1)) - want * want.z; upv.normalize()
            flp = fl - want * fl.dot(want); flp.normalize()
            ang = math.degrees(flp.angle(upv)); sgn = 1 if flp.cross(upv).dot(want) > 0 else -1
            if ang > 90: ang -= 180
            rot_bone_world(hm, want, sgn * ang * k.get("flatup", 0.8))
            continue
        roll = k.get(pre + "roll", 0.0)
        if roll:   # turn the hand about its own long axis (the katana's direction is set by this)
            upd(); hy = (RW @ B(side + "Hand").matrix.to_3x3().col[1]).normalized()
            rot_bone_world(B(side + "Hand"), hy, roll)
    # feet (IK targets) + knee poles over the toes
    for tgt, pole, key, sx in ((FL, KL, "lf", 1), (FR, KR, "rf", -1)):
        x, z, yaw = k[key]
        tgt.location = Vector((-x, -z, ANK)); tgt.keyframe_insert("location", frame=f)
        toe = Matrix.Rotation(math.radians(-yaw), 3, "Z") @ Vector((0, -1, 0))
        pole.location = tgt.location + toe * 0.8 + Vector((0, 0, 0.5)); pole.keyframe_insert("location", frame=f)
    upd()
    for pb in P:
        if pb.name.endswith(("Leg", "UpLeg")): continue
        pb.keyframe_insert("rotation_quaternion", frame=f)
    B("Hips").keyframe_insert("location", frame=f)

rig.animation_data_create()
act = bpy.data.actions.new("EG_Command"); rig.animation_data.action = act
for o in (FL, FR, KL, KR): o.animation_data_create()
RR = float(os.environ.get("EG_RROLL", "180"))   # 180: blade hangs down-forward at rest, trails down-back at the thrust (ref chunyang_20.25)
for f, k in KEYS:
    k = dict(k); k["rroll"] = k.get("rroll", 0.0) + RR
    sc.frame_set(f); pose(k, f)

# snappy: the thrust (f3 -> f6) linear, the rest smooth
def fcurves(a):
    try: return list(a.fcurves)
    except Exception:
        out = []
        for layer in a.layers:
            for strip in layer.strips:
                for cb in strip.channelbags: out.extend(cb.fcurves)
        return out
for o in (rig, FL, FR, KL, KR):
    for fc in fcurves(o.animation_data.action):
        for kp in fc.keyframe_points:
            kp.interpolation = "LINEAR" if 3 <= kp.co.x < 5 else "BEZIER"

# bake to plain FK
sc.frame_start, sc.frame_end = 0, LENGTH
bpy.context.view_layer.objects.active = rig
for o in bpy.context.view_layer.objects: o.select_set(False)
rig.select_set(True)
bpy.ops.object.mode_set(mode="POSE")
for pb in P:
    try: pb.select = True
    except Exception: pb.bone.select = True
bpy.ops.nla.bake(frame_start=0, frame_end=LENGTH, only_selected=True, visual_keying=True, clear_constraints=True,
                 use_current_action=False, bake_types={"POSE"})
bpy.ops.object.mode_set(mode="OBJECT")
baked = rig.animation_data.action
old = bpy.data.actions.get("EG_Command")
if old and old != baked: bpy.data.actions.remove(old)
baked.name = "EG_Command"; baked.use_fake_user = True
for o in (FL, FR, KL, KR): bpy.data.objects.remove(o)
print("baked", baked.name)

# v6: blade vs leg bones every frame (baked FK), and the wrist bend (forearm line vs blade)
with open(os.path.join(OUT, "eg_leg_clear.txt"), "w") as fh:
    worst = 9.0
    for f in range(0, LENGTH + 1):
        sc.frame_set(f); upd()
        c = leg_clear(); worst = min(worst, c)
        ax, fl, base, tip = sword_world()
        fo = ((rig.matrix_world @ B("RightHand").head) - (rig.matrix_world @ B("RightForeArm").head)).normalized()
        fh.write(f"f{f}: blade-leg clearance {c:.3f} m, wrist bend {math.degrees(fo.angle(ax)):.0f} deg, tip height {tip.z:.2f} m\n")
    fh.write(f"worst {worst:.3f} m\n")
    print("LEG CLEAR worst", round(worst, 3))
with open(os.path.join(OUT, "eg_body_clear.txt"), "w") as fh:
    worstb = {}
    for f in range(0, LENGTH + 1):
        sc.frame_set(f); upd()
        bc = body_clear()
        for kk, vv in bc.items(): worstb[kk] = min(worstb.get(kk, 9.0), vv)
        fh.write(f"f{f}: " + " ".join(f"{kk} {vv:.3f}" for kk, vv in bc.items()) + "\n")
    fh.write("worst " + " ".join(f"{kk} {vv:.3f}" for kk, vv in worstb.items()) + "\n")
    print("BODY CLEAR worst", {kk: round(vv, 3) for kk, vv in worstb.items()})

# per-key log: left hand position relative to the chest + arm straightness
with open(os.path.join(OUT, "eg_keys.txt"), "w") as fh:
    for f, k in KEYS:
        sc.frame_set(f); upd()
        lh = rig.matrix_world @ B("LeftHand").head; sh = rig.matrix_world @ B("LeftArm").head
        hd = rig.matrix_world @ B("Head").head; rh = rig.matrix_world @ B("RightHand").head
        fh.write(f"f{f}: Lhand {tuple(round(v, 2) for v in lh)} shoulder {tuple(round(v, 2) for v in sh)} head {tuple(round(v, 2) for v in hd)} Rhand {tuple(round(v, 2) for v in rh)}\n")

if RENDER:
    cam = bpy.data.objects.new("EG_Cam", bpy.data.cameras.new("EG_Cam")); sc.collection.objects.link(cam)
    cam.data.type = "ORTHO"; cam.data.ortho_scale = 2.6
    sc.camera = cam
    sun = bpy.data.objects.new("EG_Sun", bpy.data.lights.new("EG_Sun", "SUN")); sc.collection.objects.link(sun)
    sc.render.engine = "BLENDER_WORKBENCH"
    sc.display.shading.light = "STUDIO"; sc.display.shading.color_type = "MATERIAL"
    sc.render.resolution_x = sc.render.resolution_y = 360
    sc.render.image_settings.file_format = "PNG"
    for o in sc.objects:
        if o.name.startswith("GS_Eye"): o.hide_render = True
    views = {
        # game-like: above and behind (55 deg), the character faces up-screen
        "game": (Vector((0, 1, 0)) * math.cos(math.radians(55)) * 8 + Vector((0, 0, 0.9 + 8 * math.sin(math.radians(55)))), (math.radians(35), 0, math.radians(180))),
        # 3/4 front-left (the 검결 hand is visible)
        "q34": (Vector((3.2, -4.2, 2.0)), None),
        "side": (Vector((8, 0, 1.0)), (math.radians(90), 0, math.radians(90))),
        # v13: straight down (blade must rest ON the shoulder line) and the character's right side
        "top": (Vector((0, 0, 9.0)), (0, 0, math.radians(180))),
        "rside": (Vector((-8, 0, 1.3)), (math.radians(90), 0, math.radians(-90))),
    }
    if os.environ.get("EG_VIEWS"): views = {k: v for k, v in views.items() if k in os.environ["EG_VIEWS"].split(",")}
    for vn, (loc, rot) in views.items():
        cam.location = loc
        if rot: cam.rotation_euler = rot
        else:
            d = (Vector((0, 0, 1.1)) - loc).normalized()
            cam.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
        d = os.path.join(OUT, vn); os.makedirs(d, exist_ok=True)
        for f in [int(x) for x in os.environ["EG_FRAMES"].split(",")] if os.environ.get("EG_FRAMES") else range(0, LENGTH + 1):
            sc.frame_set(f); sc.render.filepath = os.path.join(d, f"f_{f:03d}.png")
            bpy.ops.render.render(write_still=True)

if EXPORT:
    for a in list(bpy.data.actions):
        if a.name != "EG_Command": bpy.data.actions.remove(a)
    for o in bpy.context.view_layer.objects: o.select_set(False)
    for n in ("GS_Rig", "GS_Hero"):
        o = bpy.data.objects[n]; o.select_set(True)
    bpy.context.view_layer.objects.active = rig
    fbx = ROOT + os.environ["EG_FBX"] if os.environ.get("EG_FBX") else ROOT + ("/Assets/Game/GSHero/Art/EG_CommandFF14.fbx" if FF14BASE else "/Assets/Game/GSHero/Art/EG_CommandShoulder.fbx" if SHOULDER else "/Assets/Game/GSHero/Art/EG_Command.fbx")
    bpy.ops.export_scene.fbx(filepath=fbx, use_selection=True, object_types={"ARMATURE", "MESH"}, add_leaf_bones=False,
                             bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
                             bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0, use_armature_deform_only=True)
    print("exported", fbx)
print("done")
