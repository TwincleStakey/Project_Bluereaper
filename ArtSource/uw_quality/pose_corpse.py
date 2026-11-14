# Pose the Sketchfab "Zombie: The Burnt" (CatBYTE Studio, CC BY) MakeHuman rig into lying corpse poses, bake, decimate, export FBX.
import bpy, sys, math
from mathutils import Matrix, Vector
out = sys.argv[sys.argv.index('--')+1]
src = bpy.data.filepath
X, Y, Z = Vector((1,0,0)), Vector((0,1,0)), Vector((0,0,1))   # armature space: Y up, +X = character left, +Z = front

POSES = {
 # on the back, pugilistic burn pose: elbows/knees flexed, fists towards the chest
 'Corpse_Back': [('upperarm01_L', Z, -55), ('upperarm01_L', Y, -35), ('lowerarm01_L', Y, -95),
                 ('upperarm01_R', Z, 50), ('upperarm01_R', Y, 40), ('lowerarm01_R', Y, 85),
                 ('upperleg01_L', X, -35), ('lowerleg01_L', X, 70), ('upperleg01_R', X, -20), ('upperleg01_R', Z, -10), ('lowerleg01_R', X, 45),
                 ('neck01', X, 20), ('spine03', X, 10)],
 # face down, one arm reaching forward (crawled and fell)
 'Corpse_Front': [('upperarm01_L', Z, 80), ('upperarm01_L', Y, -60), ('lowerarm01_L', Y, -20),
                  ('upperarm01_R', Z, -70), ('upperarm01_R', Y, -20), ('lowerarm01_R', Y, 70),
                  ('upperleg01_L', X, -40), ('lowerleg01_L', X, 60), ('upperleg01_L', Z, -15), ('upperleg01_R', Z, 8),
                  ('neck01', Z, 25), ('spine03', Z, 8)],
 # curled on the side
 'Corpse_Side': [('spine04', X, -20), ('spine02', X, -20), ('neck01', X, -25),
                 ('upperarm01_L', Z, -70), ('upperarm01_L', Y, -70), ('lowerarm01_L', Y, -110),
                 ('upperarm01_R', Z, 70), ('upperarm01_R', Y, 70), ('lowerarm01_R', Y, 100),
                 ('upperleg01_L', X, -80), ('lowerleg01_L', X, 100), ('upperleg01_R', X, -60), ('lowerleg01_R', X, 110)],
}
BODY = {'Corpse_Back': (-90, 0), 'Corpse_Front': (90, 0), 'Corpse_Side': (0, 90)}   # (rot about X, rot about Z) so the body lies down

for name, ops in POSES.items():
    bpy.ops.wm.open_mainfile(filepath=src)
    arm = bpy.data.objects['zombie_maleskel']
    if arm.animation_data: arm.animation_data.action = None
    for pb in arm.pose.bones: pb.matrix_basis = Matrix.Identity(4)
    bpy.context.view_layer.update()
    for bone, axis, deg in ops:
        pb = arm.pose.bones.get(bone)
        if not pb: print('missing', bone); continue
        M = pb.matrix.copy(); h = M.translation.copy()
        pb.matrix = Matrix.Translation(h) @ Matrix.Rotation(math.radians(deg), 4, axis) @ Matrix.Translation(-h) @ M
        bpy.context.view_layer.update()
    body = bpy.data.objects['zombie_maleskel-male_generic']
    for o in list(bpy.data.objects):
        if o not in (arm, body): bpy.data.objects.remove(o, do_unlink=True)
    bpy.context.view_layer.objects.active = body
    body.select_set(True)
    deps = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(body.evaluated_get(deps))
    me.transform(body.matrix_world)
    new = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(new)
    bpy.data.objects.remove(body, do_unlink=True); bpy.data.objects.remove(arm, do_unlink=True)
    # world: Blender Z up. lie down
    rx, rz = BODY[name]
    me.transform(Matrix.Rotation(math.radians(rx), 4, 'X') @ Matrix.Rotation(math.radians(rz), 4, 'Y'))
    # scale to 1.75 m standing height (rig is ~15.1 units tall) and drop onto z = 0, centred
    s = 1.75 / 15.1; me.transform(Matrix.Scale(s, 4))
    vs = [v.co for v in me.vertices]
    mn = Vector((min(v.x for v in vs), min(v.y for v in vs), min(v.z for v in vs))); mx = Vector((max(v.x for v in vs), max(v.y for v in vs), max(v.z for v in vs)))
    me.transform(Matrix.Translation(Vector((-(mn.x+mx.x)/2, -(mn.y+mx.y)/2, -mn.z + 0.0))))
    bpy.ops.object.select_all(action='DESELECT'); new.select_set(True); bpy.context.view_layer.objects.active = new
    d = new.modifiers.new('dec', 'DECIMATE'); d.ratio = 0.2
    bpy.ops.object.modifier_apply(modifier='dec')
    print('POSED', name, len(new.data.polygons), tuple(round(v,2) for v in new.dimensions))
    bpy.context.scene.unit_settings.scale_length = 1.0
    bpy.ops.export_scene.fbx(filepath=f'{out}/{name}.fbx', use_selection=True, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y', mesh_smooth_type='FACE', bake_anim=False, add_leaf_bones=False)
    bpy.ops.render.opengl if False else None
