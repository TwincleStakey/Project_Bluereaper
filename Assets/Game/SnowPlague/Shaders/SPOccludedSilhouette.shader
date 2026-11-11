// Silhouette of a player (or any actor) hidden behind scenery: draws only where the actor is BEHIND what is already in
// the depth buffer (ZTest Greater), flat colour + fresnel, no depth write. Meant for cmd.DrawRenderer(renderer, mat, 0, 0)
// on the player's renderers with colour + depth attached (depth read-only), after opaques. SnowPlague/Foreground and
// the other opaque shaders write depth and their see-through dither holes do not, so the real player shows in the hole
// and the silhouette shows everywhere else the player is covered.
Shader "SnowPlague/OccludedSilhouette"
{
    Properties
    {
        [HDR] _Color("Silhouette colour", Color) = (0.95, 0.62, 0.25, 1)
        _Base("Flat fill", Range(0, 1)) = 0.25
        _Fresnel("Fresnel power", Range(0.5, 6)) = 2.5
        _Alpha("Opacity", Range(0, 1)) = 0.85
        _Push("Push toward camera (m), avoids self / ground slivers", Range(0, 0.3)) = 0.06
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "Silhouette"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Greater
            Cull Back
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Base, _Fresnel, _Alpha, _Push;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float3 ws : TEXCOORD0; float3 n : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };
            V vert(A a)
            {
                V o = (V)0; UNITY_SETUP_INSTANCE_ID(a); UNITY_TRANSFER_INSTANCE_ID(a, o);
                float3 ws = TransformObjectToWorld(a.positionOS.xyz);
                ws += GetWorldSpaceNormalizeViewDir(ws) * _Push;
                o.ws = ws; o.positionCS = TransformWorldToHClip(ws); o.n = TransformObjectToWorldNormal(a.normalOS);
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float f = pow(1 - saturate(dot(normalize(i.n), GetWorldSpaceNormalizeViewDir(i.ws))), _Fresnel);
                return half4(_Color.rgb * (_Base + f * (1 - _Base)), saturate(_Alpha * _Color.a));
            }
            ENDHLSL
        }
    }
}
