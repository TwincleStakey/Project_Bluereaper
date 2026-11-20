# Shape/size preview of FBX models already in the project (solid workbench, game-camera angle from the south).
# blender -b --factory-startup -P preview_fbx.py -- <out dir> <fbx> [<fbx> ...]
import bpy, sys, os, math, mathutils
args = sys.argv[sys.argv.index("--") + 1:]
OUT, FILES = args[0], args[1:]
os.makedirs(OUT, exist_ok=True)
for f in FILES:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=f)
    obs = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    lo = mathutils.Vector((1e9,) * 3); hi = -lo
    for o in obs:
        for b in o.bound_box:
            w = o.matrix_world @ mathutils.Vector(b)
            lo = mathutils.Vector(map(min, lo, w)); hi = mathutils.Vector(map(max, hi, w))
    c = (lo + hi) / 2; d = hi - lo; r = max(d) * 1.5
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"; sc.display.shading.light = "STUDIO"; sc.display.shading.color_type = "MATERIAL"
    sc.render.resolution_x = sc.render.resolution_y = 400
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); sc.collection.objects.link(cam); sc.camera = cam
    p = math.radians(35)
    cam.location = (c.x, c.y - r * math.cos(p), c.z + r * math.sin(p)); cam.rotation_euler = (math.radians(90) - p, 0, 0)
    name = os.path.splitext(os.path.basename(f))[0]
    sc.render.filepath = os.path.join(OUT, name + ".png"); bpy.ops.render.render(write_still=True)
    tris = sum(sum(len(pl.vertices) - 2 for pl in o.data.polygons) for o in obs)
    print(f"DIM {name}: {d.x:.2f} x {d.y:.2f} x {d.z:.2f} (x, depth, up) tris {tris} objs {len(obs)}")
