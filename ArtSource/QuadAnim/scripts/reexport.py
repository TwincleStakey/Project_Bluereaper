# re-export a retargeted clip blend to FBX without embedded textures: blender -b out/x.blend -P reexport.py -- out.fbx
import bpy, sys
out = sys.argv[sys.argv.index('--') + 1]
sc = bpy.context.scene
bpy.ops.object.select_all(action='DESELECT')
for o in sc.objects:
    if o.type in ('ARMATURE', 'MESH') and not o.name.startswith('Plane') and o.visible_get(): o.select_set(True)
bpy.ops.export_scene.fbx(filepath=out, use_selection=True, apply_scale_options='FBX_SCALE_ALL', bake_anim=True, bake_anim_use_all_actions=False,
                         bake_anim_use_nla_strips=False, add_leaf_bones=False, path_mode='STRIP', embed_textures=False)
print('FBX_OK', out)
