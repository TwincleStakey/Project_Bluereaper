import bpy, sys, bmesh
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=sys.argv[-1])
o=[o for o in bpy.data.objects if o.type=='MESH'][0]
bm=bmesh.new(); bm.from_mesh(o.data); M=o.matrix_world
# loose parts
seen=set(); parts=[]
for v in bm.verts:
    if v.index in seen: continue
    st=[v]; comp=[]; seen.add(v.index)
    while st:
        x=st.pop(); comp.append(x)
        for e in x.link_edges:
            y=e.other_vert(x)
            if y.index not in seen: seen.add(y.index); st.append(y)
    ps=[M@c.co for c in comp]
    parts.append((len(comp),[round(min(p[i] for p in ps),3) for i in range(3)],[round(max(p[i] for p in ps),3) for i in range(3)]))
parts.sort(key=lambda p:-p[0])
print("PARTS",len(parts))
for p in parts[:40]: print("P",p)
import collections
h=collections.Counter(int((M@v.co).z*20) for v in bm.verts); print("ZHIST",sorted(h.items()))
