# Writes the SnowPlague lit .shader wrappers (Ground, Prop, Vegetation): each is the same five passes (forward,
# shadow caster, depth, depth normals, meta) around a surface include. python -I gen_shaders.py <Shaders dir>
import sys, os
OUT = sys.argv[1]
P = "Assets/Game/SnowPlague/Shaders/"

SHADERS = {
    "SPGround": dict(name="SnowPlague/Ground", surf="SPGroundSurface.hlsl", defines=[], cull="Back", shadow=False, props='''
        _SnowA("Snow albedo", 2D) = "white" {}
        _SnowN("Snow normal (GL)", 2D) = "grey" {}
        _SnowH("Snow height", 2D) = "grey" {}
        _GravelA("Gravel albedo", 2D) = "grey" {}
        _GravelN("Gravel normal (GL)", 2D) = "grey" {}
        _GravelH("Gravel height", 2D) = "grey" {}
        _MudA("Wet soil albedo", 2D) = "grey" {}
        _MudN("Wet soil normal (GL)", 2D) = "grey" {}
        _MudH("Wet soil height", 2D) = "grey" {}
        _LeafA("Leaf litter albedo", 2D) = "grey" {}
        _LeafN("Leaf litter normal (GL)", 2D) = "grey" {}
        _LeafH("Leaf litter height", 2D) = "grey" {}
        _Tiles("Metres per tile (snow, gravel, soil, leaves)", Vector) = (4, 5, 6, 4)
        _SoilTint("Soil tint", Color) = (0.55, 0.62, 0.72, 1)
        _GravelTint("Gravel tint", Color) = (0.62, 0.66, 0.72, 1)
        _LeafTint("Leaf tint", Color) = (0.55, 0.5, 0.48, 1)
        _RedLeaf("Red maple leaf", Color) = (0.42, 0.05, 0.04, 1)
        _SnowTint("Snow tint (sRGB 200-235)", Color) = (0.8, 0.84, 0.88, 1)
        _BlendDepth("Height blend depth", Range(0.02, 0.6)) = 0.15
        _SnowFill("Snow fills hollows", Range(0, 3)) = 1.4
        _SnowBias("Snow bias", Range(-1, 1)) = 0
        _NormalStrength("Normal strength", Range(0, 2)) = 0.8
        _Calm("Calm (less scan contrast)", Range(0, 1)) = 0.35
        _RedLeaves("Red maple leaves", Range(0, 1)) = 0.35
        _Wet("Wet mud", Range(0, 1)) = 0.8
        _Footprints("Footprints in the snow", Range(0, 1)) = 0.8'''),
    "SPProp": dict(name="SnowPlague/Prop", surf="SPPropSurface.hlsl", defines=["SP_SEETHROUGH_KW"], cull="[_Cull]", shadow=True, props='''
        _BaseMap("Albedo", 2D) = "white" {}
        [Normal] _BumpMap("Normal (GL)", 2D) = "bump" {}
        _Tint("Tint", Color) = (1, 1, 1, 1)
        _TintVar("Per-instance tint (rgb) and brightness spread (a)", Color) = (1, 1, 1, 0.1)
        _NormalStrength("Normal strength", Range(0, 2)) = 1
        _Smoothness("Smoothness", Range(0, 1)) = 0.15
        _Wetness("Wet", Range(0, 1)) = 0
        _Desaturate("Desaturate", Range(0, 1)) = 0.4
        [Toggle] _VertexFrost("Kit vertex colours (frost R, AO G)", Float) = 1
        _AOStrength("Vertex AO strength", Range(0, 1)) = 0.8
        _SnowBias("Snow bias", Range(-1, 1)) = 0
        _GroundMelt("Ground melt height (m)", Range(0, 1)) = 0.25
        [HDR] _EmissionColor("Emission", Color) = (0, 0, 0, 1)
        _Flicker("Flicker", Range(0, 1)) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2
        [Toggle(SP_SEETHROUGH)] _SeeThrough("See-through near players", Float) = 0'''),
    "SPVegetation": dict(name="SnowPlague/Vegetation", surf="SPVegSurface.hlsl", defines=["SP_DEFORM", "SP_SEETHROUGH_KW", "SP_ALPHA_KW"], cull="[_Cull]", shadow=True, props='''
        _BaseMap("Albedo (atlas or bark)", 2D) = "white" {}
        [Normal] _BumpMap("Normal (GL)", 2D) = "bump" {}
        [Toggle] _UseTex("Use albedo", Float) = 1
        [Toggle] _UseNormal("Use normal", Float) = 0
        _NormalStrength("Normal strength", Range(0, 2)) = 1
        _Tint("Tint", Color) = (1, 1, 1, 1)
        _Wither("Wither (0 = own colour)", Range(0, 1)) = 0
        _WitherA("Wither: olive", Color) = (0.36, 0.38, 0.22, 1)
        _WitherB("Wither: ochre", Color) = (0.73, 0.64, 0.37, 1)
        _WitherC("Wither: brown", Color) = (0.36, 0.28, 0.2, 1)
        _WitherD("Wither: grey", Color) = (0.42, 0.44, 0.46, 1)
        _TipCol("Tip colour (a = amount)", Color) = (0.75, 0.78, 0.8, 0)
        _Bend("Wind bend", Range(0, 2)) = 0.4
        _Trample("Trample", Range(0, 1)) = 0
        _SnowBias("Snow bias", Range(-1, 1)) = 0
        _Wrap("Wrap lighting", Range(0, 1)) = 0.3
        _Smoothness("Smoothness", Range(0, 1)) = 0.1
        _AOStrength("Vertex AO strength", Range(0, 1)) = 0.8
        _GroundMelt("Ground melt height (m)", Range(0, 1)) = 0.15
        _Cutoff("Alpha cutoff", Range(0, 1)) = 0.5
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 0
        [Toggle(_ALPHATEST_ON)] _AlphaClip("Alpha clip (cards)", Float) = 0
        [Toggle(SP_SEETHROUGH)] _SeeThrough("See-through near players", Float) = 0'''),
}

PASS = '''
        Pass
        {{
            Name "{name}"
            Tags {{ "LightMode"="{mode}" }}
            {state}
            HLSLPROGRAM
            #include_with_pragmas "{P}SPPassPragmas.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
{extra}
{defines}
            #define {passdef}
            #include "{P}SPLit.hlsl"
            #include "{P}{surf}"
            ENDHLSL
        }}'''

for fn, s in SHADERS.items():
    defs = []
    kws = []
    for d in s["defines"]:
        if d == "SP_SEETHROUGH_KW": kws.append("            #pragma shader_feature_local SP_SEETHROUGH")
        elif d == "SP_ALPHA_KW": kws.append("            #pragma shader_feature_local _ALPHATEST_ON")
        else: defs.append(f"            #define {d}")
    D = "\n".join(defs + kws)
    passes = []
    fwd_extra = ('            #include_with_pragmas "' + P + 'SPForwardPragmas.hlsl"\n'
                 '            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"\n'
                 '            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"')
    passes.append(PASS.format(name="ForwardLit", mode="UniversalForward", state=f"Cull {s['cull']}", P=P, extra=fwd_extra, defines=D, passdef="SP_PASS_FORWARD", surf=s["surf"]))
    if s["shadow"]:
        passes.append(PASS.format(name="ShadowCaster", mode="ShadowCaster", state=f"ZWrite On ZTest LEqual ColorMask 0 Cull {s['cull']}", P=P,
                                  extra="            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW",
                                  defines=D, passdef="SP_PASS_SHADOW", surf=s["surf"]))
    passes.append(PASS.format(name="DepthOnly", mode="DepthOnly", state=f"ZWrite On ColorMask R Cull {s['cull']}", P=P, extra="", defines=D, passdef="SP_PASS_DEPTH", surf=s["surf"]))
    passes.append(PASS.format(name="DepthNormals", mode="DepthNormals", state=f"ZWrite On Cull {s['cull']}", P=P, extra="", defines=D, passdef="SP_PASS_DEPTHNORMALS", surf=s["surf"]))
    passes.append(PASS.format(name="Meta", mode="Meta", state="Cull Off", P=P, extra="            #pragma shader_feature EDITOR_VISUALIZATION", defines=D, passdef="SP_PASS_META", surf=s["surf"]))
    txt = f'''// Generated by BlenderSource~/gen_shaders.py: edit the surface include ({s["surf"]}) or the generator, not this file.
Shader "{s["name"]}"
{{
    Properties
    {{{s["props"]}
    }}
    SubShader
    {{
        Tags {{ "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="{"AlphaTest" if "SP_ALPHA_KW" in s["defines"] else "Geometry"}" }}
{"".join(passes)}
    }}
}}
'''
    open(os.path.join(OUT, fn + ".shader"), "w", encoding="utf8").write(txt)
    print("wrote", fn)
