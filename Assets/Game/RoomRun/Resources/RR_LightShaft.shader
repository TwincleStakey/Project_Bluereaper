// StageAccents (11-13 overnight dark-fantasy pass): moonlight shafts and light pools, no realtime lights.
//   Shaft: an open cone along the moon direction. Bright where the surface faces the camera, fading at its silhouette,
//          at both ends and where it meets geometry (soft depth), with slow drifting streaks. Additive.
//   Pool:  a box drawn back faces with ZTest Always (a projected decal): the ground inside it is brightened in proportion
//          to its own colour (Blend DstColor One), so a dark floor gets a moonlit patch, not a flat grey film.
Shader "RoomRun/LightShaft"
{
    Properties
    {
        [HDR] _Color("Colour", Color) = (0.5, 0.6, 0.8, 1)
        _Mode("0 shaft, 1 pool", Float) = 0
        _Edge("Edge softness", Range(0.5, 8)) = 2.5
        _Soft("Depth fade (m)", Float) = 1.5
        _Streaks("Streak count", Float) = 7
        _Seed("Seed", Float) = 0
    }
    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
    CBUFFER_START(UnityPerMaterial)
        float4 _Color; float _Mode, _Edge, _Soft, _Streaks, _Seed;
    CBUFFER_END
    ENDHLSL
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-20" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        // shaft
        Pass
        {
            Name "Shaft"
            Tags { "LightMode"="UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 nW : TEXCOORD1; float3 pW : TEXCOORD2; float4 sp : TEXCOORD3; };
            V vert(A a)
            {
                V o;
                o.pW = TransformObjectToWorld(a.positionOS.xyz);
                o.pos = TransformWorldToHClip(o.pW);
                o.nW = TransformObjectToWorldNormal(a.normalOS);
                o.uv = a.uv;
                o.sp = ComputeScreenPos(o.pos);
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                if (_Mode > 0.5) discard;
                float3 v = normalize(GetCameraPositionWS() - i.pW);
                float face = pow(saturate(abs(dot(normalize(i.nW), v))), _Edge);
                float y = i.uv.y;   // 0 at the ground end, 1 at the sky end
                // foot at 0.3x so the column never stands as a white wall on the floor (11-14 review), brightest a third of the way up
                float ends = smoothstep(0.0, 0.3, y) * pow(saturate(1 - y), 2.2) * lerp(0.3, 1.0, saturate(y * 3));
                float t = _Time.y * 0.06 + _Seed;
                float u = i.uv.x * 6.2831853;
                float streak = 0.72 + 0.17 * sin(u * _Streaks + t * 3 + sin(u * 3 + _Seed) * 1.5)
                                    + 0.11 * sin(u * (_Streaks * 2.3) - t * 5 + y * 3);   // half the grain (11-14 review)
                // soft where it meets geometry, and gone right in front of the camera
                float2 suv = i.sp.xy / i.sp.w;
                float scene = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                float me = LinearEyeDepth(i.pos.z, _ZBufferParams);
                float soft = saturate((scene - me) / _Soft);
                float near = saturate((me - 4) / 8);
                float k = face * ends * streak * soft * near;
                return half4(_Color.rgb * k, 1);
            }
            ENDHLSL
        }
        // pool (projected onto whatever is inside the box)
        Pass
        {
            Name "Pool"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend DstColor One
            ZWrite Off
            ZTest Always
            Cull Front
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            struct A { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos : SV_POSITION; float4 sp : TEXCOORD0; float3 pW : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };
            V vert(A a)
            {
                V o; UNITY_SETUP_INSTANCE_ID(a); UNITY_TRANSFER_INSTANCE_ID(a, o);
                o.pW = TransformObjectToWorld(a.positionOS.xyz);
                o.pos = TransformWorldToHClip(o.pW); o.sp = ComputeScreenPos(o.pos);
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                if (_Mode < 0.5) discard;
                // world position of the scene behind this pixel: along the view ray to the scene's eye depth (no inverse
                // view-projection, so it does not care how the target is flipped)
                float3 cam = GetCameraPositionWS();
                float3 ray = i.pW - cam;
                float3 fwd = -UNITY_MATRIX_V[2].xyz;
                float eye = LinearEyeDepth(SampleSceneDepth(i.sp.xy / i.sp.w), _ZBufferParams);
                float3 w = cam + ray * (eye / dot(ray, fwd));
                // fields whose ground/grass has no depth pass leave the depth texture empty there: fall back to the box's
                // ground plane (its centre height), whichever is nearer the camera
                float baseY = GetObjectToWorldMatrix()._m13;
                if (ray.y < -1e-4)
                {
                    float3 onPlane = cam + ray * ((baseY - cam.y) / ray.y);
                    if (dot(onPlane - cam, fwd) < dot(w - cam, fwd)) w = onPlane;
                }
                float3 o = mul(GetWorldToObjectMatrix(), float4(w, 1)).xyz;   // unit box: -0.5..0.5
                if (any(abs(o) > 0.5)) discard;
                float r = length(o.xz) * 2;
                float k = pow(saturate(1 - r), _Edge) * smoothstep(0.5, 0.2, abs(o.y));
                // a little break-up so the patch is not a perfect disc
                float n = 0.8 + 0.2 * sin(w.x * 1.7 + _Seed) * sin(w.z * 1.3 - _Seed * 0.7);
                return half4(_Color.rgb * k * n, 1);
            }
            ENDHLSL
        }
    }
}
