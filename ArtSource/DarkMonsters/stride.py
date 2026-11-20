import bpy, sys
path=sys.argv[sys.argv.index("--")+1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=path)
arm=[o for o in bpy.data.objects if o.type=='ARMATURE'][0]
act=arm.animation_data.action
f0,f1=[int(x) for x in act.frame_range]
sc=bpy.context.scene; fps=sc.render.fps/sc.render.fps_base
def bone(n):
    for b in arm.pose.bones:
        if b.name.endswith(n): return b
L,R,H=bone("LeftToeBase") or bone("LeftFoot"),bone("RightToeBase") or bone("RightFoot"),bone("Hips")
mw=arm.matrix_world
sc.frame_set(f0)
pts=[]
for f in range(f0,f1+1):
    sc.frame_set(f)
    l=mw@L.head; r=mw@R.head; h=mw@H.head
    pts.append((l,r,h))
speeds=[]
for i in range(1,len(pts)):
    (l0,r0,h0),(l1,r1,h1)=pts[i-1],pts[i]
    # planted foot = lower one; its motion relative to the hips is the ground speed
    a,b=(l0,l1) if l0.z<r0.z else (r0,r1)
    d=(b-h1)-(a-h0); d.z=0
    speeds.append(d.length*fps)
speeds.sort()
mid=speeds[len(speeds)//4:len(speeds)*3//4]
v=sum(mid)/len(mid)
# hips height from the rest pose
hh=(mw@arm.data.bones[H.name].head_local).z
print("STRIDE", path.split("/")[-1], "frames", f1-f0, "fps", fps, "speed", round(v,3), "hips", round(hh,3), "norm", round(v/hh,3))
