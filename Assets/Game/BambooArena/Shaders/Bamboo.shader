// Bamboo clumps (GPU instanced by ReedInstancer). Same wind and length-preserving bend as the reeds, stiffer at the
// base so only the upper culms and the leaf sprays sway. Culms are yellow-green with darker nodes; leaves are dark
// green and glow a little when backlit.
// See-through: inside a screen-space circle around the player, bamboo that is in front of the player dissolves with
// an ordered (Bayer) dither, so the bamboo stays opaque (no sorting issues) and the field stays cheap.
Shader "BambooArena/Bamboo"
{
    Properties
    {
        _CulmCol("Culm", Color) = (0.42, 0.50, 0.24, 1)
        _CulmTop("Culm (young, top)", Color) = (0.55, 0.62, 0.30, 1)
        _NodeCol("Node ring", Color) = (0.24, 0.26, 0.14, 1)
        _LeafCol("Leaf", Color) = (0.16, 0.26, 0.11, 1)
        _LeafTip("Leaf tip", Color) = (0.32, 0.42, 0.16, 1)
        _RootCol("Root shade", Color) = (0.05, 0.06, 0.04, 1)
        _Ambient("Ambient", Color) = (0.20, 0.24, 0.20, 1)
        _Translucency("Leaf translucency", Range(0, 3)) = 1.2
        _Bend("Bend amount", Range(0, 2)) = 0.6
        _Nodes("Nodes along the culm", Float) = 9
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Cull Off

        HLSLINCLUDE
        #include "Assets/Game/ReedField/Shaders/ReedCommon.hlsl"
        #include "Assets/Game/RoomRun/Shaders/RRSeeThrough.hlsl"   // 11-13: shared soft falloff
        CBUFFER_START(UnityPerMaterial)
            float4 _CulmCol, _CulmTop, _NodeCol, _LeafCol, _LeafTip, _RootCol, _Ambient;
            float _Translucency, _Bend, _Nodes;
        CBUFFER_END
        float4 _BA_SeeThrough;   // xy: player position on screen (0..1), z: radius (fraction of screen height), w: player view depth
        float _BA_SeeThroughOn;

        struct Attr
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv : TEXCOORD0;
            float2 data : TEXCOORD1;  // height fraction, part (0 leaf, 0.5 culm)
            float4 color : COLOR;     // rnd, stiffness
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        float3 BendBamboo(Attr a, out float3 nws, out float gust)
        {
            float3 root = TransformObjectToWorld(float3(0, 0, 0));
            float3 ws = TransformObjectToWorld(a.positionOS.xyz);
            nws = TransformObjectToWorldNormal(a.normalOS);
            float rnd = frac(a.color.r + RFHash(root.xz));
            float hf = a.data.x;
            float tr;
            float2 offs = RFBend(root.xz, rnd, a.color.g, gust, tr) * _Bend;
            float3 rel = ws - root;
            float ang = min(length(offs), 1.0);
            if (ang > 1e-4)
            {
                float2 d = offs / length(offs);
                float a2 = ang * hf * hf;            // stiff base, the top arcs
                float sn = sin(a2), cs = cos(a2);
                float along = dot(rel.xz, d);
                float2 sideXZ = rel.xz - d * along;
                float nAlong = along * cs + rel.y * sn;
                float nY = rel.y * cs - along * sn;
                rel = float3(sideXZ.x + d.x * nAlong, nY, sideXZ.y + d.y * nAlong);
            }
            // leaves flutter on their own, sideways only
            float leaf = 1 - step(0.25, a.data.y);
            rel.xz += float2(sin(_RF_Time * 2.3 + rnd * 40 + hf * 5), cos(_RF_Time * 1.9 + rnd * 23)) * 0.035 * leaf * _RF_Wind.z;
            return root + rel;
        }

        // 4x4 Bayer threshold for the see-through dither.
        float Bayer4(float2 p)
        {
            uint2 q = uint2(p) % 4;
            static const float m[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
            return (m[q.y * 4 + q.x] + 0.5) / 16.0;
        }

        void SeeThroughClip(float4 positionCS, float3 ws)
        {
            if (_BA_SeeThroughOn < 0.5) return;
            // screen position the same way as Camera.WorldToViewportPoint (bottom-up), also when rendering flipped into a texture
            float4 hc = TransformWorldToHClip(ws);
            float4 sp = ComputeScreenPos(hc);
            float2 uv = sp.xy / sp.w;
            float viewDepth = -TransformWorldToView(ws).z;
            float2 dlt = (uv - _BA_SeeThrough.xy) * float2(_ScreenParams.x / _ScreenParams.y, 1);
            float r = length(dlt);
            if (RRSeeThroughActive())
            {
                // 11-13 (Kido: soft like other top-down games): RoomRun's shared radial falloff (RRSeeThrough.hlsl)
                float c = RRSeeFadeOne(uv, viewDepth, float4(_BA_SeeThrough.xy, _BA_SeeThrough.w, 1), _ScreenParams.x / _ScreenParams.y)
                    * RRSeeHeight(ws.y) * (1 - _RR_SeeShape.z);
                if (c > 0.001) clip(RRSeeDither(positionCS.xy) - c);
                return;
            }
            float fade = 1 - smoothstep(_BA_SeeThrough.z * 0.55, _BA_SeeThrough.z, r);
            fade *= step(viewDepth, _BA_SeeThrough.w - 0.6);   // only bamboo in front of the player
            clip(Bayer4(positionCS.xy) - fade * 0.92 - 0.001);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            struct V2F { float4 positionCS : SV_POSITION; float3 ws : TEXCOORD0; float3 nws : TEXCOORD1; float4 d : TEXCOORD2; float viewDepth : TEXCOORD3; };

            V2F vert(Attr a)
            {
                UNITY_SETUP_INSTANCE_ID(a);
                V2F o; float g;
                o.ws = BendBamboo(a, o.nws, g);
                o.positionCS = TransformWorldToHClip(o.ws);
                o.viewDepth = -TransformWorldToView(o.ws).z;
                o.d = float4(a.data.x, a.data.y, a.uv.y, g);
                return o;
            }

            half4 frag(V2F i, bool front : SV_IsFrontFace) : SV_Target
            {
                SeeThroughClip(i.positionCS, i.ws);
                float hf = i.d.x, isCulm = step(0.25, i.d.y), along = i.d.z;
                float rnd = RFHash(floor(i.ws.xz * 3));
                float node = smoothstep(0.92, 1.0, frac(hf * _Nodes + rnd));
                float3 culm = lerp(_CulmCol.rgb, _CulmTop.rgb, hf) * (0.85 + 0.3 * rnd);
                culm = lerp(culm, _NodeCol.rgb, node * 0.8);
                float3 leaf = lerp(_LeafCol.rgb, _LeafTip.rgb, saturate(along)) * (0.8 + 0.4 * rnd);
                float3 albedo = lerp(leaf, culm, isCulm);
                albedo = lerp(_RootCol.rgb, albedo, smoothstep(0.0, 0.35, hf));

                float3 n = normalize(i.nws) * (front ? 1 : -1);
                float3 v = normalize(_WorldSpaceCameraPos - i.ws);
                Light ml = GetMainLight();
                float dl = dot(n, ml.direction);
                float ndl = saturate(dl) * 0.8 + saturate(-dl) * 0.4 + 0.15;
                float3 col = albedo * (_Ambient.rgb * (0.4 + 0.6 * hf) + ml.color * ndl);
                float back = pow(saturate(dot(-v, ml.direction) * 0.5 + 0.5), 2);
                col += leaf * (1 - isCulm) * ml.color * back * _Translucency * 0.6;
                col += albedo * i.d.w * 0.25 * smoothstep(0.5, 1, hf) * ml.color;   // gust sheen on the canopy

                InputData inputData = (InputData)0;
                inputData.positionWS = i.ws;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                uint count = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(count)
                    Light add = GetAdditionalLight(lightIndex, i.ws, half4(1, 1, 1, 1));
                    col += albedo * add.color * add.distanceAttenuation * saturate(dot(n, add.direction) * 0.5 + 0.5);
                LIGHT_LOOP_END
                return half4(RFFog(col, i.ws), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ColorMask R
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            struct V { float4 pos : SV_POSITION; float3 ws : TEXCOORD0; };
            V vert(Attr a) { UNITY_SETUP_INSTANCE_ID(a); float3 n; float g; float3 ws = BendBamboo(a, n, g); V o; o.pos = TransformWorldToHClip(ws); o.ws = ws; return o; }
            half frag(V i) : SV_Target { SeeThroughClip(i.pos, i.ws); return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            struct V { float4 pos : SV_POSITION; float3 n : TEXCOORD0; float3 ws : TEXCOORD1; };
            V vert(Attr a) { UNITY_SETUP_INSTANCE_ID(a); float3 n; float g; float3 ws = BendBamboo(a, n, g); V o; o.pos = TransformWorldToHClip(ws); o.n = n; o.ws = ws; return o; }
            half4 frag(V i, bool front : SV_IsFrontFace) : SV_Target { SeeThroughClip(i.pos, i.ws); return half4(normalize(i.n) * (front ? 1 : -1), 0); }
            ENDHLSL
        }
    }
}
