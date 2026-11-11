// Blood decal: a unit cube projected straight down onto the ground inside it by reconstructing world position from the
// depth texture (screen-space decal, no DBuffer). _Cell < 0 picks an atlas cell from the object's world-position hash.
// Atlas SP_Blood_MS2.png is composed only from the Megascans Blood Stain scan (ArtSource/SnowPlague/gen_blood_atlas.py):
// R thickness (blurred coverage, reaches past the stain = soak), G scan BaseColor.r, B scan BaseColor.g x4, A opacity.
// r12 (Kido: "너무 붉고 젖은 느낌도 아니고"; ss5 kill): two passes
//   1. "BloodSoak" (SRPDefaultUnlit, drawn first): MULTIPLY - the ground darkens to black-red and its own gravel/snow
//      detail shows through, like blood soaked into dirt; thin film + scattered scan droplets around the stain.
//   2. "BloodWet" (UniversalForward): the pooled thick middles only - an opaque-ish dark crimson film, lit, with a
//      small strong specular (moon + lantern lights) and environment sheen on normals from the thickness, so the pools
//      read wet and slightly raised.
// Neither pass touches vegetation / props: only fragments within a few cm of the decal's ground height and facing up.
Shader "SnowPlague/BloodDecal"
{
    Properties
    {
        _BloodTex("Blood atlas (R thick, G/B scan colour, A opacity)", 2D) = "white" {}
        _Cell("Atlas cell (0-3, -1 = random)", Float) = -1
        _StainMul("Stain multiply (thick)", Color) = (0.42, 0.13, 0.11, 1)
        _SoakMul("Soak multiply (halo)", Color) = (0.82, 0.55, 0.52, 1)
        _FilmMul("Film multiply (thin blood)", Color) = (0.62, 0.22, 0.19, 1)
        _Grain("Grain / speckle breakup", Range(0, 1)) = 0.75
        _Glint("Wet glint (camera key)", Range(0, 4)) = 1
        _Soak("Soak halo strength", Range(0, 1)) = 0.6
        _Drops("Scattered droplets", Range(0, 1)) = 0.8
        _PoolCol("Wet pool colour", Color) = (0.23, 0.03, 0.025, 1)
        _Pool("Wet pool opacity", Range(0, 1)) = 0.85
        _Spec("Specular strength", Range(0, 10)) = 3
        _SpecPow("Specular tightness", Range(16, 1024)) = 400
        _Bump("Thickness bump", Range(0, 8)) = 3
        _Sheen("Environment sheen", Range(0, 2)) = 0.5
        _Emit("Emission", Range(0, 1)) = 0.04
        _MaxRise("Max rise above the neighbouring ground (m)", Range(0.02, 0.5)) = 0.08
        _Debug("Debug (rise/normal view)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-100" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        HLSLINCLUDE
        #include "Assets/Game/SnowPlague/Shaders/SPCommon.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
        TEXTURE2D(_BloodTex); SAMPLER(sampler_BloodTex);
        CBUFFER_START(UnityPerMaterial)
            float4 _BloodTex_ST, _StainMul, _SoakMul, _FilmMul, _PoolCol;
            float _Cell, _Soak, _Drops, _Pool, _Spec, _SpecPow, _Bump, _Sheen, _Emit, _MaxRise, _Debug, _Grain, _Glint;
        CBUFFER_END
        struct A { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
        struct V { float4 positionCS : SV_POSITION; float4 sp : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
        V vert(A a)
        {
            V o; UNITY_SETUP_INSTANCE_ID(a); UNITY_TRANSFER_INSTANCE_ID(a, o);
            o.positionCS = TransformObjectToHClip(a.positionOS.xyz); o.sp = ComputeScreenPos(o.positionCS); return o;
        }
        float2 CellUV(float2 p01, float cell) { return p01 * 0.5 + float2(fmod(cell, 2), 1 - floor(cell / 2)) * 0.5; }

        struct BD { float3 ws, n, nb; float2 suv; float thick, cov, core, soak, drops, mottle, fade, rise, grain, fine, rad, edgeN, spread; };
        float CellA(float2 p, float cell) { return SAMPLE_TEXTURE2D(_BloodTex, sampler_BloodTex, CellUV(frac(p), cell)).a; }
        float2 Rot(float2 p, float a) { float s, c; sincos(a, s, c); return float2(p.x * c - p.y * s, p.x * s + p.y * c); }
        BD Eval(V i)
        {
            BD d;
            d.suv = i.sp.xy / i.sp.w;
            float raw = SampleSceneDepth(d.suv);
            #if !UNITY_REVERSED_Z
            raw = lerp(UNITY_NEAR_CLIP_VALUE, 1, raw);
            #endif
            d.ws = ComputeWorldSpacePosition(d.suv, raw, UNITY_MATRIX_I_VP);
            float3 os = TransformWorldToObject(d.ws);
            clip(0.5 - abs(os));
            float3 n = normalize(cross(ddy(d.ws), ddx(d.ws)));
            if (n.y < 0) n = -n;
            d.n = n;
            // ground only (bl1: an absolute height window failed, the ground under a decal varies 5-20 cm): a pixel is
            // ground when it is not raised above its screen neighbours. Grass blades, brush twigs, stakes and props stand
            // 10+ cm above the depth found 6 / 16 px to each side; gravel and the ground's own relief do not.
            float rise = d.ws.y - TransformObjectToWorld(float3(0, 0, 0)).y;
            float minY = d.ws.y;
            [unroll] for (int k = 0; k < 8; k++)
            {
                float2 dir = k % 4 == 0 ? float2(1, 0) : k % 4 == 1 ? float2(-1, 0) : k % 4 == 2 ? float2(0, 1) : float2(0, -1);
                float2 o = d.suv + dir * (k < 4 ? 6 : 16) / _ScreenParams.xy;
                float rk = SampleSceneDepth(o);
                #if !UNITY_REVERSED_Z
                rk = lerp(UNITY_NEAR_CLIP_VALUE, 1, rk);
                #endif
                minY = min(minY, ComputeWorldSpacePosition(o, rk, UNITY_MATRIX_I_VP).y);
            }
            d.rise = d.ws.y - minY;
            d.fade = (1 - smoothstep(_MaxRise * 0.4, _MaxRise, d.ws.y - minY)) * (1 - smoothstep(0.4, 0.5, rise)) * smoothstep(0.7, 0.88, n.y);
            if (_Debug < 0.5) clip(d.fade - 0.01);

            float cell = _Cell >= 0 ? _Cell : floor(SPInstanceRand() * 3.999);
            float rnd = SPInstanceRand();
            // look8 (Kido "핏자국 왜이래": stamped stickers): each decal turns and mirrors its atlas cell by its own hash, so
            // two decals with the same cell never repeat; a round fade keeps the turned cell's corners from cutting a square
            float2 q = Rot(os.xz, rnd * 6.283) * float2(frac(rnd * 7.13) > 0.5 ? -1 : 1, 1);
            d.rad = 1 - smoothstep(0.36, 0.5, length(os.xz));
            float2 uv = CellUV(clamp(q, -0.49, 0.49) + 0.5, cell);
            float4 b = SAMPLE_TEXTURE2D(_BloodTex, sampler_BloodTex, uv);
            d.thick = b.r * d.rad;
            d.edgeN = SPFbmI(d.ws.xz * 2.4 + rnd * 37.0);   // world noise: the soak front is uneven, never the scan's crisp outline
            // bl5 (lead: "flat uniform blobs with crisp cut-out edges"): everything below is broken by scan-derived grain
            // at two scales (the pool and splatter cells tiled x5.3 and x11.1), the outline feathers into dots, the solid part is only the
            // small pooled cores, and a spray of scan droplets / streak flecks at three scales radiates around it
            float2 p = Rot(os.xz, rnd * 6.283);
            d.grain = smoothstep(0.08, 0.5, CellA(Rot(p, 0.9) * 5.3 + 0.21, 0));   // splatter cells: denser than the drip cell
            d.fine = smoothstep(0.08, 0.5, CellA(Rot(p, 2.3) * 11.1 + 0.63, 1));
            // look8: blood soaked into snow, not a stamped sticker. The shape is the scan's blurred thickness (R), pushed in
            // and out by world noise so the front is feathered and uneven; spread 0 = rim .. 1 = saturated middle.
            // The scan's crisp opacity (A) only mottles the inside; no droplet / fleck spray any more (_Drops unused).
            d.spread = saturate((d.thick - 0.05 + (d.edgeN - 0.5) * 0.34) / 0.55);
            float cov = smoothstep(0.1, 0.7, b.a) * d.rad;
            d.cov = cov;
            // pooled clots: only where the scan is thickest AND opaque, broken into a few patches; soft edged
            float patch = smoothstep(0.3, 0.7, CellA(Rot(p, 5.2) * 2.1 + 0.44, 1) * 0.6 + d.edgeN * 0.6);
            d.core = smoothstep(0.84, 0.98, d.thick) * smoothstep(0.35, 0.85, b.a) * patch * d.rad;
            d.soak = 1 - smoothstep(0.0, 0.35, d.spread);   // the pale-pink outer diffusion band
            d.drops = 0;
            d.mottle = saturate(1 + (b.g - 0.33) * 4 + (b.b - 0.12) * 1.5);

            // wet meniscus normal from the thickness gradient
            const float e = 0.003;
            float tx = SAMPLE_TEXTURE2D(_BloodTex, sampler_BloodTex, uv + float2(e, 0)).r;
            float tz = SAMPLE_TEXTURE2D(_BloodTex, sampler_BloodTex, uv + float2(0, e)).r;
            float3 wx = normalize(TransformObjectToWorldDir(float3(1, 0, 0)));
            float3 wz = normalize(TransformObjectToWorldDir(float3(0, 0, 1)));
            d.nb = normalize(n - (wx * (tx - d.thick) + wz * (tz - d.thick)) * _Bump * 10);

            return d;
        }
        ENDHLSL

        // 1. soak: multiply the ground toward black-red (gravel and snow texture stay visible through it)
        Pass
        {
            Name "BloodSoak"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend DstColor Zero
            ZWrite Off
            ZTest GEqual
            Cull Front
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment fragSoak
            #pragma multi_compile_instancing
            half4 fragSoak(V i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                BD d = Eval(i);
                if (_Debug > 0.5) return half4(saturate(d.rise * 5), d.n.y > 0.8 ? 1 : 0.3, saturate(-d.rise * 5), 1);
                // look8: snow -> pale pink diffusion at the uneven rim -> red soaked body (snow texture shows through the
                // multiply, scan colour mottles it) -> dark maroon only in the pooled clots
                float s = d.spread;
                float3 mul = lerp(1, _SoakMul.rgb, smoothstep(0.0, 0.22, s) * _Soak);
                float3 film = _FilmMul.rgb * lerp(0.85, 1.12, d.mottle) * lerp(1, 0.85, d.fine * _Grain * d.cov);
                mul = lerp(mul, film, smoothstep(0.25, 0.85, s) * lerp(0.8, 1, d.cov));
                mul = lerp(mul, _StainMul.rgb, saturate(d.core * 1.3));
                return half4(lerp(1, mul, d.fade), 1);
            }
            ENDHLSL
        }

        // 2. wet pools: lit dark crimson film on the thick middles + small strong specular + sheen (premultiplied)
        Pass
        {
            Name "BloodWet"
            Tags { "LightMode"="UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest GEqual
            Cull Front
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment fragWet
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            half4 fragWet(V i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                BD d = Eval(i);
                clip(0.5 - _Debug);
                float pool = d.core * _Pool * d.fade;
                float wet = saturate(d.core + smoothstep(0.6, 0.95, d.thick) * d.cov * 0.35 + d.drops * 0.5) * d.fade;
                clip(wet - 0.003);
                float3 v = GetWorldSpaceNormalizeViewDir(d.ws);

                Light ml = GetMainLight(TransformWorldToShadowCoord(d.ws));
                float sh = ml.distanceAttenuation * ml.shadowAttenuation;
                float3 diff = SampleSH(d.n) + ml.color * saturate(dot(d.n, ml.direction)) * sh;
                float3 spec = ml.color * sh * pow(saturate(dot(d.nb, normalize(ml.direction + v))), _SpecPow);
                #if defined(_ADDITIONAL_LIGHTS) || defined(_CLUSTER_LIGHT_LOOP)
                {
                    InputData inputData = (InputData)0;
                    inputData.positionWS = d.ws; inputData.normalizedScreenSpaceUV = d.suv;
                    uint count = GetAdditionalLightsCount();
                    LIGHT_LOOP_BEGIN(count)
                        Light al = GetAdditionalLight(lightIndex, d.ws);
                        float3 lc = al.color * al.distanceAttenuation;
                        diff += lc * saturate(dot(d.n, al.direction));
                        spec += lc * pow(saturate(dot(d.nb, normalize(al.direction + v))), _SpecPow * 0.6);
                    LIGHT_LOOP_END
                }
                #endif
                // camera key: a light mirrored from the view about a slightly tilted up, so wherever the meniscus or a
                // droplet bump faces the game camera a small hot glint shows (the moon alone sits at the wrong angle)
                float3 kl = normalize(reflect(-v, float3(0, 1, 0)) + float3(0.58, 0, -0.4));   // bl5: 0.22/-0.15 lit every flat pool white
                // look4: the key glint only on slopes (meniscus, droplets): on a flat pool facing the camera it lit the whole
                // core as one flat pale sheet (QA #8 "white hole" in view 04); spec capped so a lantern can't white it out either
                float slope = saturate((1 - dot(d.nb, d.n)) * 30);
                spec += _Glint * slope * (ml.color * 0.6 + SampleSH(float3(0, 1, 0))) * pow(saturate(dot(d.nb, normalize(kl + v))), _SpecPow);   // bl6: x fine showed the x11 tiling as a pattern
                float fres = 0.04 + 0.96 * pow(1 - saturate(dot(d.nb, v)), 5);
                float3 env = GlossyEnvironmentReflection(reflect(-v, d.nb), 0.06, 1) * fres * _Sheen;
                float3 col = _PoolCol.rgb * lerp(0.6, 1.1, d.mottle);
                float3 c = col * (diff + _Emit) * pool + min(spec * _Spec + env, 1.2) * wet;
                // no RFFog here: premultiplied output, fog colour would be added where the pool alpha is 0
                return half4(c, pool);
            }
            ENDHLSL
        }
    }
}
