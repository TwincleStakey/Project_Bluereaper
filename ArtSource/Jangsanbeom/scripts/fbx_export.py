import bpy, sys
out=sys.argv[sys.argv.index('--')+1]
# curves -> meshes so FBX keeps pipes/ropes
for o in list(bpy.data.objects):
    if o.type=='CURVE':
        bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active=o
        bpy.ops.object.convert(target='MESH')
bpy.ops.object.select_all(action='DESELECT')
for o in bpy.data.objects:
    if o.type in ('ARMATURE','MESH') and not o.name.startswith('Plane'): o.select_set(True)
bpy.ops.export_scene.fbx(filepath=out, use_selection=True, apply_scale_options='FBX_SCALE_ALL', bake_anim=True, bake_anim_use_all_actions=False, bake_anim_use_nla_strips=False, add_leaf_bones=False, path_mode='COPY', embed_textures=True)
print('FBX_OK', out)
