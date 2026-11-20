# game-camera renders (perspective, FOV 45, pitch 55 like LSCamera) in a bright and a very dark field
import bpy, math, sys
from mathutils import Vector
S=sys.argv[sys.argv.index('--')+1]
sc=bpy.context.scene
for e in ('BLENDER_EEVEE_NEXT','BLENDER_EEVEE'):
    try: sc.render.engine=e; break
    except Exception: pass
sc.render.resolution_x=800; sc.render.resolution_y=600; sc.view_settings.view_transform='Standard'
bo=[o for o in bpy.data.objects if o.name.startswith('tripo')][0]
ev=bo.evaluated_get(bpy.context.evaluated_depsgraph_get()); vs=[ev.matrix_world@v.co for v in ev.data.vertices]
lo=Vector([min(v[i] for v in vs) for i in range(3)]); hi=Vector([max(v[i] for v in vs) for i in range(3)]); CEN=(lo+hi)/2
bpy.ops.mesh.primitive_plane_add(size=8, location=(0,0,lo.z)); gp=bpy.context.object
gm=bpy.data.materials.new('Ground'); gm.use_nodes=True; gb=gm.node_tree.nodes['Principled BSDF']; gp.data.materials.append(gm)
w=sc.world or bpy.data.worlds.new('W'); sc.world=w; w.use_nodes=True; bg=w.node_tree.nodes.get('Background')
cd=bpy.data.cameras.new('C'); cam=bpy.data.objects.new('C',cd); sc.collection.objects.link(cam); sc.camera=cam; cd.lens_unit='FOV'; cd.angle=math.radians(45)
suns=[]
for ang in (50,-30):
    ld=bpy.data.lights.new('S','SUN'); L=bpy.data.objects.new('S',ld); sc.collection.objects.link(L); L.rotation_euler=(math.radians(45),0,math.radians(ang)); suns.append(ld)
def light(dark):
    if dark:
        bg.inputs[0].default_value=(0.004,0.004,0.007,1); gb.inputs['Base Color'].default_value=(0.02,0.02,0.025,1); gb.inputs['Roughness'].default_value=0.9
        suns[0].energy=0.35; suns[0].color=(0.55,0.65,1.0); suns[1].energy=0.0
    else:
        bg.inputs[0].default_value=(0.5,0.5,0.52,1); gb.inputs['Base Color'].default_value=(0.35,0.34,0.32,1); gb.inputs['Roughness'].default_value=0.9
        suns[0].energy=3.0; suns[0].color=(1,1,1); suns[1].energy=1.5
def game(yaw,dist,name,tgt=None,pitch=55):
    t=tgt or CEN; y=math.radians(yaw); p=math.radians(pitch)
    cam.location=t+Vector((math.sin(y)*math.cos(p),-math.cos(y)*math.cos(p),math.sin(p)))*dist
    cam.rotation_euler=(t-cam.location).to_track_quat('-Z','Y').to_euler()
    sc.render.filepath=S+name; bpy.ops.render.render(write_still=True)
for dark in (False,True):
    light(dark); k='dark' if dark else 'bright'
    game(35,0.85,f'close_{k}.png',tgt=CEN+Vector((0,0,0.08)))
    game(20,1.9,f'gA_{k}.png')
    game(140,1.9,f'gB_{k}.png')
print('DONE')
