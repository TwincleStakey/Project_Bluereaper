// 심연 고원 (11-19): the glowing depth far below the plateau, and the haze sheets in between.
//   _Mode 0 = the floor of the abyss (opaque): hot orange-red light welling up, brightest in a band round the
//             plateau's foot and fading to near-black far out, slow drifting gradient noise (no drawn shapes)
//   _Mode 1 = a haze sheet (transparent): crimson veil at some depth, soft where it cuts through rock (depth fade)
Shader "KillKnight/AbyssGlow"
{
    Properties
    {
        _Hot("Hot colour (HDR)", Color) = (2.2, 0.6, 0.3, 1)
        _Deep("Far colour", Color) = (0.5, 0.08, 0.11, 1)
        _Ring("Bright band: inner r, peak r, outer r, noise", Vector) = (40, 120, 380, 0.55)
        _Alpha("Sheet alpha", Range(0, 1)) = 0.35
        _Fade("Sheet depth fade m", Float) = 6
        [Enum(Floor,0,Sheet,1,RimGlow,2)] _Mode("Mode", Float) = 0
        _RimY("Rim glow: peak y, fade to top y, bottom y, 0", Vector) = (-10, -0.5, -55, 0)
        [Enum(UnityEngine.Rendering.BlendMode)] _Src("Src", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _Dst("Dst", Float) = 0
        [Enum(Off,0,On,1)] _ZW("ZWrite", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry+20" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Blend [_Src] [_Dst]
            ZWrite [_ZW]
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float4 _Hot, _Deep, _Ring, _RimY; float _Alpha, _Fade, _Mode, _Src, _Dst, _ZW;
            CBUFFER_END
            struct A { float4 p : POSITION; };
            struct V { float4 cs : SV_POSITION; float3 ws : TEXCOORD0; float4 sp : TEXCOORD1; };
            V vert(A a) { V o; o.ws = TransformObjectToWorld(a.p.xyz); o.cs = TransformWorldToHClip(o.ws); o.sp = ComputeScreenPos(o.cs); return o; }
            float2 G2(float2 p) { p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3))); return -1 + 2 * frac(sin(p) * 43758.5453); }
            float GN(float2 p)
            {
                float2 i = floor(p), f = frac(p); float2 u = f * f * (3 - 2 * f);
                return 0.5 + 0.5 * lerp(lerp(dot(G2(i), f), dot(G2(i + float2(1, 0)), f - float2(1, 0)), u.x),
                                        lerp(dot(G2(i + float2(0, 1)), f - float2(0, 1)), dot(G2(i + float2(1, 1)), f - float2(1, 1)), u.x), u.y);
            }
            float FBM(float2 p) { return GN(p) * 0.5 + GN(p * 2.1 + 5.2) * 0.3 + GN(p * 4.3 + 1.7) * 0.2; }
            half4 frag(V i) : SV_Target
            {
                float2 w = i.ws.xz; float t = _Time.y;
                float r = length(w);
                float n = FBM(w / 40.0 + float2(t * 0.012, -t * 0.009));
                float n2 = FBM(w / 13.0 - float2(t * 0.02, t * 0.015));
                float band = smoothstep(_Ring.x, _Ring.y, r) * (1 - smoothstep(_Ring.y, _Ring.z, r));
                float g = band * lerp(1, n * 1.4 * (0.6 + 0.6 * n2), _Ring.w);
                if (_Mode < 0.5)
                    return half4(lerp(_Deep.rgb, _Hot.rgb, saturate(g)), 1);
                if (_Mode > 1.5)
                {
                    // v5 rim glow (additive): a soft wall of light welling up just under the plateau edge
                    float y = i.ws.y;
                    float up = smoothstep(_RimY.y, _RimY.x, y);
                    float dn = smoothstep(_RimY.z, _RimY.x, y);
                    float2 suv2 = i.sp.xy / i.sp.w;
                    float soft2 = saturate((LinearEyeDepth(SampleSceneDepth(suv2), _ZBufferParams) - LinearEyeDepth(i.cs.z, _ZBufferParams)) / max(0.01, _Fade));
                    float flick = 0.75 + 0.25 * FBM(float2(i.ws.x + i.ws.z, y) / 9.0 + float2(t * 0.05, -t * 0.08));
                    return half4(_Hot.rgb * up * dn * flick * soft2 * _Alpha, 0);
                }
                // haze sheet: veil thickness by noise, soft where it meets rock
                float2 suv = i.sp.xy / i.sp.w;
                float sceneZ = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                float myZ = LinearEyeDepth(i.cs.z, _ZBufferParams);
                float soft = saturate((sceneZ - myZ) / max(0.01, _Fade));
                float a = _Alpha * soft * saturate(0.35 + n * 1.1) * (0.4 + 0.6 * band + 0.25);
                return half4(lerp(_Deep.rgb, _Hot.rgb, saturate(g * 0.8)), saturate(a));
            }
            ENDHLSL
        }
    }
}
