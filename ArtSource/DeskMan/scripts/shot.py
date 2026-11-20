# blender -b blend -P shot.py -- outprefix action frame[,frame] views
import bpy, sys, math
from mathutils import Vector
a=sys.argv[sys.argv.index('--')+1:]
out,act,frames,views=a[0],a[1],[int(x) for x in a[2].split(',')],a[3].split(',')
sc=bpy.context.scene; ar=bpy.data.objects['ScholarRig']; A=bpy.data.actions[act]; ar.animation_data.action=A
if A.slots: ar.animation_data.action_slot=A.slots[0]
sc.render.film_transparent=False
sc.render.engine='BLENDER_WORKBENCH'; sc.display.shading.light='STUDIO'; sc.display.shading.color_type='TEXTURE'
sc.render.resolution_x=640; sc.render.resolution_y=640
# desk block for context
import bmesh
if 'DeskBlock' not in bpy.data.objects:
    bpy.ops.mesh.primitive_cube_add(size=1,location=(0,-0.60,0.76)); d=bpy.context.object; d.name='DeskBlock'; d.scale=(1.9,0.6,0.04)
    bpy.ops.mesh.primitive_cube_add(size=1,location=(0,0.0,0.46)); c=bpy.context.object; c.name='Seat'; c.scale=(0.55,0.5,0.04)
cam=bpy.data.objects.new('cam',bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera=cam
cam.data.type='ORTHO'; cam.data.ortho_scale=1.9
ctr=Vector((0,-0.3,0.8))
V={'side':Vector((1,0,0)),'front':Vector((0,-1,0.15)),'game':Vector((0.0,-math.cos(math.radians(49.5)),math.sin(math.radians(49.5)))),
   'gameside':Vector((math.cos(math.radians(49.5)),0,math.sin(math.radians(49.5)))),'top':Vector((0,-0.01,1))}
for f in frames:
    sc.frame_set(f)
    for n in views:
        d=V[n].normalized(); cam.location=ctr+d*6; cam.rotation_euler=(-d).to_track_quat('-Z','Y').to_euler()
        sc.render.filepath=f"{out}_{act}_{f}_{n}.png"; bpy.ops.render.render(write_still=True)
