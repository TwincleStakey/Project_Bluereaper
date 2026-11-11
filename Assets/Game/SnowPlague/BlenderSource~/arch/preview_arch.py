# Preview render of the architecture pieces (textured, snow from vertex R) from a game-like camera.
# blender -b <work>/sp_arch.blend --factory-startup -P preview_arch.py -- <textures dir> <out png> [layout] [cam dist]
import bpy, sys, os, math
from mathutils import Vector, Euler

args = sys.argv[sys.argv.index("--") + 1:]
TEX, OUTP = args[0], args[1]
LAYOUT = args[2] if len(args) > 2 else "all"
DIST = float(args[3]) if len(args) > 3 else 60.0
TEXMAP = {"SP_Arch_RedWood": ("rough_wood", (0.32, 0.09, 0.07)), "SP_Arch_Timber": ("rough_wood", (0.32, 0.27, 0.23)),
          "SP_Arch_Deck": ("weathered_planks", (0.7, 0.66, 0.6)), "SP_Arch_RoofTile": ("roof_tiles_14", (0.3, 0.32, 0.34)),
          "SP_Arch_WallStone": ("japanese_stone_wall", (0.8, 0.8, 0.8)), "SP_Stone": ("japanese_stone_wall", (0.5, 0.52, 0.54)),
          "SP_StoneLantern": ("japanese_stone_wall", (0.6, 0.62, 0.64)), "SP_Arch_Rock01": ("rock_face_01", (0.7, 0.72, 0.75)),
          "SP_Arch_Rock02": ("rock_face_02", (0.7, 0.72, 0.75))}

for m in bpy.data.materials:
    m.use_nodes = True
    nt = m.node_tree; nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial"); bs = nt.nodes.new("ShaderNodeBsdfPrincipled")
    nt.links.new(bs.outputs[0], out.inputs[0])
    if m.name == "SP_LanternGlow":
        bs.inputs["Emission Color"].default_value = (1.0, 0.45, 0.14, 1); bs.inputs["Emission Strength"].default_value = 12
        continue
    t = TEXMAP.get(m.name)
    col = nt.nodes.new("ShaderNodeVertexColor"); col.layer_name = "Col"
    sep = nt.nodes.new("ShaderNodeSeparateColor"); nt.links.new(col.outputs[0], sep.inputs[0])
    mix = nt.nodes.new("ShaderNodeMix"); mix.data_type = 'RGBA'
    ramp = nt.nodes.new("ShaderNodeMapRange"); ramp.inputs[1].default_value = 0.3; ramp.inputs[2].default_value = 0.55
    nt.links.new(sep.outputs[0], ramp.inputs[0]); nt.links.new(ramp.outputs[0], mix.inputs[0])
    mix.inputs[7].default_value = (0.85, 0.88, 0.92, 1)
    tint = nt.nodes.new("ShaderNodeMix"); tint.data_type = 'RGBA'; tint.blend_type = 'MULTIPLY'; tint.inputs[0].default_value = 1
    if t:
        img = nt.nodes.new("ShaderNodeTexImage")
        p = os.path.join(TEX, t[0] + "_diff_1k.jpg")
        img.image = bpy.data.images.load(p, check_existing=True)
        nt.links.new(img.outputs[0], tint.inputs[6]); tint.inputs[7].default_value = (*[c * 1.6 for c in t[1]], 1)
    else:
        tint.inputs[6].default_value = (0.5, 0.5, 0.5, 1)
    nt.links.new(tint.outputs[2], mix.inputs[6])
    ao = nt.nodes.new("ShaderNodeMix"); ao.data_type = 'RGBA'; ao.blend_type = 'MULTIPLY'; ao.inputs[0].default_value = 1
    nt.links.new(mix.outputs[2], ao.inputs[6]); nt.links.new(sep.outputs[1], ao.inputs[7])
    nt.links.new(ao.outputs[2], bs.inputs["Base Color"])
    bs.inputs["Roughness"].default_value = 0.8

obs = {o.name: o for o in bpy.data.objects if o.type == 'MESH'}
layout = {
    "SP_Arch_Bridge": (-8, 6, 0), "SP_Arch_Gate": (14, 7, 0), "SP_Arch_Wall_Straight": (-13, -3, 0), "SP_Arch_Wall_Broken": (-8.6, -3, 0),
    "SP_Arch_Wall_Corner": (-5.5, -4.6, 0), "SP_Arch_Lantern_A": (2, -2, 0), "SP_Arch_Lantern_B": (5, -2, 0),
    "SP_Arch_RockFace_A": (11, -4, 0), "SP_Arch_RockFace_B": (19, -4, 0)}
if LAYOUT != "all":
    for n, o in obs.items(): o.hide_render = n != LAYOUT
    target = obs[LAYOUT]; target.location = (0, 0, 0)
    vs = [v.co for v in target.data.vertices]; centre = Vector((0, 0, max(v.z for v in vs) * 0.4))
else:
    for n, o in obs.items(): o.location = layout.get(n, (0, 0, 0))
    centre = Vector((3, 2, 1))
bpy.ops.mesh.primitive_plane_add(size=200, location=(0, 0, 0))
g = bpy.context.active_object; gm = bpy.data.materials.new("ground"); gm.use_nodes = True
gb = next(n for n in gm.node_tree.nodes if n.type == "BSDF_PRINCIPLED"); gb.inputs["Base Color"].default_value = (0.62, 0.66, 0.7, 1)
g.data.materials.append(gm)
# camera: 50 deg down from the south, 30 deg fov
cam = bpy.data.cameras.new("cam"); cam.angle = math.radians(30)
co = bpy.data.objects.new("cam", cam); bpy.context.scene.collection.objects.link(co)
pitch = math.radians(float(args[4]) if len(args) > 4 else 50)
co.location = centre + Vector((0, -math.cos(pitch), math.sin(pitch))) * DIST
co.rotation_euler = Euler((math.radians(90) - pitch, 0, 0))
bpy.context.scene.camera = co
sun = bpy.data.lights.new("sun", 'SUN'); sun.energy = 2.5; sun.angle = math.radians(3)
so = bpy.data.objects.new("sun", sun); so.rotation_euler = Euler((math.radians(55), 0, math.radians(-35))); bpy.context.scene.collection.objects.link(so)
w = bpy.data.worlds.new("w"); bpy.context.scene.world = w; w.use_nodes = True
bg = next(n for n in w.node_tree.nodes if n.type == "BACKGROUND"); bg.inputs[0].default_value = (0.35, 0.4, 0.48, 1); bg.inputs[1].default_value = 0.8
sc = bpy.context.scene
for e in ("BLENDER_EEVEE", "BLENDER_EEVEE_NEXT", "CYCLES"):
    try: sc.render.engine = e; break
    except TypeError: pass
sc.render.resolution_x, sc.render.resolution_y = 1600, 900
sc.render.filepath = OUTP
try: sc.view_settings.view_transform = "AgX"
except TypeError: pass
bpy.ops.render.render(write_still=True)
print("rendered", OUTP, sc.render.engine)
