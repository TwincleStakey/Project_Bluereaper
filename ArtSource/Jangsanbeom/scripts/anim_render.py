import bpy, math, sys
from mathutils import Vector
argv=sys.argv[sys.argv.index('--')+1:]; OUT=argv[0]
sc=bpy.context.scene
for e in ('BLENDER_EEVEE','BLENDER_EEVEE_NEXT'):
    try: sc.render.engine=e; break
    except Exception: pass
sc.render.resolution_x=960; sc.render.resolution_y=540
w=sc.world or bpy.data.worlds.new('W'); sc.world=w; w.use_nodes=True
bg=w.node_tree.nodes.get('Background'); bg.inputs[0].default_value=(0.5,0.5,0.52,1); bg.inputs[1].default_value=1.0
sc.view_settings.view_transform='Standard'
for ang,en in ((50,3.0),(-30,1.5)):
    ld=bpy.data.lights.new('S','SUN'); ld.energy=en; L=bpy.data.objects.new('S',ld); sc.collection.objects.link(L); L.rotation_euler=(math.radians(45),0,math.radians(ang))
cd=bpy.data.cameras.new('C'); cam=bpy.data.objects.new('C',cd); sc.collection.objects.link(cam); sc.camera=cam
cd.type='ORTHO'; cd.ortho_scale=1.35
cam.location=Vector((1.5,-1.9,0.85)); cam.rotation_euler=(Vector((0,-0.05,0.27))-cam.location).to_track_quat('-Z','Y').to_euler()
# floor plane for contact reading
bpy.ops.mesh.primitive_plane_add(size=4, location=(0,0,0)); fl=bpy.context.object
m=bpy.data.materials.new('F'); m.use_nodes=True; m.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value=(0.42,0.42,0.44,1); fl.data.materials.append(m)
arm=[o for o in bpy.data.objects if o.type=='ARMATURE'][0]
act=arm.animation_data.action; f0,f1=[int(x) for x in act.frame_range]
sc.frame_start=f0; sc.frame_end=f1
sc.render.image_settings.file_format='PNG'
sc.render.filepath=OUT+'_frames/'; bpy.ops.render.render(animation=True)
sc.render.image_settings.file_format='PNG'
for i,f in enumerate(range(f0,f1+1,(f1-f0)//7)):
    sc.frame_set(f); sc.render.filepath=OUT+f'_k{i}.png'; bpy.ops.render.render(write_still=True)
print('ANIM_DONE',f0,f1)
