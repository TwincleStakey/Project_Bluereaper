// The reed field arena: a square of 억새/갈대 with a water channel, a trodden path and a broken boardwalk, closed in by
// a river on the near (south) side and earthen banks with walls of tall reeds on the other three, and more reeds on
// hills beyond that fade into haze, so the camera never sees an edge or the void.
// Drives the wind (same idea as the LookStudy grass), hooks the LookStudy trample map, and sets the mood
// (golden sunset by default, moonlit night on Insert). Every slider applies live, in the editor and in Play.
// Play keys: PageUp/PageDown reed height, Home/End wind, Insert mood. Values changed with the keys are kept as the
// scene defaults when Play stops (see RFPlaySettings in the editor folder).
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Reeds
{
    // The 112 x 88 layout (11-10, Kido: every arena 2x per side). The old 56 x 44 points doubled, plus clearings for the
    // new space so the field has several places to fight in. The builder writes these into the scene on every rebuild.
    public static class ReedLayout
    {
        public static Vector2[] Channel => new[] { new Vector2(-80, 18), new Vector2(-48, 14), new Vector2(-24, 2), new Vector2(-4, -14), new Vector2(20, -18), new Vector2(44, -6), new Vector2(80, -24) };
        public static Vector2[] Path => new[] { new Vector2(-8, -42), new Vector2(-6, -30), new Vector2(0, -16), new Vector2(0, 0), new Vector2(-12, 16), new Vector2(-28, 24), new Vector2(-22, 36), new Vector2(-16, 42) };
        public static Vector3[] Clearings => new[] { new Vector3(0, 0, 6.5f), new Vector3(-28, 24, 10f), new Vector3(34, 20, 7.5f), new Vector3(32, -30, 6f),
                                                     new Vector3(-40, -24, 7f), new Vector3(10, 30, 6.5f), new Vector3(-46, 32, 5.5f), new Vector3(46, -36, 5f) };
    }

    [ExecuteAlways]
    [RequireComponent(typeof(ReedInstancer))]
    public class ReedField : MonoBehaviour
    {
        public enum Mood { Sunset, Moonlit }

        [Header("Reeds")]
        [Tooltip("Average reed height in metres. The player is 1.7 m.")]
        [Range(0.5f, 6f)] public float height = 2.0f;
        [Range(0f, 0.6f)] public float heightVariation = 0.2f;
        [Tooltip("Clumps per square metre, relative to the default.")]
        [Range(0.2f, 3f)] public float density = 1.8f;
        [Tooltip("Share of 갈대 (brown reed) among the 억새 (silver grass) away from the water.")]
        [Range(0f, 1f)] public float reedShare = 0.2f;
        [Tooltip("Snapped, folded-over stems.")]
        [Range(0f, 0.5f)] public float brokenShare = 0.08f;

        [Header("Wind")]
        [Range(0f, 2f)] public float windStrength = 0.7f;
        [Range(0f, 360f)] public float windAngle = 35f;
        [Range(0.1f, 3f)] public float gustSpeed = 1f;
        [Tooltip("Width of the gust waves rolling across the field (m).")]
        [Range(2f, 40f)] public float gustSize = 14f;
        [Range(0f, 1.5f)] public float gustStrength = 1f;
        [Range(0f, 1f)] public float flutter = 0.5f;

        [Header("Look")]
        public Mood mood = Mood.Sunset;
        [Tooltip("Sunset plume colour (the moonlit mood uses its own silver).")]
        public Color plumeColor = new Color(0.62f, 0.48f, 0.32f);
        public Color plumeTip = new Color(0.88f, 0.74f, 0.54f);
        public Color leafColor = new Color(0.26f, 0.20f, 0.12f);
        [Range(0f, 2f)] public float gustSheen = 0.6f;
        [Range(0f, 1f)] public float groundFog = 0.12f;
        [Range(0.2f, 4f)] public float groundFogHeight = 1.2f;
        [Range(0f, 0.1f)] public float distanceFog = 0.035f;
        [Tooltip("Distance from the camera where the haze starts (m). The arena stays clear inside it.")]
        public float hazeStart = 30f;

        [Header("Arena")]
        [Tooltip("Playable square (x, z). The player cannot leave it; RoomRun uses the same size.")]
        public Vector2 arena = new Vector2(112, 88);   // 11-10: 2x per side (was 56 x 44)
        [Tooltip("Scenery beyond the arena so the camera never sees an edge.")]
        public float outerMargin = 34f;
        public float riverWidth = 9f;
        public int seed = 7;
        public float waterLevel = -0.32f;
        [Tooltip("Water channel through the field, as control points (x, z) of a smooth curve.")]
        public Vector2[] channel = ReedLayout.Channel;
        public float channelWidth = 3.6f;
        [Tooltip("Trodden path / boardwalk through the reeds, as control points (x, z).")]
        public Vector2[] path = ReedLayout.Path;
        [Tooltip("Open spots (x, z, radius) with only low grass: spawn point and room to fight in.")]
        public Vector3[] clearings = ReedLayout.Clearings;
        [Tooltip("Spots (x, z, radius) kept clear for props such as dead trees and 장승.")]
        public Vector3[] keepOut = new Vector3[0];

        [Header("Assets")]
        public Mesh[] susuki;
        public Mesh susukiBroken;
        public Mesh susukiFar;
        [Tooltip("Very cheap card clump for far scenery outside the arena.")]
        public Mesh susukiCard;
        public Mesh[] reeds;
        public Mesh lowGrass;
        [Tooltip("Fallen stalks and broken plumes lying on the ground in clearings and on paths.")]
        public Mesh litter;
        public Material susukiMat, reedMat, grassMat;
        public Material groundMat, waterMat, fogMat, wispMat;
        public Light moon;
        [Header("Baked shadows")]
        [Tooltip("Static props (trees, boardwalk, 장승...) baked by the builder, one per mood.")]
        public Texture2D propShadowSunset, propShadowMoonlit;
        [Range(0f, 1f)] public float reedShadowStrength = 0.55f;
        public const float ShadowCell = 0.5f;
        [Range(0.4f, 1.2f)] public float plumeTone = 0.8f;
        public Volume volume;

        // Enemies or other movers can register here (x, z, 0, radius); the shader parts the reeds around them. Empty for now.
        public static readonly List<Vector4> Stalkers = new List<Vector4>();
        // Set when Play-mode keys changed a value, so the editor can keep it as the scene default.
        public static bool PlayTweaked;

        ReedInstancer inst;
        bool dirty = true;
        float hintUntil; string hint;
        readonly Vector4[] stalkerArr = new Vector4[4];
        List<Vector2> chanPts, pathPts;

        static readonly int ID_Time = Shader.PropertyToID("_RF_Time"), ID_Wind = Shader.PropertyToID("_RF_Wind"),
            ID_Gust = Shader.PropertyToID("_RF_Gust"), ID_Stalk = Shader.PropertyToID("_RF_Stalkers"),
            ID_FogCol = Shader.PropertyToID("_RF_FogCol"), ID_FogP = Shader.PropertyToID("_RF_FogParams");

        // ------------------------------------------------------------------ moods
        struct Preset
        {
            public Color light, ambient, fog, plume, tip, leaf, leafTip, stem, root, sky, wisp;
            public float intensity, pitch, yaw, translucency, saturation, contrast, exposure, temperature, bloom;
            public Color peat, mud, moss, path;
        }

        Preset Current => PresetFor(mood);
        public Vector2 SunFor(Mood m) { var p = PresetFor(m); return new Vector2(p.pitch, p.yaw); }

        Preset PresetFor(Mood m) => m == Mood.Sunset ? new Preset
        {
            light = new Color(1.0f, 0.70f, 0.45f), intensity = 1.9f, pitch = 13f, yaw = 195f,
            ambient = new Color(0.34f, 0.22f, 0.14f), fog = new Color(0.86f, 0.56f, 0.32f),
            plume = plumeColor, tip = plumeTip, leaf = leafColor, leafTip = new Color(0.58f, 0.42f, 0.22f),
            stem = new Color(0.44f, 0.31f, 0.16f), root = new Color(0.07f, 0.045f, 0.03f), sky = new Color(0.62f, 0.36f, 0.18f),
            wisp = new Color(1.6f, 0.9f, 0.45f, 1) * 1.4f, translucency = 1.6f,
            saturation = -16f, contrast = 12f, exposure = 0.0f, temperature = 6f, bloom = 0.7f,
            peat = new Color(0.10f, 0.07f, 0.045f), mud = new Color(0.17f, 0.12f, 0.075f), moss = new Color(0.20f, 0.16f, 0.08f), path = new Color(0.26f, 0.19f, 0.12f),
        } : new Preset
        {
            light = new Color(0.62f, 0.70f, 0.90f), intensity = 1.5f, pitch = 38f, yaw = 150f,
            ambient = new Color(0.12f, 0.14f, 0.20f), fog = new Color(0.13f, 0.15f, 0.20f),
            plume = new Color(0.52f, 0.49f, 0.43f), tip = new Color(0.80f, 0.75f, 0.64f), leaf = new Color(0.20f, 0.22f, 0.17f), leafTip = new Color(0.42f, 0.38f, 0.28f),
            stem = new Color(0.38f, 0.34f, 0.26f), root = new Color(0.035f, 0.04f, 0.035f), sky = new Color(0.10f, 0.12f, 0.17f),
            wisp = new Color(0.5f, 0.95f, 0.6f, 1) * 1.4f,   /* 11-14 QA: white-cyan read as bullets */ translucency = 1.2f,
            saturation = -28f, contrast = 16f, exposure = 0.15f, temperature = 0f, bloom = 0.75f,
            peat = new Color(0.07f, 0.065f, 0.05f), mud = new Color(0.12f, 0.105f, 0.08f), moss = new Color(0.13f, 0.14f, 0.09f), path = new Color(0.17f, 0.15f, 0.12f),
        };

        void OnEnable() { inst = GetComponent<ReedInstancer>(); dirty = true; }
        void OnValidate() { dirty = true; }

        void Start()
        {
            if (!Application.isPlaying) return;
            PlayTweaked = false;
            // Hook the LookStudy trample map so the player flattens the reeds (LookStudyTrampleFeed feeds it the player).
            if (!GetComponent<RFMoonShafts>()) gameObject.AddComponent<RFMoonShafts>();
            // 11-14 (Kido's arena rule, QA: the boardwalk deck and props in the arena could catch the player): nothing
            // inside the arena collides any more; the pieces stay as flat scenery. The ground keeps its collider.
            int off = 0;
            foreach (var root in gameObject.scene.GetRootGameObjects())
            {
                if (root.name != "Landmarks" && root.name != "Boardwalk") continue;
                foreach (var c in root.GetComponentsInChildren<Collider>())
                {
                    var p = c.bounds.center - transform.position;
                    if (InArena(new Vector2(p.x, p.z), -1f)) { c.enabled = false; off++; }
                }
            }
            if (off > 0) Debug.Log($"[ReedField] {off} prop colliders inside the arena turned off");
            // 11-14: the boardwalk's soft contact-shadow quads (ReedField/Blob, multiply) stayed on the path where the
            // deck above them is gone or hidden: those were the dark rounded rectangles on the clearing floor
            // (Kido's first screenshot). A blob only stays where a deck renderer above it is drawn.
            int blobs = 0;
            foreach (var root in gameObject.scene.GetRootGameObjects())
                foreach (var br in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (!br.sharedMaterial || br.sharedMaterial.shader.name != "ReedField/Blob" || !br.transform.parent) continue;
                    bool deck = false;
                    foreach (var r in br.transform.parent.GetComponentsInChildren<MeshRenderer>())
                        if (r != br && r.enabled && r.gameObject.activeInHierarchy && r.sharedMaterial && r.sharedMaterial.shader.name != "ReedField/Blob") { deck = true; break; }
                    if (!deck || InArena(new Vector2(br.transform.position.x - transform.position.x, br.transform.position.z - transform.position.z), -1f)) { br.enabled = false; blobs++; }
                }
            if (blobs > 0) Debug.Log($"[ReedField] {blobs} contact-shadow blobs turned off");
            // 11-14 QA: the 장승 standing inside the arena read from the top camera as dark capsule figures, easy to take
            // for enemies. Hidden there; the ones on the banks outside the edge stay (EdgeFeatures).
            foreach (var root in gameObject.scene.GetRootGameObjects())
            {
                if (root.name != "Landmarks") continue;
                foreach (Transform t in root.transform)
                {
                    // 11-14 (Kido on the coded 폐촌 dead trees: "너가 만든 저 이상한 나무들 빼봐"): hidden everywhere, with the
                    // crows perched on them; the banks get downloaded snags and dead conifers instead (EdgeFeatures)
                    if (t.name.Contains("DeadTree") || t.name.Contains("Crow")) { t.gameObject.SetActive(false); continue; }
                    if (!t.name.Contains("Jangseung")) continue;
                    var lp = t.position - transform.position;
                    if (InArena(new Vector2(lp.x, lp.z), -1f)) t.gameObject.SetActive(false);
                }
            }
            if (!LookStudyTrail.Instance) LookStudyTrail.Ensure(new Vector2(transform.position.x, transform.position.z), Mathf.Max(arena.x, arena.y) + 8f);
        }

        // 11-14: static props in this field (landmarks, 장승, edge trees placed later by RoomRun) cast realtime shadows
        // that the low sun stretched into big soft dark lobes across the clearings (Kido's "dark rectangle"). The field
        // has its own baked shade; only moving things keep realtime shadows. Re-checked every 2 s for late arrivals.
        float nextShadowSweep;
        void SweepPropShadows()
        {
            if (Time.time < nextShadowSweep) return;
            nextShadowSweep = Time.time + 2f;
            foreach (var root in gameObject.scene.GetRootGameObjects())
                foreach (var r in root.GetComponentsInChildren<MeshRenderer>())
                {
                    if (r.shadowCastingMode == ShadowCastingMode.Off) continue;
                    if (r.GetComponentInParent<Animator>() || r.GetComponentInParent<CharacterController>() || r.GetComponentInParent<Rigidbody>()) continue;
                    r.shadowCastingMode = ShadowCastingMode.Off;
                }
        }

        void Update()
        {
            if (Application.isPlaying) { Keys(); SweepPropShadows(); }
            if (dirty) { dirty = false; Generate(); ApplyMood(); BakeReedShadow(); }
            SetGlobals();
        }

        // RoomRun's environment panel re-aims the sun and regrades every room; keep this field's own light and grade.
        void LateUpdate()
        {
            if (!Application.isPlaying) return;
            var p = Current;
            if (moon) { moon.transform.rotation = Quaternion.Euler(p.pitch, p.yaw, 0); moon.color = p.light; moon.intensity = p.intensity; }
            Grade(volume ? volume.profile : null, p);
        }

        // ------------------------------------------------------------------ layout
        static List<Vector2> Spline(Vector2[] pts, float step = 0.5f)
        {
            var o = new List<Vector2>();
            if (pts == null || pts.Length < 2) return o;
            for (int i = 0; i < pts.Length - 1; i++)
            {
                Vector2 p0 = pts[Mathf.Max(i - 1, 0)], p1 = pts[i], p2 = pts[i + 1], p3 = pts[Mathf.Min(i + 2, pts.Length - 1)];
                int n = Mathf.Max(2, Mathf.CeilToInt(Vector2.Distance(p1, p2) / step));
                for (int k = 0; k < n; k++)
                {
                    float t = k / (float)n, t2 = t * t, t3 = t2 * t;
                    o.Add(0.5f * (2 * p1 + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3));
                }
            }
            o.Add(pts[pts.Length - 1]);
            return o;
        }

        static float Dist(List<Vector2> line, Vector2 p, out float along)
        {
            float best = 1e9f; along = 0; float acc = 0;
            for (int i = 0; i < line.Count - 1; i++)
            {
                Vector2 a = line[i], b = line[i + 1], ab = b - a;
                float len = ab.magnitude;
                float t = len > 1e-5f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / (len * len)) : 0;
                float d = (a + ab * t - p).sqrMagnitude;
                if (d < best) { best = d; along = acc + t * len; }
                acc += len;
            }
            return Mathf.Sqrt(best);
        }

        void EnsureLines() { chanPts = Spline(channel); pathPts = Spline(path); }

        public List<Vector2> PathLine { get { EnsureLines(); return pathPts; } }
        public List<Vector2> ChannelLine { get { EnsureLines(); return chanPts; } }
        public Vector2 FullSize => arena + Vector2.one * outerMargin * 2f;

        public float ChannelHalfWidth(float along) => channelWidth * 0.5f * (1f + 0.35f * Mathf.Sin(along * 0.21f + seed) + 0.15f * Mathf.Sin(along * 0.73f));

        public float ChannelDistance(Vector2 p, out float half)
        {
            if (chanPts == null) EnsureLines();
            float d = Dist(chanPts, p, out float along);
            half = ChannelHalfWidth(along);
            return d;
        }

        public float PathDistance(Vector2 p) { if (pathPts == null) EnsureLines(); return Dist(pathPts, p, out _); }

        public float ClearingFactor(Vector2 p)
        {
            float f = 0;
            foreach (var c in clearings)
            {
                float d = Vector2.Distance(p, new Vector2(c.x, c.y));
                f = Mathf.Max(f, 1f - Mathf.Clamp01((d - c.z * 0.75f) / (c.z * 0.4f)));
            }
            return f;
        }

        // How far outside the arena a point is on the bank sides (west, east, north), and on the river side (south).
        public void Outside(Vector2 p, out float bank, out float south)
        {
            float hx = arena.x * 0.5f, hz = arena.y * 0.5f;
            // the river bank wobbles too, so the south edge is not a straight line
            south = Mathf.Max(0f, -hz - p.y + (Mathf.PerlinNoise(p.x * 0.05f + 77, seed) - 0.5f) * 5f + (Mathf.PerlinNoise(p.x * 0.13f, 3 + seed) - 0.5f) * 1.2f);
            float wobble = (Mathf.PerlinNoise(p.x * 0.11f + 3, p.y * 0.11f + seed) - 0.5f) * 2.4f;
            bank = Mathf.Max(0f, Mathf.Max(Mathf.Abs(p.x) - hx, p.y - hz) + wobble);
        }

        // 0..1 inside the river along the south edge (the far bank starts after riverWidth).
        public float RiverFactor(Vector2 p)
        {
            Outside(p, out _, out float south);
            // signed: negative inside the arena (Outside clamps it to 0 there)
            float hz = arena.y * 0.5f;
            south = -hz - p.y + (Mathf.PerlinNoise(p.x * 0.05f + 77, seed) - 0.5f) * 5f + (Mathf.PerlinNoise(p.x * 0.13f, 3 + seed) - 0.5f) * 1.2f;
            float w = riverWidth + (Mathf.PerlinNoise(p.x * 0.07f, seed) - 0.5f) * 3f;
            // The near bank starts ~2.5 m inside the arena: the south edge is shallow water you can wade into, getting
            // deeper right where the arena wall stands, so the boundary reads as the river and not an invisible cut.
            return Mathf.Clamp01(Mathf.Min(south + 2.5f - 0.6f, w - south) / 4f);   // wide, gently sloping river banks
        }

        // Ground height in local space: gentle bumps, the channel and river cut below the water, banks and hills
        // rising outside the arena, clearings slightly raised.
        public float GroundHeight(Vector2 p)
        {
            float h = (Mathf.PerlinNoise(p.x * 0.06f + seed, p.y * 0.06f) - 0.5f) * 0.5f + (Mathf.PerlinNoise(p.x * 0.2f, p.y * 0.2f + seed) - 0.5f) * 0.12f;
            Outside(p, out float bank, out float south);
            float farBank = Mathf.Max(0f, south - riverWidth - 1f);
            float rise = Mathf.Max(bank, farBank);
            float hills = (Mathf.PerlinNoise(p.x * 0.045f + 17, p.y * 0.045f + seed) * 0.7f + 0.3f);
            h += Mathf.SmoothStep(0f, 1f, rise / 5f) * 2.0f + Mathf.Max(rise - 5f, 0f) * 0.16f * hills;
            if (bank < 6f)
            {
                float d = ChannelDistance(p, out float half) + ShoreNoise(p);
                // gentle sandbar slope from the bank down into the water
                float cut = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(half * 0.2f, half + 4.5f, d));
                cut *= 1f - Mathf.Clamp01(bank / 6f);
                h = Mathf.Lerp(h, waterLevel - 0.45f, cut);
            }
            float river = RiverFactor(p);
            // deep river (Kido 11-10): the bed drops away past the wading strip to ~2.5 m under the surface mid-stream
            float riverBed = waterLevel - 0.6f - 1.9f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 1f, river));
            h = Mathf.Lerp(h, riverBed, Mathf.SmoothStep(0f, 1f, river) * (1f - Mathf.Clamp01(bank / 4f)));
            h += ClearingFactor(p) * 0.06f;
            return h;
        }

        // Irregular shoreline: little bays and points instead of a clean curve.
        // Long, smooth sweeping bends with a few sandbar tongues and inlets (low frequency only, no jaggedness).
        public float ShoreNoise(Vector2 p) =>
            (Mathf.PerlinNoise(p.x * 0.07f + 41, p.y * 0.07f + seed) - 0.5f) * 4.0f + (Mathf.PerlinNoise(p.x * 0.16f + 7, p.y * 0.16f) - 0.5f) * 1.2f;

        public bool InArena(Vector2 p, float margin = 0f) => Mathf.Abs(p.x) <= arena.x * 0.5f - margin && Mathf.Abs(p.y) <= arena.y * 0.5f - margin;

        // ------------------------------------------------------------------ generation
        public void Generate()
        {
            if (!inst) inst = GetComponent<ReedInstancer>();
            inst.Clear();
            heightGrid = null;
            EnsureLines();
            if (susuki == null || susuki.Length == 0 || !susukiMat) return;
            var rnd = new System.Random(seed);
            float R() => (float)rnd.NextDouble();
            var origin = transform.position;
            var rot0 = transform.rotation;

            // Inside the arena: full clumps on a jittered grid.
            float spacing = 0.82f / Mathf.Sqrt(Mathf.Max(density, 0.05f));
            float hx = arena.x * 0.5f + 1.5f, hz = arena.y * 0.5f + 1.5f;
            for (float z = -hz; z <= hz; z += spacing)
            for (float x = -hx; x <= hx; x += spacing)
            {
                var p = new Vector2(x + (R() - 0.5f) * spacing * 0.9f, z + (R() - 0.5f) * spacing * 0.9f);
                if (RiverFactor(p) > 0.0f) continue;
                float cd = ChannelDistance(p, out float half);
                float pd = PathDistance(p);
                float clear = ClearingFactor(p);
                bool blocked = false;
                foreach (var k in keepOut) if (Vector2.Distance(p, new Vector2(k.x, k.y)) < k.z) { blocked = true; break; }
                if (blocked) continue;
                float patch = Mathf.PerlinNoise(p.x * 0.09f + 31, p.y * 0.09f + seed);
                // Ragged, natural edges: the shoreline, path and clearing borders wobble with noise.
                float edgeN = (Mathf.PerlinNoise(p.x * 0.45f + 13, p.y * 0.45f + seed) - 0.5f) * 1.6f;
                float shore = cd - half + ShoreNoise(p);       // < 0 means in the water
                float y = GroundHeight(p);
                // reeds keep off the wet sandbar: they start back on the dry bank
                bool inWater = shore < 2.2f || y < waterLevel + 0.25f;
                bool deep = inWater;                            // no reeds stand in the water at all
                float pathClear = Mathf.InverseLerp(0.9f, 2.2f, pd + edgeN);
                clear = Mathf.Clamp01(clear + edgeN * 0.25f);
                bool reedHere = !deep && pathClear > 0.02f && clear < 0.95f && R() <= Mathf.Lerp(0.9f, 1f, patch) * pathClear * (1f - clear);

                // Low grass only where the ground shows: paths, clearings and gaps. Under the reeds it is never seen.
                if (!reedHere)
                {
                    // A few muted tufts only: the open floor stays calm and readable for combat.
                    // 11-14 (Kido on a clearing: "이런대 좀 심심해"): the open floor gets low dry-grass patches (clumped by
                    // noise, not an even sprinkle) and a few small, short islands of reeds. Nothing collides.
                    float tuft = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.42f, 0.72f, Mathf.PerlinNoise(p.x * 0.22f + 71f, p.y * 0.22f + seed)));
                    float isle = Mathf.InverseLerp(0.6f, 0.76f, Mathf.PerlinNoise(p.x * 0.11f + 19f, p.y * 0.11f + seed * 0.7f));
                    if (susuki != null && susuki.Length > 0 && susukiMat && !inWater && pd > 1.6f && isle > 0f && R() < isle * 0.7f)
                    {
                        float ih = height * Mathf.Lerp(0.35f, 0.6f, R()) * Mathf.Lerp(0.7f, 1f, isle);
                        Put(susuki[rnd.Next(susuki.Length)], susukiMat, origin, rot0, new Vector3(p.x, y - 0.02f, p.y), R() * 360f, Mathf.Lerp(0.9f, 1.2f, R()), ih);
                    }
                    else if (lowGrass && grassMat && !inWater && R() < Mathf.Lerp(0.06f, 0.5f, tuft))
                    {
                        float gs = Mathf.Lerp(0.6f, 1.05f, R()) * Mathf.Lerp(1f, 1.6f, tuft);   // 11-14: bigger in the patches, so they read from the camera
                        Put(lowGrass, grassMat, origin, rot0, new Vector3(p.x, y, p.y), R() * 360f, gs * 1.2f, gs);
                    }
                    // 11-13 (Kido: the clearings look bare): fallen stalks and broken plumes lying flat (a few cm high,
                    // no collider), denser toward the reed edge so the clearing floor reads as part of the marsh.
                    else if (litter && susukiMat && !inWater && R() < 0.08f * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.25f, 0.6f, clear))))   // 11-14 QA: only along the reed fringe, open clearings stay clean
                        Put(litter, susukiMat, origin, rot0, new Vector3(p.x, y + 0.01f, p.y), R() * 360f, Mathf.Lerp(0.8f, 1.2f, R()), 1f);

                    continue;
                }

                bool nearWater = shore < 2.5f;
                // Along the waterline the reeds thin out and get shorter, so the bank reads as a soft edge.
                if (shore < 4.5f && R() > Mathf.InverseLerp(2.2f, 4.5f, shore) * 0.8f + 0.2f) continue;
                bool useReed = reeds != null && reeds.Length > 0 && reedMat && (nearWater ? R() < 0.7f : R() < reedShare);
                Mesh mesh; Material mat;
                if (useReed) { mesh = reeds[rnd.Next(reeds.Length)]; mat = reedMat; }
                else if (susukiBroken && R() < brokenShare * (1f + 2f * (1f - pathClear))) { mesh = susukiBroken; mat = susukiMat; }
                else { mesh = susuki[rnd.Next(susuki.Length)]; mat = susukiMat; }

                float hn = Mathf.PerlinNoise(p.x * 0.07f + 5, p.y * 0.07f + 9) * 2f - 1f;
                float h = height * (1f + heightVariation * (hn * 0.7f + (R() * 2f - 1f) * 0.5f));
                h *= Mathf.Lerp(0.8f, 1f, pathClear) * Mathf.Lerp(1f, 0.7f, clear);
                if (shore < 5f) h *= Mathf.Lerp(0.55f, 1f, Mathf.InverseLerp(2.2f, 5f, shore));
                // edges of the reed mass (toward paths and clearings) fall off in height too
                h *= Mathf.Lerp(0.65f, 1f, Mathf.SmoothStep(0, 1, pathClear)) ;
                h = Mathf.Max(h, 0.3f);
                float w = Mathf.Lerp(1.0f, 1.3f, R()) * (0.8f + 0.15f * h);
                Put(mesh, mat, origin, rot0, new Vector3(p.x, y - 0.02f, p.y), R() * 360f, w, h);
            }

            // Outside (Kido 11-10: the edge has to read as "can't go there" like the bamboo arena, the north edge most of
            // all): a continuous wall of very tall, dense reeds on the bank sides, rows getting taller toward the outside
            // and standing on the bank berm (GroundHeight rises outside the arena). Detailed clumps in the front rows, the
            // lighter far clump behind. No flat card clumps: from the RoomRun camera they read as petals lying on the ground.
            var far = susukiFar ? susukiFar : susuki[0];
            var full = FullSize;
            float fx = full.x * 0.5f, fz = full.y * 0.5f;
            const float wallDepth = 11f, wallStep = 0.7f, outerStep = 1.6f;
            for (float z = -fz; z <= fz; z += wallStep)
            for (float x = -fx; x <= fx; x += wallStep)
            {
                var p = new Vector2(x + (R() - 0.5f) * wallStep * 0.8f, z + (R() - 0.5f) * wallStep * 0.8f);
                Outside(p, out float bank, out float south);
                bool farBank = south > riverWidth + 1.5f;
                float o = Mathf.Max(bank, farBank ? south - riverWidth - 1.5f : 0f);
                if (o <= 0.2f || o > wallDepth) continue;
                float y = GroundHeight(p);
                // taller toward the outside: ~3.4 m at the front row, ~5.5 m at the back
                float t = Mathf.Clamp01(o / wallDepth);
                float h = height * Mathf.Lerp(1.7f, 2.75f, Mathf.Sqrt(t)) * Mathf.Lerp(0.9f, 1.1f, R());
                bool front = o < 4f;
                var mesh = front ? susuki[rnd.Next(susuki.Length)] : far;
                float w = (front ? 1.35f : 1.7f) * Mathf.Lerp(0.9f, 1.15f, R());
                Put(mesh, susukiMat, origin, rot0, new Vector3(p.x, y - 0.05f, p.y), R() * 360f, w, h);
            }
            // Beyond the wall: the far clump only, sparse over the hills, thinning into the haze.
            for (float z = -fz; z <= fz; z += outerStep)
            for (float x = -fx; x <= fx; x += outerStep)
            {
                var p = new Vector2(x + (R() - 0.5f) * outerStep * 0.9f, z + (R() - 0.5f) * outerStep * 0.9f);
                Outside(p, out float bank, out float south);
                float o = Mathf.Max(bank, south > riverWidth + 1.5f ? south - riverWidth - 1.5f : 0f);
                if (o <= wallDepth) continue;
                if (R() > Mathf.Lerp(0.8f, 0.35f, Mathf.InverseLerp(wallDepth, outerMargin, o))) continue;
                float y = GroundHeight(p);
                Put(far, susukiMat, origin, rot0, new Vector3(p.x, y - 0.05f, p.y), R() * 360f, 2.0f * Mathf.Lerp(0.9f, 1.2f, R()), height * Mathf.Lerp(2.2f, 2.8f, R()));
            }
            // The far river bank gets reeds at the waterline too.
            for (float x = -fx; x <= fx; x += 1.4f)
            {
                var p = new Vector2(x + (R() - 0.5f) * 0.8f, -arena.y * 0.5f - riverWidth - Mathf.Lerp(0.5f, 2.5f, R()));
                if (!reedMat || reeds == null || reeds.Length == 0) break;
                Put(reeds[rnd.Next(reeds.Length)], reedMat, origin, rot0, new Vector3(p.x, GroundHeight(p) - 0.05f, p.y), R() * 360f, 1.3f, height * Mathf.Lerp(0.9f, 1.15f, R()));
            }
        }

        void Put(Mesh mesh, Material mat, Vector3 origin, Quaternion rot0, Vector3 local, float yaw, float w, float h)
        {
            float realH = h * mesh.bounds.max.y;
            if (realH > 0.8f) Mark(local, 0.32f * w, realH);
            inst.Add(mesh, mat, Matrix4x4.TRS(origin + rot0 * local, rot0 * Quaternion.Euler(0, yaw, 0), new Vector3(w, h, w)));
        }

        // ------------------------------------------------------------------ baked reed shadows
        // Every placed clump writes its height into a coarse grid; from that one top-down shadow texture is made by
        // marching toward the sun (a point is shaded when a reed between it and the sun is taller than the sun line).
        // Rebuilt whenever the field is regenerated (height, density, mood...), so it always matches the reeds.
        float[] heightGrid; int gw, gh;
        Texture2D reedShadowTex;

        public Vector4 ShadowRect { get { var f = FullSize; return new Vector4(transform.position.x - f.x * 0.5f, transform.position.z - f.y * 0.5f, f.x, f.y); } }

        void Mark(Vector3 local, float r, float h)
        {
            if (heightGrid == null)
            {
                var f = FullSize; gw = Mathf.CeilToInt(f.x / ShadowCell); gh = Mathf.CeilToInt(f.y / ShadowCell);
                heightGrid = new float[gw * gh];
            }
            var full = FullSize;
            int cx = Mathf.FloorToInt((local.x + full.x * 0.5f) / ShadowCell), cz = Mathf.FloorToInt((local.z + full.y * 0.5f) / ShadowCell);
            int rr = Mathf.CeilToInt(r / ShadowCell);
            for (int z = cz - rr; z <= cz + rr; z++)
            for (int x = cx - rr; x <= cx + rr; x++)
            {
                if (x < 0 || z < 0 || x >= gw || z >= gh) continue;
                int i = z * gw + x;
                float top = h + local.y;
                if (top > heightGrid[i]) heightGrid[i] = top;
            }
        }

        public void BakeReedShadow()
        {
            if (heightGrid == null) return;
            var p = Current;
            float elev = Mathf.Max(p.pitch, 6f) * Mathf.Deg2Rad;
            float tanE = Mathf.Tan(elev);
            var toSun = -(Quaternion.Euler(p.pitch, p.yaw, 0) * Vector3.forward);
            var d = new Vector2(toSun.x, toSun.z).normalized;
            float maxH = 0; foreach (var v in heightGrid) maxH = Mathf.Max(maxH, v);
            // 11-14: was 14 m. With the low sunset sun the reed walls threw 8-14 m shade lobes across the clearings, which
            // read as dark blotches/rectangles on the open floor from the top-down camera (Kido, QA). Short shade only.
            float maxDist = Mathf.Min(maxH / tanE, 4.5f);
            int steps = Mathf.CeilToInt(maxDist / ShadowCell);
            var full = FullSize;
            var occ = new float[gw * gh];
            for (int z = 0; z < gh; z++)
            for (int x = 0; x < gw; x++)
            {
                var wp = new Vector2(-full.x * 0.5f + (x + 0.5f) * ShadowCell, -full.y * 0.5f + (z + 0.5f) * ShadowCell);
                float g = GroundHeight(wp);
                float o = 0;
                for (int k = 1; k <= steps; k++)
                {
                    float t = k * ShadowCell;
                    int qx = Mathf.FloorToInt((wp.x + d.x * t + full.x * 0.5f) / ShadowCell), qz = Mathf.FloorToInt((wp.y + d.y * t + full.y * 0.5f) / ShadowCell);
                    if (qx < 0 || qz < 0 || qx >= gw || qz >= gh) break;
                    float above = heightGrid[qz * gw + qx] - (g + t * tanE);
                    if (above > 0) o = Mathf.Max(o, Mathf.Clamp01(above / 0.8f));
                    if (o >= 1) break;
                }
                occ[z * gw + x] = o;
            }
            // soften: two box-blur passes
            var tmp = new float[occ.Length];
            for (int pass = 0; pass < 2; pass++)
            {
                for (int z = 0; z < gh; z++) for (int x = 0; x < gw; x++)
                {
                    float sum = 0; int n = 0;
                    for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
                    {
                        int xx = x + dx, zz = z + dz;
                        if (xx < 0 || zz < 0 || xx >= gw || zz >= gh) continue;
                        sum += occ[zz * gw + xx]; n++;
                    }
                    tmp[z * gw + x] = sum / n;
                }
                (occ, tmp) = (tmp, occ);
            }
            if (!reedShadowTex || reedShadowTex.width != gw || reedShadowTex.height != gh)
            {
                if (reedShadowTex) DestroyImmediate(reedShadowTex);
                reedShadowTex = new Texture2D(gw, gh, TextureFormat.R8, false, true) { name = "RF_ReedShadow (runtime)", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
            }
            var px = new byte[gw * gh];
            for (int i = 0; i < px.Length; i++) px[i] = (byte)Mathf.RoundToInt(255f * (1f - occ[i] * reedShadowStrength));
            reedShadowTex.SetPixelData(px, 0); reedShadowTex.Apply(false);
            BakeReedAO();
        }

        // 11-13 (Kido: reeds read flat, no self shadow): mass occlusion for the reed shader (RFMass). Per 0.5 m cell:
        // R = share of the ground within ~1.6 m covered by clumps taller than 0.6 m, G = their mean height / 8 m.
        Texture2D reedAOTex;
        public void BakeReedAO()
        {
            if (heightGrid == null) return;
            var full = FullSize;
            var cov = new float[gw * gh]; var hgt = new float[gw * gh];
            for (int z = 0; z < gh; z++)
            for (int x = 0; x < gw; x++)
            {
                int i = z * gw + x;
                if (heightGrid[i] <= 0) continue;
                var wp = new Vector2(-full.x * 0.5f + (x + 0.5f) * ShadowCell, -full.y * 0.5f + (z + 0.5f) * ShadowCell);
                float h = heightGrid[i] - GroundHeight(wp);
                if (h < 0.6f) continue;
                cov[i] = 1; hgt[i] = h;
            }
            const int r = 3; // cells: ~1.6 m box
            var c2 = new float[cov.Length]; var h2 = new float[cov.Length];
            BoxBlur(cov, c2, gw, gh, r); BoxBlur(hgt, h2, gw, gh, r);
            if (!reedAOTex || reedAOTex.width != gw || reedAOTex.height != gh)
            {
                if (reedAOTex) DestroyImmediate(reedAOTex);
                reedAOTex = new Texture2D(gw, gh, TextureFormat.RGBA32, false, true) { name = "RF_ReedAO (runtime)", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
            }
            var px = new Color32[gw * gh];
            for (int i = 0; i < px.Length; i++)
            {
                float c = c2[i];
                float mean = c > 0.01f ? h2[i] / c : 0f;   // mean height of the covered cells only
                px[i] = new Color32((byte)Mathf.RoundToInt(255f * Mathf.Clamp01(c)), (byte)Mathf.RoundToInt(255f * Mathf.Clamp01(mean / 8f)), 0, 255);
            }
            reedAOTex.SetPixels32(px); reedAOTex.Apply(false);
        }

        // separable box blur (sum / count) with a radius in cells
        static void BoxBlur(float[] src, float[] dst, int w, int h, int r)
        {
            var tmp = new float[src.Length];
            for (int z = 0; z < h; z++)
            {
                float s = 0; int n = 0;
                for (int x = -r; x <= r; x++) if (x >= 0 && x < w) { s += src[z * w + x]; n++; }
                for (int x = 0; x < w; x++)
                {
                    tmp[z * w + x] = s / n;
                    int add = x + r + 1, rem = x - r;
                    if (add < w) { s += src[z * w + add]; n++; }
                    if (rem >= 0) { s -= src[z * w + rem]; n--; }
                }
            }
            for (int x = 0; x < w; x++)
            {
                float s = 0; int n = 0;
                for (int z = -r; z <= r; z++) if (z >= 0 && z < h) { s += tmp[z * w + x]; n++; }
                for (int z = 0; z < h; z++)
                {
                    dst[z * w + x] = s / n;
                    int add = z + r + 1, rem = z - r;
                    if (add < h) { s += tmp[add * w + x]; n++; }
                    if (rem >= 0) { s -= tmp[rem * w + x]; n--; }
                }
            }
        }

        // ------------------------------------------------------------------ meshes the builder bakes
        public Mesh BuildGroundMesh(float cell = 0.5f)
        {
            EnsureLines();
            var full = FullSize;
            float hx = full.x * 0.5f, hz = full.y * 0.5f;
            int nx = Mathf.CeilToInt(hx * 2 / cell) + 1, nz = Mathf.CeilToInt(hz * 2 / cell) + 1;
            var v = new Vector3[nx * nz]; var c = new Color[nx * nz]; var t = new int[(nx - 1) * (nz - 1) * 6];
            for (int j = 0; j < nz; j++)
            for (int i = 0; i < nx; i++)
            {
                var p = new Vector2(-hx + i * cell, -hz + j * cell);
                float cd = ChannelDistance(p, out float half);
                v[j * nx + i] = new Vector3(p.x, GroundHeight(p), p.y);
                Outside(p, out float bank, out _);
                float wet = Mathf.Max((1f - Mathf.Clamp01((cd - half) / 2.2f)) * (1f - Mathf.Clamp01(bank / 6f)), RiverFactor(p) > 0f ? 1f : 0f);
                float pathT = InArena(p) ? Mathf.Max(1f - Mathf.Clamp01((PathDistance(p) - 0.5f) / 1.1f), ClearingFactor(p) * 0.6f) : 0f;
                pathT *= Mathf.PerlinNoise(p.x * 0.8f, p.y * 0.8f) * 0.5f + 0.6f;
                // B: covered by reeds (their shade and litter), everywhere the field grows.
                float under = (1f - Mathf.Clamp01(wet * 1.5f)) * (1f - Mathf.Clamp01(pathT * 1.6f)) * (1f - ClearingFactor(p));
                c[j * nx + i] = new Color(Mathf.Clamp01(wet), Mathf.Clamp01(pathT), Mathf.Clamp01(under), 1);
            }
            int k = 0;
            for (int j = 0; j < nz - 1; j++)
            for (int i = 0; i < nx - 1; i++)
            {
                int a = j * nx + i;
                t[k++] = a; t[k++] = a + nx; t[k++] = a + 1;
                t[k++] = a + 1; t[k++] = a + nx; t[k++] = a + nx + 1;
            }
            var mesh = new Mesh { name = "RF_Ground", indexFormat = IndexFormat.UInt32 };
            mesh.vertices = v; mesh.colors = c; mesh.triangles = t;
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        // The deep river along the south: from just inside the arena edge to past the far bank, one row of quads.
        // 11-14 (Kido: the cut-off water edge showed): the river surface now runs from 7 m inside the arena (it also
        // covers the wading shallows, so no seam between two water shaders) to well past the far bank, where the
        // ground has risen above the water; the River shader fades out near both mesh edges (_RiverZ.zw).
        public float RiverMeshNear => -arena.y * 0.5f + 7f;
        public float RiverMeshFar => -arena.y * 0.5f - riverWidth - 12f;
        public Vector4 RiverParams => new Vector4(RiverZ.x, RiverZ.y, transform.position.z + RiverMeshNear, transform.position.z + RiverMeshFar);
        public Vector2 RiverZ => new Vector2(transform.position.z - arena.y * 0.5f + 1.0f, transform.position.z - arena.y * 0.5f - riverWidth - 2.5f);
        public Mesh BuildRiverMesh()
        {
            var full = FullSize;
            float z0 = RiverMeshNear, z1 = RiverMeshFar;
            var v = new List<Vector3>(); var t = new List<int>();
            int n = Mathf.CeilToInt(full.x / 4f);
            for (int i = 0; i <= n; i++)
            {
                float x = -full.x * 0.5f + full.x * i / n;
                v.Add(new Vector3(x, waterLevel + 0.01f, z0)); v.Add(new Vector3(x, waterLevel + 0.01f, z1));
                if (i > 0) { int s0 = v.Count - 4; t.AddRange(new[] { s0, s0 + 2, s0 + 1, s0 + 1, s0 + 2, s0 + 3 }); }
            }
            for (int i = 0; i < t.Count; i += 3)
            {
                var nn = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]);
                if (nn.y < 0) { (t[i + 1], t[i + 2]) = (t[i + 2], t[i + 1]); }
            }
            var mesh = new Mesh { name = "RF_River" };
            mesh.SetVertices(v); mesh.SetTriangles(t, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        public Mesh BuildWaterMesh()
        {
            EnsureLines();
            var v = new List<Vector3>(); var t = new List<int>();
            float acc = 0;
            for (int i = 0; i < chanPts.Count; i++)
            {
                Vector2 a = chanPts[Mathf.Max(i - 1, 0)], b = chanPts[Mathf.Min(i + 1, chanPts.Count - 1)];
                var d = (b - a).normalized; var n = new Vector2(-d.y, d.x);
                if (i > 0) acc += Vector2.Distance(chanPts[i - 1], chanPts[i]);
                float half = ChannelHalfWidth(acc) + 3.5f;   // wider than the water: the shoreline is where the ground rises through it
                var p = chanPts[i];
                v.Add(new Vector3(p.x + n.x * half, waterLevel, p.y + n.y * half));
                v.Add(new Vector3(p.x - n.x * half, waterLevel, p.y - n.y * half));
                if (i > 0) { int s = v.Count - 4; t.AddRange(new[] { s, s + 2, s + 1, s + 1, s + 2, s + 3 }); }
            }
            // (the south strip that used to sit in front of the river is gone: the river surface covers the shallows now)
            // Make every triangle face up.
            for (int i = 0; i < t.Count; i += 3)
            {
                var n = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]);
                if (n.y < 0) { (t[i + 1], t[i + 2]) = (t[i + 2], t[i + 1]); }
            }
            var mesh = new Mesh { name = "RF_Water" };
            mesh.SetVertices(v); mesh.SetTriangles(t, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        // ------------------------------------------------------------------ wind, fog and mood
        public void SetGlobals()
        {
            float a = windAngle * Mathf.Deg2Rad;
            Shader.SetGlobalFloat(ID_Time, Application.isPlaying ? Time.time : (float)(Time.realtimeSinceStartupAsDouble % 3600.0));
            Shader.SetGlobalVector(ID_Wind, new Vector4(Mathf.Sin(a), Mathf.Cos(a), windStrength, gustSpeed));
            Shader.SetGlobalVector(ID_Gust, new Vector4(gustSize, gustStrength, flutter, gustSheen));
            for (int i = 0; i < 4; i++) stalkerArr[i] = i < Stalkers.Count ? Stalkers[i] : Vector4.zero;
            Shader.SetGlobalVectorArray(ID_Stalk, stalkerArr);
            var fog = Current.fog;
            Shader.SetGlobalVector(ID_FogCol, new Vector4(fog.r, fog.g, fog.b, groundFogHeight));
            // 11-14: fog noise 0.6 -> 0.2. The ground fog thickness varied over ~16 m blobs, which on the open floor read
            // as light/dark blotches around the player (Kido's "dark rectangle", QA "dirt patches")
            Shader.SetGlobalVector(ID_FogP, new Vector4(groundFog, distanceFog, 0.2f, hazeStart));
            Shader.SetGlobalVector("_RF_FogArena", new Vector4(transform.position.x, transform.position.z, arena.x * 0.5f, arena.y * 0.5f));
            var prop = mood == Mood.Sunset ? propShadowSunset : propShadowMoonlit;
            Shader.SetGlobalFloat("_RF_ShadowOn", reedShadowTex ? 1f : 0f);
            if (reedShadowTex) Shader.SetGlobalTexture("_RF_ReedShadow", reedShadowTex);
            Shader.SetGlobalFloat("_RF_AOOn", reedAOTex ? 1f : 0f);
            if (reedAOTex) Shader.SetGlobalTexture("_RF_ReedAO", reedAOTex);
            Shader.SetGlobalTexture("_RF_PropShadow", prop ? prop : Texture2D.whiteTexture);
            Shader.SetGlobalVector("_RF_ShadowRect", ShadowRect);
            Shader.SetGlobalFloat("_RF_WaterLevel", transform.position.y + waterLevel);
        }

        public void ApplyMood()
        {
            var p = Current;
            foreach (var m in new[] { susukiMat, reedMat, grassMat })
            {
                if (!m) continue;
                m.SetColor("_Ambient", p.ambient);
                // 11-13 (Kido: "너무 쨍하지 않게 누르고"): the plumes were the brightest thing on screen; the light now
                // comes from the moon shafts, wisps and lanterns instead
                m.SetColor("_PlumeCol", p.plume * plumeTone);
                m.SetColor("_PlumeTip", p.tip * plumeTone);
                m.SetFloat("_AOStrength", 0.9f); m.SetFloat("_TopLight", 0.5f);
                m.SetColor("_LeafCol", p.leaf);
                m.SetColor("_LeafTip", p.leafTip);
                m.SetColor("_StemCol", p.stem);
                m.SetColor("_RootCol", p.root);
                m.SetFloat("_Translucency", p.translucency);
                m.SetFloat("_GustSheen", gustSheen);
            }
            // Low tufts in the clearings: close to the ground colour, no dark roots, so they do not draw lines on the floor.
            if (grassMat)
            {
                bool sun = mood == Mood.Sunset;
                grassMat.SetColor("_LeafCol", sun ? new Color(0.30f, 0.23f, 0.15f) : new Color(0.16f, 0.17f, 0.13f));
                grassMat.SetColor("_LeafTip", sun ? new Color(0.42f, 0.33f, 0.22f) : new Color(0.26f, 0.25f, 0.2f));
                grassMat.SetColor("_RootCol", sun ? new Color(0.22f, 0.16f, 0.1f) : new Color(0.08f, 0.08f, 0.07f));
                grassMat.SetFloat("_Translucency", 0.2f);
                grassMat.SetFloat("_GustSheen", 0.1f);
            }
            if (reedMat) reedMat.SetColor("_ReedPlumeCol", mood == Mood.Sunset ? new Color(0.55f, 0.36f, 0.22f) : new Color(0.36f, 0.28f, 0.24f));
            if (groundMat)
            {
                groundMat.SetColor("_Ambient", p.ambient);
                groundMat.SetColor("_Peat", p.peat);
                groundMat.SetColor("_Tint", mood == Mood.Sunset ? new Color(1.0f, 0.86f, 0.70f) : new Color(0.55f, 0.6f, 0.7f));
            }
            if (waterMat)
            {
                bool sun = mood == Mood.Sunset;
                waterMat.SetColor("_ShallowCol", sun ? new Color(0.20f, 0.13f, 0.07f) : new Color(0.10f, 0.12f, 0.15f));
                waterMat.SetColor("_DeepCol", sun ? new Color(0.05f, 0.04f, 0.035f) : new Color(0.02f, 0.025f, 0.035f));
                waterMat.SetColor("_FoamCol", sun ? new Color(1f, 0.9f, 0.72f) : new Color(0.6f, 0.65f, 0.75f));
                // almost mirror-calm: faint ripples, soft reflections
                waterMat.SetFloat("_Glint", sun ? 0.35f : 0.25f);
                waterMat.SetFloat("_NormalStrength", 0.12f);
                waterMat.SetFloat("_SurfaceScale", 2.2f);
                waterMat.SetFloat("_RippleStrength", 2.5f);
                waterMat.SetFloat("_Reflection", 0.32f);
                waterMat.SetFloat("_MaxAlpha", 0.72f);
                waterMat.SetFloat("_MurkDepth", 0.7f);
            }
            // 11-14: the fog sheets lying on the clearings were the "dark rectangles / blotches" (light sheet patches with dark gaps
            // and card edges, from the top-down camera): much fainter, they only soften the reed tops now
            if (fogMat) fogMat.SetColor("_Color", new Color(p.fog.r, p.fog.g, p.fog.b, mood == Mood.Sunset ? 0.035f : 0.045f));
            if (wispMat) wispMat.SetColor("_Color", p.wisp);
            foreach (var wt in FindObjectsByType<RFWater>(FindObjectsSortMode.None)) wt.UpdateSky();
            foreach (var w in FindObjectsByType<RFWisp>(FindObjectsSortMode.None))
                if (w.glowLight) w.glowLight.color = mood == Mood.Sunset ? new Color(1f, 0.6f, 0.3f) : new Color(0.45f, 0.85f, 1f);
            if (moon) { moon.color = p.light; moon.intensity = p.intensity; moon.transform.rotation = Quaternion.Euler(p.pitch, p.yaw, 0); }
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = p.ambient * 1.4f;
            RenderSettings.fogColor = p.fog;
            var cam = Camera.main;
            if (cam && cam.clearFlags == CameraClearFlags.SolidColor) cam.backgroundColor = p.fog;
            if (volume) Grade(Application.isPlaying ? volume.profile : volume.sharedProfile, p);
        }

        static void Grade(VolumeProfile prof, Preset p)
        {
            if (!prof) return;
            if (prof.TryGet(out ColorAdjustments ca))
            {
                ca.saturation.Override(p.saturation); ca.contrast.Override(p.contrast); ca.postExposure.Override(p.exposure); ca.colorFilter.Override(Color.white);
            }
            if (prof.TryGet(out WhiteBalance wb)) wb.temperature.Override(p.temperature);
            if (prof.TryGet(out Bloom bl)) bl.intensity.Override(p.bloom);
        }

        // ------------------------------------------------------------------ play-mode test keys
        void Keys()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.pageUpKey.wasPressedThisFrame) { height = Mathf.Min(6f, height + 0.25f); dirty = true; PlayTweaked = true; Hint($"갈대 높이 {height:0.00} m"); }
            if (kb.pageDownKey.wasPressedThisFrame) { height = Mathf.Max(0.5f, height - 0.25f); dirty = true; PlayTweaked = true; Hint($"갈대 높이 {height:0.00} m"); }
            if (kb.homeKey.wasPressedThisFrame) { windStrength = Mathf.Min(2f, windStrength + 0.2f); PlayTweaked = true; Hint($"바람 {windStrength:0.0}"); }
            if (kb.endKey.wasPressedThisFrame) { windStrength = Mathf.Max(0f, windStrength - 0.2f); PlayTweaked = true; Hint($"바람 {windStrength:0.0}"); }
            if (kb.insertKey.wasPressedThisFrame) { mood = mood == Mood.Sunset ? Mood.Moonlit : Mood.Sunset; dirty = true; PlayTweaked = true; Hint(mood == Mood.Sunset ? "석양" : "달밤"); }
        }

        void OnDisable()
        {
            // the bamboo arena's ground cover uses the reed shader too: no reed-field AO outside this field
            Shader.SetGlobalFloat("_RF_AOOn", 0f);
            // Remember what was set with the keys so the editor can keep it after Play (RFPlaySettings).
            if (!Application.isPlaying || !PlayTweaked) return;
            PlayerPrefs.SetString("ReedField.PlayTweak", JsonUtility.ToJson(new Tweak { height = height, windStrength = windStrength, mood = (int)mood }));
            PlayerPrefs.Save();
        }

        [System.Serializable] public struct Tweak { public float height, windStrength; public int mood; }

        void Hint(string s) { hint = s; hintUntil = Time.unscaledTime + 2.5f; }

        GUIStyle style;
        void OnGUI()
        {
            if (!Application.isPlaying || Time.unscaledTime > hintUntil) return;
            if (style == null) style = new GUIStyle(GUI.skin.label) { fontSize = 22, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.95f, 0.9f, 0.8f) } };
            GUI.Label(new Rect(0, Screen.height * 0.12f, Screen.width, 40), hint + "   (PgUp/PgDn 높이 · Home/End 바람 · Insert 분위기 · Play 끝나면 기본값으로 저장)", style);
        }
    }
}
