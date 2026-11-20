# Quick look at sp_kit.blend: palette-coloured materials with the baked frost mask (vertex colour R) shown as white.
# blender -b <work>/sp_kit.blend --factory-startup -P preview_kit.py -- <out png prefix> [name filter]
import bpy, sys, math
from mathutils import Vector
a = sys.argv[sys.argv.index("--") + 1:]
out = a[0]; filt = a[1] if len(a) > 1 else ""; atlas = a[2] if len(a) > 2 else ""
sc = bpy.context.scene
cols = {"SP_Bark": (0.045, 0.045, 0.05), "SP_Wood": (0.12, 0.09, 0.07), "SP_Straw": (0.30, 0.24, 0.14), "SP_Rope": (0.25, 0.2, 0.13),
        "SP_Cloth": (0.28, 0.25, 0.21), "SP_Clay": (0.16, 0.10, 0.07), "SP_Iron": (0.06, 0.06, 0.06), "SP_Grass": (0.22, 0.21, 0.18), "SP_PineBark": (0.2, 0.09, 0.06), "SP_Stone": (0.3, 0.3, 0.3),
        "SP_LanternGlow": (1.0, 0.5, 0.15), "SP_Snow": (0.8, 0.83, 0.86), "SP_Bone": (0.6, 0.57, 0.5), "SP_LightCone": (1, 0.6, 0.3)}
CARD = {"SP_Needles", "SP_Maple", "SP_GrassCard"}
for m in bpy.data.materials:
    m.use_nodes = True
    nt = m.node_tree
    b = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    attr = nt.nodes.new("ShaderNodeVertexColor"); attr.layer_name = "Col"
    sep = nt.nodes.new("ShaderNodeSeparateColor")
    mix = nt.nodes.new("ShaderNodeMix"); mix.data_type = "RGBA"
    nt.links.new(attr.outputs[0], sep.inputs[0])
    nt.links.new(sep.outputs[0], mix.inputs[0])
    c = cols.get(m.name, (0.3, 0.3, 0.3))
    mix.inputs[6].default_value = (*c, 1); mix.inputs[7].default_value = (0.85, 0.88, 0.9, 1)
    nt.links.new(mix.outputs[2], b.inputs[0])
    if m.name in CARD and atlas:
        tx = nt.nodes.new("ShaderNodeTexImage"); tx.image = bpy.data.images.load(atlas)
        mul = nt.nodes.new("ShaderNodeMix"); mul.data_type = "RGBA"; mul.blend_type = "MULTIPLY"; mul.inputs[0].default_value = 1
        nt.links.new(tx.outputs[0], mul.inputs[6]); mix.inputs[6].default_value = (1, 1, 1, 1)
        nt.links.new(mix.outputs[2], mul.inputs[7]); nt.links.new(mul.outputs[2], b.inputs[0])
        nt.links.new(tx.outputs[1], b.inputs["Alpha"])
        try: m.surface_render_method = "DITHERED"
        except Exception: pass
    if m.name == "SP_LanternGlow":
        b.inputs["Emission Color"].default_value = (1, 0.45, 0.12, 1); b.inputs["Emission Strength"].default_value = 8
    b.inputs["Roughness"].default_value = 0.85
for ob in bpy.data.objects:
    if ob.type == "MESH" and filt and filt not in ob.name: ob.hide_render = True
ground = bpy.data.meshes.new("g"); g = bpy.data.objects.new("g", ground); sc.collection.objects.link(g)
ground.from_pydata([(-50, -50, 0), (80, -50, 0), (80, 30, 0), (-50, 30, 0)], [], [(0, 1, 2, 3)])
gm = bpy.data.materials.new("gm"); gm.use_nodes = True
next(n for n in gm.node_tree.nodes if n.type == "BSDF_PRINCIPLED").inputs[0].default_value = (0.17, 0.2, 0.23, 1)
ground.materials.append(gm)
world = bpy.data.worlds.new("w"); sc.world = world; world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (0.25, 0.3, 0.38, 1)
world.node_tree.nodes["Background"].inputs[1].default_value = 0.6
sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN")); sc.collection.objects.link(sun)
sun.rotation_euler = (math.radians(40), math.radians(10), math.radians(-30)); sun.data.energy = 2.5; sun.data.color = (0.8, 0.88, 1.0)
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); sc.collection.objects.link(cam); sc.camera = cam
for eng in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE"):
    try: sc.render.engine = eng; break
    except TypeError: pass
sc.view_settings.view_transform = "Standard"
vis = [o for o in bpy.data.objects if o.type == "MESH" and not o.hide_render and o.name != "g"]
mn = Vector((min(o.location.x - o.dimensions.x / 2 for o in vis), min(o.location.y for o in vis), 0))
mx = Vector((max(o.location.x + o.dimensions.x / 2 for o in vis), max(o.location.y for o in vis), 0))
c = (mn + mx) / 2
span = max(mx.x - mn.x, (mx.y - mn.y) * 1.8) + 4
sc.render.resolution_x, sc.render.resolution_y = 1800, 1000
# RoomRun-like steep view (~70 deg down)
ang = math.radians(70)
cam.data.type = "ORTHO"; cam.data.ortho_scale = span
cam.location = c + Vector((0, -math.cos(ang) * 40, math.sin(ang) * 40)); cam.rotation_euler = (math.radians(90 - 70), 0, 0)
sc.render.filepath = f"{out}_top.png"; bpy.ops.render.render(write_still=True)
cam.data.type = "PERSP"; cam.data.lens = 28
cam.location = c + Vector((0, -span * 0.55, span * 0.22)); cam.rotation_euler = (math.radians(75), 0, 0)
sc.render.filepath = f"{out}_side.png"; bpy.ops.render.render(write_still=True)
