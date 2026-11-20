// Shadow / grounding pass (11-19, Kido: "게임 전체에 그림자·음영이 하나도 없어서 붕 떠 보인다"; then "PoE2, D4 기준으로 최대한 다 걸어").
// One runtime layer on top of every field, fully reversible (OnDisable puts every value back), nothing in the stage scenes,
// shaders or material assets is changed:
//   pipeline - main light shadow map 4096, 4 cascades split for the top-down camera (ground is 19-33 m from it), soft
//              shadows high, additional-light shadow atlas 4096. The shadow distance stays CameraRig's (50 m x zoom).
//   sun      - the main directional light casts soft shadows at full strength; per room the sun goes up and the flat
//              ambient down a little, so the lit/shaded difference reads (LookStudy shaders light from the _LSSunCol /
//              _LSShadowMul globals instead of the light, so those are scaled the same way).
//   actors   - player and enemies cast and receive real shadows (feet sit on the ground); larger props too.
//   local    - up to 2 fire/lantern lights near the player get soft shadows (total shadowed local lights <= 4).
//   ssao     - the renderer's SSAO was on but invisible: intensity 0.4, radius 0.3 m, and applied through the
//              _SCREEN_SPACE_OCCLUSION shader keyword, which almost no custom shader here samples. It now runs after
//              opaques (so it darkens every shader, also the ones that never sample the AO texture), radius 0.5 m,
//              intensity ~0.9.
//   grade    - a volume above LookFX (p1150, below UWLook 1200): +10 contrast and saturation (ACES opt-in), blacks
//              not lifted, bloom only on bright sources, vignette ~0.3. Off in 폐촌 (its post belongs to another pass)
//              and 저승 (UWLook owns it).
// Each room entry first writes the untouched state to ArtSource/ShadowPass/diag_<room>.txt (editor only).
// Toggles: PlayerPrefs RR.ShadowPass (master, 1 by default since 11-19 12:23), RR.ShadowPass.<sun|actors|props|local|ssao|grade|pipe> (1 default).
// rr_cmd: "call RoomRun.ShadowPass.Off" / ".On", shots token "off=ShadowPass" for a before/after in one run.
// v5 (11-19): contact shadow under every actor (ContactShadow.cs), own toggle RR.ContactShadow (1; "off=ContactShadow").
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace RoomRun
{
    [DisallowMultipleComponent]
    public sealed class ShadowPass : MonoBehaviour
    {
        public const string Key = "RR.ShadowPass";
        // 11-19 12:23 Kido on the v5 compare ("훨씬 좋다 적용해"): ON for everyone by default; RR.ShadowPass=0 still turns it
        // off (RR.ContactShadow=0 just the contact shadow). The capture-only period left RR.ShadowPass at 0 in this editor,
        // so the first run after this change resets it to 1 once (RR.ShadowPass.Default5).
        public static bool Enabled => capture >= 0 ? capture == 1 : PlayerPrefs.GetInt(Key, 1) != 0;
        static void MigrateDefault()
        {
            if (PlayerPrefs.GetInt(Key + ".Default5", 0) != 0) return;
            PlayerPrefs.SetInt(Key + ".Default5", 1); PlayerPrefs.SetInt(Key, 1); PlayerPrefs.Save();
        }
        // a capture can still force it for itself only: shots token "sp=0" (a before shot) / "sp=1"; RoomRunRemote drops that
        // override (ForceOff = back to the default, it no longer switches anything off) when the capture / Play / queue item ends
        static int capture = -1;
        public static bool CaptureOn { get => capture == 1; set => capture = value ? 1 : 0; }
        public static void ForceOff() { capture = -1; }
        public static void On() { PlayerPrefs.SetInt(Key, 1); PlayerPrefs.Save(); }
        public static void Off() { PlayerPrefs.SetInt(Key, 0); PlayerPrefs.Save(); }
        static bool Sub(string n) => PlayerPrefs.GetInt(Key + "." + n, 1) != 0;
        static void SetSub(string n, bool v) { PlayerPrefs.SetInt(Key + "." + n, v ? 1 : 0); PlayerPrefs.Save(); Kick(); }
        public static void GradeOff() { SetSub("grade", false); }
        public static void GradeOn() { SetSub("grade", true); }
        public static void SsaoOff() { SetSub("ssao", false); }
        public static void SsaoOn() { SetSub("ssao", true); }
        public static void SunOff() { SetSub("sun", false); }
        public static void SunOn() { SetSub("sun", true); }
        public static void PropsOff() { SetSub("props", false); }
        public static void PropsOn() { SetSub("props", true); }
        public static void LocalOff() { SetSub("local", false); }
        public static void LocalOn() { SetSub("local", true); }
        // the parts are separate components only so a capture can switch one off ("off=ShadowPassSsao"); they hold no state
        bool PartOn<T>() where T : Behaviour { var c = GetComponent<T>(); return !c || c.enabled; }
        public void Reapply()
        {
            if (!enabled || pending >= 0 || cur == null) return;
            Revert();
            Apply();
        }
        static void Kick() { var sp = FindAnyObjectByType<ShadowPass>(); if (sp && sp.enabled) { sp.enabled = false; sp.enabled = true; } }

        public sealed class Tune
        {
            // v3 (11-19, Kido on the v1 compare: "the BEFORE ones are better" - v1 brightened the frame 29 -> 36, went grey
            // and flat, and its vignette 0.3 replaced the stages' 0.42). The stage look stays exactly as it is (brightness,
            // colour, desaturated mood, vignette, tonemapper, ambient colour); only grounding is added: real soft shadows
            // at full strength from actors and props, the shade side (ambient) a touch lower, contact AO under feet / walls
            // / rocks. No sun boost, no grade volume. v1: sun x1.25, ambient x0.8, LS 1.15 / 0.78, grade +10 / +10.
            // (v2 - sun x1.5 at 35 deg, ambient x0.5, contrast +20 - was dropped before review.)
            public float sun = 1f;           // directional light intensity x
            public float ambient = 0.9f;     // RenderSettings ambient x
            public float lsSun = 1f;         // LookStudy shaders: _LSSunCol x
            public float lsShadow = 0.9f;    // LookStudy shaders: _LSShadowMul (their flat ambient) x
            public float elevation = 0f;     // sun pitch in degrees (yaw kept), 0 = keep the stage's
            public bool grade = false;       // the grade volume (contrast / saturation / vignette): off, the stages own the look
            public float contrastAdd = 0f, saturationAdd = 0f, exposureAdd = 0f, lift = 0f, vignette = 0.42f;
            public float probe = 0f;
            public float groundShadow = 1f; // _RRGroundShadow: ground fog layers let shadows through (0 = old look)
            // v5 contact shadow (ContactShadow.cs, RR.ContactShadow): darkness at the centre, size x, sun offset m, height fade m
            public float csDark = 0.55f, csSize = 1f, csSunOffset = 0.15f, csLift = 2.5f;
            public bool split = false;        // split toning: cool shadows, warm highlights (over LookFX's milder one)
            public Color splitShadows = new Color(0.30f, 0.42f, 0.60f), splitHighlights = new Color(0.80f, 0.66f, 0.48f);
            public float splitBalance = 0f;
            // 0 keep the stage's tonemapper, 1 ACES, 2 Neutral. ACES was tried first: on these graded-dark fields it took
            // the mean luminance from ~30 to ~10 (11-19 capture), so it is opt-in per room until the stage grades are re-based.
            public int tonemap = 0;
            public int aoSource = 1;   // 1 DepthNormals, 0 depth only (depth-only reconstruction darkened 폐촌 evenly, no contact shading)
            public float aoIntensity = 1.2f, aoRadius = 0.6f, aoDirect = 0.3f;   // contact only (v1 0.9 / 0.5 read as an even grey)
            public Tune MemberwiseCloneTune() => (Tune)MemberwiseClone();
        }
        public static Tune Default = new Tune();
        public static readonly Dictionary<string, Tune> Rooms = new Dictionary<string, Tune>
        {
            // UWLook grades it; the floor must never go pale: no extra sun, ambient only down
            // UWLook grades it; the floor must never go pale
            { "underworld", new Tune { lsShadow = 1f } },
            // 폐촌: moon / fog belong to the light-radius pass; v1 SSAO 0.45 / 0.5 halved the frame evenly
            { "abandoned", new Tune { lsShadow = 1f, aoIntensity = 0.8f, aoRadius = 0.5f } },
            // instanced grass everywhere: a wide radius darkens the field evenly
            { "grass", new Tune { aoIntensity = 1.0f, aoRadius = 0.5f } },
        };
        public static Tune For(string room) => room != null && Rooms.TryGetValue(room, out var t) ? t : Default;

        public static string LastNote = "";

        RunDirector run;
        string roomSeen; int sceneLoads, sceneSeen = -1, pending = -1;
        static int sceneLoadCount;

        // ------------------------------------------------------------------ undo records
        readonly List<System.Action> undo = new List<System.Action>();
        readonly HashSet<int> actorDone = new HashSet<int>();
        Light sunLight; float sunWritten; Quaternion sunRotWritten;
        Vector3 lsSunBase, lsShadowBase, lsSunWritten, lsShadowWritten; bool lsActive; Tune cur;
        GameObject volGo;

        void OnEnable()
        {
            run = GetComponent<RunDirector>();
            roomSeen = null; sceneSeen = -1; pending = -1;
        }

        void OnDisable() { Revert(); }

        void Revert()
        {
            for (int i = undo.Count - 1; i >= 0; i--) { try { undo[i](); } catch (System.Exception e) { Debug.LogWarning("[ShadowPass] revert: " + e.Message); } }
            undo.Clear();
            actorDone.Clear();
            sunLight = null; lsActive = false; enemyNoted = false;
            if (volGo) Destroy(volGo);
            volGo = null;
        }

        void LateUpdate()
        {
            if (!run) run = RunDirector.Instance;
            if (!run) return;
            // undo on the old scene before the next one loads (its ambient/sun must not be overwritten afterwards)
            if (run.Transitioning) { if (undo.Count > 0 || volGo) Revert(); roomSeen = null; return; }
            if (run.Room == null) return;
            if (run.Room.id != roomSeen || sceneLoadCount != sceneSeen)
            {
                roomSeen = run.Room.id; sceneSeen = sceneLoadCount;
                Revert();
                pending = 4;   // let StageMood / LookFX / the stage scripts set the room up first
                return;
            }
            if (pending > 0) { pending--; return; }
            if (pending == 0)
            {
                pending = -1;
                cur = For(roomSeen);
#if UNITY_EDITOR
                cur = ReadTuneFile(roomSeen, cur);
#endif
#if UNITY_EDITOR
                try { WriteDiag(roomSeen); } catch (System.Exception e) { Debug.LogWarning("[ShadowPass] diag: " + e.Message); }
#endif
                Apply();
                return;
            }
            if (Time.frameCount % 15 == 0) Maintain();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { sceneLoadCount = 0; capture = -1; }

        // ------------------------------------------------------------------ apply
        void Apply()
        {
#if UNITY_EDITOR
            cur = ReadTuneFile(roomSeen, For(roomSeen));
#endif
            var sb = new StringBuilder($"[ShadowPass] {roomSeen}:");
            if (Sub("pipe")) ApplyPipeline(sb);
            if (Sub("sun") && PartOn<ShadowPassSun>()) ApplySun(sb);
            if (Sub("ssao") && PartOn<ShadowPassSsao>()) ApplySsao(sb);
            if (Sub("local")) ApplyLocalLights(sb);
            if (Sub("props")) ApplyProps(sb);
            if (Sub("actors")) ApplyActors(sb);
            if (Sub("grade") && cur.grade && PartOn<ShadowPassGrade>()) ApplyGrade(sb);
            if (ContactShadow.Enabled) ApplyContact(sb);
            // v4: ground fog sheets (snow FogSheet, reed FogCard) thin where the ground is shadowed (RRGroundShadow.hlsl)
            Shader.SetGlobalFloat("_RRGroundShadow", cur.groundShadow);
            undo.Add(() => Shader.SetGlobalFloat("_RRGroundShadow", 0f));
            sb.Append($" groundFog x{cur.groundShadow:0.0}");
            LastNote = sb.ToString();
            Log(LastNote);
#if UNITY_EDITOR
            CancelInvoke(nameof(LateDiag)); Invoke(nameof(LateDiag), 2.5f);
#endif
#if UNITY_EDITOR
            // v4 probe (tune.txt "<room> probe 1"): a 2 x 2 m slab 2.5 m above the player, casting only, to see whether
            // the floor shows any main-light shadow at all
            if (cur.probe > 0.5f && run.Player)
            {
                var pr = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(pr.GetComponent<Collider>());
                pr.name = "ShadowPass.ProbeSlab";
                pr.transform.position = run.Player.transform.position + new Vector3(2.5f, 2.5f, 0f);
                pr.transform.localScale = new Vector3(2f, 0.3f, 2f);
                var mr = pr.GetComponent<MeshRenderer>(); mr.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                undo.Add(() => { if (pr) Destroy(pr); });
            }
#endif
        }

        void Maintain()
        {
            // new enemies keep spawning
            if (Sub("actors")) ApplyActors(null);
            // a stage script re-setting the LookStudy globals: take its value as the new base
            if (lsActive)
            {
                Vector3 s = Shader.GetGlobalVector("_LSSunCol"), d = Shader.GetGlobalVector("_LSShadowMul");
                if ((s - lsSunWritten).sqrMagnitude > 1e-6f || (d - lsShadowWritten).sqrMagnitude > 1e-6f)
                {
                    lsSunBase = s; lsShadowBase = d;
                    WriteLS();
                }
            }
            // StageMood's fire flicker etc. never touch the sun; something else re-setting it: re-scale from that
            if (sunLight && !Mathf.Approximately(sunLight.intensity, sunWritten))
            {
                sunWritten = sunLight.intensity * cur.sun;
                sunLight.intensity = sunWritten;
            }
            if (sunLight && cur.elevation > 0f && Quaternion.Angle(sunLight.transform.rotation, sunRotWritten) > 0.01f)
            {
                var e = sunLight.transform.eulerAngles;
                sunLight.transform.rotation = Quaternion.Euler(cur.elevation, e.y, e.z);
                sunRotWritten = sunLight.transform.rotation;
            }
        }

        static UniversalRenderPipelineAsset Asset => GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;

        void ApplyPipeline(StringBuilder sb)
        {
            var a = Asset; if (!a) return;
            int res0 = a.mainLightShadowmapResolution, casc0 = a.shadowCascadeCount, addRes0 = a.additionalLightsShadowmapResolution;
            Vector3 split0 = a.cascade4Split; float border0 = a.cascadeBorder;
            var sq = typeof(UniversalRenderPipelineAsset).GetProperty("softShadowQuality", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            object sq0 = sq != null ? sq.GetValue(a) : null;
            a.mainLightShadowmapResolution = 4096;
            a.shadowCascadeCount = 4;
            // camera->ground is 19-33 m at the default zoom: the first cascade holds the whole near half of the screen
            a.cascade4Split = new Vector3(0.5f, 0.66f, 0.82f);
            a.cascadeBorder = 0.1f;
            // soft shadows and additional-light shadows are already supported by PC_RPAsset (read-only at runtime)
            a.additionalLightsShadowmapResolution = 4096;
            if (sq != null) { try { sq.SetValue(a, System.Enum.ToObject(sq.PropertyType, 3)); } catch { } }   // High
            undo.Add(() =>
            {
                var b = Asset; if (!b) return;
                b.mainLightShadowmapResolution = res0; b.shadowCascadeCount = casc0; b.cascade4Split = split0; b.cascadeBorder = border0;
                b.additionalLightsShadowmapResolution = addRes0;
                if (sq != null && sq0 != null) { try { sq.SetValue(b, sq0); } catch { } }
            });
            sb.Append($" pipe(4096 x4, was {res0} x{casc0}, dist {a.shadowDistance:0})");
        }

        static Light FindSun(RunDirector run)
        {
            var s = RenderSettings.sun;
            if (s && s.isActiveAndEnabled && s.type == LightType.Directional) return s;
            Light best = null;
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.isActiveAndEnabled && l.type == LightType.Directional && (!best || l.intensity > best.intensity)) best = l;
            return best;
        }

        void ApplySun(StringBuilder sb)
        {
            var l = FindSun(run);
            if (l)
            {
                var sh0 = l.shadows; float st0 = l.shadowStrength, i0 = l.intensity;
                float b0 = l.shadowBias, nb0 = l.shadowNormalBias;
                l.shadows = LightShadows.Soft;
                l.shadowStrength = 1f;
                l.intensity = i0 * cur.sun;
                sunLight = l; sunWritten = l.intensity;
                var r0 = l.transform.rotation; var e0 = l.transform.eulerAngles;
                // the top-down camera looks at the ground: a lower sun gives the long diagonal shadows (PoE2 / D4)
                if (cur.elevation > 0f) l.transform.rotation = Quaternion.Euler(cur.elevation, e0.y, e0.z);
                sunRotWritten = l.transform.rotation;
                undo.Add(() => { if (!l) return; l.shadows = sh0; l.shadowStrength = st0; l.shadowBias = b0; l.shadowNormalBias = nb0; if (Mathf.Approximately(l.intensity, sunWritten)) l.intensity = i0; if (Quaternion.Angle(l.transform.rotation, sunRotWritten) < 0.01f) l.transform.rotation = r0; });
                sb.Append($" sun({l.name} {i0:0.00}->{l.intensity:0.00}, pitch {e0.x:0}->{l.transform.eulerAngles.x:0}, shadows {sh0}->Soft, str {st0:0.00}->1)");
            }
            else sb.Append(" sun(none)");


            if (!Mathf.Approximately(cur.ambient, 1f))
            {
                Color a0 = RenderSettings.ambientLight, s0 = RenderSettings.ambientSkyColor, e0 = RenderSettings.ambientEquatorColor, g0 = RenderSettings.ambientGroundColor;
                float in0 = RenderSettings.ambientIntensity; var p0 = RenderSettings.ambientProbe;
                float k = cur.ambient;
                RenderSettings.ambientLight = a0 * k; RenderSettings.ambientSkyColor = s0 * k; RenderSettings.ambientEquatorColor = e0 * k; RenderSettings.ambientGroundColor = g0 * k;
                var p = p0; p *= k; RenderSettings.ambientProbe = p;
                Color sW = RenderSettings.ambientSkyColor;
                undo.Add(() => { if (RenderSettings.ambientSkyColor != sW) return; RenderSettings.ambientLight = a0; RenderSettings.ambientSkyColor = s0; RenderSettings.ambientEquatorColor = e0; RenderSettings.ambientGroundColor = g0; RenderSettings.ambientIntensity = in0; RenderSettings.ambientProbe = p0; });
                sb.Append($" ambient x{k:0.00}");
            }

            if (!Mathf.Approximately(cur.lsSun, 1f) || !Mathf.Approximately(cur.lsShadow, 1f))
            {
                lsSunBase = Shader.GetGlobalVector("_LSSunCol"); lsShadowBase = Shader.GetGlobalVector("_LSShadowMul");
                if (lsSunBase.sqrMagnitude > 1e-6f)
                {
                    lsActive = true;
                    WriteLS();
                    undo.Add(() =>
                    {
                        Vector3 s = Shader.GetGlobalVector("_LSSunCol"), d = Shader.GetGlobalVector("_LSShadowMul");
                        if ((s - lsSunWritten).sqrMagnitude < 1e-6f) Shader.SetGlobalVector("_LSSunCol", lsSunBase);
                        if ((d - lsShadowWritten).sqrMagnitude < 1e-6f) Shader.SetGlobalVector("_LSShadowMul", lsShadowBase);
                    });
                    sb.Append($" LS sun {lsSunBase.x:0.00}->{lsSunWritten.x:0.00} amb {lsShadowBase.x:0.00}->{lsShadowWritten.x:0.00}");
                }
            }
        }

        void WriteLS()
        {
            lsSunWritten = lsSunBase * cur.lsSun; lsShadowWritten = lsShadowBase * cur.lsShadow;
            Shader.SetGlobalVector("_LSSunCol", lsSunWritten);
            Shader.SetGlobalVector("_LSShadowMul", lsShadowWritten);
        }

        // ------------------------------------------------------------------ SSAO (renderer feature settings, internal)
        static ScriptableRendererFeature FindSsao()
        {
            var a = Asset; if (!a) return null;
            foreach (var rd in a.rendererDataList)
            {
                if (!rd) continue;
                foreach (var f in rd.rendererFeatures)
                    if (f && f.GetType().Name == "ScreenSpaceAmbientOcclusion") return f;
            }
            return null;
        }

        void ApplySsao(StringBuilder sb)
        {
            if (cur.aoIntensity <= 0f) { sb.Append(" ssao(skipped for this room)"); return; }
            var f = FindSsao();
            if (!f) { sb.Append(" ssao(missing)"); return; }
            var sf = f.GetType().GetField("m_Settings", BindingFlags.Instance | BindingFlags.NonPublic);
            var s = sf?.GetValue(f);
            if (s == null) { sb.Append(" ssao(no settings)"); return; }
            var st = s.GetType();
            const BindingFlags BF = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            var keep = new Dictionary<FieldInfo, object>();
            void Set(string n, object v)
            {
                var fi = st.GetField(n, BF); if (fi == null) return;
                if (!keep.ContainsKey(fi)) keep[fi] = fi.GetValue(s);
                fi.SetValue(s, fi.FieldType.IsEnum ? System.Enum.ToObject(fi.FieldType, v) : v);
            }
            object Get(string n) => st.GetField(n, BF)?.GetValue(s);
            string was = "";
            bool active0 = f.isActive;
#if UNITY_EDITOR
            SsaoFromDisk();   // the feature lives in an asset: values a previous run left in memory must not become the "before"
#endif
            was = $"int {Get("Intensity")} r {Get("Radius")} after {Get("AfterOpaque")} src {Get("Source")}";
            Set("AOMethod", 0);           // blue noise
            Set("Source", cur.aoSource);  // DepthNormals (most stage shaders have the pass); 0 = depth only
            Set("NormalSamples", 1);
            Set("AfterOpaque", true);     // multiply the opaque image: custom shaders do not sample _ScreenSpaceOcclusionTexture
            Set("Downsample", false);
            Set("Intensity", cur.aoIntensity);
            Set("Radius", cur.aoRadius);
            Set("DirectLightingStrength", cur.aoDirect);
            Set("Samples", 0);            // high, 12
            Set("BlurQuality", 0);        // bilateral
            Set("Falloff", 60f);
            f.SetActive(true);
            undo.Add(() => { foreach (var kv in keep) kv.Key.SetValue(s, kv.Value); if (f) f.SetActive(active0); });
            sb.Append($" ssao({was} -> int {cur.aoIntensity} r {cur.aoRadius} after-opaque src {cur.aoSource})");
        }

        // ------------------------------------------------------------------ casters
        static bool HasPass(Material m, string lightMode)
        {
            if (!m || !m.shader) return false;
            var sh = m.shader; var tag = new ShaderTagId("LightMode"); var want = new ShaderTagId(lightMode);
            for (int i = 0; i < sh.passCount; i++) if (sh.FindPassTagValue(i, tag) == want) return true;
            return false;
        }
        static bool CastsOk(Renderer r)
        {
            var ms = r.sharedMaterials;
            if (ms == null || ms.Length == 0) return false;
            foreach (var m in ms) if (!m || m.renderQueue >= 2450 || !HasPass(m, "ShadowCaster")) return false;
            return true;
        }

        void ForceCast(Renderer r, bool receive)
        {
            if (!r || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) return;
            if (!CastsOk(r)) return;
            var c0 = r.shadowCastingMode; bool rc0 = r.receiveShadows;
            if (c0 == ShadowCastingMode.On && (!receive || rc0)) return;
            if (c0 == ShadowCastingMode.ShadowsOnly) return;
            r.shadowCastingMode = ShadowCastingMode.On;
            if (receive) r.receiveShadows = true;
            undo.Add(() => { if (r) { r.shadowCastingMode = c0; r.receiveShadows = rc0; } });
        }

        bool enemyNoted;
        void ApplyActors(StringBuilder sb)
        {
            if (!run) return;
            if (run.Player && actorDone.Add(run.Player.GetInstanceID()))
            {
                int u0 = undo.Count, n = 0; var shs = new HashSet<string>();
                foreach (var r in run.Player.GetComponentsInChildren<Renderer>(true)) { ForceCast(r, true); n++; if (r.sharedMaterial) shs.Add(r.sharedMaterial.shader.name + (HasPass(r.sharedMaterial, "ShadowCaster") ? "" : "(no ShadowCaster)")); }
                if (sb != null) sb.Append($" player({n} renderers, {undo.Count - u0} switched to cast/receive; {string.Join(",", shs)})");
            }
            foreach (var e in run.Enemies)
            {
                if (!e || !actorDone.Add(e.GetInstanceID())) continue;
                int u0 = undo.Count, n = 0; var shs = new HashSet<string>(); var modes = new HashSet<string>();
                foreach (var r in e.GetComponentsInChildren<Renderer>(true))
                {
                    modes.Add(r.GetType().Name + ":" + r.shadowCastingMode + (r.receiveShadows ? "/recv" : "/norecv"));
                    ForceCast(r, true); n++;
                    if (r.sharedMaterial) shs.Add(r.sharedMaterial.shader.name + (HasPass(r.sharedMaterial, "ShadowCaster") ? "" : "(no ShadowCaster)"));
                }
                if (!enemyNoted) { enemyNoted = true; Log($"[ShadowPass] {roomSeen} first enemy {e.name}: {n} renderers, {undo.Count - u0} switched; before {string.Join(",", modes)}; shaders {string.Join(",", shs)}"); }
            }
        }

        internal static void Log(string s)
        {
            Debug.Log(s);
#if UNITY_EDITOR
            try
            {
                string dir = System.IO.Path.Combine(Application.dataPath, "..", "ArtSource", "ShadowPass");
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "log.txt"), System.DateTime.Now.ToString("HH:mm:ss ") + s + System.Environment.NewLine);
            }
            catch { }
#endif
        }

        static readonly string[] PropSkip = { "grass", "fern", "fog", "veil", "mist", "water", "river", "decal", "shaft", "glow", "card", "blob", "sil", "edge", "sky", "ground", "floor", "terrain", "moss", "leaf", "leaves", "reed", "flame", "fire", "wisp", "halo", "ring", "puddle", "lava" };
        void ApplyProps(StringBuilder sb)
        {
            if (!run || !run.Player) return;
            Vector3 c = run.Player.transform.position;
            int n = 0;
            for (int si = 0; si < UnityEngine.SceneManagement.SceneManager.sceneCount; si++)
            {
                var sc = UnityEngine.SceneManagement.SceneManager.GetSceneAt(si);
                if (!sc.isLoaded) continue;
                foreach (var root in sc.GetRootGameObjects())
                    foreach (var r in root.GetComponentsInChildren<MeshRenderer>())
                    {
                        if (!r.enabled || r.shadowCastingMode != ShadowCastingMode.Off) continue;
                        if (r.GetComponentInParent<PlayerController>() || r.GetComponentInParent<EnemyController>()) continue;
                        var b = r.bounds;
                        if (b.size.y < 0.6f || Mathf.Max(b.size.x, b.size.z) > 25f || b.size.y > 30f) continue;
                        var flat = b.center - c; flat.y = 0f;
                        if (flat.magnitude > 60f) continue;
                        string nm = (r.name + "|" + (r.sharedMaterial ? r.sharedMaterial.name : "") + "|" + (r.sharedMaterial && r.sharedMaterial.shader ? r.sharedMaterial.shader.name : "")).ToLowerInvariant();
                        bool skip = false; foreach (var k in PropSkip) if (nm.Contains(k)) { skip = true; break; }
                        if (skip) continue;
                        int before = undo.Count;
                        ForceCast(r, false);
                        if (undo.Count > before) n++;
                    }
            }
            sb.Append($" props(+{n} casters)");
        }

        static readonly string[] FireNames = { "fire", "torch", "lantern", "brazier", "flame", "candle", "lamp", "hall" };
        void ApplyLocalLights(StringBuilder sb)
        {
            if (!run || !run.Player) return;
            Vector3 c = run.Player.transform.position;
            var all = FindObjectsByType<Light>(FindObjectsSortMode.None).Where(l => l.isActiveAndEnabled && l.type != LightType.Directional && l.intensity > 0f).ToList();
            int shadowed = all.Count(l => l.shadows != LightShadows.None);
            var pick = all.Where(l => l.shadows == LightShadows.None && !l.GetComponentInParent<PlayerController>() && !l.GetComponentInParent<EnemyController>()
                                      && FireNames.Any(k => l.name.ToLowerInvariant().Contains(k)))
                          .OrderBy(l => (l.transform.position - c).sqrMagnitude).Take(Mathf.Clamp(4 - shadowed, 0, 2)).ToList();
            foreach (var l in pick)
            {
                if ((l.transform.position - c).magnitude > 30f) continue;
                var s0 = l.shadows; float st0 = l.shadowStrength, np0 = l.shadowNearPlane;
                l.shadows = LightShadows.Soft; l.shadowStrength = 0.85f; l.shadowNearPlane = 0.2f;
                var ll = l;
                undo.Add(() => { if (ll) { ll.shadows = s0; ll.shadowStrength = st0; ll.shadowNearPlane = np0; } });
            }
            sb.Append($" local(lights {all.Count}, shadowed {shadowed} + {pick.Count})");
        }

        // ------------------------------------------------------------------ v5 contact shadow under every actor
        void ApplyContact(StringBuilder sb)
        {
            var cs = GetComponent<ContactShadow>();
            bool added = !cs;
            if (added) cs = gameObject.AddComponent<ContactShadow>();
            cs.darkness = cur.csDark; cs.size = cur.csSize; cs.sunOffset = cur.csSunOffset; cs.liftRange = cur.csLift;
            cs.enabled = true;
            var keep = cs;
            undo.Add(() => { if (keep) { keep.enabled = false; DestroyImmediate(keep); } });
            sb.Append($" contact(dark {cur.csDark:0.00} size x{cur.csSize:0.00} sun {cur.csSunOffset:0.00} m)");
        }

        // ------------------------------------------------------------------ grade
        static T Base<T>(System.Func<T, bool> has) where T : VolumeComponent
        {
            T best = null; float bestP = float.MinValue;
            foreach (var v in VolumeManager.instance.GetVolumes(~0))
            {
                if (!v || !v.isActiveAndEnabled || !v.isGlobal || v.priority >= 1150f || v.weight <= 0f) continue;
                var p = v.HasInstantiatedProfile() ? v.profile : v.sharedProfile;
                if (!p || !p.TryGet<T>(out var c) || !c.active || !has(c)) continue;
                if (v.priority >= bestP) { bestP = v.priority; best = c; }
            }
            return best;
        }

        void ApplyGrade(StringBuilder sb)
        {
            volGo = new GameObject("ShadowPass.Grade");
            volGo.transform.SetParent(transform, false);
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true; vol.priority = 1150f; vol.weight = 1f;
            var p = ScriptableObject.CreateInstance<VolumeProfile>();
            p.name = "ShadowPass.Grade";
            vol.sharedProfile = p;

            if (cur.tonemap > 0)
            {
                var tm = p.Add<Tonemapping>(false);
                tm.mode.Override(cur.tonemap == 1 ? TonemappingMode.ACES : TonemappingMode.Neutral);
            }

            var ca0 = Base<ColorAdjustments>(c => c.contrast.overrideState);
            var cs0 = Base<ColorAdjustments>(c => c.saturation.overrideState);
            var ce0 = Base<ColorAdjustments>(c => c.postExposure.overrideState);
            float con = ca0 ? ca0.contrast.value : 0f, sat = cs0 ? cs0.saturation.value : 0f, exp = ce0 ? ce0.postExposure.value : 0f;
            var ca = p.Add<ColorAdjustments>(false);
            ca.contrast.Override(Mathf.Clamp(con + cur.contrastAdd, -100f, 100f));
            ca.saturation.Override(Mathf.Clamp(sat + cur.saturationAdd, -100f, 100f));
            ca.postExposure.Override(exp + cur.exposureAdd);

            // lift is added in linear space before tonemapping: on these dark fields (linear 0.01-0.05) even -0.035 turned
            // the whole frame black (11-19 first capture). Blacks stay where the stage grades put them; only a tiny nudge.
            if (Mathf.Abs(cur.lift) > 1e-5f)
            {
                var lgg = p.Add<LiftGammaGain>(false);
                lgg.lift.Override(new Vector4(1f, 1f, 1f, cur.lift));
            }

            if (cur.split)
            {
                var spt = p.Add<SplitToning>(false);
                spt.shadows.Override(cur.splitShadows);
                spt.highlights.Override(cur.splitHighlights);
                spt.balance.Override(cur.splitBalance);
            }

            var bl0 = Base<Bloom>(b => b.threshold.overrideState);
            var bl = p.Add<Bloom>(false);
            bl.threshold.Override(Mathf.Max(bl0 ? bl0.threshold.value : 0.9f, 1.05f));

            var vg = p.Add<Vignette>(false);
            vg.intensity.Override(cur.vignette);

            var pp = p;
            undo.Add(() => { if (pp) Destroy(pp); });
            sb.Append($" grade(tm {cur.tonemap}, contrast {con:0}->{ca.contrast.value:0}, sat {sat:0}->{ca.saturation.value:0}, lift {cur.lift}, split {(cur.split ? "cool/warm" : "stage")}, vignette {cur.vignette})");
        }

        // ------------------------------------------------------------------ diagnostics (untouched state)
#if UNITY_EDITOR
        void WriteDiag(string room)
        {
            var sb = new StringBuilder();
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            sb.AppendLine($"room {room}  scene {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}  {System.DateTime.Now:HH:mm:ss}");
            var a = Asset;
            if (a) sb.AppendLine($"pipeline {a.name}: mainShadowRes {a.mainLightShadowmapResolution} cascades {a.shadowCascadeCount} split4 {a.cascade4Split} dist {a.shadowDistance:0.0} soft {a.supportsSoftShadows} depthBias {a.shadowDepthBias} normalBias {a.shadowNormalBias} addShadows {a.supportsAdditionalLightShadows}");
            var f = FindSsao();
            if (f)
            {
                var s = f.GetType().GetField("m_Settings", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(f);
                var parts = new List<string>();
                if (s != null) foreach (var fi in s.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)) parts.Add(fi.Name + "=" + fi.GetValue(s));
                sb.AppendLine($"ssao active {f.isActive}: {string.Join(" ", parts)}");
            }
            else sb.AppendLine("ssao: none");
            sb.AppendLine($"ambient mode {RenderSettings.ambientMode} sky {RenderSettings.ambientSkyColor} eq {RenderSettings.ambientEquatorColor} gnd {RenderSettings.ambientGroundColor} flat {RenderSettings.ambientLight} int {RenderSettings.ambientIntensity:0.00} probeDC {RenderSettings.ambientProbe[0, 0]:0.000},{RenderSettings.ambientProbe[1, 0]:0.000},{RenderSettings.ambientProbe[2, 0]:0.000}");
            sb.AppendLine($"LS globals sun {(Vector3)Shader.GetGlobalVector("_LSSunCol")} shadowMul {(Vector3)Shader.GetGlobalVector("_LSShadowMul")}");
            sb.AppendLine($"RenderSettings.sun {(RenderSettings.sun ? RenderSettings.sun.name : "null")}");
            var lights = FindObjectsByType<Light>(FindObjectsSortMode.None);
            foreach (var l in lights.Where(l => l.type == LightType.Directional))
                sb.AppendLine($"  DIR {l.name} en {l.isActiveAndEnabled} int {l.intensity:0.00} col {l.color} shadows {l.shadows} str {l.shadowStrength:0.00} bias {l.shadowBias:0.00}/{l.shadowNormalBias:0.00} rot {l.transform.eulerAngles} mode {l.lightmapBakeType}");
            int nPt = lights.Count(l => l.type != LightType.Directional && l.isActiveAndEnabled), nPtSh = lights.Count(l => l.type != LightType.Directional && l.isActiveAndEnabled && l.shadows != LightShadows.None);
            sb.AppendLine($"local lights on {nPt} (shadowed {nPtSh}): " + string.Join(", ", lights.Where(l => l.type != LightType.Directional && l.isActiveAndEnabled).Take(14).Select(l => $"{l.name}({l.type} {l.intensity:0.0} r{l.range:0} {l.shadows})")));
            // volume stack (final values the camera sees last frame)
            var st = VolumeManager.instance.stack;
            if (st != null)
            {
                var tm = st.GetComponent<Tonemapping>(); var ca = st.GetComponent<ColorAdjustments>(); var lgg = st.GetComponent<LiftGammaGain>(); var smh = st.GetComponent<ShadowsMidtonesHighlights>(); var bl = st.GetComponent<Bloom>(); var vg = st.GetComponent<Vignette>();
                sb.AppendLine($"post: tonemap {tm?.mode.value} exposure {ca?.postExposure.value:0.00} contrast {ca?.contrast.value:0} sat {ca?.saturation.value:0} filter {ca?.colorFilter.value} lift {lgg?.lift.value} smh.shadows {smh?.shadows.value} bloom thr {bl?.threshold.value:0.00} int {bl?.intensity.value:0.00} vignette {vg?.intensity.value:0.00}");
            }
            foreach (var v in VolumeManager.instance.GetVolumes(~0).Where(v => v && v.isActiveAndEnabled))
            {
                var p = v.HasInstantiatedProfile() ? v.profile : v.sharedProfile;
                sb.AppendLine($"  volume {v.name} p{v.priority} w{v.weight:0.00} global {v.isGlobal}: " + (p ? string.Join(",", p.components.Where(c => c.active).Select(c => c.GetType().Name)) : "-"));
            }
            // renderers near the player, by shader
            Vector3 c0 = run && run.Player ? run.Player.transform.position : Vector3.zero;
            var rows = new Dictionary<string, int[]>();   // n, castOn, recv, SC, DN, DO, mlsKw, ssaoKw
            void Count(Renderer r, string group)
            {
                var m = r.sharedMaterial; var sh = m ? m.shader : null;
                string key = group + " | " + (sh ? sh.name : "null");
                if (!rows.TryGetValue(key, out var v)) { v = new int[8]; rows[key] = v;
                    if (m) { v[3] = HasPass(m, "ShadowCaster") ? 1 : 0; v[4] = HasPass(m, "DepthNormals") || HasPass(m, "DepthNormalsOnly") ? 1 : 0; v[5] = HasPass(m, "DepthOnly") ? 1 : 0; }
                    if (sh) { v[6] = sh.keywordSpace.FindKeyword("_MAIN_LIGHT_SHADOWS_CASCADE").isValid || sh.keywordSpace.FindKeyword("_MAIN_LIGHT_SHADOWS").isValid ? 1 : 0; v[7] = sh.keywordSpace.FindKeyword("_SCREEN_SPACE_OCCLUSION").isValid ? 1 : 0; }
                }
                v[0]++; if (r.shadowCastingMode != ShadowCastingMode.Off) v[1]++; if (r.receiveShadows) v[2]++;
            }
            if (run && run.Player) foreach (var r in run.Player.GetComponentsInChildren<Renderer>()) if (r.enabled) Count(r, "PLAYER");
            if (run) foreach (var e in run.Enemies.Take(30)) if (e) foreach (var r in e.GetComponentsInChildren<Renderer>()) if (r.enabled) Count(r, "ENEMY");
            for (int si = 0; si < UnityEngine.SceneManagement.SceneManager.sceneCount; si++)
            {
                var sc = UnityEngine.SceneManagement.SceneManager.GetSceneAt(si);
                if (!sc.isLoaded) continue;
                foreach (var root in sc.GetRootGameObjects())
                    foreach (var r in root.GetComponentsInChildren<Renderer>())
                    {
                        if (!r.enabled || r is ParticleSystemRenderer) continue;
                        if (r.GetComponentInParent<PlayerController>() || r.GetComponentInParent<EnemyController>()) continue;
                        var flat = r.bounds.center - c0; flat.y = 0;
                        if (flat.magnitude > 40f && r.bounds.extents.magnitude < 40f) continue;
                        Count(r, "SCENE");
                    }
            }
            // 11-19 v4: everything drawn at the player's feet (which surface should show the hero's shadow, and what lies on it)
            if (run && run.Player)
            {
                Vector3 pp = run.Player.transform.position;
                sb.AppendLine($"UNDER PLAYER at {pp}: renderer | shader | queue | recv cast | layer | bounds y | keywords");
                for (int si = 0; si < UnityEngine.SceneManagement.SceneManager.sceneCount; si++)
                {
                    var sc = UnityEngine.SceneManagement.SceneManager.GetSceneAt(si);
                    if (!sc.isLoaded) continue;
                    foreach (var root in sc.GetRootGameObjects())
                        foreach (var r in root.GetComponentsInChildren<Renderer>())
                        {
                            if (!r.enabled || !r.gameObject.activeInHierarchy || r is ParticleSystemRenderer) continue;
                            if (r.GetComponentInParent<PlayerController>() || r.GetComponentInParent<EnemyController>()) continue;
                            var b = r.bounds;
                            if (pp.x < b.min.x - 0.5f || pp.x > b.max.x + 0.5f || pp.z < b.min.z - 0.5f || pp.z > b.max.z + 0.5f) continue;
                            if (b.max.y < pp.y - 1.5f || b.min.y > pp.y + 1.0f) continue;
                            var m = r.sharedMaterial;
                            sb.AppendLine($"  {r.name} | {(m && m.shader ? m.shader.name : "-")} | {(m ? m.renderQueue : 0)} | {r.receiveShadows} {r.shadowCastingMode} | {LayerMask.LayerToName(r.gameObject.layer)} | {b.min.y:0.00}..{b.max.y:0.00} | {(m ? string.Join(" ", m.shaderKeywords) : "")}");
                        }
                }
                foreach (var li in Object.FindObjectsByType<LookStudyInstancer>(FindObjectsSortMode.None)) sb.AppendLine($"  instancer {li.name}");
                // casters between the player's feet and the sun (bounds test): a big one here keeps the whole spot in shadow
                var sunL = FindSun(run);
                if (sunL)
                {
                    var ray = new Ray(pp + Vector3.up * 0.3f, -sunL.transform.forward);
                    sb.AppendLine($"SUN RAY from feet toward {sunL.name} dir {ray.direction}: casters hit (bounds)");
                    for (int si = 0; si < UnityEngine.SceneManagement.SceneManager.sceneCount; si++)
                    {
                        var sc = UnityEngine.SceneManagement.SceneManager.GetSceneAt(si);
                        if (!sc.isLoaded) continue;
                        foreach (var root in sc.GetRootGameObjects())
                            foreach (var r in root.GetComponentsInChildren<Renderer>())
                            {
                                if (!r.enabled || !r.gameObject.activeInHierarchy || r.shadowCastingMode == ShadowCastingMode.Off) continue;
                                if (r.GetComponentInParent<PlayerController>()) continue;
                                if (r.bounds.IntersectRay(ray, out float dist) && dist < 120f)
                                    sb.AppendLine($"  {r.name} | {(r.sharedMaterial && r.sharedMaterial.shader ? r.sharedMaterial.shader.name : "-")} | {r.shadowCastingMode} | dist {dist:0.0} | size {r.bounds.size}");
                            }
                    }
                }
            }
            sb.AppendLine("group | shader : n castOn recv | passes ShadowCaster DepthNormals DepthOnly | kw mainShadows ssao");
            foreach (var kv in rows.OrderBy(k => k.Key.StartsWith("SCENE") ? 1 : 0).ThenByDescending(k => k.Value[0]))
                sb.AppendLine($"  {kv.Key} : {kv.Value[0]} {kv.Value[1]} {kv.Value[2]} | {kv.Value[3]} {kv.Value[4]} {kv.Value[5]} | {kv.Value[6]} {kv.Value[7]}");
            string dir = System.IO.Path.Combine(Application.dataPath, "..", "ArtSource", "ShadowPass");
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, $"diag_{room}.txt"), sb.ToString());
        }
#endif

#if UNITY_EDITOR
        // v4 probe: the live shadow state a moment after apply (대전 앞 showed no hero shadow on the same shader as 민가)
        void LateDiag()
        {
            if (!run || run.Room == null) return;
            var sb = new StringBuilder();
            sb.AppendLine($"late {run.Room.id} {System.DateTime.Now:HH:mm:ss}");
            var a = Asset;
            if (a) sb.AppendLine($"asset {a.name} mainShadows {a.supportsMainLightShadows} dist {a.shadowDistance:0.0} casc {a.shadowCascadeCount} res {a.mainLightShadowmapResolution}");
            sb.AppendLine($"quality shadows {QualitySettings.shadows} RenderSettings.sun {(RenderSettings.sun ? RenderSettings.sun.name : "null")}");
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional) sb.AppendLine($"  DIR {l.name} en {l.isActiveAndEnabled} int {l.intensity:0.00} shadows {l.shadows} str {l.shadowStrength:0.00} cull {l.cullingMask} rl {l.renderingLayerMask} scene {l.gameObject.scene.name}");
            var cam = run.Rig ? run.Rig.Cam : Camera.main;
            if (cam)
            {
                var cd = cam.GetComponent<UniversalAdditionalCameraData>();
                sb.AppendLine($"cam {cam.name} pos {cam.transform.position} renderShadows {(cd ? cd.renderShadows.ToString() : "-")} cullMask {cam.cullingMask} far {cam.farClipPlane}");
            }
            sb.AppendLine($"globals _LSSunCol {(Vector3)Shader.GetGlobalVector("_LSSunCol")} _LSShadowMul {(Vector3)Shader.GetGlobalVector("_LSShadowMul")} _MainLightPosition {Shader.GetGlobalVector("_MainLightPosition")} _MainLightColor {Shader.GetGlobalVector("_MainLightColor")}");
            if (run.Player) foreach (var r in run.Player.GetComponentsInChildren<Renderer>(true)) sb.AppendLine($"  player {r.name} en {r.enabled}/{r.gameObject.activeInHierarchy} cast {r.shadowCastingMode} layer {r.gameObject.layer} rl {r.renderingLayerMask} shader {(r.sharedMaterial ? r.sharedMaterial.shader.name : "-")}");
            try { System.IO.File.WriteAllText(System.IO.Path.Combine(Application.dataPath, "..", "ArtSource", "ShadowPass", $"late_{run.Room.id}.txt"), sb.ToString()); } catch { }
        }

        // 11-19 v3: the SSAO renderer feature is an asset, so the in-memory settings outlive Play; the v2 values (2.0 / 1 m,
        // after-opaque) were found still set at the next run, which made that run's "before" shots carry AO too. Every Play
        // start and every apply first puts back what Assets/Settings/PC_Renderer.asset holds on disk.
        public static string SsaoFromDisk()
        {
            var f = FindSsao(); if (!f) return "no ssao";
            var s = f.GetType().GetField("m_Settings", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(f);
            if (s == null) return "no settings";
            string path = System.IO.Path.Combine(Application.dataPath, "Settings", "PC_Renderer.asset");
            if (!System.IO.File.Exists(path)) return "no asset file";
            var lines = System.IO.File.ReadAllLines(path);
            int at = System.Array.FindIndex(lines, l => l.Trim() == "m_Name: ScreenSpaceAmbientOcclusion");
            if (at < 0) return "feature not in file";
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            const BindingFlags BF = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            var sb = new StringBuilder();
            bool inSettings = false;
            for (int i = at + 1; i < lines.Length; i++)
            {
                string t = lines[i];
                if (t.StartsWith("--- ")) break;
                if (t.Trim() == "m_Settings:") { inSettings = true; continue; }
                if (!inSettings) continue;
                if (!t.StartsWith("    ")) break;
                int c = t.IndexOf(':'); if (c < 0) continue;
                string k = t.Substring(0, c).Trim(), v = t.Substring(c + 1).Trim();
                var fi = s.GetType().GetField(k, BF); if (fi == null) continue;
                try
                {
                    object o;
                    if (fi.FieldType == typeof(bool)) o = v != "0";
                    else if (fi.FieldType == typeof(float)) o = float.Parse(v, ci);
                    else if (fi.FieldType == typeof(int)) o = int.Parse(v, ci);
                    else if (fi.FieldType.IsEnum) o = System.Enum.ToObject(fi.FieldType, int.Parse(v, ci));
                    else continue;
                    if (!Equals(fi.GetValue(s), o)) { sb.Append($" {k} {fi.GetValue(s)}->{o}"); fi.SetValue(s, o); }
                }
                catch { }
            }
            return sb.Length > 0 ? "ssao reset from disk:" + sb : "ssao matches disk";
        }

        // ArtSource/ShadowPass/tune.txt (editor only, read on every room entry): "<room|*> <key> <value>", keys
        // sun amb lssun lsamb con sat exp vig ao aor aod grade(0/1) tm aosrc elev split(0/1)
        static Tune ReadTuneFile(string room, Tune t)
        {
            string path = System.IO.Path.Combine(Application.dataPath, "..", "ArtSource", "ShadowPass", "tune.txt");
            if (!System.IO.File.Exists(path)) return t;
            var c = (Tune)t.MemberwiseCloneTune();
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            foreach (var raw in System.IO.File.ReadAllLines(path))
            {
                var p = raw.Trim().Split(new[] { ' ', '	' }, System.StringSplitOptions.RemoveEmptyEntries);
                if (p.Length < 3 || p[0].StartsWith("#") || (p[0] != "*" && p[0] != room)) continue;
                if (!float.TryParse(p[2], System.Globalization.NumberStyles.Float, ci, out float v)) continue;
                switch (p[1])
                {
                    case "sun": c.sun = v; break;
                    case "amb": c.ambient = v; break;
                    case "lssun": c.lsSun = v; break;
                    case "lsamb": c.lsShadow = v; break;
                    case "con": c.contrastAdd = v; break;
                    case "sat": c.saturationAdd = v; break;
                    case "exp": c.exposureAdd = v; break;
                    case "vig": c.vignette = v; break;
                    case "ao": c.aoIntensity = v; break;
                    case "aor": c.aoRadius = v; break;
                    case "aod": c.aoDirect = v; break;
                    case "grade": c.grade = v > 0.5f; break;
                    case "tm": c.tonemap = (int)v; break;
                    case "aosrc": c.aoSource = (int)v; break;
                    case "elev": c.elevation = v; break;
                    case "split": c.split = v > 0.5f; break;
                    case "gsh": c.groundShadow = v; break;
                    case "probe": c.probe = v; break;
                    case "cs": c.csDark = v; break;
                    case "css": c.csSize = v; break;
                    case "cso": c.csSunOffset = v; break;
                    case "csl": c.csLift = v; break;
                }
            }
            return c;
        }
#endif

        // ------------------------------------------------------------------ boot
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += (s, m) => sceneLoadCount++;
            Shader.SetGlobalFloat("_RRGroundShadow", 0f);
            MigrateDefault();
#if UNITY_EDITOR
            try { var r = SsaoFromDisk(); if (r.StartsWith("ssao reset")) Log("[ShadowPass] play start: " + r); } catch (System.Exception e) { Debug.LogWarning("[ShadowPass] ssao reset: " + e.Message); }
#endif
            var go = new GameObject("ShadowPass.Driver") { hideFlags = HideFlags.HideInHierarchy };
            DontDestroyOnLoad(go);
            go.AddComponent<Driver>();
        }

        sealed class Driver : MonoBehaviour
        {
            float next; int lastPref = -1;
            void Update()
            {
                if (Time.unscaledTime < next) return;
                next = Time.unscaledTime + 0.5f;
                var run = RunDirector.Instance;
                if (!run) return;
                int pref = Enabled ? 1 : 0;
                var sp = run.GetComponent<ShadowPass>();
                if (!sp)
                {
                    sp = run.gameObject.AddComponent<ShadowPass>(); sp.enabled = pref == 1; lastPref = pref;
                    run.gameObject.AddComponent<ShadowPassSun>(); run.gameObject.AddComponent<ShadowPassSsao>(); run.gameObject.AddComponent<ShadowPassGrade>();
                    return;
                }
                // only follow the pref when it changes, so a capture's "off=ShadowPass" is not fought
                if (pref != lastPref) { sp.enabled = pref == 1; lastPref = pref; }
            }
        }
    }

    // switches for captures (off=ShadowPassSsao etc.); toggling one re-applies the pass at once
    public sealed class ShadowPassSun : MonoBehaviour { void OnEnable() { GetComponent<ShadowPass>()?.Reapply(); } void OnDisable() { GetComponent<ShadowPass>()?.Reapply(); } }
    public sealed class ShadowPassSsao : MonoBehaviour { void OnEnable() { GetComponent<ShadowPass>()?.Reapply(); } void OnDisable() { GetComponent<ShadowPass>()?.Reapply(); } }
    public sealed class ShadowPassGrade : MonoBehaviour { void OnEnable() { GetComponent<ShadowPass>()?.Reapply(); } void OnDisable() { GetComponent<ShadowPass>()?.Reapply(); } }
}
