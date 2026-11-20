// Snow plague field: one lit skeleton for every opaque SnowPlague shader (ground, props, vegetation).
// A shader defines, before including this file once per pass:
//   SP_PASS_FORWARD | SP_PASS_SHADOW | SP_PASS_DEPTH | SP_PASS_DEPTHNORMALS | SP_PASS_META
//   void SPSurface(SPInput i, inout SPOut o)          material logic (also used by meta, and for alpha clip in depth passes)
//   optional: #define SP_DEFORM + float3 SPDeform(float3 ws, float3 root, SPAttr a)   vertex wind / trample in world space
//   optional: #define SP_SEETHROUGH                   dither hole around the players
//   optional: keyword LS_PROC                         procedural instancing (LookStudyInstancer / RenderMeshPrimitives):
//             matrix from _LSProcMats[SV_InstanceID + _LSProcBase], tint from _LSProcTints (-> SPInput.tint)
// Lighting goes through URP's UniversalFragmentPBR, so lightmaps, shadowmask, APV, light cookies, Forward+ and
// light layers all work like URP Lit. Fog is the field fog (ReedCommon RFFog) plus the full-screen height fog pass.
#ifndef SP_LIT_INCLUDED
#define SP_LIT_INCLUDED

// 11-12 perf: LS_PROC_IDX = LS_PROC, but the instance id goes through an index buffer of the visible instances
// (LookStudyInstancer.packVisible: one draw per mesh + material instead of one per 8 m chunk)
#if defined(LS_PROC_IDX) && !defined(LS_PROC)
#define LS_PROC
#define SP_PROC_IDX
#endif

#include "Assets/Game/SnowPlague/Shaders/SPCommon.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#if defined(SP_PASS_META)
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/MetaInput.hlsl"
#endif
#if defined(LOD_FADE_CROSSFADE)
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
#endif

#if defined(LS_PROC)
StructuredBuffer<float4x4> _LSProcMats;
StructuredBuffer<float4> _LSProcTints;
uint _LSProcBase;
#if defined(SP_PROC_IDX)
StructuredBuffer<uint> _LSProcIdx;
#endif
#endif

struct SPAttr
{
    float4 positionOS : POSITION;
    float3 normalOS   : NORMAL;
    float4 tangentOS  : TANGENT;
    float4 color      : COLOR;
    float2 uv         : TEXCOORD0;
    float2 lmUV       : TEXCOORD1;
    float2 dynUV      : TEXCOORD2;
#if defined(LS_PROC) && !defined(UNITY_INSTANCING_ENABLED)
    uint procId       : SV_InstanceID;
#endif
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct SPVary
{
    float4 positionCS : SV_POSITION;
    float2 uv         : TEXCOORD0;
    float3 ws         : TEXCOORD1;
    float3 nws        : TEXCOORD2;
    float4 tws        : TEXCOORD3;
    float4 color      : TEXCOORD4;
    float4 extra      : TEXCOORD5;   // x: instance random, y: height above the object origin, zw: free
#if defined(SP_PASS_FORWARD)
    DECLARE_LIGHTMAP_OR_SH(staticLightmapUV, vertexSH, 6);
    #ifdef DYNAMICLIGHTMAP_ON
    float2 dynamicLightmapUV : TEXCOORD7;
    #endif
    #ifdef USE_APV_PROBE_OCCLUSION
    float4 probeOcclusion : TEXCOORD8;
    #endif
#endif
#if defined(LS_PROC)
    float4 tint       : TEXCOORD9;
#endif
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

struct SPInput
{
    float3 ws, nws;
    float4 tws;
    float2 uv;
    float4 color;
    float4 extra;
    float4 tint;      // procedural instancing tint (LS_PROC), else 1
    float4 positionCS;
    float3 viewWS;
    bool front;
};

struct SPOut
{
    float3 albedo;
    float3 normalWS;
    float alpha;
    float smoothness;
    float occlusion;
    float3 emission;
    float rim;        // rim light mask (0..1)
    float wrap;       // wrap lighting amount (thin vegetation)
    float specular;   // 0..1 spec scale
    float sparkle;    // snow glints (lit by the moon and the lanterns)
};

void SPSurface(SPInput i, inout SPOut o);
#if defined(SP_DEFORM)
float3 SPDeform(float3 ws, float3 root, SPAttr a);
#endif

#if defined(SP_PASS_SHADOW)
float3 _LightDirection;
float3 _LightPosition;
#endif

SPVary SPVert(SPAttr a)
{
    SPVary o = (SPVary)0;
    UNITY_SETUP_INSTANCE_ID(a);
    UNITY_TRANSFER_INSTANCE_ID(a, o);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
#if defined(LS_PROC)
    // Procedural instancing: unity_ObjectToWorld is identity, the matrix comes from the buffer (uniform scale assumed).
    #if defined(UNITY_INSTANCING_ENABLED)
    uint pid = a.instanceID + _LSProcBase;
    #else
    uint pid = a.procId + _LSProcBase;
    #endif
    #if defined(SP_PROC_IDX)
    pid = _LSProcIdx[pid];
    #endif
    float4x4 pm = _LSProcMats[pid];
    float3 ws = mul(pm, float4(a.positionOS.xyz, 1)).xyz;
    float3 root = float3(pm._m03, pm._m13, pm._m23);
    float3 nWS = normalize(mul((float3x3)pm, a.normalOS));
    float3 tWS = normalize(mul((float3x3)pm, a.tangentOS.xyz));
    float tw = a.tangentOS.w;
    o.tint = _LSProcTints[pid];
#else
    float3 ws = TransformObjectToWorld(a.positionOS.xyz);
    float3 root = TransformObjectToWorld(float3(0, 0, 0));
    VertexNormalInputs ni = GetVertexNormalInputs(a.normalOS, a.tangentOS);
    float3 nWS = ni.normalWS;
    float3 tWS = ni.tangentWS;
    float tw = a.tangentOS.w * GetOddNegativeScale();
#endif
#if defined(SP_DEFORM)
    ws = SPDeform(ws, root, a);
#endif
    o.ws = ws; o.nws = nWS;
    o.tws = float4(tWS, tw);
    o.uv = a.uv; o.color = a.color;
    o.extra = float4(SPInstanceRandAt(root), ws.y - root.y, 0, 0);
#if defined(SP_PASS_SHADOW)
    #if _CASTING_PUNCTUAL_LIGHT_SHADOW
    float3 ld = normalize(_LightPosition - ws);
    #else
    float3 ld = _LightDirection;
    #endif
    float4 cs = TransformWorldToHClip(ApplyShadowBias(ws, nWS, ld));
    #if UNITY_REVERSED_Z
    cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
    #else
    cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
    #endif
    o.positionCS = cs;
#elif defined(SP_PASS_META)
    o.positionCS = UnityMetaVertexPosition(a.positionOS.xyz, a.lmUV, a.dynUV);
#else
    o.positionCS = TransformWorldToHClip(ws);
#endif
#if defined(SP_PASS_FORWARD)
    OUTPUT_LIGHTMAP_UV(a.lmUV, unity_LightmapST, o.staticLightmapUV);
    #ifdef DYNAMICLIGHTMAP_ON
    o.dynamicLightmapUV = a.dynUV * unity_DynamicLightmapST.xy + unity_DynamicLightmapST.zw;
    #endif
    OUTPUT_SH4(ws, o.nws, GetWorldSpaceNormalizeViewDir(ws), o.vertexSH, o.probeOcclusion);
#endif
    return o;
}

SPInput SPMakeInput(SPVary v, bool front)
{
    SPInput i;
    i.ws = v.ws; i.nws = normalize(v.nws) * (front ? 1 : -1); i.tws = v.tws; i.uv = v.uv; i.color = v.color; i.extra = v.extra;
#if defined(LS_PROC)
    i.tint = v.tint;
#else
    i.tint = float4(1, 1, 1, 1);
#endif
    i.positionCS = v.positionCS; i.viewWS = GetWorldSpaceNormalizeViewDir(v.ws); i.front = front;
    return i;
}

SPOut SPDefaultOut(SPInput i)
{
    SPOut o;
    o.albedo = 0.5; o.normalWS = i.nws; o.alpha = 1; o.smoothness = 0.1; o.occlusion = 1; o.emission = 0;
    o.rim = 1; o.wrap = 0; o.specular = 1; o.sparkle = 0;
    return o;
}

// Tangent-space normal (xy from a GL normal map, already * 2 - 1) to world, using the mesh tangent frame.
float3 SPNormalTS(SPInput i, float2 nxy, float strength)
{
    float3 t = normalize(i.tws.xyz);
    float3 b = cross(i.nws, t) * (i.tws.w >= 0 ? 1 : -1);
    nxy *= strength;
    return normalize(i.nws * sqrt(saturate(1 - dot(nxy, nxy))) + t * nxy.x + b * nxy.y);
}

#if defined(SP_PASS_FORWARD)
void SPFrag(SPVary v, bool front : SV_IsFrontFace
    , out half4 outColor : SV_Target0
#ifdef _WRITE_RENDERING_LAYERS
    , out uint outRenderingLayers : SV_Target1
#endif
)
{
    UNITY_SETUP_INSTANCE_ID(v);
    SPInput i = SPMakeInput(v, front);
#if defined(SP_SEETHROUGH)
    SPSeeThroughClip(v.positionCS, v.ws);
#endif
#if defined(LOD_FADE_CROSSFADE)
    LODFadeCrossFade(v.positionCS);
#endif
    SPOut o = SPDefaultOut(i);
    SPSurface(i, o);
#if defined(_ALPHATEST_ON)
    clip(o.alpha - 0.5);
#endif
    InputData d = (InputData)0;
    d.positionWS = v.ws;
    d.positionCS = v.positionCS;
    d.normalWS = normalize(o.normalWS);
    d.viewDirectionWS = i.viewWS;
#if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
    d.shadowCoord = TransformWorldToShadowCoord(v.ws);
    #if defined(SP_SHADOW_JITTER)
    // v14 (Kido "눈위엥가면 너무 깨지는데 밑에"): on the dark glossy ice the shadow-map texels of the player / branches read as
    // hard stair-steps; each pixel looks the shadow up a few cm off in a random direction (interleaved gradient noise),
    // so the edge becomes a soft dithered falloff (the film grain hides the dither)
    {
        float n1 = frac(52.9829189 * frac(dot(v.positionCS.xy, float2(0.06711056, 0.00583715))));
        float n2 = frac(52.9829189 * frac(dot(v.positionCS.yx + 17.0, float2(0.06711056, 0.00583715))));
        float ang = n1 * 6.2831853, rad = SP_SHADOW_JITTER * sqrt(n2);
        d.shadowCoord = TransformWorldToShadowCoord(v.ws + float3(cos(ang), 0, sin(ang)) * rad);
    }
    #endif
#endif
    // Forward+ (_CLUSTER_LIGHT_LOOP) picks its light cluster from this
    d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(v.positionCS);
    d.shadowMask = half4(1, 1, 1, 1);
#if defined(DYNAMICLIGHTMAP_ON)
    d.bakedGI = SAMPLE_GI(v.staticLightmapUV, v.dynamicLightmapUV, v.vertexSH, d.normalWS);
    d.shadowMask = SAMPLE_SHADOWMASK(v.staticLightmapUV);
#elif !defined(LIGHTMAP_ON) && (defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2))
    d.bakedGI = SAMPLE_GI(v.vertexSH, GetAbsolutePositionWS(d.positionWS), d.normalWS, d.viewDirectionWS, v.positionCS.xy, v.probeOcclusion, d.shadowMask);
#else
    d.bakedGI = SAMPLE_GI(v.staticLightmapUV, v.vertexSH, d.normalWS);
    d.shadowMask = SAMPLE_SHADOWMASK(v.staticLightmapUV);
#endif

    SurfaceData s = (SurfaceData)0;
    s.albedo = o.albedo; s.alpha = 1; s.smoothness = o.smoothness; s.occlusion = o.occlusion; s.emission = o.emission;
    s.metallic = 0; s.specular = 0; s.normalTS = float3(0, 0, 1);
    half4 c = UniversalFragmentPBR(d, s);
    if (o.wrap > 0)
    {
        // wrap lighting for thin vegetation: light leaks round to the unlit side
        Light ml = GetMainLight(d.shadowCoord, d.positionWS, d.shadowMask);
        float ndl = dot(d.normalWS, ml.direction);
        c.rgb += o.albedo * ml.color * ml.shadowAttenuation * (saturate(ndl * 0.5 + 0.5) - saturate(ndl)) * o.wrap;
    }
    if (o.sparkle > 0)
    {
        Light sl = GetMainLight(d.shadowCoord, d.positionWS, d.shadowMask);
        c.rgb += o.sparkle * (sl.color * sl.shadowAttenuation + _SP_RimWarm.rgb * SPLanternWarm(v.ws) * 3.0);
    }
    float3 rim = SPRim(d.normalWS, i.viewWS, v.ws, o.rim);
    c.rgb += o.albedo * rim + rim * 0.15;
    c.rgb = RFFog(c.rgb, v.ws);
    outColor = half4(c.rgb, 1);
#ifdef _WRITE_RENDERING_LAYERS
    outRenderingLayers = EncodeMeshRenderingLayer();
#endif
}
#endif

#if defined(SP_PASS_SHADOW) || defined(SP_PASS_DEPTH)
half4 SPFrag(SPVary v, bool front : SV_IsFrontFace) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(v);
#if defined(SP_SEETHROUGH) && defined(SP_PASS_DEPTH)
    SPSeeThroughClip(v.positionCS, v.ws);
#endif
#if defined(_ALPHATEST_ON)
    SPInput i = SPMakeInput(v, front);
    SPOut o = SPDefaultOut(i);
    SPSurface(i, o);
    clip(o.alpha - 0.5);
#endif
    return 0;
}
#endif

#if defined(SP_PASS_DEPTHNORMALS)
void SPFrag(SPVary v, bool front : SV_IsFrontFace
    , out half4 outNormal : SV_Target0
#ifdef _WRITE_RENDERING_LAYERS
    , out uint outRenderingLayers : SV_Target1
#endif
)
{
    UNITY_SETUP_INSTANCE_ID(v);
#if defined(SP_SEETHROUGH)
    SPSeeThroughClip(v.positionCS, v.ws);
#endif
    SPInput i = SPMakeInput(v, front);
#if defined(_ALPHATEST_ON)
    SPOut o = SPDefaultOut(i);
    SPSurface(i, o);
    clip(o.alpha - 0.5);
#endif
    outNormal = half4(NormalizeNormalPerPixel(i.nws), 0);
#ifdef _WRITE_RENDERING_LAYERS
    outRenderingLayers = EncodeMeshRenderingLayer();
#endif
}
#endif

#if defined(SP_PASS_META)
half4 SPFrag(SPVary v) : SV_Target
{
    SPInput i = SPMakeInput(v, true);
    SPOut o = SPDefaultOut(i);
    SPSurface(i, o);
    MetaInput m = (MetaInput)0;
    m.Albedo = o.albedo;
    m.Emission = o.emission;
    return UnityMetaFragment(m);
}
#endif
#endif
