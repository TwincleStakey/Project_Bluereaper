// 11-19 대전 앞 volume pilot (Kido approved: ONE map, the PoE2 stone-plaza "volume" rules). A sub-layer of EnvRecompose,
// palace only, built after Palace() and only while PlayerPrefs RR.EnvRecompose.palace.volume = 1 (DEFAULT OFF so the other
// threads' palace captures do not change). Everything it changes at runtime is put back when the room is left / Play ends:
//   1. light: the key (moon) light at ~33 deg from the north-west, so a 1 m object throws a 1.5-1.6 m shadow toward the
//      south-east, INTO the clearing; contrast from LESS ambient in shadow, not more light (mean screen luminance must not
//      rise): LookStudy floors light from _LSSunCol / _LSShadowMul (their own ambient), URP props from RenderSettings ambient
//      + the light. The floor (LookStudy/StageTerrain) does sample the main shadow (LSShadow, keeps 22% of the sun), but the
//      unshadowed local lights filled it; the existing global _RRGroundShadow (RRGroundShadow.hlsl, 0 by default, ShadowPass
//      sets 1) dims those inside the sun shadow, so this layer sets it to 1 while it is on. No shader file is edited.
//   2. tall verticals on the clearing RING (r ~13-17 m of the ~20 m central clearing), 5 groups, irregular spacing, the
//      south-east quarter empty, more on the light (NW) side; heights stepping down 8-9 m / 3.5-4.5 m / 1.5-2 m; bases sunk
//      into rubble + a mud scan; two 장승 flanking the west gate road like a gate.
//   3. the centre stays free (pieces taller than 0.45 m within 9 m are switched off) and shows only the approved floor
//      (11-19 Kido: the stone-slab dais looked weird, removed; PS_DaisSlab files stay on disk, unused).
//   4. off-arena shadow casters: 3 big dead trees / a pillar just outside the west + north-west walls (light side).
//   5. edge: low rubble in front, mid pieces (1.5-3 m) behind it, then the existing hanok wall.
//   6. warm: 2 warm lights at the feet of two tall pieces (lighting them from below), the light budget lowered so the
//      field stays at <= 8 realtime lights; 2 shadowed accents max (the field's own FireShadowSpot pair).
//   7. fog only between layers: <= 7 thin smoke cards behind the north / side walls (<= 6 in any view), none over the court.
// All models are downloaded assets already in the project: Megascans japanese stone pillar (EF_RF_StonePillar), Megascans
// dead pine trunk (EF_RF_DeadTrunk), Sketchfab coniferous dead trees (Hell/DeadTree0/1, Dari, CC BY), 장승 (EF_Jangseung,
// ththqkf, CC BY), stone pagoda (Hell/Pagoda), Oriental stone lantern (StageLantern, kebijini, CC BY), KHS incense burner
// (PP_IncenseB), snags (EF_RF_Snag_*), PalaceStage rubble / rocks / mud scans, Kenney smoke_04 (CC0). Credits in
// Assets/Game/RoomRun/CREDITS.txt.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace RoomRun
{
    public sealed partial class EnvRecompose
    {
        public const string VolumeKey = "RR.EnvRecompose.palace.volume";
        public static bool VolumeOn() => PlayerPrefs.GetInt(VolumeKey, 0) != 0;

        // ------------------------------------------------------------------ tuning (editor: ArtSource/EnvRecompose/volume_tune.txt "key value")
        static readonly Dictionary<string, float> vt = new Dictionary<string, float>
        {
            { "elev", 33f }, { "yaw", 135f },        // key light: elevation / heading (135 = from the north-west, shadows to the SE)
            { "lssun", 1.38f }, { "lsamb", 0.42f },  // LookStudy floor: _LSSunCol x, _LSShadowMul x
            { "key", 1.1f }, { "amb", 0.55f },      // URP props: key light intensity x, RenderSettings ambient x
            { "gsh", 1f },                           // _RRGroundShadow while on
            { "warmI", 3.2f }, { "warmR", 7f },      // warm base lights
            { "budget", 5f },                        // field point / spot lights kept (+2 warm + the player's radius light = 8)
            { "fogA", 0.22f },                       // fog card alpha
            { "dim", 0.8f },
            { "sdist", 85f },                        // URP shadow distance while on (the 1.6x camera is ~36 m from the player; 50 cut the far casters)                         // value of the placed verticals (base colour x)
        };
        static float V(string k) => vt.TryGetValue(k, out var v) ? v : 0f;
        static void ReadVolumeTune()
        {
#if UNITY_EDITOR
            try
            {
                var f = System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, "ArtSource", "EnvRecompose", "volume_tune.txt");
                if (!System.IO.File.Exists(f)) return;
                foreach (var raw in System.IO.File.ReadAllLines(f))
                {
                    var p = raw.Trim().Split(' ');
                    if (p.Length >= 2 && !p[0].StartsWith("#") && float.TryParse(p[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v)) vt[p[0]] = v;
                }
            }
            catch (System.Exception e) { Debug.LogWarning("[EnvRecompose] volume_tune: " + e.Message); }
#endif
        }

        // ------------------------------------------------------------------ the runtime light state (asserted every frame, put back on leave)
        static bool volActive; static string volKey;
        static Light volSun;
        static Quaternion sunRotBase, sunRotW; static float sunIntBase, sunIntW;
        static Vector4 lsSunBase, lsSunW, lsAmbBase, lsAmbW;
        static Color ambLBase, ambSBase, ambEBase, ambGBase, ambSW; static float ambIBase; static SphericalHarmonicsL2 probeBase;
        static float gshBase, sdistBase = -1f;
        static readonly List<(Vector3 p, float h)> volCasters = new List<(Vector3, float)>();   // placed verticals (world base, height)

        static Light FindSun()
        {
            if (RenderSettings.sun && RenderSettings.sun.type == LightType.Directional) return RenderSettings.sun;
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l && l.enabled && l.type == LightType.Directional) return l;
            return null;
        }

        static void VolumeApplyLight(string key)
        {
            volSun = FindSun();
            if (volSun)
            {
                sunRotBase = volSun.transform.rotation; sunIntBase = volSun.intensity;
                sunRotW = Quaternion.Euler(V("elev"), V("yaw"), 0f); sunIntW = sunIntBase * V("key");
                volSun.transform.rotation = sunRotW; volSun.intensity = sunIntW;
                if (volSun.shadows == LightShadows.None) volSun.shadows = LightShadows.Soft;
            }
            lsSunBase = Shader.GetGlobalVector("_LSSunCol"); lsAmbBase = Shader.GetGlobalVector("_LSShadowMul");
            lsSunW = lsSunBase * V("lssun"); lsAmbW = lsAmbBase * V("lsamb");
            Shader.SetGlobalVector("_LSSunCol", lsSunW); Shader.SetGlobalVector("_LSShadowMul", lsAmbW);
            ambLBase = RenderSettings.ambientLight; ambSBase = RenderSettings.ambientSkyColor; ambEBase = RenderSettings.ambientEquatorColor;
            ambGBase = RenderSettings.ambientGroundColor; ambIBase = RenderSettings.ambientIntensity; probeBase = RenderSettings.ambientProbe;
            float a = V("amb");
            RenderSettings.ambientLight = ambLBase * a; RenderSettings.ambientSkyColor = ambSW = ambSBase * a;
            RenderSettings.ambientEquatorColor = ambEBase * a; RenderSettings.ambientGroundColor = ambGBase * a;
            var sh = probeBase; sh *= a; RenderSettings.ambientProbe = sh;
            gshBase = Shader.GetGlobalFloat("_RRGroundShadow");
            Shader.SetGlobalFloat("_RRGroundShadow", V("gsh"));
            if (GraphicsSettings.currentRenderPipeline is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset urp && V("sdist") > 0f)
            { sdistBase = urp.shadowDistance; urp.shadowDistance = V("sdist"); }
            volActive = true; volKey = key;
            Debug.Log($"[EnvRecompose]   volume light: sun {(volSun ? volSun.name : "none")} rot {sunRotBase.eulerAngles} -> ({V("elev")},{V("yaw")}) int {sunIntBase:0.00} -> {sunIntW:0.00}; LS sun {(Vector3)lsSunBase} -> {(Vector3)lsSunW} amb {(Vector3)lsAmbBase} -> {(Vector3)lsAmbW}; RS ambient x{a} (mode {RenderSettings.ambientMode}); _RRGroundShadow {gshBase} -> {V("gsh")}");
        }

        // another layer may write the same values after us (StageMood on a late frame, ShadowPass): if a value is not what
        // we wrote, take it as the new base and scale it again
        static void VolumeAssert()
        {
            if (volSun)
            {
                if (Quaternion.Angle(volSun.transform.rotation, sunRotW) > 0.01f) { sunRotBase = volSun.transform.rotation; volSun.transform.rotation = sunRotW; }
                if (Mathf.Abs(volSun.intensity - sunIntW) > 1e-4f) { sunIntBase = volSun.intensity; sunIntW = sunIntBase * V("key"); volSun.intensity = sunIntW; }
            }
            var s = Shader.GetGlobalVector("_LSSunCol"); if ((s - lsSunW).sqrMagnitude > 1e-7f) { lsSunBase = s; lsSunW = s * V("lssun"); Shader.SetGlobalVector("_LSSunCol", lsSunW); }
            var d = Shader.GetGlobalVector("_LSShadowMul"); if ((d - lsAmbW).sqrMagnitude > 1e-7f) { lsAmbBase = d; lsAmbW = d * V("lsamb"); Shader.SetGlobalVector("_LSShadowMul", lsAmbW); }
            if (RenderSettings.ambientSkyColor != ambSW)
            {
                float a = V("amb");
                ambLBase = RenderSettings.ambientLight; ambSBase = RenderSettings.ambientSkyColor; ambEBase = RenderSettings.ambientEquatorColor;
                ambGBase = RenderSettings.ambientGroundColor; probeBase = RenderSettings.ambientProbe;
                RenderSettings.ambientLight = ambLBase * a; RenderSettings.ambientSkyColor = ambSW = ambSBase * a;
                RenderSettings.ambientEquatorColor = ambEBase * a; RenderSettings.ambientGroundColor = ambGBase * a;
                var sh = probeBase; sh *= a; RenderSettings.ambientProbe = sh;
            }
            if (Mathf.Abs(Shader.GetGlobalFloat("_RRGroundShadow") - V("gsh")) > 1e-4f) Shader.SetGlobalFloat("_RRGroundShadow", V("gsh"));
        }

        static void VolumeRestore()
        {
            if (!volActive) return;
            volActive = false;
            if (volSun) { if (Quaternion.Angle(volSun.transform.rotation, sunRotW) < 0.01f) volSun.transform.rotation = sunRotBase; if (Mathf.Abs(volSun.intensity - sunIntW) < 1e-4f) volSun.intensity = sunIntBase; }
            if ((Shader.GetGlobalVector("_LSSunCol") - lsSunW).sqrMagnitude < 1e-6f) Shader.SetGlobalVector("_LSSunCol", lsSunBase);
            if ((Shader.GetGlobalVector("_LSShadowMul") - lsAmbW).sqrMagnitude < 1e-6f) Shader.SetGlobalVector("_LSShadowMul", lsAmbBase);
            if (RenderSettings.ambientSkyColor == ambSW)
            {
                RenderSettings.ambientLight = ambLBase; RenderSettings.ambientSkyColor = ambSBase; RenderSettings.ambientEquatorColor = ambEBase;
                RenderSettings.ambientGroundColor = ambGBase; RenderSettings.ambientIntensity = ambIBase; RenderSettings.ambientProbe = probeBase;
            }
            Shader.SetGlobalFloat("_RRGroundShadow", gshBase);
            if (sdistBase > 0f && GraphicsSettings.currentRenderPipeline is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset urp) { urp.shadowDistance = sdistBase; sdistBase = -1f; }
            volSun = null; volCasters.Clear();
            Debug.Log("[EnvRecompose] volume light restored");
        }

        // called from LateUpdate every frame, before the build check
        void VolumeUpkeep(string key)
        {
            if (!volActive) return;
            if (key != volKey) { VolumeRestore(); return; }
            VolumeAssert();
        }
        void OnDestroy() => VolumeRestore();

        // ================================================================== the build
        static int volPieces;
        static void PalaceVolume(Scene scene)
        {
            int before = count;
            volCasters.Clear();
            ClearCentre(scene);
            Ring();
            OffArena();
            EdgeLayers();
            FogCards();
            VolumeApplyLight(CurrentKey());
            volPieces = count - before;
            Debug.Log($"[EnvRecompose]   volume: {volPieces} pieces, {volCasters.Count} tall casters");
        }
        static string CurrentKey()
        {
            var run = RunDirector.Instance;
            return run && run.Room != null ? run.Room.id + "@" + SceneManager.GetActiveScene().handle : "";
        }

        // 3. pieces of the field taller than 0.45 m within 9 m of the centre (the combat space) are switched off
        static void ClearCentre(Scene scene)
        {
            var names = new List<string>();
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (!r || !r.enabled || !r.gameObject.activeInHierarchy || r.gameObject.scene != scene || r.transform.IsChildOf(root)) continue;
                if (r is ParticleSystemRenderer || r.GetComponentInParent<PlayerController>() || r.GetComponentInParent<Light>() || r.GetComponentInParent<Health>()) continue;
                var b = r.bounds; if (b.size.x > 6f || b.size.z > 6f) continue;      // the floor tiles / mounds
                var p = b.center - off;
                if (new Vector2(p.x, p.z).magnitude > 9f) continue;
                float top = b.max.y - GroundAt(p.x, p.z);
                if (top <= 0.45f) continue;
                Hide(r.gameObject); names.Add($"{r.name} {top:0.0}m");
            }
            if (names.Count > 0) Debug.Log("[EnvRecompose]   volume centre cleared: " + string.Join(", ", names));
        }

        // a matte value-stepped copy of every material (smoothness <= 0.08, metallic 0, base colour x k)
        static readonly Dictionary<(Material, float), Material> matteMats = new Dictionary<(Material, float), Material>();
        static void Matte(GameObject go, float k)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                var ms = r.sharedMaterials;
                for (int i = 0; i < ms.Length; i++)
                {
                    var m = ms[i]; if (!m) continue;
                    if (!matteMats.TryGetValue((m, k), out var d) || !d)
                    {
                        d = new Material(m) { name = m.name + "_vol" };
                        foreach (var c in new[] { "_BaseColor", "_Color", "_Tint" })
                            if (d.HasProperty(c)) { var t = d.GetColor(c); d.SetColor(c, new Color(t.r * k, t.g * k, t.b * k, t.a)); }
                        foreach (var g in new[] { "_Smoothness", "_Glossiness", "_GlossMapScale" }) if (d.HasProperty(g)) d.SetFloat(g, Mathf.Min(d.GetFloat(g), 0.08f));
                        if (d.HasProperty("_Metallic")) d.SetFloat("_Metallic", 0f);
                        if (d.HasProperty("_SpecularHighlights")) { d.SetFloat("_SpecularHighlights", 0f); d.EnableKeyword("_SPECULARHIGHLIGHTS_OFF"); }
                        matteMats[(m, k)] = d;
                    }
                    ms[i] = d;
                }
                r.sharedMaterials = ms;
            }
        }

        // a vertical: scaled to h, `sink` of it under the floor, a little lean, casting
        static GameObject Vert(string path, float x, float z, float h, float yaw, float sink = 0.08f, float lean = 2.5f, bool cast = true)
        {
            var g = Prop(path, x, z, yaw, 1f, 0f, R(-lean, lean), 0f);
            if (!g) return null;
            if (path.StartsWith("StageLantern/")) LanternMat(g);
            var rs = g.GetComponentsInChildren<Renderer>(); if (rs.Length == 0) return g;
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            if (b.size.y > 1e-3f) g.transform.localScale *= h / b.size.y;
            b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            float gy = GroundAt(x, z);
            g.transform.position += Vector3.up * (gy - b.min.y - sink * b.size.y);
            foreach (var r in rs) { r.shadowCastingMode = cast ? ShadowCastingMode.On : ShadowCastingMode.Off; r.receiveShadows = true; }
            Matte(g, V("dim"));
            if (cast && h >= 1.4f) volCasters.Add((new Vector3(x, gy, z) + new Vector3(off.x, 0f, off.z), h * (1f - sink)));
            return g;
        }
        static Material lanternMat;
        static void LanternMat(GameObject g)
        {
            if (!lanternMat) { var sh = Shader.Find("RoomRun/VColLit"); if (sh) { lanternMat = new Material(sh) { name = "ER_Lantern" }; if (lanternMat.HasProperty("_Tint")) lanternMat.SetColor("_Tint", new Color(0.62f, 0.61f, 0.6f)); } }
            if (!lanternMat) return;
            foreach (var r in g.GetComponentsInChildren<Renderer>()) { var ms = r.sharedMaterials; for (int i = 0; i < ms.Length; i++) ms[i] = lanternMat; r.sharedMaterials = ms; }
        }

        // the base buried: a ragged mud scan under it, stones + a broken block or two leaning on the foot (all <= 0.5 m)
        static readonly string[] SkirtBits = { "PalaceStage/PS_Rocks", "PalaceStage/PS_Pebbles", "PalaceStage/PS_Rocks", "PalaceStage/PS_Tile", "PalaceStage/PS_Pebbles" };
        static void Skirt(float x, float z, float rad, int n, bool block = true)
        {
            Flat($"PalaceStage/PS_G_Mud_{rng.Next(3)}", x + R(-0.4f, 0.4f), z + R(-0.4f, 0.4f), rad * R(2.4f, 3f), rad * R(2.8f, 3.6f), R(0, 360), 0.024f);
            for (int i = 0; i < n; i++)
            {
                float a = R(0f, Mathf.PI * 2f), d = rad * R(0.55f, 1.25f);
                Prop(SkirtBits[rng.Next(SkirtBits.Length)], x + Mathf.Cos(a) * d, z + Mathf.Sin(a) * d, R(0, 360), R(0.85f, 1.2f), R(-18f, 18f), R(-18f, 18f), 0.12f);
            }
            if (!block) return;
            for (int i = 0; i < 2; i++)
            {
                float a = R(0f, Mathf.PI * 2f), d = rad * R(0.45f, 0.8f);
                var g = Prop("PalaceStage/PS_Rubble", x + Mathf.Cos(a) * d, z + Mathf.Sin(a) * d, R(0, 360), 1f, 0f, R(-10f, 10f), 0.3f);
                if (g) { FitTop(g, R(0.35f, 0.5f), 0.3f); Matte(g, 0.85f); }
            }
        }
        static void FitTop(GameObject g, float h, float sink)
        {
            var rs = g.GetComponentsInChildren<Renderer>(); if (rs.Length == 0) return;
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            if (b.size.y < 1e-3f) return;
            g.transform.localScale *= (h / (1f - sink)) / b.size.y;
            b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            var p = g.transform.position - off;
            g.transform.position += Vector3.up * (GroundAt(p.x, p.z) - b.min.y - sink * b.size.y);
            foreach (var r in rs) r.shadowCastingMode = ShadowCastingMode.Off;
        }

        static readonly List<Light> volLights = new List<Light>();
        static void WarmBase(float x, float z, float y)
        {
            var go = new GameObject("ER_WarmBase");
            go.transform.SetParent(root, false);
            go.transform.position = new Vector3(x, GroundAt(x, z) + y, z) + new Vector3(off.x, 0f, off.z);
            var l = go.AddComponent<Light>();
            l.type = LightType.Point; l.color = new Color(1f, 0.56f, 0.3f); l.intensity = V("warmI"); l.range = V("warmR"); l.shadows = LightShadows.None;
            l.renderMode = LightRenderMode.ForcePixel;
            volLights.Add(l); lightsOn++;
        }

        // 2. the ring: clearing ~20 m around the centre, pieces at 13-17 m. Light from the NW -> most mass NW / N, the SE
        //    quarter (a 110-200) empty, the south (camera side) kept low so nothing tall stands between camera and player.
        static void Ring()
        {
            volLights.Clear();
            // A  north-west (a ~325, r 16): broken palace stone pillar 8.5 m, a shorter column 4.2 m, a stone lantern 1.9 m
            Vert("EdgeFeatures/EF_RF_StonePillar", -9.6f, 12.9f, 8.6f, R(0, 360), 0.06f, 2f);
            Vert("EdgeFeatures/EF_RF_StonePillar", -7.2f, 14.6f, 4.2f, R(0, 360), 0.1f, 4f);
            Vert("StageLantern/KoreanStoneLantern", -11.9f, 11.1f, 1.9f, 25f, 0.04f, 1.5f);
            Skirt(-9.6f, 12.9f, 1.5f, 7); Skirt(-7.2f, 14.6f, 1.0f, 4, false);
            Vert("PalaceStage/PS_Rubble", -8.4f, 11.6f, 0.45f, R(0, 360), 0.3f, 8f, false);
            // warm burner at the big pillar's foot (lights it from below)
            Vert("EdgeFeatures/PP_IncenseB", -8.3f, 11.3f, 1.25f, 200f, 0.03f, 1f);
            WarmBase(-8.6f, 11.8f, 0.7f);

            // B  north-north-east (a ~30, r 17): old dead tree 9 m, stone pagoda 4.4 m, a snag 1.8 m
            Vert("EdgeFeatures/EF_RF_DeadTrunk", 8.6f, 14.8f, 9.2f, R(0, 360), 0.05f, 3f);
            Vert("Hell/Pagoda", 11.4f, 13.1f, 4.4f, 18f, 0.05f, 1f);
            Vert("EdgeFeatures/EF_RF_Snag_B", 6.9f, 16.6f, 1.8f, R(0, 360), 0.08f, 6f);
            Skirt(8.6f, 14.8f, 1.6f, 7); Skirt(11.4f, 13.1f, 1.3f, 5, false);

            // C  west: two 장승 flanking the west gate road (z -1..13) like a gate, 7.3 m apart, a broken block by one
            Vert("EdgeFeatures/EF_Jangseung", -15.2f, 1.2f, 4.1f, 95f, 0.1f, 2.5f);
            Vert("EdgeFeatures/EF_Jangseung", -15.8f, 8.5f, 3.8f, 82f, 0.1f, 3.5f);
            Vert("EdgeFeatures/EF_RF_Snag_A", -16.9f, -0.6f, 1.6f, R(0, 360), 0.08f, 5f);
            Skirt(-15.2f, 1.2f, 1.0f, 5); Skirt(-15.8f, 8.5f, 1.0f, 4);

            // D  south-west (a ~228, r 14): low group only (stone lantern 2.1 m, a short snag 1.5 m)
            Vert("StageLantern/KoreanStoneLantern", -10.6f, -9.2f, 2.1f, -30f, 0.04f, 2f);
            Vert("EdgeFeatures/EF_RF_Snag_C", -12.5f, -8.1f, 1.5f, R(0, 360), 0.08f, 6f);
            Skirt(-10.6f, -9.2f, 1.0f, 5);

            // E  east (a ~84, r 15.5): broken column 5 m, burner + warm light at its foot, a snag 1.7 m
            Vert("EdgeFeatures/EF_RF_StonePillar", 15.6f, 1.8f, 5.0f, R(0, 360), 0.08f, 3f);
            Vert("EdgeFeatures/PP_IncenseB", 14.5f, 0.2f, 1.25f, 300f, 0.03f, 1f);
            Vert("EdgeFeatures/EF_RF_Snag_D", 17.4f, 3.4f, 1.7f, R(0, 360), 0.08f, 6f);
            Skirt(15.6f, 1.8f, 1.3f, 6);
            WarmBase(14.9f, 0.9f, 0.7f);
        }

        // 4. just outside the walkable court on the light side: only their shadows reach in (13 m tree -> ~20 m shadow)
        static void OffArena()
        {
            // 11-19 v3: the thin conifers 4 m past the wall threw no shadow you could see -> solid trunks right behind it
            Vert("EdgeFeatures/EF_RF_DeadTrunk", -56f, 20f, 14f, R(0, 360), 0.02f, 3f);
            Vert("Hell/DeadTree0", -56f, -6f, 13f, R(0, 360), 0.02f, 3f);
            Vert("EdgeFeatures/EF_RF_DeadTrunk", -33f, 49.5f, 12.5f, R(0, 360), 0.02f, 3f);
            Vert("EdgeFeatures/EF_RF_StonePillar", -55.5f, 37f, 10f, R(0, 360), 0.02f, 2f);
        }

        // 5. edge: low rubble in front (court side), a mid piece 1.5-3 m behind it, then the wall. Groups 7-16 m apart,
        //    kept off the side gates (z -1..13), the fallen bay (west z -31..-17), the burnt gallery (east z -30..-12),
        //    the hall front (north |x| < 27)
        static void EdgeLayers()
        {
            string[] mid = { "EdgeFeatures/EF_RF_StonePillar", "EdgeFeatures/EF_RF_Snag_A", "EdgeFeatures/EF_Jangseung", "EdgeFeatures/EF_RF_Snag_D", "EdgeFeatures/EF_RF_StonePillar" };
            void Group(float x, float z, float inX, float inZ, int k)
            {
                string what = mid[k % mid.Length];
                float h = what.Contains("Pillar") ? R(2.2f, 2.9f) : what.Contains("Jangseung") ? R(2.4f, 2.8f) : R(1.6f, 2.2f);
                Vert(what, x, z, h, R(0, 360), 0.1f, what.Contains("Snag") ? 6f : 3f);
                if (k % 2 == 0) Vert(mid[(k + 1) % mid.Length], x + R(-1.6f, 1.6f) * (1f - Mathf.Abs(inX)), z + R(-1.6f, 1.6f) * (1f - Mathf.Abs(inZ)), R(1.5f, 1.9f), R(0, 360), 0.1f, 6f);
                // low rubble 0.3-1 m in front
                for (int i = 0; i < 3; i++)
                {
                    var g = Prop("PalaceStage/PS_Rubble", x + inX * R(1.8f, 3.2f) + R(-1.5f, 1.5f), z + inZ * R(1.8f, 3.2f) + R(-1.5f, 1.5f), R(0, 360), 1f, 0f, R(-8f, 8f), 0.2f);
                    if (g) { FitTop(g, R(0.3f, 0.9f), 0.2f); Matte(g, 0.85f); }
                }
                Skirt(x, z, 1.1f, 4, false);
            }
            int n = 0;
            foreach (var z in new[] { -41f, -35f, 18f, 31f, 39f }) Group(-49.8f + R(-0.5f, 0.5f), z + R(-1f, 1f), 1f, 0f, n++);
            foreach (var z in new[] { -43f, -5f, 23f, 36f }) Group(49.8f + R(-0.5f, 0.5f), z + R(-1f, 1f), -1f, 0f, n++);
            foreach (var x in new[] { -40f, -31f, 34f, 45f }) Group(x + R(-1f, 1f), 41.8f + R(-0.4f, 0.4f), 0f, -1f, n++);
        }

        // 7. thin smoke cards behind the walls (Kenney smoke_04, CC0); nothing over the court or the bottom of the screen
        static Material fogMat;
        static void FogCards()
        {
            if (!CodePlanes) return;   // 11-20: the fog cards are code quads too (RR.EnvRecompose.codePlanes = 1 brings them back)
            if (!fogMat)
            {
                var tex = Resources.Load<Texture2D>("VFX/smoke_04");
                var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (!tex || !sh) { Debug.LogWarning("[EnvRecompose]   volume fog: no smoke_04 / particles shader"); return; }
                fogMat = new Material(sh) { name = "ER_FogCard" };
                fogMat.SetTexture("_BaseMap", tex);
                fogMat.SetFloat("_Surface", 1f); fogMat.SetFloat("_Blend", 0f);
                fogMat.SetOverrideTag("RenderType", "Transparent");
                fogMat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha); fogMat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha); fogMat.SetInt("_ZWrite", 0);
                fogMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                fogMat.renderQueue = 3000;
            }
            fogMat.SetColor("_BaseColor", new Color(0.13f, 0.135f, 0.155f, V("fogA")));
            void Card(float x, float z, float y, float sx, float sz, float yaw)
            {
                var go = new GameObject("ER_FogCard");
                go.transform.SetParent(root, false);
                go.transform.SetPositionAndRotation(new Vector3(x, baseY + y, z) + new Vector3(off.x, 0f, off.z), Quaternion.Euler(0f, yaw, 0f));
                go.transform.localScale = new Vector3(sx, 1f, sz);
                go.AddComponent<MeshFilter>().sharedMesh = Quad();
                var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = fogMat;
                r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                count++;
            }
            // north, behind the wall / in front of the hall roofs (layers: court | wall | fog | hall)
            Card(-36f, 53f, 3.2f, 26f, 11f, 4f); Card(-6f, 56f, 4.5f, 30f, 10f, -3f); Card(30f, 53.5f, 3.6f, 26f, 11f, 7f);
            // behind the side walls
            Card(-61f, -16f, 2.6f, 12f, 26f, 2f); Card(-62f, 26f, 2.8f, 12f, 24f, -5f);
            Card(61.5f, -24f, 2.6f, 12f, 24f, -3f); Card(61f, 20f, 3f, 12f, 26f, 6f);
        }

        // ------------------------------------------------------------------ capture stats (editor): vertical shadows in view
        // Every tall shadow caster in the palace scene (>= 1.4 m, casting, enabled) whose shadow on the floor (base -> base +
        // light dir * h / tan(elev)) is in the camera view and within the shadow distance is counted; logged to
        // ArtSource/EnvRecompose/volume_log.txt each time the player stands still at a new spot (the shots teleport it).
        static Vector3 lastLoggedAt = new Vector3(9999, 0, 0), stillAt; static float stillSince, nextCount;
        static void ShadowLogTick(RunDirector run)
        {
#if UNITY_EDITOR
            if (!run.Player || Time.unscaledTime < nextCount) return;
            nextCount = Time.unscaledTime + 0.2f;
            var p = run.Player.transform.position;
            if ((p - stillAt).sqrMagnitude > 0.04f) { stillAt = p; stillSince = Time.unscaledTime; return; }
            if (Time.unscaledTime - stillSince < 0.3f || (p - lastLoggedAt).sqrMagnitude < 4f) return;
            lastLoggedAt = p;
            int n = CountVerticalShadows(out var det);
            var sun = FindSun();
            string line = $"{System.DateTime.Now:HH:mm:ss} palace volume={(VolumeOn() ? 1 : 0)} active={(volActive ? 1 : 0)} at ({p.x - run.Room.center.x:0},{p.z - run.Room.center.z:0}) shadows {n} sun {(sun ? sun.transform.eulerAngles.ToString("0") : "-")} | {det}";
            var dir = System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, "ArtSource", "EnvRecompose");
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "volume_log.txt"), line + System.Environment.NewLine);
#endif
        }
        public static int CountVerticalShadows(out string detail)
        {
            detail = "";
            var cam = Camera.main; var sun = FindSun();
            if (!cam || !sun) return -1;
            var dir = sun.transform.forward; if (dir.y > -0.05f) return -1;
            var flat = new Vector3(dir.x, 0f, dir.z) / -dir.y;           // shadow offset on the floor per metre of height
            float sd = GraphicsSettings.currentRenderPipeline is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset urp ? urp.shadowDistance : QualitySettings.shadowDistance;
            var scene = SceneManager.GetActiveScene();
            var seen = new List<Vector3>(); int n = 0; var sb = new System.Text.StringBuilder();
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (!r || !r.enabled || !r.gameObject.activeInHierarchy || r.shadowCastingMode == ShadowCastingMode.Off || r is ParticleSystemRenderer) continue;
                if (r.GetComponentInParent<PlayerController>() || r.GetComponentInParent<Health>()) continue;
                var b = r.bounds; if (b.size.y < 1.4f || b.size.x > 14f || b.size.z > 14f) continue;
                var basePt = new Vector3(b.center.x, b.min.y, b.center.z);
                bool dup = false; foreach (var s in seen) if ((new Vector2(s.x - basePt.x, s.z - basePt.z)).sqrMagnitude < 1.2f) { dup = true; break; }
                if (dup) continue;
                float h = b.size.y;
                bool vis = false;
                for (float t = 0.25f; t <= 1.01f && !vis; t += 0.25f)
                {
                    var q = basePt + flat * h * t;
                    if ((q - cam.transform.position).magnitude > sd) continue;
                    var vp = cam.WorldToViewportPoint(q);
                    if (vp.z > 0f && vp.x > 0.02f && vp.x < 0.98f && vp.y > 0.02f && vp.y < 0.98f) vis = true;
                }
                if (!vis) continue;
                seen.Add(basePt); n++;
                sb.Append(r.name).Append(' ').Append(h.ToString("0.0")).Append("m; ");
            }
            // the volume layer's own pieces get merged into big MeshChunker tiles, so they are counted from the placing list
            foreach (var c in volCasters)
            {
                bool dup = false; foreach (var s in seen) if ((new Vector2(s.x - c.p.x, s.z - c.p.z)).sqrMagnitude < 1.2f) { dup = true; break; }
                if (dup) continue;
                bool vis = false;
                for (float t = 0.25f; t <= 1.01f && !vis; t += 0.25f)
                {
                    var q = c.p + flat * c.h * t;
                    if ((q - cam.transform.position).magnitude > sd) continue;
                    var vp = cam.WorldToViewportPoint(q);
                    if (vp.z > 0f && vp.x > 0.02f && vp.x < 0.98f && vp.y > 0.02f && vp.y < 0.98f) vis = true;
                }
                if (!vis) continue;
                seen.Add(c.p); n++;
                sb.Append("vol(").Append((c.p.x - off.x).ToString("0")).Append(',').Append((c.p.z - off.z).ToString("0")).Append(") ").Append(c.h.ToString("0.0")).Append("m; ");
            }
            detail = $"cam {cam.name} d{(cam.transform.position - (RunDirector.Instance && RunDirector.Instance.Player ? RunDirector.Instance.Player.transform.position : Vector3.zero)).magnitude:0} sd{sd:0} | " + sb;
            return n;
        }
    }
}
