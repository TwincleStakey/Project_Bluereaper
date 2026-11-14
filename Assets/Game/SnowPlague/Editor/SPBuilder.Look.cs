// 설원 역병 look pass (11-11 redo 2, colour / light / VFX worker). Kido rejected the flat cold blue-grey where
// everything was desaturated and the blood read black. Target = tarae ss5:
//   - desaturated warm sepia / brown-grey base (dirt, wood, brush keep a muted brown)
//   - snow a cool off-white and the brightest area; deep dark shadows; dark vignette
//   - strong warm orange lantern glows
//   - blood a saturated bright red, with glowing red embers / sparks around fresh kills.
// Runs from BuildLightAndPost after the volume exists and before the camera is made. Everything it touches already
// exists (materials in `mats`, vignette "Blood" decals, lantern spots).
// NOTE: in Play the RoomRun StageMood volume (priority 1000) replaces ColorAdjustments / SMH / LGG / Vignette / Grain,
// and LookFX (1100) replaces Bloom and saturation. What survives from this profile in Play: Tonemapping, White Balance,
// Split Toning, Color Curves (the red-keeping Hue vs Sat). Editor captures (SPRemote.Shots) mirror StageMood only.
// look1 (11-12, ss5 QA): ColorAdjustments / Bloom here now equal the Play values (StageMood "snow" + LookFX "snow"),
// because SPRemote's capture volume only overrides exposure: before, captures graded at contrast 22 and Play at 50.
// Moon gets a canopy cookie rendered from the downloaded pine prefabs (ThirdParty/Trees), so ~30 % of the arena floor
// sits in big pine shadow; lantern spots are small (2.8 m) pools; the purple wall is a faint grey ground line.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static partial class SPBuilder
{
    static partial void ConfigureLook(VolumeProfile profile, Light moon)
    {
        // never grade another field: a profile shared from outside this folder gets replaced by our own
        var path = profile ? AssetDatabase.GetAssetPath(profile) : null;
        if (!profile || string.IsNullOrEmpty(path) || !path.StartsWith(Root))
        {
            var own = SpecProfile();
            foreach (var v in root.GetComponentsInChildren<Volume>()) if (v.sharedProfile == profile) v.sharedProfile = own;
            Log("LOOK shared profile " + path + " replaced by the snow profile");
            profile = own;
        }
        LookGrade(profile);
        EditorUtility.SetDirty(profile);
        foreach (var c in profile.components) EditorUtility.SetDirty(c);

        LookLights(moon);
        LookCookie(moon);
        LookFog();
        LookVegetation();
        LookBoundary();
        LookLanterns();
        int blood = LookBlood();
        Log($"LOOK grade + warm light + fog + lanterns, blood decals={blood} (embers + glow on each)");
    }

    static T LookComp<T>(VolumeProfile p) where T : VolumeComponent
    {
        if (p.TryGet<T>(out var c)) { c.active = true; return c; }
        c = p.Add<T>(true);
        AssetDatabase.AddObjectToAsset(c, p);
        return c;
    }
    // ------------------------------------------------------------------ volume grade
    // look1: = StageMood "snow" contrast / filter (keep the two in step; Play uses StageMood's, captures use these)
    static readonly float LookContrast = 24f;   // v14 (Kido "타래는 눌려있고 안개 낀 느낌, 우리는 너무 쨍해"): compressed (was 50)
    static readonly Color LookFilter = new Color(0.96f, 0.955f, 0.925f);   // v14: = StageMood snow

    static void LookGrade(VolumeProfile p)
    {
        // Neutral keeps the hues where we put them (ACES would yellow the lanterns and darken the mids everyone tuned)
        LookComp<Tonemapping>(p).mode.Override(TonemappingMode.Neutral);

        // look1: contrast / filter / saturation = the Play grade (StageMood "snow" contrast + filter, LookFX "snow"
        // saturation -30): SPRemote's capture volume only overrides exposure, so captures used to grade at contrast 22
        var ca = LookComp<ColorAdjustments>(p);
        ca.postExposure.Override(-0.1f);
        ca.contrast.Override(LookContrast);
        ca.colorFilter.Override(LookFilter);
        ca.saturation.Override(-40f);   // v14 = LookFX snow
        ca.hueShift.overrideState = false;

        // look1: 8 -> 3 (QA #2: warm light on tan dirt read pink; ss5 snow is neutral grey)
        var wb = LookComp<WhiteBalance>(p);
        wb.temperature.Override(14f); wb.tint.Override(-3f);   // v14 (ss5 mids are sepia R>G>B; ours read magenta, B>G): warmer, tint toward green

        // r10 (ss5): sepia lives in the darks and mids (dirt, wood, brush = warm brown-grey), the brightest tones (snow)
        // stay a neutral off-white. look1: darks less sepia (0.56/0.5/0.43): the shadow colour carries a little blue now
        var st = LookComp<SplitToning>(p);
        st.shadows.Override(new Color(0.56f, 0.51f, 0.45f));   // v14: sepia darks
        st.highlights.Override(new Color(0.5f, 0.5f, 0.5f));
        st.balance.Override(10f);

        // Hue vs Sat (0.5 = x1, 1 = x2): look1 red x1.16 / crimson x1.12 (r12 x1.32 / x1.28 turned the blood #c01010; ss5
        // blood is maroon), orange x1.06, yellow..blue x0.6-0.7
        var cc = LookComp<ColorCurves>(p);
        cc.hueVsSat.Override(new TextureCurve(new[]
        {
            new Keyframe(0f, 0.58f), new Keyframe(0.04f, 0.56f), new Keyframe(0.085f, 0.53f), new Keyframe(0.14f, 0.38f),
            new Keyframe(0.25f, 0.32f), new Keyframe(0.5f, 0.3f), new Keyframe(0.68f, 0.32f), new Keyframe(0.86f, 0.5f), new Keyframe(0.95f, 0.56f),
        }, 0.5f, true, new Vector2(0, 1)));
        // Hue vs Hue (0.5 = no shift): pull red-orange a few degrees toward red so fresh blood never reads brown
        cc.hueVsHue.Override(new TextureCurve(new[] { new Keyframe(0f, 0.5f), new Keyframe(0.035f, 0.488f), new Keyframe(0.07f, 0.5f) }, 0.5f, true, new Vector2(0, 1)));

        // = LookFX "snow" (Play): only the fire boxes and embers bloom
        var bl = LookComp<Bloom>(p);
        bl.threshold.Override(1.15f); bl.intensity.Override(0.9f); bl.scatter.Override(0.6f); bl.clamp.Override(30f);
        bl.tint.Override(new Color(1f, 0.82f, 0.64f)); bl.highQualityFiltering.Override(false);

        // look1 (QA #3c): #0b0c0e, 0.42. In Play StageMood's own vignette (0.42, near black) replaces it
        var vg = LookComp<Vignette>(p);
        vg.color.Override(new Color(0.043f, 0.047f, 0.055f)); vg.intensity.Override(0.42f); vg.smoothness.Override(0.5f);
        vg.center.Override(new Vector2(0.5f, 0.48f)); vg.rounded.Override(false);

        // v14: film-like compressed range: blacks lifted into a cool grey, whites rolled off, so the frame reads
        // "pressed down" under a veil like ss5 instead of crisp
        var lgg = LookComp<LiftGammaGain>(p);
        lgg.lift.Override(new Vector4(0.99f, 1.0f, 1.03f, 0.045f));
        lgg.gain.Override(new Vector4(1f, 0.995f, 0.98f, -0.03f));   // v14b: -0.08 pressed every highlight down
        vg.intensity.Override(0.5f); vg.smoothness.Override(0.6f);

        var fg = LookComp<FilmGrain>(p);
        fg.type.Override(FilmGrainLookup.Medium2); fg.intensity.Override(0.18f);
    }

    // ------------------------------------------------------------------ moon, ambient, rim
    static void LookLights(Light moon)
    {
        if (moon)
        {
            // look1: neutral moon (QA #2: lit snow neutral grey, the blue goes in the shadow / ambient)
            moon.color = new Color(0.82f, 0.82f, 0.83f);
            moon.shadowStrength = 0.8f;   // q14 (QA: tree shadows pure black; ss5 strong but not crushed): was 0.92   // tr13 (Kido: ss5 has stronger shadows)
            moon.intensity *= 1.12f;       // tr14: lit snow brighter (ambient is lowered in StageMood "snow")
        }
        // trilight. look1: up = slate blue (#5a6170 hue: shadowed snow is where the blue lives), sides = neutral grey
        // (was warm grey-brown: with the warm light it made the dirt pink), down = neutral snow bounce
        RenderSettings.ambientSkyColor = new Color(0.25f, 0.285f, 0.36f);   // q14: cooler, a little more fill so shadows go blue-grey (was 0.235/0.255/0.3)
        RenderSettings.ambientEquatorColor = new Color(0.33f, 0.33f, 0.335f);
        RenderSettings.ambientGroundColor = new Color(0.47f, 0.47f, 0.48f);
        var field = root.GetComponent<SnowPlague.SPField>();
        if (field)
        {
            field.rimWarm = new Color(1.8f, 0.7f, 0.24f); field.rimWarmStrength = 0.26f;   // look1: 0.34 -> 0.26 with the smaller lantern pools
            field.rimCold = new Color(0.5f, 0.54f, 0.6f); field.rimColdStrength = 0.1f;
            field.flickerAmount = 0.12f;
        }
    }

    // ------------------------------------------------------------------ canopy cookie on the moon (QA #3a)
    // A big pine-canopy shadow over ~30 % of the floor, like ss5's tree shadows: rendered once from the downloaded
    // tree prefabs (ThirdParty/Trees: Japanese black pine, Sketchfab pines / firs / dead conifers) as black cut-out
    // silhouettes seen from the moon (orthographic, so the image is their shadow), wrapped so it tiles.
    // Bump CookieVer to re-render (the PNG is kept so builds are stable).
    const int CookieVer = 6;  // q14: softer, lighter canopy shadow   // look2: layer-isolated render, coverage-driven; look3: clumps spread, ~25 %; look4: crowns only; look6: ~22 %
    static readonly Vector2 CookieSize = new Vector2(40f, 20f);   // light-space metres; on the ground ~40 x 47 m (moon 25 deg up)
    static string CookiePath => Root + "/Textures/SP_CanopyCookie_v" + CookieVer + ".png";

    static void LookCookie(Light moon)
    {
        if (!moon) return;
        var abs = Path.Combine(Directory.GetParent(Application.dataPath).FullName, CookiePath);
        if (!File.Exists(abs))
        {
            foreach (var old in Directory.GetFiles(Path.GetDirectoryName(abs), "SP_CanopyCookie_v*.png"))
                AssetDatabase.DeleteAsset(Root + "/Textures/" + Path.GetFileName(old));
            if (!LookRenderCookie(moon.transform.rotation, abs)) return;
            AssetDatabase.ImportAsset(CookiePath, ImportAssetOptions.ForceUpdate);
        }
        var imp = AssetImporter.GetAtPath(CookiePath) as TextureImporter;
        if (imp && (imp.sRGBTexture || imp.wrapMode != TextureWrapMode.Repeat || imp.textureCompression != TextureImporterCompression.CompressedHQ || imp.alphaSource != TextureImporterAlphaSource.None))
        {
            imp.textureType = TextureImporterType.Default; imp.sRGBTexture = false; imp.wrapMode = TextureWrapMode.Repeat;
            imp.alphaSource = TextureImporterAlphaSource.None; imp.mipmapEnabled = true; imp.maxTextureSize = 2048;
            imp.textureCompression = TextureImporterCompression.CompressedHQ;
            imp.SaveAndReimport();
        }
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(CookiePath);
        if (!tex) { Log("LOOK cookie: no texture at " + CookiePath); return; }
        moon.cookie = tex;
        var ad = moon.GetComponent<UniversalAdditionalLightData>();
        if (!ad) ad = moon.gameObject.AddComponent<UniversalAdditionalLightData>();
        ad.lightCookieSize = CookieSize;
        ad.lightCookieOffset = new Vector2(7f, 3f);
        Log($"LOOK cookie {CookiePath} size {CookieSize}");
    }

    static bool LookRenderCookie(Quaternion rot, string abs)
    {
        const string P = Root + "/ThirdParty/Trees/Prefabs/";
        // look2: rendered in the open scene, far below it (y -4000) on layer 31 with the camera culling only that layer.
        // look1's preview scene drew the instanced grass scatter (layer 0 draws reach every camera) and none of the trees.
        const int Layer = 31;
        var O = new Vector3(0f, -4000f, 0f);
        // pines / firs make the mass, the hero pine its cloud pads, dead conifers the bare branch lace
        // look4: no dead conifers and no bark (crowns only): their trunks threw 25-40 m straight pole shadows over the floor
        var pool = new[] { "TR_Pine", "TR_Pine", "TR_Fir", "TR_Fir", "TR_NobleFir" };   /* 11-14: black pine banned (Kido) */
        var prefabs = new Dictionary<string, GameObject>();
        foreach (var n in pool.Distinct()) { var pf = AssetDatabase.LoadAssetAtPath<GameObject>(P + n + ".prefab"); if (pf) prefabs[n] = pf; else Log("LOOK cookie: missing " + P + n); }
        if (prefabs.Count == 0) return false;
        var tmp = new List<Object>();
        var holder = new GameObject("CookieGrove (temp)");
        tmp.Add(holder);
        try
        {
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            var swap = new Dictionary<Material, Material>();
            Material Black(Material src)
            {
                if (src && swap.TryGetValue(src, out var have)) return have;
                var m = new Material(unlit); tmp.Add(m);
                m.SetColor("_BaseColor", Color.black);
                var t = src && src.HasProperty("_BaseMap") ? src.GetTexture("_BaseMap") : null;
                bool cut = t && src.HasProperty("_Cutoff") && (src.IsKeywordEnabled("_ALPHATEST_ON") || src.name.Contains("Needles") || src.name.Contains("Branch"));
                if (cut)
                {
                    m.SetTexture("_BaseMap", t); m.SetFloat("_AlphaClip", 1f); m.SetFloat("_Cutoff", src.GetFloat("_Cutoff"));
                    m.EnableKeyword("_ALPHATEST_ON"); m.renderQueue = (int)RenderQueue.AlphaTest;
                }
                if (!cut && src && src.name.Contains("Bark"))   // look4: bark (trunks, limbs) drawn fully clipped = not drawn
                { m.SetFloat("_AlphaClip", 1f); m.SetFloat("_Cutoff", 1f); m.SetColor("_BaseColor", new Color(0, 0, 0, 0)); m.EnableKeyword("_ALPHATEST_ON"); }
                if (src) swap[src] = m;
                return m;
            }
            var right = rot * Vector3.right; var up = rot * Vector3.up; var fwd = rot * Vector3.forward;
            var rnd = new System.Random(7713);
            float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
            int placed = 0, clumpN = 0;
            // look3: clump centres on a shuffled 4 x 2 grid of cells (jittered), so the tile has no empty band that repeats
            var cells = Enumerable.Range(0, 8).OrderBy(_ => rnd.Next()).ToArray();
            void Clump(int count)
            {
                int cell = cells[clumpN++ % cells.Length];
                var c = new Vector2((cell % 4 + R(0.2f, 0.8f)) * CookieSize.x / 4f - CookieSize.x * 0.5f, (cell / 4 + R(0.2f, 0.8f)) * CookieSize.y / 2f - CookieSize.y * 0.5f);
                for (int i = 0; i < count; i++)
                {
                    var name = pool[rnd.Next(pool.Length)];
                    if (!prefabs.TryGetValue(name, out var pf)) continue;
                    var uv = c + new Vector2(R(-6, 6), R(-3, 3));
                    var g = uv.x * right + uv.y * up; g -= fwd * (g.y / fwd.y);   // ground point (y = 0) seen at uv
                    var yaw = Quaternion.Euler(0, R(0, 360), 0); float s = R(0.85f, 1.35f);
                    for (int dx = -1; dx <= 1; dx++)
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            var go = (GameObject)Object.Instantiate(pf, holder.transform);
                            go.transform.SetPositionAndRotation(O + g + right * (dx * CookieSize.x) + up * (dy * CookieSize.y), yaw);
                            go.transform.localScale = Vector3.one * s;
                            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
                            foreach (var lg in go.GetComponentsInChildren<LODGroup>(true)) lg.enabled = false;
                            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                            { r.sharedMaterials = r.sharedMaterials.Select(Black).ToArray(); r.shadowCastingMode = ShadowCastingMode.Off; }
                        }
                    placed++;
                }
            }

            const int W = 2048, H = 1024;
            var camGo = new GameObject("CookieCam (temp)"); camGo.transform.SetParent(holder.transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false; cam.cullingMask = 1 << Layer;
            cam.orthographic = true; cam.orthographicSize = CookieSize.y * 0.5f;
            cam.transform.SetPositionAndRotation(O - fwd * 150f, rot);
            cam.nearClipPlane = 1f; cam.farClipPlane = 400f;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.white; cam.allowHDR = false; cam.allowMSAA = true;
            var cd = camGo.AddComponent<UniversalAdditionalCameraData>();
            cd.renderPostProcessing = false; cd.renderShadows = false; cd.antialiasing = AntialiasingMode.None;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 }; tmp.Add(rt);
            var read = new Texture2D(W, H, TextureFormat.RGBA32, false, true); tmp.Add(read);
            float[] Render()
            {
                cam.targetTexture = rt; cam.aspect = CookieSize.x / CookieSize.y; cam.Render();
                RenderTexture.active = rt;
                read.ReadPixels(new Rect(0, 0, W, H), 0, 0); read.Apply();
                cam.targetTexture = null; RenderTexture.active = null;
                var px = read.GetPixels();
                var v = new float[W * H];
                for (int i = 0; i < v.Length; i++) v[i] = px[i].r;
                return v;
            }
            float Cover(float[] v) { int d = 0; for (int i = 0; i < v.Length; i += 7) if (v[i] < 0.5f) d++; return d / (v.Length / 7f); }

            // grow the grove clump by clump until 25-35 % of the tile is in canopy shadow
            float[] a = null; float cover = 0f;
            for (int round = 0; round < 10; round++)
            {
                Clump(3);
                a = Render(); cover = Cover(a);
                if (cover >= 0.19f) break;   // look3: 0.27 overshot to 39 %; look6: 0.23 gave 30 % crowns-only, 0.19 for ~22 %
            }
            if (placed == 0 || a == null) return false;

            // soft penumbra (canopy 6-15 m up): two box passes of 3 px (~6 cm each); floor 0.08 = moon shadowStrength 0.92
            var b = new float[W * H];
            void Box(float[] src, float[] dst, int rad, bool horiz)
            {
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        float sum = 0;
                        for (int o = -rad; o <= rad; o++)
                            sum += horiz ? src[y * W + ((x + o + W) % W)] : src[((y + o + H) % H) * W + x];
                        dst[y * W + x] = sum / (2 * rad + 1);
                    }
            }
            for (int pass = 0; pass < 2; pass++) { Box(a, b, 5, true); Box(b, a, 5, false); }   // q14: softer penumbra (was 3 px)
            var outPx = new Color32[W * H];
            for (int i = 0; i < a.Length; i++)
            {
                byte bv = (byte)Mathf.RoundToInt(Mathf.Clamp01(Mathf.Lerp(0.22f, 1f, a[i])   /* q14: cookie floor 0.08 -> 0.22, the canopy shadow is no longer black */) * 255f);
                outPx[i] = new Color32(bv, bv, bv, 255);
            }
            var outTex = new Texture2D(W, H, TextureFormat.RGBA32, false, true); tmp.Add(outTex);
            outTex.SetPixels32(outPx); outTex.Apply();
            File.WriteAllBytes(abs, outTex.EncodeToPNG());
            Log($"LOOK cookie rendered: {placed} trees (x9 wrap copies), canopy shadow {100f * cover:0}% of the tile");
            return true;
        }
        catch (System.Exception e) { Log("LOOK cookie FAILED " + e.Message); return false; }
        finally
        {
            RenderTexture.active = null;
            foreach (var o in tmp) if (o) Object.DestroyImmediate(o);
        }
    }

    // ------------------------------------------------------------------ fog: darker, so it stops lifting the blacks (QA #1)
    static void LookFog()
    {
        void C(string mat, string prop, Color c) { if (mats.TryGetValue(mat, out var m) && m) { m.SetColor(prop, c); EditorUtility.SetDirty(m); } }
        void F(string mat, string prop, float v) { if (mats.TryGetValue(mat, out var m) && m) { m.SetFloat(prop, v); EditorUtility.SetDirty(m); } }
        // look1: near haze #3a3d42 (was a light warm grey 0.56 that lifted the blacks). look8 (QA amateur #7): that dark
        // colour x the scrolling fog noise drew blurry dark smudges over the snow / ice in the far corner of 07 and 01
        // ("dirt on the lens"). Now a mid grey near the snow shadow value, a much flatter noise, a later start and a
        // lower cap, so the haze only greys the distance evenly.
        C("SP_HeightFog", "_NearCol", new Color(0.34f, 0.36f, 0.4f));   // v14: cool grey-blue veil
        C("SP_HeightFog", "_FarCol", new Color(0.08f, 0.075f, 0.07f));
        F("SP_HeightFog", "_Density", 0.022f);   // v14 (was 0.014)
        F("SP_HeightFog", "_NoiseAmount", 0.12f);
        F("SP_HeightFog", "_Start", 8f);   // v14: the veil starts in front of the player (was 30: only the distance)
        F("SP_HeightFog", "_MaxFog", 0.6f);   // v14 (was 0.45)
        // low sheets: neutral mid grey and thinner over the arena (they hazed the brush and dirt to grey)
        C("SP_FogSheetLow", "_Col", new Color(0.46f, 0.47f, 0.48f));
        C("SP_FogSheetMid", "_Col", new Color(0.42f, 0.43f, 0.44f));
        C("SP_FogSheetHigh", "_Col", new Color(0.38f, 0.39f, 0.4f));
        F("SP_FogSheetLow", "_Density", 0.18f); F("SP_FogSheetMid", "_Density", 0.1f);   // v14 (was 0.1 / 0.06)
        C("SP_FogSea", "_Col", new Color(0.5f, 0.5f, 0.5f));
    }

    // ------------------------------------------------------------------ pine canopy cards (QA amateur #6)
    // "Shattered glass": up-facing needle cards went fully white (snow cushion over the whole card, lit straight by the
    // moon) with hard edges. Less and patchier snow on the needles, more wrap (soft, translucent-looking shading),
    // no gloss, half the rim. Only the SP_* field materials; the shared ThirdParty/Trees TR_* copies are not touched.
    static void LookVegetation()
    {
        int n = 0;
        foreach (var name in new[] { "SP_SK_HeroNeedles", "SP_SK_Branch", "SP_SK_FG_HeroNeedles", "SP_SK_FG_Branch", "SP_V_Needles", "SP_V_FG_Needles", "SP_Needles" })
        {
            if (!mats.TryGetValue(name, out var m) || !m) continue;
            if (m.HasProperty("_SnowCover")) m.SetFloat("_SnowCover", m.GetFloat("_SnowCover") * 0.6f);
            if (m.HasProperty("_SnowBias")) m.SetFloat("_SnowBias", Mathf.Max(-1f, m.GetFloat("_SnowBias") - 0.2f));
            if (m.HasProperty("_Wrap")) m.SetFloat("_Wrap", Mathf.Max(m.GetFloat("_Wrap"), 0.6f));
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", Mathf.Min(m.GetFloat("_Smoothness"), 0.05f));
            if (m.HasProperty("_RimScale")) m.SetFloat("_RimScale", m.GetFloat("_RimScale") * 0.5f);
            EditorUtility.SetDirty(m); n++;
        }
        Log($"LOOK canopy: {n} needle materials -> snow x0.6, bias -0.2, wrap >= 0.6, gloss <= 0.05, rim x0.5");
    }

    // ------------------------------------------------------------------ arena boundary (QA #9)
    // Was a 4 m additive purple wall. Now a faint cold-grey line where it meets the ground: colour (0.3,0.31,0.33) x0.14
    // additive (<= 0.05 linear), faded out by ~0.45 m above the ground (_FadeTop 0.19 of the 4 m ring; base 0.3 m under).
    static void LookBoundary()
    {
        if (!mats.TryGetValue("SP_BoundaryWall", out var m) || !m) return;
        m.SetColor("_Col", new Color(0.3f, 0.31f, 0.33f)); m.SetColor("_EdgeCol", new Color(0.42f, 0.43f, 0.45f));
        m.SetFloat("_Intensity", 0.14f); m.SetFloat("_FadeTop", 0.19f); m.SetFloat("_Intersect", 0.25f);
        EditorUtility.SetDirty(m);
    }

    // ------------------------------------------------------------------ lanterns (QA #12, amateur #2 / #18)
    static void LookLanterns()
    {
        if (mats.TryGetValue("SP_LanternGlow", out var g) && g)
        {
            // the glow lives on the lantern: the fire box stays bright and blooms a little
            g.SetColor("_EmissionColor", new Color(1f, 0.6f, 0.32f) * 6f);
            EditorUtility.SetDirty(g);
        }
        int n = 0;
        foreach (var l in root.GetComponentsInChildren<Light>(true))
            if (l.name.EndsWith("LanternSpot") && LookLanternSpot(l)) n++;
        Log($"LOOK lanterns: {n} spots -> range 2.8, x0.6, #ffb070, soft cone, no shadows");
    }

    // look1: spots from Lantern() (SPBuilder.cs, 5 / 7-8 m, 3300 K) -> #ffb070, x0.6, range 2.8 m: a small soft pool at
    // the foot instead of a 3-4 m saturated orange disc. look4: no shadows (the bridge-deck rectangle, extra shadow maps).
    // look8 (amateur #18, hard straight pool edges): wide soft cone (outer 160, inner 20) so the pool fades out radially.
    // Idempotent: a spot already done has useColorTemperature off.
    static bool LookLanternSpot(Light l)
    {
        if (!l.useColorTemperature && l.range <= 2.81f) return false;
        l.useColorTemperature = false; l.color = new Color(1f, 0.69f, 0.44f);
        l.intensity *= 0.6f; l.range = 2.8f;
        if (l.type == LightType.Spot) { l.spotAngle = 160f; l.innerSpotAngle = 20f; }
        l.shadows = LightShadows.None;
        return true;
    }

    // look8: the Korean lanterns (KLSwap, after BuildLightAndPost) make their own spots with Lantern() and add a
    // LanternRoofLight 0.45 m over the cap (1.2) that blew the cap to a flat white disc (amateur #2) and a 7 m spot with
    // the big flat orange wash (amateur #18). Call this right after KLSwap() / HeroCornerVignette in SPBuilder.Build.
    public static void LookLanternsLate()
    {
        if (!root) return;
        int spots = 0, roofs = 0;
        foreach (var l in root.GetComponentsInChildren<Light>(true))
        {
            if (l.name.EndsWith("LanternSpot")) { if (LookLanternSpot(l)) spots++; }
            else if (l.name == "LanternRoofLight" && l.intensity > 0.4f) { l.intensity = 0.35f; l.range *= 0.8f; roofs++; }
        }
        var field = root.GetComponent<SnowPlague.SPField>();
        if (field && field.lanterns != null)   // SPField flickers from the intensity it reads in OnEnable, so nothing to resync
            Log($"LOOK lanterns late: {spots} spots, {roofs} roof lights 1.2 -> 0.35 (field lanterns {field.lanterns.Length})");
        else Log($"LOOK lanterns late: {spots} spots, {roofs} roof lights 1.2 -> 0.35");
    }

    // ------------------------------------------------------------------ blood: decal colours + embers + red glow
    static int LookBlood()
    {
        if (mats.TryGetValue("SP_Blood", out var b) && b) LookSetBlood(b);
        var ember = Mat("SP_BloodEmber", "Universal Render Pipeline/Particles/Unlit", m => LookAdditive(m, new Color(2.5f, 2.5f, 2.5f, 1f)));
        var glow = Mat("SP_BloodGlow", "Universal Render Pipeline/Particles/Unlit", m => LookAdditive(m, new Color(1.6f, 1.6f, 1.6f, 1f)));
        var rnd = new System.Random(4417);
        int n = 0;
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name != "Blood") continue;
            var r = t.GetComponent<MeshRenderer>();
            if (r && r.sharedMaterial && r.sharedMaterial.shader.name == "SnowPlague/BloodDecal") LookSetBlood(r.sharedMaterial);
            float size = Mathf.Max(t.lossyScale.x, t.lossyScale.z);
            LookEmbers(t, size, ember, rnd);
            LookGlow(t, size, glow, rnd);
            n++;
        }
        return n;
    }

    // r12 (Kido: "너무 붉고 젖은 느낌도 아니고"): black-red like ss5 (#3a0806..#6a0f0c), not vermilion. The soak pass
    // multiplies the ground (its gravel/snow detail shows through), the wet pass puts a lit dark film + small hard
    // specular on the pooled middles. Multiply colours are sRGB (ground x colour).
    static void LookSetBlood(Material m)
    {
        // bl5: crimson film multiply (~(120,40,35) on snow) over most of the stain, dark only in the small pooled cores
        // look1 (QA #13): maroon #5a0e0e..#7a1414 on snow (film ~(0.52,0.12,0.11) x snow ~180 = ~(94,22,20)), darker wet
        // centre (stain / pool), halo less pink
        // look8 (Kido "핏자국 왜이래"): soaked into the snow - pale pink diffusion at the rim, red body, maroon clots only
        m.SetColor("_SoakMul", new Color(0.95f, 0.72f, 0.74f)); m.SetColor("_FilmMul", new Color(0.62f, 0.16f, 0.15f));
        m.SetColor("_StainMul", new Color(0.3f, 0.05f, 0.045f));
        m.SetFloat("_Soak", 1f); m.SetFloat("_Drops", 0f); m.SetFloat("_Grain", 0.3f);
        m.SetColor("_PoolCol", new Color(0.15f, 0.02f, 0.018f)); m.SetFloat("_Pool", 0.85f);
        // glint: bl4's pow 250 x 6 never showed at the game camera; broader lobe + a camera-mirrored key light
        // look7: the moon (25 deg up, north) sits near the mirror angle of the south game camera: pow 60 x2 lit every
        // flat pool core as one pale sheet (~0.9 before the +1.3 EV grade). pow 320 keeps it to the meniscus / droplet bumps
        m.SetFloat("_Spec", 1.2f); m.SetFloat("_SpecPow", 320f); m.SetFloat("_Glint", 0.35f); m.SetFloat("_Bump", 4f); m.SetFloat("_Sheen", 0.15f);   // tr14: sheen/glint read as white paper flecks (view 04)
        // (bl8 tried spec x6 / glint 3 / film 0.56 / pool 0.65 + grain over the whole interior: the stain broke into sparse
        // specks with grey-pink sheen patches, so these are the bl7 values)
        m.SetFloat("_Emit", 0.04f); m.SetFloat("_MaxRise", 0.08f); m.SetFloat("_Debug", 0f);
        EditorUtility.SetDirty(m);
    }

    static void LookAdditive(Material m, Color baseCol)
    {
        m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Game/AbandonedVillage/Textures/AV_Glow.png"));
        m.SetColor("_BaseColor", baseCol);   // HDR via the material: particle vertex colours are 8-bit
        m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 2f); m.SetFloat("_ZWrite", 0f);
        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.One);
        m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One); m.SetFloat("_DstBlendAlpha", (float)BlendMode.One);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.DisableKeyword("_ALPHAPREMULTIPLY_ON"); m.DisableKeyword("_ALPHAMODULATE_ON");
        m.renderQueue = (int)RenderQueue.Transparent; m.SetOverrideTag("RenderType", "Transparent");
    }

    static ParticleSystem LookPS(string name, Transform blood, Material mat, out ParticleSystemRenderer pr)
    {
        var go = new GameObject(name);
        go.transform.SetParent(blood.parent, false);   // not under the decal cube: its scale would stretch the particles
        go.transform.SetPositionAndRotation(blood.position, Quaternion.identity);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        pr = go.GetComponent<ParticleSystemRenderer>();
        pr.sharedMaterial = mat; pr.shadowCastingMode = ShadowCastingMode.Off; pr.receiveShadows = false;
        return ps;
    }

    // red/orange sparks rising slowly out of the blood (ss5 fresh kills): ~10-25 alive per spot, tiny additive cards
    static void LookEmbers(Transform blood, float size, Material mat, System.Random rnd)
    {
        var ps = LookPS("BloodEmbers", blood, mat, out var pr);
        var main = ps.main; main.loop = true; main.prewarm = true; main.duration = 5f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 3.2f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.55f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.055f);   // r12: smaller (was 0.03-0.085)
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.16f, 0.04f), new Color(1f, 0.42f, 0.12f));
        main.gravityModifier = -0.04f;
        main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = 40;
        var em = ps.emission; em.rateOverTime = Mathf.Clamp(size * 1f, 1f, 2.5f);   // r12: subtler still   // r10: was 3-9, subtle (ss5 has a few sparks, not a fountain)
        em.SetBursts(new[] { new ParticleSystem.Burst(0f, 2, 4, 4, (float)(1.3 + rnd.NextDouble() * 1.4)) });   // occasional puffs
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = size * 0.32f; sh.radiusThickness = 1f;
        sh.rotation = new Vector3(-90f, 0f, 0f); sh.position = new Vector3(0, 0.05f, 0);
        sh.randomDirectionAmount = 0.35f;
        var noise = ps.noise; noise.enabled = true; noise.strength = 0.35f; noise.frequency = 0.8f; noise.scrollSpeed = 0.4f; noise.quality = ParticleSystemNoiseQuality.Low;
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.55f, 0.25f), 0f), new GradientColorKey(new Color(1f, 0.2f, 0.05f), 0.35f), new GradientColorKey(new Color(0.7f, 0.05f, 0.02f), 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1, 1, 0.3f));
        pr.renderMode = ParticleSystemRenderMode.Billboard; pr.maxParticleSize = 0.05f;
        ps.Play();
    }

    // a low, breathing red glow lying on the blood (ss5's red light pools at the kills): 1-2 big soft flat cards
    static void LookGlow(Transform blood, float size, Material mat, System.Random rnd)
    {
        var ps = LookPS("BloodGlow", blood, mat, out var pr);
        var main = ps.main; main.loop = true; main.prewarm = true; main.duration = 3f;
        main.startLifetime = 3f; main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(size * 1.1f, size * 1.5f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new Color(0.8f, 0.04f, 0.02f, 0.1f);   // r12: barely-there red breath (was 0.2)   // r10: 0.32 washed a red haze over the scan stain
        main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = 2;
        var em = ps.emission; em.rateOverTime = 0.67f;
        var sh = ps.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = size * 0.08f; sh.position = new Vector3(0, 0.08f, 0);
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.45f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        pr.renderMode = ParticleSystemRenderMode.HorizontalBillboard; pr.maxParticleSize = 2f;
        ps.Play();
    }
}
