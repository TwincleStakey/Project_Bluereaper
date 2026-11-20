# 장산범 v2 (11-20 Kido: "위에 면이 다 깨졌는데"): the Tripo body is ~3700 loose fur cards, each with its own flat normal, so in
# Unity every card catches the light on its own and the back reads as shattered facets (Blender's soft render hides it).
# Standard hair-card fix: give the cards the normals of a smooth proxy shell instead of their own. Here the shell is the
# skeleton as capsules: each vertex's normal points straight out from the nearest bone (blended a little with the
# card's own normal so it is not dead flat). Neighbouring cards now shade as one smooth furry surface.
# (Laying the cards flat was tried first: v2_fur_flatten.py - it turned them into big sheets, worse.)
#   blender -b jsb_v2_full.blend -P v2_fur_shellnormals.py -- <out.blend> [own 0.25]
import bpy, sys
import numpy as np
from mathutils import Vector
a = sys.argv[sys.argv.index('--') + 1:]
OUT = a[0]; OWN = float(a[1]) if len(a) > 1 else 0.25
body = [o for o in bpy.data.objects if o.type == 'MESH' and o.name.startswith('tripo')][0]
arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
me = body.data
inv = body.matrix_world.inverted()
segs = [(inv @ (arm.matrix_world @ b.head_local), inv @ (arm.matrix_world @ b.tail_local)) for b in arm.data.bones
        if b.name not in ('Root',) and (b.tail_local - b.head_local).length > 1e-4]
H = np.array([s[0][:] for s in segs]); T = np.array([s[1][:] for s in segs]); AB = T - H; ABL = (AB * AB).sum(1)
co = np.array([v.co[:] for v in me.vertices])
nrm = np.zeros_like(co)
for i0 in range(0, len(co), 4000):
    P = co[i0:i0 + 4000]
    u = np.clip(((P[:, None, :] - H[None]) * AB[None]).sum(2) / ABL[None], 0, 1)
    Q = H[None] + AB[None] * u[..., None]
    d = np.linalg.norm(P[:, None, :] - Q, axis=2)
    # soft blend of the 3 nearest bones so the normal field has no seams between bones
    idx = np.argsort(d, axis=1)[:, :3]
    acc = np.zeros_like(P)
    for j in range(3):
        k = idx[:, j]; dd = d[np.arange(len(P)), k]; q = Q[np.arange(len(P)), k]
        w = 1.0 / np.maximum(dd, 1e-4) ** 3
        r = P - q; r /= np.maximum(np.linalg.norm(r, axis=1, keepdims=True), 1e-7)
        acc += r * w[:, None]
    nrm[i0:i0 + 4000] = acc / np.maximum(np.linalg.norm(acc, axis=1, keepdims=True), 1e-7)
# blend a little of the card's own (vertex) normal back in, flipped to face outward
own = np.array([v.normal[:] for v in me.vertices])
sgn = np.sign((own * nrm).sum(1, keepdims=True)); sgn[sgn == 0] = 1
n2 = nrm * (1 - OWN) + own * sgn * OWN
n2 /= np.maximum(np.linalg.norm(n2, axis=1, keepdims=True), 1e-7)
me.normals_split_custom_set_from_vertices([tuple(x) for x in n2])
print('SHELLNORMALS_OK verts', len(co), 'bones', len(segs), 'own', OWN)
bpy.ops.wm.save_as_mainfile(filepath=OUT, copy=True)
