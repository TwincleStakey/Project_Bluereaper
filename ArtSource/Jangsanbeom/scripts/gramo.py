import bpy, bmesh, math, sys
from mathutils import Vector, Matrix
P = r"C:/Users/PC/video2rig/projects/werewolf/props/"
arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
body = [o for o in bpy.data.objects if o.type == 'MESH'][0]
col = bpy.data.collections.new('Gramophone'); bpy.context.scene.collection.children.link(col)

# ---------- placement (body faces -Y, left +X, up +Z) ----------
C = Vector((0.02, 0.005, 0.457))          # drum centre, above the mid back
D = Vector((0, -1, 0.21)).normalized()    # horn axis: forward, slightly up
MOUNT = Vector((0.02, -0.035, 0.352))      # where the bracket meets the back
DRUM_R, DRUM_L = 0.030, 0.075
HORN_L, BELL_R, NECK_R = 0.29, 0.082, 0.017

def basis(d):
    q = d.to_track_quat('Z', 'Y'); return q.to_matrix().to_4x4()
B = basis(D)
def at(local):  # local (x,y,z) with z along the horn axis, origin at drum centre
    return C + (B @ Vector(local).to_4d()).to_3d()

# ---------- materials ----------
TEX = P + "tex_dl/rusty_metal_04_"
def metal(name, tint, patina, scale=9.0, verdigris=0.35):
    """CC0 rusty metal (Poly Haven rusty_metal_04) box-projected in object space, with teal verdigris patches mixed in."""
    m = bpy.data.materials.new(name); m.use_nodes = True; nt = m.node_tree; b = nt.nodes['Principled BSDF']; L = nt.links.new
    tc = nt.nodes.new('ShaderNodeTexCoord'); mp = nt.nodes.new('ShaderNodeMapping'); mp.inputs['Scale'].default_value = (scale, scale, scale)
    L(tc.outputs['Object'], mp.inputs['Vector'])
    def img(kind, non_color):
        t = nt.nodes.new('ShaderNodeTexImage'); t.image = bpy.data.images.load(TEX + kind + "_1k.jpg", check_existing=True)
        if non_color: t.image.colorspace_settings.name = 'Non-Color'
        t.projection = 'BOX'; t.projection_blend = 0.3; L(mp.outputs['Vector'], t.inputs['Vector']); return t
    d, r, n = img('diff', False), img('rough', True), img('nor_gl', True)
    tm = nt.nodes.new('ShaderNodeMix'); tm.data_type = 'RGBA'; tm.blend_type = 'MULTIPLY'; tm.inputs['Factor'].default_value = 1.0
    L(d.outputs['Color'], tm.inputs['A']); tm.inputs['B'].default_value = tint
    nz = nt.nodes.new('ShaderNodeTexNoise'); nz.inputs['Scale'].default_value = 45; nz.inputs['Detail'].default_value = 6; L(tc.outputs['Object'], nz.inputs['Vector'])
    ramp = nt.nodes.new('ShaderNodeValToRGB'); ramp.color_ramp.elements[0].position = 1 - verdigris; ramp.color_ramp.elements[1].position = 1 - verdigris + 0.08
    L(nz.outputs['Fac'], ramp.inputs['Fac'])
    vm = nt.nodes.new('ShaderNodeMix'); vm.data_type = 'RGBA'; L(ramp.outputs['Color'], vm.inputs['Factor']); L(tm.outputs['Result'], vm.inputs['A']); vm.inputs['B'].default_value = patina
    L(vm.outputs['Result'], b.inputs['Base Color'])
    L(r.outputs['Color'], b.inputs['Roughness'])
    inv = nt.nodes.new('ShaderNodeMath'); inv.operation = 'SUBTRACT'; inv.inputs[0].default_value = 1.0; L(r.outputs['Color'], inv.inputs[1]); L(inv.outputs['Value'], b.inputs['Metallic'])
    nm = nt.nodes.new('ShaderNodeNormalMap'); L(n.outputs['Color'], nm.inputs['Color']); L(nm.outputs['Normal'], b.inputs['Normal'])
    return m
def flat(name, col_, rough=0.8, metal_=0.0):
    m = bpy.data.materials.new(name); m.use_nodes = True; b = m.node_tree.nodes['Principled BSDF']
    b.inputs['Base Color'].default_value = col_; b.inputs['Roughness'].default_value = rough; b.inputs['Metallic'].default_value = metal_
    return m
def cloth_mat(name, img):
    m = bpy.data.materials.new(name); m.use_nodes = True; nt = m.node_tree; b = nt.nodes['Principled BSDF']
    t = nt.nodes.new('ShaderNodeTexImage'); t.image = bpy.data.images.load(P + img)
    nt.links.new(t.outputs['Color'], b.inputs['Base Color']); nt.links.new(t.outputs['Alpha'], b.inputs['Alpha'])
    b.inputs['Roughness'].default_value = 0.9
    try: m.surface_render_method = 'DITHERED'
    except Exception: m.blend_method = 'CLIP'
    m.use_backface_culling = False
    return m
M_IRON = metal('GramoIron', (0.85, 0.85, 0.88, 1), (0.27, 0.44, 0.41, 1))
M_DARK = metal('GramoDarkIron', (0.5, 0.5, 0.52, 1), (0.20, 0.33, 0.31, 1), verdigris=0.25)
M_INSIDE = metal('GramoInside', (0.35, 0.33, 0.32, 1), (0.13, 0.22, 0.20, 1), verdigris=0.3)
M_BRONZE = flat('GramoBronze', (0.42, 0.30, 0.13, 1), 0.45, 0.9)
M_ROPE = flat('GramoRope', (0.13, 0.035, 0.025, 1), 0.95)
M_BANNER = cloth_mat('GramoBanner', 'banner.png')
M_CLOTH = cloth_mat('GramoCloth', 'cloth.png')

objs = []
def new_obj(name, bm, mats):
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new(name, me); col.objects.link(o)
    for m in mats: me.materials.append(m)
    for p in me.polygons: p.use_smooth = True
    objs.append(o); return o

def lathe(name, prof, mats, segs=40, cap=False):
    """prof: list of (radius, z) in horn-local space; surface of revolution around the horn axis."""
    bm = bmesh.new(); rings = []
    for r, z in prof:
        rings.append([bm.verts.new(at((r * math.cos(2 * math.pi * k / segs), r * math.sin(2 * math.pi * k / segs), z))) for k in range(segs)])
    for a, b in zip(rings, rings[1:]):
        for k in range(segs): bm.faces.new((a[k], a[(k + 1) % segs], b[(k + 1) % segs], b[k]))
    if cap:
        bm.faces.new(list(reversed(rings[0]))); bm.faces.new(rings[-1])
    return new_obj(name, bm, mats)

# horn bell: flared, from the drum front to the mouth, plus a rolled rim
z0 = DRUM_L / 2
prof = [(NECK_R + (BELL_R - NECK_R) * (t ** 2.6), z0 + t * HORN_L) for t in [i / 30 for i in range(31)]]
h = lathe('Horn', prof, [M_IRON]); s = h.modifiers.new('thick', 'SOLIDIFY'); s.thickness = 0.0025; s.offset = 1
inner = lathe('HornInside', [(r * 0.985, z) for r, z in reversed(prof[4:])], [M_INSIDE])
inner.data.flip_normals() if hasattr(inner.data, 'flip_normals') else None
rim_z = z0 + HORN_L
lathe('HornRim', [(BELL_R + 0.0045 * math.cos(a), rim_z + 0.0045 * math.sin(a)) for a in [i * 2 * math.pi / 12 for i in range(13)]], [M_DARK], segs=48)
# horn seams: thin raised lines along the bell
for k in range(6):
    ang = k * math.pi / 3
    pts = [(r * 1.004 * math.cos(ang), r * 1.004 * math.sin(ang), z) for r, z in prof[3::3]]
    cu = bpy.data.curves.new(f'Seam{k}', 'CURVE'); cu.dimensions = '3D'; cu.bevel_depth = 0.0012
    sp = cu.splines.new('POLY'); sp.points.add(len(pts) - 1)
    for p, q in zip(sp.points, pts): p.co = (*at(q), 1)
    o = bpy.data.objects.new(f'Seam{k}', cu); col.objects.link(o); cu.materials.append(M_DARK); objs.append(o)

# drum with bands and rivets
lathe('Drum', [(DRUM_R, -DRUM_L / 2), (DRUM_R, DRUM_L / 2)], [M_DARK], cap=True)
for zb in (-0.030, -0.006, 0.020, 0.036):
    lathe(f'Band{zb}', [(DRUM_R + 0.0035, zb - 0.004), (DRUM_R + 0.0045, zb), (DRUM_R + 0.0035, zb + 0.004)], [M_IRON], segs=40)
    for k in range(10):
        a = k * 2 * math.pi / 10
        bpy.ops.mesh.primitive_uv_sphere_add(radius=0.0018, segments=8, ring_count=6, location=at(((DRUM_R + 0.0045) * math.cos(a), (DRUM_R + 0.0045) * math.sin(a), zb)))
        rv = bpy.context.object; rv.data.materials.append(M_BRONZE); bpy.context.collection.objects.unlink(rv); col.objects.link(rv); objs.append(rv)
# back cap: round plate with spokes (from the rear view)
lathe('BackPlate', [(0.0, -DRUM_L / 2 - 0.004), (DRUM_R + 0.006, -DRUM_L / 2 - 0.004), (DRUM_R + 0.006, -DRUM_L / 2)], [M_IRON], segs=40)
bpy.ops.mesh.primitive_cylinder_add(radius=0.008, depth=0.006, location=at((0, 0, -DRUM_L / 2 - 0.007)))
hub = bpy.context.object; hub.matrix_world = Matrix.Translation(hub.location) @ B; hub.data.materials.append(M_BRONZE)
bpy.context.collection.objects.unlink(hub); col.objects.link(hub); objs.append(hub)

def tube(name, pts, r, mat, res=4):
    cu = bpy.data.curves.new(name, 'CURVE'); cu.dimensions = '3D'; cu.bevel_depth = r; cu.bevel_resolution = res
    sp = cu.splines.new('BEZIER'); sp.bezier_points.add(len(pts) - 1)
    for bp, p in zip(sp.bezier_points, pts): bp.co = p; bp.handle_left_type = bp.handle_right_type = 'AUTO'
    o = bpy.data.objects.new(name, cu); col.objects.link(o); cu.materials.append(mat); objs.append(o); return o
# rear pipe with an elbow, and the valve stem on top of the drum
tube('RearPipe', [at((0, -0.012, -DRUM_L / 2 - 0.006)), at((0, -0.02, -DRUM_L / 2 - 0.03)), at((0, -0.008, -DRUM_L / 2 - 0.055)), at((0, -0.03, -DRUM_L / 2 - 0.062))], 0.0045, M_DARK)
tube('TopPipe', [at((0, DRUM_R, -0.022)), at((0, DRUM_R + 0.022, -0.02)), at((0, DRUM_R + 0.026, 0.004)), at((0, DRUM_R + 0.012, 0.03))], 0.0035, M_DARK)
bpy.ops.mesh.primitive_cylinder_add(radius=0.0055, depth=0.016, location=at((0, DRUM_R + 0.012, -0.004)))
v = bpy.context.object; v.data.materials.append(M_IRON); bpy.context.collection.objects.unlink(v); col.objects.link(v); objs.append(v)
bpy.ops.mesh.primitive_cylinder_add(radius=0.009, depth=0.003, location=at((0, DRUM_R + 0.021, -0.004)))
v = bpy.context.object; v.data.materials.append(M_BRONZE); bpy.context.collection.objects.unlink(v); col.objects.link(v); objs.append(v)
# curved bracket under the drum down into the back, ending in a square mount block
under = at((0, -DRUM_R, 0.0))
tube('Bracket', [under, under + Vector((0, -0.012, -0.025)), MOUNT + Vector((0, 0, 0.03)), MOUNT + Vector((0, 0, 0.008))], 0.011, M_DARK, res=1)
tube('Bracket2', [at((0, -DRUM_R, 0.03)), at((0, -DRUM_R - 0.025, 0.04)), MOUNT + Vector((0, -0.03, 0.012)), MOUNT + Vector((0, -0.018, 0.004))], 0.004, M_DARK)
bpy.ops.mesh.primitive_cube_add(size=1, location=MOUNT)
blk = bpy.context.object; blk.scale = (0.045, 0.045, 0.024); bpy.ops.object.transform_apply(location=False, rotation=False, scale=True); blk.data.materials.append(M_IRON)
bv = blk.modifiers.new('bevel', 'BEVEL'); bv.width = 0.003; bv.segments = 2
bpy.context.collection.objects.unlink(blk); col.objects.link(blk); objs.append(blk)
# medallion on the horn neck's left side
side = at((NECK_R + 0.008, 0, z0 + 0.03))
bpy.ops.mesh.primitive_cylinder_add(radius=0.011, depth=0.003, location=side)
md = bpy.context.object; md.rotation_euler = (0, math.radians(90), 0); md.data.materials.append(M_BRONZE)
bpy.context.collection.objects.unlink(md); col.objects.link(md); objs.append(md)
# rope: two helices wound around the neck, plus one braided tail hanging down
def radius_at(z):
    t = max(0.0, min(1.0, (z - z0) / HORN_L)); return NECK_R + (BELL_R - NECK_R) * (t ** 2.6)
for j, (za, zb) in enumerate(((z0 + 0.03, z0 + 0.07), (z0 + 0.13, z0 + 0.15))):
    pts = []
    for i in range(60):
        t = i / 59; z = za + t * (zb - za); a = t * 2 * math.pi * 3
        r = radius_at(z) + 0.004; pts.append(at((r * math.cos(a), r * math.sin(a), z)))
    tube(f'Rope{j}', pts, 0.0028, M_ROPE, res=2)
hang = at((0, -radius_at(z0 + 0.14) - 0.004, z0 + 0.14))
tube('RopeTail', [hang, hang + Vector((0.004, 0.003, -0.03)), hang + Vector((0.0, 0.0, -0.055))], 0.0025, M_ROPE, res=2)

# hanging cloths: talisman banners and torn strips
def strip(name, top, w, h, mat, yaw):
    bm = bmesh.new(); n = 6
    vs = [[bm.verts.new((sx * w / 2, 0, -h * i / n)) for sx in (-1, 1)] for i in range(n + 1)]
    for a, b in zip(vs, vs[1:]): bm.faces.new((a[0], a[1], b[1], b[0]))
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    uv = me.uv_layers.new()
    for poly in me.polygons:
        for li in poly.loop_indices:
            co = me.vertices[me.loops[li].vertex_index].co; uv.data[li].uv = (co.x / w + 0.5, 1 + co.z / h)
    o = bpy.data.objects.new(name, me); col.objects.link(o); me.materials.append(mat)
    o.matrix_world = Matrix.Translation(top) @ Matrix.Rotation(math.radians(yaw), 4, 'Z')
    sd = o.modifiers.new('drape', 'SIMPLE_DEFORM'); sd.deform_method = 'BEND'; sd.angle = math.radians(12); sd.deform_axis = 'X'
    o.modifiers.new('thick', 'SOLIDIFY').thickness = 0.0006
    objs.append(o); return o
for i, (zb, xs) in enumerate(((-0.030, 1), (-0.006, 1), (0.020, -1), (-0.030, -1))):
    top = at((xs * (DRUM_R + 0.006), 0.0, zb)) + Vector((0, 0, -0.004))
    if i % 2 == 0: strip(f'Banner{i}', top, 0.024, 0.075, M_BANNER, 90 if xs > 0 else -90)
    else: strip(f'Cloth{i}', top, 0.014, 0.09, M_CLOTH, 90 if xs > 0 else -90)
strip('ClothNeck', at((NECK_R + 0.006, 0, z0 + 0.06)) + Vector((0, 0, -0.004)), 0.012, 0.07, M_CLOTH, 90)
strip('BannerBell', at((0, -BELL_R * 0.45, z0 + HORN_L * 0.6)) + Vector((0, 0, -0.004)), 0.018, 0.035, M_BANNER, 0)

# ---------- clearance check against the body (head and mane) ----------
dg = bpy.context.evaluated_depsgraph_get()
import mathutils
bvh = mathutils.bvhtree.BVHTree.FromObject(body, dg)
worst = 1.0; worst_name = ''
for o in objs:
    if o.name.startswith(('Bracket', 'Mount', 'Cube')) or o is blk: continue
    eo = o.evaluated_get(dg)
    try: me = eo.to_mesh()
    except Exception: continue
    for v in list(me.vertices)[::3]:
        p = o.matrix_world @ v.co; hit = bvh.find_nearest(p)
        if hit[0] is not None and hit[3] < worst: worst, worst_name = hit[3], o.name
    eo.to_mesh_clear()
print('CLEARANCE', round(worst, 4), worst_name)

for o in objs:
    mw = o.matrix_world.copy(); o.parent = arm; o.parent_type = 'BONE'; o.parent_bone = 'Spine2'
    bpy.context.view_layer.update(); o.matrix_world = mw
OUT = P + "werewolf_gramophone"
bpy.ops.wm.save_as_mainfile(filepath=OUT + ".blend", copy=True)
bpy.ops.object.select_all(action='DESELECT')
for o in bpy.data.objects:
    if o.type in ('ARMATURE', 'MESH', 'CURVE'): o.select_set(True)
bpy.ops.export_scene.gltf(filepath=OUT + ".glb", export_format='GLB', use_selection=True, export_apply=True)
print('GRAMO', len(objs))
