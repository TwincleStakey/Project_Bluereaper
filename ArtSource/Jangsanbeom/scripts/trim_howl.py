import bpy
CUT, END = 74, 92
arm=[o for o in bpy.data.objects if o.type=='ARMATURE'][0]
act=arm.animation_data.action
fcs = act.fcurves if hasattr(act,'fcurves') else [fc for l in act.layers for s in l.strips for cb in s.channelbags for fc in cb.fcurves]
n=0
for fc in fcs:
    v0=fc.evaluate(1)
    for kp in [k for k in fc.keyframe_points if k.co.x > CUT][::-1]:
        fc.keyframe_points.remove(kp)
    k=fc.keyframe_points.insert(END, v0)
    for kp in fc.keyframe_points:
        if kp.co.x >= CUT: kp.interpolation='BEZIER'; kp.handle_left_type=kp.handle_right_type='AUTO_CLAMPED'
    fc.update(); n+=1
bpy.context.scene.frame_start=1; bpy.context.scene.frame_end=END
try: act.frame_range=(1,END)
except Exception: pass
act.use_frame_range=True; act.frame_start=1; act.frame_end=END
bpy.ops.wm.save_mainfile()
print('TRIM',n)
