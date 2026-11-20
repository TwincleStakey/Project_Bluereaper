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
        // v2 thinning above. Foliage shaders only (RR_SEE_WIDE: bamboo, LS foliage, SP vegetation); landmarks/houses/rocks/reeds keep v2. Tall plant MeshRenderers whose shader has no see-through (plain URP
        // Lit trees) are switched to shadows-only while they cover that ellipse (WideProps below).
        public const string WideKey = "RR.SeeWide", WideInnerKey = "RR.SeeWide.Inner", WideOuterKey = "RR.SeeWide.Outer",
            WideStretchKey = "RR.SeeWide.Stretch", WidePropsKey = "RR.SeeWide.Props";
        public const float DefWideInner = 7f, DefWideOuter = 11f, DefWideStretch = 1.2f;   // r5 ("조금더 넓게"): r3 was 5/8, r1 9/13/1.35
        // 11-20 (Kido: "위에 나무만 가려야지 아래 바닥 풀까지 다 사라지게 하면 어떻게해"): the wide clear only takes what is
        // higher than this above the player's feet (tree trunks/canopies, bamboo above head height); grass, flowers, shrubs
        // and stumps below stay (soft v2 still thins what is right in front of the player).
        public const string WideHStartKey = "RR.SeeWide.HStart", WideHFullKey = "RR.SeeWide.HFull";
        public const float DefWideHStart = 2.2f, DefWideHFull = 3.0f;
        public float wideHStart = DefWideHStart, wideHFull = DefWideHFull;
        // 11-20 r4 (Kido: "다른 게임들처럼 위에 환경이 좀 투명하게", not erased): part of the canopy stays
        public const string WideKeepKey = "RR.SeeWide.Keep";
        public const float DefWideKeep = 0.45f;
        public float wideKeep = DefWideKeep;
        // 11-20 r5 (Kido: "딱 빛범위만 아무것도 안가리게 하고 나머지는 지금처럼 투명"): inside the player's light circle
        // (PlayerLightRadius pool, ~4.5 m on the ground) every occluder above the knees is gone completely
        public const string WideClearKey = "RR.SeeWide.Clear", WideClearFadeKey = "RR.SeeWide.ClearFade";
        public const float DefWideClear = 4.5f, DefWideClearFade = 6f;
        public float wideClear = DefWideClear, wideClearFade = DefWideClearFade;
        // r6 (Kido, 계곡: grass round the sunk player vanished): the light-circle clear only takes what is higher than this
        // above the feet (+0.8 m fade), i.e. tall plants, never grass or shrubs
        public const string WideClearHKey = "RR.SeeWide.ClearH";
        public const float DefWideClearH = 1.6f;
        public float wideClearH = DefWideClearH;
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
            ID_WideRight = Shader.PropertyToID("_RR_SeeWideRight"), ID_PlayersWS = Shader.PropertyToID("_RR_SeePlayerWS"), ID_Wide2 = Shader.PropertyToID("_RR_SeeWide2");
        readonly Vector4[] players = new Vector4[3], playersWS = new Vector4[3];
        readonly WideProps props = new WideProps();
        readonly SeeLitSwap litSwap = new SeeLitSwap();
        PlayerController[] found = new PlayerController[0];
        float nextScan;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var go = new GameObject("SeeThroughFX");
            DontDestroyOnLoad(go);
            go.AddComponent<SeeThroughFX>();
        }

        // remote capture helpers (rr_queue "call RoomRun.SeeThroughFX.WideOff" / WideOn / ProbeNext)
        public static void WideOff() { PlayerPrefs.SetInt(WideKey, 0); PlayerPrefs.Save(); }
        public static void WideOn() { PlayerPrefs.SetInt(WideKey, 1); PlayerPrefs.Save(); }
        /// <summary>Next play session: 4 s after the first player appears, logs every big renderer in view (name, shader, bounds).</summary>
        public static void ProbeNext() { PlayerPrefs.SetInt("RR.SeeWide.Probe", 1); PlayerPrefs.Save(); }
        float probeAt = -1f;

        void Probe(Camera cam)
        {
            PlayerPrefs.SetInt("RR.SeeWide.Probe", 0);
            var sb = new System.Text.StringBuilder("[SeeWide probe] props managed " + props.Count + " hidden " + props.HiddenCount + "\n");
            var planes = GeometryUtility.CalculateFrustumPlanes(cam);
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (!r || !r.enabled || !r.isVisible) continue;
                var b = r.bounds;
                if (Mathf.Max(b.size.x, b.size.y, b.size.z) < 5f || !GeometryUtility.TestPlanesAABB(planes, b)) continue;
                var m = r.sharedMaterial;
                sb.Append($"  {r.name} ({r.GetType().Name}) shader {(m && m.shader ? m.shader.name : "-")} mat {(m ? m.name : "-")} shadow {r.shadowCastingMode} center {b.center} size {b.size} path {Path(r.transform)}\n");
            }
            Debug.Log(sb.ToString());
        }

        static string Path(Transform t) { var s = t.name; for (int i = 0; i < 4 && t.parent; i++) { t = t.parent; s = t.name + "/" + s; } return s; }

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
                wideProps = PlayerPrefs.GetInt(WidePropsKey, 0) != 0;   // r4: off by default (it can only remove, not thin)
                wideInner = PlayerPrefs.GetFloat(WideInnerKey, DefWideInner);
                wideOuter = PlayerPrefs.GetFloat(WideOuterKey, DefWideOuter);
                wideStretch = PlayerPrefs.GetFloat(WideStretchKey, DefWideStretch);
                wideHStart = PlayerPrefs.GetFloat(WideHStartKey, DefWideHStart);
                wideHFull = PlayerPrefs.GetFloat(WideHFullKey, DefWideHFull);
                wideKeep = PlayerPrefs.GetFloat(WideKeepKey, DefWideKeep);
                wideClear = PlayerPrefs.GetFloat(WideClearKey, DefWideClear);
                wideClearFade = PlayerPrefs.GetFloat(WideClearFadeKey, DefWideClearFade);
                wideClearH = PlayerPrefs.GetFloat(WideClearHKey, DefWideClearH);
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
            Shader.SetGlobalVector(ID_Wide2, new Vector4(Mathf.Clamp01(wideKeep), Mathf.Max(0f, wideClear), Mathf.Max(wideClearFade, wideClear + 0.3f), wideClearH));
            Shader.SetGlobalVector(ID_WideRight, new Vector4(right.x, right.z, wideHStart, Mathf.Max(wideHFull, wideHStart + 0.1f)));
            if (wide && PlayerPrefs.GetInt(SeeLitSwap.Key, 1) != 0) litSwap.Update(); else litSwap.Restore();
            if (wide && wideProps) props.Update(cam, playersWS, n, new Vector2(right.x, right.z), wIn, wOut, wSt, wideHFull);
            else props.Clear();
            if (probeAt < 0f && PlayerPrefs.GetInt("RR.SeeWide.Probe", 0) != 0) probeAt = Time.unscaledTime + 4f;
            if (probeAt > 0f && Time.unscaledTime >= probeAt) { probeAt = 0f; Probe(cam); }

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

        void OnDisable() { Shader.SetGlobalVector(ID_Shape, Vector4.zero); props.Clear(); litSwap.Restore(); }
    }

    /// <summary>
    /// 11-20 wide clear for tall PLANTS whose shader cannot dither itself away (downloaded trees in plain URP Lit):
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

        internal static bool HasOwnSeeThrough(Material m)
        {
            if (!m || !m.shader) return true;          // leave broken/missing materials alone
            if (m.renderQueue > 2500) return true;     // transparent / additive VFX: not ours to hide
            string n = m.shader.name;
            if (n.StartsWith("LookStudy/") || n.StartsWith("BambooArena/") || n.StartsWith("ReedField/") || n == "BossSeal/HazeProp") return true;
            if (n.StartsWith("SnowPlague/") && m.IsKeywordEnabled("SP_SEETHROUGH")) return true;
            return false;
        }

        // 11-20 Kido: foliage only. The first version also hid the 서낭당 stone tower and the 문인석 in 대나무숲 (plain URP
        // Lit, tall): landmarks, statues, houses and rocks must never vanish. A renderer qualifies only when its own name,
        // a parent's name or a material name reads as a plant.
        static readonly string[] FoliageWords = { "tree", "leaf", "leaves", "foliage", "bamboo", "canopy", "branch", "pine", "oak",
            "birch", "maple", "willow", "bush", "shrub", "fern", "grass", "reed", "plant", "trunk", "nanmu", "namu", "veg" };

        static bool Has(string n)
        {
            if (string.IsNullOrEmpty(n)) return false;
            n = n.ToLowerInvariant();
            foreach (var w in FoliageWords) if (n.Contains(w)) return true;
            return false;
        }

        internal static bool IsFoliage(MeshRenderer mr)
        {
            var t = mr.transform;
            for (int i = 0; i < 3 && t; i++, t = t.parent) if (Has(t.name)) return true;
            foreach (var m in mr.sharedMaterials) if (m && (Has(m.name) || (m.mainTexture && Has(m.mainTexture.name)))) return true;
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
                if (!any || !IsFoliage(mr)) continue;
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

    /// <summary>
    /// 11-20 r6 (Kido: a bare branchy tree in the field covered the player and did not fade): tall plant renderers drawn
    /// with plain URP Lit (downloaded trees such as VH_BareTreeA/B) get a RoomRun/SeeLit clone of their material (same base
    /// map/colour, normal, cutoff, smoothness) so they thin like bamboo and LS foliage. Originals come back when the wide
    /// see-through is switched off. PlayerPrefs RR.SeeWide.Lit (1 default).
    /// </summary>
    sealed class SeeLitSwap
    {
        public const string Key = "RR.SeeWide.Lit";
        const float MinHeight = 2.5f, MaxFootprint = 40f;
        static Shader seeLit;
        static readonly int ID_BaseMap = Shader.PropertyToID("_BaseMap"), ID_BaseColor = Shader.PropertyToID("_BaseColor"),
            ID_BumpMap = Shader.PropertyToID("_BumpMap"), ID_BumpScale = Shader.PropertyToID("_BumpScale"), ID_Cutoff = Shader.PropertyToID("_Cutoff"),
            ID_AlphaClip = Shader.PropertyToID("_AlphaClip"), ID_Smooth = Shader.PropertyToID("_Smoothness"), ID_Metal = Shader.PropertyToID("_Metallic"),
            ID_Cull = Shader.PropertyToID("_Cull");
        readonly Dictionary<Material, Material> clones = new Dictionary<Material, Material>();
        readonly List<KeyValuePair<MeshRenderer, Material[]>> swapped = new List<KeyValuePair<MeshRenderer, Material[]>>();
        readonly HashSet<int> seen = new HashSet<int>();
        float nextScan;
        int sceneStamp = -1;

        static bool IsPlainLit(Material m) => m && m.shader && (m.shader.name == "Universal Render Pipeline/Lit" || m.shader.name == "Universal Render Pipeline/Simple Lit");

        Material Clone(Material src)
        {
            if (clones.TryGetValue(src, out var c)) return c;
            c = new Material(seeLit) { name = src.name + " (SeeLit)", hideFlags = HideFlags.DontSave, renderQueue = 2450, enableInstancing = true };
            if (src.HasProperty(ID_BaseMap)) { c.SetTexture(ID_BaseMap, src.GetTexture(ID_BaseMap)); c.SetTextureScale(ID_BaseMap, src.GetTextureScale(ID_BaseMap)); c.SetTextureOffset(ID_BaseMap, src.GetTextureOffset(ID_BaseMap)); }
            if (src.HasProperty(ID_BaseColor)) c.SetColor(ID_BaseColor, src.GetColor(ID_BaseColor));
            if (src.HasProperty(ID_BumpMap) && src.GetTexture(ID_BumpMap)) { c.SetTexture(ID_BumpMap, src.GetTexture(ID_BumpMap)); if (src.HasProperty(ID_BumpScale)) c.SetFloat(ID_BumpScale, src.GetFloat(ID_BumpScale)); }
            if (src.HasProperty(ID_Smooth)) c.SetFloat(ID_Smooth, src.GetFloat(ID_Smooth));
            if (src.HasProperty(ID_Metal)) c.SetFloat(ID_Metal, src.GetFloat(ID_Metal));
            if (src.HasProperty(ID_Cutoff)) c.SetFloat(ID_Cutoff, src.GetFloat(ID_Cutoff));
            bool clipOn = src.IsKeywordEnabled("_ALPHATEST_ON") || (src.HasProperty(ID_AlphaClip) && src.GetFloat(ID_AlphaClip) > 0.5f);
            c.SetFloat(ID_AlphaClip, clipOn ? 1f : 0f);
            if (src.HasProperty(ID_Cull)) c.SetFloat(ID_Cull, src.GetFloat(ID_Cull));
            clones[src] = c;
            return c;
        }

        public void Update()
        {
            int stamp = SceneManager.sceneCount * 7919 + SceneManager.GetActiveScene().handle;
            if (Time.unscaledTime < nextScan && stamp == sceneStamp) return;
            nextScan = Time.unscaledTime + 3f; sceneStamp = stamp;
            if (!seeLit) seeLit = Shader.Find("RoomRun/SeeLit");
            if (!seeLit) return;
            for (int i = swapped.Count - 1; i >= 0; i--) if (!swapped[i].Key) swapped.RemoveAt(i);
            // stage meshes may be DontSave objects, which FindObjectsByType skips: walk the loaded scenes' roots too
            var all = new HashSet<MeshRenderer>(Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None));
            for (int si = 0; si < SceneManager.sceneCount; si++)
            {
                var sc = SceneManager.GetSceneAt(si);
                if (!sc.isLoaded) continue;
                foreach (var go in sc.GetRootGameObjects()) foreach (var r in go.GetComponentsInChildren<MeshRenderer>(false)) all.Add(r);
            }
            foreach (var mr in all)
            {
                if (!mr || !seen.Add(mr.GetInstanceID())) continue;
                var b = mr.bounds;
                if (b.size.y < MinHeight || Mathf.Max(b.size.x, b.size.z) > MaxFootprint) continue;
                var ms = mr.sharedMaterials;
                bool any = false;
                foreach (var m in ms) if (IsPlainLit(m)) { any = true; break; }
                if (!any || !WideProps.IsFoliage(mr)) continue;
                if (mr.GetComponentInParent<Animator>() || mr.GetComponentInParent<PlayerController>()) continue;
                var orig = (Material[])ms.Clone();
                for (int k = 0; k < ms.Length; k++) if (IsPlainLit(ms[k])) ms[k] = Clone(ms[k]);
                mr.sharedMaterials = ms;
                swapped.Add(new KeyValuePair<MeshRenderer, Material[]>(mr, orig));
            }
        }

        public void Restore()
        {
            if (swapped.Count == 0 && seen.Count == 0) return;
            foreach (var kv in swapped) if (kv.Key) kv.Key.sharedMaterials = kv.Value;
            swapped.Clear(); seen.Clear(); nextScan = 0f; sceneStamp = -1;
        }
    }
}
