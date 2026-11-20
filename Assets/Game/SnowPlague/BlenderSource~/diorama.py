# Look-dev diorama of the kit (not the game scene): a 24 x 16 m patch at the game camera angle (~70 deg down),
# moonlight + one lantern. blender -b --factory-startup -P diorama.py -- <work dir> <textures dir> <out png>
import bpy, sys, os, math, random
from mathutils import Vector
a = sys.argv[sys.argv.index("--") + 1:]
WORK, TEX, OUT = a
bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene
for f in ("sp_kit.blend", "sp_kit2.blend"):
    with bpy.data.libraries.load(os.path.join(WORK, f)) as (src, dst):
        dst.objects = list(src.objects)
    for ob in dst.objects:
        if ob: sc.collection.objects.link(ob); ob.location = (0, 0, -100)   # hidden until placed
objs = {o.name: o for o in sc.objects}

def img(n):
    p = os.path.join(TEX, n)
    return bpy.data.images.load(p) if os.path.exists(p) else None

COL = {"SP_Bark": (0.05, 0.05, 0.055), "SP_Wood": (0.13, 0.1, 0.08), "SP_Straw": (0.32, 0.27, 0.17), "SP_Rope": (0.25, 0.2, 0.13),
       "SP_Cloth": (0.26, 0.24, 0.21), "SP_Clay": (0.17, 0.1, 0.07), "SP_Iron": (0.05, 0.05, 0.05), "SP_Grass": (0.33, 0.31, 0.26),
       "SP_PineBark": (0.22, 0.1, 0.07), "SP_Stone": (0.33, 0.34, 0.35), "SP_Snow": (0.78, 0.81, 0.84), "SP_Bone": (0.55, 0.52, 0.46)}
TEXMAP = {"SP_Wood": "wood_planks_grey_diff_1k.jpg", "SP_Straw": "ThatchedRoof002A_diff_1k.jpg", "SP_Cloth": "Fabric030_diff_1k.jpg",
          "SP_Stone": "dark_rock_02_diff_1k.jpg", "SP_PineBark": "pine_bark_diff_1k.jpg", "SP_Bark": "bark_brown_02_diff_1k.jpg"}
atlas = img("SP_FoliageAtlas.png")
for m in bpy.data.materials:
    m.use_nodes = True; nt = m.node_tree
    b = next((n for n in nt.nodes if n.type == "BSDF_PRINCIPLED"), None)
    if not b: continue
    vc = nt.nodes.new("ShaderNodeVertexColor"); vc.layer_name = "Col"
    sep = nt.nodes.new("ShaderNodeSeparateColor"); nt.links.new(vc.outputs[0], sep.inputs[0])
    base = nt.nodes.new("ShaderNodeMix"); base.data_type = "RGBA"; base.blend_type = "MULTIPLY"; base.inputs[0].default_value = 1
    base.inputs[6].default_value = (*COL.get(m.name, (0.3, 0.3, 0.3)), 1); base.inputs[7].default_value = (1, 1, 1, 1)
    if m.name in TEXMAP and img(TEXMAP[m.name]):
        t = nt.nodes.new("ShaderNodeTexImage"); t.image = img(TEXMAP[m.name])
        g = nt.nodes.new("ShaderNodeRGBToBW"); nt.links.new(t.outputs[0], g.inputs[0])
        mm = nt.nodes.new("ShaderNodeMath"); mm.operation = "MULTIPLY"; mm.inputs[1].default_value = 2.2
        nt.links.new(g.outputs[0], mm.inputs[0]); cmb = nt.nodes.new("ShaderNodeCombineColor")
        for k in range(3): nt.links.new(mm.outputs[0], cmb.inputs[k])
        nt.links.new(cmb.outputs[0], base.inputs[7])
    if m.name in ("SP_Needles", "SP_Maple", "SP_GrassCard") and atlas:
        t = nt.nodes.new("ShaderNodeTexImage"); t.image = atlas
        nt.links.new(t.outputs[0], base.inputs[6]); nt.links.new(t.outputs[1], b.inputs["Alpha"])
        if m.name == "SP_Needles": base.inputs[7].default_value = (0.35, 0.35, 0.35, 1)
    snow = nt.nodes.new("ShaderNodeMix"); snow.data_type = "RGBA"
    nt.links.new(sep.outputs[0], snow.inputs[0]); nt.links.new(base.outputs[2], snow.inputs[6]); snow.inputs[7].default_value = (0.72, 0.76, 0.8, 1)
    nt.links.new(snow.outputs[2], b.inputs[0]); b.inputs["Roughness"].default_value = 0.8
    if m.name == "SP_LanternGlow":
        b.inputs["Emission Color"].default_value = (1, 0.42, 0.1, 1); b.inputs["Emission Strength"].default_value = 25
    if m.name == "SP_LightCone":
        b.inputs["Alpha"].default_value = 0.0

# ground: patchy snow over dark soil
gm = bpy.data.meshes.new("ground"); g = bpy.data.objects.new("ground", gm); sc.collection.objects.link(g)
gm.from_pydata([(-30, -20, 0), (30, -20, 0), (30, 20, 0), (-30, 20, 0)], [], [(0, 1, 2, 3)])
uv = gm.uv_layers.new()
for li, v in enumerate([(0, 0), (1, 0), (1, 1), (0, 1)]): uv.data[li].uv = (v[0] * 10, v[1] * 6.7)
mat = bpy.data.materials.new("ground"); mat.use_nodes = True; nt = mat.node_tree; b = nt.nodes["Principled BSDF"]
snow_i = bpy.data.images.load(os.path.join(TEX, "..", "..", "..", "..", "ArtSource", "SnowPlague", "downloads", "tex", "snow_03", "snow_03_diff_2k.jpg"))
t = nt.nodes.new("ShaderNodeTexImage"); t.image = snow_i
hsv = nt.nodes.new("ShaderNodeHueSaturation"); hsv.inputs["Saturation"].default_value = 0.3; hsv.inputs["Value"].default_value = 0.8
nt.links.new(t.outputs[0], hsv.inputs["Color"]); nt.links.new(hsv.outputs[0], b.inputs[0]); b.inputs["Roughness"].default_value = 0.9
gm.materials.append(mat)

rng = random.Random(3)
def put(name, x, y, rot=None, s=1.0):
    src = objs.get(name)
    if not src: print("missing", name); return
    o = src.copy(); sc.collection.objects.link(o)
    o.location = (x, y, 0); o.rotation_euler = (0, 0, rot if rot is not None else rng.uniform(0, 6.28)); o.scale = (s, s, s)

put("SP_StoneLantern", -4, 3, 0.1); put("SP_StoneLantern", 5, 4.5, 0.0); put("SP_StoneLantern_Broken", 8, -2)
put("SP_LightCone", -4, 3, 0, 0.0001)
put("SP_StrawMat_A", 0.5, 0.5, 0.4); put("SP_StrawMat_Two", 3, -1.5, -0.3); put("SP_StrawMat_Pulled", -2.5, -2.2, 1.9)
put("SP_Handcart_Broken", -7, -1, 0.7); put("SP_CartWheel", -5.2, -3.2)
put("SP_Stakes_Row", 2, 6.5, 0.05); put("SP_Stakes_Broken", 6, 7, -0.1)
put("SP_Sack_Pile", -8.5, 2.5); put("SP_Jar_Broken", -6, 5); put("SP_Jar_Lid", -6.8, 5.6); put("SP_Rags_A", 1.5, 2.5)
put("SP_MapleBush_B", 10, 3); put("SP_MapleBush_A", -10, 6)
for i in range(16):
    put(rng.choice(["SP_DeadShrub_A", "SP_DeadShrub_B", "SP_DeadShrub_C"]), rng.uniform(-12, 12), rng.uniform(-6, 8))
for i in range(40):
    put(rng.choice(["SP_GrassTuft_A", "SP_GrassTuft_B", "SP_GrassTuft_C"]), rng.uniform(-12, 12), rng.uniform(-7, 8))
for i in range(80):
    put(rng.choice(["SP_Pebble_A", "SP_Pebble_B", "SP_Pebble_D", "SP_PebbleCluster", "SP_StoneChip_B", "SP_TwigBit"]), rng.uniform(-12, 12), rng.uniform(-7, 8))
put("SP_SnowDrift_A", 9, -5); put("SP_SnowBank", -9, 8.5, 0.1); put("SP_SnowDrift_B", 11, 7)
put("SP_Stump_A", 7, 1.5); put("SP_DeadTree_B", 12, 9); put("SP_DeadTree_A", -12.5, 9.5); put("SP_Bone", 2.3, -3.6)
# FG0: pines at the camera-side (south) edge
put("SP_RedPine_A", -9, -11, 0.4); put("SP_RedPine_C", 10, -12, 2.0); put("SP_FGBranch_A", -14, -6, 0.3)

world = bpy.data.worlds.new("w"); sc.world = world; world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (0.06, 0.08, 0.12, 1); world.node_tree.nodes["Background"].inputs[1].default_value = 0.8
sun = bpy.data.objects.new("moon", bpy.data.lights.new("moon", "SUN")); sc.collection.objects.link(sun)
sun.rotation_euler = (math.radians(68), 0, math.radians(-35)); sun.data.energy = 2.2; sun.data.color = (0.7, 0.8, 1.0); sun.data.angle = 0.05
for lp in ((-4, 3), (5, 4.5)):
    l = bpy.data.objects.new("lan", bpy.data.lights.new("lan", "SPOT")); sc.collection.objects.link(l)
    l.location = (lp[0], lp[1], 2.0); l.data.energy = 900; l.data.color = (1, 0.55, 0.25); l.data.spot_size = math.radians(150); l.data.shadow_soft_size = 0.1
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); sc.collection.objects.link(cam); sc.camera = cam
ang = math.radians(70); dist = 30
cam.location = (0, -math.cos(ang) * dist, math.sin(ang) * dist); cam.rotation_euler = (math.radians(20), 0, 0); cam.data.lens = 35
for eng in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE"):
    try: sc.render.engine = eng; break
    except TypeError: pass
sc.view_settings.view_transform = "AgX"; sc.view_settings.look = "AgX - Medium High Contrast" if "AgX - Medium High Contrast" else "None"
sc.render.resolution_x, sc.render.resolution_y = 1920, 1080
try:
    sc.eevee.use_shadows = True
except Exception: pass
# mist for fog
sc.render.filepath = OUT
bpy.ops.render.render(write_still=True)
