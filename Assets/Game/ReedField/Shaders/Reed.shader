// 억새/갈대 clumps drawn with GPU instancing by ReedInstancer.
// Bends the whole clump around its root with the wind (gust waves roll across the field), lies down where the player
// tramples (LookStudy trail map) and parts where unseen things move. Plumes are silver-gold and glow a little against
// the moon or the red sky; roots are dark so the field reads as a mass from the high top-down camera.
Shader "ReedField/Reed"
{
    Properties
    {
        _RootCol("Root", Color) = (0.035, 0.04, 0.035, 1)
        _LeafCol("Leaf", Color) = (0.20, 0.22, 0.17, 1)
        _LeafTip("Leaf tip (dry)", Color) = (0.42, 0.38, 0.28, 1)
        _StemCol("Stem", Color) = (0.38, 0.34, 0.26, 1)
        _PlumeCol("Plume", Color) = (0.62, 0.58, 0.50, 1)
        _PlumeTip("Plume tip", Color) = (0.86, 0.82, 0.74, 1)
        _ReedPlumeCol("Reed (갈대) plume", Color) = (0.36, 0.28, 0.24, 1)
        _Variation("Colour variation", Range(0,1)) = 0.35
        _Translucency("Plume translucency", Range(0,4)) = 2.2
        _Wrap("Wrap lighting", Range(0,1)) = 0.5
        _Ambient("Ambient", Color) = (0.09, 0.10, 0.14, 1)
        _GustSheen("Gust sheen", Range(0,2)) = 0.6
        _IsReed("Is 갈대 (0 = 억새)", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Cull Off

        HLSLINCLUDE
        #include "ReedCommon.hlsl"
        #include "Assets/Game/RoomRun/Shaders/RRSeeThrough.hlsl"   // 11-13: tall reeds in front of the player thin out softly
        CBUFFER_START(UnityPerMaterial)
            float4 _RootCol, _LeafCol, _LeafTip, _StemCol, _PlumeCol, _PlumeTip, _ReedPlumeCol, _Ambient;
            float _Variation, _Translucency, _Wrap, _GustSheen, _IsReed;
        CBUFFER_END

        struct Attr
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv : TEXCOORD0;   // across, along
            float2 data : TEXCOORD1; // height fraction, part (0 leaf, 0.5 stem, 1 plume)
            float4 color : COLOR;    // rnd, stiffness, plume tip fraction
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Bent { float3 ws; float3 nws; float gust; float trample; float rnd; };

        Bent BendVertex(Attr a)
        {
            Bent o;
            float3 root = TransformObjectToWorld(float3(0, 0, 0));
            float3 ws = TransformObjectToWorld(a.positionOS.xyz);
            float3 nws = TransformObjectToWorldNormal(a.normalOS);
            float H = length(float3(UNITY_MATRIX_M._m01, UNITY_MATRIX_M._m11, UNITY_MATRIX_M._m21)); // clump height in metres
            float rnd = frac(a.color.r + RFHash(root.xz));
            float hf = a.data.x;
            float2 offs = RFBend(root.xz, rnd, a.color.g, o.gust, o.trample);
            // Same shape as the LookStudy grass: displacement grows with height squared, so the base stays planted
            // and the tip arcs; the stalk shortens a little as it leans so it does not stretch.
            // Length-preserving bend: each vertex rotates about the root in the (lean, up) plane by an angle that grows
            // along the stalk, so the base stays planted, the tip arcs and nothing stretches or bobs.
            float3 rel = ws - root;
            float ang = min(length(offs), 1.35);
            if (ang > 1e-4)
            {
                float2 d = offs / length(offs);
                float a2 = ang * hf;
                float sn = sin(a2), cs = cos(a2);
                float along = dot(rel.xz, d);
                float2 sideXZ = rel.xz - d * along;
                float nAlong = along * cs + rel.y * sn;
                float nY = rel.y * cs - along * sn;
                rel = float3(sideXZ.x + d.x * nAlong, nY, sideXZ.y + d.y * nAlong);
            }
            ws = root + rel;
            // Plumes trail behind the stalk: a slower, delayed sway and a slight droop along each strand.
            float plume = step(0.75, a.data.y);
            float lag = sin(_RF_Time * 1.3 + rnd * 6.28 + dot(root.xz, float2(0.6, 0.4)) - 0.9);
            // Sideways only: no vertical motion from wind.
            ws.xz += _RF_Wind.xy * lag * 0.025 * H * a.uv.y * plume * _RF_Wind.z;
            ws = RRCutApply(ws, root, 0.1);   // greatsword cuts
            o.ws = ws;
            o.nws = nws;
            o.rnd = rnd;
            return o;
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

            struct V2F
            {
                float4 positionCS : SV_POSITION;
                float3 ws : TEXCOORD0;
                float3 nws : TEXCOORD1;
                float4 d : TEXCOORD2;  // hf, part, along, tip fraction
                float4 e : TEXCOORD3;  // rnd, gust, trample, across
            };

            V2F vert(Attr a)
            {
                UNITY_SETUP_INSTANCE_ID(a);
                V2F o;
                Bent b = BendVertex(a);
                o.ws = b.ws; o.nws = b.nws;
                o.positionCS = TransformWorldToHClip(b.ws);
                o.d = float4(a.data.x, a.data.y, a.uv.y, a.color.b);
                o.e = float4(b.rnd, b.gust, b.trample, a.uv.x);
                return o;
            }

            half4 frag(V2F i, bool front : SV_IsFrontFace) : SV_Target
            {
                RRSeeThroughClip(i.positionCS, i.ws);
                float hf = i.d.x, part = i.d.y, along = i.d.z, tip = i.d.w;
                float rnd = i.e.x, gust = i.e.y, trample = i.e.z;
                float isPlume = step(0.75, part), isStem = step(0.25, part) * (1 - isPlume);

                float3 leaf = lerp(_LeafCol.rgb, _LeafTip.rgb, saturate(along * along * (0.6 + rnd)));
                float3 stem = _StemCol.rgb;
                float3 plumeBase = lerp(_PlumeCol.rgb, _ReedPlumeCol.rgb, _IsReed);
                float3 plume = lerp(plumeBase, _PlumeTip.rgb * lerp(1, 0.7, _IsReed), saturate(tip * 0.9 + 0.1));
                float3 albedo = leaf * (1 - isPlume - isStem) + stem * isStem + plume * isPlume;
                // Patchy colour across the field, darker dead patches and a bit of per-stem difference.
                float patchN = RFNoise(i.ws.xz * 0.11);
                albedo *= lerp(1.0, 0.65 + 0.55 * patchN, _Variation) * (0.9 + 0.2 * rnd);
                // Dark roots: the clump grows out of shadow.
                albedo = lerp(_RootCol.rgb, albedo, smoothstep(0.0, 0.45, hf));

                float3 n = normalize(i.nws) * (front ? 1 : -1);
                float3 v = normalize(_WorldSpaceCameraPos - i.ws);
                Light ml = GetMainLight();
                // Baked shade from the reed mass and props: deep in the stand it is dark, the tops stay in the sun.
                // Reeds read no shadow map at all (cheapest): the dark root gradient does the shading inside the stand.
                float shadow = 1;
                // Thin blades: lit on the side facing the moon, light shines through from behind, plus a soft wrap.
                float dl = dot(n, ml.direction);
                float ndl = saturate(dl) * 0.8 + saturate(-dl) * 0.5 * (0.4 + hf) + _Wrap * 0.3;
                float3 col = albedo * (_Ambient.rgb * (0.4 + 0.6 * hf) + ml.color * ndl * shadow);
                // Plumes glow when the light is behind them (moon or red sky through the seed heads).
                float back = pow(saturate(dot(-v, ml.direction) * 0.5 + 0.5), 2);
                // Glow mainly on the backlit rim: the outer, thinner part of each plume strand.
                float rim = tip * tip;
                col += plume * isPlume * ml.color * (back * _Translucency * rim + 0.15) * shadow;
                // Gust fronts show as a brighter sheen rolling over the tops, so the waves read from above.
                col += albedo * gust * _GustSheen * smoothstep(0.4, 1.0, hf) * (0.4 + isPlume) * ml.color;
                // Trampled reeds lie in shadow of their neighbours.
                col *= 1 - trample * 0.35;

                InputData inputData = (InputData)0;
                inputData.positionWS = i.ws;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                uint count = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(count)
                    Light add = GetAdditionalLight(lightIndex, i.ws, half4(1, 1, 1, 1));
                    float3 lc = add.color * add.distanceAttenuation;
                    col += albedo * lc * saturate(dot(n, add.direction) * 0.5 + 0.5);
                    col += plume * isPlume * lc * 0.6;
                LIGHT_LOOP_END

                col = RFFog(col, i.ws);
                return half4(col, 1);
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
            struct OV { float4 cs : SV_POSITION; float3 ws : TEXCOORD0; };
            OV vert(Attr a) { UNITY_SETUP_INSTANCE_ID(a); OV o; o.ws = BendVertex(a).ws; o.cs = TransformWorldToHClip(o.ws); return o; }
            half frag(OV i) : SV_Target { RRSeeThroughClip(i.cs, i.ws); return 0; }
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
            V vert(Attr a) { UNITY_SETUP_INSTANCE_ID(a); Bent b = BendVertex(a); V o; o.ws = b.ws; o.pos = TransformWorldToHClip(b.ws); o.n = b.nws; return o; }
            half4 frag(V i, bool front : SV_IsFrontFace) : SV_Target { RRSeeThroughClip(i.pos, i.ws); float3 n = normalize(i.n) * (front ? 1 : -1); return half4(n, 0); }
            ENDHLSL
        }
    }
}
