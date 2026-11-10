// Blood decal (the only saturated colour on the field): a unit cube projected straight down onto whatever is inside it
// (ground, snow, straw mats) by reconstructing world position from the depth texture. Cheap screen-space decal, no
// DBuffer pass. SP_Blood.png is a 2x2 atlas (pool, splatter, drag trail, drips); _Cell < 0 picks one from the object's
// world-position hash (no MaterialPropertyBlock). Wet: darker thick core, glossy moon glint, fades on steep surfaces.
Shader "SnowPlague/BloodDecal"
{
    Properties
    {
        _BloodTex("Blood atlas (A coverage, R thickness)", 2D) = "white" {}
        _Col("Blood", Color) = (0.42, 0.04, 0.035, 1)
        _Core("Thick core", Color) = (0.13, 0.012, 0.01, 1)
        _Cell("Atlas cell (0-3, -1 = random)", Float) = -1
        _Opacity("Opacity", Range(0, 1)) = 0.95
        _Gloss("Gloss", Range(0, 1)) = 0.7
        _Steep("Fade on steep faces", Range(0, 1)) = 0.6
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-100" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "BloodDecal"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest GEqual
            Cull Front
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Assets/Game/SnowPlague/Shaders/SPCommon.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            TEXTURE2D(_BloodTex); SAMPLER(sampler_BloodTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _BloodTex_ST, _Col, _Core;
                float _Cell, _Opacity, _Gloss, _Steep;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float4 sp : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            V vert(A a)
            {
                V o; UNITY_SETUP_INSTANCE_ID(a); UNITY_TRANSFER_INSTANCE_ID(a, o);
                o.positionCS = TransformObjectToHClip(a.positionOS.xyz); o.sp = ComputeScreenPos(o.positionCS); return o;
            }
            half4 frag(V i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float2 suv = i.sp.xy / i.sp.w;
                float raw = SampleSceneDepth(suv);
                #if !UNITY_REVERSED_Z
                raw = lerp(UNITY_NEAR_CLIP_VALUE, 1, raw);
                #endif
                float3 ws = ComputeWorldSpacePosition(suv, raw, UNITY_MATRIX_I_VP);
                float3 os = TransformWorldToObject(ws);
                clip(0.5 - abs(os));
                float3 n = normalize(cross(ddy(ws), ddx(ws)));
                if (n.y < 0) n = -n;
                float cell = _Cell >= 0 ? _Cell : floor(SPInstanceRand() * 3.999);
                float2 uv = (os.xz + 0.5) * 0.5 + float2(fmod(cell, 2), 1 - floor(cell / 2)) * 0.5;
                float4 b = SAMPLE_TEXTURE2D(_BloodTex, sampler_BloodTex, uv);
                float thick = b.r;
                float a = b.a * _Opacity * lerp(1, smoothstep(0.3, 0.8, n.y), _Steep) * saturate((0.5 - abs(os.y)) * 6);
                float3 col = lerp(_Col.rgb, _Core.rgb, smoothstep(0.2, 0.8, thick));
                Light ml = GetMainLight();
                float3 v = GetWorldSpaceNormalizeViewDir(ws);
                float3 lit = SampleSH(n) + ml.color * saturate(dot(n, ml.direction));
                float spec = pow(saturate(dot(n, normalize(ml.direction + v))), 80) * _Gloss * thick * 2;
                float3 c = col * lit + ml.color * spec;
                c = RFFog(c, ws);
                return half4(c, a);
            }
            ENDHLSL
        }
    }
}
