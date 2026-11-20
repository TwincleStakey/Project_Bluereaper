# Weathered, broken wooden cross the 거점 허수아비 hangs on (Kido 11-20: like the hub concept, "많이 망가진" not smooth).
# Procedural: subdivided boxes + long grain cracks + chipped/eroded edges + snapped splintered ends + bitten-out chunks.
# UVs map the length along the grain of the Megascans weathered plank scan (AV_MS_Planks_A, grey part v 0.22-0.97).
# blender -b --python scarecrow_cross.py -- <out.fbx> [preview_dir]
# Units: metres at final size, Blender front -Y (same as scarecrow_rig.py), origin = ground under the scarecrow.
import bpy, bmesh, sys, os, math, random
from mathutils import Vector, noise, Matrix

a = sys.argv[sys.argv.index("--") + 1:]
out_fbx = os.path.abspath(a[0]); prev = os.path.abspath(a[1]) if len(a) > 1 else None
LIFT = 0.6                     # scarecrow feet above ground
SC_H = 2.85
ARM_Z = LIFT + 0.775 * SC_H    # arm bone height
POST_Y, BEAM_Y = 0.30, 0.17    # behind the body (+Y = back)

bpy.ops.wm.read_factory_settings(use_empty=True)
rnd = random.Random(7)

def fbm(p, oct=4):
    s, amp, f = 0.0, 1.0, 1.0
    for _ in range(oct):
        s += amp * noise.noise(p * f); amp *= 0.5; f *= 2.03
    return s

def timber(name, length, w, d, seed, end0="saw", end1="saw", step=0.035):
    """Beam along local +Z from 0 to length, cross-section w (x) by d (y). end = 'saw' | 'snap'."""
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=(w, d, length), verts=bm.verts)
    bmesh.ops.translate(bm, vec=(0, 0, length / 2), verts=bm.verts)
    def cut(co, no):
        geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
        bmesh.ops.bisect_plane(bm, geom=geom, plane_co=co, plane_no=no)
    n = int(length / step)
    for i in range(1, n): cut((0, 0, length * i / n), (0, 0, 1))
    for i in range(1, 6): cut(((i / 6 - 0.5) * w, 0, 0), (1, 0, 0))
    for i in range(1, 6): cut((0, (i / 6 - 0.5) * d, 0), (0, 1, 0))
    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4])
    off = Vector((seed * 13.1, seed * 7.7, seed * 3.3))
    # crack lines: (face side, lateral position 0..1, start, end, depth)
    cracks = [(rnd.choice("xy"), rnd.uniform(0.2, 0.8), rnd.uniform(0, length * 0.5), rnd.uniform(length * 0.5, length), rnd.uniform(0.025, 0.05)) for _ in range(9)]
    snap_len = 0.5
    # bitten-out chunks on the corners: (z, corner sx, sy, length, depth)
    bites = []
    for k in range(6):
        bz = rnd.uniform(0.6 if end0 == "snap" else 0.2, length - (0.7 if end1 == "snap" else 0.3))
        bites.append((bz, rnd.choice((-1, 1)), rnd.choice((-1, 1)), rnd.uniform(0.12, 0.3), rnd.uniform(0.035, 0.06)))
    for v in bm.verts:
        x, y, z = v.co
        u, t = x / w + 0.5, y / d + 0.5          # 0..1 across
        # distance into the section from the nearest face (0 at surface)
        ex = min(u, 1 - u) * w; ey = min(t, 1 - t) * d
        disp = Vector((0, 0, 0))
        # weathering: long grain grooves (stretched noise) + lumpy surface
        g = abs(noise.noise(Vector((x * 60, y * 60, z * 2.5)) + off))
        lump = fbm(Vector((x * 6, y * 6, z * 3)) + off)
        inward = 0.007 * lump - 0.009 * (1 - min(1, g * 3))
        # eroded/chipped edges: the corners rot away
        corner = max(0.0, 1 - min(ex, ey) / 0.03) if min(ex, ey) < 0.03 else 0.0
        inward -= corner * (0.016 + 0.04 * max(0, fbm(Vector((x * 9, y * 9, z * 9)) + off * 2)))
        # cracks
        for side, pos, c0, c1, dep in cracks:
            if c0 < z < c1:
                lat = u if side == "x" else t
                wob = pos + 0.06 * noise.noise(Vector((z * 3, seed, 0)))
                if abs(lat - wob) < 0.06:
                    fade = min(1, (z - c0) / 0.2, (c1 - z) / 0.2)
                    inward -= dep * fade * (1 - abs(lat - wob) / 0.06) ** 0.6
        # chunks: corner region carved inward toward the axis, jagged rim
        for bz, sx, sy, bl, bd in bites:
            if abs(z - bz) < bl / 2 and x * sx > 0 and y * sy > 0:
                f = 1 - (2 * abs(z - bz) / bl) ** 2
                rim = 0.7 + 0.6 * noise.noise(Vector((x * 25, y * 25, z * 25)) + off)
                cut = bd * f * rim - (min(ex, ey) if ex < ey else min(ex, ey))
                if cut > 0: inward -= cut * 1.4
        # push along the dominant outward axis
        if ex < ey: disp.x = math.copysign(1, x) * inward
        else: disp.y = math.copysign(1, y) * inward
        v.co += disp
        # snapped ends: splinter spikes running along the grain, fibres peel outward
        for end, zt, sgn in ((end0, 0.0, -1), (end1, length, 1)):
            if end != "snap": continue
            dz = abs(z - zt)
            if dz < snap_len:
                spike = 0.5 + 0.5 * noise.noise(Vector((x * 14, y * 14, seed + zt)))
                spike = spike ** 2.2
                along = -(1 - spike) * (snap_len - dz) * 1.0     # pull the end back unevenly
                v.co.z += -sgn * (snap_len - dz) * (1 - spike) * 0.9
                peel = (snap_len - dz) / snap_len * spike * 0.045
                v.co.x += math.copysign(peel, x); v.co.y += math.copysign(peel * 0.7, y)
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    ob = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(ob)
    uv_grain(ob, w, d)
    return ob

def uv_grain(ob, w, d):
    me = ob.data
    uvl = me.uv_layers.new(name="UVMap")
    V0, V1 = 0.22, 0.97
    side_v = {}
    for p in me.polygons:
        n = p.normal
        ax = max(range(3), key=lambda i: abs(n[i]))
        key = (ax, n[ax] > 0)
        if key not in side_v: side_v[key] = (rnd.uniform(V0, V1 - 0.16), rnd.uniform(0, 1))
        v0, u0 = side_v[key]
        for li in p.loop_indices:
            co = me.vertices[me.loops[li].vertex_index].co
            if ax == 2:      # end grain
                uvl.data[li].uv = (u0 + co.x / 1.2, v0 + (co.y / d + 0.5) * 0.12)
            else:
                lat = co.y / d + 0.5 if ax == 0 else co.x / w + 0.5
                uvl.data[li].uv = (u0 + co.z / 1.25, v0 + lat * 0.13)

post = timber("CrossPost", 4.0, 0.17, 0.15, 1, end0="saw", end1="snap")
post.location = (0.02, POST_Y, -0.45)            # 0.45 m in the ground
post.rotation_euler = (math.radians(-2.0), math.radians(2.5), 0)
beam = timber("CrossBeam", 3.05, 0.13, 0.11, 2, end0="snap", end1="saw")
beam.rotation_euler = (math.radians(1.5), math.radians(90 - 3.5), 0)   # along +X, slight sag
beam.location = (-1.6, BEAM_Y, ARM_Z - 0.02)

# rope lashing at the joint: a few wraps of a bevelled curve
cu = bpy.data.curves.new("lash", 'CURVE'); cu.dimensions = '3D'; cu.bevel_depth = 0.011; cu.bevel_resolution = 2
sp = cu.splines.new('POLY'); pts = []
cx, cy, cz = 0.02, (POST_Y + BEAM_Y) / 2, ARM_Z - 0.02
for i in range(4 * 24 + 1):
    a_ = i / 24 * 2 * math.pi; k = i / (4 * 24)
    diag = 1 if (i // 24) % 2 == 0 else -1
    r = 0.13 + 0.01 * math.sin(a_ * 3)
    x = cx + math.cos(a_) * 0.11 * diag + math.sin(a_) * 0.02
    y = cy + math.sin(a_) * r * 0.75
    z = cz + math.cos(a_) * 0.09 * diag * -1 + (k - 0.5) * 0.08
    pts.append((x, y, z))
sp.points.add(len(pts) - 1)
for p_, c in zip(sp.points, pts): p_.co = (*c, 1)
lash = bpy.data.objects.new("CrossLash", cu); bpy.context.scene.collection.objects.link(lash)
bpy.context.view_layer.objects.active = lash; lash.select_set(True); bpy.ops.object.convert(target='MESH')

# materials: wood (texture assigned in Unity) and rope
mw = bpy.data.materials.new("Cross_Wood"); mr = bpy.data.materials.new("Cross_Rope")
for ob in (post, beam): ob.data.materials.append(mw)
lash.data.materials.append(mr)
for ob in (post, beam, lash):
    bpy.context.view_layer.objects.active = ob; ob.select_set(True)
    bpy.ops.object.shade_smooth() if ob is lash else None
bpy.ops.object.select_all(action='DESELECT')
for ob in (post, beam, lash): ob.select_set(True)
bpy.context.view_layer.objects.active = post
bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
tris = sum(len(p.vertices) - 2 for o in (post, beam, lash) for p in o.data.polygons)
print("CROSS tris", tris)
for o in (post, beam, lash):
    ws = [o.matrix_world @ v.co for v in o.data.vertices]
    print("BOUNDS", o.name, len(ws), [round(min(w[i] for w in ws),2) for i in range(3)] if ws else None, [round(max(w[i] for w in ws),2) for i in range(3)] if ws else None)
bpy.ops.export_scene.fbx(filepath=out_fbx, use_selection=True, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
                         add_leaf_bones=False, bake_anim=False, path_mode='STRIP', mesh_smooth_type='FACE')
print("DONE", out_fbx)

if prev:
    os.makedirs(prev, exist_ok=True)
    tex = bpy.data.images.load(os.path.abspath("Assets/Game/AbandonedVillage/ThirdParty/Megascans/Planks/AV_MS_Planks_A.jpg"))
    mw.use_nodes = True; nt = mw.node_tree; ti = nt.nodes.new("ShaderNodeTexImage"); ti.image = tex
    nt.links.new(ti.outputs[0], nt.nodes["Principled BSDF"].inputs["Base Color"]); nt.nodes.active = ti
    mr.diffuse_color = (0.25, 0.2, 0.14, 1)
    # the scarecrow for scale
    bpy.ops.import_scene.fbx(filepath=os.path.abspath("Assets/Game/Hub/Scarecrow/Models/Scarecrow.fbx"))
    for o in bpy.context.selected_objects:
        if o.parent is None: o.scale *= SC_H / 1.8; o.location.z += LIFT
    sc = bpy.context.scene; sc.render.engine = 'BLENDER_WORKBENCH'; sc.render.resolution_x = 900; sc.render.resolution_y = 1000
    sh = sc.display.shading; sh.light = 'STUDIO'; sh.color_type = 'TEXTURE'
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); sc.collection.objects.link(cam); sc.camera = cam
    cam.data.type = 'ORTHO'; cam.data.ortho_scale = 4.4
    for nm, loc, rot in (("front", (0, -8, 2.0), (90, 0, 0)), ("back", (0, 8, 2.0), (90, 0, 180)), ("side", (8, 0, 2.0), (90, 0, 90)),
                         ("q34", (-5, -6, 6.5), (55, 0, -40)), ("beamend", (-2.2, -0.9, 3.0), (75, 0, -60)), ("posttop", (0.6, -1.1, 4.4), (70, 0, -20))):
        cam.location = loc; cam.rotation_euler = [math.radians(x) for x in rot]
        cam.data.ortho_scale = 1.4 if nm in ("beamend", "posttop") else 4.4
        sc.render.filepath = os.path.join(prev, "cross_" + nm + ".png"); bpy.ops.render.render(write_still=True)
