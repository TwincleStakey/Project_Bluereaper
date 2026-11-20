# 장산범 v2: export the full model (body + gramophone + snake parasite, rest pose, no animation) for Unity.
#   blender -b jsb_v2_snake.blend -P v2_export_model.py -- <out.fbx>
# Animations come from the small per-clip FBX files (same AutoRig hierarchy), so the props are stored once.
import bpy, sys
out = sys.argv[sys.argv.index('--') + 1]
sc = bpy.context.scene
arm = bpy.data.objects['AutoRig']
if arm.animation_data: arm.animation_data.action = None
for p in arm.pose.bones: p.matrix_basis.identity()
bpy.ops.object.select_all(action='DESELECT')
n = 0
for o in sc.objects:
    if o.type in ('ARMATURE', 'MESH') and not o.name.startswith('Plane') and o.visible_get(): o.select_set(True); n += 1
bpy.ops.export_scene.fbx(filepath=out, use_selection=True, apply_scale_options='FBX_SCALE_ALL', bake_anim=False, add_leaf_bones=False, path_mode='COPY', embed_textures=False)
print('MODEL_OK', out, n, 'objects', sorted({m.name for o in sc.objects if o.type == 'MESH' for m in o.data.materials if m})[:30])
