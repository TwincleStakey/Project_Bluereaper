# Hero pine (Sketchfab "Japanese Black Pine" by matt z chan, CC BY) -> game version for the snow field.
# blender -b --factory-startup -P hero_pine_prep.py -- <src.fbx> <out.fbx>
# - needle cards take their normals from the pad hull (Default_1, a 166-tri volume around the crown), so the snow
#   rule (world-up x noise) whitens the tops of the pads and leaves the undersides dark, instead of following each card
# - vertex colours for the SnowPlague shaders: R 0, G AO (dark at the trunk base / under the crown), B random per part,
#   A sway (0 trunk .. 1 outer needles)
import bpy, sys, random
from mathutils import Vector
args = sys.argv[sys.argv.index("--") + 1:]
SRC, OUT = args[0], args[1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SRC)
obs = [o for o in bpy.data.objects if o.type == 'MESH']
mat_of = lambda o: o.material_slots[0].material.name if o.material_slots and o.material_slots[0].material else ""
hull = next(o for o in obs if mat_of(o).startswith("Default"))
needles = [o for o in obs if mat_of(o).startswith("wire")]
zmin = min((o.matrix_world @ v.co).z for o in obs for v in o.data.vertices)
zmax = max((o.matrix_world @ v.co).z for o in obs for v in o.data.vertices)
H = zmax - zmin

# hull normals -> needle cards
for o in needles:
    bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
    m = o.modifiers.new("dt", 'DATA_TRANSFER')
    m.object = hull; m.use_loop_data = True; m.data_types_loops = {'CUSTOM_NORMAL'}; m.loop_mapping = 'POLYINTERP_NEAREST'
    m.mix_mode = 'REPLACE'; m.mix_factor = 0.85
    bpy.ops.object.modifier_apply(modifier="dt")

rng = random.Random(5)
for o in obs:
    me = o.data
    for a in [a.name for a in me.color_attributes]: me.color_attributes.remove(me.color_attributes[a])
    ca = me.color_attributes.new("Col", 'BYTE_COLOR', 'CORNER')
    is_needle = o in needles; is_hull = o is hull
    rb = rng.random()
    for poly in me.polygons:
        for li, vi in zip(poly.loop_indices, poly.vertices):
            p = o.matrix_world @ me.vertices[vi].co
            t = (p.z - zmin) / max(H, 1e-4)
            if is_needle or is_hull:
                ao = 0.55 + 0.45 * min(1.0, max(0.0, (t - 0.15) / 0.6))
                sway = 0.6 * min(1.0, t * 1.2)
            else:   # bark: dark grey-brown base flare, lighter up the trunk
                ao = 0.3 + 0.7 * min(1.0, max(0.0, t / 0.35)) ** 1.5
                sway = 0.15 * t * t
            ca.data[li].color = (0.0, ao, rb, sway)
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.fbx(filepath=OUT, use_selection=True, object_types={"MESH"}, apply_scale_options="FBX_SCALE_ALL",
                         bake_space_transform=True, axis_forward="-Z", axis_up="Y", mesh_smooth_type="FACE",
                         colors_type="LINEAR", add_leaf_bones=False, path_mode="STRIP")
print("DONE", OUT, "height", round(H, 2), "needle objects", len(needles))
