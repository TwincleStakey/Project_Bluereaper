// BossSeal arena floor (11-15, Kido ref: Diablo IV Vessel of Hatred boss platform, ~2x bigger, Korean shamanic seal).
// One shader for the whole floor mesh (built by BSBuilder), zones by distance from the centre:
//   seal disk (r < R1.x)       dark_rock_02 scan + the seal mask: CC0 bagua octagon (palgwae) with the Korean taegeuk
//                              (PD flag) in the middle. R = trigrams/frame (inlay, glowing edges), G = taegeuk red half
//                              (glows), B = blue half (dark obsidian)
//   metal band                 rusty_metal_02, mapped along the ring
//   talisman ring              stone; alternating the Joseon 길상호신부 cinnabar strokes (glow) and the 삼두매 부적
//                              print lines (engraved, faint) standing radially, top outward
//   metal band
//   text ring                  red_sandstone_wall blocks along the ring; carved 부적목판 woodblock characters (engraved)
//   metal rim, stone chamfer   down to the dirt
//   dirt                       red_mud_stones + cracked_red_ground (Poly Haven CC0), darker outward; north of the platform the
//                              floor melts into red haze (the "room goes red and see-through at the back")
// All motifs are the downloaded images (ArtSource/BossSeal/bs_masks.py only cropped/thresholded them). Matte everywhere.
// URP forward lit (main light + shadows, additional lights, fog).
Shader "BossSeal/Ground"
{
    Properties
    {
        _MudA("Mud A albedo", 2D) = "grey" {}
        _MudAN("Mud A normal (GL)", 2D) = "bump" {}
        _MudB("Mud B albedo", 2D) = "grey" {}
        _MudBN("Mud B normal (GL)", 2D) = "bump" {}
        _Stone("Seal stone albedo", 2D) = "grey" {}
        _StoneN("Seal stone normal (GL)", 2D) = "bump" {}
        _StoneAO("Seal stone AO", 2D) = "white" {}
        _Trim("Ring blocks albedo", 2D) = "grey" {}
        _TrimN("Ring blocks normal (GL)", 2D) = "bump" {}
        _Metal("Metal band albedo", 2D) = "grey" {}
        _MetalN("Metal band normal (GL)", 2D) = "bump" {}
        _Seal("Seal mask (R palgwae, G taegeuk red, B taegeuk blue)", 2D) = "black" {}
        _Bujeok("Bujeok strokes", 2D) = "black" {}
        _Hawk("Samdumae lines", 2D) = "black" {}
        _Text("Woodblock text grooves", 2D) = "black" {}

        _R1("Radii: seal, band1 out, talisman out, band2 out", Vector) = (6.3, 6.9, 11.1, 11.6)
        _R2("Radii: text out, rim out, chamfer out, seal mask width", Vector) = (14.2, 14.8, 15.6, 11.6)
        _Tiles("Talisman tiles, text tiles, talisman width m, text width m", Vector) = (12, 28, 3.0, 1.75)

        _MudTint("Mud tint", Color) = (1.0, 0.64, 0.52, 1)
        _StoneTint("Seal stone tint", Color) = (0.8, 0.58, 0.5, 1)
        _TrimTint("Ring block tint", Color) = (0.55, 0.38, 0.32, 1)
        _MetalTint("Metal tint", Color) = (0.62, 0.42, 0.34, 1)
        _Glow("Inlay glow (HDR)", Color) = (2.4, 0.62, 0.14, 1)
        _GlowAmt("Glow: palgwae fill, palgwae edge, taegeuk, bujeok", Vector) = (0.0, 0.75, 0.03, 0.9)
        _Engrave("Engrave: hawk dark, hawk glow, text dark, text glow", Vector) = (0.45, 0.07, 0.55, 0.03)
        _Pulse("Pulse: amount, speed, ripple per m", Vector) = (0.18, 1.1, 0.35, 0)
        _Haze("North haze colour (HDR) / strength", Color) = (0.62, 0.08, 0.035, 0.7)
        _HazeZ("Haze: z start, z full, outer dark r start, r end", Vector) = (20, 50, 30, 56)
        _NrmStr("Normal strength", Range(0, 2)) = 1
        _Smooth("Smoothness (matte)", Range(0, 1)) = 0.06
        _MetalSmooth("Metal band smoothness", Range(0, 1)) = 0.22
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_MudA); SAMPLER(sampler_MudA); TEXTURE2D(_MudAN); TEXTURE2D(_MudB); TEXTURE2D(_MudBN);
        TEXTURE2D(_Stone); TEXTURE2D(_StoneN); TEXTURE2D(_StoneAO);
        TEXTURE2D(_Trim); TEXTURE2D(_TrimN); TEXTURE2D(_Metal); TEXTURE2D(_MetalN);
        TEXTURE2D(_Seal); SAMPLER(sampler_Seal);
        TEXTURE2D(_Bujeok); TEXTURE2D(_Hawk); TEXTURE2D(_Text);
        CBUFFER_START(UnityPerMaterial)
        float4 _MudA_ST, _R1, _R2, _Tiles, _MudTint, _StoneTint, _TrimTint, _MetalTint, _Glow, _GlowAmt, _Engrave, _Pulse, _Haze, _HazeZ;
        float _NrmStr, _Smooth, _MetalSmooth;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attr { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct V2F { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; float fog : TEXCOORD2; };

            V2F vert(Attr a)
            {
                V2F o;
                o.positionWS = TransformObjectToWorld(a.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(a.normalOS);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            float H21(float2 p) { float3 p3 = frac(float3(p.xyx) * 0.1031); p3 += dot(p3, p3.yzx + 33.33); return frac((p3.x + p3.y) * p3.z); }
            float VN(float2 p)
            {
                float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(H21(i), H21(i + float2(1, 0)), f.x), lerp(H21(i + float2(0, 1)), H21(i + float2(1, 1)), f.x), f.y);
            }
            float FBM(float2 p) { return VN(p) * 0.55 + VN(p * 2.07 + 11.3) * 0.3 + VN(p * 4.3 + 3.7) * 0.15; }
            float Luma(float3 c) { return dot(c, float3(0.2126, 0.7152, 0.0722)); }

            // sample with explicit gradients (polar uvs jump at tile seams / the angle wrap; the gradients do not)
            #define SG(tex, uv, gx, gy) SAMPLE_TEXTURE2D_GRAD(tex, sampler_MudA, uv, gx, gy)
            #define SM(tex, uv, gx, gy) SAMPLE_TEXTURE2D_GRAD(tex, sampler_Seal, uv, gx, gy)

            float3 TS(float4 packed) { return UnpackNormalScale(packed, _NrmStr); }

            // band weight with a 3 cm soft edge
            float Band(float r, float a, float b) { return smoothstep(a - 0.03, a + 0.03, r) * (1 - smoothstep(b - 0.03, b + 0.03, r)); }

            half4 frag(V2F i) : SV_Target
            {
                float3 ws = i.positionWS;
                float2 w = ws.xz;
                float r = length(w);
                float th = atan2(w.x, w.y);                     // 0 = north, + clockwise seen from above
                float2 radial = r > 1e-4 ? w / r : float2(0, 1);
                float2 tang = float2(radial.y, -radial.x);        // direction of increasing th
                // screen-space footprint in metres (for the polar gradients)
                float2 dwx = ddx(w), dwy = ddy(w);
                float2 dRx = float2(dot(dwx, tang), dot(dwx, radial)), dRy = float2(dot(dwy, tang), dot(dwy, radial));   // (arc m, radial m)

                float3 N = normalize(i.normalWS);
                float t = _Time.y;

                // ------------------------------------------------ dirt (everywhere, the rings cover it)
                float2 uvA = w / 4.2, uvB = float2(w.x * 0.8 - w.y * 0.6, w.x * 0.6 + w.y * 0.8) / 5.7 + 0.37;
                float bm = smoothstep(0.38, 0.66, FBM(w * 0.05 + 3.1));
                float3 alb = lerp(SG(_MudA, uvA, ddx(uvA), ddy(uvA)).rgb, SG(_MudB, uvB, ddx(uvB), ddy(uvB)).rgb, bm);
                float3 nA = TS(SG(_MudAN, uvA, ddx(uvA), ddy(uvA)));
                float3 nB = TS(SG(_MudBN, uvB, ddx(uvB), ddy(uvB))); nB.xy = float2(nB.x * 0.8 + nB.y * 0.6, -nB.x * 0.6 + nB.y * 0.8);
                float3 nts = normalize(lerp(nA, nB, bm));
                alb = lerp(alb, Luma(alb).xxx, 0.25) * _MudTint.rgb * (0.85 + (FBM(w * 0.11 + 9.0) - 0.5) * 0.5);
                // soot ring hugging the platform foot
                alb *= lerp(0.55, 1.0, smoothstep(_R2.z, _R2.z + 2.2, r + (FBM(w * 0.4) - 0.5) * 1.2));
                bool polarT = false;                              // tangent frame: world (x, z) or polar (tang, radial)
                float smooth = _Smooth;
                float3 emis = 0;

                // ------------------------------------------------ platform zones
                float wSeal = 1 - smoothstep(_R1.x - 0.03, _R1.x + 0.03, r);
                float wB1 = Band(r, _R1.x, _R1.y);
                float wTal = Band(r, _R1.y, _R1.z);
                float wB2 = Band(r, _R1.z, _R1.w);
                float wTxt = Band(r, _R1.w, _R2.x);
                float wRim = Band(r, _R2.x, _R2.y);
                float wCh = Band(r, _R2.y, _R2.z);
                float wPlat = saturate(wSeal + wB1 + wTal + wB2 + wTxt + wRim + wCh);
                float pulse = 1 + _Pulse.x * sin(t * _Pulse.y - r * _Pulse.z);

                // stone for the seal disk and the talisman ring (world mapped)
                float2 uvS = w / 3.6;
                float3 stone = SG(_Stone, uvS, ddx(uvS), ddy(uvS)).rgb;
                stone = lerp(stone, Luma(stone).xxx, 0.3) * _StoneTint.rgb;
                float3 stoneN = TS(SG(_StoneN, uvS, ddx(uvS), ddy(uvS)));
                float stoneAO = SG(_StoneAO, uvS, ddx(uvS), ddy(uvS)).r;
                stone *= lerp(0.6, 1.0, stoneAO);

                // ring-mapped textures: u along the arc (metres / tile), v across the band
                float rm1 = (_R1.x + _R1.y) * 0.5, rm2 = (_R1.z + _R1.w) * 0.5, rm3 = (_R1.w + _R2.x) * 0.5, rm4 = (_R2.x + _R2.z) * 0.5;
                // metal bands: integer tiles round the ring so the wrap at th = +-pi is seamless
                float nM1 = round(6.2831853 * rm1 / 1.6), nM2 = round(6.2831853 * rm2 / 1.6);
                float2 uvM1 = float2(th / 6.2831853 * nM1, (r - _R1.x) / 1.6), uvM2 = float2(th / 6.2831853 * nM2, (r - _R1.z) / 1.6);
                float2 gM1x = dRx * float2(nM1 / (6.2831853 * max(r, 0.1)), 1 / 1.6), gM1y = dRy * float2(nM1 / (6.2831853 * max(r, 0.1)), 1 / 1.6);
                float2 gM2x = dRx * float2(nM2 / (6.2831853 * max(r, 0.1)), 1 / 1.6), gM2y = dRy * float2(nM2 / (6.2831853 * max(r, 0.1)), 1 / 1.6);
                float3 metal1 = SG(_Metal, uvM1, gM1x, gM1y).rgb * _MetalTint.rgb, metal2 = SG(_Metal, uvM2, gM2x, gM2y).rgb * _MetalTint.rgb;
                float3 metalN1 = TS(SG(_MetalN, uvM1, gM1x, gM1y)), metalN2 = TS(SG(_MetalN, uvM2, gM2x, gM2y));
                // ring blocks (text ring, rim, chamfer)
                float nT = round(6.2831853 * rm3 / 2.4);
                float2 uvT = float2(th / 6.2831853 * nT, (r - _R1.w) / 2.6);
                float2 gTx = dRx * float2(nT / (6.2831853 * max(r, 0.1)), 1 / 2.6), gTy = dRy * float2(nT / (6.2831853 * max(r, 0.1)), 1 / 2.6);
                float3 trim = SG(_Trim, uvT, gTx, gTy).rgb;
                trim = lerp(trim, Luma(trim).xxx, 0.35) * _TrimTint.rgb;
                float3 trimN = TS(SG(_TrimN, uvT, gTx, gTy));
                float nR = round(6.2831853 * rm4 / 1.6);
                float2 uvR = float2(th / 6.2831853 * nR + 0.5, (r - _R2.x) / 1.6);
                float2 gRx = dRx * float2(nR / (6.2831853 * max(r, 0.1)), 1 / 1.6), gRy = dRy * float2(nR / (6.2831853 * max(r, 0.1)), 1 / 1.6);
                float3 rimMetal = SG(_Metal, uvR, gRx, gRy).rgb * _MetalTint.rgb * 0.8;
                float3 rimN = TS(SG(_MetalN, uvR, gRx, gRy));
                float3 cham = SG(_Trim, uvR * float2(1, 1.6), gRx, gRy).rgb * _TrimTint.rgb * 0.7;

                // ---- seal: palgwae + taegeuk
                float2 uvSeal = float2(w.x, w.y) / _R2.w + 0.5;
                float2 gSx = ddx(uvSeal), gSy = ddy(uvSeal);
                float3 sm0 = SM(_Seal, uvSeal, gSx, gSy).rgb;
                float3 sm1 = SM(_Seal, uvSeal, gSx * 2.5, gSy * 2.5).rgb;             // blurred mips for the inlay edges
                float inSeal = step(0, uvSeal.x) * step(uvSeal.x, 1) * step(0, uvSeal.y) * step(uvSeal.y, 1);   // the clamp would smear the border strokes
                sm0 *= inSeal; sm1 *= inSeal;
                float edgeR = saturate(abs(sm0.r - sm1.r) * 3.0) * saturate(sm0.r * 2);   // inner rim of each inlay only
                float edgeG = saturate(abs(sm0.g - sm1.g) * 3.0);
                float3 sealAlb = stone;
                sealAlb = lerp(sealAlb, metal1 * 0.9, sm0.r);                    // trigrams: rusted metal inlay
                sealAlb = lerp(sealAlb, float3(0.05, 0.035, 0.035), sm0.b);       // blue half: obsidian
                sealAlb = lerp(sealAlb, sealAlb * 0.5, sm0.g);
                float3 sealE = _Glow.rgb * (sm0.r * _GlowAmt.x + edgeR * _GlowAmt.y) + _Glow.rgb * (sm0.g * _GlowAmt.z + edgeG * 0.5);

                // ---- talisman ring: N tiles round, odd = bujeok, even = samdumae
                float nTal = _Tiles.x;
                float slot = th / 6.2831853 * nTal;
                float k = floor(slot + 0.5);                                         // tile index, centred at k
                float arc = (slot - k) * 6.2831853 / nTal * r;                       // metres from the tile centre along the arc
                float tv = (r - _R1.y - 0.08) / (_R1.z - _R1.y - 0.16);
                float2 uvTal = float2(arc / _Tiles.z + 0.5, tv);
                float2 gTalx = dRx * float2(1 / _Tiles.z, 1 / (_R1.z - _R1.y - 0.16)), gTaly = dRy * float2(1 / _Tiles.z, 1 / (_R1.z - _R1.y - 0.16));
                float inTal = smoothstep(0, 0.12, uvTal.x) * smoothstep(1, 0.88, uvTal.x) * smoothstep(0, 0.08, tv) * smoothstep(1, 0.92, tv);
                bool isBujeok = fmod(abs(k), 2) < 0.5;
                float mB = SM(_Bujeok, uvTal, gTalx, gTaly).r * inTal;
                float mH = SM(_Hawk, uvTal, gTalx, gTaly).r * inTal;
                float3 talAlb = stone * 0.92;
                float3 talE = 0;
                if (isBujeok) { talAlb = lerp(talAlb, talAlb * 0.3, mB); talE = _Glow.rgb * mB * _GlowAmt.w; }
                else { talAlb *= 1 - _Engrave.x * mH; talE = _Glow.rgb * mH * _Engrave.y; }

                // ---- text ring
                float nTx = _Tiles.y;
                float slot3 = th / 6.2831853 * nTx;
                float k3 = floor(slot3 + 0.5);
                float arc3 = (slot3 - k3) * 6.2831853 / nTx * r;
                float tv3 = (r - _R1.w - 0.12) / (_R2.x - _R1.w - 0.24);
                float2 uvTx = float2(arc3 / _Tiles.w + 0.5, tv3);
                float2 gTxx = dRx * float2(1 / _Tiles.w, 1 / (_R2.x - _R1.w - 0.24)), gTxy = dRy * float2(1 / _Tiles.w, 1 / (_R2.x - _R1.w - 0.24));
                float inTx = smoothstep(0.02, 0.16, uvTx.x) * smoothstep(0.98, 0.84, uvTx.x) * smoothstep(0.02, 0.1, tv3) * smoothstep(0.98, 0.9, tv3);
                float mT = SM(_Text, uvTx, gTxx, gTxy).r * inTx;
                float3 txtAlb = trim * (1 - _Engrave.z * mT);
                float3 txtE = _Glow.rgb * mT * _Engrave.w;

                // ---- combine zones
                float3 platAlb = sealAlb * wSeal + metal1 * wB1 + talAlb * wTal + metal2 * wB2 + txtAlb * wTxt + rimMetal * wRim + cham * wCh;
                float3 platE = (sealE * wSeal + talE * wTal + txtE * wTxt) * pulse;
                #define PW(nn) float3(nn.x * tang.x + nn.y * radial.x, nn.x * tang.y + nn.y * radial.y, nn.z)
                float3 platNw = normalize(stoneN * (wSeal + wTal) + PW(metalN1) * wB1 + PW(metalN2) * wB2 + PW(trimN) * (wTxt + wCh) + PW(rimN) * wRim + float3(0, 0, 1e-4));
                alb = lerp(alb, platAlb, wPlat);
                nts = normalize(lerp(nts, platNw, wPlat));
                smooth = lerp(smooth, _MetalSmooth, wB1 + wB2 + wRim);
                emis += platE;

                // ------------------------------------------------ outer dark and the red north haze
                float outer = smoothstep(_HazeZ.z, _HazeZ.w, r);
                float north = smoothstep(_HazeZ.x, _HazeZ.y, w.y) * smoothstep(_R2.z, _R2.z + 6, r);
                alb *= lerp(1.0, 0.25, outer * (1 - north));

                // TBN on the mesh normal (the bevels between the rings shade from the geometry)
                float3 T = normalize(float3(1, 0, 0) - N * N.x);
                float3 n = normalize(T * nts.x + cross(T, N) * nts.y + N * nts.z);

                InputData d = (InputData)0;
                d.positionWS = ws;
                d.positionCS = i.positionCS;
                d.normalWS = n;
                d.viewDirectionWS = GetWorldSpaceNormalizeViewDir(ws);
                d.shadowCoord = TransformWorldToShadowCoord(ws);
                d.fogCoord = i.fog;
                d.bakedGI = SampleSH(n);
                d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                d.shadowMask = half4(1, 1, 1, 1);

                SurfaceData sd = (SurfaceData)0;
                sd.albedo = saturate(alb);
                sd.smoothness = smooth;
                sd.metallic = 0;
                sd.occlusion = 1;
                sd.alpha = 1;
                sd.emission = emis;
                sd.normalTS = float3(0, 0, 1);
                half4 col = UniversalFragmentPBR(d, sd);
                col.rgb = MixFog(col.rgb, i.fog);
                // haze after fog: the back of the room dissolves into a dim red glow
                float hz = north * _Haze.a;
                col.rgb = lerp(col.rgb, _Haze.rgb * (0.6 + 0.4 * FBM(w * 0.06 + float2(0, t * 0.05))), hz);
                return col;
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 vert(float4 p : POSITION) : SV_POSITION { return TransformObjectToHClip(p.xyz); }
            half4 frag() : SV_Target { return 0; }
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
    }
}
