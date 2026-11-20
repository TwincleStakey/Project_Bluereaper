# T-pose scholar (ear piece removed, real scale) -> FBX with embedded texture for the Mixamo auto-rigger.
import bpy, sys
argv = sys.argv[sys.argv.index('--') + 1:]
bpy.ops.wm.open_mainfile(filepath=argv[0])
for o in list(bpy.data.objects):
    if o.name != 'Scholar_Body': bpy.data.objects.remove(o)
b = bpy.data.objects['Scholar_Body']
for m in list(b.modifiers): b.modifiers.remove(m)
b.parent = None
b.vertex_groups.clear()
bpy.ops.object.select_all(action='DESELECT'); b.select_set(True); bpy.context.view_layer.objects.active = b
bpy.ops.export_scene.fbx(filepath=argv[1], use_selection=True, apply_scale_options='FBX_SCALE_ALL', path_mode='COPY', embed_textures=True, add_leaf_bones=False, bake_anim=False)
print('UPLOAD_OK', argv[1], b.dimensions[:])
