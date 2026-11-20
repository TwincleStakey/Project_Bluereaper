// 11-13 r9 night pass (Kido: "make 폐촌 properly dark fantasy, glow emphasised, moonlight shafts, never just dirt").
// References: Downloads/scene-refs/abandoned-village/darkfantasy (BRIEF.md: Tarae ash field / hanok yard, POE2, D4,
// V Rising, Hades II). What is taken from them:
//   - wet ground: dark glossy puddles that catch the moon and the lantern light (Tarae ash field), 0.8-3 m, a few per screen
//   - the moon broken up by drifting cloud / branch shadow on the ground (light cookie on the moonlight), so the floor
//     reads as pools of cold light and dark instead of one evenly lit sheet
//   - ash flakes drifting through the light (Tarae, D4 fog fires)
// All flat or airborne, nothing blocks movement.
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static partial class AVBuilder
{
    // a grey RGB, irregular soft-edged alpha blob: the shape of one puddle (noise-warped, a few satellite drops)
    static Texture2D MakeMask(string name, int res, System.Func<float, float, float> alpha)
    {
        var path = Root + "/Generated/" + name + ".png";
        var tex = new Texture2D(res, res, TextureFormat.RGBA32, false);
        var px = new Color32[res * res];
        for (int j = 0; j < res; j++)
            for (int i = 0; i < res; i++)
            {
                float u = (i + 0.5f) / res, v = (j + 0.5f) / res;
                byte a = (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha(u, v)) * 255f);
                px[j * res + i] = new Color32(200, 200, 200, a);
            }
        tex.SetPixels32(px); tex.Apply();
        File.WriteAllBytes(Path.Combine(Directory.GetParent(Application.dataPath).FullName, path), tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.alphaIsTransparency = true; imp.wrapMode = TextureWrapMode.Clamp; imp.sRGBTexture = true; imp.mipmapEnabled = true;
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static void BuildPuddles(System.Func<float, float, float> G, Transform parent)
    {
        var masks = new Material[3];
        for (int k = 0; k < 3; k++)
        {
            float ox = 17f * k + 3f, oz = 11f * k + 5f;
            MakeMask("AV_PuddleMask_" + k, 256, (u, v) =>
            {
                float x = (u - 0.5f) * 2f, z = (v - 0.5f) * 2f;
                float warp = (Mathf.PerlinNoise(u * 3f + ox, v * 3f + oz) - 0.5f) * 0.7f + (Mathf.PerlinNoise(u * 9f + ox, v * 9f + oz) - 0.5f) * 0.25f;
                float r = Mathf.Sqrt(x * x * (1.3f + 0.4f * k) + z * z) + warp;
                return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.82f, 0.6f, r));
            });
            var m = DecalMat("AV_PuddleWater_" + k, Root + "/Generated/AV_PuddleMask_" + k + ".png", new Color(0.2f, 0.18f, 0.17f, 0.6f), 0.93f, true);   // r9b: near-black read as holes; now a wet darker mud film
            m.SetFloat("_Metallic", 0.2f);
            m.SetFloat("_EnvironmentReflections", 1f); m.SetFloat("_SpecularHighlights", 1f);
            m.renderQueue = (int)RenderQueue.Transparent - 60;   // under the blood
            EditorUtility.SetDirty(m);
            masks[k] = m;
        }
        int n = 0;
        for (int t = 0; t < 4000 && n < 40; t++)
        {
            float x = R(-ArenaHalfX + 2f, ArenaHalfX - 2f), z = R(ArenaMinZ + 2f, ArenaMaxZ - 2f);
            // puddles gather in the low, worn ground: along the streets and in noise-chosen hollows, never evenly spread
            float want = 0.35f * Mathf.Clamp01(1.5f - StreetDist(x, z) / 3f) + Band(Fbm(x, z, 0.06f, 210f, 33f), 0.62f, 0.05f) * 0.6f;
            if (rng.NextDouble() > want) continue;
            float s = R(0.9f, 2.6f);
            DecalDir(parent, masks[rng.Next(3)], x, z, s * R(0.9f, 1.6f), s, R(0, 360), G, 0.012f);
            if (rng.NextDouble() < 0.5)   // a smaller one beside it
                DecalDir(parent, masks[rng.Next(3)], x + R(-1.6f, 1.6f) * s, z + R(-1.6f, 1.6f) * s, s * 0.4f, s * 0.35f, R(0, 360), G, 0.012f);
            n++;
        }
        File.AppendAllText(Path.Combine(Directory.GetParent(Application.dataPath).FullName, "ArtSource/AbandonedVillage/av_log.txt"), $"PUDDLES {n}\n");
    }

    // cloud and bare-branch shadow drifting over the ground: a tiling cookie on the moonlight (~70 m per tile)
    static void MoonCookie(Light moon)
    {
        var path = Root + "/Generated/AV_MoonCookie.png";
        const int res = 512;
        var tex = new Texture2D(res, res, TextureFormat.RGBA32, false);
        var px = new Color32[res * res];
        float Tile(float u, float v, float f, float o)   // seamless noise on a torus
        {
            float a = u * Mathf.PI * 2f, b = v * Mathf.PI * 2f;
            return Mathf.PerlinNoise(Mathf.Cos(a) * f + o, Mathf.Sin(a) * f + o * 0.7f) * 0.5f + Mathf.PerlinNoise(Mathf.Cos(b) * f + o * 1.3f, Mathf.Sin(b) * f + o) * 0.5f;
        }
        for (int j = 0; j < res; j++)
            for (int i = 0; i < res; i++)
            {
                float u = i / (float)res, v = j / (float)res;
                float cloud = Tile(u, v, 1.6f, 10f) * 0.65f + Tile(u, v, 4.1f, 40f) * 0.35f;
                float lit = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.42f, 0.6f, cloud));
                // long soft streaks, as if light falls through gaps between eaves and dead branches
                float streak = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 0.75f, Mathf.PerlinNoise(u * 18f + v * 3f, v * 2f + 7f)));
                float k = Mathf.Clamp01(0.6f + 0.4f * lit) * (1f - 0.2f * streak * (1f - lit));   // r9b: 0.28 floor made whole screens black
                byte c = (byte)Mathf.RoundToInt(k * 255f);
                px[j * res + i] = new Color32(c, c, c, c);
            }
        tex.SetPixels32(px); tex.Apply();
        File.WriteAllBytes(Path.Combine(Directory.GetParent(Application.dataPath).FullName, path), tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.wrapMode = TextureWrapMode.Repeat; imp.sRGBTexture = false; imp.textureType = TextureImporterType.Default; imp.SaveAndReimport();
        moon.cookie = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        var ad = moon.GetComponent<UniversalAdditionalLightData>() ?? moon.gameObject.AddComponent<UniversalAdditionalLightData>();
        ad.lightCookieSize = new Vector2(34f, 34f);   // r10: 1-4 m dapples instead of 10 m clouds
        ad.lightCookieOffset = new Vector2(13f, 7f);
    }

    // grey ash flakes drifting down through the whole arena, catching the warm light where they pass a fire
    static void BuildAsh(Transform parent)
    {
        var go = new GameObject("AshFall"); go.transform.SetParent(parent, false); go.transform.position = new Vector3(0, 9f, -3.5f);
        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main; main.loop = true; main.startLifetime = new ParticleSystem.MinMaxCurve(9f, 14f); main.startSpeed = 0f; main.maxParticles = 1400;
        main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.1f); main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.75f, 0.72f, 0.7f, 0.55f), new Color(0.45f, 0.43f, 0.42f, 0.4f));
        main.simulationSpace = ParticleSystemSimulationSpace.World; main.prewarm = true; main.gravityModifier = 0f;
        var em = ps.emission; em.rateOverTime = 120f;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(110f, 2f, 110f);
        var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(0.3f, 0.8f); vel.y = new ParticleSystem.MinMaxCurve(-0.9f, -0.5f); vel.z = new ParticleSystem.MinMaxCurve(-0.2f, 0.2f);
        var no = ps.noise; no.enabled = true; no.strength = 0.4f; no.frequency = 0.3f; no.quality = ParticleSystemNoiseQuality.Low;
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.1f), new GradientAlphaKey(1, 0.85f), new GradientAlphaKey(0, 1) });
        col.color = g;
        var r = go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = Particle("AV_Ash", T("AV_Glow.png"), new Color(0.62f, 0.6f, 0.58f, 0.8f), false);
        r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        ps.Simulate(14f, true, true); ps.Play();
    }

    // a dead campfire still smouldering: warm low glow, a few sparks rising, flickering
    static void Smoulder(Vector3 p, Transform parent)
    {
        var go = new GameObject("Smoulder"); go.transform.SetParent(parent, false); go.transform.position = p + Vector3.up * 0.15f;
        var es = go.AddComponent<ParticleSystem>();
        var m = es.main; m.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.8f); m.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.4f);
        m.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.07f); m.maxParticles = 30; m.simulationSpace = ParticleSystemSimulationSpace.World;
        var e = es.emission; e.rateOverTime = 8f;
        var sh = es.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 0.45f; sh.rotation = new Vector3(-90, 0, 0);
        var n = es.noise; n.enabled = true; n.strength = 0.5f; n.frequency = 1.1f; n.quality = ParticleSystemNoiseQuality.Low;
        var col = es.colorOverLifetime; col.enabled = true;
        var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.7f, 0.3f), 0), new GradientColorKey(new Color(0.9f, 0.25f, 0.08f), 1) }, new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.1f), new GradientAlphaKey(0, 1) });
        col.color = g;
        var r = go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = Particle("AV_Ember", T("AV_Glow.png"), new Color(1f, 0.55f, 0.18f, 1f), true); r.shadowCastingMode = ShadowCastingMode.Off;
        es.Play();
        PointLight(p + Vector3.up * 0.3f, Hex("#b8381a"), 1.0f, 3.2f, go.transform, false, true);   // r13: dull red, not a glowing coin
    }

    // r14 (approver: central screens empty / near-black; refs: Tarae ash field, POE2 burnt village): the remains of burnt
    // houses inside the yard, all flat - a soot-black footprint, a broken line of sunk foundation stones round it with
    // gaps, charred planks and roof-tile shards lying in it, sometimes still smouldering. ~9 of them, 13 m+ apart.
    static void BuildBurntFootprints(System.Func<float, float, float> G, Transform parent)
    {
        var root = new GameObject("BurntFootprints").transform; root.SetParent(parent, false);
        MakeMask("AV_SootMask", 256, (u, v) =>
        {
            float x = Mathf.Abs(u - 0.5f) * 2f, z = Mathf.Abs(v - 0.5f) * 2f;
            float edge = Mathf.Max(x, z) + (Mathf.PerlinNoise(u * 6f + 3f, v * 6f + 9f) - 0.5f) * 0.35f + (Mathf.PerlinNoise(u * 19f, v * 19f) - 0.5f) * 0.12f;
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.98f, 0.7f, edge)) * (0.75f + 0.25f * Mathf.PerlinNoise(u * 11f + 40f, v * 11f));
        });
        var soot = DecalMat("AV_Soot", Root + "/Generated/AV_SootMask.png", new Color(0.11f, 0.1f, 0.095f, 0.65f), 0.05f, true);
        soot.renderQueue = (int)RenderQueue.Transparent - 70; EditorUtility.SetDirty(soot);
        var spots = new System.Collections.Generic.List<Vector2>();
        for (int t = 0; t < 2000 && spots.Count < 9; t++)
        {
            var p = new Vector2(R(-38f, 38f), R(-40f, 33f));
            if (p.magnitude < 7f || StreetDist(p.x, p.y) < 4.5f) continue;
            bool ok = true; foreach (var q in spots) if (Vector2.Distance(p, q) < 13f) { ok = false; break; }
            if (ok) spots.Add(p);
        }
        foreach (var c in spots)
        {
            float yaw = R(0, 360), w = R(4.5f, 7f), dpt = R(3.8f, 5.5f);
            var rot = Quaternion.Euler(0, yaw, 0);
            Vector2 L(float fx, float fz) { var v = rot * new Vector3(fx, 0, fz); return c + new Vector2(v.x, v.z); }
            DecalDir(root, soot, c.x, c.y, w + 1.2f, dpt + 1.2f, yaw, G, 0.011f);
            // foundation stones along the outline, gaps where they were carried off
            float per = 2f * (w + dpt), step = R(0.75f, 0.95f);
            for (float s = 0; s < per; s += step * R(0.8f, 1.25f))
            {
                float side = s / per;   // r16: the stones survive mostly on one side, wide gaps elsewhere
                if (rng.NextDouble() < 0.25 + 0.45f * Mathf.Clamp01(Mathf.Sin(side * Mathf.PI * 2f + yaw) * 0.5f + 0.5f)) continue;
                float u = s;
                Vector2 e = u < w ? L(u - w / 2, -dpt / 2) : (u -= w) < dpt ? L(w / 2, u - dpt / 2) : (u -= dpt) < w ? L(w / 2 - u, dpt / 2) : L(-w / 2, dpt / 2 - (u - w));
                e += new Vector2(R(-0.45f, 0.45f), R(-0.45f, 0.45f)); HalfRock(new Vector3(e.x, G(e.x, e.y), e.y), R(0.45f, 1.0f), root);
            }
            for (int i = rng.Next(3, 6); i > 0; i--)
            {
                var p = L(R(-w / 2.5f, w / 2.5f), R(-dpt / 2.5f, dpt / 2.5f));
                var pl = PlacePart("MS:Planks/AV_MS_Planks", new Vector3(p.x, G(p.x, p.y), p.y), R(0, 360), R(0.7f, 1.6f), root);   // r15: single planks at odd angles (the pack read as a grill)
                KeepLow(pl, 0.14f);
                if (pl) foreach (var r in pl.GetComponentsInChildren<Renderer>()) { var m = r.sharedMaterials; for (int k = 0; k < m.Length; k++) m[k] = CharMat(m[k]); r.sharedMaterials = m; }
            }
            for (int i = rng.Next(2, 5); i > 0; i--) { var p = L(R(-w / 2, w / 2), R(-dpt / 2, dpt / 2)); Place("MS:Tile/AV_MS_Tile", new Vector3(p.x, G(p.x, p.y), p.y), R(0, 360), R(0.8f, 1.2f), root); }
            if (rng.NextDouble() < 0.4) { var p = L(R(-1f, 1f), R(-1f, 1f)); Smoulder(new Vector3(p.x, G(p.x, p.y), p.y), root); }
        }
        foreach (var r in root.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.Off;
        File.AppendAllText(Path.Combine(Directory.GetParent(Application.dataPath).FullName, "ArtSource/AbandonedVillage/av_log.txt"), $"BURNT {spots.Count}\n");
    }

    static Material charMat;
    static Material CharMat(Material src)
    {
        if (!src) return src;
        var path = MatDir + "/AV_CharredPlanks.mat";
        if (!charMat) charMat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!charMat) { charMat = new Material(src); AssetDatabase.CreateAsset(charMat, path); }
        charMat.CopyPropertiesFromMaterial(src);
        charMat.SetColor("_BaseColor", new Color(0.16f, 0.13f, 0.11f, 1f)); charMat.SetFloat("_Smoothness", 0.05f);
        EditorUtility.SetDirty(charMat);
        return charMat;
    }
}
