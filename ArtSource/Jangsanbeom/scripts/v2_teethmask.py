# 장산범 v2: teeth mask for the howl (11-19 Kido: "이빨이 발광하게"). The teeth are found by texture colour, per pixel: inside the
# faces around the Jaw bone (the mouth), pixels with the yellowish ivory of the fangs (green clearly above blue, bright) are
# teeth; white fur (no colour), pink lips/gums (green = blue) and the dark brown inside of the mouth are not.
#   blender -b jsb_v2_full.blend -P v2_teethmask.py -- <out.png> [radiusFactor] [debugRenderPrefix]
import bpy, sys, math, numpy as np
from mathutils import Vector, Euler
a = sys.argv[sys.argv.index('--') + 1:]
out = a[0]; ELONG = float(a[1]) if len(a) > 1 else 1.6; dbg = a[2] if len(a) > 2 else None
body = [o for o in bpy.data.objects if o.type == 'MESH' and o.name.startswith('tripo')][0]; me = body.data
arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
img = max((im for im in bpy.data.images if im.size[0] > 0 and im.type == 'IMAGE'), key=lambda i: i.size[0] * i.size[1]); W, H = img.size
px = np.array(img.pixels[:], dtype=np.float32).reshape(H, W, 4)
uvl = me.uv_layers['UVMap'].data
V = np.array([body.matrix_world @ v.co for v in me.vertices])
jb = arm.data.bones['Jaw']
J0 = np.array(arm.matrix_world @ jb.head_local); J1 = np.array(arm.matrix_world @ jb.tail_local)
ab = J1 - J0; L = np.linalg.norm(ab)
t = np.clip(((V - J0) @ ab) / (ab @ ab), 0, 1); d = np.linalg.norm(V - (J0 + np.outer(t, ab)), axis=1)
# teeth = small, pointed, separate pieces of mesh inside the mouth whose texture is the brownish ivory of the fangs (the fur
# tufts around the mouth are pale and unsaturated; tongue and palate are bigger, flatter pieces)
import bmesh
bm = bmesh.new(); bm.from_mesh(me); bm.verts.ensure_lookup_table()
isl = [-1] * len(bm.verts); islands = []
for v in bm.verts:
    if isl[v.index] >= 0: continue
    k = len(islands); st = [v]; isl[v.index] = k; mem = []
    while st:
        x = st.pop(); mem.append(x.index)
        for e in x.link_edges:
            y = e.other_vert(x)
            if isl[y.index] < 0: isl[y.index] = k; st.append(y)
    islands.append(mem)
vuv = {}
for l in me.loops:
    if l.vertex_index not in vuv: vuv[l.vertex_index] = (uvl[l.index].uv.x, uvl[l.index].uv.y)
sel = set(); nt = 0
for m in islands:
    if len(m) < 5 or len(m) > 60: continue
    P = V[m]; c = P.mean(0)
    if abs(c[0]) > 0.05 or not (-0.52 < c[1] < -0.40) or not (0.215 < c[2] < 0.31): continue
    cols = np.array([px[min(H - 1, int(vuv[i][1] * H)), min(W - 1, int(vuv[i][0] * W)), :3] for i in m if i in vuv])
    if len(cols) == 0: continue
    mc = cols.mean(0)
    if mc[0] - mc[2] < 0.19: continue                       # pale fur
    sv = np.linalg.svd(P - c, compute_uv=False)
    if sv[0] / max(sv[1], 1e-9) < ELONG: continue           # not pointed
    ext = P.max(0) - P.min(0)
    if ext.max() > 0.045: continue
    if ext[2] < 1.4 * max(ext[0], ext[1]) or max(ext[0], ext[1]) > 0.016: continue   # a fang stands upright and is thin
    if 0.3 * mc[0] + 0.59 * mc[1] + 0.11 * mc[2] > 0.42: continue
    sel.update(m); nt += 1
print('TEETH islands', nt, 'verts', len(sel))
mask = np.zeros((H, W), dtype=np.float32); nf = 0
for poly in me.polygons:
    if not all(v in sel for v in poly.vertices): continue
    uv = [uvl[li].uv for li in poly.loop_indices]
    for k in range(1, len(uv) - 1):
        tri = np.array([[uv[0].x * W, uv[0].y * H], [uv[k].x * W, uv[k].y * H], [uv[k + 1].x * W, uv[k + 1].y * H]])
        x0, y0 = np.floor(tri.min(0)).astype(int) - 1; x1, y1 = np.ceil(tri.max(0)).astype(int) + 1
        x0, y0 = max(0, x0), max(0, y0); x1, y1 = min(W - 1, x1), min(H - 1, y1)
        if x1 < x0 or y1 < y0: continue
        xs, ys = np.meshgrid(np.arange(x0, x1 + 1) + 0.5, np.arange(y0, y1 + 1) + 0.5)
        dd = (tri[1, 1] - tri[2, 1]) * (tri[0, 0] - tri[2, 0]) + (tri[2, 0] - tri[1, 0]) * (tri[0, 1] - tri[2, 1])
        if abs(dd) < 1e-9: continue
        l0 = ((tri[1, 1] - tri[2, 1]) * (xs - tri[2, 0]) + (tri[2, 0] - tri[1, 0]) * (ys - tri[2, 1])) / dd
        l1 = ((tri[2, 1] - tri[0, 1]) * (xs - tri[2, 0]) + (tri[0, 0] - tri[2, 0]) * (ys - tri[2, 1])) / dd
        l2 = 1 - l0 - l1
        mask[y0:y1 + 1, x0:x1 + 1][(l0 >= -0.08) & (l1 >= -0.08) & (l2 >= -0.08)] = 1.0
    nf += 1
print('mouth faces', nf, 'tooth pixels', int(mask.sum()), 'coverage %.5f' % float(mask.mean()))
N = 1024; f = W // N
small = mask.reshape(N, f, N, f).max(axis=(1, 3)) if f >= 1 and W % N == 0 else mask
o = bpy.data.images.new('JSB2_TeethMask', small.shape[1], small.shape[0], alpha=False)
rgba = np.zeros((small.shape[0], small.shape[1], 4), dtype=np.float32); rgba[..., 0] = small; rgba[..., 1] = small; rgba[..., 2] = small; rgba[..., 3] = 1
o.pixels = rgba.ravel(); o.filepath_raw = out; o.file_format = 'PNG'; o.save()
print('TEETHMASK_OK', out, small.shape)
if dbg:
    # paint the mask green onto the texture and render the open mouth, to check by eye
    p2 = px.copy(); m = mask > 0.5; p2[m, 0] = 0; p2[m, 1] = 1; p2[m, 2] = 0
    img.pixels = p2.ravel()
    for p in arm.pose.bones: p.matrix_basis.identity()
    if arm.animation_data: arm.animation_data.action = None
    jaw = arm.pose.bones['Jaw']; jaw.rotation_mode = 'XYZ'
    bpy.context.view_layer.update(); z0 = (arm.matrix_world @ jaw.tail).z
    jaw.rotation_euler = Euler((math.radians(30), 0, 0)); bpy.context.view_layer.update()
    if (arm.matrix_world @ jaw.tail).z > z0: jaw.rotation_euler = Euler((math.radians(-30), 0, 0))
    sc = bpy.context.scene
    for ob in sc.objects:
        if ob.type == 'MESH' and not ob.name.startswith('tripo'): ob.hide_render = True
    sc.render.engine = 'BLENDER_WORKBENCH'; sc.display.shading.light = 'FLAT'; sc.display.shading.color_type = 'TEXTURE'
    sc.render.resolution_x = 1400; sc.render.resolution_y = 1000
    cam = bpy.data.objects.new('C', bpy.data.cameras.new('C')); sc.collection.objects.link(cam); sc.camera = cam
    cam.data.type = 'ORTHO'; cam.data.ortho_scale = 0.2
    tgt = Vector((0, -0.46, 0.265)); cam.location = tgt + Vector((0.3, -1, -0.5)); cam.rotation_euler = (tgt - cam.location).to_track_quat('-Z', 'Y').to_euler()
    sc.render.filepath = dbg; bpy.ops.render.render(write_still=True)
    print('DEBUG_OK', dbg)
