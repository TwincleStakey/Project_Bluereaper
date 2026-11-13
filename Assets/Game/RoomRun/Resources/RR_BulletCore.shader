// Bullet readability (Kido 11-13: on dark fields the player's missiles were darkened with the scene). LookFX draws this
// AFTER post-processing (its own render pass), so StageMood's exposure, saturation, contrast, vignette and grain never
// touch it: every shot keeps the same colour and brightness on every stage.
// One procedural sprite per bullet, facing the camera, stretched along the shot's path for player shots:
//   a white-hot core melting into a saturated coloured glow (no rim ring, 11-13), with a faint edgeless shade behind it
//   so the shot still reads on bright ground (snow) where light added to white would vanish.
// Premultiplied alpha: rgb is added, alpha darkens what is behind.
Shader "RoomRun/BulletCore"
{
    Properties
    {
        _Color("Rim colour", Color) = (1, 0.2, 0.5, 1)
        _CoreColor("Core colour", Color) = (1, 1, 1, 1)
        _Core("Core radius (0..1 of the sprite)", Range(0.05, 0.9)) = 0.3
        _Rim("Rim radius", Range(0.1, 1)) = 0.62
        _Glow("Outer glow", Range(0, 2)) = 0.6
        _Dark("Dark backing", Range(0, 1)) = 0.55
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Overlay" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "BulletCore"
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _Color, _CoreColor; float _Core, _Rim, _Glow, _Dark;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            V vert(A a)
            {
                UNITY_SETUP_INSTANCE_ID(a);
                V o; o.pos = TransformObjectToHClip(a.positionOS.xyz); o.uv = a.uv;
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                // 11-13 (Kido: "외곽 동그란 아웃라인 별로, 빛나게만"): no hard rim ring; a white-hot centre that melts into
                // a coloured glow, and only a faint soft shade behind it (no visible edge) so it still reads on snow.
                float r = length(i.uv * 2 - 1);
                float core = exp(-pow(r / _Core, 2) * 2.5);
                float glow = pow(saturate(1 - r), 2.0) * (1 + _Glow) + exp(-pow(r / _Rim, 2) * 2.0);
                float3 rgb = _Color.rgb * saturate(glow) + _CoreColor.rgb * core;
                float a = pow(saturate(1 - r), 2.5) * _Dark;
                return half4(rgb, a);
            }
            ENDHLSL
        }
    }
}
