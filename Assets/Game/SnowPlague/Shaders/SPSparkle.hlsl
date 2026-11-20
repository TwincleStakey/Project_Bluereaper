#ifndef SP_SPARKLE_INCLUDED
#define SP_SPARKLE_INCLUDED
// r13 (Kido 11-13: "눈에 조그마하게 빛나는거 나쁘진 않은데 너무 많고 일정해서 이상해"): snow glints, sparse and irregular.
// The old field lit ~28 % of a 7 cm grid everywhere, so it read as an even sprinkle. Here:
//  - a warped fbm mask (6-15 m patches, ~20 % of the snow) holds almost all glints, broken up by a finer 1-2 m mask;
//    outside the patches only a rare stray is left
//  - cells are fully jittered (no grid rows), two sizes (fine ~6 cm, rare coarse ~16 cm)
//  - intensity is skewed (pow): most glints dim, a few hot
//  - each glint shows for a narrow band of view directions (twinkles as the camera moves)
// The main-light shadow is applied by SPLit (sparkle x shadowAttenuation), so the patches only show in the moonlit snow.
// Needs SPHashI / SPNoiseI / SPFbmI (SPCommon.hlsl). Returns 0..~1.6 before the material's _Sparkle.

float SPSparkleCell(float2 xz, float scale, float density, float3 viewWS, int seed, float rad)
{
    float2 sc = xz * scale;
    float2 cid = floor(sc);
    int2 ic = (int2)cid + int2(seed * 31, seed * 17);
    float h0 = SPHashI(ic), h1 = SPHashI(ic + int2(5, 9)), h2 = SPHashI(ic + int2(11, 3)), h3 = SPHashI(ic + int2(7, 23));
    if (h1 > density) return 0;                                  // empty cell
    float2 cp = frac(sc) - (0.15 + float2(h0, h2) * 0.7);        // centre anywhere in the cell (no rows)
    float r = rad * lerp(0.45, 1.15, h3);                        // size varies per glint
    float dot1 = 1 - smoothstep(r * 0.35, r, length(cp));
    float tw = frac(h0 * 37.1 + dot(viewWS, float3(5.3, 9.1, 7.7)) * (0.6 + h2));
    float flash = smoothstep(0.88, 0.96, tw);
    float amp = 0.35 + 1.5 * h3 * h3 * h3;                       // mostly dim, a few hot
    return dot1 * flash * amp;
}

// mask: the 0..1 patch weight (also usable to tint), returned for debugging
float SPSparkleField(float2 xz, float3 viewWS, out float mask)
{
    float2 w = float2(SPNoiseI(xz * 0.045 + 3.1), SPNoiseI(xz * 0.045 + 7.7)) - 0.5;
    float m = SPFbmI(xz * 0.085 + w * 2.6 + 11.3);
    float patch = smoothstep(0.47, 0.66, m);                     // irregular 6-15 m patches (r13b: wider, a0 left almost none)
    float brk = SPNoiseI(xz * 0.7 + w * 3.0 + 2.2);
    patch *= smoothstep(0.2, 0.65, brk);                        // break each patch into clumps and gaps
    mask = patch;
    float dFine = lerp(0.012, 0.24, patch);                      // strays outside, up to 16 % of cells inside
    float dCoarse = lerp(0.002, 0.07, patch * patch);
    float g = SPSparkleCell(xz, 16.0, dFine, viewWS, 1, 0.22);
    g += 1.4 * SPSparkleCell(xz, 6.0, dCoarse, viewWS, 2, 0.16);
    return g;
}
float SPSparkleField(float2 xz, float3 viewWS) { float m; return SPSparkleField(xz, viewWS, m); }
#endif
