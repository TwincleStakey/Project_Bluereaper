# Quick look at reed_kit.blend: renders the clumps side by side and from above.
# blender -b <work>/reed_kit.blend --factory-startup -P preview_reeds.py -- <out png prefix>
import bpy, sys, math
from mathutils import Vector
out = sys.argv[sys.argv.index("--") + 1]
sc = bpy.context.scene
cols = {"ReedLeaf": (0.16, 0.17, 0.10, 1), "ReedStem": (0.45, 0.40, 0.28, 1), "ReedPlume": (0.85, 0.80, 0.70, 1),
        "BW_Wood": (0.25, 0.18, 0.12, 1), "BW_Post": (0.15, 0.11, 0.08, 1)}
for m in bpy.data.materials:
    m.use_nodes = True
    b = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    b.inputs[0].default_value = cols.get(m.name, (0.5, 0.5, 0.5, 1))
for ob in bpy.data.objects:
    if ob.type == "MESH":
        for p in ob.data.polygons: p.use_smooth = True
world = bpy.data.worlds.new("w"); sc.world = world; world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (0.35, 0.4, 0.5, 1)
sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN")); sc.collection.objects.link(sun)
sun.rotation_euler = (math.radians(50), 0, math.radians(30)); sun.data.energy = 3
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); sc.collection.objects.link(cam); sc.camera = cam
try: sc.render.engine = "BLENDER_EEVEE_NEXT"
except TypeError:
    try: sc.render.engine = "BLENDER_EEVEE"
    except TypeError: pass
sc.render.resolution_x, sc.render.resolution_y = 1600, 700
reeds = [o for o in bpy.data.objects if o.type == "MESH" and not o.name.startswith("RF_Board")]
cx = sum(o.location.x for o in reeds) / len(reeds)
for tag, loc, rot in (("side", (cx, -6.0, 0.7), (math.radians(86), 0, 0)), ("top", (cx, -2.6, 5.2), (math.radians(30), 0, 0))):
    cam.location = loc; cam.rotation_euler = rot; cam.data.lens = 35
    sc.render.filepath = f"{out}_{tag}.png"; bpy.ops.render.render(write_still=True)
bw = [o for o in bpy.data.objects if o.name.startswith("RF_Board")]
if bw:
    bx = sum(o.location.x for o in bw) / len(bw)
    cam.location = (bx + 2, -9, 6); cam.rotation_euler = (math.radians(55), 0, 0)
    sc.render.filepath = f"{out}_boardwalk.png"; bpy.ops.render.render(write_still=True)
