// 장산범 fog ring (11-20 Kido: "안개 퀄이 너무 별로"). The particle fog read as cheap: the same puff sprite repeated, hard
// straight cuts where a sprite met the floor, no depth. This is the bank seen in the reference fight instead: a stack of
// horizontal fog sheets (one draw per sheet, 0.4 m to ~10 m), each sampling two scrolling noise layers at its own height,
// so seen from above the fog has billows, holes and depth and the boss sinks into it. The density rises with the distance
// past a wobbling inner edge (higher sheets start further out, so the bank slopes up like a wall), it melts softly into the
// floor and into anything standing in it (scene depth), and the tops are lit pale while the low fog stays blue and dark.
Shader "BulletHeaven/JangsanbeomFogRing"
{
    Properties
    {
        _NoiseTex ("Noise (tiling)", 2D) = "grey" {}
        _LowCol ("Low fog colour", Color) = (0.20, 0.25, 0.34, 1)
        _TopCol ("Lit top colour", Color) = (0.55, 0.62, 0.74, 1)
        _Density ("Density", Range(0, 3)) = 1.2
        _Scale ("Noise scale (m per tile, layer 1 / 2)", Vector) = (26, 9, 0, 0)
        _Speed ("Swirl (m/s)", Float) = 1.1
        _Contrast ("Noise contrast", Range(0.5, 4)) = 1.5
        _Ramp ("Inner edge ramp (m)", Float) = 9
        _Wobble ("Inner edge wobble (m)", Float) = 5
        _SoftDepth ("Soft intersection (m)", Float) = 2.5
        _Centre ("Clear patch centre (xyz) and radius (w)", Vector) = (0, 0, 0, 28)
        _Layer ("Sheet 0..1 (set per sheet)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-40" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "FogRing"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            TEXTURE2D(_NoiseTex); SAMPLER(sampler_NoiseTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _NoiseTex_ST, _LowCol, _TopCol, _Scale, _Centre;
                float _Density, _Speed, _Contrast, _Ramp, _Wobble, _SoftDepth, _Layer;
            CBUFFER_END
            float _JSB_UseFixedTime, _JSB_FixedTime;
            float T() { return _JSB_UseFixedTime > 0.5 ? _JSB_FixedTime : _Time.y; }
            struct A { float4 positionOS : POSITION; };
            struct V { float4 positionCS : SV_POSITION; float3 ws : TEXCOORD0; float4 sp : TEXCOORD1; };
            V vert(A a)
            {
                V o; o.ws = TransformObjectToWorld(a.positionOS.xyz); o.positionCS = TransformWorldToHClip(o.ws);
                o.sp = ComputeScreenPos(o.positionCS); return o;
            }
            float N(float2 p) { return SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, p).r; }
            half4 frag(V i) : SV_Target
            {
                float t = T();
                float2 d = i.ws.xz - _Centre.xz; float r = length(d); float ang = atan2(d.y, d.x);
                // the bank turns slowly round the clearing and every sheet drifts a little differently (parallax = depth)
                float2 tang = float2(-d.y, d.x) / max(r, 1e-3);
                float2 p = i.ws.xz + tang * t * _Speed * (0.7 + 0.6 * _Layer);
                float n1 = N(p / _Scale.x + float2(_Layer * 0.37, _Layer * 0.21));
                float n2 = N(p / _Scale.y - tang * t * 0.06 + float2(_Layer * 0.53, -_Layer * 0.17));
                float n = saturate(pow(saturate(n1 * 0.65 + n2 * 0.55), _Contrast) * 1.5);
                // inner edge: wobbles round the circle and breathes; higher sheets start further out
                float wob = (N(float2(ang * 0.8, t * 0.03)) - 0.5) * 2.0 * _Wobble;
                float inner = _Centre.w + wob + _Layer * _Ramp * 1.2;
                float dens = smoothstep(inner, inner + _Ramp, r) * (0.35 + 0.65 * n);
                // soft into the floor and into whatever stands in the fog
                float2 suv = i.sp.xy / i.sp.w;
                float scene = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                float self = -TransformWorldToView(i.ws).z;
                float soft = saturate((scene - self) / _SoftDepth);
                float a = saturate(dens * _Density * soft * (0.55 + 0.45 * (1.0 - _Layer)));
                half3 col = lerp(_LowCol.rgb, _TopCol.rgb, saturate(_Layer * 0.8 + n * 0.45));
                return half4(col, a * 0.5);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
