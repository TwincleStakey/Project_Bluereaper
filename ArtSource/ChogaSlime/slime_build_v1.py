# 珥덇?吏??щ씪??(Choga Slime) mid-boss: body + two arms from metaballs, rigged arms, eyes.
# Run: blender -b --python slime_build.py
# Blender coords: Z up, house front = -Y (AV_Choga_A doors face -Y). House: walls x +-3.28, front wall y -2.1,
# eaves z 3.2 at x +-3.9 / y +-2.73, roof peak z 5.1.
import bpy, bmesh, math, json, os, random
from mathutils import Vector, Quaternion
from mathutils.bvhtree import BVHTree

HERE = os.path.dirname(os.path.abspath(__file__))
OUT_FBX = r"C:\UnityProject\BulletHeavenArt\Assets\Game\ChogaSlime\Models\ChogaSlime.fbx"
HOUSE = r"C:\UnityProject\BulletHeavenArt\Assets\Game\AbandonedVillage\Models\AV_Choga_A.fbx"
random.seed(7)

bpy.ops.wm.read_factory_settings(use_empty=True)
scn = bpy.context.scene

def bez(p0, p1, p2, p3, t):
    u = 1 - t
    return p0 * (u*u*u) + p1 * (3*u*u*t) + p2 * (3*u*t*t) + p3 * (t*t*t)

# house first: the slime is shaped against its roof (ray-cast) so it drapes instead of floating
bpy.ops.import_scene.fbx(filepath=HOUSE)
house_objs = [o for o in scn.objects if o.type == 'MESH']
_hm = house_objs[0]; _hw = _hm.matrix_world
house_bvh = BVHTree.FromPolygons([_hw @ v.co for v in _hm.data.vertices], [list(p.vertices) for p in _hm.data.polygons])
def roof_z(x, y):
    h = house_bvh.ray_cast(Vector((x, y, 30)), Vector((0, 0, -1)))[0]
    return h.z if h is not None else None

def arm_path(side):
    """side -1 = left (-X), +1 = right. Returns dense list of (point, s, radius)."""
    sx = side
    S = Vector((3.2*sx, -0.2, 6.0))
    C = Vector((3.75*sx, -2.55, 2.3))     # hugging the front corner, below the eave
    T = Vector((1.7*sx, -2.42, 1.55))     # tip lying on the front wall
    P1 = S + Vector((1.5*sx, -0.3, -0.1))
    P2 = C + Vector((0.75*sx, 0.45, 1.5))
    Q1 = C + (C - P2).normalized() * 0.6
    Q2 = T + Vector((1.0*sx, -0.12, 0.2))
    pts = [bez(S, P1, P2, C, i/80) for i in range(80)] + [bez(C, Q1, Q2, T, i/40) for i in range(41)]
    out = []; s = 0.0
    for i, p in enumerate(pts):
        if i: s += (p - pts[i-1]).length
        out.append([p, s])
    L = s
    for o in out:
        f = o[1] / L
        # heavy shoulder, forearm, a fat grip where it wraps the corner, rounded (not pointed) tip
        r = 1.2 * (1 - f) ** 1.3 + 0.42 * (1 - (1 - f) ** 1.3)
        r += 0.16 * math.exp(-((f - 0.66) / 0.08) ** 2)
        o.append(r)
    return out, L

# ---------------- metaball body ----------------
mb = bpy.data.metaballs.new("SlimeMB")
mb.resolution = 0.1; mb.render_resolution = 0.1; mb.threshold = 0.6
mbo = bpy.data.objects.new("SlimeMB", mb); scn.collection.objects.link(mbo)

def ell(co, size, r=1.0, stiff=2.0):
    # the element radius bounds its influence: largest half-extent goes in the radius, ratios in size
    mx = max(size); e = mb.elements.new(); e.type = 'ELLIPSOID'; e.co = co; e.radius = r * mx
    e.size_x, e.size_y, e.size_z = [s / mx for s in size]; e.stiffness = stiff; return e
def ball(co, r, stiff=2.0):
    e = mb.elements.new(); e.type = 'BALL'; e.co = co; e.radius = r; e.stiffness = stiff; return e
def caps(a, b, r, stiff=2.0):
    a = Vector(a); b = Vector(b); d = b - a
    e = mb.elements.new(); e.type = 'CAPSULE'; e.co = (a + b) / 2; e.radius = r; e.stiffness = stiff
    e.size_x = d.length / 2; e.rotation = Vector((1, 0, 0)).rotation_difference(d.normalized()); return e

# 1) a sheet of jelly lying on the thatch: balls just above the roof surface. Full over the back and middle,
#    thinning toward the front so the front eave and the doors stay visible under it.
for gx in range(-12, 13):
    for gy in range(-9, 10):
        x = gx * 0.32 + random.uniform(-0.08, 0.08); y = gy * 0.3 + random.uniform(-0.08, 0.08)
        e2 = (x / 3.75) ** 2 + (y / 2.6) ** 2
        if e2 > 1.0: continue
        z = roof_z(x, y)
        if z is None: continue
        front = max(0.0, (-y - 0.6) / 2.0)         # 0 behind, 1 at the front eave
        r = 0.95 * (1 - 0.45 * e2) * (1 - 0.5 * front)
        if r < 0.22: continue
        ball((x, y, z + 0.12), r, 2.0)
# 2) the heavy mass slumped on top, sagging backward and to the sides; crown in front for the eyes
ell((0, 0.45, 5.5), (5.3, 4.0, 2.9), 1.0, 2.0)
ell((0, 0.3, 7.6), (4.3, 3.5, 2.5), 1.0, 2.0)
ell((0.1, -0.1, 9.0), (2.7, 2.4, 1.7), 1.0, 2.0)
for sx in (-1, 1):
    ell((3.0*sx, 0.1, 6.0), (2.4, 2.4, 2.0), 1.0, 2.0)
# soft asymmetric bulges (low stiffness -> wide, gentle)
for co, r in [((1.4, 1.2, 7.9), 1.3), ((-1.7, 1.3, 7.1), 1.4), ((-0.9, 0.8, 8.8), 1.0), ((1.9, 1.6, 5.9), 1.2)]:
    ball(co, r, 1.3)
# 3) drips running down off the eave edge (start inside the sheet so they stay attached)
drips = []
for k in range(14):
    a = (k / 14) * 2 * math.pi + random.uniform(-0.12, 0.12)
    ex = math.cos(a) * 3.55; ey = math.sin(a) * 2.45
    if ey < -1.9 and abs(ex) < 2.4: continue       # front middle stays clear
    if abs(ex) > 2.6 and ey < 0.9: continue        # keep drips off the arm path (they fuse with the arm and smear when it moves)
    z0 = roof_z(ex * 0.93, ey * 0.93)
    if z0 is None: continue
    bot = 3.15 - random.uniform(0.2, 1.3)
    rr = random.uniform(0.2, 0.32)
    tip = Vector((ex * 1.1, ey * 1.1, bot))
    caps((ex * 0.93, ey * 0.93, z0 + 0.1), (ex * 1.08, ey * 1.08, 3.2), rr * 1.25)
    caps((ex * 1.08, ey * 1.08, 3.2), tip, rr)
    ball(tip - Vector((0, 0, 0.12)), rr * 1.45)
    drips.append(list(tip))
# 4) arms
arms = {}
for sx, nm in ((-1, "L"), (1, "R")):
    path, L = arm_path(sx)
    arms[nm] = (path, L)
    for i in range(0, len(path), 3):
        p, s, r = path[i]
        ball(p, r * 1.05, 2.0)
bpy.context.view_layer.objects.active = mbo; mbo.select_set(True)
bpy.ops.object.convert(target='MESH')
body = bpy.context.view_layer.objects.active; body.name = "ChogaSlime"
print("CONVERTED", len(body.data.vertices), [round(v, 2) for v in body.dimensions])

# remesh + organic noise + smooth
m = body.modifiers.new("remesh", 'REMESH'); m.mode = 'VOXEL'; m.voxel_size = 0.07
bpy.ops.object.modifier_apply(modifier="remesh")
tex = bpy.data.textures.new("lumps", 'CLOUDS'); tex.noise_scale = 1.6; tex.noise_depth = 2
d = body.modifiers.new("disp", 'DISPLACE'); d.texture = tex; d.strength = 0.22; d.mid_level = 0.5
bpy.ops.object.modifier_apply(modifier="disp")
sm = body.modifiers.new("smooth", 'SMOOTH'); sm.factor = 0.8; sm.iterations = 12
bpy.ops.object.modifier_apply(modifier="smooth")
dec = body.modifiers.new("dec", 'DECIMATE'); dec.ratio = 0.35
bpy.ops.object.modifier_apply(modifier="dec")
for p in body.data.polygons: p.use_smooth = True
print("FINAL", len(body.data.vertices), len(body.data.polygons), [round(v, 2) for v in body.dimensions])

# ---------------- eyes ----------------
bvh = BVHTree.FromObject(body, bpy.context.evaluated_depsgraph_get())
eyes = []
for sx, nm in ((-1, "L"), (1, "R")):
    zs = [v.co.z for v in body.data.vertices]; ztop = max(zs)
    hit = None
    for zz in [ztop - 0.6 - 0.15 * i for i in range(30)]:
        h, n_, i_, d_ = bvh.ray_cast(Vector((0.62 * sx, -12, zz)), Vector((0, 1, 0)))
        if h is not None and n_.y < -0.55: hit, nrm = h, n_; break
    if hit is None: raise RuntimeError("eye ray missed")
    c = hit - nrm * 0.05
    bpy.ops.mesh.primitive_uv_sphere_add(segments=24, ring_count=12, radius=0.2, location=c)
    e = bpy.context.active_object; e.name = "Eye" + nm
    # almond: wide, short, thin along the normal; tilted inward-down for a mean look
    e.scale = (1.35, 0.55, 0.62)
    q = Vector((0, -1, 0)).rotation_difference(nrm)
    tilt = Quaternion(nrm, math.radians(-14 * sx))
    e.rotation_mode = 'QUATERNION'; e.rotation_quaternion = tilt @ q
    eyes.append(e)
    print("EYE", nm, [round(v, 2) for v in c], [round(v, 2) for v in nrm])

# materials
def mat(n):
    m = bpy.data.materials.get(n) or bpy.data.materials.new(n); return m
body.data.materials.append(mat("M_ChogaSlime"))
for e in eyes: e.data.materials.append(mat("M_ChogaSlimeEye"))

# ---------------- armature ----------------
arm_data = bpy.data.armatures.new("SlimeRig"); rig = bpy.data.objects.new("SlimeRig", arm_data)
scn.collection.objects.link(rig)
bpy.ops.object.select_all(action='DESELECT'); rig.select_set(True); bpy.context.view_layer.objects.active = rig
bpy.ops.object.mode_set(mode='EDIT')
eb = arm_data.edit_bones
root = eb.new("Root"); root.head = (0, 0, 0); root.tail = (0, 0, 1.0)
bodyb = eb.new("Body"); bodyb.head = (0, 0.2, 4.6); bodyb.tail = (0, 0.2, 9.5); bodyb.parent = root
NB = 7
bone_s = {}
for nm in ("L", "R"):
    path, L = arms[nm]
    marks = [L * k / NB for k in range(NB + 1)]
    def at(s):
        for i in range(1, len(path)):
            if path[i][1] >= s:
                a, b = path[i-1], path[i]; t = (s - a[1]) / max(1e-6, b[1] - a[1]); return a[0].lerp(b[0], t)
        return path[-1][0]
    prev = bodyb
    for k in range(NB):
        b = eb.new(f"Arm{nm}{k}"); b.head = at(marks[k]); b.tail = at(marks[k+1]); b.parent = prev
        b.use_connect = k > 0; prev = b
    bone_s[nm] = marks
bpy.ops.object.mode_set(mode='OBJECT')

# ---------------- weights ----------------
def smooth(a, b, x):
    t = min(1, max(0, (x - a) / (b - a))); return t * t * (3 - 2 * t)
vg = {n: body.vertex_groups.new(name=n) for n in ["Body"] + [f"Arm{s}{k}" for s in "LR" for k in range(NB)]}
col = body.data.color_attributes.new("Col", 'BYTE_COLOR', 'POINT')
dense = {nm: arms[nm][0] for nm in arms}
nmix = 0
for v in body.data.vertices:
    p = v.co
    best = None
    for nm in ("L", "R"):
        for q, s, r in dense[nm][::2]:
            dd = (p - q).length - r
            if best is None or dd < best[0]: best = (dd, nm, s, r)
    dd, nm, s, r = best
    L = arms[nm][1]
    a = smooth(0.5, 2.2, s) * (1 - smooth(0.0, 0.3, dd))   # tight: roof-sheet verts near the arm must stay on the body
    ws = {}
    if a > 0.001:
        seg = L / NB
        x = s / seg - 0.5   # bone centres at 0..NB-1
        k0 = int(math.floor(x)); t = x - k0
        for k, w in ((k0, 1 - t), (k0 + 1, t)):
            k = min(NB - 1, max(0, k)); ws[f"Arm{nm}{k}"] = ws.get(f"Arm{nm}{k}", 0) + w * a
    ws["Body"] = 1 - a
    for n, w in ws.items():
        if w > 0.002: vg[n].add([v.index], w, 'REPLACE')
    col.data[v.index].color = (a, 0, 0, 1)
    if 0.05 < a < 0.95: nmix += 1

print('MIXED', nmix, len(body.data.vertices))
body.parent = rig; mod = body.modifiers.new("rig", 'ARMATURE'); mod.object = rig
for e in eyes:
    mw = e.matrix_world.copy(); e.parent = rig; e.parent_type = 'BONE'; e.parent_bone = "Body"; e.matrix_world = mw

# metadata for Unity (rest arm control points in Blender space)
meta = {"bones": NB, "drips": drips}
json.dump(meta, open(os.path.join(HERE, "slime_meta.json"), "w"))

bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, "ChogaSlime.blend"))
os.makedirs(os.path.dirname(OUT_FBX), exist_ok=True)
bpy.ops.object.select_all(action='DESELECT')
for o in [rig, body] + eyes: o.select_set(True)
bpy.ops.export_scene.fbx(filepath=OUT_FBX, use_selection=True, add_leaf_bones=False, bake_anim=False,
                         use_armature_deform_only=False, mesh_smooth_type='FACE', colors_type='LINEAR',
                         apply_scale_options='FBX_SCALE_ALL')
print("EXPORTED", OUT_FBX)

# ---------------- preview render with the house ----------------
# (house already imported at the top)
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); scn.collection.objects.link(cam); scn.camera = cam
scn.render.engine = 'BLENDER_WORKBENCH'; scn.display.shading.light = 'MATCAP'
scn.display.shading.color_type = 'OBJECT'
scn.render.resolution_x = 1100; scn.render.resolution_y = 900
body.color = (0.12, 0.12, 0.14, 1)
for e in eyes: e.color = (1, 0.05, 0.02, 1)
for name, loc in (("front", (0, -24, 14)), ("q34", (-16, -18, 14)), ("game", (0, -14, 22))):
    cam.location = loc
    tgt = Vector((0, 0, 4.5)); cam.rotation_mode = 'QUATERNION'
    cam.rotation_quaternion = (tgt - Vector(loc)).to_track_quat('-Z', 'Y')
    scn.render.filepath = os.path.join(HERE, f"preview_{name}.png"); bpy.ops.render.render(write_still=True)
print("DONE")
