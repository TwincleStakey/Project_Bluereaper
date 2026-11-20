# Snake parasite for 장산범 (replaces the flat iron band + tendril of parasite_v2).
# One round, ridged, worm/snake body: its head end is coiled around the gramophone stand and drum (so the gramophone
# and the parasite are one thing), it dives into the front shoulder, then surfaces and burrows again along the torso
# (arches). Every entry/exit is a torn hole with a swollen lip, a little blood and jagged veins spreading out.
# Run on werewolf_gramophone_v4.blend. Body faces -Y, left +X, up +Z; model height 0.43.
import bpy, bmesh, math, sys, random
from mathutils import Vector, Matrix, kdtree, noise
import mathutils
random.seed(11)
OUT = sys.argv[sys.argv.index('--') + 1]
P = r"C:/Users/PC/video2rig/projects/werewolf/props/"
arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
body = [o for o in bpy.data.objects if o.type == 'MESH' and o.name.startswith('tripo')][0]

# gramophone 1.3x and pushed into the front shoulder (same as band_v4)
M0 = Vector((0.02, -0.175, 0.372)); MOUNT = M0 + Vector((0, 0.06, 0)); SCL = 1.3
XF = Matrix.Translation(MOUNT) @ Matrix.Scale(SCL, 4) @ Matrix.Translation(-M0)
for o_ in bpy.data.collections['Gramophone'].objects: o_.matrix_world = XF @ o_.matrix_world
bpy.context.view_layer.update()
C = Vector((0.02, -0.155, 0.478)); D = Vector((0, -1, 0.16)).normalized()
DRUM_R, DRUM_L, SQ = 0.030, 0.075, 0.78
Bm = D.to_track_quat('Z', 'Y').to_matrix().to_4x4()
def at(l): l = Vector(l); l.y *= SQ; return MOUNT + (C + (Bm @ l.to_4d()).to_3d() - M0) * SCL

bvh = mathutils.bvhtree.BVHTree.FromObject(body, bpy.context.evaluated_depsgraph_get())
col = bpy.data.collections.new('SnakeParasite'); bpy.context.scene.collection.children.link(col)

# ---------------- materials ----------------
TX = P + "tex_dl/bark_brown_02_"
def flesh_mat(name, dark, red, crust, emit_strength):
    """dark-red wet flesh with patches of grey-brown dried crust (Diablo IV tendril ref) + fibrous bark normal"""
    m = bpy.data.materials.new(name); m.use_nodes = True; nt = m.node_tree; b = nt.nodes['Principled BSDF']; L = nt.links.new
    tc = nt.nodes.new('ShaderNodeTexCoord')
    nz = nt.nodes.new('ShaderNodeTexNoise'); nz.inputs['Scale'].default_value = 60; nz.inputs['Detail'].default_value = 8; nz.inputs['Roughness'].default_value = 0.65
    L(tc.outputs['Object'], nz.inputs['Vector'])
    cr = nt.nodes.new('ShaderNodeValToRGB'); e = cr.color_ramp.elements
    e[0].position = 0.50; e[0].color = (0, 0, 0, 1); e[1].position = 0.60; e[1].color = (1, 1, 1, 1)
    L(nz.outputs['Fac'], cr.inputs['Fac'])
    uv = nt.nodes.new('ShaderNodeUVMap'); mp = nt.nodes.new('ShaderNodeMapping'); mp.inputs['Scale'].default_value = (14, 2, 1)
    L(uv.outputs['UV'], mp.inputs['Vector'])
    def img(k, nc):
        t = nt.nodes.new('ShaderNodeTexImage'); t.image = bpy.data.images.load(TX + k + '_1k.jpg', check_existing=True)
        if nc: t.image.colorspace_settings.name = 'Non-Color'
        L(mp.outputs['Vector'], t.inputs['Vector']); return t
    d, n = img('diff', False), img('nor_gl', True)
    bw = nt.nodes.new('ShaderNodeRGBToBW'); L(d.outputs['Color'], bw.inputs['Color'])
    fr = nt.nodes.new('ShaderNodeValToRGB'); fr.color_ramp.elements[0].color = dark; fr.color_ramp.elements[1].color = red
    L(bw.outputs['Val'], fr.inputs['Fac'])
    mix = nt.nodes.new('ShaderNodeMix'); mix.data_type = 'RGBA'
    L(cr.outputs['Color'], mix.inputs['Factor']); L(fr.outputs['Color'], mix.inputs[6]); mix.inputs[7].default_value = crust
    L(mix.outputs[2], b.inputs['Base Color'])
    rg = nt.nodes.new('ShaderNodeMapRange'); rg.inputs['To Min'].default_value = 0.22; rg.inputs['To Max'].default_value = 0.75
    L(cr.outputs['Color'], rg.inputs['Value']); L(rg.outputs['Result'], b.inputs['Roughness'])
    nm = nt.nodes.new('ShaderNodeNormalMap'); nm.inputs['Strength'].default_value = 2.0
    L(n.outputs['Color'], nm.inputs['Color']); L(nm.outputs['Normal'], b.inputs['Normal'])
    b.inputs['Emission Color'].default_value = (1.0, 0.06, 0.02, 1); b.inputs['Emission Strength'].default_value = emit_strength
    return m
def plain(name, base, rough, emit=(0, 0, 0, 1), es=0.0):
    m = bpy.data.materials.new(name); m.use_nodes = True; b = m.node_tree.nodes['Principled BSDF']
    b.inputs['Base Color'].default_value = base; b.inputs['Roughness'].default_value = rough
    b.inputs['Emission Color'].default_value = emit; b.inputs['Emission Strength'].default_value = es
    return m
M_SNAKE = flesh_mat('SnakeFlesh', (0.012, 0.002, 0.002, 1), (0.19, 0.022, 0.018, 1), (0.085, 0.065, 0.055, 1), 0.03)
M_LIP = flesh_mat('HoleLip', (0.03, 0.005, 0.005, 1), (0.28, 0.04, 0.035, 1), (0.10, 0.075, 0.06, 1), 0.02)
M_HOLE = plain('HoleInside', (0.008, 0.0, 0.0, 1), 0.3, (1.0, 0.05, 0.02, 1), 0.4)
M_BLOOD = plain('Blood', (0.055, 0.002, 0.002, 1), 0.06)
M_VEIN = flesh_mat('Vein', (0.02, 0.004, 0.008, 1), (0.13, 0.02, 0.03, 1), (0.08, 0.06, 0.06, 1), 0.02)
MATS = [M_SNAKE, M_LIP, M_HOLE, M_BLOOD, M_VEIN]

# ---------------- body helpers ----------------
LEG = ('Thigh', 'Shin', 'Paw', 'Toes', 'UpperArm', 'Forearm', 'Scapula')
gname = {g.index: g.name for g in body.vertex_groups}
def leg_face(fi):
    acc = {}
    for vi in body.data.polygons[fi].vertices:
        for g in body.data.vertices[vi].groups: acc[gname[g.group]] = acc.get(gname[g.group], 0) + g.weight
    return bool(acc) and max(acc, key=acc.get).startswith(LEG)
def torso_radius(c, d):
    o = c + d * 0.5; remaining = 0.7
    while remaining > 0:
        hit = bvh.ray_cast(o, -d, remaining)
        if hit[0] is None: break
        if not leg_face(hit[2]): return max((hit[0] - c).length, 0.07)
        step = (hit[0] - o).length + 0.002; o = o - d * step; remaining -= step
    return 0.11
def on_skin(p, lift=0.0):
    h = bvh.find_nearest(p); return h[0] + h[1] * lift, h[1]
def smooth(pts, it):
    for _ in range(it): pts = [pts[0]] + [(pts[k - 1] + pts[k] * 2 + pts[k + 1]) / 4 for k in range(1, len(pts) - 1)] + [pts[-1]]
    return pts

class Mesh:
    def __init__(s): s.bm = bmesh.new(); s.uv = s.bm.loops.layers.uv.new(); s.u = 0.0
    def tube(s, pts, rad, segs=14, mi=0, rough=0.0, ridge=0.0, cap=False):
        """round tube; rad(t) radius; rough = noise displacement (not a smooth pipe); ridge = worm-like rings"""
        bm = s.bm; n = len(pts); lens = [0.0]
        for a, b in zip(pts, pts[1:]): lens.append(lens[-1] + (b - a).length)
        rings = []; prev = None
        for i, p in enumerate(pts):
            tg = (pts[min(i + 1, n - 1)] - pts[max(i - 1, 0)]).normalized()
            if prev is None: side = tg.orthogonal().normalized()
            else: side = (prev - tg * prev.dot(tg)).normalized()   # parallel transport: no twisting
            prev = side; nrm = tg.cross(side)
            r = max(rad(i / (n - 1)), 0.0004)
            rr = r * (1 + ridge * (0.5 + 0.5 * math.cos(lens[i] / 0.0075 * 2 * math.pi)) ** 3)
            ring = []
            for k in range(segs):
                a = 2 * math.pi * k / segs; dv = side * math.cos(a) + nrm * math.sin(a)
                q = p + dv * rr
                dn = noise.noise(q * 260) * 0.55 + noise.noise(q * 900) * 0.25 + noise.noise(q * 70) * 0.6
                ring.append(bm.verts.new(q + dv * rr * rough * dn))
            rings.append(ring)
        for i in range(n - 1):
            for k in range(segs):
                f = bm.faces.new((rings[i][k], rings[i][(k + 1) % segs], rings[i + 1][(k + 1) % segs], rings[i + 1][k]))
                f.material_index = mi; f.smooth = True
                for lp, u, v in zip(f.loops, (lens[i], lens[i], lens[i + 1], lens[i + 1]), (k / segs, (k + 1) / segs, (k + 1) / segs, k / segs)):
                    lp[s.uv].uv = (s.u + u, v)
        if cap:
            for ring in (rings[0], rings[-1]):
                f = bm.faces.new(ring if ring is rings[-1] else list(reversed(ring))); f.material_index = mi; f.smooth = True
        s.u += lens[-1]
    def patch(s, center, nrm, radius, mi, lift, jag=0.45, n=18, stretch=None):
        """irregular flat splat conformed to the skin (blood / hole interior)"""
        bm = s.bm; t1 = (stretch or nrm.orthogonal()).normalized(); t1 = (t1 - nrm * t1.dot(nrm)).normalized(); t2 = nrm.cross(t1)
        c0 = bm.verts.new(on_skin(center, lift)[0]); ring = []
        ph = random.uniform(0, 9)
        for k in range(n):
            a = 2 * math.pi * k / n
            r = radius * (1 + jag * (noise.noise(Vector((math.cos(a) * 2 + ph, math.sin(a) * 2, ph))) + 0.35 * math.sin(a * 5 + ph)))
            mid = on_skin(center + (t1 * math.cos(a) * 1.0 + t2 * math.sin(a)) * r * 0.55, lift)[0]
            ring.append((bm.verts.new(mid), bm.verts.new(on_skin(center + (t1 * math.cos(a) + t2 * math.sin(a)) * r, lift)[0])))
        for k in range(n):
            a, b = ring[k], ring[(k + 1) % n]
            for vs in ((c0, a[0], b[0]), (a[0], a[1], b[1], b[0])):
                f = bm.faces.new(vs); f.material_index = mi; f.smooth = True
                for lp in f.loops: lp[s.uv].uv = (s.u, 0.5)
    def obj(s, name):
        me = bpy.data.meshes.new(name); s.bm.to_mesh(me); s.bm.free()
        for m_ in MATS: me.materials.append(m_)
        o = bpy.data.objects.new(name, me); col.objects.link(o); return o

R0 = 0.0160                                      # snake radius at the shoulder

def hole(mesh, p, tg, r):
    """torn entry/exit hole: swollen ragged lip, dark inside, a little blood, jagged veins"""
    surf, nrm = on_skin(p)
    t1 = (tg - nrm * tg.dot(nrm)).normalized(); t2 = nrm.cross(t1)
    mesh.patch(surf, nrm, r * 1.25, 2, 0.0006, jag=0.2, stretch=t1)                 # dark wound under the body
    ring = []
    for k in range(29):
        a = 2 * math.pi * k / 28
        rr = r * 1.35 * (1 + 0.18 * noise.noise(Vector((math.cos(a) * 3, math.sin(a) * 3, p.x * 50))))
        q, _ = on_skin(surf + (t1 * math.cos(a) * 1.35 + t2 * math.sin(a)) * rr, 0.0015)
        ring.append(q)
    ring[-1] = ring[0]
    mesh.tube(ring, lambda t: r * (0.38 + 0.18 * math.sin(t * 2 * math.pi * 5 + p.y * 40)), segs=8, mi=1, rough=0.35)
    # blood: a splat around the lip and a couple of short trickles, staying on the skin
    mesh.patch(surf + t1 * random.uniform(-0.5, 0.5) * r, nrm, r * random.uniform(1.5, 1.9), 3, 0.0009, jag=0.4, n=30)
    down = Vector((0, 0, -1)); down = (down - nrm * down.dot(nrm))
    if down.length > 0.3:
        down.normalize()
        for _ in range(random.randint(1, 2)):
            st = surf + (t2 * random.uniform(-1, 1) + down) * r * 1.6; ln = random.uniform(0.012, 0.028); pts = []
            for q in range(8):
                f = q / 7; pts.append(on_skin(st + down * ln * f + t2 * 0.002 * math.sin(f * 7), 0.0008)[0])
            mesh.tube(pts, lambda t: 0.0017 * (1 - 0.5 * t) + (0.0012 if t > 0.85 else 0), segs=6, mi=3, cap=True)
    # jagged, branching veins radiating from the hole (not smooth: kinks, uneven thickness)
    for _ in range(random.randint(2, 3)):
        ang = random.uniform(0, 2 * math.pi); vein(mesh, surf + (t1 * math.cos(ang) + t2 * math.sin(ang)) * r * 1.5,
                                                  t1 * math.cos(ang) + t2 * math.sin(ang), random.uniform(0.05, 0.09), 0.0019, 2)

def vein(mesh, start, direction, length, r0, depth):
    pts = []; p = start.copy(); d = direction.normalized(); step = 0.003
    nsteps = max(4, int(length / step)); branches = []
    for i in range(nsteps):
        surf, nrm = on_skin(p)
        d = (d - nrm * d.dot(nrm)).normalized()
        pts.append(surf + nrm * (r0 * 0.35))
        turn = random.gauss(0, 0.45)                                                    # kinks
        d = (Matrix.Rotation(turn, 3, nrm) @ d).normalized(); p = surf + d * step
        if depth > 0 and i > 3 and random.random() < 0.10: branches.append((surf.copy(), (Matrix.Rotation(random.choice((-1, 1)) * random.uniform(0.5, 1.0), 3, nrm) @ d)))
    if len(pts) < 3: return
    mesh.tube(pts, lambda t: r0 * (1 - 0.8 * t) * (1 + 0.35 * math.sin(t * 37) * math.sin(t * 13)), segs=6, mi=4, rough=0.25)
    for bp, bd in branches[:2]: vein(mesh, bp, bd, length * 0.45, r0 * 0.6, depth - 1)

# ---------------- torso path: shoulder -> hips, spiralling around the torso only ----------------
SH = Vector((0.02, -0.10, 0.30)); HP = Vector((0.02, 0.19, 0.28)); TURNS = 3.0; N = 1600
skin, outs = [], []
for i in range(N + 1):
    t = i / N; c = SH.lerp(HP, t); a = math.pi / 2 + t * TURNS * 2 * math.pi
    d = Vector((math.cos(a), 0.0, math.sin(a))); r = min(torso_radius(c, d), 0.15)
    skin.append(c + d * r); outs.append(d)
skin = smooth(skin, 6)
# lateral S-wiggle so it reads as a snake, not a hoop
tang = [(skin[min(i + 1, N)] - skin[max(i - 1, 0)]).normalized() for i in range(N + 1)]
skin = [p + tang[i].cross(outs[i]).normalized() * 0.010 * math.sin(i / N * 2 * math.pi * 15) for i, p in enumerate(skin)]
snaps = [on_skin(p) for p in skin]
def r_snake(t): return R0 * (1 - 0.45 * t)
ARCHES = [(0.02, 0.11), (0.15, 0.25), (0.29, 0.37), (0.41, 0.52), (0.56, 0.64), (0.68, 0.79), (0.83, 0.91), (0.95, 1.0)]
body_mesh = Mesh()
for ai, (t0, t1) in enumerate(ARCHES):
    i0, i1 = int(t0 * N), int(t1 * N); tail = ai == len(ARCHES) - 1
    pts, rads = [], []
    for i in range(i0, i1 + 1):
        u = (i - i0) / (i1 - i0); t = i / N; r = r_snake(t)
        if tail: h = -1.6 * r + 2.1 * r * min(1, u / 0.4) ** 0.6 - (0.0 if u < 0.6 else 0.3 * r * (u - 0.6) / 0.4)
        else: h = -1.6 * r + (1.6 * r + 0.75 * r) * math.sin(math.pi * u) ** 0.45
        surf, nrm = snaps[i]; pts.append(surf + nrm * h); rads.append(r * (1 if not tail else max(0.12, 1 - u ** 1.5)))
    pts = smooth(pts, 3)
    body_mesh.tube(pts, lambda t, rads=rads: rads[min(int(t * (len(rads) - 1)), len(rads) - 1)], segs=16, mi=0, rough=0.18, ridge=0.05, cap=True)
    # holes where it crosses the skin
    def crossing(rng):
        for j in rng:
            surf, nrm = on_skin(pts[j])
            if (pts[j] - surf).dot(nrm) > -0.15 * rads[j]: return j
        return None
    j = crossing(range(len(pts)))
    if j is not None: hole(body_mesh, pts[j], pts[min(j + 1, len(pts) - 1)] - pts[j], rads[j])
    if not tail:
        j = crossing(range(len(pts) - 1, -1, -1))
        if j is not None: hole(body_mesh, pts[j], pts[j] - pts[max(j - 1, 0)], rads[j])
body_obj = body_mesh.obj('SnakeBody')

# ---------------- top: from the shoulder hole up around the stand, coiled on the drum, head into the drum ----------------
top_mesh = Mesh()
H0, n0 = on_skin(MOUNT + Vector((0.0, 0.035, 0.0)))
stand_top = at((0, -DRUM_R * 1.05, 0.0))
pts = [H0 - n0 * 0.02, H0 - n0 * 0.005]
for i in range(1, 50):                                   # 1.75 turns around the stand
    t = i / 49; c = (H0 + n0 * 0.004).lerp(stand_top, t); a = t * 1.75 * 2 * math.pi - math.pi / 2
    rr = 0.016 * (1 - 0.3 * t)
    pts.append(c + Vector((math.cos(a), math.sin(a), 0)) * rr)
for i in range(1, 80):                                   # 1.4 turns around the drum, drifting forward
    t = i / 79; z = -DRUM_L * 0.4 + t * DRUM_L * 0.8; a = -math.pi / 2 + t * 1.4 * 2 * math.pi
    rr = DRUM_R + R0 * 0.85 if t < 0.85 else DRUM_R + R0 * 0.85 * (1 - (t - 0.85) / 0.15 * 1.4)   # head dives into the drum
    pts.append(at((rr * math.cos(a), rr * math.sin(a), z)))
pts = smooth(pts, 4)
top_mesh.tube(pts, lambda t: R0 * (1.05 - 0.25 * t), segs=16, mi=0, rough=0.18, ridge=0.05, cap=True)
hole(top_mesh, H0 + n0 * 0.002, Vector((0, 0, 1)), R0)
top_obj = top_mesh.obj('SnakeTop')

# ---------------- skinning ----------------
kd = kdtree.KDTree(len(body.data.vertices))
for v in body.data.vertices: kd.insert(body.matrix_world @ v.co, v.index)
kd.balance()
def torso_weights(p):
    acc = {}
    for co, idx, d in kd.find_n(p, 10):
        w_ = 1 / (d + 1e-4)
        for g in body.data.vertices[idx].groups:
            if not gname[g.group].startswith(LEG): acc[gname[g.group]] = acc.get(gname[g.group], 0) + g.weight * w_
    t = sum(acc.values()) or 1; return {n: w / t for n, w in acc.items() if w / t > 0.02} or {'Spine2': 1.0}
gram_bone = bpy.data.objects['Drum'].parent_bone
def assign(o, weights):
    for v in o.data.vertices:
        for n, w in weights(v.co).items():
            if w > 0: (o.vertex_groups.get(n) or o.vertex_groups.new(name=n)).add([v.index], w, 'REPLACE')
    o.parent = arm; o.matrix_parent_inverse = arm.matrix_world.inverted()
    o.modifiers.new('Armature', 'ARMATURE').object = arm
assign(body_obj, torso_weights)
def top_w(p):                                            # shoulder part follows the torso, the coil lifts with the gramophone
    k = max(0.0, min(1.0, (p.z - (H0.z + 0.01)) / 0.04)); k = k * k * (3 - 2 * k)
    w = {n: x * (1 - k) for n, x in torso_weights(p).items()}; w[gram_bone] = w.get(gram_bone, 0) + k; return w
assign(top_obj, top_w)
print('SNAKE verts', len(body_obj.data.vertices), len(top_obj.data.vertices), 'gram bone', gram_bone)
bpy.ops.wm.save_as_mainfile(filepath=OUT, copy=True)
