// Dark fantasy pass over every field (Kido 11-11, Tarae Unbound / Path of Exile 2 refs): one look for the whole game.
// Each stage scene keeps its own lighting and post volume; on every visit this layers on top of it:
//   - a high-priority post volume that kills colour (low saturation, cold desaturated filter, heavy contrast, blue-black
//     shadows, a strong vignette) and pulls exposure down;
//   - the sun dimmed and greyed, the ambient dimmed and greyed, a dark low fog;
//   - a few flickering warm fire pools (lantern/brazier light on the ground, no shadows) placed around the arena, so the
//     field reads as a dark place lit by a handful of sources, with the player's own light pool (FieldLighting) on top.
// Nothing in the stage scenes or their assets is changed. Per-field numbers are in Moods below.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace RoomRun
{
    public sealed class StageMood : MonoBehaviour
    {
        public sealed class Mood
        {
            public float exposure = -1f;       // EV added to the stage's own post exposure
            public float saturation = -55f;    // absolute (URP -100..100)
            public float contrast = 24f;
            public Color filter = new Color(0.80f, 0.83f, 0.86f);
            public float sun = 0.45f;          // directional light intensity scale
            public float sunGrey = 0.6f;       // how far the sun colour moves toward grey
            public float ambient = 0.55f;      // ambient scale (on top of FieldLighting's)
            public float ambientGrey = 0.6f;
            public Color fog = new Color(0.10f, 0.11f, 0.12f);
            public float fogDensity = 0.018f;  // 0 or less: keep the stage's own fog
            public int firePools = 16;         // >0: eight stone lanterns lit just outside the arena edge (11-13; was metres between block braziers); 0 = none
            public Color fire = new Color(1f, 0.55f, 0.24f);
            public Color sunColor = Color.clear;   // a > 0: the sun takes this colour (before sunGrey) instead of the stage's
            public float reedAmbCut = 0f;          // ReedField/Reed: cut the flat ambient so the sun and AO shade (_RF_AmbCut)
            public bool reedSunShadow;             // ReedField/Reed: realtime sun shadow on the reeds (_RF_RTShadow)
        }

        // Tuned per field from screenshots; anything not listed uses the defaults above.
        static readonly Dictionary<string, Mood> Moods = new Dictionary<string, Mood>
        {
            { "grass",      new Mood { exposure = -0.9f, saturation = -62f, filter = new Color(0.80f, 0.82f, 0.80f), fog = new Color(0.09f, 0.10f, 0.09f), fogDensity = 0.02f } },
            { "valley",     new Mood { exposure = -0.5f, saturation = -50f } },
            { "village",    new Mood { exposure = -0.3f, saturation = -50f, firePools = 0 } },   // 11-14 Kido: 민가 is abandoned, no lit lanterns
            { "palace",     new Mood { exposure = -0.3f, saturation = -50f } },
            // the underworld drives its own fog every frame (UWEnvironment), so it keeps it; ghost-fire pools are cold blue
            { "underworld", new Mood { exposure = 0.5f, sun = 1.0f, ambient = 1.0f, saturation = -50f, filter = new Color(0.80f, 0.80f, 0.86f), fogDensity = 0f, firePools = 0, fire = new Color(0.45f, 0.75f, 1f) } },   // 11-13 quality pass: no block lanterns, the stage lights its own 26 lanterns (Underworld/UWLook grades above this)
            // 11-11 폐촌 redo: ground was near black (refs: mid-grey readable mud, Tarae ss1); a bit more moon and ambient, less fog
            // 11-12 Kido: much more contrast, darks very dark -> high contrast, low ambient (shade goes near black), moon keeps lit ground readable
            // 11-13 (Kido: "nothing like POE2"): the grey filter and greyed moon flattened the new Megascans ground to one
            // grey; neutral-warm filter, the moon keeps its cold colour against the warm lantern pools
            { "abandoned",  new Mood { exposure = 0.8f, /* 11-14 폐촌 r14: refs median luminance 0.10-0.14, ours ~0.06 */ saturation = -40f, contrast = 40f, filter = new Color(1f, 0.97f, 0.92f), sun = 1.2f, sunGrey = 0.2f, ambient = 0.55f, ambientGrey = 0.3f, firePools = 0, fog = new Color(0.16f, 0.18f, 0.21f), fogDensity = 0.009f } },
            // 11-14 (Kido: "하얀 빛대신 석양이 지는 느낌으로 좀 붉은 느낌", "전체적으로 붕 뜨는 느낌 ... 쉐이드가 안들어가는"): a low red
            // sunset sun kept near full (it was x0.45 and greyed, so the flat reed ambient carried everything and nothing
            // was shaded), its colour kept; the reeds take the realtime sun shadows and lose most of their flat ambient
            { "reeds",      new Mood { exposure = -1.25f, saturation = -40f, contrast = 22f, filter = new Color(1f, 0.86f, 0.78f),
                                       sun = 0.95f, sunGrey = 0.12f, sunColor = new Color(1f, 0.52f, 0.3f, 1f), ambient = 0.45f, ambientGrey = 0.35f,
                                       fog = new Color(0.16f, 0.085f, 0.06f), fogDensity = 0.016f, reedAmbCut = 0.55f, reedSunShadow = true } },
            // 11-13 bamboo redo (Kido: C mood, Sekiro bamboo at night): darker materials and a cold moon in the scene, so the
            // grade only lifts it; the old -0.6 EV / -58 sat / green-grey filter turned the floor into one olive mush
            { "bamboo",     new Mood { exposure = 0.15f, saturation = -42f, filter = new Color(0.95f, 0.96f, 0.99f), fire = new Color(0.95f, 0.6f, 0.38f) } },   // r9: darker, softer lantern colour
            // snow plague: has its own stone lanterns and full-screen height fog (SPField), so no fire pools, own fog kept
            // 11-11 redo: the default dim moon (x0.45) and -0.5 EV crushed the snow to dark grey; snow must stay the brightest thing on screen
            { "snow",       new Mood { exposure = 1.35f, saturation = -40f, contrast = 24f, filter = new Color(0.96f, 0.955f, 0.925f), sun = 1f, sunGrey = 0.15f, ambient = 0.55f, ambientGrey = 0.25f, firePools = 0, fogDensity = 0f } },   // d14 (Kido "설원 너무 밝아졌는데 ... 다크 판타지 색감", match TARAE ss5: p50 95, p95 200, snow grey-neutral [187,186,179]; ours read p50 115-140, p95 236-252, warm [231,219,209]): exposure 1.3 -> 0.75, warm filter -> neutral grey, saturation -22 -> -32, contrast 50 -> 42 (ss5 low-mids p25 55, ours 30), filter a touch warm (ours read lilac). v14 (Kido "타래는 눌려있고 안개 낀 느낌, 우리는 너무 쨍해"): contrast 42 -> 24, saturation -40, exposure 0.75 -> 1.35 (the lower contrast and the dirty grey snow albedo; v14b: 1.15 left no bright snow, ss5 ~15 %).   // look6 (11-12): 1.3 (look2: 0.7 -> 1.0, look4 1.15, look5 1.25), contrast 50 had sunk the snow peaks to ~145 (ss5 ~190). r10 (ss5): lighter sepia filter (0.92/0.8 turned the snow beige; the warmth now comes from SP_PostFX split toning on the darks), red kept by SP_PostFX curves
        };
        static readonly Mood Default = new Mood();

        public static Mood Current { get; private set; }

        RunDirector run;
        int sceneSeen = -1;
        readonly List<(Light l, float baseI, float seed)> fires = new List<(Light, float, float)>();

        void Awake() { run = GetComponent<RunDirector>(); }
        // perf A/B (off:StageMood): the volume and the fires go with the component
        GameObject moodGo;
        void OnEnable() { if (moodGo) moodGo.SetActive(true); }
        void OnDisable() { if (moodGo) moodGo.SetActive(false); }

        void LateUpdate()
        {
            if (!run) return;
            int scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;
            if (scene != sceneSeen && !run.Transitioning && run.Room != null) { sceneSeen = scene; Apply(run.Room.id); }
            if (fires.Count > 0) PickShadowFires();
            float t = Time.time;
            for (int i = 0; i < fires.Count; i++)
            {
                var (l, baseI, seed) = fires[i];
                if (!l) continue;
                l.intensity = baseI * (0.82f + 0.12f * Mathf.Sin(t * 9.1f + seed) + 0.06f * Mathf.Sin(t * 23.7f + seed * 2.3f));
                for (int s = 0; s < spots.Length; s++)
                    if (spotFire[s] == i && spots[s]) spots[s].intensity = l.intensity * 1.3f;
            }
        }

        // The two fires nearest the player light the ground through a downward spot light with soft shadows instead of
        // their point light: one shadow map each instead of six, so enemies and props throw long shadows away from them.
        readonly Light[] spots = new Light[2];
        readonly int[] spotFire = { -1, -1 };

        void Apply(string id)
        {
            var m = Moods.TryGetValue(id, out var mm) ? mm : Default;
            Current = m;
            fires.Clear();
            spotFire[0] = spotFire[1] = -1; nextShadowPick = 0f;

            // the stage's own exposure, so ours is relative to it
            float baseExposure = 0f;
            int bestPriority = int.MinValue;
            foreach (var v in FindObjectsByType<Volume>(FindObjectsSortMode.None))
            {
                if (!v.isGlobal || !v.sharedProfile || v.priority < bestPriority) continue;
                if (v.sharedProfile.TryGet<ColorAdjustments>(out var ca) && ca.postExposure.overrideState) { baseExposure = ca.postExposure.value; bestPriority = (int)v.priority; }
            }

            var go = moodGo = new GameObject("StageMood");   // lives in the stage scene, so it goes when the stage unloads
            var vol = go.AddComponent<Volume>();
            vol.isGlobal = true; vol.priority = 1000;
            var p = ScriptableObject.CreateInstance<VolumeProfile>();
            p.name = "StageMood (runtime)";
            vol.sharedProfile = p;

            var c = p.Add<ColorAdjustments>(true);
            c.postExposure.Override(baseExposure + m.exposure);
            c.saturation.Override(m.saturation);
            c.contrast.Override(m.contrast);
            c.colorFilter.Override(m.filter);
            c.hueShift.overrideState = false;

            var smh = p.Add<ShadowsMidtonesHighlights>(true);
            smh.shadows.Override(new Vector4(0.86f, 0.92f, 1.08f, -0.12f));     // blue-black shadows
            smh.midtones.Override(new Vector4(0.98f, 0.98f, 1f, -0.04f));
            smh.highlights.Override(new Vector4(1.04f, 1f, 0.94f, 0f));          // fire stays warm

            // no lifted blacks: stage "readable" modes lift them into a milky haze, which reads as grey fog once dimmed
            var lgg = p.Add<LiftGammaGain>(true);
            lgg.lift.Override(new Vector4(1f, 1f, 1f, 0f));
            lgg.gamma.Override(new Vector4(1f, 1f, 1f, 0f));

            var vig = p.Add<Vignette>(true);
            vig.intensity.Override(0.42f); vig.smoothness.Override(0.55f);
            vig.color.Override(new Color(0.01f, 0.012f, 0.018f));

            var grain = p.Add<FilmGrain>(true);
            grain.intensity.Override(0.16f); grain.response.Override(0.8f);

            // sun and ambient: dimmer and greyer
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (l.type != LightType.Directional || l.transform.IsChildOf(transform)) continue;
                l.intensity *= m.sun;
                if (m.sunColor.a > 0f) l.color = new Color(m.sunColor.r, m.sunColor.g, m.sunColor.b);
                l.color = Grey(l.color, m.sunGrey);
            }
            Shader.SetGlobalFloat("_RF_AmbCut", m.reedAmbCut);
            Shader.SetGlobalFloat("_RF_RTShadow", m.reedSunShadow ? 1f : 0f);
            RenderSettings.ambientLight = Grey(RenderSettings.ambientLight, m.ambientGrey) * m.ambient;
            RenderSettings.ambientSkyColor = Grey(RenderSettings.ambientSkyColor, m.ambientGrey) * m.ambient;
            RenderSettings.ambientEquatorColor = Grey(RenderSettings.ambientEquatorColor, m.ambientGrey) * m.ambient;
            RenderSettings.ambientGroundColor = Grey(RenderSettings.ambientGroundColor, m.ambientGrey) * m.ambient;
            RenderSettings.ambientIntensity *= m.ambient;
            // a probe built from colours has to be rebuilt by hand
            var sh = RenderSettings.ambientProbe; sh *= m.ambient; RenderSettings.ambientProbe = sh;

            if (m.fogDensity > 0f)
            {
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.fogColor = m.fog;
                RenderSettings.fogDensity = m.fogDensity;
            }

            // fire pools: braziers on a loose grid over the arena (one every ~firePools metres), each a warm flickering
            // light on the ground. Only the two nearest the player cast (soft) shadows, so props and enemies throw long
            // shadows away from them without paying for shadows on every light.
            if (m.firePools > 0)
            {
                for (int s = 0; s < spots.Length; s++)
                {
                    var sg = new GameObject("FireShadowSpot");
                    sg.transform.SetParent(go.transform, false);
                    var sl = sg.AddComponent<Light>();
                    sl.type = LightType.Spot; sl.spotAngle = 165f; sl.innerSpotAngle = 90f; sl.range = 11f;
                    sl.shadows = LightShadows.Soft; sl.shadowStrength = 0.92f; sl.shadowBias = 0.04f; sl.shadowNearPlane = 0.2f;
                    sl.renderMode = LightRenderMode.ForcePixel; sl.enabled = false;
                    spots[s] = sl;
                }
                // 11-13 QA (Kido's arena rule: nothing inside the arena stands up or blocks): the block-built lanterns on a grid
                // inside the arena are gone. Eight real stone lanterns (Sketchfab scan "Oriental_stone_lantern" by kebijini,
                // CC BY 4.0, Resources/StageLantern) stand 1.2 m outside the arena edge, two per side at the quarter points
                // (the door gaps sit at the side centres), each with its warm light leaning into the arena. 8 realtime lights.
                var c0 = run.AreaCenter; var size = run.AreaSize;
                var rnd = new System.Random(id.GetHashCode());
                var lanternMesh = LanternMesh();
                var lanternMat = LanternMaterial();
                float hx = size.x * 0.5f + 1.2f, hz = size.y * 0.5f + 1.2f;
                var spotsAt = new List<(Vector3 p, Vector3 inward)>();
                foreach (float t in new[] { -0.5f, 0.5f })
                {
                    spotsAt.Add((new Vector3(t * size.x * 0.5f, 0, hz), Vector3.back));
                    spotsAt.Add((new Vector3(t * size.x * 0.5f, 0, -hz), Vector3.forward));
                    spotsAt.Add((new Vector3(hx, 0, t * size.y * 0.5f), Vector3.left));
                    spotsAt.Add((new Vector3(-hx, 0, t * size.y * 0.5f), Vector3.right));
                }
                foreach (var (off, inward) in spotsAt)
                {
                    var pos = c0 + off;
                    pos.y = Ground.Sample(pos, c0.y);
                    var root = new GameObject("StoneLantern").transform;
                    root.SetParent(go.transform, false);
                    root.position = pos;
                    root.rotation = Quaternion.LookRotation(inward) * Quaternion.Euler(0, ((float)rnd.NextDouble() - 0.5f) * 20f, 0);
                    float lightY = 1.15f;
                    if (lanternMesh && lanternMat)
                    {
                        var b = lanternMesh.bounds;
                        float sc = LanternHeight / Mathf.Max(0.01f, b.size.y);
                        var mg = new GameObject("Mesh", typeof(MeshFilter), typeof(MeshRenderer));
                        mg.transform.SetParent(root, false);
                        mg.transform.localScale = Vector3.one * sc;
                        mg.transform.localPosition = new Vector3(-b.center.x * sc, -b.min.y * sc - 0.06f, -b.center.z * sc);   // slab a little into the ground
                        mg.GetComponent<MeshFilter>().sharedMesh = lanternMesh;
                        var mr = mg.GetComponent<MeshRenderer>();
                        mr.sharedMaterial = lanternMat;
                        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                        lightY = LanternHeight * 0.62f;   // the firebox
                    }
                    var lg = new GameObject("Light");
                    lg.transform.SetParent(root, false);
                    lg.transform.localPosition = new Vector3(0, lightY, 0.9f);   // a little inward so the pool falls on the arena floor
                    var l = lg.AddComponent<Light>();
                    l.type = LightType.Point; l.shadows = LightShadows.None; l.renderMode = LightRenderMode.ForcePixel;
                    l.shadowStrength = 0.9f; l.shadowBias = 0.05f; l.shadowNearPlane = 0.3f;
                    l.color = m.fire; l.range = 14f; l.intensity = 16f;
                    fires.Add((l, l.intensity, (float)rnd.NextDouble() * 10f));
                }
            }
        }

        const float LanternHeight = 1.75f;
        static Mesh lanternMeshCache; static Material lanternMatCache;
        static Mesh LanternMesh()
        {
            if (lanternMeshCache) return lanternMeshCache;
            var go = Resources.Load<GameObject>("StageLantern/KoreanStoneLantern");
            var mf = go ? go.GetComponentInChildren<MeshFilter>() : null;
            return lanternMeshCache = mf ? mf.sharedMesh : null;
        }
        static Material LanternMaterial()
        {
            if (lanternMatCache) return lanternMatCache;
            var sh = Shader.Find("RoomRun/VColLit");
            if (!sh) return null;
            lanternMatCache = new Material(sh) { name = "StageLantern (runtime)" };
            lanternMatCache.SetColor("_Tint", new Color(0.9f, 0.88f, 0.85f));
            return lanternMatCache;
        }

        // the two fires nearest the player get shadows, re-checked a few times a second
        float nextShadowPick;
        void PickShadowFires()
        {
            if (Time.unscaledTime < nextShadowPick || !run.Player) return;
            nextShadowPick = Time.unscaledTime + 0.25f;
            var p = run.Player.transform.position;
            int a = -1, b = -1; float da = float.MaxValue, db = float.MaxValue;
            for (int i = 0; i < fires.Count; i++)
            {
                if (!fires[i].l) continue;
                float d = (fires[i].l.transform.position - p).sqrMagnitude;
                if (d < da) { b = a; db = da; a = i; da = d; } else if (d < db) { b = i; db = d; }
            }
            var pick = new[] { a, b };
            for (int s = 0; s < spots.Length; s++)
            {
                if (!spots[s]) continue;
                int f = pick[s];
                spotFire[s] = f;
                spots[s].enabled = f >= 0;
                if (f < 0) continue;
                spots[s].transform.SetPositionAndRotation(fires[f].l.transform.position, Quaternion.LookRotation(Vector3.down, Vector3.forward));
                spots[s].color = fires[f].l.color;
            }
            for (int i = 0; i < fires.Count; i++)
                if (fires[i].l) fires[i].l.enabled = i != a && i != b;
        }

        static Color Grey(Color c, float k)
        {
            float y = c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
            return new Color(Mathf.Lerp(c.r, y, k), Mathf.Lerp(c.g, y, k), Mathf.Lerp(c.b, y, k), c.a);
        }
    }
}
