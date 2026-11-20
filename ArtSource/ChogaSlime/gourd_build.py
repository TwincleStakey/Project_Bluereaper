# 흉박 (cursed gourd) for the 그슨대 boss: a big round calabash (~1.4 m), shallow ribs, a crooked dry stem.
import bpy, bmesh, math
from mathutils import Vector
bpy.ops.wm.read_factory_settings(use_empty=True)
bm = bmesh.new()
SEG, RING = 40, 24
verts = []
for i in range(RING + 1):
    v = i / RING; th = math.pi * v                       # 0 bottom .. pi top
    for j in range(SEG):
        ph = 2 * math.pi * j / SEG
        r = math.sin(th) * (1 + 0.035 * math.cos(ph * 10)) # 10 shallow ribs
        z = -math.cos(th) * 0.82 - 0.06 * math.sin(th) ** 2 * (1 if th < math.pi / 2 else -1)
        z += 0.08 * math.exp(-((th - math.pi) / 0.35) ** 2) * -1   # dimple at the stem
        verts.append(bm.verts.new((r * math.cos(ph) * 0.7, r * math.sin(ph) * 0.7, (z + 0.82) * 0.7)))
for i in range(RING):
    for j in range(SEG):
        a = verts[i * SEG + j]; b = verts[i * SEG + (j + 1) % SEG]; c = verts[(i + 1) * SEG + (j + 1) % SEG]; d = verts[(i + 1) * SEG + j]
        try: bm.faces.new((a, b, c, d))
        except ValueError: pass
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-4)
me = bpy.data.meshes.new("Hyungbak"); bm.to_mesh(me); bm.free()
g = bpy.data.objects.new("Hyungbak", me); bpy.context.scene.collection.objects.link(g)
for p in me.polygons: p.use_smooth = True
# stem: a bent tapered tube
curve = bpy.data.curves.new("stem", 'CURVE'); curve.dimensions = '3D'
sp = curve.splines.new('BEZIER'); sp.bezier_points.add(2)
for k, (co, r) in enumerate([((0, 0, 1.08), 0.09), ((0.06, 0, 1.3), 0.06), ((0.22, 0.05, 1.42), 0.035)]):
    bp = sp.bezier_points[k]; bp.co = co; bp.handle_left_type = bp.handle_right_type = 'AUTO'; bp.radius = r
curve.bevel_depth = 1.0; curve.bevel_resolution = 3
st = bpy.data.objects.new("Stem", curve); bpy.context.scene.collection.objects.link(st)
bpy.context.view_layer.objects.active = st; st.select_set(True); bpy.ops.object.convert(target='MESH')
g.data.materials.append(bpy.data.materials.new("M_Hyungbak"))
st.data.materials.append(bpy.data.materials.new("M_HyungbakStem"))
bpy.ops.object.select_all(action='DESELECT'); g.select_set(True); st.select_set(True); bpy.context.view_layer.objects.active = g
bpy.ops.object.join()
bpy.ops.export_scene.fbx(filepath=r"C:\UnityProject\BulletHeavenArt\Assets\Game\ChogaSlime\Models\Hyungbak.fbx", use_selection=True, apply_scale_options='FBX_SCALE_ALL')
print("GOURD", [round(v, 2) for v in g.dimensions])
