import bpy, sys
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=sys.argv[-1])
for o in bpy.data.objects:
    print("OBJ", o.name, o.type, tuple(round(v,3) for v in o.dimensions), o.location[:], o.rotation_euler[:])
    if o.type=='MESH':
        me=o.data; print("  verts",len(me.vertices),"tris",sum(len(p.vertices)-2 for p in me.polygons))
        import mathutils
        bb=[o.matrix_world@mathutils.Vector(c) for c in o.bound_box]
        print("  bb min",[round(min(v[i] for v in bb),3) for i in range(3)],"max",[round(max(v[i] for v in bb),3) for i in range(3)])
        for m in me.materials: print("  mat",m.name)
for i in bpy.data.images: print("IMG",i.name,i.size[:])
