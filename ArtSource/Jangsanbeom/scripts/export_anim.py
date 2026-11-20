# load the howl/charge action from the old gramophone blends onto the snake model's armature and export FBX for Unity
import bpy, sys
a = sys.argv[sys.argv.index('--') + 1:]; src, out = a[0], a[1]
arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
if src != 'self':
    with bpy.data.libraries.load(src, link=False) as (df, dt):
        dt.actions = list(df.actions); dt.objects = [n for n in df.objects]
    srcarm = [o for o in dt.objects if o and o.type == 'ARMATURE'][0]
    act = srcarm.animation_data.action
    names = {fc.data_path.split('"')[1] for fc in act.fcurves if 'pose.bones' in fc.data_path} if hasattr(act, 'fcurves') else set()
    print('ACT', act.name, act.frame_range[:], 'bones', len(names), 'missing', [n for n in names if n not in arm.pose.bones])
    arm.animation_data_create(); arm.animation_data.action = act
    try: arm.animation_data.action_slot = arm.animation_data.action_suitable_slots[0]
    except Exception as e: print('slot', e)
    bpy.context.scene.frame_start, bpy.context.scene.frame_end = int(act.frame_range[0]), int(act.frame_range[1])
    for o in dt.objects:
        if o and o.users_collection == (): bpy.data.objects.remove(o)
bpy.ops.object.select_all(action='DESELECT')
for o in bpy.context.scene.objects:
    if o.type in ('ARMATURE', 'MESH') and not o.name.startswith('Plane'): o.select_set(True)
bpy.ops.export_scene.fbx(filepath=out, use_selection=True, apply_scale_options='FBX_SCALE_ALL', bake_anim=True, bake_anim_use_all_actions=False, bake_anim_use_nla_strips=False, add_leaf_bones=False, path_mode='COPY', embed_textures=True)
print('FBX_OK', out, bpy.context.scene.frame_start, bpy.context.scene.frame_end)
