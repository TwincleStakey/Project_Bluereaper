// Underworld look (quality pass 11-13, Kido: POE2 / No Rest for the Wicked, heavy, dark, low saturation, real post).
// Added at play time by UWEnvironment. Nothing in the scene or in a material asset is changed: everything is runtime
// (light values, MaterialPropertyBlocks, renderer/particle toggles, a runtime volume profile).
//
// Pass 11-13b (owner: "star dots over the whole screen, white circles on the floor, looks cheap"):
//   - every brightness number in this stage is seen through +2.2 EV of post exposure (x4.6) and ACES. Most of the old
//     effects were authored for a ~1x grade, so they blew out:
//       star dots   = EmbersViolet (2,400 HDR additive sparks over the whole floor), EmbersRed, the tiled "Mote" / small
//                     "Orb" billboards (HDR core 4.0), UWSkyFx sky motes, light-grey Ash flakes at 11 m right under the
//                     camera. All off or near-black now; a few faint embers rise from the lanterns instead (one emitter).
//       white discs = LanternHalo / PaperHalo billboards (2.2 m additive discs, the low ones 0.5 m above the floor),
//                     9 hot moon-shaft spots (inner cone 25 %), low lanterns at 14 cd from 0.6 m (inverse-square hot spot).
//                     Halos shrunk and dimmed, shafts cut to 3 very soft dim ones, lantern intensity set per height so the
//                     floor under a lantern peaks at a fixed illuminance.
//       milky haze  = fog cards (void / edge / rim, unlit light grey alpha 1) and mist particles (unlit lavender), and the
//                     old height fog colour (0.2 grey pre-exposure = 0.9 after it). Cards darkened with property blocks,
//                     mist particles off, fog colour given post-exposure and divided back.
//   - UWFlicker on every scene light rewrote intensity from its own baseIntensity each frame, so the old intensity edits
//     (lanterns x14, paper x1.6) fought it. Now UWFlicker.baseIntensity is what gets set.
//   - height fog is an analytic integral of exponential height density along the view ray (UWHeightFog.shader): it only
//     builds up below ~1.5 m and in the low ground / abyss; the 30-45 m of air between camera and floor adds nothing.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Underworld
{
    [DisallowMultipleComponent]
    public class UWLook : MonoBehaviour
    {
        public static bool On = true;

        [Header("Grade")]
        public float exposure = 1.6f;   // 3rd pass (Kido: "너무 밝고 일반 필드"): -0.5 EV;   // EV; the shared bullet / enemy look (LookFX 2.3) was tuned around this
        // ref_targets.md 5-6: refs keep mids near-neutral, shadows slightly cool (~210 deg), highlights warm (25-40 deg);
        // our highlights measured blue (236 deg) -> neutral filter, warm white balance, split toning cool/warm.
        public float contrast = 14f, saturation = -18f, temperature = 6f;   // hell pass (11-13 night): darker, fire colour kept, glow carries the image
        public Color filter = Color.white;
        // URP split toning: 0.5 grey is neutral, below 0.5 darkens (soft light). Shadows ~210 deg sat 15, highs ~35 deg sat 20.
        public Color splitShadows = new Color(0.47f, 0.44f, 0.46f), splitHighlights = new Color(0.58f, 0.48f, 0.4f);   // red-black shadows, orange highlights
        public float splitBalance = -20f;
        // LiftGammaGain lift offset. Was -0.035 plus SMH shadows -0.08 plus darkening split toning: 74 % of the frame
        // measured pure black (luma < 10). Slightly positive now so the dark field reads as luma 10-25, not 0.
        public float shadowLift = 0.008f;
        public float bloomIntensity = 1.25f, bloomThreshold = 0.95f, bloomScatter = 0.75f;   // lava / fire bloom wide
        public float vignette = 0.45f, vignetteSmooth = 0.5f, grain = 0.1f;

        [Header("Lighting")]
        public Color moonColor = new Color(0.75f, 0.8f, 0.9f);
        public float moonIntensity = 0.18f;   // 12:21 capture was 3-4x over ref luma (p50 108 vs 22-32): the field is lit by pools, not the moon
        public Color ambientBase = new Color(0.022f, 0.024f, 0.03f), ambientUp = new Color(0.012f, 0.013f, 0.016f);   // cool grey SH

        [Header("Player light pool (counters RoomRun FieldLighting / LookFX while this field is live, restored after)")]
        [Tooltip("ref_targets.md 3: refs r50 ~5.5 m, centre 4-8x the surround, ~3500 K. Ours was r50 ~2 m, 60-110x, blue-white.")]
        public bool retunePlayerPool = true;
        public Color playerPoolColor = new Color(0.8f, 0.78f, 0.76f);   // 3rd pass: pale cold, not warm   // ~3500 K (255,214,170)
        public float playerPoolHeight = 7f, playerPoolRange = 15f, playerPoolLux = 0.9f;   // review_01: player read too dark on dark rock   // Kido: floor read pale/bone-white under the pool   // r50 ~ 0.77 x height

        [Header("Lantern pools (~2500 K)")]
        public Color lanternColor = new Color(1f, 0.62f, 0.3f);
        [Tooltip("Illuminance on the floor straight under a tall lantern (intensity = this x height^2). Moonlit floor is ~0.35.")]
        public float lanternFloorLux = 1.4f;
        [Tooltip("Low lanterns hold the light 0.6 m up inside the firebox; they are small glows, not pools.")]
        public float lowLanternIntensity = 1.2f;
        public float lanternFlicker = 0.1f;

        [Header("Braziers (warm pools from lights only, off the spawn centre)")]
        public Vector2[] braziers = { };   // pass 5 (final_04 #4): sourceless light pools read as round red stains - off   // 3rd pass: 8 cold soul-fire pools
        public Color brazierColor = new Color(1f, 0.36f, 0.1f);   // hell pass: fire pools
        public float brazierHeight = 3f, brazierFloorLux = 3.2f;

        [Header("Moon shafts (cold, soft, dim)")]
        public Vector2[] shafts = { };   // hell pass: no moon shafts
        public Color shaftColor = new Color(0.72f, 0.8f, 0.95f);
        public float shaftHeight = 14f, shaftFloorLux = 0.35f, shaftAngle = 90f, shaftInner = 10f;

        [Header("Embers (rise from the lanterns only)")]
        public float emberRate = 3f;

        [Header("Height fog")]
        [Tooltip("Fog colour as it should read after exposure (pre-tonemap). Divided by 2^exposure before use.")]
        public Color fogColor = new Color(0.07f, 0.035f, 0.028f);   // hell pass: smoky red-brown haze, low
        [Tooltip("Share of the lit scene colour the fog keeps, so fog inside a lantern pool glows warm instead of greying it.")]
        public float fogLitScatter = 0.45f;
        public float fogBase = 0.1f, fogFalloff = 0.9f, fogDensity = 0.55f, fogMax = 0.85f;   // 3rd pass: thick low fog to ~2.5 m
        public float fogNoiseScale = 0.04f, fogNoiseAmount = 0.8f;
        public Vector2 fogWind = new(0.05f, 0.02f);

        Volume vol;
        Transform fxRoot;
        bool Live;
        Material fogMat;
        FogPass fogPass;
        ParticleSystem embers;
        readonly List<Vector3> emberSpots = new();
        float emberAcc;

        Color FogPre => fogColor / Mathf.Pow(2f, exposure);

        void Start()
        {
            vol = gameObject.AddComponent<Volume>();
            vol.isGlobal = true; vol.priority = 1200;
            var p = ScriptableObject.CreateInstance<VolumeProfile>();
            p.name = "UWLook (runtime)";
            vol.sharedProfile = p;

            p.Add<Tonemapping>(true).mode.Override(TonemappingMode.ACES);
            // review_01: LookFX's bokeh DoF (focus on the player) turned the lava sea far below into blocky low-res squares
            p.Add<DepthOfField>(true).mode.Override(DepthOfFieldMode.Off);
            var ca = p.Add<ColorAdjustments>(true);
            ca.postExposure.Override(exposure); ca.contrast.Override(contrast); ca.saturation.Override(saturation);
            ca.colorFilter.Override(filter); ca.hueShift.Override(0f);
            var wb = p.Add<WhiteBalance>(true); wb.temperature.Override(temperature); wb.tint.Override(0f);
            var st = p.Add<SplitToning>(true); st.shadows.Override(splitShadows); st.highlights.Override(splitHighlights); st.balance.Override(splitBalance);
            var smh = p.Add<ShadowsMidtonesHighlights>(true);
            smh.shadows.Override(new Vector4(0.98f, 1f, 1.04f, -0.02f));
            smh.midtones.Override(new Vector4(1f, 1f, 1f, 0f));
            smh.highlights.Override(new Vector4(1.03f, 1f, 0.96f, 0f));
            var lgg = p.Add<LiftGammaGain>(true);
            lgg.lift.Override(new Vector4(1f, 1f, 1f, shadowLift)); lgg.gamma.Override(new Vector4(1f, 1f, 1f, 0f)); lgg.gain.Override(new Vector4(1f, 1f, 1f, 0f));
            var bl = p.Add<Bloom>(true);
            bl.intensity.Override(bloomIntensity); bl.threshold.Override(bloomThreshold); bl.scatter.Override(bloomScatter);
            bl.tint.Override(new Color(1f, 0.9f, 0.8f)); bl.highQualityFiltering.Override(true);
            var vg = p.Add<Vignette>(true); vg.intensity.Override(vignette); vg.smoothness.Override(vignetteSmooth); vg.color.Override(new Color(0.004f, 0.005f, 0.008f));
            var fg = p.Add<FilmGrain>(true); fg.type.Override(FilmGrainLookup.Thin1); fg.intensity.Override(grain); fg.response.Override(0.8f);
            p.Add<ChromaticAberration>(true).intensity.Override(0.04f);

            var sh = Shader.Find("Underworld/HeightFog");
            if (sh) fogMat = new Material(sh) { hideFlags = HideFlags.DontSave };
            fogPass = new FogPass { renderPassEvent = RenderPassEvent.AfterRenderingSkybox };
            RenderPipelineManager.beginCameraRendering += Inject;

            fxRoot = new GameObject("UWLook lights").transform;
            fxRoot.SetParent(transform, false);
            ApplyStage();
            HideArenaRubble();
            ScatterBones();
            HellFx();
            HideOldProps();
            HellSet();
            // Kido 11-13 ("저 유령같은것들 지워봐"): the stage's decorative standing ghosts (builder group "Ghosts") off
            foreach (var root in gameObject.scene.GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name == "Ghosts") { t.gameObject.SetActive(false); Debug.Log("UWLook: decorative ghosts hidden"); }
            Lights();
            Declutter();
        }

        void OnDestroy()
        {
            SharedCounters(false);   // the stage scene unloads: give RoomRun its own values back
            Shader.SetGlobalFloat("_DK_SmokeScale", 0f);
            RenderPipelineManager.beginCameraRendering -= Inject;
            if (fogMat) Destroy(fogMat);
        }

        // ------------------------------------------------------------------ stage mood scripts, moon, ambient
        void ApplyStage()
        {
            var env = GetComponent<UWEnvironment>();
            if (env)
            {
                env.readableOn = false;
                env.fogGrey = FogPre;           // RenderSettings fog (exp2): colour pre-exposure, nearly off
                env.fogDensity = 0.003f;
                env.Set(1f, 0f);
            }
            var br = GetComponent<UWBrightness>();
            if (br)
            {
                br.brightness = 0f;
                br.baseMoon = moonIntensity;
                br.ambientBase = ambientBase; br.ambientUp = ambientUp;
                if (br.moon) { br.moon.color = moonColor; br.moon.shadows = LightShadows.Soft; br.moon.shadowStrength = 0.85f; }
                br.Apply();
            }
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = ambientBase; RenderSettings.ambientEquatorColor = ambientBase * 0.8f; RenderSettings.ambientGroundColor = ambientBase * 0.4f;
            // the scene's baked reflection cubemap is the old violet sky: damp floor patches mirrored it as purple blotches (12:27)
            RenderSettings.reflectionIntensity = 0f;
        }

        // ------------------------------------------------------------------ lights
        static void SetI(Light l, float intensity, float flicker)
        {
            // UWFlicker rewrites intensity every frame from baseIntensity, so that is the value to set
            var f = l.GetComponent<UWFlicker>();
            if (f) { f.baseIntensity = intensity; f.amount = flicker; }
            l.intensity = intensity;
        }

        void Lights()
        {
            int tall = 0, low = 0;
            emberSpots.Clear();
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (l.type != LightType.Point || l.transform.IsChildOf(fxRoot)) continue;
                switch (l.gameObject.name)
                {
                    case "LanternLight":
                    {
                        // UWBuilder: tall lantern light 1.9 m up, range 6.5; low lantern 0.6 m up, range 4 (scene values)
                        bool isTall = l.range > 5f;
                        l.color = lanternColor; l.shadows = LightShadows.None;
                        if (isTall)
                        {
                            const float h = 1.9f;
                            SetI(l, lanternFloorLux * h * h, lanternFlicker); l.range = 9f; tall++;
                            emberSpots.Add(l.transform.position + Vector3.up * 0.35f);
                        }
                        else { SetI(l, lowLanternIntensity, lanternFlicker); l.range = 4f; low++; }
                        break;
                    }
                    case "PaperLight": l.color = new Color(1f, 0.38f, 0.2f); SetI(l, 2.4f, 0.12f); l.range = 6f; break;
                    case "OrbLight": l.color = new Color(1f, 0.4f, 0.12f); SetI(l, 2.2f, 0.08f); l.range = 6f; break;      // cold ghost fire
                    case "SoulGateLight": l.color = new Color(1f, 0.25f, 0.08f); SetI(l, 8f, 0.1f); l.range = 13f; break;
                    case "GateWarmFill": l.color = lanternColor; SetI(l, 4f, 0.08f); break;
                    case "BridgeFillA": case "BridgeFillB": l.color = new Color(0.55f, 0.65f, 0.85f); SetI(l, 2.5f, 0.06f); break;
                    // violet portal lights painted purple patches across the plaza (12:24 capture): cold, dimmer, gate-local
                    case "PortalKey": case "PortalBack": l.color = new Color(1f, 0.35f, 0.1f); SetI(l, 5f, 0.08f); l.range = Mathf.Min(l.range, 9f); break;
                }
            }
            Debug.Log($"UWLook: lanterns {tall} tall + {low} low");

            for (int i = 0; i < braziers.Length; i++)
            {
                var l = NewLight("BrazierPool", new Vector3(braziers[i].x, brazierHeight, braziers[i].y), Quaternion.identity, LightType.Point);
                l.color = brazierColor; l.range = 11f;
                l.intensity = brazierFloorLux * brazierHeight * brazierHeight;
                l.gameObject.AddComponent<UWFlicker>().baseIntensity = l.intensity;
                var f = l.GetComponent<UWFlicker>(); f.amount = 0.08f; f.speed = 2.5f; f.phase = i * 3.1f;
            }
            for (int i = 0; i < shafts.Length; i++)
            {
                var l = NewLight("MoonShaft", new Vector3(shafts[i].x, shaftHeight, shafts[i].y), Quaternion.Euler(82f, 155f, 0f), LightType.Spot);
                l.color = shaftColor;
                l.intensity = shaftFloorLux * shaftHeight * shaftHeight * (0.85f + 0.3f * Mathf.PerlinNoise(i * 1.7f, 0.3f));
                l.spotAngle = shaftAngle; l.innerSpotAngle = shaftInner;   // tiny inner cone = falloff across the whole disc
                l.range = shaftHeight * 1.7f;                              // past the cone edge, so the range window never cuts it
            }
        }

        Light NewLight(string name, Vector3 pos, Quaternion rot, LightType type)
        {
            var go = new GameObject(name);
            go.transform.SetParent(fxRoot, false);
            go.transform.SetPositionAndRotation(pos, rot);
            var l = go.AddComponent<Light>();
            l.type = type; l.shadows = LightShadows.None; l.renderMode = LightRenderMode.ForcePixel;
            return l;
        }

        // ------------------------------------------------------------------ dots, discs and milky cards
        void Declutter()
        {
            var mpb = new MaterialPropertyBlock();
            Material emberMat = null;
            int off = 0, halos = 0, cards = 0;
            var fogPre = FogPre;

            foreach (var ps in FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
            {
                var r = ps.GetComponent<ParticleSystemRenderer>();
                var n = ps.gameObject.name;
                string mat = r && r.sharedMaterial ? r.sharedMaterial.name : "";
                if (n == "EmbersRed" && r) emberMat = r.sharedMaterial;
                if (n == "EmbersRed")   // hell pass: red embers kept (violet off)
                {
                    // Kido 11-13 ("원래 보라색 vfx ... 몽환스럽게"): violet soul sparks back, a quarter of the old density
                    var em = ps.emission; em.rateOverTimeMultiplier *= 0.5f;   // pass 8: more rising embers (glow sources), now ember orange
                    { var mm0 = ps.main; mm0.startSizeMultiplier *= 1.6f; }   // pass 9: sparks were under 4 px at the gameplay zoom
                    var main = ps.main; main.maxParticles = Mathf.Min(main.maxParticles, 600);
                    // hell pass 2: these read as lavender/white "stars" over the floor; ember orange instead
                    main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.38f, 0.08f, 1f), new Color(1f, 0.6f, 0.2f, 1f));
                    Refill(ps);
                }
                else if (n == "EmbersViolet" || mat == "UW_PtMist")
                {
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    if (r) r.enabled = false;
                    off++;
                }
                else if (n == "Ash")
                {
                    // a few dark flakes, not light-grey dots under the camera
                    // ref_targets.md 5: -70 % specks, larger, under the bloom threshold, warm ash colour
                    var main = ps.main; main.maxParticles = 220; main.startColor = new Color(0.05f, 0.036f, 0.026f, 0.5f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.16f);
                    var em = ps.emission; em.rateOverTime = 18f;
                    FadeOnly(ps, 0.1f, 0.8f);
                    Refill(ps);
                }
                else if (n == "SoulStream" || n == "PortalSpiral")
                {
                    var main = ps.main; var c = main.startColor.color;
                    main.startColor = new Color(1f, 0.42f, 0.12f, c.a);   // hell pass 2: violet -> ember
                    Refill(ps);
                }
            }

            foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (!r.sharedMaterial) continue;
                var n = r.gameObject.name; var mat = r.sharedMaterial.name;
                if (n.StartsWith("FogBank") || n.StartsWith("GroundMist")) { r.enabled = false; off++; continue; }
                switch (mat)
                {
                    case "UW_OrbSmall":
                        if (n == "SoulGateGlow") { Glow(r, mpb, new Color(1f, 0.7f, 0.4f) * 2f, new Color(1f, 0.3f, 0.06f) * 0.3f, 0.8f); halos++; }
                        else { r.enabled = false; off++; }   // hell pass 2: loose blue-white motes all off   // loose "Orb" / "Mote" billboards: keep one in four (dreamy pass)
                        continue;
                    case "UW_OrbViolet": Glow(r, mpb, new Color(1f, 0.8f, 0.5f) * 2.0f, new Color(1f, 0.3f, 0.05f) * 0.35f, 0.8f); halos++; continue;   // violet again
                    case "UW_HaloLantern": Glow(r, mpb, lanternColor * 1.8f, new Color(0.9f, 0.45f, 0.15f) * 0.18f, 1.1f); halos++; continue;
                    case "UW_HaloPaper": Glow(r, mpb, new Color(1f, 0.38f, 0.2f) * 1.4f, new Color(0.8f, 0.2f, 0.1f) * 0.16f, 1.0f); halos++; continue;
                    case "UW_FogVoid": r.enabled = false; off++; continue;   // hell pass 2: rectangular dark cards over the lava sea read as slabs; height fog does the haze
                    case "UW_FogEdge": r.enabled = false; off++; continue;   // hell pass 2: standing cards read as black slabs over the lava sea
                    case "UW_FogGround": r.enabled = false; off++; continue;   // hell pass 2: rectangular dark patches over the lava sea
                    case "UW_SoulVeil": r.enabled = false; off++; continue;   // hell pass 2: violet veil haze off
                }
            }
            // hell pass 2: any remaining cold (blue / violet / white-blue) scene emitter reads as "stars" in a lava world -> ember
            int warmed = 0;
            foreach (var ps in FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
            {
                if (ps.transform.IsChildOf(transform) || ps.gameObject.scene != gameObject.scene) continue;   // stage dressing only, never weapon / bullet VFX
                var main = ps.main; var sc = main.startColor;
                Color c = sc.mode == ParticleSystemGradientMode.Color ? sc.color : sc.mode == ParticleSystemGradientMode.TwoColors ? sc.colorMax
                        : sc.mode == ParticleSystemGradientMode.Gradient || sc.mode == ParticleSystemGradientMode.RandomColor ? sc.gradient.Evaluate(0.5f) : sc.gradientMax.Evaluate(0.5f);
                var col = ps.colorOverLifetime; Color cl = col.enabled ? col.color.Evaluate(0.5f) : Color.white;
                bool cold = c.b > c.r + 0.05f || (col.enabled && cl.b > cl.r + 0.05f);
                if (!cold) continue;
                float a2 = c.a;
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.36f, 0.08f, a2), new Color(1f, 0.55f, 0.18f, a2));
                if (col.enabled) { var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, col.color.gradient != null ? col.color.gradient.alphaKeys : new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) }); col.color = g; }
                var tr = ps.trails; if (tr.enabled) tr.colorOverLifetime = new Color(1f, 0.4f, 0.1f);
                warmed++;
                var pr = ps.GetComponent<ParticleSystemRenderer>();
                Debug.Log($"UWLook: warmed cold emitter {ps.gameObject.name} ({(pr && pr.sharedMaterial ? pr.sharedMaterial.name : "-")})");
            }
            Debug.Log($"UWLook: {off} dot/mist emitters and cards off, {halos} halos toned, {cards} fog cards darkened, {warmed} cold emitters warmed");

            if (emberMat && emberSpots.Count > 0 && emberRate > 0f) embers = MakeEmbers(emberMat);
        }

        static void Glow(Renderer r, MaterialPropertyBlock mpb, Color core, Color halo, float size)
        {
            r.GetPropertyBlock(mpb);   // keeps the builder's _Phase
            mpb.SetColor("_Core", core); mpb.SetColor("_Halo", halo); mpb.SetFloat("_Size", size); mpb.SetFloat("_Flare", 0f);
            r.SetPropertyBlock(mpb);
        }

        // fog cards: UWEnvironment rewrites the shared material's _Col; a property block overrides it per renderer
        static void Col(Renderer r, MaterialPropertyBlock mpb, Color c, float a)
        {
            r.GetPropertyBlock(mpb);
            c.a = a; mpb.SetColor("_Col", c);
            r.SetPropertyBlock(mpb);
        }

        static void FadeOnly(ParticleSystem ps, float inAt, float outAt)
        {
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, inAt), new GradientAlphaKey(1f, outAt), new GradientAlphaKey(0f, 1f) });
            col.color = g;
        }

        // prewarmed systems keep their old particles for a full lifetime; restart them with the new settings
        static void Refill(ParticleSystem ps)
        {
            ps.Simulate(ps.main.startLifetime.constantMax, true, true);
            ps.Play(true);
        }

        ParticleSystem MakeEmbers(Material m)
        {
            var go = new GameObject("UWLook embers");
            go.transform.SetParent(fxRoot, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.loop = true; main.playOnAwake = false; main.maxParticles = 80;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 4f); main.startSpeed = 0f;
            var em = ps.emission; em.rateOverTime = 0f;
            var shape = ps.shape; shape.enabled = false;
            var nz = ps.noise; nz.enabled = true; nz.strength = 0.25f; nz.frequency = 0.6f;
            FadeOnly(ps, 0.15f, 0.6f);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = m; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        void TickEmbers()
        {
            if (!embers || emberSpots.Count == 0) return;
            emberAcc += emberRate * Time.deltaTime;
            while (emberAcc >= 1f)
            {
                emberAcc -= 1f;
                var p = emberSpots[Random.Range(0, emberSpots.Count)] + new Vector3(Random.Range(-0.15f, 0.15f), 0f, Random.Range(-0.15f, 0.15f));
                var ep = new ParticleSystem.EmitParams
                {
                    position = p, applyShapeToPosition = false,
                    velocity = new Vector3(Random.Range(-0.08f, 0.08f), Random.Range(0.35f, 0.7f), Random.Range(-0.08f, 0.08f)),
                    startSize = Random.Range(0.035f, 0.065f),
                    startLifetime = Random.Range(2.5f, 4f),
                    startColor = new Color32(110, 50, 18, 255),   // x the material's HDR tint (3) = 1.3 max: under the bloom threshold
                };
                embers.Emit(ep, 1);
            }
        }

        // ------------------------------------------------------------------ shared RoomRun effects, countered while live
        // The white blotch at the player: FieldLighting's pool (2.2 cd at 2.4 m + a 1.2 cd foot light at 0.9 m = small hot
        // disc) plus LookFX's additive blue "aura" ground card. FieldLighting re-asserts its public fields every frame, so
        // those fields are what gets set; LookFX's aura material is private, reached by reflection (guarded, warned once).
        // Everything is put back when the underworld stops being the active field or this component is destroyed.
        RoomRun.FieldLighting fl;
        bool flSaved;
        Color flColor; float flRange, flIntensity, flHeight, flFoot;
        Material auraMat;
        bool auraSaved, auraLookedUp;
        Color auraColor;
        float nextFind;

        void SharedCounters(bool want)
        {
            if (want && !fl && Time.unscaledTime >= nextFind)
            {
                nextFind = Time.unscaledTime + 1f;
                fl = FindAnyObjectByType<RoomRun.FieldLighting>();
            }
            if (want && fl)
            {
                if (!flSaved) { flColor = fl.playerLight; flRange = fl.playerRange; flIntensity = fl.playerIntensity; flHeight = fl.playerHeight; flFoot = fl.footIntensity; flSaved = true; }
                fl.playerLight = playerPoolColor; fl.playerHeight = playerPoolHeight; fl.playerRange = playerPoolRange;
                fl.playerIntensity = playerPoolLux * playerPoolHeight * playerPoolHeight;
                fl.footIntensity = 0f;   // the tight foot light made the hard centre
            }
            else if (!want && flSaved)
            {
                if (fl) { fl.playerLight = flColor; fl.playerRange = flRange; fl.playerIntensity = flIntensity; fl.playerHeight = flHeight; fl.footIntensity = flFoot; }
                flSaved = false;
            }

            if (want && !auraLookedUp)
            {
                auraLookedUp = true;
                var lfx = FindAnyObjectByType<RoomRun.LookFX>();
                var fi = typeof(RoomRun.LookFX).GetField("aura", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                auraMat = lfx && fi != null ? fi.GetValue(lfx) as Material : null;
                if (!auraMat) Debug.LogWarning("UWLook: LookFX aura material not found (field renamed?); player aura card left as is");
            }
            if (auraMat && auraMat.HasProperty("_Color"))
            {
                if (want && !auraSaved) { auraColor = auraMat.GetColor("_Color"); auraSaved = true; }
                if (want) auraMat.SetColor("_Color", Color.clear);   // additive card: black = invisible
                else if (auraSaved) { auraMat.SetColor("_Color", auraColor); auraSaved = false; }
            }
            if (!want && !auraSaved) auraLookedUp = false;   // look it up again on the next visit
        }

        // ------------------------------------------------------------------ per frame
        // QA 11-13: ghost enemies rendered pure white under the +2.2 EV grade; give them a dim, cold ghost-fire look
        // (property block per renderer, so the shared material asset stays as it is). Enemies spawn later, so re-checked twice a second.
        float nextGhostTone;
        MaterialPropertyBlock ghostMpb;
        public Color ghostLit = new Color(0.34f, 0.37f, 0.42f, 1f), ghostGlow = new Color(0.012f, 0.03f, 0.045f);
        public Color ghostBody = new Color(0.36f, 0.4f, 0.46f, 0.55f), ghostRim = new Color(0.35f, 0.55f, 0.75f), ghostEdge = new Color(0.45f, 0.8f, 1.1f);
        void ToneGhosts()
        {
            ghostMpb ??= new MaterialPropertyBlock();
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                var m = r.sharedMaterial;
                if (!m || r.HasPropertyBlock()) continue;
                if (m.name.StartsWith("UW_Ghost"))
                {
                    ghostMpb.Clear();
                    ghostMpb.SetColor("_Col", ghostBody); ghostMpb.SetColor("_Rim", ghostRim); ghostMpb.SetColor("_Edge", ghostEdge);
                    r.SetPropertyBlock(ghostMpb);
                }
                else if (m.name.StartsWith("DM_Ganfaul"))   // the RoomRun "ghost" enemy: white URP Lit + emission, blown out here
                {
                    ghostMpb.Clear();
                    ghostMpb.SetColor("_BaseColor", ghostLit); ghostMpb.SetColor("_EmissionColor", ghostGlow);
                    r.SetPropertyBlock(ghostMpb);
                }
            }
        }

        // QA 11-13 + arena rule (nothing inside the arena that reads as an obstacle): the scattered box-like rubble chunks
        // inside the play area are hidden; the ones outside it stay as edge dressing.
        public Rect arenaRect = new Rect(-52f, -41f, 100f, 74f);   // RoomRunConfig underworld: centre (-2,-4), 100 x 74
        // 3rd pass (Kido: "저승 왠 그냥 일반 필드"): Fab Megascans bones (Resources/Bones) strewn flat over the ash, no colliders,
        // denser in piles toward the arena edges, a clear ring around the spawn point. Seeded, so every visit looks the same.
        public int boneCount = 40, bonePiles = 6;
        public Vector2 boneScale = new(1.3f, 1.8f);   // Kido 11-13: leg bone was person-sized; scan leg bone 0.28 m -> ~0.4-0.5 m (real femur)
        void ScatterBones()
        {
            var prefabs = Resources.LoadAll<GameObject>("Bones");
            if (prefabs.Length == 0) { Debug.LogWarning("UWLook: no bone prefabs in Resources/Bones"); return; }
            var root = new GameObject("UWLook bones").transform;
            root.SetParent(transform, false);
            var rnd = new System.Random(1313);
            float F(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
            var c = arenaRect.center;
            int placed = 0;
            void Put(Vector2 xz)
            {
                if ((xz - new Vector2(-2f, -4f)).sqrMagnitude < 49f) return;   // keep the spawn clear
                var pos = new Vector3(xz.x, 30f, xz.y);
                pos.y = Physics.Raycast(pos, Vector3.down, out var hit, 60f) ? hit.point.y : 0f;
                var go = Instantiate(prefabs[rnd.Next(prefabs.Length)], root);
                go.transform.SetPositionAndRotation(pos + Vector3.up * 0.02f, Quaternion.Euler(F(-6f, 6f), F(0f, 360f), F(-6f, 6f)));
                go.transform.localScale = Vector3.one * F(boneScale.x, boneScale.y);
                placed++;
            }
            for (int i = 0; i < boneCount; i++)
            {
                // bias outward: radius from the centre grows with a square root, then pushed toward the rim
                var dir = new Vector2(F(-1f, 1f), F(-1f, 1f)).normalized;
                float t = Mathf.Sqrt(F(0.15f, 1f));
                Put(c + new Vector2(dir.x * arenaRect.width * 0.5f, dir.y * arenaRect.height * 0.5f) * t * 0.95f);
            }
            for (int p = 0; p < bonePiles; p++)
            {
                var dir = new Vector2(F(-1f, 1f), F(-1f, 1f)).normalized;
                var pc = c + new Vector2(dir.x * arenaRect.width * 0.5f, dir.y * arenaRect.height * 0.5f) * F(0.75f, 0.95f);
                int n = rnd.Next(4, 8);
                for (int k = 0; k < n; k++) Put(pc + new Vector2(F(-1.6f, 1.6f), F(-1.6f, 1.6f)));
            }
            Debug.Log($"UWLook: {placed} bones scattered");
        }

        // Kido 11-13: something eerie seeping out of the floor + the area outside the arena should feel truly hellish.
        //  - soul wisps: faint cold mist puffs seeping up out of the ash all over the arena, low and slow (no gameplay effect);
        //  - hell rim: red-orange fire glow welling up from the chasm just outside the arena edge, and dark smoke columns rising
        //    from it, lit from below. Nothing inside the arena is added except the low wisps.
        // Particles reuse the stage's Kenney puff sprite (UW_PtMist's texture) through runtime material copies.
        public int wispRate = 12, smokeColumns = 12, rimFires = 0;   // hell pass: the lava sea glow replaces the rim fire lights (perf)
        public Color wispColor = new Color(0.12f, 0.08f, 0.07f, 0.12f), fireColor = new Color(1f, 0.32f, 0.08f), smokeColor = new Color(0.09f, 0.075f, 0.07f, 0.55f);
        void HellFx()
        {
            Texture puff = null; Shader psh = Shader.Find("Underworld/Particle");
            foreach (var r in FindObjectsByType<ParticleSystemRenderer>(FindObjectsSortMode.None))
                if (r.sharedMaterial && r.sharedMaterial.name.StartsWith("UW_PtMist")) { puff = r.sharedMaterial.GetTexture("_MainTex"); break; }
            // hell pass 2: the stage puff is a halftone-dot sprite; scaled to 6-13 m smoke columns it read as speckled black bushes
            var soft = Resources.Load<Texture2D>("VFX/smoke_04"); if (soft) puff = soft;
            if (!psh || !puff) { Debug.LogWarning("UWLook: no mist sprite/shader for hell fx"); return; }
            Material M(Color tint, bool additive, float soft)
            {
                var m = new Material(psh) { hideFlags = HideFlags.DontSave };
                m.SetTexture("_MainTex", puff); m.SetColor("_Tint", tint);
                m.SetFloat("_Src", (float)(additive ? BlendMode.One : BlendMode.SrcAlpha));
                m.SetFloat("_Dst", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
                m.SetFloat("_SoftFade", soft);
                return m;
            }
            var root = new GameObject("UWLook hell fx").transform; root.SetParent(transform, false);
            ParticleSystem PS(string n, Vector3 pos, Material m)
            {
                var go = new GameObject(n); go.transform.SetParent(root, false); go.transform.position = pos;
                var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var r = go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = ShadowCastingMode.Off;
                var mm = ps.main; mm.simulationSpace = ParticleSystemSimulationSpace.World; mm.playOnAwake = true; mm.loop = true; mm.prewarm = true;
                return ps;
            }
            Gradient Fade(float a0, float peakAt, float a1, float midAlpha)
            {
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                    new[] { new GradientAlphaKey(a0, 0), new GradientAlphaKey(1, peakAt), new GradientAlphaKey(midAlpha, 0.7f), new GradientAlphaKey(a1, 1) });
                return g;
            }
            var c = arenaRect.center;

            // soul wisps over the whole arena
            {
                var ps = PS("SoulWisps", new Vector3(c.x, 0.15f, c.y), M(wispColor, false, 2.5f));   // the particle shader ignores vertex alpha: faintness lives in the tint
                var mm = ps.main; mm.startLifetime = new ParticleSystem.MinMaxCurve(5f, 8f); mm.startSpeed = 0f;
                mm.startSize = new ParticleSystem.MinMaxCurve(3.5f, 7f); mm.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f);
                mm.maxParticles = 300;
                var em = ps.emission; em.rateOverTime = wispRate;
                var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(arenaRect.width, 0.1f, arenaRect.height);
                var v = ps.velocityOverLifetime; v.enabled = true; v.space = ParticleSystemSimulationSpace.World;
                v.x = new ParticleSystem.MinMaxCurve(-0.08f, 0.12f); v.y = new ParticleSystem.MinMaxCurve(0.12f, 0.3f); v.z = new ParticleSystem.MinMaxCurve(-0.05f, 0.1f);
                var col = ps.colorOverLifetime; col.enabled = true; col.color = Fade(0f, 0.35f, 0f, 0.6f);
                var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.6f, 1, 1.4f));
                var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.2f, 0.2f);
                ps.Play();
            }

            // hell rim: points on a ring 4-14 m outside the arena edge
            var rnd = new System.Random(666);
            float F(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
            Vector3 Rim(float outMin, float outMax)
            {
                float a = F(0f, Mathf.PI * 2f); var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                float tx = Mathf.Abs(d.x) > 1e-3f ? arenaRect.width * 0.5f / Mathf.Abs(d.x) : 1e9f;
                float tz = Mathf.Abs(d.y) > 1e-3f ? arenaRect.height * 0.5f / Mathf.Abs(d.y) : 1e9f;
                var e = c + d * (Mathf.Min(tx, tz) + F(outMin, outMax));
                return new Vector3(e.x, 0f, e.y);
            }
            for (int i = 0; i < rimFires; i++)
            {
                var p = Rim(5f, 11f); p.y = F(0.3f, 1.2f);   // just beyond the edge, low: red spill creeps onto the arena rim and the void fog (from below, the solid plaza hid it)
                var lg = new GameObject("HellGlow"); lg.transform.SetParent(root, false); lg.transform.position = p;
                var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.color = fireColor; l.range = F(13f, 18f); l.intensity = F(10f, 20f);
                l.shadows = LightShadows.None; l.renderMode = LightRenderMode.ForcePixel;
            }
            var smokeMat = M(smokeColor, false, 2f);
            var glowMat = M(new Color(1f, 0.3f, 0.06f, 1f) * 0.5f, true, 2f);
            for (int i = 0; i < smokeColumns; i++)
            {
                var p = Rim(5f, 14f); p.y = -2f;
                var ps = PS("HellSmoke", p, smokeMat);
                var mm = ps.main; mm.startLifetime = new ParticleSystem.MinMaxCurve(7f, 11f); mm.startSpeed = 0f;
                mm.startSize = new ParticleSystem.MinMaxCurve(3f, 6f); mm.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f); mm.maxParticles = 60;
                var em = ps.emission; em.rateOverTime = 4f;
                var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 2f; sh.rotation = new Vector3(90, 0, 0);
                var v = ps.velocityOverLifetime; v.enabled = true; v.space = ParticleSystemSimulationSpace.World;
                v.x = new ParticleSystem.MinMaxCurve(-0.2f, 0.3f); v.y = new ParticleSystem.MinMaxCurve(1.2f, 2.2f); v.z = new ParticleSystem.MinMaxCurve(-0.2f, 0.2f);
                var col = ps.colorOverLifetime; col.enabled = true; col.color = Fade(0f, 0.15f, 0f, 0.5f);
                var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.7f, 1, 2.2f));
                var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.25f, 0.25f);
                ps.Play();
                // fire glow at the base of each column
                var gp = PS("HellGlowPuff", p + Vector3.up * 0.5f, glowMat);
                var gm = gp.main; gm.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 2.5f); gm.startSpeed = 0f;
                gm.startSize = new ParticleSystem.MinMaxCurve(3f, 5f); gm.maxParticles = 12;
                var ge = gp.emission; ge.rateOverTime = 3f;
                var gs = gp.shape; gs.shapeType = ParticleSystemShapeType.Circle; gs.radius = 1.5f; gs.rotation = new Vector3(90, 0, 0);
                var gc = gp.colorOverLifetime; gc.enabled = true; gc.color = Fade(0f, 0.4f, 0f, 0.5f);
                gp.Play();
            }
            Debug.Log($"UWLook: hell fx: wisps {wispRate}/s, {rimFires} rim fires, {smokeColumns} smoke columns");
        }

        // Hell set dressing (11-13 night, Kido: depth from BIG structures at the rim only, tall black silhouettes along the
        // bottom of the screen, nothing inside the battlefield, existing/downloaded assets only). Prefabs come from
        // Resources/Hell (built by Tools/Underworld/Quality/Build Hell Props from third-party meshes already in the project).
        //   south rim (screen bottom): very tall dead trees and stone pillars, near-black, no shadows -> foreground silhouettes
        //   north rim: the burning ghost gate (Korean gate) under a giant stone pagoda, jangseung guardians, fires
        //   east / west rims: rows of colossal pillars, boulders, dead trees, skull piles, fires in the gaps
        public bool hellSet = true;
        public Rect plazaRect = new Rect(-44f, -29f, 88f, 56f);   // UW_Plaza bounds: rim dressing goes just outside this
        public float seaHeight = -6f;
        public float lilyGlow = 2.4f;
        void HellSet()
        {
            if (!hellSet) return;
            GameObject P(string n) => Resources.Load<GameObject>("Hell/" + n);
            var root = new GameObject("UWLook hell set").transform; root.SetParent(transform, false);
            var rnd = new System.Random(4242);
            float F(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
            // pass 3 (QA_02 #8): off the plaza the raycast hits nothing - stand on the lava sea, not float at plaza height
            // pass 4: cast from just above the walk surface and ignore triggers (from 40 m the ray hit room trigger volumes,
            // so small dressing like the lilies ended up floating out of view)
            // (the plaza has no collider: on the plaza the walk surface is y = 0, off it the lava sea)
            float Ground(float x, float z) => Physics.Raycast(new Vector3(x, 3f, z), Vector3.down, out var h, 20f, ~0, QueryTriggerInteraction.Ignore) ? h.point.y
                : plazaRect.Contains(new Vector2(x, z)) ? 0f : seaHeight;
            int placed = 0;
            GameObject Put(string n, float x, float z, float yaw, float scale, bool shadows, float sink = 0f)
            {
                var pf = P(n); if (!pf) return null;
                var go = Instantiate(pf, root);
                go.transform.SetPositionAndRotation(new Vector3(x, Ground(x, z) - sink, z), Quaternion.Euler(0f, yaw, 0f));
                go.transform.localScale = Vector3.one * scale;
                foreach (var r in go.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
                placed++;
                return go;
            }
            float xMin = plazaRect.xMin, xMax = plazaRect.xMax, zMin = plazaRect.yMin, zMax = plazaRect.yMax;
            string[] trees = { "DeadTree0", "DeadTree1", "DeadTree2", "DeadTree3" };

            // south rim: foreground silhouettes (the camera looks north, so these stand between the camera and the arena)
            // 1st capture: pillars here read as huge black slabs over half the screen. Only spindly dead trees, further out
            // and less tall, so they frame the bottom edge without covering the arena.
            for (float x = xMin - 6f; x <= xMax + 6f; x += F(8f, 13f))
                Put(trees[rnd.Next(4)], x, zMin - F(7f, 12f), F(0, 360), F(5.5f, 6.5f), false, 0f);   // pass 3: rooted on the sea (6 m lower), so they frame the bottom edge without crossing the arena
            // north rim: ghost gate + pagoda + guardians
            float gx = plazaRect.center.x, gz = zMax + 10f;   // 1st capture: at +6 / x1.5 the gate was a black wall over the top of the screen
            Put("Gate", gx, gz, 90f, 1.7f, true);   // pass 3: rises out of the lava sea (6 m below), so bigger
            Put("Pagoda", gx + 2f, zMax + 30f, F(0, 360), 0.6f, true);
            Put("Jangseung", gx - 8f, zMax - 0.3f, 180f, 5f, true); Put("Jangseung", gx + 8f, zMax - 0.3f, 180f, 5f, true);   // on the lip (thin posts)
            for (float x = xMin - 4f; x <= xMax + 4f; x += F(9f, 14f))
            {
                if (Mathf.Abs(x - gx) < 14f) continue;
                Put(trees[rnd.Next(4)], x, zMax + F(6f, 14f), F(0, 360), F(6f, 8f), true);   // pass 3: north pillars read as giant black slabs; dead trees only   // pass 3: rooted on the sea
            }
            // east / west rims: colossal pillar rows, boulders, trees
            foreach (float side in new[] { -1f, 1f })
            {
                float x0 = side < 0 ? xMin - 7f : xMax + 7f;
                for (float z = zMin; z <= zMax; z += F(8f, 12f))
                {
                    float x = x0 + side * F(0f, 6f);
                    // pass 2: from the high camera anything tall on the east / west rims projects across the screen as a long
                    // black slab (pillars, then trees). Kido: tall silhouettes only at the bottom / outer edge. Sides: low rock only.
                    if (rnd.NextDouble() < 0.6) Put("Boulder", x, z, F(0, 360), F(4.5f, 7f), false, 0f);   // pass 3: basalt crags rising from the sea
                }
            }
            // review_01 #3: the plaza ends in ruler-straight edges; broken basalt boulders along the lip, just outside it
            // pass 2: a regular row of 5-7 m boulders read as black beads hiding the north backdrop. Smaller, irregular, sides
            // and south only (the gate / jangseung own the north lip)
            for (float x = xMin; x <= xMax; x += F(5f, 11f)) if (rnd.NextDouble() < 0.6) Put("Boulder", x, zMin - F(4.5f, 6.5f), F(0, 360), F(4f, 6.5f), false, 0f);
            for (float z = zMin; z <= zMax; z += F(5f, 11f)) { if (rnd.NextDouble() < 0.6) Put("Boulder", xMin - F(4.5f, 6.5f), z, F(0, 360), F(4f, 6.5f), false, 0f); if (rnd.NextDouble() < 0.6) Put("Boulder", xMax + F(4.5f, 6.5f), z, F(0, 360), F(4f, 6.5f), false, 0f); }
            // skull piles at the four corners, just outside the play area
            foreach (var (cx, cz) in new[] { (xMin - 3f, zMin - 3f), (xMax + 3f, zMin - 3f), (xMin - 3f, zMax + 3f), (xMax + 3f, zMax + 3f) })
                Put("Skulls", cx, cz, F(0, 360), 1.6f, false);

            // pass 4 (final_03 #3): the plaza mesh has a 45-degree chamfer at its NE corner; a basalt crag rising from the sea
            // fills the notch so it no longer reads as a cut
            // pass 5 (final_04 #1): break the ruler-straight plaza lip at gameplay zoom - a dense row of basalt shoulders
            // rising from the sea whose tops just reach the lip (dome profile: they stay below the walk surface inside it)
            for (float x = xMin - 2f; x <= xMax + 2f; x += F(3f, 6f))
            {
                if (Mathf.Abs(x - gx) > 15f) Put("Boulder", x, zMax + F(0.4f, 1.4f), F(0, 360), F(4.3f, 6f), false, 0f);
                Put("Boulder", x, zMin - F(0.4f, 1.4f), F(0, 360), F(4.3f, 6f), false, 0f);
            }
            for (float z = zMin; z <= zMax; z += F(3f, 6f))
            {
                Put("Boulder", xMin - F(0.4f, 1.4f), z, F(0, 360), F(4.3f, 6f), false, 0f);
                Put("Boulder", xMax + F(0.4f, 1.4f), z, F(0, 360), F(4.3f, 6f), false, 0f);
            }
            // (tried a crag in the NE chamfer: read as a black hole - off)
            // pass 4: 피안화 (red spider lily) clumps on the charred rim strip just inside the plaza edge, thickest on the gate
            // approach. Procedural mesh (UWSpiderLily), no colliders, knee-high: ground dressing, not an obstacle.
            var lilySh = Shader.Find("Underworld/Lily");
            if (lilySh)
            {
                var lilyMat = new Material(lilySh) { hideFlags = HideFlags.DontSave, enableInstancing = false };
                lilyMat.SetFloat("_Glow", lilyGlow);
                int clumps = 0;
                void Lily(float x, float z, int count, float r)
                {
                    var go = new GameObject("SpiderLily"); go.transform.SetParent(root, false);
                    go.transform.position = new Vector3(x, Ground(x, z) + 0.01f, z);
                    go.AddComponent<MeshFilter>().sharedMesh = UWSpiderLily.Clump(count, r, rnd.Next(), 1f);
                    var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = lilyMat; mr.shadowCastingMode = ShadowCastingMode.Off;
                    clumps++;
                }
                for (float x = xMin + 2f; x <= xMax - 2f; x += F(4f, 8f))
                {
                    bool approach = Mathf.Abs(x - gx) < 12f;
                    if (approach || rnd.NextDouble() < 0.55) Lily(x, zMax - F(0.6f, 1.8f), approach ? 14 : rnd.Next(6, 11), approach ? 1.6f : F(0.8f, 1.4f));
                    if (rnd.NextDouble() < 0.45) Lily(x, zMin + F(0.6f, 1.8f), rnd.Next(6, 11), F(0.8f, 1.4f));
                }
                for (float z = zMin + 2f; z <= zMax - 2f; z += F(4f, 8f))
                {
                    if (rnd.NextDouble() < 0.5) Lily(xMin + F(0.6f, 1.8f), z, rnd.Next(6, 11), F(0.8f, 1.4f));
                    if (rnd.NextDouble() < 0.5) Lily(xMax - F(0.6f, 1.8f), z, rnd.Next(6, 11), F(0.8f, 1.4f));
                }
                var dbgM = UWSpiderLily.Plant();
                Debug.Log($"UWLook: {clumps} spider lily clumps; plant v{dbgM.vertexCount} t{dbgM.triangles.Length / 3} bounds {dbgM.bounds}; shader ok {lilySh.isSupported}; first at {(root.Find("SpiderLily") ? root.Find("SpiderLily").position.ToString() : "-")}");
            }

            // fires at the gate and along the north / side rims (lights + additive flame puffs + embers)
            var fires = new List<Vector3> { new(gx - 5f, 0, gz - 2f), new(gx + 5f, 0, gz - 2f), new(gx, 0, zMax + 18f) };
            for (int i = 0; i < 8; i++) { float a = F(0f, Mathf.PI * 2f); var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a)); if (d.y < -0.3f) continue; fires.Add(Rim3(d, F(4f, 9f))); }
            foreach (var f in fires) Fire(root, new Vector3(f.x, Ground(f.x, f.z), f.z));
            // pass 4 (final_03 #4, "hall reads as a box"): the roof burns along its ridge, and a high warm light picks out the
            // roof tiles so the hall reads as a building from the top-down camera
            foreach (var rx in new[] { -9f, 0f, 9f }) Fire(root, new Vector3(gx + rx, seaHeight + 12.5f, gz + F(-1.5f, 1.5f)));
            {
                var lg = new GameObject("GateRoofLight"); lg.transform.SetParent(root, false); lg.transform.position = new Vector3(gx, seaHeight + 24f, gz - 6f);
                var l = lg.AddComponent<Light>(); l.type = LightType.Spot; l.spotAngle = 70f; l.innerSpotAngle = 30f; l.range = 40f; l.intensity = 2500f;   // pass 5: 120 did not reach the roof
                l.color = new Color(1f, 0.45f, 0.2f); l.shadows = LightShadows.None; l.renderMode = LightRenderMode.ForcePixel;
                lg.transform.rotation = Quaternion.LookRotation(new Vector3(gx, seaHeight + 10f, gz) - lg.transform.position);
            }
            // lava sea filling the void around and below the plaza: everything beyond the arena edge glows from below
            // 11-14: surface from the downloaded Lava Vol.04 maps (HellFX/UW_LavaField); the old procedural sea is the fallback
            var sea = Resources.Load<Material>("HellFX/UW_LavaField");
            if (!sea) sea = Resources.Load<Material>("UW_LavaSea");
            if (sea)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(q.GetComponent<Collider>());
                q.name = "UWLook lava sea"; q.transform.SetParent(root, false);
                q.transform.position = new Vector3(arenaRect.center.x, seaHeight, arenaRect.center.y);
                q.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                q.transform.localScale = new Vector3(320f, 320f, 1f);
                var mr = q.GetComponent<MeshRenderer>(); mr.sharedMaterial = sea; mr.shadowCastingMode = ShadowCastingMode.Off;
                // big warm glow welling up from the sea onto the plaza edges / props
                for (int i = 0; i < seaGlows; i++)
                {
                    float a2 = i / (float)seaGlows * Mathf.PI * 2f + 0.3f; var d = new Vector2(Mathf.Cos(a2), Mathf.Sin(a2));
                    var lp = Rim3(d, 10f); lp.y = seaHeight + 3f;
                    var lg = new GameObject("SeaGlow"); lg.transform.SetParent(root, false); lg.transform.position = lp;
                    var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.color = new Color(1f, 0.32f, 0.08f); l.range = 26f; l.intensity = 40f;
                    l.shadows = LightShadows.None; l.renderMode = LightRenderMode.ForcePixel;
                }
            }
            FireSea(root, Ground);
            Debug.Log($"UWLook: hell set {placed} props, {fires.Count} fires, {fireLights} fire lights");
        }

        Vector3 Rim3(Vector2 d, float outward)
        {
            var c = plazaRect.center;
            float tx = Mathf.Abs(d.x) > 1e-3f ? plazaRect.width * 0.5f / Mathf.Abs(d.x) : 1e9f;
            float tz = Mathf.Abs(d.y) > 1e-3f ? plazaRect.height * 0.5f / Mathf.Abs(d.y) : 1e9f;
            var e = c + d * (Mathf.Min(tx, tz) + outward);
            return new Vector3(e.x, 0f, e.y);
        }

        // ---------------------------------------------------------------- 11-14 sea of fire (Kido: burning corpses, lava)
        // Flames are the downloaded Skulmovski flame flipbook (16 x 5 frames, HellFX/UW_Flame) on plain Shuriken systems,
        // smoke = its smoke layer sprite, sparks = its spark sprite. Corpses = "Zombie: The Burnt" posed lying (HellFX/Corpse_*).
        // Nothing here has a collider. Light budget: at most maxFireLights point lights for all fires together.
        public int seaFires = 52, lipFires = 26, rimCorpses = 12, seaCorpses = 16, maxFireLights = 4, seaGlows = 3;
        int fireLights;
        Material flameMat, smokeMat2, sparkMat;
        bool FireMats()
        {
            if (!flameMat) { flameMat = Resources.Load<Material>("HellFX/UW_Flame"); smokeMat2 = Resources.Load<Material>("HellFX/UW_FireSmoke"); sparkMat = Resources.Load<Material>("HellFX/UW_Spark"); }
            return flameMat;
        }
        static Gradient FlameGrad(float hot)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.Lerp(new Color(1f, 0.55f, 0.2f), new Color(1f, 0.8f, 0.5f), hot), 0), new GradientColorKey(new Color(1f, 0.36f, 0.08f), 0.45f), new GradientColorKey(new Color(0.55f, 0.07f, 0.02f), 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.1f), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0, 1) });
            return g;
        }
        ParticleSystem Flames(Transform parent, Vector3 pos, Vector3 box, float sizeMin, float sizeMax, float rate, float hot = 0.5f)
        {
            var go = new GameObject("HellFlames"); go.transform.SetParent(parent, false); go.transform.position = pos;
            var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var r = go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = flameMat; r.shadowCastingMode = ShadowCastingMode.Off;
            r.renderMode = ParticleSystemRenderMode.Billboard; r.pivot = new Vector3(0f, 0.35f, 0f);   // flame base near the emitter, not mid-sprite
            r.maxParticleSize = 2f;
            var mm = ps.main; mm.simulationSpace = ParticleSystemSimulationSpace.World; mm.loop = true; mm.prewarm = true; mm.playOnAwake = true;
            mm.startLifetime = new ParticleSystem.MinMaxCurve(1.1f, 1.7f); mm.startSpeed = 0f;
            mm.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax); mm.startRotation = new ParticleSystem.MinMaxCurve(-0.12f, 0.12f);
            mm.maxParticles = Mathf.CeilToInt(rate * 2f) + 2;
            var em = ps.emission; em.rateOverTime = rate;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = box;
            var v = ps.velocityOverLifetime; v.enabled = true; v.space = ParticleSystemSimulationSpace.World;
            v.x = new ParticleSystem.MinMaxCurve(-0.1f, 0.15f); v.y = new ParticleSystem.MinMaxCurve(0.2f, 0.5f); v.z = new ParticleSystem.MinMaxCurve(-0.1f, 0.1f);
            var col = ps.colorOverLifetime; col.enabled = true; col.color = FlameGrad(hot);
            var ts = ps.textureSheetAnimation; ts.enabled = true; ts.mode = ParticleSystemAnimationMode.Grid; ts.numTilesX = 16; ts.numTilesY = 5;
            ts.animation = ParticleSystemAnimationType.WholeSheet; ts.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0, 1, 1));
            ts.startFrame = new ParticleSystem.MinMaxCurve(0f, 79f); ts.cycleCount = 1;
            ps.Play();
            return ps;
        }
        void Smoke(Transform parent, Vector3 pos, float radius, float size, float rate)
        {
            if (!smokeMat2) return;
            var go = new GameObject("HellFireSmoke"); go.transform.SetParent(parent, false); go.transform.position = pos;
            var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var r = go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = smokeMat2; r.shadowCastingMode = ShadowCastingMode.Off;
            var mm = ps.main; mm.simulationSpace = ParticleSystemSimulationSpace.World; mm.loop = true; mm.prewarm = true;
            mm.startLifetime = new ParticleSystem.MinMaxCurve(4f, 7f); mm.startSpeed = 0f; mm.maxParticles = Mathf.CeilToInt(rate * 7f) + 2;
            mm.startSize = new ParticleSystem.MinMaxCurve(size * 0.7f, size * 1.3f); mm.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f);
            var em = ps.emission; em.rateOverTime = rate;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = radius; sh.rotation = new Vector3(90, 0, 0);
            var v = ps.velocityOverLifetime; v.enabled = true; v.space = ParticleSystemSimulationSpace.World;
            v.x = new ParticleSystem.MinMaxCurve(0.1f, 0.5f); v.y = new ParticleSystem.MinMaxCurve(1.2f, 2.2f); v.z = new ParticleSystem.MinMaxCurve(-0.2f, 0.3f);
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.2f), new GradientAlphaKey(0, 1) });
            col.color = g;
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.6f, 1, 1.8f));
            var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
            ps.Play();
        }
        void Sparks(Transform parent, Vector3 pos, Vector3 box, float rate)
        {
            if (!sparkMat) return;
            var go = new GameObject("HellSparks"); go.transform.SetParent(parent, false); go.transform.position = pos;
            var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var r = go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = sparkMat; r.shadowCastingMode = ShadowCastingMode.Off;
            var mm = ps.main; mm.simulationSpace = ParticleSystemSimulationSpace.World; mm.loop = true; mm.prewarm = true;
            mm.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.4f); mm.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.5f); mm.maxParticles = Mathf.CeilToInt(rate * 2.5f) + 2;
            mm.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.14f);
            var em = ps.emission; em.rateOverTime = rate;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = box;
            var v = ps.velocityOverLifetime; v.enabled = true; v.space = ParticleSystemSimulationSpace.World;
            v.y = new ParticleSystem.MinMaxCurve(1.5f, 3.5f); v.x = new ParticleSystem.MinMaxCurve(-0.4f, 0.6f); v.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(new Color(1f, 0.3f, 0.05f), 1) }, new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 0.6f), new GradientAlphaKey(0, 1) });
            col.color = g;
            var no = ps.noise; no.enabled = true; no.strength = 0.6f; no.frequency = 0.8f;
            ps.Play();
        }
        void FireLight(Transform parent, Vector3 pos, float range, float intensity)
        {
            if (fireLights >= maxFireLights) return;
            fireLights++;
            var lg = new GameObject("HellFireLight"); lg.transform.SetParent(parent, false); lg.transform.position = pos;
            var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.color = new Color(1f, 0.42f, 0.14f); l.range = range; l.intensity = intensity;
            l.shadows = LightShadows.None; l.renderMode = LightRenderMode.ForcePixel;
            var fl = lg.AddComponent<UWFlicker>(); fl.baseIntensity = l.intensity; fl.amount = 0.25f;
        }

        // a fire (gate, rims, roof): the downloaded flame flipbook + smoke + sparks
        void Fire(Transform root, Vector3 p)
        {
            if (!FireMats()) return;
            var go = new GameObject("HellFire"); go.transform.SetParent(root, false); go.transform.position = p;
            Flames(go.transform, p + Vector3.up * 0.1f, new Vector3(2.2f, 0.1f, 2.2f), 2.2f, 3.6f, 9f, 0.7f);
            Smoke(go.transform, p + Vector3.up * 2.5f, 1.2f, 3.5f, 1.2f);
            Sparks(go.transform, p + Vector3.up * 0.5f, new Vector3(2f, 0.2f, 2f), 6f);
            FireLight(go.transform, p + Vector3.up * 1.5f, 14f, 18f);
        }

        // the lava sea burns: flame patches scattered over it, burning corpses on the plaza rim strip and adrift in the lava
        void FireSea(Transform root, System.Func<float, float, float> ground)
        {
            if (!FireMats()) { Debug.LogWarning("UWLook: HellFX materials missing (run Tools/Underworld/Quality/Build Hell Fire Assets)"); return; }
            var rnd = new System.Random(1414);
            float F(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
            var fx = new GameObject("UWLook fire sea").transform; fx.SetParent(root, false);
            var cps = new[] { Resources.Load<GameObject>("HellFX/Corpse_Back"), Resources.Load<GameObject>("HellFX/Corpse_Front"), Resources.Load<GameObject>("HellFX/Corpse_Side") };
            int nc = 0;
            void Corpse(Vector3 at, float sink, float flameRate)
            {
                var pf = cps[rnd.Next(cps.Length)]; if (!pf) return;
                var go = Instantiate(pf, fx);
                go.transform.SetPositionAndRotation(at - Vector3.up * sink, Quaternion.Euler(0f, F(0f, 360f), 0f));
                go.transform.localScale = Vector3.one * F(0.95f, 1.1f);
                nc++;
                if (flameRate > 0f)
                {
                    Flames(go.transform, at + Vector3.up * 0.15f, new Vector3(0.9f, 0.2f, 1.5f), 0.9f, 1.7f, flameRate, 0.4f);
                    if (rnd.NextDouble() < 0.5) Smoke(go.transform, at + Vector3.up * 1.8f, 0.5f, 2.2f, 0.6f);
                }
            }
            float xMin = plazaRect.xMin, xMax = plazaRect.xMax, zMin = plazaRect.yMin, zMax = plazaRect.yMax;
            Vector3 OnRimStrip()
            {
                // 0.8-3 m inside a random plaza edge (lilies share the strip; corpses have no colliders)
                int side = rnd.Next(4); float t = F(0.05f, 0.95f), d = F(0.8f, 3f);
                float x = side == 0 ? xMin + d : side == 1 ? xMax - d : Mathf.Lerp(xMin, xMax, t);
                float z = side == 2 ? zMin + d : side == 3 ? zMax - d : Mathf.Lerp(zMin, zMax, t);
                if (side == 3 && Mathf.Abs(x - plazaRect.center.x) < 12f) x += 24f * Mathf.Sign(x - plazaRect.center.x + 0.01f);   // keep the gate approach clear
                return new Vector3(x, ground(x, z), z);
            }
            Vector3 OnSea(float outMin, float outMax)
            {
                float a = F(0f, Mathf.PI * 2f);
                var p = Rim3(new Vector2(Mathf.Cos(a), Mathf.Sin(a)), F(outMin, outMax)); p.y = seaHeight; return p;
            }
            for (int i = 0; i < rimCorpses; i++) Corpse(OnRimStrip(), 0.05f, F(4f, 7f));
            for (int i = 0; i < seaCorpses; i++) Corpse(OnSea(2f, 16f), 0.25f, F(3f, 6f));
            // flame patches on the lava: 2-6 m wide fields of 2-5 m flames
            for (int i = 0; i < seaFires; i++)
            {
                var p = OnSea(0.5f, 16f);   // pass 2: most of the sea beyond ~16 m is never on screen
                float w = F(2.5f, 6f);
                Flames(fx, p, new Vector3(w, 0.2f, w * F(0.5f, 1f)), 2.6f, 5.5f, w * 2.4f, F(0.3f, 0.8f));
                if (i % 3 == 0) Smoke(fx, p + Vector3.up * 3f, w * 0.4f, 5f, 1.2f);
                if (i % 2 == 0) Sparks(fx, p + Vector3.up * 0.5f, new Vector3(w, 0.2f, w), 4f);
            }
            // pass 2: the boulder band along the plaza lip hid the sea from the gameplay camera (south edge read as a black
            // wall): flames burning on the lip itself, just outside the walkable plaza, at plaza height
            for (int i = 0; i < lipFires; i++)
            {
                float a = F(0f, Mathf.PI * 2f); var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                if (d.y > 0.5f && Mathf.Abs(d.x) < 0.25f) continue;   // gate side has its own fires
                var p = Rim3(d, F(0.8f, 4f)); p.y = 0.2f;
                float w = F(2f, 4.5f);
                Flames(fx, p, new Vector3(w, 0.2f, w * 0.6f), 2.2f, 4.2f, w * 2.2f, F(0.4f, 0.8f));
                if (i % 4 == 0) Smoke(fx, p + Vector3.up * 3f, w * 0.4f, 4f, 0.8f);
            }
            Debug.Log($"UWLook: fire sea {seaFires} flame patches, {lipFires} lip fires, {nc} corpses");
        }

        // Kido 11-13: the stage's first-draft primitive props (my own placeholder pagoda, gate, statues, rocks, walls, trees,
        // shrubs, lantern posts) read awkward; hidden here (renderers only, their lights stay) and replaced by HellSet.
        static readonly string[] OldProps = { "UW_Pagoda", "UW_Gate", "UW_Statue", "UW_RockPillar", "UW_DeadTree_", "UW_Shrub_", "UW_Wall_", "UW_Fence", "UW_Rock_", "UW_LanternLow", "UW_LanternPole", "UW_LanternTall", "UW_BridgeWide", "UW_Bridge", "GateDais", "Cylinder" };   // pass 6: primitive candle posts on the plaza   // pass 3: plank boardwalk off the west edge read as a level-editor cut
        void HideOldProps()
        {
            int n = 0;
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (r.gameObject.scene != gameObject.scene) continue;
                for (var t = r.transform; t; t = t.parent)
                {
                    bool hit = false;
                    foreach (var o in OldProps) if (t.name.StartsWith(o)) { hit = true; break; }
                    if (hit) { r.enabled = false; n++; break; }
                }
            }
            // the violet portal swirl becomes a hell-mouth: molten core, orange body, char edge
            var mpb = new MaterialPropertyBlock();
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (r.gameObject.scene != gameObject.scene || !r.sharedMaterial || r.sharedMaterial.name != "UW_Portal") continue;
                r.GetPropertyBlock(mpb);
                mpb.SetColor("_Core", new Color(1f, 0.78f, 0.45f) * 2f); mpb.SetColor("_Mid", new Color(1f, 0.28f, 0.05f) * 1.5f); mpb.SetColor("_Edge", new Color(0.18f, 0.02f, 0.01f));
                r.SetPropertyBlock(mpb);
            }
            Debug.Log($"UWLook: hid {n} first-draft prop renderers");
        }

        void HideArenaRubble()
        {
            int n = 0;
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (r.gameObject.scene != gameObject.scene || !r.gameObject.name.StartsWith("UW_Rubble")) continue;
                var p = r.transform.position;
                if (arenaRect.Contains(new Vector2(p.x, p.z))) { r.enabled = false; n++; }
            }
            Debug.Log($"UWLook: hid {n} rubble chunks inside the arena");
        }

        void LateUpdate()
        {
            // only while the underworld is the active field: RoomRun can keep this scene loaded under another field
            Live = On && gameObject.scene == UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (vol) vol.weight = Live ? 1f : 0f;
            if (fxRoot) fxRoot.gameObject.SetActive(Live);
            if (Live) TickEmbers();
            Shader.SetGlobalFloat("_DK_SmokeScale", Live ? 0.3f : 0f);   // slam dust (Dokkaebi/Smoke) was blown out to beige blobs under the +2.2 EV grade
            if (Live && Time.unscaledTime >= nextGhostTone) { nextGhostTone = Time.unscaledTime + 0.5f; ToneGhosts(); }
            SharedCounters(Live && retunePlayerPool);
            if (fogMat)
            {
                fogMat.SetColor("_FogCol", FogPre);
                fogMat.SetFloat("_LitScatter", fogLitScatter);
                fogMat.SetFloat("_Base", fogBase); fogMat.SetFloat("_Falloff", Mathf.Max(0.05f, fogFalloff));
                fogMat.SetFloat("_Density", fogDensity); fogMat.SetFloat("_MaxFog", fogMax);
                fogMat.SetVector("_Noise", new Vector4(fogNoiseScale, fogNoiseAmount, fogWind.x, fogWind.y));
            }
        }

        void Inject(ScriptableRenderContext ctx, Camera cam)
        {
            if (!Live || !isActiveAndEnabled || !fogMat) return;
            if (cam.cameraType != CameraType.Game && cam.cameraType != CameraType.SceneView) return;
            var data = cam.GetUniversalAdditionalCameraData();
            if (data == null || data.scriptableRenderer == null) return;
            fogPass.mat = fogMat;
            fogPass.ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Color);
            data.scriptableRenderer.EnqueuePass(fogPass);
        }

        class FogPass : ScriptableRenderPass
        {
            public Material mat;
            public FogPass() { requiresIntermediateTexture = true; }
            class PassData { public TextureHandle src, depth; public Material mat; }

            public override void RecordRenderGraph(RenderGraph rg, ContextContainer frameData)
            {
                var res = frameData.Get<UniversalResourceData>();
                if (res.isActiveTargetBackBuffer || mat == null) return;
                var src = res.activeColorTexture;
                var desc = rg.GetTextureDesc(src);
                desc.name = "UW_HeightFog"; desc.clearBuffer = false; desc.depthBufferBits = 0;
                var dst = rg.CreateTexture(desc);
                using (var b = rg.AddRasterRenderPass<PassData>("UW Height Fog", out var d))
                {
                    d.src = src; d.mat = mat; d.depth = res.cameraDepthTexture;
                    b.UseTexture(src, AccessFlags.Read);
                    if (d.depth.IsValid()) b.UseTexture(d.depth, AccessFlags.Read);
                    b.SetRenderAttachment(dst, 0, AccessFlags.Write);
                    b.AllowGlobalStateModification(true);
                    b.SetRenderFunc((PassData p, RasterGraphContext c) =>
                    {
                        if (p.depth.IsValid()) p.mat.SetTexture("_CameraDepthTexture", p.depth);
                        Blitter.BlitTexture(c.cmd, p.src, new Vector4(1, 1, 0, 0), p.mat, 0);
                    });
                }
                res.cameraColor = dst;
            }
        }
    }
}
