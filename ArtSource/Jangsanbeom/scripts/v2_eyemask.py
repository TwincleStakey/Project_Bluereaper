# 장산범 v2: eye mask for the glow shader (white where the eyeballs are in UV space).
#   blender -b jsb_v2_tripo.glb-imported or rigged.blend -P v2_eyemask.py -- <out.png> [debug.png]
# Eyes are found on the mesh, not painted by hand: vertices on the front of the head, at eye height, whose texture colour
# is the dark saturated red of the eyeballs; the faces around them are rasterised into a 1024 mask.
import bpy, sys, numpy as np
from mathutils import Vector
a = sys.argv[sys.argv.index('--') + 1:]
out = a[0]
body = [o for o in bpy.data.objects if o.type == 'MESH' and o.name.startswith('tripo')][0]
me = body.data
img = max((im for im in bpy.data.images if im.size[0] > 0 and im.type == 'IMAGE'), key=lambda i: i.size[0] * i.size[1])
W, H = img.size
px = np.array(img.pixels[:], dtype=np.float32).reshape(H, W, 4)
uvl = me.uv_layers.active.data
V = np.array([body.matrix_world @ v.co for v in me.vertices])
# vertex colour from the texture (first loop that uses the vertex)
vuv = {}
for l in me.loops:
    if l.vertex_index not in vuv: vuv[l.vertex_index] = uvl[l.index].uv.copy()
def tex(uv):
    x = min(W - 1, max(0, int(uv.x * W))); y = min(H - 1, max(0, int(uv.y * H))); return px[y, x, :3]
head = [i for i in range(len(V)) if V[i][1] < -0.43 and 0.27 < V[i][2] < 0.36 and abs(V[i][0]) < 0.06 and i in vuv]
cand = []
for i in head:
    r, g, b = tex(vuv[i]); lum = 0.3 * r + 0.59 * g + 0.11 * b
    if r > 0.12 and r > g * 2.2 and r > b * 2.0 and lum < 0.22: cand.append(i)
print('HEAD verts', len(head), 'red-dark candidates', len(cand))
P = V[cand] if cand else np.zeros((0, 3))
eyes = []
for sx in (1, -1):
    m = P[np.sign(P[:, 0]) == sx] if len(P) else P
    if len(m) == 0: continue
    # the eye is the densest red cluster on that side, in the upper part of the face (the mouth is lower)
    top = m[m[:, 2] > np.percentile(m[:, 2], 55)]
    c = np.median(top, axis=0); eyes.append(c)
    print('EYE', sx, tuple(round(float(v), 4) for v in c), 'from', len(top), 'verts')
R = 0.011
sel = set()
for c in eyes:
    d = np.linalg.norm(V - c, axis=1)
    for i in np.nonzero(d < R)[0]:
        if int(i) in vuv:
            r, g, b = tex(vuv[int(i)])
            if r > g * 1.5: sel.add(int(i))
print('eye verts', len(sel))
N = 1024
mask = np.zeros((N, N), dtype=np.float32)
nf = 0
for poly in me.polygons:
    if not all(v in sel for v in poly.vertices): continue
    uv = [uvl[li].uv for li in poly.loop_indices]
    for k in range(1, len(uv) - 1):
        tri = np.array([[uv[0].x, uv[0].y], [uv[k].x, uv[k].y], [uv[k + 1].x, uv[k + 1].y]]) * N
        x0, y0 = np.floor(tri.min(0)).astype(int) - 1; x1, y1 = np.ceil(tri.max(0)).astype(int) + 1
        x0, y0 = max(0, x0), max(0, y0); x1, y1 = min(N - 1, x1), min(N - 1, y1)
        xs, ys = np.meshgrid(np.arange(x0, x1 + 1) + 0.5, np.arange(y0, y1 + 1) + 0.5)
        d = (tri[1, 1] - tri[2, 1]) * (tri[0, 0] - tri[2, 0]) + (tri[2, 0] - tri[1, 0]) * (tri[0, 1] - tri[2, 1])
        if abs(d) < 1e-9: continue
        l0 = ((tri[1, 1] - tri[2, 1]) * (xs - tri[2, 0]) + (tri[2, 0] - tri[1, 0]) * (ys - tri[2, 1])) / d
        l1 = ((tri[2, 1] - tri[0, 1]) * (xs - tri[2, 0]) + (tri[0, 0] - tri[2, 0]) * (ys - tri[2, 1])) / d
        l2 = 1 - l0 - l1
        inside = (l0 >= -0.15) & (l1 >= -0.15) & (l2 >= -0.15)        # a little bleed past the edges so seams do not show
        mask[y0:y1 + 1, x0:x1 + 1][inside] = 1.0
    nf += 1
print('faces', nf, 'mask coverage %.4f' % float(mask.mean()))
o = bpy.data.images.new('JSB2_EyeMask', N, N, alpha=False)
rgba = np.zeros((N, N, 4), dtype=np.float32); rgba[..., 0] = mask; rgba[..., 1] = mask; rgba[..., 2] = mask; rgba[..., 3] = 1
o.pixels = rgba.ravel(); o.filepath_raw = out; o.file_format = 'PNG'; o.save()
print('EYEMASK_OK', out)
