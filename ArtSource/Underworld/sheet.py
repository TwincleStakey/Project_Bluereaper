import bpy, math
from mathutils import Vector
bpy.ops.wm.open_mainfile(filepath=r"F:\UnityProject\BulletHeavenArt\ArtSource\Underworld\underworld_props.blend")
sc = bpy.context.scene
skip = {"UW_Plaza", "UW_PlatformBack", "UW_GateTerrace", "UW_Steps"}
obs = [o for o in sc.objects if o.name not in skip]
x = 0.0; y = 0.0; rowh = 0
cols = 7
for i, o in enumerate(sorted(obs, key=lambda o: o.name)):
    c, r = i % cols, i // cols
    o.location = (c * 11.0, -r * 14.0, 0)
for o in sc.objects:
    if o.name in skip:
        o.location = (90, 0, 0)
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
sc.collection.objects.link(cam)
cam.data.type = "ORTHO"; cam.data.ortho_scale = 85
cam.location = (33, -60, 40); cam.rotation_euler = (math.radians(62), 0, 0)
sc.camera = cam
try:
    sc.render.engine = "BLENDER_WORKBENCH"
except TypeError:
    pass
sc.display.shading.color_type = "MATERIAL"
sc.display.shading.light = "STUDIO"
sc.render.resolution_x = 1600; sc.render.resolution_y = 1100
sc.render.filepath = r"F:\UnityProject\BulletHeavenArt\ArtSource\Underworld\props_sheet.png"
bpy.ops.render.render(write_still=True)
# ground layout top-down
for o in sc.objects:
    o.hide_render = o.name not in skip
    if o.name in skip:
        o.location = (0, 0, 0)
cam.data.ortho_scale = 46; cam.location = (0, 2, 60); cam.rotation_euler = (0, 0, 0)
sc.render.resolution_x = 1000; sc.render.resolution_y = 900
sc.render.filepath = r"F:\UnityProject\BulletHeavenArt\ArtSource\Underworld\ground_top.png"
bpy.ops.render.render(write_still=True)
