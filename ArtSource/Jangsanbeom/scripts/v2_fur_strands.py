# 장산범 v2 (11-19 Kido: "점들로 발광하는 게 아니라 털이 빛나는 것처럼 길게"): the Tripo fur is ~3700 loose tufts (cards). For every
# tuft write a second UV channel "Strand": u = 0 at the root (the end nearest the body) .. 1 at the tip, v = a random 0..1 id
# per tuft. The body itself (the biggest island) gets v = -1 (not a strand). The glow shader runs light along u.
#   blender -b jsb_v2_snake.blend -P v2_fur_strands.py -- <out.blend>
import bpy, bmesh, sys, random
import numpy as np
from mathutils import Vector, bvhtree
random.seed(19)
out = sys.argv[sys.argv.index('--') + 1]
body = [o for o in bpy.data.objects if o.type == 'MESH' and o.name.startswith('tripo')][0]
me = body.data
bm = bmesh.new(); bm.from_mesh(me); bm.verts.ensure_lookup_table(); bm.faces.ensure_lookup_table()
# islands by connectivity
island = [-1] * len(bm.verts); islands = []
for v in bm.verts:
    if island[v.index] >= 0: continue
    k = len(islands); stack = [v]; island[v.index] = k; members = []
    while stack:
        x = stack.pop(); members.append(x.index)
        for e in x.link_edges:
            y = e.other_vert(x)
            if island[y.index] < 0: island[y.index] = k; stack.append(y)
    islands.append(members)
sizes = [len(m) for m in islands]; main = int(np.argmax(sizes))
print('ISLANDS', len(islands), 'main', sizes[main], 'verts of', len(bm.verts))
co = np.array([v.co[:] for v in bm.verts])
# BVH of the main body to tell which end of a tuft is the root
mb = bmesh.new()
mverts = {}
for vi in islands[main]: mverts[vi] = mb.verts.new(bm.verts[vi].co)
for f in bm.faces:
    if island[f.verts[0].index] == main:
        try: mb.faces.new([mverts[v.index] for v in f.verts])
        except ValueError: pass
tree = bvhtree.BVHTree.FromBMesh(mb)
arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
segs = [(arm.matrix_world @ b.head_local, arm.matrix_world @ b.tail_local) for b in arm.data.bones if b.name not in ('Root',)]
inv = body.matrix_world.inverted()
segs = [(inv @ a_, inv @ b_) for a_, b_ in segs]
def seg_dist(p):
    best = 1e9
    for a_, b_ in segs:
        ab = b_ - a_; t = max(0.0, min(1.0, (p - a_).dot(ab) / max(1e-9, ab.dot(ab))))
        best = min(best, (a_ + ab * t - p).length)
    return best
along = np.zeros(len(bm.verts)); rid = np.full(len(bm.verts), -1.0); across = np.zeros(len(bm.verts))
n_strand = 0
for k, members in enumerate(islands):
    if len(members) < 4: continue
    P = co[members]; c = P.mean(0)
    _, _, vt = np.linalg.svd(P - c, full_matrices=False); ax = vt[0]
    t = (P - c) @ ax; t0, t1 = t.min(), t.max()
    if t1 - t0 < 1e-5: continue
    e0 = Vector(c + ax * t0); e1 = Vector(c + ax * t1)
    d0 = seg_dist(e0); d1 = seg_dist(e1)   # the root is the end nearer the skeleton (the 'body' island is tiny: the mesh is all tufts)
    u = (t - t0) / (t1 - t0)
    if d1 < d0: u = 1.0 - u            # the root is the end that sits on the body
    w = (P - c) @ vt[1]; w0, w1 = w.min(), w.max()   # 11-19 '몸 왜이래': across-the-card coordinate so the shader draws thin hairs, not whole lit cards
    across[members] = (w - w0) / max(1e-6, w1 - w0)
    along[members] = u; rid[members] = random.random(); n_strand += 1
print('STRANDS', n_strand)
# write the second UV layer
uv = me.uv_layers.get('Strand') or me.uv_layers.new(name='Strand')
for loop in me.loops:
    vi = loop.vertex_index; uv.data[loop.index].uv = (float(along[vi]), float(rid[vi]))
ux = me.uv_layers.get('StrandX') or me.uv_layers.new(name='StrandX')
for loop in me.loops:
    ux.data[loop.index].uv = (float(across[loop.vertex_index]), 0.0)
me.uv_layers.active_index = 0
for i, l in enumerate(me.uv_layers): print('UVLAYER', i, l.name)
bpy.ops.wm.save_as_mainfile(filepath=out, copy=True)
print('STRANDS_OK', out)
