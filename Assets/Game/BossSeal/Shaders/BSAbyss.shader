// BossSeal abyss (11-15, Kido: "외곽 아래로 절벽 같은 느낌"): the far-below plane round the mesa. Unlit, dim red haze welling up
// from the deep, slowly drifting, darker toward the camera side so the cliff tops read against it.
Shader "BossSeal/Abyss"
{
    Properties
    {
        _Deep("Deep colour", Color) = (0.02, 0.006, 0.004, 1)
        _Glow("Lava glow (HDR)", Color) = (0.9, 0.2, 0.04, 1)
        _North("Glow: north z start, z full, south dark", Vector) = (-20, 60, 0.45, 0)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry+10" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            ZWrite On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float4 _Deep, _Glow, _North;
            CBUFFER_END
            struct A { float4 p : POSITION; };
            struct V { float4 cs : SV_POSITION; float3 ws : TEXCOORD0; };
            V vert(A a) { V o; o.ws = TransformObjectToWorld(a.p.xyz); o.cs = TransformWorldToHClip(o.ws); return o; }
            float H21(float2 p) { float3 p3 = frac(float3(p.xyx) * 0.1031); p3 += dot(p3, p3.yzx + 33.33); return frac((p3.x + p3.y) * p3.z); }
            float VN(float2 p) { float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(H21(i), H21(i + float2(1, 0)), f.x), lerp(H21(i + float2(0, 1)), H21(i + float2(1, 1)), f.x), f.y); }
            half4 frag(V i) : SV_Target
            {
                float2 w = i.ws.xz; float t = _Time.y * 0.04;
                float n = VN(w * 0.035 + float2(t, -t * 0.7)) * 0.6 + VN(w * 0.09 - float2(t * 1.3, t)) * 0.4;
                float north = smoothstep(_North.x, _North.y, w.y);
                float n2 = VN(w * 0.21 - float2(t * 0.8, t * 0.3));
                float vein = smoothstep(0.62, 0.95, n * 0.7 + n2 * 0.5) * smoothstep(0.3, 0.7, VN(w * 0.03 + 7.0));   // soft molten pools glowing through, no drawn lines
                float g = saturate(vein + saturate(n - 0.5) * 0.45) * lerp(_North.z, 1, north);
                return half4(lerp(_Deep.rgb, _Glow.rgb, g), 1);
            }
            ENDHLSL
        }
    }
}
