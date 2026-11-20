# 장산범 v2 (11-20 Kido: "긁을 때 등 갈퀴가 너무 크고 얼굴이 아예 안 보여"): the fur cards on top of the shoulders, neck and
# head rise into a tall crest that the high game camera looks over onto the head - in the scrape the face hides behind it.
# Press the crest down: every vertex above a "ceiling" (spine/neck/head bone height + CEIL) gets its height above the
# ceiling squashed to SQ. Cards are the whole body (no skin under them), so shortening whole cards opened holes (tried
# first); squashing only what sticks up keeps the surface closed. Ramps in from the shoulders (Y0) to the neck (Y1).
#   blender -b jsb_v2_full.blend -P v2_fur_trim.py -- <out.blend> [ceil 0.025] [squash 0.15] [y0 -0.08] [y1 -0.22] [shrink 0.65]
import bpy, sys
import numpy as np
from mathutils import Vector
a = sys.argv[sys.argv.index('--') + 1:]
OUT = a[0]; CEIL = float(a[1]) if len(a) > 1 else 0.055; SQ = float(a[2]) if len(a) > 2 else 0.3
Y0 = float(a[3]) if len(a) > 3 else -0.10; Y1 = float(a[4]) if len(a) > 4 else -0.24
body = [o for o in bpy.data.objects if o.type == 'MESH' and o.name.startswith('tripo')][0]
arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
me = body.data; mw = body.matrix_world; inv = mw.inverted()
# height of the bone line along y (Spine2 .. Head tip)
pts = []
for n in ('Spine2', 'Spine3', 'Neck1', 'Neck2', 'Head'):
    b = arm.data.bones[n]; pts += [arm.matrix_world @ b.head_local, arm.matrix_world @ b.tail_local]
pts.sort(key=lambda p: p.y)
ys = np.array([p.y for p in pts]); zs = np.array([p.z for p in pts])
W = np.array([(mw @ v.co)[:] for v in me.vertices])
bz = np.interp(W[:, 1], ys, zs)
ceil = bz + CEIL
ramp = np.clip((Y0 - W[:, 1]) / (Y0 - Y1), 0, 1)
over = W[:, 2] - ceil
# 11-20 Kido ("목이 업사이드 다운", the head read as flipped): pressing the fur on top of the HEAD down onto the head bone
# laid the forehead fur over the face. Neck and shoulders only: stop before the head (Head bone starts at y -0.39).
HEADY = -0.36
m = (over > 0) & (ramp > 0) & (W[:, 1] > HEADY)
k = 1 - (1 - SQ) * ramp
W[m, 2] = ceil[m] + over[m] * k[m]
# pass 2 (the squash alone barely showed in game): the whole upper half of the shoulders/neck/head shrinks toward the bone
# line by SHR (x and z together, so the mass gets smaller without opening holes)
SHR = float(a[5]) if len(a) > 5 else 1.0   # 0.6 tried: moving whole cards opened gaps, off by default
# per fur card (whole card moves with one transform from its centre, so nothing is stretched across the seam); the
# weight fades in from below the bone line to above it
import bmesh
bm = bmesh.new(); bm.from_mesh(me); bm.verts.ensure_lookup_table()
isl = [-1] * len(bm.verts); groups = []
for v in bm.verts:
    if isl[v.index] >= 0: continue
    k2 = len(groups); st = [v]; isl[v.index] = k2; mem = []
    while st:
        x = st.pop(); mem.append(x.index)
        for e in x.link_edges:
            y = e.other_vert(x)
            if isl[y.index] < 0: isl[y.index] = k2; st.append(y)
    groups.append(mem)
bm.free()
nmoved = 0
for mem in groups:
    if len(mem) > 2000: continue                                  # the main shell island, if any
    c = W[mem].mean(0)
    if c[1] < ys.min() - 0.02: continue
    r = float(np.clip((Y0 - c[1]) / (Y0 - Y1), 0, 1))
    b = float(np.interp(c[1], ys, zs))
    wv = float(np.clip((c[2] - (b - 0.06)) / 0.08, 0, 1))
    f = 1 - (1 - SHR) * r * wv
    if f > 0.995: continue
    base = b - 0.03
    nc = np.array([c[0] * f, c[1], base + (c[2] - base) * f])
    W[mem] = nc + (W[mem] - c) * (0.75 + 0.25 * f)              # the card itself also a bit smaller
    nmoved += 1
print('SHRINK cards', nmoved, 'factor', SHR)
for i, v in enumerate(me.vertices): v.co = inv @ Vector(W[i])
me.update()
print('TRIM_OK verts', int(m.sum()), 'ceil', CEIL, 'squash', SQ, 'y', Y0, Y1)
bpy.ops.wm.save_as_mainfile(filepath=OUT, copy=True)
