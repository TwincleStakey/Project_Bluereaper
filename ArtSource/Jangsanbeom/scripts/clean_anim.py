import bpy, sys
argv=sys.argv[sys.argv.index('--')+1:]
SMOOTH=[tuple(map(int,s.split(':'))) for s in argv[0].split(',')] if argv and argv[0] else []
sc=bpy.context.scene
arm=[o for o in bpy.data.objects if o.type=='ARMATURE'][0]
body=[o for o in bpy.data.objects if o.type=='MESH' and o.name.startswith('tripo')][0]
act=arm.animation_data.action
fcs = act.fcurves if hasattr(act,'fcurves') else [fc for l in act.layers for s in l.strips for cb in s.channelbags for fc in cb.fcurves]
f0,f1=[int(x) for x in act.frame_range]
# 1) smooth spikes: drop keys inside each window so the curve interpolates across it
for a,b in SMOOTH:
    for fc in fcs:
        for kp in [k for k in fc.keyframe_points if a < k.co.x < b][::-1]: fc.keyframe_points.remove(kp)
        for kp in fc.keyframe_points:
            if a-1 <= kp.co.x <= b+1: kp.interpolation='BEZIER'; kp.handle_left_type=kp.handle_right_type='AUTO_CLAMPED'
        fc.update()
# 2) ground contact: per frame, lift/lower the whole rig so the lowest body vertex sits on the floor
import numpy as np
base_z=arm.location.z
lows=[]
for f in range(f0,f1+1):
    sc.frame_set(f); dg=bpy.context.evaluated_depsgraph_get(); eb=body.evaluated_get(dg); me=eb.to_mesh()
    co=np.empty(len(me.vertices)*3); me.vertices.foreach_get('co',co); co=co.reshape(-1,3)
    mw=np.array(body.matrix_world); z=(co@mw[:3,:3].T+mw[:3,3])[:,2]
    lows.append(float(np.percentile(z,0.05))); eb.to_mesh_clear()
lows=np.convolve(np.pad(lows,2,mode='edge'),np.ones(5)/5,mode='valid')  # smooth to avoid jitter
for f,lz in zip(range(f0,f1+1),lows):
    arm.location.z=base_z-(lz-0.0); arm.keyframe_insert('location',index=2,frame=f)
bpy.ops.wm.save_mainfile()
print('CLEAN', f0,f1, 'minlow',round(min(lows),4),'maxlow',round(max(lows),4))
