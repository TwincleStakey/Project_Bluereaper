# DeskMan.glb -> rigged blend. Usage: blender -b -P rig.py -- in.glb out.blend
import bpy, sys, bmesh
from mathutils import Vector
argv=sys.argv[sys.argv.index('--')+1:]
S=1.9
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=argv[0])
me_o=[o for o in bpy.data.objects if o.type=='MESH'][0]
for o in list(bpy.data.objects):
    if o.type=='EMPTY':
        me_o.parent=None
        bpy.data.objects.remove(o)
me_o.matrix_world=me_o.matrix_world.copy()
bpy.context.view_layer.objects.active=me_o; me_o.select_set(True)
me_o.scale=(S,S,S); bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
me_o.name='Scholar_Body'; me_o.data.name='Scholar_Body'
bm=bmesh.new(); bm.from_mesh(me_o.data)
n0=len(bm.verts); bmesh.ops.remove_doubles(bm,verts=bm.verts,dist=0.0003); print('MERGE',n0,'->',len(bm.verts))
# Kido 11-20: remove the pale dangling piece by the left ear (21-vert island, 1m-scale box)
bm.verts.ensure_lookup_table(); seen=set(); kill=[]
for v in bm.verts:
    if v.index in seen: continue
    st=[v]; seen.add(v.index); isl=[]
    while st:
        x=st.pop(); isl.append(x)
        for e in x.link_edges:
            y=e.other_vert(x)
            if y.index not in seen: seen.add(y.index); st.append(y)
    c=sum((p.co for p in isl), Vector())/len(isl)/S
    if 0.04<c.x<0.07 and -0.02<c.y<0.0 and 0.81<c.z<0.875 and len(isl)<60: kill+=isl
print('EARPIECE_DEL',len(kill))
bmesh.ops.delete(bm,geom=kill,context='VERTS')
bm.to_mesh(me_o.data); bm.free()
# armature (coords at 1m scale, +X = character's Left, front = -Y)
B={
 'Hips':((0,0,0.50),(0,0,0.58),None),
 'Spine':((0,0,0.58),(0,0,0.65),'Hips'),
 'Chest':((0,0,0.65),(0,0.005,0.755),'Spine'),
 'Neck':((0,0.005,0.775),(0,0.0,0.815),'Chest'),
 'Head':((0,0.0,0.815),(0,0.0,0.93),'Neck'),
 'Shoulder.L':((0.02,0.005,0.74),(0.09,0.01,0.727),'Chest'),
 'UpperArm.L':((0.09,0.01,0.727),(0.195,0.012,0.726),'Shoulder.L'),
 'LowerArm.L':((0.195,0.012,0.726),(0.295,0.01,0.728),'UpperArm.L'),
 'Hand.L':((0.295,0.01,0.728),(0.345,0.01,0.726),'LowerArm.L'),
 'Fingers1.L':((0.345,0.01,0.726),(0.372,0.01,0.725),'Hand.L'),
 'Fingers2.L':((0.372,0.01,0.725),(0.40,0.01,0.724),'Fingers1.L'),
 'Thumb1.L':((0.30,-0.008,0.722),(0.325,-0.024,0.722),'Hand.L'),
 'Thumb2.L':((0.325,-0.024,0.722),(0.345,-0.032,0.722),'Thumb1.L'),
 'UpperLeg.L':((0.048,0,0.50),(0.040,-0.008,0.27),'Hips'),
 'LowerLeg.L':((0.040,-0.008,0.27),(0.037,0.008,0.065),'UpperLeg.L'),
 'Foot.L':((0.037,0.008,0.065),(0.038,-0.07,0.012),'LowerLeg.L'),
}
for k in list(B):
    if k.endswith('.L'):
        h,t,p=B[k]; B[k[:-2]+'.R']=((-h[0],h[1],h[2]),(-t[0],t[1],t[2]),p.replace('.L','.R') if p and p.endswith('.L') else p)
ad=bpy.data.armatures.new('ScholarRig'); ar=bpy.data.objects.new('ScholarRig',ad)
bpy.context.collection.objects.link(ar)
bpy.ops.object.select_all(action='DESELECT'); bpy.context.view_layer.objects.active=ar; ar.select_set(True)
bpy.ops.object.mode_set(mode='EDIT')
for k,(h,t,p) in B.items():
    e=ad.edit_bones.new(k); e.head=Vector(h)*S; e.tail=Vector(t)*S
for k,(h,t,p) in B.items():
    if p:
        e=ad.edit_bones[k]; e.parent=ad.edit_bones[p]
        e.use_connect=(Vector(B[p][1])-Vector(h)).length<1e-4
# legs/arms rolls: z axis forward/up consistent
for e in ad.edit_bones:
    if 'Arm' in e.name or 'Hand' in e.name or 'Finger' in e.name or 'Thumb' in e.name:
        e.align_roll(Vector((0,0,1)))
    else:
        e.align_roll(Vector((0,-1,0)))
bpy.ops.object.mode_set(mode='OBJECT')
bpy.ops.object.select_all(action='DESELECT')
me_o.select_set(True); ar.select_set(True); bpy.context.view_layer.objects.active=ar
bpy.ops.object.parent_set(type='ARMATURE_AUTO')
# weight fixes: head/hat rigid above neck
vg={g.name:g for g in me_o.vertex_groups}
fix=0
for v in me_o.data.vertices:
    z=v.co.z/S
    if z>0.835:
        for g in v.groups: me_o.vertex_groups[g.group].remove([v.index])
        vg['Head'].add([v.index],1.0,'REPLACE'); fix+=1
unw=sum(1 for v in me_o.data.vertices if not any(g.weight>0.01 for g in v.groups))
print('HEADFIX',fix,'UNWEIGHTED',unw)
bpy.ops.wm.save_as_mainfile(filepath=argv[1])
print('RIG_OK')
