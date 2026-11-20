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
        _AOStrength("Mass AO (deep in the stand)", Range(0,1)) = 0.9
        _TopLight("Tops above the canopy brighter", Range(0,1)) = 0.45
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
            float _Variation, _Translucency, _Wrap, _GustSheen, _IsReed, _AOStrength, _TopLight;
        CBUFFER_END

        // RF_PROC (11-13 perf): drawn by ReedInstancer with RenderMeshPrimitives. Matrices sit in one buffer uploaded
        // once; _RFProcIdx lists the visible clumps for this camera.
        #if defined(RF_PROC)
        StructuredBuffer<float4x4> _RFProcMats;
        StructuredBuffer<uint> _RFProcIdx;
        uint _RFProcBase;
        #endif

        // 11-14 real cuts (Kido: "갈대는 잘리는게 아니라 그냥 키만 줄여서 이상해"): a cut clump keeps its full shape below a
        // slanted cut line and loses everything above it (clipped, no longer squashed shorter). RoomRun CutDebris throws
        // the part above as a separate piece: the same mesh and material with RF_PIECE, which keeps only the band [lo, hi]
        // of the clump. RFCutKeep / RFCutSeed / RFHash are mirrored in CutDebris.cs so the stub and the top meet at the cut.
        #if defined(RF_PIECE)
        UNITY_INSTANCING_BUFFER_START(RFPiece)
            UNITY_DEFINE_INSTANCED_PROP(float4, _RFPieceCut)    // keep band lo, hi (height fraction), dissolve, -
            UNITY_DEFINE_INSTANCED_PROP(float4, _RFPieceRoot)   // the clump's root where it stood (x, z, y)
        UNITY_INSTANCING_BUFFER_END(RFPiece)
        #endif
        float RFCutKeep(float2 xz) { return 0.2 + 0.16 * frac((xz.x * 0.7131 + xz.y * 0.3217) * 7.13); }
        float RFCutSeed(float2 xz) { return frac((xz.x * 0.4123 - xz.y * 0.8817) * 5.31); }
        // height fraction of a vertex measured along the slanted cut plane
        float RFCutH(float hf, float3 posOS, float seed)
        {
            float an = seed * 6.2831853;
            return hf + 0.22 * dot(posOS.xz, float2(cos(an), sin(an)));
        }
        // screen-door dissolve for fading pieces
        void RFDissolve(float4 positionCS, float fade)
        {
            if (fade <= 0) return;
            float n = frac(52.9829189 * frac(dot(positionCS.xy, float2(0.06711056, 0.00583715))));
            clip(n - fade);
        }

        struct Attr
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv : TEXCOORD0;   // across, along
            float2 data : TEXCOORD1; // height fraction, part (0 leaf, 0.5 stem, 1 plume)
            float4 color : COLOR;    // rnd, stiffness, plume tip fraction
            #if defined(RF_PROC) && !defined(UNITY_INSTANCING_ENABLED)
            uint procId : SV_InstanceID;
            #endif
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        float4x4 ObjM(Attr a)
        {
            #if defined(RF_PROC)
                #if defined(UNITY_INSTANCING_ENABLED)
                return _RFProcMats[_RFProcIdx[a.instanceID + _RFProcBase]];
                #else
                return _RFProcMats[_RFProcIdx[a.procId + _RFProcBase]];
                #endif
            #else
            return UNITY_MATRIX_M;
            #endif
        }

        struct Bent { float3 ws; float3 nws; float gust; float trample; float rnd; float3 root; float2 cut; };   // cut: keep (> 0 drawn), dissolve

        Bent BendVertex(Attr a)
        {
            Bent o;
            float4x4 M = ObjM(a);
            float3 root = float3(M._m03, M._m13, M._m23);
            float3 ws = mul(M, float4(a.positionOS.xyz, 1)).xyz;
            // clumps are yaw x (w, h, w): the inverse transpose is the matrix with each axis divided by its squared scale
            float3 s2 = float3(dot(M._m00_m10_m20, M._m00_m10_m20), dot(M._m01_m11_m21, M._m01_m11_m21), dot(M._m02_m12_m22, M._m02_m12_m22));
            float3 nws = normalize(mul((float3x3)M, a.normalOS / max(s2, 1e-6)));
            float H = sqrt(s2.y); // clump height in metres
            float hf = a.data.x;
            #if defined(RF_PIECE)
            // a flying top: rigid (no wind or trample bend), coloured and shaded as the clump it was cut from
            float4 pc = UNITY_ACCESS_INSTANCED_PROP(RFPiece, _RFPieceCut);
            float3 home3 = UNITY_ACCESS_INSTANCED_PROP(RFPiece, _RFPieceRoot).xyz;
            float2 home = home3.xy;
            o.root = float3(home.x, home3.z, home.y);   // where it grew: the baked mass AO / shade measure height from there
            float rnd = frac(a.color.r + RFHash(home));
            float he = RFCutH(hf, a.positionOS.xyz, RFCutSeed(home));
            o.cut = float2(min(he - pc.x, pc.y - he), pc.z);
            float2 offs = 0; o.gust = 0; o.trample = 0;
            #else
            o.root = root;
            float rnd = frac(a.color.r + RFHash(root.xz));
            // greatsword cuts: everything above the slanted cut line is clipped (was RRCutApply, which squashed it shorter)
            float cc = RRCut(root.xz);
            o.cut = float2(cc > 0.001 ? lerp(1.3, RFCutKeep(root.xz), cc) - RFCutH(hf, a.positionOS.xyz, RFCutSeed(root.xz)) : 1, 0);
            float2 offs = RFBend(root.xz, rnd, a.color.g, o.gust, o.trample);
            #endif
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
            #if !defined(RF_PIECE)
            ws.xz += _RF_Wind.xy * lag * 0.025 * H * a.uv.y * plume * _RF_Wind.z;
            #endif
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
            #pragma multi_compile_local _ RF_PROC RF_PIECE
            #pragma target 4.5
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            // 11-14 (Kido: the reed field "floats", no shade gets in): RoomRun's StageMood sets these for the reed field.
            // _RF_RTShadow: reeds take the realtime sun shadow (trees, snags, characters) per vertex (cheap: one compare
            // sample per vertex, soft by interpolation). _RF_AmbCut: the flat _Ambient term is cut so the low sun and the
            // baked mass AO carry the shading. Both 0 (unset) = the old look everywhere else (bamboo ground cover, editor).
            float _RF_RTShadow, _RF_AmbCut;

            struct V2F
            {
                float4 positionCS : SV_POSITION;
                float3 ws : TEXCOORD0;
                float3 nws : TEXCOORD1;
                float4 d : TEXCOORD2;  // hf, part, along, tip fraction
                float4 e : TEXCOORD3;  // rnd, gust, trample, across
                float4 m : TEXCOORD4;  // mass occlusion, baked sun shadow, above the canopy (RFMass), moon shaft
                float2 cut : TEXCOORD5; // greatsword cut: keep (> 0 drawn), dissolve
                float2 rts : TEXCOORD6; // realtime sun shadow (1 lit), clump-volume sun side (0 away .. 1 toward the sun)
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
                o.m = float4(RFMass(b.ws, b.root), RFShaftLight(b.ws));
                o.cut = b.cut;
                o.rts = float2(1, 0.5);
                #if defined(_MAIN_LIGHT_SHADOWS) || defined(_MAIN_LIGHT_SHADOWS_CASCADE)
                if (_RF_RTShadow > 0.5) o.rts.x = MainLightRealtimeShadow(TransformWorldToShadowCoord(b.ws));
                #endif
                // the clump as one volume: a round normal from the clump's middle, so the sun side of each clump is lit and
                // the far side falls into shade (reads as a solid tuft with depth, not flat cut-out blades)
                {
                    float y = max(b.ws.y - b.root.y, 0);
                    float top = y / max(a.data.x, 0.15);
                    float3 c = b.root + float3(0, top * 0.45, 0);
                    float3 cn = b.ws - c; cn.y *= 0.6;
                    o.rts.y = saturate(dot(normalize(cn + float3(0, 1e-3, 0)), GetMainLight().direction) * 0.5 + 0.5);
                }
                return o;
            }

            half4 frag(V2F i, bool front : SV_IsFrontFace) : SV_Target
            {
                clip(i.cut.x); RFDissolve(i.positionCS, i.cut.y);
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
                albedo *= lerp(1.0, 0.65 + 0.55 * patchN, _Variation) * (0.8 + 0.4 * rnd);   // 11-14: wider clump-to-clump spread
                // Dark roots: the clump grows out of shadow.
                albedo = lerp(_RootCol.rgb, albedo, smoothstep(0.0, 0.7, hf));   // 11-14: longer dark base, tips stay lit

                float3 n = normalize(i.nws) * (front ? 1 : -1);
                float3 v = normalize(_WorldSpaceCameraPos - i.ws);
                Light ml = GetMainLight();
                // Baked shade from the reed mass and props: deep in the stand it is dark, the tops stay in the sun.
                // Reeds read no shadow map at all (cheapest): the dark root gradient does the shading inside the stand.
                // 11-13: baked mass AO + the baked reed shadow on the stalks (RFMass, per vertex): lower parts of clumps
                // deep in a stand go dark, edge clumps stay lit, clumps shorter than their neighbours sit in shade and the
                // ones sticking out above the canopy catch more light, so the top surface reads with depth.
                float ao = 1 - _AOStrength * i.m.x;
                float shadow = i.m.y * lerp(1, ao, 0.7) * i.rts.x * lerp(1, lerp(0.25, 1.25, i.rts.y), _RF_AmbCut > 0 ? 1 : 0);
                // Thin blades: lit on the side facing the moon, light shines through from behind, plus a soft wrap.
                float dl = dot(n, ml.direction);
                float ndl = saturate(dl) * 0.8 + saturate(-dl) * 0.5 * (0.4 + hf) + _Wrap * 0.3;
                float3 col = albedo * (_Ambient.rgb * (1 - _RF_AmbCut) * (0.4 + 0.6 * hf) * ao * lerp(1, ao, _RF_AmbCut) + ml.color * ndl * shadow);
                col *= 1 + _TopLight * i.m.z;
                // 11-14 QA: big slow cloud-shade patches (~10-15 m) so wide reed seas break into lit and shadowed areas
                col *= lerp(0.66 - 0.2 * saturate(_RF_AmbCut * 2), 1.0, smoothstep(0.3, 0.7, RFNoise(i.ws.xz * 0.07 + 31.0 + _RF_Wind.xy * _RF_Time * 0.03)));
                // moonlight shafts: reeds inside a beam catch the moon, the plumes glow in it
                col += (albedo * (0.5 + 0.7 * hf) + plume * isPlume * 0.8) * _RF_ShaftCol.rgb * i.m.w;
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
            #pragma multi_compile_local _ RF_PROC RF_PIECE
            #pragma target 4.5
            struct OV { float4 cs : SV_POSITION; float3 ws : TEXCOORD0; float2 cut : TEXCOORD1; };
            OV vert(Attr a) { UNITY_SETUP_INSTANCE_ID(a); OV o; Bent b = BendVertex(a); o.ws = b.ws; o.cut = b.cut; o.cs = TransformWorldToHClip(o.ws); return o; }
            half frag(OV i) : SV_Target { clip(i.cut.x); RFDissolve(i.cs, i.cut.y); RRSeeThroughClip(i.cs, i.ws); return 0; }
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
            #pragma multi_compile_local _ RF_PROC RF_PIECE
            #pragma target 4.5
            struct V { float4 pos : SV_POSITION; float3 n : TEXCOORD0; float3 ws : TEXCOORD1; float2 cut : TEXCOORD2; };
            V vert(Attr a) { UNITY_SETUP_INSTANCE_ID(a); Bent b = BendVertex(a); V o; o.ws = b.ws; o.cut = b.cut; o.pos = TransformWorldToHClip(b.ws); o.n = b.nws; return o; }
            half4 frag(V i, bool front : SV_IsFrontFace) : SV_Target { clip(i.cut.x); RFDissolve(i.pos, i.cut.y); RRSeeThroughClip(i.pos, i.ws); float3 n = normalize(i.n) * (front ? 1 : -1); return half4(n, 0); }
            ENDHLSL
        }
    }
}
