import bpy, math, sys, json
from mathutils import Matrix, Vector
S=r"C:/Users/PC/AppData/Local/Temp/claude/b4d516afcc65658a/4585abb7-cb93-4538-986c-c14fdce771d9/scratchpad/"
arm=[o for o in bpy.data.objects if o.type=='ARMATURE'][0]
sc=bpy.context.scene
AX={'x':Vector((1,0,0)),'y':Vector((0,1,0)),'z':Vector((0,0,1))}
def reset():
    for pb in arm.pose.bones: pb.matrix_basis=Matrix()
    bpy.context.view_layer.update()
def rot(name,axis,deg):
    pb=arm.pose.bones[name]
    h=pb.head.copy()
    R=Matrix.Translation(h)@Matrix.Rotation(math.radians(deg),4,AX[axis])@Matrix.Translation(-h)
    pb.matrix=R@pb.matrix
    bpy.context.view_layer.update()
def move(name,v):
    pb=arm.pose.bones[name]; pb.matrix=Matrix.Translation(Vector(v))@pb.matrix; bpy.context.view_layer.update()
for n in ('Neck2','Head'): arm.data.bones[n].inherit_scale='NONE'
POSES=json.load(open(S+"poses.json",encoding='utf-8'))
# render setup
sc.render.engine='BLENDER_EEVEE' if hasattr(bpy.types,'EEVEE_RENDER_PT_sampling') or True else 'CYCLES'
try: sc.render.engine='BLENDER_EEVEE_NEXT'
except: pass
try: sc.render.engine='BLENDER_EEVEE'
except: pass
sc.render.resolution_x=640; sc.render.resolution_y=480; sc.render.film_transparent=False
w=sc.world or bpy.data.worlds.new('W'); sc.world=w; w.use_nodes=True
bg=w.node_tree.nodes.get('Background'); bg.inputs[0].default_value=(0.55,0.55,0.55,1); bg.inputs[1].default_value=1.2
sc.view_settings.view_transform='Standard'
cd=bpy.data.cameras.new('C'); cam=bpy.data.objects.new('C',cd); sc.collection.objects.link(cam); sc.camera=cam
cd.type='ORTHO'
ld=bpy.data.lights.new('S','SUN'); ld.energy=3; L=bpy.data.objects.new('S',ld); sc.collection.objects.link(L); L.rotation_euler=(math.radians(50),0,math.radians(30))
def shot(pos,tgt,scale,path):
    cam.location=Vector(pos); cam.rotation_euler=(Vector(tgt)-cam.location).to_track_quat('-Z','Y').to_euler(); cd.ortho_scale=scale
    sc.render.filepath=path; bpy.ops.render.render(write_still=True)
for key,ops in POSES.items():
    reset()
    for op in ops:
        if op[0]=='m': move(op[1],op[2])
        elif op[0]=='s':
            arm.pose.bones[op[1]].scale=(1,op[2],1); bpy.context.view_layer.update()
        else: rot(op[0],op[1],op[2])
    shot((2.5,0.0,0.25),(0,0.0,0.25),1.3,S+f"mock/{key}_side.png")
    shot((1.6,-1.9,0.9),(0,0,0.2),1.3,S+f"mock/{key}_34.png")
print('DONE')
