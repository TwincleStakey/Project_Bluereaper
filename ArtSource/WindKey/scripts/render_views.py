import bpy, sys, math, mathutils
args=sys.argv[sys.argv.index('--')+1:]
src,out=args[0],args[1]
if src.endswith('.blend'): bpy.ops.wm.open_mainfile(filepath=src)
else:
    bpy.ops.wm.read_factory_settings(use_empty=True); bpy.ops.import_scene.gltf(filepath=src)
sc=bpy.context.scene
sc.render.engine='BLENDER_EEVEE_NEXT' if 'BLENDER_EEVEE_NEXT' in [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items] else 'BLENDER_EEVEE'
sc.render.resolution_x=sc.render.resolution_y=640
w=bpy.data.worlds.new('w'); sc.world=w; w.use_nodes=True; w.node_tree.nodes['Background'].inputs[1].default_value=1.2
ms=[o for o in sc.objects if o.type=='MESH']
pts=[o.matrix_world@mathutils.Vector(c) for o in ms for c in o.bound_box]
c=sum(pts,mathutils.Vector())/len(pts); r=max((p-c).length for p in pts)
cam=bpy.data.objects.new('cam',bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera=cam
cam.data.type='ORTHO'; cam.data.ortho_scale=r*2.2
for name,d in [('front',(0,-1,0)),('side',(1,0,0)),('top',(0,0,1)),('persp',(0.6,-0.8,0.5))]:
    v=mathutils.Vector(d).normalized(); cam.location=c+v*r*4
    cam.rotation_euler=(-v).to_track_quat('-Z','Y').to_euler()
    sc.render.filepath=f'{out}_{name}.png'; bpy.ops.render.render(write_still=True)
