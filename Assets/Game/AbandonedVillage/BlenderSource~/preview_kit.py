# Contact-sheet render of exported kit FBXs with flat material colors (shape check only).
# blender -b --factory-startup -P preview_kit.py -- <models dir> <out png> name1 name2 ...
import bpy, sys, os, math
from mathutils import Vector

args = sys.argv[sys.argv.index("--") + 1:]
SRC, OUT, NAMES = args[0], args[1], args[2:]
COL = {"AV_Wood": (0.16, 0.11, 0.07), "AV_Plank": (0.24, 0.18, 0.12), "AV_Thatch": (0.42, 0.34, 0.2), "AV_MudWall": (0.4, 0.33, 0.25),
       "AV_Stone": (0.35, 0.34, 0.32), "AV_StoneWall": (0.3, 0.29, 0.27), "AV_Tile": (0.12, 0.13, 0.14), "AV_Paper": (0.75, 0.72, 0.62),
       "AV_PaperGlow": (1.0, 0.6, 0.25), "AV_Glow": (1.0, 0.55, 0.2), "AV_Bark": (0.18, 0.16, 0.14), "AV_Onggi": (0.2, 0.1, 0.06),
       "AV_Rope": (0.45, 0.38, 0.25), "AV_Twig": (0.3, 0.22, 0.15), "AV_DryGrass": (0.5, 0.42, 0.26), "AV_Reed": (0.7, 0.65, 0.55), "AV_Crow": (0.02, 0.02, 0.025)}
bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene
x = 0.0; objs = []
for n in NAMES:
    bpy.ops.import_scene.fbx(filepath=os.path.join(SRC, n + ".fbx"))
    o = bpy.context.selected_objects[0]
    bpy.context.view_layer.update()
    w = o.dimensions.x
    o.location = (x + w / 2, 0, 0); x += w + 1.5
    objs.append(o)
for m in bpy.data.materials:
    m.use_nodes = True
    b = m.node_tree.nodes.get("Principled BSDF")
    c = COL.get(m.name.split(".")[0], (0.5, 0.0, 0.5))
    b.inputs["Base Color"].default_value = (*c, 1)
    b.inputs["Roughness"].default_value = 0.85
    if "Glow" in m.name:
        b.inputs["Emission Color"].default_value = (*c, 1); b.inputs["Emission Strength"].default_value = 6
bpy.ops.mesh.primitive_plane_add(size=400, location=(x / 2, 0, 0))
g = bpy.data.materials.new("ground"); g.use_nodes = True; g.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.12, 0.11, 0.1, 1)
bpy.context.object.data.materials.append(g)
sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN")); sc.collection.objects.link(sun)
sun.data.energy = 3.5; sun.rotation_euler = (math.radians(50), 0, math.radians(30))
world = bpy.data.worlds.new("w"); sc.world = world; world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (0.25, 0.28, 0.33, 1); world.node_tree.nodes["Background"].inputs[1].default_value = 1.0
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); sc.collection.objects.link(cam); sc.camera = cam
pitch = math.radians(float(os.environ.get("AV_PITCH", "40")))
cx = x / 2; dist = max(14.0, x * 0.95)
cam.location = (cx, -dist * math.cos(pitch), dist * math.sin(pitch) + 3)
cam.rotation_euler = (math.pi / 2 - pitch, 0, 0)
cam.data.lens = 35
sc.render.resolution_x, sc.render.resolution_y = 1800, 800
sc.render.engine = "BLENDER_EEVEE"
sc.render.filepath = OUT
bpy.ops.render.render(write_still=True)
print("RENDERED", OUT)
