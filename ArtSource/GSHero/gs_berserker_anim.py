# GS_Hero berserker-timed greatsword clips (11-15). Run headless:
#   blender -b gs_hero_work_fixed.blend --python gs_berserker_anim.py -- <outdir> [clips] [render]
# Builds a control rig on GS_Rig (sword controller holding both hands through arm IK, foot IK targets, keyed hips/spine),
# keys the clips from ArtSource/GSHero/berserker_timing.md, bakes them to plain FK actions, renders top-down check sheets,
# saves a copy of the .blend (copy=True, new name) and exports an FBX (armature + actions) for Unity.
import bpy, sys, os, math
from mathutils import Vector, Matrix, Quaternion, Euler

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = argv[0] if argv else "F:/ClaudeTemp/scratch/gsanim/out"
ONLY = argv[1].split(",") if len(argv) > 1 and argv[1] != "all" else None
RENDER = len(argv) > 2 and argv[2] == "render"
os.makedirs(OUT, exist_ok=True)

sc = bpy.data.scenes["GS_Char"]
if bpy.context.window: bpy.context.window.scene = sc
sc.render.fps = 30
rig = bpy.data.objects["GS_Rig"]; rig.hide_viewport = False; rig.hide_set(False)
sw = bpy.data.objects["GS_Sword"]
P = rig.pose.bones
def B(n): return P["mixamorig:" + n]

F, R, U = Vector((0, -1, 0)), Vector((-1, 0, 0)), Vector((0, 0, 1))   # character faces -Y, its right is -X

# ------------------------------------------------------------------ sword frame from the mesh
import numpy as np
mw = sw.matrix_world.copy()
vs = np.array([(mw @ v.co)[:] for v in sw.data.vertices])
c = vs.mean(0); A = vs - c
w, V = np.linalg.eigh(A.T @ A)
ax = Vector(V[:, 2]); thin = Vector(V[:, 0])
hand_rest = (rig.matrix_world @ B("RightHand").matrix).copy()
hp = hand_rest.translation
if (Vector(c) - hp).dot(ax) < 0: ax = -ax                     # blade dir: from the hand toward the tip
t_hand = (hp - Vector(c)).dot(ax)
grip = Vector(c) + ax * t_hand                                # hand projected on the blade axis
tip_len = float((A @ np.array(ax[:])).max()) - t_hand
pommel = Vector(c) + ax * float((A @ np.array(ax[:])).min())
edge = thin.cross(ax).normalized()
def frame(o, y, x):
    y = y.normalized(); x = (x - y * x.dot(y)).normalized(); z = x.cross(y)
    m = Matrix((x, y, z)).transposed().to_4x4(); m.translation = o; return m
S_rest = frame(grip, ax, edge)
print(f"sword: grip {grip}, tip length {tip_len:.2f}, handle to pommel {(grip - pommel).length:.2f}")

# ------------------------------------------------------------------ control rig
def empty(name, m=None, parent=None, bone=None):
    o = bpy.data.objects.get(name) or bpy.data.objects.new(name, None)
    if o.name not in sc.collection.objects: sc.collection.objects.link(o)
    o.empty_display_size = 0.1
    if parent:
        o.parent = parent
        if bone: o.parent_type = "BONE"; o.parent_bone = bone
    if m is not None:
        bpy.context.view_layer.update(); o.matrix_world = m
    return o

bpy.context.view_layer.update()
SC = empty("BZ_Sword", S_rest)
RH = empty("BZ_RHand", hand_rest, SC)
lh_rest = (rig.matrix_world @ B("LeftHand").matrix).copy()
# left hand: 16 cm down the handle, hand turned like the right one but mirrored across the blade flat
lm = S_rest @ (Matrix.Translation((0, -0.16, 0)) @ (S_rest.inverted() @ hand_rest))
lm = lm @ Matrix.Rotation(math.pi, 4, "Y")
LH = empty("BZ_LHand", lm, SC)
chest_bone = "mixamorig:Spine2"
PR = empty("BZ_PoleR", Matrix.Translation((-0.55, 0.55, 0.85)), rig, chest_bone)
PL = empty("BZ_PoleL", Matrix.Translation((0.55, 0.55, 0.85)), rig, chest_bone)
FL = empty("BZ_FootL", (rig.matrix_world @ B("LeftFoot").matrix).copy())
FR = empty("BZ_FootR", (rig.matrix_world @ B("RightFoot").matrix).copy())
# knee poles: keyed per key over the toes of each foot (ahead and up), so the knee always bends toward its own toes
# (1st pass: poles 1 m ahead of the hips; a lunging leg ran almost through them and the thigh flipped 180)
KL = empty("BZ_KneeL", Matrix.Translation((0.2, -1.0, 0.5)))
KR = empty("BZ_KneeR", Matrix.Translation((-0.2, -1.0, 0.5)))
FL_rest, FR_rest = FL.matrix_world.copy(), FR.matrix_world.copy()
def _toe(side):
    v = (rig.matrix_world @ B(side + "ToeBase").head) - (rig.matrix_world @ B(side + "Foot").head); v.z = 0
    return v.normalized()
TOE_L, TOE_R = _toe("Left"), _toe("Right")

def con(pb, typ, name, **kw):
    c = pb.constraints.get(name) or pb.constraints.new(typ); c.name = name
    for k, v in kw.items(): setattr(c, k, v)
    return c
con(B("RightForeArm"), "IK", "bzIK", target=RH, pole_target=PR, chain_count=2, pole_angle=math.radians(-90))
con(B("RightHand"), "COPY_ROTATION", "bzRot", target=RH)
con(B("LeftForeArm"), "IK", "bzIK", target=LH, pole_target=PL, chain_count=2, pole_angle=math.radians(-90))
con(B("LeftHand"), "COPY_ROTATION", "bzRot", target=LH)
# limb fix (11-15, Kido "칼질할 때마다 사지가 다 비틀어진다"): the 1st pass had the leg pole at -90, which solved with
# both thighs rolled ~175 deg about their own axis (knee hinge on a diagonal, feet rolled back -170): candy-wrapped hips.
# Probe (ArtSource/GSHero/limb_probe.txt, gs_berserker_probe.py): pole +90 with the knee locked to its X hinge gives thigh roll ~0, knees
# over the toes. Arms: pole -90 is right, but a straight T-pose chain never bent (forearm rotation 0 in every baked
# frame), so the elbows get a seed bend (+X) each key and the grip is pulled into reach (see resolve).
LEG_POLE = math.radians(90)
con(B("LeftLeg"), "IK", "bzIK", target=FL, pole_target=KL, chain_count=2, pole_angle=LEG_POLE)
con(B("LeftFoot"), "COPY_ROTATION", "bzRot", target=FL)
con(B("RightLeg"), "IK", "bzIK", target=FR, pole_target=KR, chain_count=2, pole_angle=LEG_POLE)
con(B("RightFoot"), "COPY_ROTATION", "bzRot", target=FR)
for n in ("LeftLeg", "RightLeg"): B(n).lock_ik_y = B(n).lock_ik_z = True
for n in ("LeftForeArm", "RightForeArm"): B(n).lock_ik_y = True     # no forearm roll from the IK (hand roll handled per key)
for pb in P: pb.rotation_mode = "XYZ"
LH_REL = S_rest.inverted() @ lm        # left hand in the sword frame (its roll about the handle is picked per key)
RH_REL = S_rest.inverted() @ hand_rest
REACH = 0.40                           # shoulder -> wrist target; the arm is 0.44 long (stylised short arms)

def visual_local(pb):
    m = pb.matrix; par = pb.parent; rest = pb.bone.matrix_local
    if par: loc = (par.bone.matrix_local.inverted() @ rest).inverted() @ (par.matrix.inverted() @ m)
    else: loc = rest.inverted() @ m
    q = loc.to_quaternion(); sw, t = q.to_swing_twist('Y')
    a = math.degrees(sw.angle)
    if a > 180: a = 360 - a
    return math.degrees(t), a

# ------------------------------------------------------------------ pose description -> keys
_lastq = None; _lastlq = None; _lastpole = None; _lastknee = {}; _lastfoot = {}
ANK = FL_rest.translation.z         # ankle height when the foot is flat on the ground
def sword_m(p, d, travel):
    d = Vector(d).normalized(); x = Vector(travel) if travel is not None else F
    if abs(x.normalized().dot(d)) > 0.95: x = U
    return frame(Vector(p), d, x)

HB = B("Hips").bone
def hips_loc(dz, tx, ty):
    # world offset (tx, ty forward/side, dz up) -> hips bone local location (1st pass used (0, dz, 0): local Y = up)
    wd = Vector((tx, ty, dz))
    ad = rig.matrix_world.inverted().to_3x3() @ wd
    lv = HB.matrix_local.to_3x3().inverted() @ ad
    s = HIPS_SCALE
    return Vector((lv.x * s, lv.y * s, lv.z * s))
_up = HB.matrix_local.to_3x3().inverted() @ (rig.matrix_world.inverted().to_3x3() @ U)
HIPS_SCALE = 1.0 / _up.length     # the 1st pass keyed dz straight in local units: keep that scale
print("hips local of world up:", _up, "scale", HIPS_SCALE)

def pose_body(k, f=None):
    """sets hips/spine/head from k (hips, spine, head, T); keys them when f is given"""
    dz, yaw, pitch, roll = k.get("hips", (0, 0, 0, 0))
    tx, ty = k.get("T", (0, 0))
    h = B("Hips"); h.location = hips_loc(dz, tx, ty)
    # v5: k["Y"] = whole-body yaw (deg, + = turn left / CCW from above) on top of the hips yaw; the head turns with it
    # (only the hips/spine yaw relative to the body is countered below)
    h.rotation_euler = (math.radians(pitch), math.radians(yaw + k.get("Y", 0.0)), math.radians(roll))
    syaw, spitch, sroll = k.get("spine", (0, 0, 0))
    for n, wgt in (("Spine", 0.3), ("Spine1", 0.35), ("Spine2", 0.35)):
        b = B(n); b.rotation_euler = (math.radians(spitch * wgt), math.radians(syaw * wgt), math.radians(sroll * wgt))
        if f is not None: b.keyframe_insert("rotation_euler", frame=f)
    hy, hpch = k.get("head", (0, 0))
    # the head keeps looking ahead (toward the cursor): undo part of the hips+spine pitch and yaw.
    # v2 check (11-15): the 1st version only undid 30% of the pitch on the Head bone alone, so at the deep v2 contacts the
    # head hung 43 deg forward (chest 60) and turned up to 59 deg -- the hat (100% Head weight) slid off over the shoulder,
    # and Unity's humanoid clamps the head turn at 40. Now the lean stays in the spine, the head lifts back to ~25 deg
    # down, the counter is spread Neck 40% / Head 60%, and the neck+head total stays under 42 deg (pitch first, yaw after).
    lean = pitch + spitch
    cp = min(36.0, 0.6 * lean) if lean > 0 else 0.3 * lean
    cy = 0.5 * (yaw + syaw)
    lim = 42.0
    if math.hypot(cp, cy) > lim: cy = math.copysign(math.sqrt(max(0.0, lim * lim - cp * cp)), cy)
    for n, wgt in (("Neck", 0.4), ("Head", 0.6)):
        b = B(n); b.rotation_euler = (math.radians(wgt * (hpch - cp)), math.radians(wgt * (hy - cy)), 0)
        if f is not None: b.keyframe_insert("rotation_euler", frame=f)
    if f is not None:
        h.keyframe_insert("location", frame=f); h.keyframe_insert("rotation_euler", frame=f)

def chest_m():
    bpy.context.view_layer.update()
    m = rig.matrix_world @ B("Spine2").matrix
    loc, rot, _ = m.decompose()
    return Matrix.Translation(loc) @ rot.to_matrix().to_4x4()

def chest_rot():
    """world rotation of the chest relative to its rest pose (directions keyed 'in the chest frame' go through this)"""
    bpy.context.view_layer.update()
    pb = B("Spine2"); W3 = rig.matrix_world.to_3x3()
    cur = (W3 @ pb.matrix.to_3x3()).normalized(); rest = (W3 @ pb.bone.matrix_local.to_3x3()).normalized()
    return cur @ rest.inverted()

def aim_bone(pb, d_world):
    """turn a pose bone (shortest swing about its head) so its Y axis points along d_world"""
    bpy.context.view_layer.update()
    d = (rig.matrix_world.to_3x3().inverted() @ d_world).normalized()
    M = pb.matrix.copy(); y = M.to_3x3().col[1].normalized()
    t = M.translation.copy()
    R = y.rotation_difference(d).to_matrix().to_4x4()
    pb.matrix = Matrix.Translation(t) @ R @ Matrix.Translation(-t) @ M
    bpy.context.view_layer.update()

def resolve(k):
    """v2: k['body0'] = the 1st-pass body values the arm/sword pose was keyed against. The grip and blade move rigidly
    with the chest from that body to the new (bigger) body, so the arms keep the same reach while the body lunges,
    leans and turns further. Stores P, D."""
    p, d = Vector(k["p"]), Vector(k["d"]).normalized()
    if "body0" in k:
        pose_body(k["body0"]); M0 = chest_m()
        pose_body(k); M1 = chest_m()
        T = M1 @ M0.inverted()
        p = T @ p; d = (d.lerp(T.to_3x3() @ d, k.get("follow", 0.35))).normalized()
    else:
        pose_body(k); bpy.context.view_layer.update()
    # limb fix: pull the grip in until both wrist targets are within reach of their shoulders (else the arms lock
    # straight and twist). Left arm only when it holds the handle.
    shR = rig.matrix_world @ B("RightArm").head; shL = rig.matrix_world @ B("LeftArm").head
    mid = (shR + shL) * 0.5 + Vector((0, 0, -0.15))
    twoh = k.get("lhand", 1.0) > 0.5
    reach = k.get("reach", REACH)
    for _ in range(40):
        Sm = sword_m(p, d, None)
        er = ((Sm @ RH_REL).translation - shR).length - reach
        el = ((Sm @ LH_REL).translation - shL).length - reach if twoh else -1
        ex = max(er, el)
        if ex <= 0.002: break
        p = p + (mid - p).normalized() * ex * 1.1
    k["pull"] = (Vector(k["p"]) - p).length if "body0" not in k else None
    if k.get("drag"):   # dragged blade: from the (pulled) grip straight back to a tip just above the floor
        d = drag_dir(p, k.get("drag_head"))
    # keep the tip out of the ground (a slam may touch it)
    tipz = p.z + d.z * tip_len
    if tipz < 0.06:
        need = (0.06 - p.z) / tip_len
        hz = math.sqrt(max(1e-4, 1 - need * need)); hv = Vector((d.x, d.y, 0)).normalized() * hz
        d = Vector((hv.x, hv.y, need)).normalized()
    k["P"], k["D"] = p, d

FLAT = 0.8    # v2: how far the blade rolls its flat toward the sky (top-down camera) instead of leading with the edge
def key_pose(f, k, prev=None, nxt=None):
    """k: dict with p (grip), d (blade dir), hips (dz, yaw, pitch, roll), spine (yaw, pitch, roll) spread over 3 bones,
    head (yaw, pitch), lf/rf (x, y, lift, yaw), lhand (IK influence 0..1), larm (euler deg for LeftArm when IK off),
    T (hips world offset x, y), body0 (see resolve), flat (0..1)"""
    # leading edge = direction the tip travels (from the neighbouring keys)
    tip = lambda kk: kk["P"] + kk["D"] * tip_len
    trav = None
    if prev and nxt: trav = tip(nxt) - tip(prev)
    elif nxt: trav = tip(nxt) - tip(k)
    elif prev: trav = tip(k) - tip(prev)
    if trav is not None and trav.length < 0.05: trav = k.get("edge")
    if k.get("edge") is not None: trav = Vector(k["edge"])
    # flat up: the blade's width lies horizontal (perpendicular to the blade), signed like the travel
    D = k["D"]; xf = U.cross(D)
    w = k.get("flat", FLAT)
    if xf.length > 0.25 and w > 0:
        xf.normalize()
        if trav is None: trav = xf
        tv = Vector(trav); tv = tv - D * tv.dot(D)
        if tv.length < 1e-3: tv = xf
        tv.normalize()
        if xf.dot(tv) < 0: xf = -xf
        trav = (tv * (1 - w) + xf * w)
    global _lastq, _lastpole, _lastlq
    m0 = sword_m(k["P"], D, trav)
    SC.rotation_mode = "QUATERNION"
    pose_body(k, f)
    for tgt, rest, key in ((FL, FL_rest, "lf"), (FR, FR_rest, "rf")):
        x, y, lift, fyaw = k[key]
        m = Matrix.Rotation(math.radians(fyaw), 4, "Z") @ Matrix(rest.to_3x3()).to_4x4()
        m.translation = Vector((x, y, ANK + lift)); tgt.rotation_mode = "QUATERNION"; tgt.matrix_world = m
        qf = tgt.rotation_quaternion.copy()
        if key in _lastfoot: qf.make_compatible(_lastfoot[key])
        tgt.rotation_quaternion = qf; _lastfoot[key] = qf.copy()
        tgt.keyframe_insert("location", frame=f); tgt.keyframe_insert("rotation_quaternion", frame=f)
    pose_body(k); bpy.context.view_layer.update()
    # knee poles: rotate a pole over the toes about the hip->ankle line and keep the one with the least thigh roll
    # (Unity's upper-leg twist muscle is +-60ish) while the knee still points over the toes, close to the last key
    global _lastknee
    for key, side, kp, tgt in (("lf", "Left", KL, FL), ("rf", "Right", KR, FR)):
        fyaw = k[key][3]
        toe = Matrix.Rotation(math.radians(fyaw), 3, "Z") @ (TOE_L if key == "lf" else TOE_R)
        hip = rig.matrix_world @ B(side + "UpLeg").head; ank = tgt.matrix_world.translation
        line = (ank - hip).normalized()
        base = ank + toe * 0.7 + Vector((0, 0, 0.6))
        bestk = None
        for ang in range(-75, 76, 15):
            R = Matrix.Rotation(math.radians(ang), 3, line)
            kp.location = hip + R @ (base - hip); bpy.context.view_layer.update()
            tt, _ = visual_local(B(side + "UpLeg")); tf, _ = visual_local(B(side + "Foot"))
            knee = rig.matrix_world @ B(side + "Leg").head
            kv = knee - hip; kv = kv - line * kv.dot(line); kv.z = 0
            fwd = kv.normalized().dot(toe) if kv.length > 1e-3 else 1
            cost = abs(tt) + 0.7 * abs(tf) + (80 if fwd < 0.2 else 0) + 0.5 * abs(ang - _lastknee.get(key, 0))
            if bestk is None or cost < bestk[0]: bestk = (cost, ang, tt, tf, fwd)
        kp.location = hip + Matrix.Rotation(math.radians(bestk[1]), 3, line) @ (base - hip)
        kp.keyframe_insert("location", frame=f); _lastknee[key] = bestk[1]
        k["_k" + key] = bestk
    li = k.get("lhand", 1.0)
    for n, cn in (("LeftForeArm", "bzIK"), ("LeftHand", "bzRot")):
        cc = B(n).constraints[cn]; cc.influence = li; cc.keyframe_insert("influence", frame=f)
    la = k.get("larm", (0, 0, 0))
    b = B("LeftArm"); b.rotation_euler = tuple(math.radians(v) for v in la); b.keyframe_insert("rotation_euler", frame=f)
    lf = k.get("lfore", (30, 0, 0) if li > 0.5 else (0, 0, 0))     # +X seed: the IK bends the elbow from here
    b = B("LeftForeArm"); b.rotation_euler = tuple(math.radians(v) for v in lf); b.keyframe_insert("rotation_euler", frame=f)
    if "lswing" in k and li < 0.5:
        # arm fix (11-15, Kido "왜 나루토처럼 양팔 펼치고 걸어"): the free left arm was keyed as LeftArm eulers (larm), whose
        # axes are rolled on this rig: the arm hung straight (elbow 0) and only ever swung BACK, up to 69 deg. Now it is
        # aimed in the chest's frame: upper arm down by the side swinging theta (+ = back), elbow bent toward the front.
        th, bend, out = k["lswing"]
        Rc = chest_rot()
        t = math.radians(th)
        up_d = (Rc @ Vector((out, math.sin(t), -math.cos(t)))).normalized()
        aim_bone(B("LeftArm"), up_d)
        fw = Rc @ Vector((-0.25, -1.0, 0.0)); fw = (fw - up_d * fw.dot(up_d)).normalized()
        b2 = math.radians(bend)
        aim_bone(B("LeftForeArm"), (up_d * math.cos(b2) + fw * math.sin(b2)).normalized())
        for n in ("LeftArm", "LeftForeArm"): B(n).keyframe_insert("rotation_euler", frame=f)
    b = B("RightForeArm"); b.rotation_euler = (math.radians(30), 0, 0); b.keyframe_insert("rotation_euler", frame=f)
    bpy.context.view_layer.update()
    # limb fix: right arm pole (elbow plane roll) picked near -90 for the least upper-arm + wrist roll; left hand roll
    # about the handle picked for the least wrist roll. Unity's humanoid has no hand-roll muscle (it lands in the
    # forearm twist, +-90), so big wrist rolls came out clamped / candy-wrapped.
    ikR = B("RightForeArm").constraints["bzIK"]
    # the blade is symmetric: rolled 180 about its axis it looks the same from above, so the roll (0/180 +-30) is also
    # picked for the least right-wrist roll, with a penalty for jumping away from the previous key
    best = None
    owner = k.get("_like", k)
    rolls = (0,) if "_choice" in owner else (0, 180, 30, -30, 150, 210)
    poles = (owner["_choice"][1],) if "_choice" in owner else range(-150, -29, 15)
    for roll in rolls:
        mm = m0 @ Matrix.Rotation(math.radians(roll), 4, "Y")
        if "_choice" in owner:      # shared pose (READY, clip ends): the very same sword rotation as the first time
            mm = owner["_choice"][3].to_matrix().to_4x4(); mm.translation = m0.translation
        q = mm.to_quaternion()
        if _lastq is not None: q.make_compatible(_lastq)
        SC.location = mm.translation; SC.rotation_quaternion = q
        dq = math.degrees(_lastq.rotation_difference(q).angle) if _lastq is not None else 0
        if dq > 180: dq = 360 - dq
        for a in poles:
            ikR.pole_angle = math.radians(a); bpy.context.view_layer.update()
            ta, _ = visual_local(B("RightArm")); th, _ = visual_local(B("RightHand"))
            el = rig.matrix_world @ B("RightForeArm").head; sh = rig.matrix_world @ B("RightArm").head
            cost = abs(th) + 0.6 * abs(ta) + 2.0 * max(0, abs(ta) - 80) + 2.0 * max(0, abs(th) - 70)                 + (40 if el.z > sh.z + 0.05 else 0) + 0.4 * min(abs(roll), abs(roll - 180))                 + 0.4 * dq + (0.7 * abs(a - _lastpole) if _lastpole is not None else 0)
            if best is None or cost < best[0]: best = (cost, a, th, ta, q.copy(), mm.translation.copy(), roll)
    SC.location = best[5]; SC.rotation_quaternion = best[4]; _lastq = best[4].copy()
    SC.keyframe_insert("location", frame=f); SC.keyframe_insert("rotation_quaternion", frame=f)
    ikR.pole_angle = math.radians(best[1]); ikR.keyframe_insert("pole_angle", frame=f); _lastpole = best[1]
    bpy.context.view_layer.update()
    k["_r"] = best
    if li > 0.5:
        SCw = SC.matrix_world.copy(); bestL = None
        for a in ((owner["_choice"][2],) if "_choice" in owner and owner["_choice"][2] is not None else range(0, 360, 20)):
            LH.matrix_world = SCw @ Matrix.Rotation(math.radians(a), 4, "Y") @ LH_REL
            bpy.context.view_layer.update()
            ta, _ = visual_local(B("LeftArm")); th, _ = visual_local(B("LeftHand"))
            cost = abs(th) + 0.6 * abs(ta) + 2.0 * max(0, abs(ta) - 80)
            if _lastlq is not None:
                dl = math.degrees(_lastlq.rotation_difference(LH.matrix_basis.to_quaternion()).angle); dl = 360 - dl if dl > 180 else dl
                cost += 0.2 * dl
            if bestL is None or cost < bestL[0]: bestL = (cost, a, th, ta)
        if k.get("_la") is not None and abs(((bestL[1] - k["_la"] + 180) % 360) - 180) > 100: pass
        LH.matrix_world = SCw @ Matrix.Rotation(math.radians(bestL[1]), 4, "Y") @ LH_REL
        LH.rotation_mode = "QUATERNION"
        q = LH.rotation_quaternion.copy()
        if _lastlq is not None: q.make_compatible(_lastlq)
        _lastlq = q; LH.rotation_quaternion = q
        LH.keyframe_insert("location", frame=f); LH.keyframe_insert("rotation_quaternion", frame=f)
        k["_l"] = bestL
    if "_choice" not in owner:
        owner["_choice"] = (best[6], best[1], k["_l"][1] if li > 0.5 else None, best[4].copy())
    bpy.context.view_layer.update()
    print(f"  key f{f}: roll {best[6]} R pole {best[1]} wrist {best[2]:+.0f} arm {best[3]:+.0f}" + (f" | L roll {k['_l'][1]} wrist {k['_l'][2]:+.0f} arm {k['_l'][3]:+.0f}" if li > 0.5 else "") + (f" | pulled {k['pull']:.2f}" if k.get('pull') else "") + f" | knees L {k['_klf'][1]} tw{k['_klf'][2]:+.0f} ft{k['_klf'][3]:+.0f} R {k['_krf'][1]} tw{k['_krf'][2]:+.0f} ft{k['_krf'][3]:+.0f}")

def N(*v): return tuple(Vector(v).normalized())

# stances (feet: x, y, lift, yaw deg) -- left foot forward, right foot back, like the reference
ST = dict(lf=(0.17, -0.16, 0, 15), rf=(-0.20, 0.16, 0, -20))
READY = dict(p=(-0.16, -0.22, 1.08), d=N(-0.45, 0.55, 0.70), hips=(-0.05, 12, 6, 0), spine=(4, 8, 0), **ST)
# drag (11-15 arm fix, Kido "버서커처럼 칼 바닥에 끌면서 걸어야지", ref berserker_walk_timing.md): idle and run carry the
# sword in the right hand only, blade trailing straight back from beside the right hip, tip on the floor ~1.2 m behind;
# body leaning forward and turned a little toward the sword side (right shoulder back); left arm free by the side.
TIP_Z = 0.06
def drag_dir(p, head=None):
    h = Vector(head if head else (-0.12, 1.0, 0.0)); h.z = 0; h.normalize()
    s = max(-0.95, min(0.95, (TIP_Z - Vector(p).z) / tip_len))
    return Vector((h.x * math.sqrt(1 - s * s), h.y * math.sqrt(1 - s * s), s)).normalized()
def drag_pose(hips, spine, lsw, dz=0.0, **kw):
    p = (-0.27, 0.10, 0.86 + dz)
    k = dict(p=p, d=tuple(drag_dir(p)), hips=hips, spine=spine, lhand=0.0, larm=(0, 0, 0), lfore=(0, 0, 0),
             lswing=lsw, drag=True, flat=0.0, edge=(0, 0, -1), **ST)
    k.update(kw); return k
DRAG_IDLE = drag_pose((-0.10, -8, 12, 0), (-12, 10, 0), (-10, 35, 0.08))   # lswing theta is in the chest frame: minus the forward lean
if os.environ.get("BZ_PROBE"):    exec(open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "gs_berserker_probe.py")).read()); sys.exit(0)

CLIPS = {}
# hit 1 -- forehand sweep: blade back behind the right shoulder (2 f), sweeps right -> front (contact f5, 1 f hold) -> up over the left shoulder
CLIPS["BZ_Slash1"] = (16, 5, [
    (0, READY),
    (2, dict(p=(-0.32, 0.06, 1.18), d=N(-0.35, 0.92, 0.08), hips=(-0.07, -38, 8, 0), spine=(-28, 10, 0), lf=(0.17, -0.16, 0.06, 15), rf=(-0.20, 0.16, 0, -25))),
    (3, dict(p=(-0.36, -0.12, 1.06), d=N(-1.0, -0.05, -0.05), hips=(-0.09, -22, 12, 0), spine=(-14, 12, 0), lf=(0.17, -0.30, 0.08, 15), rf=(-0.20, 0.16, 0, -25))),
    (4, dict(p=(-0.18, -0.36, 1.00), d=N(-0.72, -0.70, -0.04), hips=(-0.11, 0, 14, 0), spine=(2, 14, 0), lf=(0.18, -0.38, 0, 10), rf=(-0.20, 0.16, 0, -20))),
    (5, dict(p=(0.02, -0.44, 0.98), d=N(0.08, -1.0, -0.06), hips=(-0.12, 22, 16, 0), spine=(16, 14, 0), lf=(0.18, -0.38, 0, 10), rf=(-0.20, 0.18, 0, -10))),
    (6, dict(p=(0.06, -0.43, 0.99), d=N(0.18, -0.98, -0.04), hips=(-0.12, 24, 16, 0), spine=(18, 14, 0), lf=(0.18, -0.38, 0, 10), rf=(-0.20, 0.18, 0, -10))),
    (8, dict(p=(0.26, -0.18, 1.22), d=N(0.62, 0.25, 0.74), hips=(-0.09, 40, 10, 0), spine=(24, 8, 0), lf=(0.18, -0.38, 0, 10), rf=(-0.20, 0.16, 0, 0))),
    (12, dict(p=(0.22, -0.08, 1.20), d=N(0.46, 0.40, 0.79), hips=(-0.07, 32, 8, 0), spine=(18, 6, 0), lf=(0.18, -0.34, 0, 10), rf=(-0.20, 0.16, 0, 0))),
    (16, dict(p=(0.21, -0.05, 1.21), d=N(0.45, 0.42, 0.79), hips=(-0.06, 30, 6, 0), spine=(16, 6, 0), lf=(0.18, -0.32, 0, 10), rf=(-0.20, 0.16, 0, 0))),
])
H1END = CLIPS["BZ_Slash1"][2][-1][1]
# hit 2 -- backhand diagonal: blade lifted up-left behind the head (2 f), over the top onto the front-right (contact f5), down to the right
CLIPS["BZ_Slash2"] = (20, 5, [
    (0, H1END),
    (2, dict(p=(0.20, 0.04, 1.46), d=N(0.48, 0.52, 0.70), hips=(-0.05, 44, 2, 0), spine=(26, -4, 0), lf=(0.18, -0.32, 0, 10), rf=(-0.20, 0.16, 0.05, 0))),
    (3, dict(p=(0.06, -0.24, 1.46), d=N(0.20, -0.58, 0.79), hips=(-0.07, 24, 8, 0), spine=(14, 4, 0), lf=(0.18, -0.32, 0, 10), rf=(-0.24, 0.10, 0.06, -10))),
    (4, dict(p=(-0.04, -0.44, 1.26), d=N(-0.30, -0.90, 0.22), hips=(-0.10, 0, 16, 0), spine=(0, 14, 0), lf=(0.18, -0.32, 0, 10), rf=(-0.26, 0.06, 0, -20))),
    (5, dict(p=(-0.12, -0.46, 1.00), d=N(-0.55, -0.75, -0.36), hips=(-0.13, -20, 20, 0), spine=(-14, 16, 0), lf=(0.18, -0.32, 0, 10), rf=(-0.26, 0.06, 0, -25))),
    (6, dict(p=(-0.15, -0.44, 0.98), d=N(-0.62, -0.68, -0.38), hips=(-0.13, -22, 20, 0), spine=(-16, 16, 0), lf=(0.18, -0.32, 0, 10), rf=(-0.26, 0.06, 0, -25))),
    (9, dict(p=(-0.30, -0.16, 0.86), d=N(-0.62, 0.50, -0.36), hips=(-0.11, -44, 14, 0), spine=(-26, 12, 0), lf=(0.18, -0.30, 0, 10), rf=(-0.26, 0.06, 0, -25))),
    (14, dict(p=(-0.32, -0.06, 0.90), d=N(-0.50, 0.70, -0.20), hips=(-0.08, -36, 10, 0), spine=(-20, 10, 0), lf=(0.18, -0.28, 0, 10), rf=(-0.24, 0.10, 0, -25))),
    (20, dict(p=(-0.32, -0.04, 0.92), d=N(-0.48, 0.72, -0.18), hips=(-0.07, -34, 8, 0), spine=(-18, 8, 0), lf=(0.18, -0.26, 0, 10), rf=(-0.24, 0.12, 0, -25))),
])
H2END = CLIPS["BZ_Slash2"][2][-1][1]
# hit 3 -- heavy overhead slam: lift (7 f) with the body leaning back, drop (2 f) to the ground in front (contact f9, 3 f hold), recover
SLAM = dict(p=(0.04, -0.52, 0.86), d=N(0.30, -0.70, -0.65), hips=(-0.22, 18, 30, 0), spine=(12, 18, 0), lf=(0.20, -0.46, 0, 10), rf=(-0.24, 0.28, 0, -25))   # diagonal: right-high -> front-left ground (reads from top-down)
CLIPS["BZ_Slash3"] = (36, 9, [
    (0, H2END),
    (4, dict(p=(-0.25, 0.00, 1.45), d=N(-0.55, 0.45, 0.70), hips=(-0.03, -30, -4, 0), spine=(-18, -6, 0), lf=(0.18, -0.26, 0.04, 10), rf=(-0.24, 0.16, 0, -25))),
    (7, dict(p=(-0.22, 0.06, 1.68), d=N(-0.75, 0.35, 0.56), hips=(0.0, -40, -8, 0), spine=(-25, -10, 0), lf=(0.20, -0.40, 0.10, 10), rf=(-0.24, 0.24, 0, -25))),
    (8, dict(p=(-0.12, -0.32, 1.55), d=N(-0.70, -0.50, 0.50), hips=(-0.08, -10, 10, 0), spine=(-6, 6, 0), lf=(0.20, -0.46, 0.03, 10), rf=(-0.24, 0.28, 0, -25))),
    (9, SLAM),
    (12, dict(SLAM, p=(0.0, -0.53, 0.85))),
    (16, dict(SLAM, p=(0.0, -0.50, 0.88), hips=(-0.20, 6, 26, 0))),
    (28, dict(READY, p=(-0.10, -0.30, 1.0), d=N(-0.15, 0.10, 0.98), hips=(-0.08, 8, 10, 0), lf=(0.18, -0.24, 0, 12), rf=(-0.21, 0.18, 0, -20))),
    (36, READY),
])
# dodge -- low forward dash (2 f crouch, 9 f travel, recover): sword trails low behind in the right hand, left arm thrown back
TRAIL = dict(p=(-0.34, 0.20, 0.72), d=N(-0.30, 0.90, -0.30), lhand=0.0, larm=(0, 0, 0), lfore=(0, 0, 0), lswing=(0, 30, 0.1))   # arm fix: left arm aimed (was larm (-35,0,-60), rolled axes)
CLIPS["BZ_Dash"] = (30, 0, [
    (0, DRAG_IDLE),
    (2, dict(TRAIL, hips=(-0.16, 6, 26, 0), spine=(0, 10, 0), lf=(0.18, -0.24, 0, 10), rf=(-0.20, 0.22, 0.04, -15))),
    (5, dict(TRAIL, hips=(-0.20, 4, 38, 0), spine=(0, 8, 0), lf=(0.18, -0.48, 0.16, 5), rf=(-0.18, 0.46, 0.10, -10))),
    (8, dict(TRAIL, hips=(-0.20, 4, 40, 0), spine=(0, 8, 0), lf=(0.18, -0.52, 0.10, 5), rf=(-0.18, 0.40, 0.18, -10))),
    (11, dict(TRAIL, hips=(-0.26, 6, 34, 0), spine=(0, 10, 0), lf=(0.19, -0.44, 0, 8), rf=(-0.22, 0.34, 0, -20))),
    (22, dict(TRAIL, hips=(-0.22, 8, 28, 0), spine=(2, 10, 0), lf=(0.19, -0.36, 0, 10), rf=(-0.22, 0.28, 0, -20))),
    (30, DRAG_IDLE),
])

# ------------------------------------------------------------------ v2 (11-15 2nd pass): the body does the work
# Kido: "허우적거리고 공격한다는 느낌이 전혀 없다" -- the 1st pass read as standing still and waving the arms. Same frame
# timing; every hit now steps long into the target (front foot ~0.7 m out, hips forward), knees bend deep, the hips lead
# the turn (wider yaw range), the chest folds over the blade at contact, and hit 3 hops up and drives down. The arms/sword
# of the 1st pass ride rigidly on the chest (resolve), so reach and grip stay as they were.
def V2(old, hips, spine, T, lf, rf, **kw):
    k = dict(old)
    k["body0"] = dict(hips=old.get("hips", (0, 0, 0, 0)), spine=old.get("spine", (0, 0, 0)))
    k.update(hips=hips, spine=spine, T=T, lf=lf, rf=rf); k.update(kw)
    return k
K1 = dict(CLIPS["BZ_Slash1"][2]); K2 = dict(CLIPS["BZ_Slash2"][2]); K3 = dict(CLIPS["BZ_Slash3"][2])
H1END_V2 = V2(K1[16], (-0.12, 36, 10, 0), (20, 10, 0), (0, -0.10), (0.19, -0.50, 0, 10), (-0.21, 0.20, 0, -5))
CLIPS["BZ_Slash1"] = (16, 5, [
    (0, DRAG_IDLE),
    (2, V2(K1[2], (-0.12, -58, 10, 0), (-40, 14, 0), (0.02, 0.06), (0.17, -0.22, 0.12, -15), (-0.22, 0.18, 0, -25))),
    (3, V2(K1[3], (-0.20, -30, 20, 0), (-20, 20, 0), (0.0, -0.12), (0.18, -0.50, 0.10, 12), (-0.22, 0.20, 0, -25))),
    (4, V2(K1[4], (-0.27, 2, 26, 0), (4, 24, 0), (0.0, -0.24), (0.19, -0.70, 0, 10), (-0.22, 0.22, 0, -15))),
    (5, V2(K1[5], (-0.31, 32, 32, 0), (24, 30, 0), (0.02, -0.28), (0.19, -0.72, 0, 10), (-0.22, 0.24, 0, -5))),
    (6, V2(K1[6], (-0.31, 34, 32, 0), (26, 30, 0), (0.02, -0.28), (0.19, -0.72, 0, 10), (-0.22, 0.24, 0, -5))),
    (8, V2(K1[8], (-0.24, 56, 18, 0), (34, 14, 0), (0.02, -0.24), (0.19, -0.70, 0, 10), (-0.22, 0.22, 0, 0))),
    (12, V2(K1[12], (-0.16, 42, 13, 0), (24, 11, 0), (0.0, -0.15), (0.19, -0.58, 0, 10), (-0.21, 0.21, 0, -5))),
    (16, H1END_V2),
])
H2END_V2 = V2(K2[20], (-0.13, -38, 10, 0), (-22, 9, 0), (0, -0.14), (0.20, -0.56, 0, 10), (-0.24, 0.16, 0, -25))
CLIPS["BZ_Slash2"] = (20, 5, [
    (0, H1END_V2),
    (2, V2(K2[2], (-0.06, 54, -4, 0), (36, -8, 0), (0, -0.06), (0.19, -0.50, 0, 10), (-0.21, 0.20, 0.06, 0))),
    (3, V2(K2[3], (-0.17, 26, 14, 0), (14, 12, 0), (0, -0.16), (0.19, -0.54, 0.12, 10), (-0.24, 0.14, 0, -10))),
    (4, V2(K2[4], (-0.27, 0, 26, 0), (0, 22, 0), (0, -0.26), (0.20, -0.74, 0.02, 10), (-0.25, 0.12, 0, -20))),
    (5, V2(K2[5], (-0.32, -26, 34, 0), (-18, 30, 0), (0, -0.30), (0.20, -0.78, 0, 10), (-0.25, 0.12, 0, -25))),
    (6, V2(K2[6], (-0.32, -28, 34, 0), (-20, 30, 0), (0, -0.30), (0.20, -0.78, 0, 10), (-0.25, 0.12, 0, -25))),
    (9, V2(K2[9], (-0.25, -54, 22, 0), (-34, 18, 0), (0, -0.26), (0.20, -0.76, 0, 10), (-0.25, 0.14, 0, -25))),
    (14, V2(K2[14], (-0.17, -42, 14, 0), (-26, 12, 0), (0, -0.18), (0.20, -0.62, 0, 10), (-0.24, 0.16, 0, -25))),
    (20, H2END_V2),
])
SLAM2 = V2(SLAM, (-0.36, 16, 38, 0), (12, 26, 0), (0, -0.44), (0.24, -0.82, 0, 10), (-0.27, 0.20, 0, -25))
CLIPS["BZ_Slash3"] = (36, 9, [
    (0, H2END_V2),
    (4, V2(K3[4], (-0.22, -34, 6, 0), (-20, -2, 0), (0, -0.10), (0.20, -0.54, 0, 10), (-0.24, 0.16, 0, -25))),   # load: knees bent
    (6, V2(K3[7], (0.24, -40, -8, 0), (-26, -12, 0), (0, -0.24), (0.20, -0.58, 0.34, 10), (-0.24, 0.02, 0.44, -25))),  # off the ground
    (7, V2(K3[7], (0.32, -44, -12, 0), (-28, -16, 0), (0, -0.34), (0.20, -0.68, 0.40, 10), (-0.24, -0.06, 0.50, -25))), # apex, blade high
    (8, V2(K3[8], (0.04, -12, 16, 0), (-6, 12, 0), (0, -0.42), (0.22, -0.80, 0.12, 10), (-0.25, 0.10, 0.18, -25))),
    (9, SLAM2),
    (12, dict(SLAM2, p=(0.0, -0.53, 0.85))),
    (16, dict(SLAM2, p=(0.0, -0.50, 0.88), hips=(-0.32, 6, 32, 0), body0=dict(hips=(-0.20, 6, 26, 0), spine=SLAM["spine"]))),
    (28, V2(K3[28], (-0.12, 8, 12, 0), (4, 9, 0), (0, -0.12), (0.18, -0.30, 0, 12), (-0.21, 0.18, 0, -20))),
    (36, DRAG_IDLE),
])
# ------------------------------------------------------------------ v3 (11-15 sweep fix)
# Kido: "칼질할 때 캐릭터가 계속 앞뒤로 움직인다" + "횡으로 크게 베는데 지금은 삼각형으로 공격". v2 moved the hips 0.28 m out
# and back inside each clip while Unity also lunged the root, so the body went out and back every hit and the feet slid.
# v3: the step is root motion. ROOT[clip][f] = how far the body has advanced (m, along the aim) at frame f; it only ever
# grows. In the clip the hips stay over the root (T = 0) and the feet are keyed EVERY frame at (world plant - ROOT[f]),
# so a planted foot stays put in the world while Unity moves the root by the same table (GreatswordCombat.RootPath).
# Every clip starts and ends in the ST stance (front foot 0.16 ahead, back 0.16 behind): front foot steps out during the
# wind-up/strike, the back foot steps up right after contact (done by the cancel frame).
# Hits 1-2: flat horizontal two-hand sweeps, blade at waist height on a circle round the body, keyed every frame of the
# arc: hit 1 from ~120 deg right-back through the front (contact) to ~115 deg left; hit 2 the return sweep that runs on
# round behind the right side into the drag. Hips/chest yaw lead, the blade lags then whips through.
def bdir(th, z=0.0):
    a = math.radians(th); v = Vector((math.sin(a), -math.cos(a), 0.0)); v.z = z; return tuple(v.normalized())
def sweep_key(th, hips, spine, zg=1.0, rad=0.36, dz=-0.10, lag=0.0, **kw):
    a = math.radians(th + lag * 0.5)
    p = (math.sin(a) * rad, -math.cos(a) * rad, zg)
    k = dict(p=p, d=bdir(th + lag, dz), hips=hips, spine=spine, T=(0, 0)); k.update(kw); return k
ROOT = {
    "BZ_Slash1": [0, 0, 0, .10, .25, .35, .40, .43, .45, .46, .47, .48, .485, .49, .495, .50, .50],
    "BZ_Slash2": [0, 0, 0, .09, .22, .32, .37, .40, .42, .43, .44, .445, .45, .45, .45, .45, .45, .45, .45, .45, .45],
    "BZ_Slash3": [0, 0, 0, 0, 0, .04, .14, .28, .44, .52, .54, .545, .55, .553, .556, .558, .56,
                  .565, .57, .58, .59, .60, .61, .62, .63, .64, .645, .65, .655, .66, .67, .68, .69, .695, .70, .70, .70],
}
# foot steps: (frame from, frame to, s from, s to, lift) in world metres along the aim; planted otherwise
STEPS = {
    "BZ_Slash1": dict(lf=[(1, 4, .16, .66, .10)], rf=[(6, 9, -.16, .34, .08)]),
    "BZ_Slash2": dict(lf=[(1, 4, .16, .61, .10)], rf=[(6, 9, -.16, .29, .08)]),
    "BZ_Slash3": dict(lf=[(5, 9, .16, .86, .40)], rf=[(5, 9, -.16, .04, .45), (22, 28, .04, .54, .08)]),
}
FOOTX = dict(lf=(0.18, 10), rf=(-0.22, -20))
def foot_world(clip, side, f):
    ev = STEPS[clip][side]; s = ev[0][2]; lift = 0.0
    for a, b, s0, s1, lh in ev:
        if f <= a: s = s0; break
        if f < b:
            u = (f - a) / (b - a); e = u * u * (3 - 2 * u)
            s = s0 + (s1 - s0) * e; lift = lh * math.sin(math.pi * u); break
        s = s1
    return s, lift
def feet_at(clip, f):
    r = ROOT[clip][f]; out = {}
    for side in ("lf", "rf"):
        s, lift = foot_world(clip, side, f); x, yaw = FOOTX[side]
        out[side] = (x, -(s - r), lift, yaw)
    return out
def with_feet(clip, f, k):
    k2 = dict(k); k2.update(feet_at(clip, f)); k2["T"] = (0, 0)
    k2["_like"] = k.get("_like", k)     # shared poses (DRAG_IDLE, clip ends) keep one cached sword roll / poles
    return k2

S1 = [
    (2, sweep_key(-122, (-0.14, -48, 10, 0), (-36, 12, 0), zg=1.02)),
    (3, sweep_key(-80, (-0.20, -18, 16, 0), (-26, 16, 0), lag=-12)),
    (4, sweep_key(-38, (-0.25, 12, 20, 0), (-8, 20, 0), lag=-14)),
    (5, sweep_key(4, (-0.28, 30, 22, 0), (10, 22, 0), rad=0.40, lag=-10)),
    (6, sweep_key(42, (-0.28, 38, 20, 0), (22, 20, 0), rad=0.40, lag=-4)),
    (7, sweep_key(82, (-0.26, 44, 16, 0), (32, 16, 0), lag=4)),
    (8, sweep_key(112, (-0.24, 48, 14, 0), (36, 14, 0), lag=6)),
    (10, sweep_key(120, (-0.20, 48, 12, 0), (36, 12, 0), zg=1.06, dz=0.0)),
]
H1END_V3 = sweep_key(108, (-0.14, 44, 10, 0), (32, 10, 0), zg=1.12, dz=0.12)
CLIPS["BZ_Slash1"] = (16, 5, [(0, DRAG_IDLE)] + S1 + [(16, H1END_V3)])
S2 = [
    (2, sweep_key(124, (-0.14, 50, 10, 0), (36, 12, 0), zg=1.02)),
    (3, sweep_key(80, (-0.20, 20, 16, 0), (26, 16, 0), lag=12)),
    (4, sweep_key(38, (-0.25, -10, 20, 0), (8, 20, 0), lag=14)),
    (5, sweep_key(-4, (-0.28, -30, 22, 0), (-10, 22, 0), rad=0.40, lag=10)),
    (6, sweep_key(-44, (-0.28, -38, 20, 0), (-22, 20, 0), rad=0.40, lag=4)),
    (7, sweep_key(-84, (-0.26, -44, 16, 0), (-32, 16, 0), lag=-4)),
    (8, sweep_key(-114, (-0.24, -48, 14, 0), (-36, 14, 0), lag=-6)),
    (10, sweep_key(-135, (-0.22, -50, 12, 0), (-36, 12, 0), zg=0.98, dz=-0.15)),
    (13, sweep_key(-150, (-0.18, -42, 12, 0), (-28, 12, 0), zg=0.92, dz=-0.30)),
]
H2END_V3 = dict(p=(-0.30, 0.02, 0.92), d=bdir(-155, -0.30), hips=(-0.12, -36, 10, 0), spine=(-24, 10, 0), T=(0, 0))
CLIPS["BZ_Slash2"] = (20, 5, [(0, H1END_V3)] + S2 + [(20, H2END_V3)])
# hit 3: the v2 slam keys (arms/sword ride on the chest via body0) with the body over the root and the hop as root motion
CLIPS["BZ_Slash3"] = (36, 9, [(0, H2END_V3)] + [(f, k) for f, k in CLIPS["BZ_Slash3"][2] if f != 0 and f != 36] + [(36, DRAG_IDLE)])
# ------------------------------------------------------------------ v4 hit 3 = 360 spin (11-15, Kido: "3타 위에서 아래로
#찍는거 지금처럼 하지말고 한바퀴 돌면서 360도 베기"). Ref A f75-82: hit 2's follow-through whirls a full turn in 8 frames with
# the blade flat. Hit 2 ends with the blade back-right (clockwise sweep), so the spin goes on clockwise from there.
# The revolution itself is NOT in the clip: GreatswordCombat turns the visual 0 -> 360 over clip f3-f11 about the planted
# left foot (SpinYaw / SpinPivot), so the legs never wind up and the tip stays on a round circle (sweep_key radius in the
# body frame). The clip keys a 2 f coil (hips turned against the spin, blade up to the right side), then hips/chest lead
# the turn while the blade lags out to the right (bearing ~100), whips round to the back-right over f9-f11, a 2 f hold,
# and recovers into the one-hand drag. Right foot lifted while the body turns on the left foot.
S3 = [
    (1, sweep_key(-150, (-0.14, -12, 12, 0), (-10, 12, 0), zg=0.98, dz=-0.05)),
    (2, sweep_key(-128, (-0.16, 14, 14, 0), (8, 14, 0), zg=1.00, dz=-0.03, rad=0.42)),
    (3, sweep_key(-118, (-0.18, -6, 14, 0), (-4, 14, 0), zg=1.00, dz=-0.03, rad=0.44)),
    (4, sweep_key(-108, (-0.18, -22, 14, 0), (-14, 14, 0), zg=1.00, dz=-0.03, rad=0.44)),
    (5, sweep_key(-102, (-0.18, -28, 14, 0), (-18, 14, 0), zg=1.00, dz=-0.03, rad=0.44)),
    (6, sweep_key(-100, (-0.18, -28, 14, 0), (-18, 14, 0), zg=1.00, dz=-0.03, rad=0.44)),
    (7, sweep_key(-100, (-0.18, -28, 14, 0), (-18, 14, 0), zg=1.00, dz=-0.03, rad=0.44)),
    (8, sweep_key(-104, (-0.18, -30, 14, 0), (-20, 14, 0), zg=1.00, dz=-0.03, rad=0.44)),
    (9, sweep_key(-116, (-0.18, -32, 14, 0), (-22, 14, 0), zg=1.00, dz=-0.04, rad=0.44)),
    (10, sweep_key(-136, (-0.18, -34, 13, 0), (-24, 13, 0), zg=0.99, dz=-0.06, rad=0.42)),
    (11, sweep_key(-152, (-0.17, -36, 12, 0), (-24, 12, 0), zg=0.97, dz=-0.10, rad=0.40)),
    (13, sweep_key(-156, (-0.16, -36, 11, 0), (-24, 11, 0), zg=0.95, dz=-0.14)),
]
CLIPS["BZ_Slash3"] = (24, 7, [(0, H2END_V3)] + S3 + [(24, DRAG_IDLE)])
ROOT["BZ_Slash3"] = [0, 0, .04, .10] + [.10] * 21
STEPS["BZ_Slash3"] = dict(lf=[(1, 3, .16, .26, .06)], rf=[(3, 11, -.16, -.06, .12)])
for _n in ROOT:
    _L, _h, _keys = CLIPS[_n]
    CLIPS[_n] = (_L, _h, [(f, with_feet(_n, f, k)) for f, k in _keys])

# ------------------------------------------------------------------ v5 wide whole-body sweeps (11-16)
# Kido (11-16, after the v3/v4 build): "동작이 너무 좁고 범위도 작고 이상해 ... 넓게 베라고 수십번 말했는데",
# "마지막 3타 돌면서 베라고 했지, 위로 올라가는 동작이 왜 있어", "좌우 돌면서 전신 3가지", "칼 위로 올라가는 동작 만들지마".
# v3/v4 turned only the hips/chest over planted feet with bent arms (tip ~1.0-1.5 m from the body) and hit 3's turn was a
# Unity-side yaw. v5 keys EVERY attack frame from per-channel curves (monotone cubic through the control rows below):
#  Y  whole-body yaw (deg, + = turn left / CCW from above; 0 = facing the cursor), the head turns with it
#  th blade bearing (world, same convention), bp blade pitch (deg, kept flat), gb grip bearing relative to the body,
#  rg grip radius asked for (resolve() pulls it in until both wrists reach: REACH 0.43 of the 0.44 arm = arms straight),
#  zg grip height, dz/hp hips drop / forward lean, sy/sp chest yaw / pitch on top of the hips, lean = hips shift (m)
#  toward the grip.
# Hit 1: coil right (blade pulled back flat to the right side), turn ~175 deg left while the blade sweeps ~275 deg
# right-back -> front (contact f5) -> left-back, then turn back to face the cursor with the blade out to the left.
# Hit 2: the mirror. Hit 3: a 380 deg clockwise spin pivoting on the left foot (keyed in the clip; the old
# GreatswordCombat.SpinYaw is off), blade flat and out to the front-right the whole turn, then settle into the ST stance.
# Feet: hit 1's right foot steps up level with the left (square stance, root advances 0.18 m); planted feet turn in place
# (yaw follows 60% of the body turn, the hips take the rest); in the spin the left foot turns in place under the pivot
# and the right foot swings round off the ground. No key ever raises the blade: bp is -3..-6 deg and zg ~ chest height.
WIDE = ("BZ_Slash1", "BZ_Slash2", "BZ_Slash3")
WIDE_REACH = 0.43
CH = ("Y", "th", "gb", "rg", "zg", "bp", "dz", "hp", "sy", "sp", "lean")
def _rows(*rows):
    """rows: (frame, dict); missing channels carry over from the row before"""
    out = []; cur = {}
    for f, d in rows:
        cur = dict(cur); cur.update(d); out.append((f, cur))
    return out
def _pchip(xs, ys, x):
    if x <= xs[0]: return ys[0]
    if x >= xs[-1]: return ys[-1]
    n = len(xs); i = max(j for j in range(n - 1) if xs[j] <= x)
    sec = [(ys[j + 1] - ys[j]) / (xs[j + 1] - xs[j]) for j in range(n - 1)]
    def tan(j):
        if j == 0: return sec[0]
        if j == n - 1: return sec[-1]
        a, b = sec[j - 1], sec[j]
        if a * b <= 0: return 0.0
        w1 = 2 * (xs[j + 1] - xs[j]) + (xs[j] - xs[j - 1]); w2 = (xs[j + 1] - xs[j]) + 2 * (xs[j] - xs[j - 1])
        return (w1 + w2) / (w1 / a + w2 / b)
    h = xs[i + 1] - xs[i]; u = (x - xs[i]) / h
    m0, m1 = tan(i) * h, tan(i + 1) * h
    u2, u3 = u * u, u * u * u
    return (2*u3 - 3*u2 + 1) * ys[i] + (u3 - 2*u2 + u) * m0 + (-2*u3 + 3*u2) * ys[i + 1] + (u3 - u2) * m1
def sample(rows, f):
    xs = [a for a, _ in rows]
    return {c: _pchip(xs, [d[c] for _, d in rows], f) for c in CH}
def ease(u): u = max(0.0, min(1.0, u)); return u * u * (3 - 2 * u)
def hv(deg, r): a = math.radians(deg); return Vector((math.sin(a) * r, -math.cos(a) * r))

# v5b (Kido 10:25 "1, 2타 너무 많이 돌릴 필요 없이 ... 레퍼런스 버서커 평타처럼", "빠르기도 너무 빠르고"): body turn and sweep
# as read from berserker_ref_sheet.png (A55-63): blade ~210 deg in 3+3 f, body ~115 deg, no extra whirl. Spacing as the
# reference: contact to contact 15 f (hit 1 cancel f10 + hit 2 contact f5) and 21 f (hit 2 cancel f18 + hit 3 contact f8).
W1 = _rows(
    (0,  dict(Y=-8,  th=-150, gb=-62, rg=0.40, zg=1.02, bp=-6, dz=-0.10, hp=10, sy=-8,  sp=8,  lean=0.0)),
    (2,  dict(Y=-55, th=-125, gb=-40, rg=0.50, zg=1.10, bp=-5, dz=-0.16, hp=12, sy=-20, sp=10, lean=0.0)),
    (3,  dict(Y=-32, th=-78,  gb=-18, rg=0.90, zg=1.14, bp=-4, dz=-0.20, hp=16, sy=-12, sp=12, lean=0.05)),
    (4,  dict(Y=-6,  th=-32,  gb=-6,  rg=1.00, zg=1.15, bp=-4, dz=-0.22, hp=20, sy=-5,  sp=14, lean=0.12)),
    (5,  dict(Y=22,  th=12,   gb=-2,  rg=1.00, zg=1.15, bp=-4, dz=-0.23, hp=23, sy=0,   sp=16, lean=0.15)),
    (6,  dict(Y=42,  th=48,   gb=4,   rg=1.00, zg=1.15, bp=-4, dz=-0.23, hp=22, sy=5,   sp=15, lean=0.14)),
    (7,  dict(Y=55,  th=72,   gb=12,  rg=0.95, zg=1.14, bp=-4, dz=-0.22, hp=19, sy=8,   sp=13, lean=0.10)),
    (8,  dict(Y=60,  th=86,   gb=22,  rg=0.85, zg=1.13, bp=-4, dz=-0.21, hp=17, sy=9,   sp=12, lean=0.06)),
    (10, dict(Y=40,  th=84,   gb=42,  rg=0.55, zg=1.11, bp=-4, dz=-0.18, hp=13, sy=5,   sp=10, lean=0.0)),
    (16, dict(Y=28,  th=80,   gb=50,  rg=0.45, zg=1.10, bp=-4, dz=-0.14, hp=10, sy=2,   sp=9,  lean=0.0)),
)
W1_CANCEL = 10
W2 = _rows(
    (0,  sample(W1, W1_CANCEL)),
    (2,  dict(Y=55,   th=125,  gb=40,  rg=0.50, zg=1.10, bp=-5, dz=-0.16, hp=12, sy=20,  sp=10, lean=0.0)),
    (3,  dict(Y=32,   th=78,   gb=18,  rg=0.90, zg=1.14, bp=-4, dz=-0.20, hp=16, sy=12,  sp=12, lean=0.05)),
    (4,  dict(Y=6,    th=32,   gb=6,   rg=1.00, zg=1.15, bp=-4, dz=-0.22, hp=20, sy=5,   sp=14, lean=0.12)),
    (5,  dict(Y=-22,  th=-12,  gb=2,   rg=1.00, zg=1.15, bp=-4, dz=-0.23, hp=23, sy=0,   sp=16, lean=0.15)),
    (6,  dict(Y=-42,  th=-48,  gb=-4,  rg=1.00, zg=1.15, bp=-4, dz=-0.23, hp=22, sy=-5,  sp=15, lean=0.14)),
    (8,  dict(Y=-60,  th=-88,  gb=-20, rg=0.90, zg=1.13, bp=-4, dz=-0.21, hp=17, sy=-9,  sp=12, lean=0.07)),
    (13, dict(Y=-62,  th=-104, gb=-40, rg=0.65, zg=1.12, bp=-4, dz=-0.19, hp=14, sy=-7,  sp=11, lean=0.0)),
    (18, dict(Y=-36,  th=-100, gb=-55, rg=0.45, zg=1.10, bp=-4, dz=-0.15, hp=11, sy=-3,  sp=10, lean=0.0)),
    (20, dict(Y=-32,  th=-98,  gb=-56, rg=0.45, zg=1.10, bp=-4, dz=-0.14, hp=10, sy=-2,  sp=9,  lean=0.0)),
)
W2_CANCEL = 18
# hit 3 (the reference's f87-95 is a raised overhead blow; Kido wants a flat turning slash instead): 4 f flat coil,
# 380 deg clockwise spin f4-f12 (contact f8, mid-turn), 2 f overshoot, settle, step into ST (f22-28)
_s = sample(W2, W2_CANCEL)
W3 = _rows(
    (0,  _s),
    (4,  dict(Y=-12,  th=-62,  gb=-40, rg=0.80, zg=1.10, bp=-4, dz=-0.20, hp=14, sy=-6,  sp=10, lean=0.0)),
    (6,  dict(Y=-102, th=-157, gb=-38, rg=1.00, zg=1.10, bp=-4, dz=-0.24, hp=16, sy=-10, sp=12, lean=0.0)),
    (8,  dict(Y=-192, th=-247, gb=-38, rg=1.00, zg=1.10, bp=-4, dz=-0.25, hp=16, sy=-10, sp=12, lean=0.0)),
    (10, dict(Y=-282, th=-337, gb=-38, rg=1.00, zg=1.10, bp=-4, dz=-0.24, hp=16, sy=-10, sp=12, lean=0.0)),
    (12, dict(Y=-372, th=-427, gb=-40, rg=0.95, zg=1.10, bp=-4, dz=-0.22, hp=15, sy=-8,  sp=11, lean=0.0)),
    (14, dict(Y=-390, th=-452, gb=-48, rg=0.75, zg=1.10, bp=-4, dz=-0.20, hp=14, sy=-6,  sp=10, lean=0.0)),
    (18, dict(Y=-376, th=-448, gb=-60, rg=0.50, zg=1.10, bp=-4, dz=-0.16, hp=11, sy=-3,  sp=9,  lean=0.0)),
    (30, dict(Y=-368, th=-442, gb=-62, rg=0.45, zg=1.10, bp=-4, dz=-0.12, hp=9,  sy=-2,  sp=8,  lean=0.0)),
)
W3_SPIN = (4, 12)
W3_LEN, W3_HIT, W3_STEP = 30, 8, 22

def _wide_key(c, T, lf, rf):
    gw = c["Y"] + c["gb"]
    g2 = Vector(T) + hv(gw, c["rg"])
    p = (g2.x, g2.y, c["zg"])
    d = bdir(c["th"], math.tan(math.radians(c["bp"])))
    lean = hv(gw, c["lean"])
    return dict(p=p, d=d, hips=(c["dz"], 0.0, c["hp"], 0.0), Y=c["Y"], spine=(c["sy"], c["sp"], 0.0),
                T=(T[0] + lean.x, T[1] + lean.y), lf=lf, rf=rf, lhand=1.0, reach=WIDE_REACH)

def _rot(v, deg): return Matrix.Rotation(math.radians(deg), 2) @ Vector(v)
WROOT = {}
FOLLOW = 0.75   # planted feet turn in place by this share of the body turn (0.6 left 63-67 deg thigh twist)
def build_wide():
    # clip-space = Blender world at that frame (Unity moves the root by WROOT, so a planted foot at world point W sits at
    # W + (0, r) in the clip). Feet/yaw state is handed from each hit's cancel frame to the next hit's frame 0.
    out = {}
    # ---- hit 1
    L = 16; r = [0.18 * ease(f / 3.0) for f in range(L + 1)]
    WROOT["BZ_Slash1"] = r
    lw = Vector((0.17, -0.16)); rw0, rw1 = Vector((-0.20, 0.16)), Vector((-0.26, -0.20))
    ly, ry = 15.0, -20.0; Yp = W1[0][1]["Y"]; keys = []; st = {}
    for f in range(L + 1):
        c = sample(W1, f); dY = c["Y"] - Yp; Yp = c["Y"]
        ly += FOLLOW * dY; ry += FOLLOW * dY
        u = f / 3.0; rwf = rw0.lerp(rw1, ease(u)); rl = 0.07 * math.sin(math.pi * min(1.0, u)) if u < 1 else 0.0
        L_ = lw + Vector((0, r[f])); R_ = rwf + Vector((0, r[f]))
        keys.append((f, _wide_key(c, (0.0, 0.0), (L_.x, L_.y, 0.0, ly), (R_.x, R_.y, rl, ry))))
        if f == W1_CANCEL: st = dict(L=L_.copy(), R=R_.copy(), ly=ly, ry=ry)
    out["BZ_Slash1"] = (L, 5, keys)
    # ---- hit 2 (from hit 1 at its cancel frame; stands, both feet planted and turning in place)
    L = 20; WROOT["BZ_Slash2"] = [0.0] * (L + 1)
    ly, ry = st["ly"], st["ry"]; Yp = W2[0][1]["Y"]; keys = []
    for f in range(L + 1):
        c = sample(W2, f); dY = c["Y"] - Yp; Yp = c["Y"]
        ly += FOLLOW * dY; ry += FOLLOW * dY
        keys.append((f, _wide_key(c, (0.0, 0.0), (st["L"].x, st["L"].y, 0.0, ly), (st["R"].x, st["R"].y, 0.0, ry))))
        if f == W2_CANCEL: st2 = dict(L=st["L"].copy(), R=st["R"].copy(), ly=ly, ry=ry)
    out["BZ_Slash2"] = (L, 5, keys)
    # ---- hit 3 (spin about the left foot, then step into ST with a short root advance)
    st = st2; L = W3_LEN
    r = [0.16 * ease((f - W3_STEP) / 6.0) for f in range(L + 1)]
    WROOT["BZ_Slash3"] = r
    P = st["L"].copy(); R0 = st["R"].copy(); a, b = W3_SPIN
    H2 = P * 0.6                     # weight onto the pivot foot before the turn
    Y2 = sample(W3, a)["Y"]
    ly, ry = st["ly"], st["ry"]; Yp = W3[0][1]["Y"]; keys = []
    L_end_w = Vector((0.17, -0.32)); R_end_w = Vector((-0.20, 0.0))   # ST round the root at its final 0.16 advance
    Lw = P.copy(); Rw = R0.copy()
    for f in range(L + 1):
        c = sample(W3, f); dY = c["Y"] - Yp; Yp = c["Y"]
        lift_r = lift_l = 0.0
        if f <= a:
            T = H2 * ease(f / a); g = FOLLOW
            Rc = R0.copy()
        elif f <= b:
            u = (f - a) / (b - a); ang = c["Y"] - Y2
            T = P + _rot(H2 - P, ang)
            Rc = P + _rot((R0 - P) * (1.0 - 0.3 * math.sin(math.pi * u)), ang)    # right foot pulled in, swung round
            lift_r = 0.11 * math.sin(math.pi * u); g = 1.0
        else:
            T = H2.lerp(Vector((0, 0)), ease((f - b) / 4.0)); g = FOLLOW
            Rc = R0.copy()
        ly += g * dY; ry += g * dY
        Lc = P.copy()
        if f >= W3_STEP:   # left foot steps forward into ST, right foot squares up after it
            u = (f - W3_STEP) / 5.0; Lc = P.lerp(L_end_w, ease(u)); lift_l = 0.08 * math.sin(math.pi * min(1, u)) if u < 1 else 0
            ly = ly + (_near(ly, 15.0) - ly) * ease(u) * 0.5
        if f >= W3_STEP + 2:
            u = (f - W3_STEP - 2) / 4.0; Rc = R0.lerp(R_end_w, ease(u)); lift_r = 0.05 * math.sin(math.pi * min(1, u)) if u < 1 else 0
            ry = ry + (_near(ry, -20.0) - ry) * ease(u) * 0.5
        rr = r[f]
        keys.append((f, _wide_key(c, (T.x, T.y), (Lc.x, Lc.y + rr, lift_l, ly), (Rc.x, Rc.y + rr, lift_r, ry))))
    out["BZ_Slash3"] = (L, W3_HIT, keys)
    return out
def _near(cur, target):
    """target + k*360 closest to cur"""
    return target + 360.0 * round((cur - target) / 360.0)

CLIPS.update(build_wide())

# ------------------------------------------------------------------ v6 single big hit 1 (11-16 17:26-17:27 KST)
# Kido: "1타로 좀 크게 해봐 3타말고 단타", "지금보다 훨씬 크고 넓게 배게해". GreatswordCombat now plays only hit 1 per click
# (Single = true), so BZ_Slash1 becomes a stand-alone heavy sweep: coil far right (blade behind the right shoulder),
# a deep lunge step (left foot 0.45 m out, root follows; the back foot is swept round and up through contact, f7-13,
# so the clip ends in ST),
# then a ~300 deg flat sweep right-back -> front (contact f9) -> behind the left shoulder over 8 frames (~37 deg/frame,
# reference-like), hips ~140 deg + chest +-25 on top (~190 deg shoulder turn), arms straight, hips shifted toward the grip
# for reach. 28 frames (0.93 s). Grip at chest height; blade pitch keyed -8..-10 through the pass because Unity's humanoid
# retarget lifts it ~10 deg (1st in-game check: +4..+7 with -4..-5 keyed), so in game it stays flat / slightly down.
# Hits 2/3 are untouched (built from the old W1 above; the old BZ_Slash1 rows stay in W1 for that hand-off).
W1B = _rows(
    (0,  dict(Y=-10, th=-140, gb=-55, rg=0.45, zg=1.05, bp=-5, dz=-0.10, hp=10, sy=-8,  sp=8,  lean=0.0)),
    (3,  dict(Y=-55, th=-158, gb=-62, rg=0.70, zg=1.10, bp=-6, dz=-0.18, hp=12, sy=-22, sp=10, lean=0.05)),
    (5,  dict(Y=-65, th=-152, gb=-55, rg=0.95, zg=1.12, bp=-8, dz=-0.24, hp=16, sy=-25, sp=12, lean=0.15)),
    (7,  dict(Y=-32, th=-82,  gb=-22, rg=1.00, zg=1.08, bp=-10, dz=-0.27, hp=20, sy=-12, sp=14, lean=0.22)),
    (9,  dict(Y=4,   th=0,    gb=-4,  rg=1.00, zg=1.04, bp=-10, dz=-0.28, hp=22, sy=0,   sp=16, lean=0.24)),
    (11, dict(Y=42,  th=80,   gb=12,  rg=1.00, zg=1.06, bp=-10, dz=-0.27, hp=20, sy=14,  sp=14, lean=0.22)),
    (13, dict(Y=72,  th=140,  gb=28,  rg=0.95, zg=1.10, bp=-9, dz=-0.26, hp=18, sy=24,  sp=12, lean=0.18)),
    (15, dict(Y=76,  th=150,  gb=38,  rg=0.80, zg=1.12, bp=-8, dz=-0.24, hp=16, sy=24,  sp=11, lean=0.08)),
    (21, dict(Y=40,  th=138,  gb=55,  rg=0.55, zg=1.10, bp=-6, dz=-0.18, hp=12, sy=10,  sp=10, lean=0.0)),
    (28, dict(Y=22,  th=122,  gb=62,  rg=0.45, zg=1.08, bp=-5, dz=-0.14, hp=10, sy=4,   sp=9,  lean=0.0)),
)
B1_LEN, B1_HIT, B1_ADV, B1_FOLLOW = 28, 9, 0.45, 0.5
def build_big1():
    L = B1_LEN
    r = [B1_ADV * ease((f - 1) / 9.0) for f in range(L + 1)]
    WROOT["BZ_Slash1"] = r
    l0, l1 = Vector((0.17, -0.16)), Vector((0.19, -0.16 - B1_ADV))        # world, -Y = forward
    r0, r1 = Vector((-0.20, 0.16)), Vector((-0.20, 0.16 - B1_ADV))
    keys = []
    for f in range(L + 1):
        c = sample(W1B, f)
        ul = (f - 1) / 4.0; ur = (f - 7) / 6.0    # back foot swept round with the turn right after contact
        Lw = l0.lerp(l1, ease(ul)); Rw = r0.lerp(r1, ease(ur))
        ll = 0.10 * math.sin(math.pi * ul) if 0 < ul < 1 else 0.0
        rl = 0.07 * math.sin(math.pi * ur) if 0 < ur < 1 else 0.0
        # feet yaw tied to the body yaw (not accumulated): front foot pivots with half the turn, the back foot (pivoting on
        # its ball, then swept round) with 0.8 of it, so neither thigh winds up and the end pose is near ST
        ly = 15.0 + 0.5 * c["Y"]; ry = -20.0 + 0.8 * c["Y"]
        L_ = Lw + Vector((0, r[f])); R_ = Rw + Vector((0, r[f]))
        keys.append((f, _wide_key(c, (0.0, 0.0), (L_.x, L_.y, ll, ly), (R_.x, R_.y, rl, ry))))
    return (L, B1_HIT, keys)
if not os.environ.get("BZ_OLD1"): CLIPS["BZ_Slash1"] = build_big1()
for _n in WIDE: ROOT[_n] = WROOT[_n]

# run -- heavy warrior run, 20 f cycle, sword resting back on the right shoulder, body leaning forward (no reference yet)
def run_key(ph):
    a = 2 * math.pi * ph
    def foot(side, off):
        q = (ph + off) % 1.0
        if q < 0.5:   # on the ground: from front to back
            y = -0.42 + 0.84 * (q / 0.5); lift = 0.0
        else:         # swinging forward
            s = (q - 0.5) / 0.5; y = 0.42 - 0.84 * s; lift = 0.26 * math.sin(math.pi * s)
        return (side * 0.13, y, lift, 0)
    # arm fix (11-15): sword dragged in the right hand (drag_pose), body turned a little toward it, left arm swinging by
    # the side against the left leg (back when the left foot is in front), elbow bent; legs = the same 20 f cycle
    dz = -0.09 - 0.04 * math.cos(2 * a)
    return drag_pose((dz, -8 + 6 * math.sin(a), 24, 0), (-12 - 10 * math.sin(a), 12, 0),
                     (-31 + 20 * math.cos(a), 78 - 8 * math.cos(a), 0.08), dz=dz + 0.10,
                     lf=foot(1, 0.0), rf=foot(-1, 0.5))
CLIPS["BZ_Run"] = (20, 0, [(f, run_key(f / 20.0)) for f in range(0, 21, 2)])
# idle -- 40 f loop, slow rise and fall
def idle_key(ph):
    a = 2 * math.pi * ph
    # arm fix (11-15): the drag stance (sword trailing on the floor, one hand) breathing
    k = dict(DRAG_IDLE); k["_like"] = DRAG_IDLE; k["hips"] = (-0.10 - 0.012 * math.sin(a), -8, 12, 0); k["spine"] = (-12, 10 + 1.5 * math.sin(a), 0)
    k["p"] = (-0.27, 0.10, 0.86 - 0.012 * math.sin(a)); k["lswing"] = (-10 + 2 * math.sin(a), 35, 0.08); return k
CLIPS["BZ_Idle"] = (40, 0, [(f, idle_key(f / 40.0)) for f in range(0, 41, 5)])

# ------------------------------------------------------------------ build each clip, bake to FK
CTRL = [SC, FL, FR, LH, KL, KR]
def clear_ctrl():
    for o in CTRL:
        if o.animation_data: o.animation_data.action = None
    if rig.animation_data: rig.animation_data.action = None
    for pb in P:
        pb.location = (0, 0, 0); pb.rotation_euler = (0, 0, 0)

baked = []
for name, (length, hit, keys) in CLIPS.items():
    if ONLY and name not in ONLY: continue
    clear_ctrl(); _lastq = _lastlq = _lastpole = None; _lastknee = {}; _lastfoot = {}; _lastfoot = {}
    rig.animation_data_create()
    for o in CTRL: o.animation_data_create()
    ctrl_act = bpy.data.actions.new(name + "_ctrl")
    rig.animation_data.action = ctrl_act
    for o in CTRL: o.animation_data.action = bpy.data.actions.new(name + "_" + o.name)
    sc.frame_set(0)
    for f, k in keys: resolve(k)
    for i, (f, k) in enumerate(keys):
        sc.frame_set(f)
        key_pose(f, k, keys[i - 1][1] if i > 0 else None, keys[i + 1][1] if i + 1 < len(keys) else None)
    if name in ROOT and name not in WIDE:
        # v3: feet keyed every frame at (world plant - root advance) so they stay planted while Unity moves the root
        for fr in range(0, length + 1):
            ft = feet_at(name, fr)
            for tgt, key in ((FL, "lf"), (FR, "rf")):
                x, y, lift, _ = ft[key]
                tgt.location = Vector((x, y, ANK + lift)); tgt.keyframe_insert("location", frame=fr)
    # snappy: linear in the swings, bezier elsewhere is fine; Blender 5 actions are layered, walk all fcurves
    def fcurves(act):
        try: return list(act.fcurves)
        except Exception:
            out = []
            for layer in act.layers:
                for strip in layer.strips:
                    for cb in strip.channelbags: out.extend(cb.fcurves)
            return out
    for o in [rig] + CTRL:
        for fc in fcurves(o.animation_data.action):
            for kp in fc.keyframe_points:
                kp.interpolation = "LINEAR" if hit and hit - 3 <= kp.co.x <= hit + 1 else "BEZIER"
                if name in ("BZ_Run", "BZ_Idle"): kp.handle_left_type = kp.handle_right_type = "AUTO_CLAMPED"
    # limb fix: in-between frames where the interpolated sword / hand targets roll a wrist or thigh too far get their own
    # key (left hand roll, right elbow plane, knee poles), picked the same way as on the main keys
    keyed = {f for f, _ in keys}
    ikR = B("RightForeArm").constraints["bzIK"]
    for it in range(2):
        nfix = 0
        for fr in range(0, length + 1):
            if fr in keyed: continue
            sc.frame_set(fr); bpy.context.view_layer.update()
            thR = visual_local(B("RightHand"))[0]; thL = visual_local(B("LeftHand"))[0]
            liL = B("LeftHand").constraints["bzRot"].influence
            if abs(thR) > 70:
                cur = math.degrees(ikR.pole_angle); bb = None
                for a in range(-165, -14, 15):
                    ikR.pole_angle = math.radians(a); bpy.context.view_layer.update()
                    ta, _ = visual_local(B("RightArm")); th, _ = visual_local(B("RightHand"))
                    c = abs(th) + 0.6 * abs(ta) + 2 * max(0, abs(ta) - 80) + 0.3 * abs(a - cur)
                    if bb is None or c < bb[0]: bb = (c, a)
                ikR.pole_angle = math.radians(bb[1]); ikR.keyframe_insert("pole_angle", frame=fr); nfix += 1
            if abs(thL) > 70 and liL > 0.5:
                SCw = SC.matrix_world.copy(); rel = SCw.inverted() @ LH.matrix_world; bb = None
                q0 = LH.rotation_quaternion.copy()
                for a in range(-90, 91, 15):
                    LH.matrix_world = SCw @ Matrix.Rotation(math.radians(a), 4, "Y") @ rel; bpy.context.view_layer.update()
                    ta, _ = visual_local(B("LeftArm")); th, _ = visual_local(B("LeftHand"))
                    c = abs(th) + 0.6 * abs(ta) + 0.2 * abs(a)
                    if bb is None or c < bb[0]: bb = (c, a)
                LH.matrix_world = SCw @ Matrix.Rotation(math.radians(bb[1]), 4, "Y") @ rel
                qq = LH.rotation_quaternion.copy(); qq.make_compatible(q0); LH.rotation_quaternion = qq
                LH.keyframe_insert("location", frame=fr); LH.keyframe_insert("rotation_quaternion", frame=fr); nfix += 1
            for side, kp in (("Left", KL), ("Right", KR)):
                tt = visual_local(B(side + "UpLeg"))[0]
                if abs(tt) <= 55: continue
                hip = rig.matrix_world @ B(side + "UpLeg").head; ank = rig.matrix_world @ B(side + "Foot").head
                line = (ank - hip).normalized(); base = kp.location.copy(); bb = None
                for a in range(-60, 61, 10):
                    kp.location = hip + Matrix.Rotation(math.radians(a), 3, line) @ (base - hip); bpy.context.view_layer.update()
                    t2 = visual_local(B(side + "UpLeg"))[0]; tf = visual_local(B(side + "Foot"))[0]
                    c = abs(t2) + 0.7 * abs(tf) + 0.2 * abs(a)
                    if bb is None or c < bb[0]: bb = (c, a)
                kp.location = hip + Matrix.Rotation(math.radians(bb[1]), 3, line) @ (base - hip)
                kp.keyframe_insert("location", frame=fr); nfix += 1
            if nfix: keyed.add(fr)
        print(f"  {name}: in-between fixes pass {it}: {nfix}")
        if not nfix: break
    # bake visual transforms of all deform bones into a clean FK action
    sc.frame_start, sc.frame_end = 0, length
    bpy.context.view_layer.objects.active = rig
    for o in bpy.context.view_layer.objects: o.select_set(False)
    rig.select_set(True)
    if bpy.context.object and bpy.context.object.mode != "POSE": bpy.ops.object.mode_set(mode="POSE")
    for pb in P:
        try: pb.select = True
        except Exception: pb.bone.select = True
    bpy.ops.nla.bake(frame_start=0, frame_end=length, only_selected=True, visual_keying=True, clear_constraints=False,
                     use_current_action=False, bake_types={"POSE"})
    act = rig.animation_data.action; act.name = name; act.use_fake_user = True
    baked.append((name, length, hit))
    print(f"baked {name}: {length} f, hit at {hit}")
    bpy.ops.object.mode_set(mode="OBJECT")

# ------------------------------------------------------------------ remove the control rig (bones keep the baked poses)
for pb in P:
    for cc in list(pb.constraints):
        if cc.name.startswith("bz"): pb.constraints.remove(cc)
for o in [SC, RH, LH, PR, PL, FL, FR, KL, KR]: bpy.data.objects.remove(o)
for a in list(bpy.data.actions):
    if a.name.endswith("_ctrl") or "_BZ_" in a.name: bpy.data.actions.remove(a)

# ------------------------------------------------------------------ v5 per-frame blade / body / leg log (baked FK only)
# blade from the baked right hand (the sword is a child of RightHand in GS_Hero.fbx): grip, tip, pitch from horizontal,
# tip height vs chest (Spine2 head) height, tip radius from the root (clip origin + root advance) and from the hips,
# body yaw (hips forward, unwrapped), thigh twist, knee direction vs toes, planted-foot slide (world = clip - root).
WIDE_LOG = []
def wide_log():
    rows = []; bad = []
    W3x = rig.matrix_world
    for name, length, hit in baked:
        if name not in WIDE: continue
        rig.animation_data.action = bpy.data.actions[name]
        yprev = None; ywr = 0.0; feetprev = {}
        for f in range(0, length + 1):
            sc.frame_set(f); bpy.context.view_layer.update()
            hand = W3x @ B("RightHand").matrix
            Sn = hand @ hand_rest.inverted() @ S_rest
            g = Sn.translation; bd = Sn.to_3x3().col[1].normalized(); tip = g + bd * tip_len
            pitch = math.degrees(math.asin(max(-1, min(1, bd.z))))
            chest = (W3x @ B("Spine2").head).z
            r = ROOT[name][f]
            root = Vector((0, -r, 0))      # in clip space the root sits at the origin; world = clip + (0, -r)
            tr = math.hypot(tip.x, tip.y)
            hips = W3x @ B("Hips").head
            trh = math.hypot(tip.x - hips.x, tip.y - hips.y)
            hz = (W3x.to_3x3() @ B("Hips").matrix.to_3x3()).col[2]     # hips local Z = facing at rest (-Y)
            y = math.degrees(math.atan2(hz.x, -hz.y))
            if yprev is not None:
                dy = (y - yprev + 180) % 360 - 180; ywr += dy
            else: ywr = y
            yprev = y
            tb = math.degrees(math.atan2(tip.x - hips.x, -(tip.y - hips.y)))
            tw = {}; kv = {}; slide = {}
            for side in ("Left", "Right"):
                tw[side] = visual_local(B(side + "UpLeg"))[0]
                hp_ = W3x @ B(side + "UpLeg").head; kn = W3x @ B(side + "Leg").head; an = W3x @ B(side + "Foot").head
                to = W3x @ B(side + "ToeBase").head
                line = (an - hp_).normalized(); ko = kn - hp_; ko = ko - line * ko.dot(line); ko.z = 0
                td = to - an; td.z = 0
                kv[side] = math.degrees(ko.to_2d().angle_signed(td.to_2d())) if ko.length > 1e-4 and td.length > 1e-4 else 0.0
                wpos = Vector((an.x, an.y - r)); planted = an.z < ANK + 0.02
                pp = feetprev.get(side)
                slide[side] = (wpos - pp[0]).length if pp and pp[1] and planted else 0.0
                feetprev[side] = (wpos, planted)
            row = dict(clip=name, frame=f, body_yaw=round(ywr, 1), tip_bearing=round(tb, 1), blade_pitch=round(pitch, 1),
                       tip_z=round(tip.z, 3), chest_z=round(chest, 3), tip_minus_chest=round(tip.z - chest, 3),
                       grip_z=round(g.z, 3), tip_r_root=round(tr, 3), tip_r_hips=round(trh, 3), root_adv=round(r, 3),
                       thigh_tw_L=round(tw["Left"]), thigh_tw_R=round(tw["Right"]), knee_vs_toe_L=round(kv["Left"]),
                       knee_vs_toe_R=round(kv["Right"]), slide_L_cm=round(slide["Left"] * 100, 1), slide_R_cm=round(slide["Right"] * 100, 1))
            rows.append(row)
            if abs(pitch) > 15: bad.append(f"{name} f{f}: blade pitch {pitch:+.1f}")
            if abs(tip.z - chest) > 0.25: bad.append(f"{name} f{f}: tip {tip.z:.2f} vs chest {chest:.2f}")
            if max(abs(tw["Left"]), abs(tw["Right"])) > 60: bad.append(f"{name} f{f}: thigh twist L{tw['Left']:+.0f} R{tw['Right']:+.0f}")
            if max(slide.values()) > 0.02: bad.append(f"{name} f{f}: planted foot slide L{slide['Left']*100:.1f} R{slide['Right']*100:.1f} cm")
    p = os.path.join(OUT, "wide_sweep_blade_log.csv")
    with open(p, "w") as fh:
        fh.write(",".join(rows[0].keys()) + "\n")
        for rw in rows: fh.write(",".join(str(v) for v in rw.values()) + "\n")
    with open(os.path.join(OUT, "wide_sweep_check.txt"), "w") as fh:
        fh.write("\n".join(bad) if bad else "all frames pass\n")
    print("WIDE LOG", p, "violations:", len(bad)); [print("  ", b) for b in bad[:60]]
wide_log()

# ------------------------------------------------------------------ check renders (top-down like the game, 55 deg)
if RENDER:
    for o in sc.objects:
        if o.name in ("GS_EyeFlame", "GS_EyeWisps_orig", "GS_HeroA", "GS_RigA"): o.hide_render = True
    cam = bpy.data.objects.get("BZ_Cam") or bpy.data.objects.new("BZ_Cam", bpy.data.cameras.new("BZ_Cam"))
    if cam.name not in sc.collection.objects: sc.collection.objects.link(cam)
    cam.data.type = "ORTHO"; cam.data.ortho_scale = 5.6   # v5: room for the ~2 m sweep
    pitch = math.radians(55)
    cam.location = Vector((0, 0, 0.9)) + Vector((0, math.cos(pitch), math.sin(pitch))) * 8   # behind the character, looking down
    cam.rotation_euler = (math.radians(90 - 55), 0, math.radians(180))
    sc.camera = cam
    sc.render.engine = "BLENDER_WORKBENCH"
    sc.display.shading.light = "STUDIO"; sc.display.shading.color_type = "MATERIAL"
    sc.render.resolution_x = sc.render.resolution_y = 320
    sc.render.film_transparent = False
    sc.render.image_settings.file_format = "PNG"
    for name, length, hit in baked:
        rig.animation_data.action = bpy.data.actions[name]
        d = os.path.join(OUT, name); os.makedirs(d, exist_ok=True)
        for f in range(0, length + 1):
            sc.frame_set(f); sc.render.filepath = os.path.join(d, f"f_{f:03d}.png")
            bpy.ops.render.render(write_still=True)
    # v5: straight top-down (the sweep circle reads true), root advance applied so the floor grid stays put
    cam.location = Vector((0, 0, 10)); cam.rotation_euler = (0, 0, 0)
    for name, length, hit in baked:
        rig.animation_data.action = bpy.data.actions[name]
        d = os.path.join(OUT, name + "_top"); os.makedirs(d, exist_ok=True)
        for f in range(0, length + 1):
            sc.frame_set(f); sc.render.filepath = os.path.join(d, f"f_{f:03d}.png")
            bpy.ops.render.render(write_still=True)
    # second view: side
    cam.location = Vector((8, 0, 1.0)); cam.rotation_euler = (math.radians(90), 0, math.radians(90))
    for name, length, hit in baked:
        rig.animation_data.action = bpy.data.actions[name]
        d = os.path.join(OUT, name + "_side"); os.makedirs(d, exist_ok=True)
        for f in range(0, length + 1):
            sc.frame_set(f); sc.render.filepath = os.path.join(d, f"f_{f:03d}.png")
            bpy.ops.render.render(write_still=True)

with open(os.path.join(OUT, "clips.txt"), "w") as fh:
    for name, length, hit in baked: fh.write(f"{name} {length} {hit}\n")
    for name in ROOT: fh.write(f"root {name} " + ",".join(f"{v:.3f}" for v in ROOT[name]) + "\n")
    leg = (B("LeftUpLeg").head - B("LeftLeg").head).length + (B("LeftLeg").head - B("LeftFoot").head).length
    fh.write(f"legLen {leg * rig.matrix_world.to_scale().x:.4f}\n")
print("done", baked)

# ------------------------------------------------------------------ save a copy (new name) + FBX for Unity
if not ONLY:
    rig.animation_data.action = bpy.data.actions["BZ_Slash1"]
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "gs_hero_berserker_anim_v5.blend"), copy=True)
    for a in list(bpy.data.actions):
        if not a.name.startswith("BZ_"): bpy.data.actions.remove(a)
    for o in bpy.context.view_layer.objects: o.select_set(False)
    for n in ("GS_Rig", "GS_Hero"):
        o = bpy.data.objects[n]; o.hide_viewport = False; o.hide_set(False); o.select_set(True)
    bpy.context.view_layer.objects.active = rig
    fbx = os.environ.get("BZ_FBX", os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "Assets", "Game", "GSHero", "Art", "GS_BerserkerAnims.fbx"))
    bpy.ops.export_scene.fbx(filepath=fbx, use_selection=True, object_types={"ARMATURE", "MESH"}, add_leaf_bones=False,
                             bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
                             bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0, use_armature_deform_only=True)
    print("exported", fbx)
