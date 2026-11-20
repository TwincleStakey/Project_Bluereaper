// Player light radius (11-19, Kido: in dark maps the hero is too dark to see; "like Path of Exile 2").
// One real point light rides above every player (co-op ready: each PlayerController gets its own component). It lights
// the hero and the ground around it with the stage's own shaders, so the floor keeps its true scan colour and only gets
// brighter; no overlay, decal or tint. The darkness reads as the contrast between inside and outside the circle, the mood
// (desaturation, split toning) stays in post (LookFX / UWLook).
//   - soft falloff only: URP's point attenuation (inverse square, smoothed to zero at the range), never a spot/cookie disc
//   - neutral-warm white, not orange
//   - per-room strength in Rooms below (bright daytime fields lower); tuning without a recompile: ArtSource/LightRadius/tune.txt
//     (editor only, re-read on every room entry) lines "<room|*> <intensity> <range> <height> [shadows 0|1]"
//   - FieldLighting's older weak pool (PlayerLightPool / PlayerFootLight) is switched off while this is on, so they never stack
// Toggle: PlayerPrefs RR.LightRadius (1 default, 0 off); rr_cmd "call RoomRun.PlayerLightRadius.Off" / ".On".
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RoomRun
{
    [DisallowMultipleComponent]
    public sealed class PlayerLightRadius : MonoBehaviour
    {
        public const string Key = "RR.LightRadius";
        public static bool Enabled => PlayerPrefs.GetInt(Key, 1) != 0;
        public static void On() { PlayerPrefs.SetInt(Key, 1); PlayerPrefs.Save(); }
        public static void Off() { PlayerPrefs.SetInt(Key, 0); PlayerPrefs.Save(); }

        public struct Tune
        {
            public float intensity, range, height; public bool shadows;
            public Tune(float i, float r, float h, bool s = false) { intensity = i; range = r; height = h; shadows = s; }
        }

        // slightly warm white; the stage grades decide the final hue
        public static Color LightColor = new Color(1f, 0.95f, 0.88f);
        // 11-19 v4 (Kido: match PoE2): the hero has no self-light any more, so this light is its key light, as in PoE2's
        // light radius. Soft inverse-square falloff, range 10 m, 4.5 m up (at 3 m the straw hat, 1.5 m nearer the light, hot-spotted;
        // 4.5 m with intensity x2.25 keeps the floor the same and halves the hat). Ground round the player ~1.5-2x the edge.
        // Point shadows are off: a cube shadow map re-renders the casters 6x.
        public static Tune Default = new Tune(13f, 10f, 4.5f);
        public static readonly Dictionary<string, Tune> Rooms = new Dictionary<string, Tune>
        {
            // near-black floor under UWLook's grade needs more light than the other fields
            { "underworld", new Tune(26f, 10f, 4.5f) },
            { "abandoned", new Tune(7f, 10f, 4.5f) },
            { "village", new Tune(20f, 10f, 4.5f) },
            { "bamboo", new Tune(20f, 10f, 4.5f) },
            // 풀밭 (11-19, the greatsword / weapon animation captures run here): StageMood -0.9 EV and the LookStudy grass and
            // ground shaders take no point lights, so the circle cannot show on this floor; the light still sets the hero's
            // ground-level fill, so it is stronger here and the hero exposure is raised (RoomHeroExposure)
            { "grass", new Tune(30f, 10f, 4.5f) },
            // white snow returns everything
            { "snow", new Tune(4.5f, 10f, 4.5f) },
            // 11-19 v10 (Kido: the hero blends into the stone floor): every playable room gets the ground light now, the
            // stone rooms included (대전 앞 was on the default 13; crypt / D4 / boss seal had it off since 11-15)
            { "palace", new Tune(22f, 10f, 4.5f) },
            { "crypt", new Tune(14f, 10f, 4.5f) },
            { "d4field", new Tune(14f, 10f, 4.5f) },
            { "bossseal", new Tune(14f, 10f, 4.5f) },
        };

        // Hero material (11-19 v4, PoE2 rule): no self-lighting. GSHero/Readable's shader-only lights are switched off on
        // per-player material instances (assets untouched): top key, sky fill and rim to 0, ambient boost to 1 (plain scene
        // ambient). What is left is the textured albedo lit by the real lights (moon + this radius light), plus the sword's
        // metal sheen and a halved edge glint as its only highlight. HeroSelfLight 1 = the material values (tune.txt "hero").
        public static float HeroSelfLight = 0f;
        // editor tune.txt overrides (-1 = not set); declared outside the editor block so player builds compile
        static float fileHero = -1f, fileMoon = -1f, fileFog = -1f, fileExp = -1f, fileHat = -1f, fileFogHero = -1f, fileSteel = -1f, fileSwSheen = -1f, fileSwGlint = -1f, fileSwKey = -1f, fileNear = -1f;
        static readonly Dictionary<string, float> fileRoomExp = new Dictionary<string, float>();
        static readonly Dictionary<string, float> fileRoomLift = new Dictionary<string, float>();
        static float fileRimS = -1f, fileRimW = -1f;
        static Color fileRimC = new Color(0, 0, 0, 0);
        static readonly string[] SelfProps = { "_TopKey", "_SkyFill", "_RimStrength", "_EdgeGlint", "_AmbientBoost", "_Sheen" };
        // what each property goes to at HeroSelfLight 0 (as a factor of the material value; ambient boost goes to 1 absolute)
        // v5: sword sheen x0.5 and edge glint x0.3 (they flashed white under the radius light; a slight metal read stays)
        static readonly float[] SelfFloor = { 0f, 0f, 0f, 0.3f, -1f, 0.5f };
        readonly Dictionary<Material, float[]> heroOrig = new Dictionary<Material, float[]>();
        // v6 (Kido: "the character is still too bright"): the hero materials lift their own albedo (_BaseColor 1.25-1.3),
        // tint the hat a pale straw (0.64/0.52/0.33) and skip the scene fog the ground gets (_NoFog 1). On the runtime copies:
        // albedo x HeroExposure, hat tint x HatScale, fog like the ground (_NoFog = HeroNoFog). tune.txt: heroExposure / hatScale / heroFog.
        // 0.5 linear = about 27% darker on screen (0.72 only gave ~13%: sRGB curve).
        public static float HeroExposure = 0.65f, HatScale = 0.7f, HeroNoFog = 0f;
        // v11 (Kido 21:03 picked C, value contrast): body 0.65 / hat 0.7 / rim 0.12 - a dark figure on the lit ground.
        // v8 (Kido 13:18: "still dark in the game"): body 0.9 / hat 0.9, near boost 0.5.
        // v7 (Kido: "the character got too dark", "the weapon must stay bright"): body between v5 and v6 (albedo x0.7, hat
        // x0.75, fogged). Weapon materials (_Metal > 0) are never darkened: albedo/hat/fog untouched, sheen back to the
        // material value, edge glint x0.5, top key x0.5 as its highlight. tune.txt: swordScale (steel colour), swordSheen,
        // swordGlint, swordKey, heroExposure (+ heroExposure.<room>).
        public static float SwordScale = 1f, SwordSheen = 1f, SwordGlint = 0.5f, SwordKey = 0.5f;
        // v10 (Kido: "림라이트"): a thin, soft, cool-neutral fresnel rim on the BODY only (GSHero/Readable's own rim term,
        // _RimSharp 0 = soft ramp, no hard band; the 11-18 white streaks came from _RimSharp .85 at strength .75). Weapon
        // keeps its own values. tune.txt: rimStrength, rimColor r,g,b, rimWidth (alias rimPower).
        public static float RimStrength = 0.12f, RimWidth = 0.4f;
        public static Color RimColor = new Color(0.72f, 0.82f, 1f);
        public static readonly Dictionary<string, float> RoomHeroExposure = new Dictionary<string, float> { { "grass", 1.15f } };
        // dark-tone lift of the body texture (GSHero/Readable _Lift: lerp toward sqrt, hue kept) per room; the armour is near
        // black, so multiplying it does little where the room grade is very dark (풀밭 -0.9 EV). Body only, never the weapon.
        // tune.txt: heroLift.<room>. Rooms not listed keep the material value (0.16-0.18).
        public static readonly Dictionary<string, float> RoomHeroLift = new Dictionary<string, float> { { "grass", 0.45f } };
        readonly HashSet<Material> swordMats = new HashSet<Material>();
        readonly Dictionary<Material, float> liftOrig = new Dictionary<Material, float>();
        readonly Dictionary<Material, (float w, float sharp, Color c)> rimOrig = new Dictionary<Material, (float, float, Color)>();
        readonly Dictionary<Material, (Color baseCol, Color hat, float noFog, Color steel)> heroCol = new Dictionary<Material, (Color, Color, float, Color)>();
        float heroApplied = -1f;

        // v5 (Kido: "PoE only lights the surroundings; with the light on the character it looks odd"): GSHero/Readable skips
        // the radius lights per pixel and instead receives what the light gives the ground under the hero's feet
        // (_RR_RadiusFill), so the hero is lit like the floor it stands on, never spot-lit from 4.5 m.
        static readonly Vector4[] radiusPos = new Vector4[4];
        static readonly PlayerLightRadius[] slots = new PlayerLightRadius[4];
        static readonly int RadiusPosId = Shader.PropertyToID("_RR_RadiusPos"), RadiusFillId = Shader.PropertyToID("_RR_RadiusFill");
        int slot = -1;

        // v7 (Kido: "just a slight light around the character so it reads"): the ground close to the hero gets NearBoost more
        // light (+30 % under the hero) by giving the one radius light a shorter range (NearRange) and more intensity, so it
        // gathers near the hero and fades sooner; no second light (several floor shaders only take a few lights per object,
        // a weak extra light was simply dropped there). The hero's fill is divided by (1 + NearBoost) so the body does not get
        // brighter, only the ground round it. tune.txt: nearBoost (0 = v6 falloff).
        public static float NearBoost = 0.5f, NearRange = 7.5f;

        Light lightComp;
        RunDirector run;
        string roomSeen;
        float height;

        public static Tune For(string room)
        {
            var t = room != null && Rooms.TryGetValue(room, out var rt) ? rt : Default;
#if UNITY_EDITOR
            if (fileTunes != null)
            {
                if (fileTunes.TryGetValue("*", out var any)) t = any;
                if (room != null && fileTunes.TryGetValue(room, out var ft)) t = ft;
            }
#endif
            return t;
        }

        void OnEnable()
        {
            var go = new GameObject("PlayerLightRadius");
            go.transform.SetParent(transform, false);
            lightComp = go.AddComponent<Light>();
            lightComp.type = LightType.Point;
            lightComp.renderMode = LightRenderMode.ForcePixel;
            lightComp.shadows = LightShadows.None;
            lightComp.color = LightColor;
            lightComp.bounceIntensity = 0f;
            roomSeen = null;
            for (int k = 0; k < slots.Length; k++) if (!slots[k]) { slots[k] = this; slot = k; break; }
        }

        static Vector4 fill;
        float fillDiv = 1f;
        // URP's range window at distance d for a light of range r (squared smooth factor)
        static float Smooth(float d, float r)
        {
            float f = d * d / Mathf.Max(0.0001f, r * r), s = Mathf.Clamp01(1f - f * f);
            return s * s;
        }
        bool IsFirstSlot() { for (int k = 0; k < slots.Length; k++) if (slots[k]) return slots[k] == this; return false; }

        // URP point attenuation at the ground right under the light (distance = height, light straight above)
        static Vector4 GroundFill(Light l, float h)
        {
            if (!l || !l.enabled || h <= 0f) return Vector4.zero;
            float d2 = h * h, f = d2 / Mathf.Max(0.0001f, l.range * l.range);
            float smooth = Mathf.Clamp01(1f - f * f); smooth *= smooth;
            Color c = l.color.linear * l.intensity * (smooth / d2);
            return new Vector4(c.r, c.g, c.b, 0f);
        }

        static void PushRadius()
        {
            bool any = false;
            for (int k = 0; k < slots.Length; k++) any |= slots[k] != null;
            Shader.SetGlobalVectorArray(RadiusPosId, radiusPos);
            Shader.SetGlobalVector(RadiusFillId, any ? fill : Vector4.zero);
        }

        void ApplyHeroSelfLight()
        {
            float k = HeroScale();
            bool fresh = false;
            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                var sm = r.sharedMaterial;
                if (!sm || !sm.shader || sm.shader.name != "GSHero/Readable") continue;
                foreach (var m in r.materials)
                {
                    if (heroOrig.ContainsKey(m)) continue;
                    var o = new float[SelfProps.Length];
                    for (int i = 0; i < SelfProps.Length; i++) o[i] = m.HasProperty(SelfProps[i]) ? m.GetFloat(SelfProps[i]) : 0f;
                    heroOrig[m] = o; fresh = true;
                    if (m.HasProperty("_Metal") && m.GetFloat("_Metal") > 0f) swordMats.Add(m);
                    liftOrig[m] = m.HasProperty("_Lift") ? m.GetFloat("_Lift") : 0f;
                    rimOrig[m] = (m.HasProperty("_RimWidth") ? m.GetFloat("_RimWidth") : 0.45f, m.HasProperty("_RimSharp") ? m.GetFloat("_RimSharp") : 0f, m.HasProperty("_RimColor") ? m.GetColor("_RimColor") : Color.white);
                    heroCol[m] = (m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : Color.white, m.HasProperty("_HatColor") ? m.GetColor("_HatColor") : Color.white, m.HasProperty("_NoFog") ? m.GetFloat("_NoFog") : 1f, m.HasProperty("_SteelColor") ? m.GetColor("_SteelColor") : Color.white);
                }
            }
            float ex = HeroExp(roomSeen), hs = HatSc(), nf = HeroFog(), st = Pick(fileSteel, SwordScale);
            float sSheen = Pick(fileSwSheen, SwordSheen), sGlint = Pick(fileSwGlint, SwordGlint), sKey = Pick(fileSwKey, SwordKey);
            float lift = HeroLift(roomSeen);
            float rimS = Pick(fileRimS, RimStrength), rimW = Pick(fileRimW, RimWidth);
            Color rimC = fileRimC.a > 0f ? fileRimC : RimColor;
            float sig = k + ex * 10f + hs * 100f + nf * 1000f + st * 10000f + sSheen * 7f + sGlint * 70f + sKey * 700f + lift * 3f + rimS * 13f + rimW * 17f + rimC.r * 19f + rimC.g * 23f + rimC.b * 29f;
            if (!fresh && Mathf.Approximately(sig, heroApplied)) return;
            heroApplied = sig;
            foreach (var kv in heroCol)
            {
                var m = kv.Key; if (!m) continue;
                var (bc, hc, f, sc) = kv.Value;
                if (swordMats.Contains(m))
                {
                    if (m.HasProperty("_SteelColor")) m.SetColor("_SteelColor", new Color(sc.r * st, sc.g * st, sc.b * st, sc.a));
                    continue;
                }
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", new Color(bc.r * ex, bc.g * ex, bc.b * ex, bc.a));
                if (m.HasProperty("_HatColor")) m.SetColor("_HatColor", new Color(hc.r * hs, hc.g * hs, hc.b * hs, hc.a));
                if (m.HasProperty("_NoFog")) m.SetFloat("_NoFog", nf);
                if (m.HasProperty("_Lift") && liftOrig.TryGetValue(m, out var lo)) m.SetFloat("_Lift", lift >= 0f ? lift : lo);
            }
            foreach (var kv in heroOrig)
            {
                var m = kv.Key; if (!m) continue;
                var o = kv.Value;
                for (int i = 0; i < SelfProps.Length; i++)
                {
                    if (!m.HasProperty(SelfProps[i])) continue;
                    float fl = SelfFloor[i];
                    if (swordMats.Contains(m))
                    {
                        if (SelfProps[i] == "_Sheen") fl = sSheen;
                        else if (SelfProps[i] == "_EdgeGlint") fl = sGlint;
                        else if (SelfProps[i] == "_TopKey") fl = sKey;
                    }
                    float off = fl < 0f ? Mathf.Min(1f, o[i]) : o[i] * fl;
                    m.SetFloat(SelfProps[i], Mathf.Lerp(off, o[i], k));
                }
                if (!swordMats.Contains(m) && m.HasProperty("_RimStrength"))
                {
                    m.SetFloat("_RimStrength", rimS);
                    if (m.HasProperty("_RimWidth")) m.SetFloat("_RimWidth", rimW);
                    if (m.HasProperty("_RimSharp")) m.SetFloat("_RimSharp", 0f);
                    if (m.HasProperty("_RimColor")) m.SetColor("_RimColor", rimC);
                }
            }
        }

        void RestoreHeroSelfLight()
        {
            foreach (var kv in heroOrig)
            {
                var m = kv.Key; if (!m) continue;
                for (int i = 0; i < SelfProps.Length; i++) if (m.HasProperty(SelfProps[i])) m.SetFloat(SelfProps[i], kv.Value[i]);
            }
            foreach (var kv in heroCol)
            {
                var m = kv.Key; if (!m) continue;
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", kv.Value.baseCol);
                if (m.HasProperty("_HatColor")) m.SetColor("_HatColor", kv.Value.hat);
                if (m.HasProperty("_NoFog")) m.SetFloat("_NoFog", kv.Value.noFog);
                if (m.HasProperty("_Lift") && liftOrig.TryGetValue(m, out var lo)) m.SetFloat("_Lift", lo);
                if (rimOrig.TryGetValue(m, out var ro))
                {
                    if (m.HasProperty("_RimWidth")) m.SetFloat("_RimWidth", ro.w);
                    if (m.HasProperty("_RimSharp")) m.SetFloat("_RimSharp", ro.sharp);
                    if (m.HasProperty("_RimColor")) m.SetColor("_RimColor", ro.c);
                }
                if (m.HasProperty("_SteelColor")) m.SetColor("_SteelColor", kv.Value.steel);
            }
            heroApplied = -1f;
        }

        static float HeroExp(string room)
        {
#if UNITY_EDITOR
            if (room != null && fileRoomExp.TryGetValue(room, out var fe)) return fe;
            if (fileExp >= 0f) return fileExp;
#endif
            return room != null && RoomHeroExposure.TryGetValue(room, out var e) ? e : HeroExposure;
        }
        // -1 = keep the material's own _Lift
        static float HeroLift(string room)
        {
#if UNITY_EDITOR
            if (room != null && fileRoomLift.TryGetValue(room, out var fl)) return fl;
#endif
            return room != null && RoomHeroLift.TryGetValue(room, out var l) ? l : -1f;
        }

        static float Pick(float file, float def)
        {
#if UNITY_EDITOR
            if (file >= 0f) return file;
#endif
            return def;
        }

        static float HatSc()
        {
#if UNITY_EDITOR
            if (fileHat >= 0f) return fileHat;
#endif
            return HatScale;
        }
        static float HeroFog()
        {
#if UNITY_EDITOR
            if (fileFogHero >= 0f) return fileFogHero;
#endif
            return HeroNoFog;
        }

        static float HeroScale()
        {
#if UNITY_EDITOR
            if (fileHero >= 0f) return fileHero;
#endif
            return HeroSelfLight;
        }

        void OnDisable()
        {
            if (slot >= 0) { slots[slot] = null; radiusPos[slot] = Vector4.zero; slot = -1; PushRadius(); }
            RestoreHeroSelfLight();
            if (lightComp) Destroy(lightComp.gameObject);
            lightComp = null;
        }

        void LateUpdate()
        {
            if (!lightComp) return;
            if (!run) run = RunDirector.Instance;
            string room = run && run.Room != null ? run.Room.id : null;
            if (room != roomSeen)
            {
                roomSeen = room;
#if UNITY_EDITOR
                ReadTuneFile();
#endif
                var t = For(room);
                float boost = Pick(fileNear, NearBoost);
                float r2 = boost > 0f && t.range > NearRange ? NearRange : t.range;
                lightComp.intensity = t.intensity * (1f + boost) * Smooth(t.height, t.range) / Mathf.Max(0.01f, Smooth(t.height, r2));
                lightComp.range = r2;
                fillDiv = 1f + boost;
                lightComp.shadows = t.shadows ? LightShadows.Soft : LightShadows.None;
                lightComp.shadowStrength = 0.8f;
                lightComp.shadowNearPlane = 0.2f;
                lightComp.color = LightColor;
                height = t.height;
                heroApplied = -1f;
                lightComp.enabled = t.intensity > 0f && t.range > 0f;
            }
            if (Time.frameCount % 30 == 0 || heroApplied < 0f) ApplyHeroSelfLight();
            // keep it straight above the feet even if the player root tilts or scales
            lightComp.transform.position = transform.position + Vector3.up * height;
            lightComp.transform.rotation = Quaternion.identity;
            if (slot >= 0)
            {
                radiusPos[slot] = lightComp.enabled ? (Vector4)lightComp.transform.position + new Vector4(0, 0, 0, 1) : Vector4.zero;
                if (IsFirstSlot()) fill = GroundFill(lightComp, height) / fillDiv;
                PushRadius();
            }
        }

#if UNITY_EDITOR
        static Dictionary<string, Tune> fileTunes;
        static void ReadTuneFile()
        {
            fileTunes = null; fileHero = -1f; fileMoon = -1f; fileFog = -1f; fileExp = -1f; fileHat = -1f; fileFogHero = -1f; fileSteel = -1f; fileSwSheen = -1f; fileSwGlint = -1f; fileSwKey = -1f; fileNear = -1f; fileRoomExp.Clear(); fileRoomLift.Clear(); fileRimS = -1f; fileRimW = -1f; fileRimC = new Color(0, 0, 0, 0);
            string path = System.IO.Path.Combine(Application.dataPath, "..", "ArtSource", "LightRadius", "tune.txt");
            if (!System.IO.File.Exists(path)) return;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            var d = new Dictionary<string, Tune>();
            foreach (var raw in System.IO.File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var p = line.Split(new[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);
                // single-value keys (11-19 v10: these were never parsed before, only the 4-number room lines were)
                if (p.Length >= 2 && p.Length < 4)
                {
                    string key = p[0];
                    if (key == "rimColor")
                    {
                        var rc = p[1].Split(',');
                        if (rc.Length >= 3 && float.TryParse(rc[0], System.Globalization.NumberStyles.Float, ci, out float cr) && float.TryParse(rc[1], System.Globalization.NumberStyles.Float, ci, out float cg) && float.TryParse(rc[2], System.Globalization.NumberStyles.Float, ci, out float cb)) fileRimC = new Color(cr, cg, cb, 1f);
                        continue;
                    }
                    if (!float.TryParse(p[1], System.Globalization.NumberStyles.Float, ci, out float v)) continue;
                    if (key.StartsWith("heroExposure.")) { fileRoomExp[key.Substring(13)] = v; continue; }
                    if (key.StartsWith("heroLift.")) { fileRoomLift[key.Substring(9)] = v; continue; }
                    switch (key)
                    {
                        case "hero": fileHero = v; break;
                        case "moon": fileMoon = v; break;
                        case "fog": fileFog = v; break;
                        case "heroExposure": fileExp = v; break;
                        case "hatScale": fileHat = v; break;
                        case "heroFog": fileFogHero = v; break;
                        case "swordScale": case "steelScale": fileSteel = v; break;
                        case "swordSheen": fileSwSheen = v; break;
                        case "swordGlint": fileSwGlint = v; break;
                        case "swordKey": fileSwKey = v; break;
                        case "nearBoost": fileNear = v; break;
                        case "rimStrength": fileRimS = v; break;
                        case "rimWidth": case "rimPower": fileRimW = v; break;
                    }
                    continue;
                }
                if (p.Length < 4) continue;
                if (!float.TryParse(p[1], System.Globalization.NumberStyles.Float, ci, out float i)) continue;
                if (!float.TryParse(p[2], System.Globalization.NumberStyles.Float, ci, out float r)) continue;
                if (!float.TryParse(p[3], System.Globalization.NumberStyles.Float, ci, out float h)) continue;
                d[p[0]] = new Tune(i, r, h, p.Length > 4 && p[4] == "1");
            }
            fileTunes = d;
        }
#endif

        // ------------------------------------------------------------------ boot: attach to every player, mute the old pool
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var go = new GameObject("PlayerLightRadius.Driver") { hideFlags = HideFlags.HideInHierarchy };
            DontDestroyOnLoad(go);
            go.AddComponent<Driver>();
        }

        // ------------------------------------------------------------------ outlines off (11-19 v4, PoE2 rule)
        // PoE2 draws no line round the player, not even behind walls (players ask GGG for one), and no rim on monsters.
        // So by default: FieldLighting's always-on hero line (HeroOutline) 0, its occluded outline v2 off (occludedOutline),
        // and the dark-field enemy rim material blacked out (additive, so black = nothing). Those systems belong to the
        // greatsword / FieldLighting work and are only switched here. PlayerPrefs RR.Outlines 1 = all of them back.
        public const string OutlinesKey = "RR.Outlines";

        // ------------------------------------------------------------------ per-room scene light (11-19, 폐촌 too bright)
        // Darkens a room with its own lights (moon, fog billboards), so the floor keeps its scan colour; the grade/post is
        // left alone. Applied once per stage scene load, after StageMood has set its values.
        public struct RoomLight { public float moon, fog; public RoomLight(float m, float f) { moon = m; fog = f; } }
        public static readonly Dictionary<string, RoomLight> RoomLights = new Dictionary<string, RoomLight>
        {
            // moon through the cloud cookie lit the pale dust into a blue-white patch round the spawn
            { "abandoned", new RoomLight(0.5f, 0.5f) },
        };

        sealed class Driver : MonoBehaviour
        {
            float next;
            int lineSeen = -1;
            Material rimSeen; Color rimColor;
            int sceneDone = -1, sceneWait = -1; float sceneAt;
            void Update()
            {
                RoomLook();
                if (Time.unscaledTime < next) return;
                next = Time.unscaledTime + 0.5f;
                int line = PlayerPrefs.GetInt(OutlinesKey, 0);
                if (line != lineSeen) { lineSeen = line; FieldLighting.HeroOutline = line != 0 ? 1f : 0f; }
                foreach (var fl in FindObjectsByType<FieldLighting>(FindObjectsSortMode.None)) fl.occludedOutline = line != 0;
                var rim = FieldLighting.EnemyRim;
                if (rim && rim.HasProperty("_Color"))
                {
                    if (rim != rimSeen) { rimSeen = rim; rimColor = rim.GetColor("_Color"); }
                    rim.SetColor("_Color", line != 0 ? rimColor : Color.black);
                }
                bool on = Enabled;
                foreach (var pc in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
                {
                    var lr = pc.GetComponent<PlayerLightRadius>();
                    if (on && !lr) pc.gameObject.AddComponent<PlayerLightRadius>();
                    else if (lr && lr.enabled != on) lr.enabled = on;
                }
                // FieldLighting keeps its own weak pool per player; hide it while the radius is on so the two never stack
                foreach (var fl in FindObjectsByType<FieldLighting>(FindObjectsSortMode.None))
                    foreach (Transform c in fl.transform)
                        if (c.name == "PlayerLightPool" || c.name == "PlayerFootLight")
                            if (c.gameObject.activeSelf == on) c.gameObject.SetActive(!on);
            }

            void RoomLook()
            {
                var run = RunDirector.Instance;
                if (!run || run.Transitioning || run.Room == null) return;
                int scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;
                if (scene == sceneDone) return;
                // wait a moment after the stage scene arrives so StageMood / FieldLighting have applied theirs first
                if (scene != sceneWait) { sceneWait = scene; sceneAt = Time.unscaledTime + 0.3f; return; }
                if (Time.unscaledTime < sceneAt) return;
                sceneDone = scene;
                if (!RoomLights.TryGetValue(run.Room.id, out var rl)) return;
#if UNITY_EDITOR
                if (fileMoon >= 0f) rl.moon = fileMoon;
                if (fileFog >= 0f) rl.fog = fileFog;
#endif
                foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                    if (l.type == LightType.Directional) l.intensity *= rl.moon;
                foreach (var ps in FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
                {
                    if (ps.gameObject.name != "GroundFog" && ps.gameObject.name != "EdgeFog") continue;
                    var main = ps.main; var g = main.startColor;
                    if (g.mode == ParticleSystemGradientMode.Color) { var c = g.color; c.a *= rl.fog; g.color = c; }
                    else { var a = g.colorMin; a.a *= rl.fog; var b = g.colorMax; b.a *= rl.fog; g.colorMin = a; g.colorMax = b; }
                    main.startColor = g;
                    var buf = new ParticleSystem.Particle[ps.main.maxParticles];
                    int n = ps.GetParticles(buf);
                    for (int i = 0; i < n; i++) { var c = buf[i].startColor; c.a = (byte)(c.a * rl.fog); buf[i].startColor = c; }
                    ps.SetParticles(buf, n);
                }
            }
        }
    }
}
