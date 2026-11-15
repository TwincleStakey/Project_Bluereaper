// 대전 앞 (palace court) floor, 11-15. Built on the D4 GDC 2024 ground rules (Kido: "디아블로 문서 읽고 제대로"):
//  - four scanned materials only: worn flagstone (Poly Haven monastery_stone_floor, CC0), joint dirt (brown_mud_leaves),
//    ash creep (burned_ground) and border gravel (Megascans gravel). No coded slabs, cracks or tile lines.
//  - tiling 6-8 m, so one stone is ~1.5-3 m (game refs: PoE2/D4/TQ2 dark courts), no fine "자글자글" repeat.
//  - height blend: dirt and ash fill the low joints first (flagstone displacement), so they seep in instead of fading.
//  - weak normals (0.45), narrow dark albedo (~0.12-0.4 linear-ish, low saturation), only mid-scale breakup,
//    and calm stone "rest areas" left between the dirt patches.
// Vertex colour (from LookStudyRuins.BuildPalace): B = court paving, G = trampled dirt outside the galleries, A = ash/burn.
Shader "LookStudy/PalaceScanGround"
{
    Properties
    {
        _StoneTex("Flagstone albedo", 2D) = "gray" {}
        _StoneNor("Flagstone normal", 2D) = "bump" {}
        _StoneHgt("Flagstone height (displacement)", 2D) = "gray" {}
        _DirtTex("Joint dirt albedo", 2D) = "gray" {}
        _DirtNor("Joint dirt normal", 2D) = "bump" {}
        _AshTex("Ash creep albedo", 2D) = "gray" {}
        _AshNor("Ash creep normal", 2D) = "bump" {}
        _GravelTex("Border gravel albedo", 2D) = "gray" {}
        _GravelNor("Border gravel normal", 2D) = "bump" {}
        _StoneTile("Flagstone tile size (m)", Float) = 7
        _SoilTile("Dirt/ash/gravel tile size (m)", Float) = 6
        _StoneTint("Stone tint", Color) = (0.62, 0.6, 0.57, 1)
        _StoneSat("Stone saturation", Range(0,1)) = 0.35
        _DirtTint("Dirt tint", Color) = (0.55, 0.5, 0.45, 1)
        _AshTint("Ash tint", Color) = (0.5, 0.48, 0.46, 1)
        _NormalK("Normal strength", Range(0,1)) = 0.45
        _JointDirt("Dirt in joints (0..1)", Range(0,1)) = 0.55
        _DirtPatch("Dirt patch coverage (0..1)", Range(0,1)) = 0.3
        _Wash("Mid-scale light/dark wash", Range(0,0.5)) = 0.16
        _DryGloss("Dry gloss scale (0 = matte)", Range(0,2)) = 0.3
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "LSStage.hlsl"
        TEXTURE2D(_StoneTex); SAMPLER(sampler_StoneTex); TEXTURE2D(_StoneNor); TEXTURE2D(_StoneHgt);
        TEXTURE2D(_DirtTex); TEXTURE2D(_DirtNor);
        TEXTURE2D(_AshTex); TEXTURE2D(_AshNor);
        TEXTURE2D(_GravelTex); TEXTURE2D(_GravelNor);
        CBUFFER_START(UnityPerMaterial)
        float _StoneTile, _SoilTile;
        float4 _StoneTint, _DirtTint, _AshTint;
        float _StoneSat, _NormalK, _JointDirt, _DirtPatch, _Wash, _DryGloss;
        CBUFFER_END
        ENDHLSL
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            struct Attr { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 color : COLOR; };
            struct V2F { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; float4 screen : TEXCOORD2; float4 color : COLOR; };

            V2F vert(Attr a)
            {
                V2F o;
                o.positionWS = TransformObjectToWorld(a.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(a.normalOS);
                o.screen = ComputeScreenPos(o.positionCS);
                o.color = a.color;
                return o;
            }

            #define S(t, uv) SAMPLE_TEXTURE2D(t, sampler_StoneTex, uv)
            #define SB(t, uv, b) SAMPLE_TEXTURE2D_BIAS(t, sampler_StoneTex, uv, b)
            float3 NorW(float4 packed, float k) { float3 t = UnpackNormal(packed); return float3(t.x * k, 0, t.y * k); }
            float3 Desat(float3 c, float s) { float l = dot(c, float3(0.3, 0.59, 0.11)); return lerp(l.xxx, c, s); }
            // Height blend (Megascans/D4 style): the layer with more height+weight wins, with a soft band.
            float HB(float hBase, float hLayer, float w, float band) { return saturate((hLayer + w * 2.0 - 1.0 - hBase) / band + 0.5); }

            half4 frag(V2F i) : SV_Target
            {
                float3 ws = i.positionWS;
                float3 N = normalize(i.normalWS);
                float2 q = ws.xz;

                // Flagstone: two scan samples (second rotated 37 deg, other tile size) and the one standing higher wins,
                // switched by a ~45 m noise. Stones stay whole (no ghosted double joints), the repeat stops lining up.
                float2 u1 = q / _StoneTile;
                float2 qr = float2(q.x * 0.799 - q.y * 0.602, q.x * 0.602 + q.y * 0.799) + float2(31.7, 12.9);
                float2 u2 = qr / (_StoneTile * 1.13);
                float h1 = S(_StoneHgt, u1).r, h2 = S(_StoneHgt, u2).r;
                float pick = smoothstep(0.38, 0.62, LSFbm(q * 0.022 + 4.7));
                float t2 = saturate((h2 + pick * 2.0 - 1.0 - h1) / 0.08 + 0.5);
                float hS = lerp(h1, h2, t2);
                float3 stone = lerp(S(_StoneTex, u1).rgb, S(_StoneTex, u2).rgb, t2);
                float3 nS = lerp(NorW(S(_StoneNor, u1), _NormalK), NorW(S(_StoneNor, u2), _NormalK), t2);
                stone = Desat(stone, _StoneSat) * _StoneTint.rgb;

                float2 us = q / _SoilTile;
                float2 usr = float2(q.x * 0.5 + q.y * 0.866, -q.x * 0.866 + q.y * 0.5) / (_SoilTile * 1.21);
                float soilMix = smoothstep(0.35, 0.65, LSFbm(q * 0.031 + 9.1));
                float3 dirt = lerp(S(_DirtTex, us).rgb, S(_DirtTex, usr).rgb, soilMix) * _DirtTint.rgb;
                // mip bias: the ash scan has bright needle specks that read as "자글자글" from the game camera
                float3 ash = SB(_AshTex, usr * 1.07, 2.0).rgb * _AshTint.rgb;
                float3 gravel = SB(_GravelTex, us * 1.4, 1.0).rgb * _DirtTint.rgb;
                dirt = Desat(dirt, 0.55); ash = Desat(ash, 0.4); gravel = Desat(gravel, 0.45);

                // Masks: mid-scale only (10-40 m blobs), so big calm stone areas remain for characters and bullets.
                float blob = LSFbm(q * 0.045 + 1.3);
                float blob2 = LSFbm(q * 0.11 + 7.9);
                float court = saturate(i.color.b);
                float outside = saturate(i.color.g);
                float wDirt = _JointDirt * 0.5 + smoothstep(1.0 - _DirtPatch, 1.0, blob * 0.75 + blob2 * 0.35) * 0.55;
                wDirt = saturate(max(wDirt, outside * 1.2));
                float mDirt = HB(hS, 0.5, wDirt, 0.18);
                float wAsh = saturate(i.color.a * 0.9 + (blob2 - 0.5) * 0.3);
                float mAsh = HB(hS, 0.45, wAsh, 0.22);
                // Gravel only in the court-to-dirt border band (D4: gravel/grass where materials meet).
                float border = saturate(1.0 - abs(outside - 0.5) * 2.2) * smoothstep(0.3, 0.7, blob2 + 0.2);
                float mGrav = HB(hS, 0.55, border, 0.2);

                float3 albedo = stone; float3 dn = nS;
                albedo = lerp(albedo, dirt, mDirt);   dn = lerp(dn, NorW(S(_DirtNor, us), _NormalK * 0.8), mDirt);
                albedo = lerp(albedo, gravel, mGrav); dn = lerp(dn, NorW(S(_GravelNor, us * 1.4), _NormalK * 0.8), mGrav);
                albedo = lerp(albedo, ash, mAsh);     dn = lerp(dn, NorW(S(_AshNor, usr * 1.07), _NormalK * 0.7), mAsh);

                // Mid-scale value wash (no high-frequency noise).
                albedo *= 1.0 + (LSFbm(q * 0.028 + 5.5) - 0.5) * 2.0 * _Wash;
                // Narrow dark range: compress toward the mid, clamp the brightest stones.
                float lum = dot(albedo, float3(0.3, 0.59, 0.11));
                albedo *= lerp(1.0, min(1.0, 0.3 / max(lum, 1e-3)), 0.75);

                float3 n = normalize(N + dn);
                // Joint AO from the stone height: soft, only where the joint is not filled with dirt.
                float ao = lerp(0.72, 1.0, smoothstep(0.15, 0.5, hS)) ;
                ao = lerp(ao, 1.0, mDirt * 0.6);

                // Footprints (same trail texture as the other stage grounds).
                float soft = saturate(mDirt + mAsh + outside);
                float e = _LSTrailRect.z / 2048.0;
                float fp = LSTrail(ws.xz).g * soft;
                if (fp > 0.002)
                {
                    float fx = LSTrail(ws.xz + float2(e, 0)).g - LSTrail(ws.xz - float2(e, 0)).g;
                    float fz = LSTrail(ws.xz + float2(0, e)).g - LSTrail(ws.xz - float2(0, e)).g;
                    n = normalize(n + float3(fx, 0, fz) * 2.2 * soft);
                    albedo *= 1.0 - fp * 0.42;
                }

                float gloss = 0.06 * _DryGloss * (1.0 - mDirt);
                float3 col = StageLight(albedo, n, ws, i.screen.xy / i.screen.w, gloss, ao);
                col = StageFog(col, ws);
                return half4(col, 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            struct DA { float4 p : POSITION; float3 n : NORMAL; };
            struct DV { float4 cs : SV_POSITION; float3 n : TEXCOORD0; };
            DV vert(DA a) { DV o; o.cs = TransformObjectToHClip(a.p.xyz); o.n = TransformObjectToWorldNormal(a.n); return o; }
            half4 frag(DV i) : SV_Target { return half4(normalize(i.n), 0); }
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
            float4 vert(float4 p : POSITION) : SV_POSITION { return TransformObjectToHClip(p.xyz); }
            half frag() : SV_Target { return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float3 _LightDirection;
            struct Attr { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            float4 vert(Attr a) : SV_POSITION
            {
                float3 ws = TransformObjectToWorld(a.positionOS.xyz);
                float3 n = TransformObjectToWorldNormal(a.normalOS);
                float4 cs = TransformWorldToHClip(ApplyShadowBias(ws, n, _LightDirection));
                #if UNITY_REVERSED_Z
                cs.z = min(cs.z, cs.w * UNITY_NEAR_CLIP_VALUE);
                #else
                cs.z = max(cs.z, cs.w * UNITY_NEAR_CLIP_VALUE);
                #endif
                return cs;
            }
            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
