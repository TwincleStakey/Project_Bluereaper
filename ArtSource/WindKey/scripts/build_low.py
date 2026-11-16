# WindKey: cut the visible top (gears + hub + collar) off the Tripo key, decimate, new UVs,
# bake albedo / tangent normal / AO from the high cut, add albedo-derived detail into the normal, export FBX.
import bpy, sys, bmesh, math, os
import numpy as np
args=sys.argv[sys.argv.index('--')+1:]
SRC, OUT = args[0], args[1]
CUT=float(args[2]) if len(args)>2 else 0.50
TARGET=int(args[3]) if len(args)>3 else 1300
RES=1024
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=SRC)
hi=[o for o in bpy.data.objects if o.type=='MESH'][0]; hi.name='WK_High'
bpy.context.view_layer.objects.active=hi
for o in bpy.data.objects: o.select_set(o==hi)
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
hi.parent=None
# cut
bm=bmesh.new(); bm.from_mesh(hi.data)
geom=bm.verts[:]+bm.edges[:]+bm.faces[:]
bmesh.ops.bisect_plane(bm, geom=geom, plane_co=(0,0,CUT), plane_no=(0,0,1), clear_inner=True)
bm.to_mesh(hi.data); bm.free()
hi.data.update()
def tris(o): return sum(len(p.vertices)-2 for p in o.data.polygons)
print("HIGH tris", tris(hi))
# low
lo=hi.copy(); lo.data=hi.data.copy(); lo.name='WindKey'; bpy.context.collection.objects.link(lo)
bpy.context.view_layer.objects.active=lo
for o in bpy.data.objects: o.select_set(o==lo)
bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.mesh.remove_doubles(threshold=0.0015)
bpy.ops.object.mode_set(mode='OBJECT')
t0=tris(lo)
if t0>TARGET:
    m=lo.modifiers.new('dec','DECIMATE'); m.ratio=TARGET/t0; m.use_collapse_triangulate=True
    bpy.ops.object.modifier_apply(modifier='dec')
m=lo.modifiers.new('tri','TRIANGULATE'); bpy.ops.object.modifier_apply(modifier='tri')
print("LOW tris", tris(lo))
# fresh UVs
while lo.data.uv_layers: lo.data.uv_layers.remove(lo.data.uv_layers[0])
lo.data.uv_layers.new(name='UV')
bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.012)
try: bpy.ops.uv.pack_islands(margin=0.006, rotate=True)
except Exception as e: print('pack', e)
bpy.ops.object.mode_set(mode='OBJECT')
bpy.ops.object.shade_smooth()
try: bpy.ops.object.shade_auto_smooth(angle=math.radians(40))
except Exception as e: print('autosmooth',e)
# bake setup
sc=bpy.context.scene; sc.render.engine='CYCLES'; sc.cycles.samples=16; sc.cycles.device='CPU'
imgs={}
mat=bpy.data.materials.new('WK_Bake'); mat.use_nodes=True; lo.data.materials.clear(); lo.data.materials.append(mat)
nt=mat.node_tree
for k in ['albedo','normal','ao']:
    im=bpy.data.images.new('WK_'+k,RES,RES,alpha=False, float_buffer=(k=='normal'))
    if k!='albedo': im.colorspace_settings.name='Non-Color'
    imgs[k]=im
tn=nt.nodes.new('ShaderNodeTexImage')
def bake(kind, img, **kw):
    tn.image=img; nt.nodes.active=tn
    for o in bpy.data.objects: o.select_set(o in (hi,lo))
    bpy.context.view_layer.objects.active=lo
    bpy.ops.object.bake(type=kind, use_selected_to_active=True, cage_extrusion=0.02, max_ray_distance=0.06, margin=8, **kw)
sc.render.bake.use_pass_direct=False; sc.render.bake.use_pass_indirect=False; sc.render.bake.use_pass_color=True
bake('DIFFUSE', imgs['albedo'], pass_filter={'COLOR'})
bake('NORMAL', imgs['normal'], normal_space='TANGENT')
sc.cycles.samples=64
bake('AO', imgs['ao'])
# detail normal from albedo luminance (texture-only details -> bumps)
def arr(im): return np.array(im.pixels[:],dtype=np.float32).reshape(RES,RES,4)
A=arr(imgs['albedo']); N=arr(imgs['normal'])
lum=(A[...,0]*0.3+A[...,1]*0.59+A[...,2]*0.11)
def blur(x):
    return (x+np.roll(x,1,0)+np.roll(x,-1,0)+np.roll(x,1,1)+np.roll(x,-1,1))/5
h=blur(blur(lum))
dx=(np.roll(h,-1,1)-np.roll(h,1,1))*0.5; dy=(np.roll(h,-1,0)-np.roll(h,1,0))*0.5
S=float(args[4]) if len(args)>4 else 6.0
n=N[...,:3]*2-1
n[...,0]-=dx*S; n[...,1]-=dy*S
n/=np.linalg.norm(n,axis=2,keepdims=True)+1e-6
N[...,:3]=n*0.5+0.5
os.makedirs(OUT,exist_ok=True)
def save(name, data, nc=False):
    im=bpy.data.images.new(name,RES,RES,alpha=False); 
    if nc: im.colorspace_settings.name='Non-Color'
    im.pixels.foreach_set(data.ravel()); im.filepath_raw=os.path.join(OUT,name+'.png'); im.file_format='PNG'; im.save()
save('WK_Albedo', A)
save('WK_Normal', N, True)
AO=arr(imgs['ao']); save('WK_AO', AO, True)
# origin at the shaft entry (cut plane centre), export Y-up FBX
bpy.context.scene.cursor.location=(0,0,CUT)
for o in bpy.data.objects: o.select_set(o==lo)
bpy.context.view_layer.objects.active=lo
bpy.ops.object.origin_set(type='ORIGIN_CURSOR'); lo.location=(0,0,0)
bpy.data.objects.remove(hi)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT,'windkey_low.blend'))
bpy.ops.export_scene.fbx(filepath=os.path.join(OUT,'WindKey.fbx'), use_selection=True, apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y', mesh_smooth_type='FACE', use_tspace=True, bake_anim=False, add_leaf_bones=False)
print("DONE")
