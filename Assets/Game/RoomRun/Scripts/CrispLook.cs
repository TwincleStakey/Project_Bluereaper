// Crisp look (11-19, Kido picked panel 5 of the "watercolour" test: "요게 가장 좋아"). The soft, washed look came from
// LookFX's depth of field (bokeh focused on the player, so the top-down floor was always half out of focus), its grey
// pressed-grade veil, scene fog, chromatic aberration and film grain. This layer sits on top of LookFX / StageMood
// without editing them (they belong to other threads):
//   - a global volume at priority 1300 (above LookFX 1100 and UWLook 1200): depth of field off, chromatic aberration 0,
//     film grain 0; split toning with cool teal-slate shadows and neutral highlights; saturation -60 and contrast 22
//     (absolute, every room); a hue-vs-saturation curve that keeps reds/oranges saturated (enemies, attacks, fire);
//     lift/gain per room (black point down to ~3-7 on screen, gain so each room stays readable)
//   - LookFX's veil switched off each frame (its private veilOn flag, set again by LookFX in its own LateUpdate)
//   - scene fog x0.5 once per stage scene load (CameraRig then keeps that as its zoom-1 fog)
//   - camera default zoom 1.45 (was 1.3 until 11-20) instead of 1.6 (with the 1.5x player = about 12.5 % of screen height; Kido: a little
//     farther than PoE2's 15-17 %); the wheel still
//     zooms in and back out to 1.6; remote captures still set their own zoom
// Kill switches: PlayerPrefs RR.Crisp 0 = the old post look, RR.CamClose 0 = the old camera.
// Editor tuning without a recompile: ArtSource/LightRadius/crisp.txt lines "<room|*> <lift> <gain>".
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace RoomRun
{
    [DefaultExecutionOrder(5000)]
    public sealed class CrispLook : MonoBehaviour
    {
        public const string Key = "RR.Crisp", CamKey = "RR.CamClose";
        public static bool On => PlayerPrefs.GetInt(Key, 1) != 0;
        public static bool CamOn => PlayerPrefs.GetInt(CamKey, 1) != 0;
        // rr_queue "call RoomRun.CrispLook.Off" / ".Enable" (both switches) for before/after captures
        public static void Off() { PlayerPrefs.SetInt(Key, 0); PlayerPrefs.SetInt(CamKey, 0); PlayerPrefs.Save(); }
        public static void Enable() { PlayerPrefs.DeleteKey(Key); PlayerPrefs.DeleteKey(CamKey); PlayerPrefs.Save(); }
        public static float CloseZoom = 1.45f /* 11-20 Kido "시야 조금 더 뒤로": 1.3 -> 1.45 (+11.5 % distance, pitch kept) */, FogScale = 0.5f, Saturation = -60f, Contrast = 22f;

        public struct Grade { public float lift, gain; public Grade(float l, float g) { lift = l; gain = g; } }
        // 대전 앞 is the look Kido picked (panel 5); the other rooms get their own gain so they read at about that level
        public static Grade Default = new Grade(-0.025f, 0.25f);
        public static readonly Dictionary<string, Grade> Rooms = new Dictionary<string, Grade>
        {
            { "palace", new Grade(-0.025f, 0.25f) },
            // tuned from capture medians (0-255) so each room reads at about the 대전 앞 level; rooms that leaned on the
            // veil for their brightness (민가, 대나무숲, 갈대밭, 봉인의 제단, 폐촌) get no black-point drop and more gain
            { "grass", new Grade(-0.02f, 0.35f) },
            { "valley", new Grade(-0.02f, 0.4f) },
            { "village", new Grade(-0.01f, 0.6f) },
            // 저승: the void stays black; more gain turned the floor island pale
            { "underworld", new Grade(0f, 0.1f) },
            { "abandoned", new Grade(0f, 0.5f) },
            { "reeds", new Grade(0f, 0.4f) },
            { "bamboo", new Grade(-0.01f, 0.55f) },
            { "bossseal", new Grade(-0.01f, 0.3f) },
            // DarkCrypt came out brighter than before with the default gain
            { "crypt", new Grade(-0.025f, -0.4f) },
            // 심연 고원 (11-20): no black-point drop (the crimson abyss and the cliff walls live in the darks), gain like 봉인의 제단
            { "killknight", new Grade(0f, 0.55f) },   // v4 capture: floor 49 vs ref 67, abyss 29 vs 86 at 0.2
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var go = new GameObject("CrispLook") { hideFlags = HideFlags.HideInHierarchy };
            DontDestroyOnLoad(go);
            go.AddComponent<CrispLook>();
        }

        static readonly FieldInfo VeilOnF = typeof(LookFX).GetField("veilOn", BindingFlags.NonPublic | BindingFlags.Instance);
        Volume vol; VolumeProfile prof;
        LiftGammaGain lgg; SplitToning split; ColorAdjustments adj; ColorCurves curves;
        DepthOfField dof; ChromaticAberration ca; FilmGrain grain;
        int sceneFogDone = -1, sceneSeen = -1; float sceneAt;
        CameraRig rigSeen;
        string roomSeen;
        Grade grade = Default;

        void Build()
        {
            vol = gameObject.AddComponent<Volume>(); vol.isGlobal = true; vol.priority = 1300;
            prof = ScriptableObject.CreateInstance<VolumeProfile>(); prof.hideFlags = HideFlags.DontSave; vol.sharedProfile = prof;
            dof = prof.Add<DepthOfField>(true); dof.mode.Override(DepthOfFieldMode.Off);
            ca = prof.Add<ChromaticAberration>(true); ca.intensity.Override(0f);
            grain = prof.Add<FilmGrain>(true); grain.intensity.Override(0f);
            split = prof.Add<SplitToning>(true);
            split.shadows.Override(new Color(0.36f, 0.52f, 0.56f)); split.highlights.Override(new Color(0.5f, 0.5f, 0.5f)); split.balance.Override(20f);
            // overrides=false: only the parameters set below take over (exposure, filter etc. stay the room's own)
            adj = prof.Add<ColorAdjustments>(false);
            adj.saturation.Override(Saturation); adj.contrast.Override(Contrast);
            curves = prof.Add<ColorCurves>(false);
            // hue vs saturation: 0.5 = unchanged; reds and oranges (hue ~0 / ~1, up to ~0.08) boosted
            curves.hueVsSat.Override(new TextureCurve(new[] { new Keyframe(0f, 0.85f), new Keyframe(0.08f, 0.75f), new Keyframe(0.16f, 0.5f), new Keyframe(0.9f, 0.5f), new Keyframe(0.97f, 0.8f) }, 0.5f, true, new Vector2(0f, 1f)));
            lgg = prof.Add<LiftGammaGain>(false);
        }

        void LateUpdate()
        {
            if (!vol) Build();
            bool on = On;
            vol.weight = on ? 1f : 0f;
            var run = RunDirector.Instance;
            string room = run && run.Room != null ? run.Room.id : null;
            if (room != roomSeen)
            {
                roomSeen = room;
#if UNITY_EDITOR
                ReadTune();
#endif
                grade = For(room);
            }
            lgg.lift.Override(new Vector4(1f, 1f, 1f, grade.lift));
            lgg.gain.Override(new Vector4(1f, 1f, 1f, grade.gain));

            if (on && VeilOnF != null)
                foreach (var l in FindObjectsByType<LookFX>(FindObjectsSortMode.None)) VeilOnF.SetValue(l, false);

            // fog: once per stage scene load, a moment after it arrived (the stage and StageMood set theirs first)
            int scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;
            if (scene != sceneSeen) { sceneSeen = scene; sceneAt = Time.unscaledTime + 0.3f; }
            if (on && scene != sceneFogDone && Time.unscaledTime >= sceneAt && run && !run.Transitioning)
            {
                sceneFogDone = scene;
                RenderSettings.fogDensity *= FogScale;
            }

            // camera: the default distance only, once per rig (captures and the wheel change it afterwards)
            var rig = run ? run.Rig : null;
            if (rig && rig != rigSeen)
            {
                rigSeen = rig;
                if (CamOn && Mathf.Approximately(rig.Zoom, CameraRig.FixedZoom)) rig.Zoom = CloseZoom;
            }
        }

        static Grade For(string room)
        {
            var g = room != null && Rooms.TryGetValue(room, out var r) ? r : Default;
#if UNITY_EDITOR
            if (fileGrades != null)
            {
                if (fileGrades.TryGetValue("*", out var a)) g = a;
                if (room != null && fileGrades.TryGetValue(room, out var f)) g = f;
            }
#endif
            return g;
        }

#if UNITY_EDITOR
        static Dictionary<string, Grade> fileGrades;
        static void ReadTune()
        {
            fileGrades = null;
            string path = System.IO.Path.Combine(Application.dataPath, "..", "ArtSource", "LightRadius", "crisp.txt");
            if (!System.IO.File.Exists(path)) return;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            var d = new Dictionary<string, Grade>();
            foreach (var raw in System.IO.File.ReadAllLines(path))
            {
                var p = raw.Trim().Split(new[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);
                if (p.Length < 3 || p[0].StartsWith("#")) continue;
                if (float.TryParse(p[1], System.Globalization.NumberStyles.Float, ci, out float l) && float.TryParse(p[2], System.Globalization.NumberStyles.Float, ci, out float g)) d[p[0]] = new Grade(l, g);
            }
            fileGrades = d;
        }
#endif
    }
}
