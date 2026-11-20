import bpy, sys, math, mathutils
argv=sys.argv[sys.argv.index('--')+1:]
src,out=argv[0],argv[1]
if src.endswith('.glb'):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=src)
else:
    bpy.ops.wm.open_mainfile(filepath=src)
sc=bpy.context.scene
sc.render.engine='BLENDER_WORKBENCH'
sc.display.shading.light='FLAT'; sc.display.shading.color_type='TEXTURE'
sc.render.resolution_x=600; sc.render.resolution_y=600
meshes=[o for o in sc.objects if o.type=='MESH']
mn=mathutils.Vector((9e9,)*3); mx=-mn
for o in meshes:
    for c in o.bound_box:
        w=o.matrix_world@mathutils.Vector(c); mn=mathutils.Vector(map(min,mn,w)); mx=mathutils.Vector(map(max,mx,w))
ctr=(mn+mx)/2; size=max(mx-mn)*1.2
cam=bpy.data.cameras.new('c'); cam.type='ORTHO'; cam.ortho_scale=size
co=bpy.data.objects.new('c',cam); sc.collection.objects.link(co); sc.camera=co
views={'front':(0,-1,0),'side':(1,0,0),'top':(0,0,1),'back':(0,1,0),'game':(0,-math.cos(math.radians(49.5)),math.sin(math.radians(49.5)))}
for n,d in views.items():
    d=mathutils.Vector(d).normalized()
    co.location=ctr+d*5
    co.rotation_euler=(-d).to_track_quat('-Z','Y').to_euler()
    sc.render.filepath=f"{out}_{n}.png"; bpy.ops.render.render(write_still=True)
