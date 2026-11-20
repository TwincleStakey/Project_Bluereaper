import bpy, sys, os
args=sys.argv[sys.argv.index('--')+1:]
d=args[0]
bpy.ops.wm.open_mainfile(filepath=os.path.join(d,'windkey_low.blend'))
o=bpy.data.objects['WindKey']; m=o.data.materials[0]; nt=m.node_tree
for n in list(nt.nodes):
    if n.type not in ('OUTPUT_MATERIAL','BSDF_PRINCIPLED'): nt.nodes.remove(n)
b=nt.nodes['Principled BSDF']
ta=nt.nodes.new('ShaderNodeTexImage'); ta.image=bpy.data.images.load(os.path.join(d,'WK_Albedo.png'))
tn=nt.nodes.new('ShaderNodeTexImage'); tn.image=bpy.data.images.load(os.path.join(d,'WK_Normal.png')); tn.image.colorspace_settings.name='Non-Color'
nm=nt.nodes.new('ShaderNodeNormalMap')
nt.links.new(ta.outputs[0],b.inputs['Base Color']); nt.links.new(tn.outputs[0],nm.inputs['Color']); nt.links.new(nm.outputs[0],b.inputs['Normal'])
b.inputs['Metallic'].default_value=0.5; b.inputs['Roughness'].default_value=0.6
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(d,'_preview.blend'))
