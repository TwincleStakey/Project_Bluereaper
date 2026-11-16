# Bring the howl/charge motion from the old gramophone blend onto the snake model EXACTLY: append the source armature
# (with its action), make every target bone copy the source bone in world space, bake visual keys, export FBX.
# (Assigning the action directly lost the Hips translation.)
import bpy, sys
a = sys.argv[sys.argv.index('--') + 1:]; src, out = a[0], a[1]
sc = bpy.context.scene
arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
with bpy.data.libraries.load(src, link=False) as (df, dt):
    dt.objects = [n for n in df.objects]; dt.scenes = list(df.scenes)
srcarm = [o for o in dt.objects if o and o.type == 'ARMATURE'][0]
sc.collection.objects.link(srcarm)
s0 = dt.scenes[0]; f0, f1 = s0.frame_start, s0.frame_end
act = srcarm.animation_data.action if srcarm.animation_data else None
if act is not None and len(a) > 2:          # clip-length range from the action (video2rig scenes keep 250)
    f0, f1 = int(act.frame_range[0]), int(act.frame_range[1])
speed = float(a[2]) if len(a) > 2 else 1.0  # play faster in the engine: same keys, higher fps
sc.render.fps = int(round(s0.render.fps * speed)); sc.render.fps_base = 1.0
print('RANGE', f0, f1, 'fps', sc.render.fps)
sc.frame_start, sc.frame_end = f0, f1
for o in dt.objects:
    if o and o is not srcarm and not o.users_collection: bpy.data.objects.remove(o)
for s in dt.scenes: bpy.data.scenes.remove(s)
srcarm.matrix_world = arm.matrix_world.copy() if srcarm.matrix_world == srcarm.matrix_world else srcarm.matrix_world
bpy.context.view_layer.update()
for o in bpy.context.view_layer.objects: o.select_set(False)
arm.select_set(True); bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode='POSE')
if arm.animation_data: arm.animation_data.action = None
for pb in arm.pose.bones:
    pb.matrix_basis.identity()
    if pb.name in srcarm.pose.bones:
        c = pb.constraints.new('COPY_TRANSFORMS'); c.target = srcarm; c.subtarget = pb.name
bpy.ops.pose.select_all(action='SELECT')
bpy.ops.nla.bake(frame_start=f0, frame_end=f1, only_selected=False, visual_keying=True, clear_constraints=True, use_current_action=False, bake_types={'POSE'})
bpy.ops.object.mode_set(mode='OBJECT')
bpy.data.objects.remove(srcarm)
# sanity: hips travel
res = []
for fr in (f0 + 1, f0 + 30):
    sc.frame_set(fr); res.append((arm.matrix_world @ arm.pose.bones['Hips'].head).copy())
print('HIPS_MOVE', round((res[1] - res[0]).length, 4), 'range', f0, f1)
bpy.ops.object.select_all(action='DESELECT')
for o in sc.objects:
    if o.type in ('ARMATURE', 'MESH') and not o.name.startswith('Plane'): o.select_set(True)
bpy.ops.export_scene.fbx(filepath=out, use_selection=True, apply_scale_options='FBX_SCALE_ALL', bake_anim=True, bake_anim_use_all_actions=False, bake_anim_use_nla_strips=False, add_leaf_bones=False, path_mode='COPY', embed_textures=True)
print('FBX_OK', out)
