import bpy, sys, os
a = sys.argv[sys.argv.index("--")+1:]
src, dst, target, name = a[0], a[1], int(a[2]), a[3]
texdir = os.path.join(os.path.dirname(dst), "Textures"); os.makedirs(texdir, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=src)
meshes=[o for o in bpy.data.objects if o.type=='MESH']
def tris(o):
    o.data.calc_loop_triangles(); return len(o.data.loop_triangles)
tot=sum(tris(o) for o in meshes)
ratio=min(1.0, target/tot)
for o in meshes:
    if tris(o) < 400: continue
    bpy.context.view_layer.objects.active=o
    m=o.modifiers.new("dec",'DECIMATE'); m.ratio=ratio; m.use_collapse_triangulate=True
    while o.modifiers[0].name!="dec": bpy.ops.object.modifier_move_up(modifier="dec")
    bpy.ops.object.modifier_apply(modifier="dec")
print("RESULT", name, tot, "->", sum(tris(o) for o in meshes))
for mat in bpy.data.materials:
    if not mat.use_nodes: continue
    for n in mat.node_tree.nodes:
        if n.type=='TEX_IMAGE' and n.image:
            sock=[l.to_socket.name+"@"+l.to_node.type for l in n.outputs[0].links]
            im=n.image; w,h=im.size
            if w>1024: im.scale(1024,1024)
            role = "albedo" if any("Base Color" in s for s in sock) else ("normal" if any("NORMAL_MAP" in s or "Normal" in s for s in sock) else "other")
            out=os.path.join(texdir, f"{name}_{mat.name.replace(':','_')}_{role}_{im.name.replace(':','_')}.png")
            im.filepath_raw=out; im.file_format='PNG'; im.save()
            print("TEX", mat.name, im.name, sock, "->", os.path.basename(out))
for o in meshes:
    for i,ms in enumerate(o.material_slots): print("SLOT", o.name, i, ms.material.name if ms.material else None)
bpy.ops.export_scene.fbx(filepath=dst, use_selection=False, object_types={'ARMATURE','MESH'}, add_leaf_bones=False,
    bake_anim=False, path_mode='STRIP', embed_textures=False, mesh_smooth_type='FACE', armature_nodetype='NULL')
