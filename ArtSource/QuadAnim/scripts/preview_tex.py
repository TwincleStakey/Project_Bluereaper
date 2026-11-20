# Quick review render of a retargeted clip blend: side + 3/4 workbench frames -> <out dir>/<view>_####.png
#   blender -b out/<clip>.blend -P preview.py -- <out dir> [step]
import bpy, sys, os, math
from mathutils import Vector
a = sys.argv[sys.argv.index('--') + 1:]
od = a[0]; step = int(a[1]) if len(a) > 1 else 2
os.makedirs(od, exist_ok=True)
sc = bpy.context.scene
sc.render.engine = 'BLENDER_WORKBENCH'
sc.display.shading.light = 'STUDIO'; sc.display.shading.color_type = 'TEXTURE'
sc.display.shading.show_shadows = True
sc.render.resolution_x, sc.render.resolution_y = 640, 400
sc.render.film_transparent = False
sc.world = sc.world or bpy.data.worlds.new("W"); sc.world.color = (0.18, 0.18, 0.2)
# ground grid
bpy.ops.mesh.primitive_plane_add(size=4, location=(0.03, 0.0, 0))
pl = bpy.context.object
m = bpy.data.materials.new('Gnd'); m.diffuse_color = (0.35, 0.36, 0.33, 1); pl.data.materials.append(m)
bpy.ops.object.modifier_add(type='WIREFRAME') if False else None
cam_d = bpy.data.cameras.new('C'); cam = bpy.data.objects.new('C', cam_d); sc.collection.objects.link(cam)
cam_d.lens = 50; sc.camera = cam
tgt = bpy.data.objects['AutoRig']
views = {'side': (Vector((1.6, 0.0, 0.35)), Vector((0.03, 0.0, 0.2))),
         'q34': (Vector((1.0, -1.1, 0.7)), Vector((0.03, 0.0, 0.18))), 'top': (Vector((0.0, -0.25, 2.0)), Vector((0.0, -0.05, 0.1)))}
for vn, (pos, look) in views.items():
    cam.location = pos
    cam.rotation_euler = (look - pos).to_track_quat('-Z', 'Y').to_euler()
    for f in range(sc.frame_start, sc.frame_end + 1, step):
        sc.frame_set(f)
        sc.render.filepath = os.path.join(od, '%s_%04d.png' % (vn, f))
        bpy.ops.render.render(write_still=True)
print('PREVIEW_OK', od)
