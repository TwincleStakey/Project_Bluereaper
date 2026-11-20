import math
def tw(pb):
    q = pb.matrix_basis.to_quaternion(); sw, t = q.to_swing_twist('Y')
    a = math.degrees(sw.angle); ax = sw.axis
    if a > 180: a = 360 - a; ax = -ax
    return math.degrees(t), a, ax
def upd(): bpy.context.view_layer.update()
def visual(pb):
    # matrix_basis does not include constraints: use the visual local matrix
    m = pb.matrix
    par = pb.parent
    rest = pb.bone.matrix_local
    if par: loc = (par.bone.matrix_local.inverted() @ rest).inverted() @ (par.matrix.inverted() @ m)
    else: loc = rest.inverted() @ m
    q = loc.to_quaternion(); sw, t = q.to_swing_twist('Y')
    a = math.degrees(sw.angle); ax = sw.axis
    if a > 180: a = 360 - a; ax = -ax
    return math.degrees(t), a, ax
h = B("Hips")
print("=== LEGS (hips down 0.25, feet planted)")
h.location = hips_loc(-0.25, 0, -0.05)
for lock in (False, True):
    for pb in (B("LeftLeg"), B("RightLeg")): pb.lock_ik_y = pb.lock_ik_z = lock
    for ang in range(-180, 180, 30):
        for n in ("LeftLeg", "RightLeg"): B(n).constraints["bzIK"].pole_angle = math.radians(ang)
        upd()
        s = []
        for side in ("Left", "Right"):
            t1, a1, ax1 = visual(B(side + "UpLeg")); t2, a2, ax2 = visual(B(side + "Leg"))
            knee = rig.matrix_world @ B(side + "Leg").head; hip = rig.matrix_world @ B(side + "UpLeg").head; ank = rig.matrix_world @ B(side + "Foot").head
            line = (ank - hip).normalized(); kv = knee - hip; kv = kv - line * kv.dot(line)
            s.append(f"{side[0]}: thighTw {t1:+4.0f} knee sw{a2:3.0f} ax({ax2.x:+.2f},{ax2.z:+.2f}) kneeFwd {(-kv.y):+.3f}")
        print(f"lock={lock} pole {ang:+4d} | " + " | ".join(s))
h.location = (0, 0, 0)
for n in ("LeftLeg", "RightLeg"): B(n).constraints["bzIK"].pole_angle = math.radians(-90)
print("=== ARMS (grip in front of the chest, bent)")
m = sword_m((-0.02, -0.26, 1.18), (0, -0.3, 0.95), Vector((1,0,0))); upd(); print("reach R", ((rig.matrix_world @ B("RightArm").head) - RH.matrix_world.translation).length, "L", ((rig.matrix_world @ B("LeftArm").head) - LH.matrix_world.translation).length); SC.rotation_mode = "QUATERNION"; SC.location = m.translation; SC.rotation_quaternion = m.to_quaternion(); upd(); print("reach R", ((rig.matrix_world @ B("RightArm").head) - RH.matrix_world.translation).length, "L", ((rig.matrix_world @ B("LeftArm").head) - LH.matrix_world.translation).length)
for seed in ((30,0,0),(-30,0,0),(0,0,30),(0,0,-30)):
    for n in ("LeftForeArm", "RightForeArm"):
        B(n).rotation_euler = tuple(math.radians(v) for v in seed)
    for ang in (-90, 0, 90, 180):
        for n in ("LeftForeArm", "RightForeArm"): B(n).constraints["bzIK"].pole_angle = math.radians(ang)
        upd()
        s = []
        for side in ("Left", "Right"):
            t1, a1, ax1 = visual(B(side + "Arm")); t2, a2, ax2 = visual(B(side + "ForeArm")); t3, a3, ax3 = visual(B(side + "Hand"))
            sh = rig.matrix_world @ B(side + "Arm").head; el = rig.matrix_world @ B(side + "ForeArm").head; wr = rig.matrix_world @ B(side + "Hand").head
            line = (wr - sh).normalized(); ev = el - sh; ev = ev - line * ev.dot(line)
            ev = ev.normalized() if ev.length > 1e-4 else ev
            s.append(f"{side[0]}: armTw {t1:+4.0f} elb sw{a2:3.0f} ax({ax2.x:+.2f},{ax2.z:+.2f}) foreTw {t2:+4.0f} handTw {t3:+4.0f} elbowDir ({ev.x:+.1f},{ev.y:+.1f},{ev.z:+.1f}) wristErr {(wr - (RH if side=='Right' else LH).matrix_world.translation).length:.3f}")
        print(f"seed {seed} pole {ang:+4d} | " + " | ".join(s))
