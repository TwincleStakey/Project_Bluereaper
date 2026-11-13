// Blood spray (r14, Kido "타래 레퍼런스처럼 피 보이게"; TARAE ss5): the kill spray - a directional fan of scanned droplets
// (Weasel Whistle Blood Splatter, alpha only) projected down onto the ground like SPBloodDecal (unit cube, depth -> world,
// ground-only by the neighbour-depth test), MULTIPLIED into the snow so its texture shows through.
// Colour targets were measured on the ss5 crops (snow ~[204,203,198]): flecks ~[125,66,55] (mul ~0.61/0.33/0.28),
// dense core ~[58,25,21] (mul ~0.28/0.12/0.11); a blurred-mip red haze joins the flecks the way ss5's spray does.
Shader "SnowPlague/BloodSpray"
{
    Properties
    {
        _SprayTex("Spray (A = droplets)", 2D) = "white" {}
        _FleckMul("Fleck multiply (thin)", Color) = (0.62, 0.30, 0.25, 1)
        _CoreMul("Core multiply (dense)", Color) = (0.28, 0.10, 0.085, 1)
        _HazeMul("Haze multiply", Color) = (0.80, 0.52, 0.48, 1)
        _Haze("Haze strength", Range(0, 1)) = 0.6
        _Strength("Strength", Range(0, 1)) = 1
        _MaxRise("Max rise above the neighbouring ground (m)", Range(0.02, 0.5)) = 0.08
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-101" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "SprayMultiply"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend DstColor Zero
            ZWrite Off
            ZTest GEqual
            Cull Front
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Assets/Game/SnowPlague/Shaders/SPCommon.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            TEXTURE2D(_SprayTex); SAMPLER(sampler_SprayTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _SprayTex_ST, _FleckMul, _CoreMul, _HazeMul;
                float _Haze, _Strength, _MaxRise;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float4 sp : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            V vert(A a)
            {
                V o; UNITY_SETUP_INSTANCE_ID(a); UNITY_TRANSFER_INSTANCE_ID(a, o);
                o.positionCS = TransformObjectToHClip(a.positionOS.xyz); o.sp = ComputeScreenPos(o.positionCS); return o;
            }
            float3 WS(float2 uv)
            {
                float raw = SampleSceneDepth(uv);
                #if !UNITY_REVERSED_Z
                raw = lerp(UNITY_NEAR_CLIP_VALUE, 1, raw);
                #endif
                return ComputeWorldSpacePosition(uv, raw, UNITY_MATRIX_I_VP);
            }
            half4 frag(V i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float2 suv = i.sp.xy / i.sp.w;
                float3 ws = WS(suv);
                float3 os = TransformWorldToObject(ws);
                clip(0.5 - abs(os));
                float3 n = normalize(cross(ddy(ws), ddx(ws)));
                if (n.y < 0) n = -n;
                // ground only (same test as SPBloodDecal): not raised above the depth 6 / 16 px to each side
                float minY = ws.y;
                [unroll] for (int k = 0; k < 8; k++)
                {
                    float2 dir = k % 4 == 0 ? float2(1, 0) : k % 4 == 1 ? float2(-1, 0) : k % 4 == 2 ? float2(0, 1) : float2(0, -1);
                    minY = min(minY, WS(suv + dir * (k < 4 ? 6 : 16) / _ScreenParams.xy).y);
                }
                float rise = ws.y - TransformObjectToWorld(float3(0, 0, 0)).y;
                float fade = (1 - smoothstep(_MaxRise * 0.4, _MaxRise, ws.y - minY)) * (1 - smoothstep(0.4, 0.5, rise)) * smoothstep(0.7, 0.88, n.y);
                clip(fade - 0.01);

                float2 uv = os.xz + 0.5;
                float edge = smoothstep(0.0, 0.04, min(min(uv.x, uv.y), min(1 - uv.x, 1 - uv.y)));
                float a = SAMPLE_TEXTURE2D(_SprayTex, sampler_SprayTex, uv).a;
                // local density from blurred mips: the fan's root reads as a dense dark core, the far droplets as bright flecks
                float dens = SAMPLE_TEXTURE2D_LOD(_SprayTex, sampler_SprayTex, uv, 5).a;
                float haze = SAMPLE_TEXTURE2D_LOD(_SprayTex, sampler_SprayTex, uv, 6.5).a;
                float rnd = SPFbmI(ws.xz * 3.1);   // world noise: the haze is patchy, never the mip's smooth blob
                float3 fleck = lerp(_FleckMul.rgb, _CoreMul.rgb, smoothstep(0.08, 0.45, dens));
                float3 mul = lerp(1, _HazeMul.rgb, saturate(haze * 3.0 * _Haze * lerp(0.4, 1.3, rnd)));
                mul = lerp(mul, fleck, smoothstep(0.2, 0.65, a));
                return half4(lerp(1, mul, fade * edge * _Strength), 1);
            }
            ENDHLSL
        }
    }
}
