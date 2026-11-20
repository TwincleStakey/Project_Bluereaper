// 11-19 ShadowPass contact shadow (Kido: "발밑 접지 그림자"): a soft dark ellipse lying on the ground under every actor.
// A small screen-space decal: one quad per actor (all in one instanced call, one material) is drawn a little above the
// floor, and each pixel it covers reads the scene depth to find the surface actually drawn there. Only surfaces within
// a hand's height of the actor's floor point get darkened, so it follows the bumpy terrain mesh (the floor collider is
// flat, the visual ground in 민가 rises up to 0.27 m above it) and never lands on walls, props, other bodies or the
// upper body of the actor itself. Multiply blend: it can only darken (no paler floor, no hue change); plain radial
// falloff, no texture, no noise.
// Instance matrix: translation = the floor point under the actor, X / Z axes = the ellipse's full width / length, the
// Y axis = the floor normal with its length = the centre darkness (0..1).
Shader "RoomRun/ContactShadow"
{
    Properties
    {
        _Tint("Shadow colour multiplied in", Color) = (0, 0, 0, 1)
        _Soft("Edge softness power", Range(0.5, 4)) = 1.3
        _Reach("Max height above the floor point m", Float) = 0.42
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-400" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "ContactShadow"
            Tags { "LightMode"="UniversalForward" }
            Blend DstColor Zero
            ZWrite Off
            ZTest Always
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _Tint; float _Soft, _Reach;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V
            {
                float4 pos : SV_POSITION;
                float3 centre : TEXCOORD0;
                float4 axX : TEXCOORD1;    // xyz axis, w = 1 / |axis|^2
                float4 axZ : TEXCOORD2;
                float4 up : TEXCOORD3;     // xyz unit floor normal, w = darkness
            };
            V vert(A a)
            {
                UNITY_SETUP_INSTANCE_ID(a);
                V o;
                float4x4 m = UNITY_MATRIX_M;
                float3 c = float3(m._m03, m._m13, m._m23);
                float3 ax = float3(m._m00, m._m10, m._m20), az = float3(m._m02, m._m12, m._m22), ay = float3(m._m01, m._m11, m._m21);
                float dark = length(ay);
                float3 n = ay / max(dark, 1e-4);
                // the card floats 0.6 m over the floor and is drawn 2x wide, so it covers the ellipse on any bump, seen
                // from the tilted top-down camera
                float3 ws = c + ax * (a.positionOS.x * 2.0) + az * (a.positionOS.z * 2.0) + n * 0.6;
                o.pos = TransformWorldToHClip(ws);
                o.centre = c;
                o.axX = float4(ax, 1.0 / max(dot(ax, ax), 1e-4));
                o.axZ = float4(az, 1.0 / max(dot(az, az), 1e-4));
                o.up = float4(n, saturate(dark));
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                float2 uv = i.pos.xy / _ScaledScreenParams.xy;
                #if UNITY_REVERSED_Z
                    float depth = SampleSceneDepth(uv);
                #else
                    float depth = lerp(UNITY_NEAR_CLIP_VALUE, 1, SampleSceneDepth(uv));
                #endif
                float3 wp = ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);
                float3 d = wp - i.centre;
                float h = dot(d, i.up.xyz);
                // the surface must be the floor here: not far below it, not up on a body / wall / prop
                float keep = saturate((_Reach - h) / 0.15) * saturate((h + 0.6) / 0.3);
                float2 p = float2(dot(d, i.axX.xyz) * i.axX.w, dot(d, i.axZ.xyz) * i.axZ.w) * 2.0;   // -1..1 across the ellipse
                float r = length(p);
                // flat core to ~35 % of the radius (the part outside the feet is what reads from above), then a long soft rim
                float k = 1.0 - smoothstep(0.35, 1.0, r);
                k = pow(k, _Soft);
                float a = i.up.w * k * keep;
                clip(a - 0.002);
                return half4(lerp(float3(1, 1, 1), _Tint.rgb, a), 1);
            }
            ENDHLSL
        }
    }
}
