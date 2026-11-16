# Telegraph animation for 장산범: angry bull-style ground scrape with the RIGHT front paw (the one that is forward),
# 3 big strokes backwards, the spine bending toward that paw, head down and turned to it, weight rocking with each drag.
# Front legs use 2-bone IK (left front stays planted), result baked to plain keys. Body faces -Y, right = -X.
import bpy, math, sys
from mathutils import Vector, Matrix, Quaternion
OUT = sys.argv[sys.argv.index('--') + 1]
sc = bpy.context.scene; sc.render.fps = 30
arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
for o in bpy.context.view_layer.objects: o.select_set(False)
arm.select_set(True); bpy.context.view_layer.objects.active = arm
AW = arm.matrix_world
B = arm.data.bones
def head(n): return AW @ B[n].head_local
def tail(n): return AW @ B[n].tail_local

# IK targets / poles (empties in world space)
def empty(name, p):
    e = bpy.data.objects.new(name, None); sc.collection.objects.link(e); e.location = p; return e
tgtR = empty('IK_R', tail('Forearm.R')); tgtL = empty('IK_L', tail('Forearm.L'))
poleR = empty('Pole_R', head('Forearm.R') + Vector((0, 0.25, 0.05))); poleL = empty('Pole_L', head('Forearm.L') + Vector((0, 0.25, 0.05)))
bpy.ops.object.mode_set(mode='POSE')
for side, t, p in (('R', tgtR, poleR), ('L', tgtL, poleL)):
    c = arm.pose.bones[f'Forearm.{side}'].constraints.new('IK'); c.target = t; c.pole_target = p; c.chain_count = 2
    # pole angle that keeps the rest pose: measured below
    c.pole_angle = 0.0
bpy.context.view_layer.update()
# find pole angle that reproduces the rest elbow position (search)
for side in ('R', 'L'):
    c = arm.pose.bones[f'Forearm.{side}'].constraints[0]; rest = head(f'Forearm.{side}')
    best = (1e9, 0)
    for k in range(-180, 181, 3):
        c.pole_angle = math.radians(k); bpy.context.view_layer.update()
        d = ((arm.matrix_world @ arm.pose.bones[f'Forearm.{side}'].head) - rest).length
        if d < best[0]: best = (d, k)
    c.pole_angle = math.radians(best[1]); print('POLE', side, best)

AX = {'x': Vector((1, 0, 0)), 'y': Vector((0, 1, 0)), 'z': Vector((0, 0, 1))}
def rot(name, axis, deg):
    pb = arm.pose.bones[name]; h = pb.head.copy()
    R = Matrix.Translation(h) @ Matrix.Rotation(math.radians(deg), 4, AX[axis]) @ Matrix.Translation(-h)
    pb.matrix = R @ pb.matrix; bpy.context.view_layer.update()
def reset():
    for pb in arm.pose.bones: pb.matrix_basis = Matrix()
    bpy.context.view_layer.update()

def ease(x): x = max(0.0, min(1.0, x)); return x * x * (3 - 2 * x)
F0, CYC, NC, F_OUT = 10, 22, 3, 18          # lean-in, frames per stroke, strokes, settle
END = F0 + CYC * NC + F_OUT
A0 = tail('Forearm.R').copy(); GROUND = A0.z
def paw_path(u, big):
    """ankle offset over one stroke: lift high and forward, slam down, drag far back on the ground, kick up behind"""
    keys = [(0.00, (0, 0.00, 0.00)), (0.18, (0, -0.06, 0.07)), (0.32, (0, -0.08, 0.02)), (0.38, (0, -0.075, 0.0)),
            (0.70, (0, 0.13, 0.0)), (0.84, (0, 0.17, 0.05)), (1.00, (0, 0.00, 0.00))]
    for (u0, a), (u1, b) in zip(keys, keys[1:]):
        if u <= u1:
            f = ease((u - u0) / (u1 - u0)); v = Vector(a).lerp(Vector(b), f); return Vector((v.x, v.y * big, v.z * big))
    return Vector((0, 0, 0))

frames = []
for f in range(0, END + 1):
    reset()
    lean = ease(f / F0) * (1 - ease((f - (END - F_OUT + 4)) / (F_OUT - 4)))       # how much the body is wound toward the paw
    k = (f - F0) // CYC; u = ((f - F0) % CYC) / CYC
    stroking = F0 <= f < F0 + CYC * NC
    big = (1.0, 1.1, 1.3)[min(max(k, 0), 2)] if stroking else 0
    drag = math.sin(math.pi * max(0, min(1, (u - 0.38) / 0.32))) if stroking else 0   # 0..1 during the drag
    lift = math.sin(math.pi * max(0, min(1, u / 0.38))) if stroking else 0
    # weight rocks back onto the hind legs and to the left while the right paw works; spine curls toward the right paw
    arm.pose.bones['Root'].location = Vector((0, 0, 0)); bpy.context.view_layer.update()
    pb = arm.pose.bones['Hips']; pb.matrix = Matrix.Translation(Vector((0.006 * lean, 0.012 * lean + 0.01 * drag * big, -0.012 * lean - 0.006 * drag))) @ pb.matrix
    bpy.context.view_layer.update()
    rot('Spine1', 'z', -4 * lean)
    rot('Spine2', 'z', -7 * lean - 3 * drag * big); rot('Spine2', 'y', -4 * lean - 3 * lift)
    rot('Spine3', 'z', -9 * lean - 5 * drag * big); rot('Spine3', 'y', -6 * lean - 4 * lift * big); rot('Spine3', 'x', 6 * lean + 4 * drag)
    rot('Neck1', 'z', -10 * lean); rot('Neck1', 'x', 12 * lean + 6 * drag * big)
    rot('Neck2', 'z', -8 * lean); rot('Neck2', 'x', 6 * lean)
    rot('Head', 'z', -6 * lean); rot('Head', 'x', 8 * lean - 10 * drag * big)                       # head jerks with each drag (snort)
    # IK targets: left front planted, right front scrapes
    tgtL.location = tail('Forearm.L') if f < 0 else tgtL.location
    tgtR.location = A0 + paw_path(u, big) if stroking else A0
    bpy.context.view_layer.update()
    # paw: toes dig in on the drag, flick on the kick
    rot('FrontPaw.R', 'x', -35 * drag * big + 25 * lift)
    rot('FrontToes.R', 'x', -25 * drag)
    for pb in arm.pose.bones:
        pb.keyframe_insert('location', frame=f); pb.keyframe_insert('rotation_quaternion', frame=f)
    tgtR.keyframe_insert('location', frame=f)
sc.frame_start, sc.frame_end = 0, END
# bake IK into plain bone keys and drop the constraints
bpy.ops.pose.select_all(action='SELECT')
bpy.ops.nla.bake(frame_start=0, frame_end=END, only_selected=False, visual_keying=True, clear_constraints=True, use_current_action=True, bake_types={'POSE'})
bpy.ops.object.mode_set(mode='OBJECT')
for e in (tgtR, tgtL, poleR, poleL): bpy.data.objects.remove(e, do_unlink=True)
act = arm.animation_data.action; act.name = 'p0_telegraph_scrape'
print('SCRAPE frames', END)
bpy.ops.wm.save_as_mainfile(filepath=OUT, copy=True)
