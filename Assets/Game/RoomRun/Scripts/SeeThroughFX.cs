// Soft see-through near the player (11-13, Kido: "the big hard circle cut is unnatural; make it soft like other top-down
// games"). Feeds the globals of Shaders/RRSeeThrough.hlsl every frame in every RoomRun stage:
//   _PlayerScreenPos[3]  each player's chest on screen (viewport xy, eye depth z, w = 1)
//   _RR_SeeShape         inner/outer radius as a fraction of screen height, min visibility, depth margin
//   _RR_SeeShape2        player feet height, height ramp start/full above the feet, temporal dither frame
// Radii are set in metres and converted with the camera distance, so the soft ring keeps its size around the player at
// any zoom. Read by LookStudy foliage/props/stage rocks+houses, reeds, bamboo and the SnowPlague SP_SEETHROUGH path
// (snow and 폐촌 trees), all as a dithered clip in the opaque queue.
// Dev tools panel (LookStudyDevTools, "가림 투명"): PlayerPrefs RR.SeeThrough 1 = soft (default), 0 = the previous behaviour
// (snow/폐촌 hard hole, bamboo's own hole, nothing in the other fields) for before/after; RR.SeeThroughTemporal 1 = the
// dither pattern moves every frame.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace RoomRun
{
    [DefaultExecutionOrder(1000)]   // after SPField / AVSeeThrough / BambooField, which write the same player globals
    public sealed class SeeThroughFX : MonoBehaviour
    {
        public const string ModeKey = "RR.SeeThrough", TemporalKey = "RR.SeeThroughTemporal";
        public static int Mode => PlayerPrefs.GetInt(ModeKey, 1);

        // 11-13 r2 (Kido: "centre clear as before, but the outer part should fade more softly, range a bit wider"):
        // inner 1.3 -> 0.7, outer 4.2 -> 5.8, plus a curve exponent on the falloff so the tail toward the outer edge is long
        // and leaves no ring. All four are live-tunable from the dev tools "가림 투명" sliders (PlayerPrefs below).
        public const string InnerKey = "RR.See.Inner", OuterKey = "RR.See.Outer", MinKey = "RR.See.Min", CurveKey = "RR.See.Curve";
        public const float DefInner = 0.7f, DefOuter = 5.8f, DefMin = 0.22f, DefCurve = 1.8f;

        /// <summary>Back to the default v2 shape: drops the slider overrides so the Def* values above apply again.</summary>
        public static void ResetV2()
        {
            PlayerPrefs.DeleteKey(InnerKey); PlayerPrefs.DeleteKey(OuterKey);
            PlayerPrefs.DeleteKey(MinKey); PlayerPrefs.DeleteKey(CurveKey);
        }

        // 11-20 wide clear (Kido, 대나무숲: bamboo between camera and player covered most of the screen; bullet heaven = the
        // play area must stay readable). RR.SeeWide 1 (default) = every tall occluder covering the ground inside an ellipse
        // around the player is removed completely (RRSeeThrough.hlsl RRSeeWideCut, dithered band at the rim); 0 = the soft
        // v2 thinning above. Reeds opt out (RR_SEE_NO_WIDE). Tall MeshRenderers whose shader has no see-through (plain URP
        // Lit props, buildings) are switched to shadows-only while they cover that ellipse (WideProps below).
        public const string WideKey = "RR.SeeWide", WideInnerKey = "RR.SeeWide.Inner", WideOuterKey = "RR.SeeWide.Outer",
            WideStretchKey = "RR.SeeWide.Stretch", WidePropsKey = "RR.SeeWide.Props";
        public const float DefWideInner = 9f, DefWideOuter = 13f, DefWideStretch = 1.35f;
        public static bool Wide => PlayerPrefs.GetInt(WideKey, 1) != 0;
        public float wideInner = DefWideInner, wideOuter = DefWideOuter, wideStretch = DefWideStretch;
        bool wideOn = true, wideProps = true;

        [Tooltip("Inside this radius (m around the player) the occluder is at its thinnest.")]
        public float innerRadius = DefInner;
        [Tooltip("Beyond this radius (m) the occluder is untouched; between inner and outer it fades smoothly.")]
        public float outerRadius = DefOuter;
        [Tooltip("How much of the occluder stays at the centre (0 = hollow hole, 1 = no see-through).")]
        [Range(0f, 1f)] public float minVisibility = DefMin;
        [Tooltip("Exponent on the falloff: 1 = plain smoothstep, higher = longer, gentler tail toward the outer radius.")]
        public float curve = DefCurve;
        float nextPrefs;
        [Tooltip("Only what is at least this much closer to the camera than the player's chest fades (m).")]
        public float depthMargin = 0.8f;
        [Tooltip("Fade starts this high above the player's feet and is full at heightFull (m): grass and low rocks stay.")]
        public float heightStart = 0.45f, heightFull = 1.4f;
        public float chestHeight = 1.0f;

        static readonly int ID_Players = Shader.PropertyToID("_PlayerScreenPos"),
            ID_Shape = Shader.PropertyToID("_RR_SeeShape"), ID_Shape2 = Shader.PropertyToID("_RR_SeeShape2"),
            ID_Shape3 = Shader.PropertyToID("_RR_SeeShape3"), ID_Wide = Shader.PropertyToID("_RR_SeeWide"),
            ID_WideRight = Shader.PropertyToID("_RR_SeeWideRight"), ID_PlayersWS = Shader.PropertyToID("_RR_SeePlayerWS");
        readonly Vector4[] players = new Vector4[3], playersWS = new Vector4[3];
        readonly WideProps props = new WideProps();
        PlayerController[] found = new PlayerController[0];
        float nextScan;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var go = new GameObject("SeeThroughFX");
            DontDestroyOnLoad(go);
            go.AddComponent<SeeThroughFX>();
        }

        void LateUpdate()
        {
            if (Time.unscaledTime >= nextPrefs)
            {
                nextPrefs = Time.unscaledTime + 0.1f;
                innerRadius = PlayerPrefs.GetFloat(InnerKey, DefInner);
                outerRadius = PlayerPrefs.GetFloat(OuterKey, DefOuter);
                minVisibility = PlayerPrefs.GetFloat(MinKey, DefMin);
                curve = PlayerPrefs.GetFloat(CurveKey, DefCurve);
                wideOn = Wide;
                wideProps = PlayerPrefs.GetInt(WidePropsKey, 1) != 0;
                wideInner = PlayerPrefs.GetFloat(WideInnerKey, DefWideInner);
                wideOuter = PlayerPrefs.GetFloat(WideOuterKey, DefWideOuter);
                wideStretch = PlayerPrefs.GetFloat(WideStretchKey, DefWideStretch);
            }
            if (Time.unscaledTime >= nextScan) { nextScan = Time.unscaledTime + 0.5f; found = FindObjectsByType<PlayerController>(FindObjectsSortMode.None); }
            var cam = Camera.main;
            int mode = Mode;
            for (int k = 0; k < 3; k++) { players[k] = Vector4.zero; playersWS[k] = Vector4.zero; }
            float feet = float.MaxValue, nearest = 0f;
            int n = 0;
            if (cam && mode != 0)
                foreach (var pc in found)
                {
                    if (!pc || !pc.isActiveAndEnabled || n >= 3) continue;
                    var p = pc.transform.position;
                    var vp = cam.WorldToViewportPoint(p + Vector3.up * chestHeight);
                    if (vp.z <= 0) continue;
                    playersWS[n] = new Vector4(p.x, p.y, p.z, 1);
                    players[n++] = new Vector4(vp.x, vp.y, vp.z, 1);
                    feet = Mathf.Min(feet, p.y);
                    nearest = nearest <= 0 ? vp.z : Mathf.Min(nearest, vp.z);
                }
            if (n == 0) { Shader.SetGlobalVector(ID_Shape, Vector4.zero); props.Clear(); return; }
            Shader.SetGlobalVectorArray(ID_Players, players);

            bool wide = wideOn && mode != 0;
            float wIn = Mathf.Max(0f, wideInner), wOut = Mathf.Max(wideOuter, wIn + 0.5f), wSt = Mathf.Max(wideStretch, 0.1f);
            var right = cam.transform.right; right.y = 0f;
            right = right.sqrMagnitude > 1e-6f ? right.normalized : Vector3.right;
            Shader.SetGlobalVectorArray(ID_PlayersWS, playersWS);
            Shader.SetGlobalVector(ID_Wide, new Vector4(wIn, wOut, wSt, wide ? 1 : 0));
            Shader.SetGlobalVector(ID_WideRight, new Vector4(right.x, right.z, 0, 0));
            if (wide && wideProps) props.Update(cam, playersWS, n, new Vector2(right.x, right.z), wIn, wOut, wSt, heightStart);
            else props.Clear();

            // metres -> fraction of screen height at the player's distance
            float h = cam.orthographic ? cam.orthographicSize * 2f : 2f * nearest * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            h = Mathf.Max(h, 0.01f);
            // (11-14: a reeds-only override was tried and removed: Kido wants the reeds to half-hide the player on purpose)
            float hs = heightStart, hf = heightFull, mv = minVisibility, inner = innerRadius, outer = outerRadius, dm = depthMargin, cv = curve;
            var rd = RunDirector.Instance;
            bool temporal = PlayerPrefs.GetInt(TemporalKey, 0) != 0;
            Shader.SetGlobalVector(ID_Shape, new Vector4(inner / h, Mathf.Max(outer, inner + 0.1f) / h, mv, dm));
            Shader.SetGlobalVector(ID_Shape3, new Vector4(Mathf.Max(cv, 0.1f), 0, 0, 0));
            Shader.SetGlobalVector(ID_Shape2,new Vector4(feet, hs, Mathf.Max(hf, hs + 0.05f), temporal ? 1 + (Time.frameCount & 63) : 0));
        }

        void OnDisable() { Shader.SetGlobalVector(ID_Shape, Vector4.zero); props.Clear(); }
    }

    /// <summary>
    /// 11-20 wide clear for tall props whose shader cannot dither itself away (plain URP Lit buildings, downloaded trees):
    /// while the camera-ray projection of the renderer's bounds onto the ground reaches into the ellipse around a player,
    /// the renderer draws shadows only (hidden, its shadow stays). Hysteresis keeps it from flickering at the rim.
    /// Skips characters (anything under an Animator or Rigidbody), skinned meshes, transparent/VFX materials, small or very
    /// wide meshes (ground tiles, terrain chunks) and every material that already has the shader-side see-through.
    /// </summary>
    sealed class WideProps
    {
        const float MinHeight = 2.5f, MaxFootprint = 16f;
        struct Item { public MeshRenderer r; public ShadowCastingMode orig; public bool hidden; }
        readonly List<Item> items = new List<Item>();
        readonly HashSet<int> seen = new HashSet<int>();
        readonly Vector3[] corners = new Vector3[8];
        float nextScan;
        int sceneStamp = -1;

        static bool HasOwnSeeThrough(Material m)
        {
            if (!m || !m.shader) return true;          // leave broken/missing materials alone
            if (m.renderQueue > 2500) return true;     // transparent / additive VFX: not ours to hide
            string n = m.shader.name;
            if (n.StartsWith("LookStudy/") || n.StartsWith("BambooArena/") || n.StartsWith("ReedField/") || n == "BossSeal/HazeProp") return true;
            if (n.StartsWith("SnowPlague/") && m.IsKeywordEnabled("SP_SEETHROUGH")) return true;
            return false;
        }

        void Scan()
        {
            foreach (var mr in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (!mr || !seen.Add(mr.GetInstanceID())) continue;
                if (mr.shadowCastingMode == ShadowCastingMode.ShadowsOnly) continue;
                var b = mr.bounds;
                if (b.size.y < MinHeight || Mathf.Max(b.size.x, b.size.z) > MaxFootprint) continue;
                if (mr.GetComponentInParent<Animator>() || mr.GetComponentInParent<Rigidbody>() || mr.GetComponentInParent<PlayerController>()) continue;
                bool any = false;
                foreach (var m in mr.sharedMaterials) if (!HasOwnSeeThrough(m)) { any = true; break; }
                if (!any) continue;
                items.Add(new Item { r = mr, orig = mr.shadowCastingMode });
            }
        }

        public int HiddenCount { get { int c = 0; foreach (var it in items) if (it.hidden) c++; return c; } }
        public int Count => items.Count;

        public void Update(Camera cam, Vector4[] players, int n, Vector2 right, float inner, float outer, float stretch, float heightStart)
        {
            int stamp = SceneManager.sceneCount * 7919 + SceneManager.GetActiveScene().handle;
            if (Time.unscaledTime >= nextScan || stamp != sceneStamp) { nextScan = Time.unscaledTime + 3f; sceneStamp = stamp; Scan(); }
            Vector3 cp = cam.transform.position;
            var fwd = new Vector2(-right.y, right.x);
            float hideAt = (inner + outer) * 0.5f, band = 0.75f;
            for (int i = items.Count - 1; i >= 0; i--)
            {
                var it = items[i];
                if (!it.r) { items.RemoveAt(i); continue; }
                if (!it.r.enabled && !it.hidden) continue;
                var b = it.r.bounds;
                float best = float.MaxValue;
                for (int k = 0; k < n; k++)
                {
                    var p = players[k];
                    if (b.max.y < p.y + heightStart || b.min.y < p.y - 3f) continue;   // low, or rising from a pit/abyss
                    float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
                    int c = 0;
                    for (int ix = 0; ix < 2; ix++) for (int iy = 0; iy < 2; iy++) for (int iz = 0; iz < 2; iz++)
                        corners[c++] = new Vector3(ix == 0 ? b.min.x : b.max.x, iy == 0 ? b.min.y : b.max.y, iz == 0 ? b.min.z : b.max.z);
                    for (int j = 0; j < 8; j++)
                    {
                        var w = corners[j];
                        float denom = cp.y - w.y;
                        float t = denom > 0.05f ? (cp.y - p.y) / denom : 1f;
                        var g = denom > 0.05f ? new Vector2(cp.x + (w.x - cp.x) * t, cp.z + (w.z - cp.z) * t) : new Vector2(w.x, w.z);
                        var d = g - new Vector2(p.x, p.z);
                        float x = Vector2.Dot(d, right) / stretch, y = Vector2.Dot(d, fwd);
                        minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x); minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
                    }
                    float dx = Mathf.Max(0f, Mathf.Max(minX, -maxX)), dy = Mathf.Max(0f, Mathf.Max(minY, -maxY));
                    best = Mathf.Min(best, Mathf.Sqrt(dx * dx + dy * dy));
                }
                bool hide = it.hidden ? best < hideAt + band : best < hideAt - band;
                if (hide != it.hidden)
                {
                    it.hidden = hide;
                    it.r.shadowCastingMode = hide ? ShadowCastingMode.ShadowsOnly : it.orig;
                    items[i] = it;
                }
            }
        }

        public void Clear()
        {
            if (items.Count == 0 && seen.Count == 0) return;
            foreach (var it in items) if (it.r && it.hidden) it.r.shadowCastingMode = it.orig;
            items.Clear(); seen.Clear();
            nextScan = 0f;
        }
    }
}
