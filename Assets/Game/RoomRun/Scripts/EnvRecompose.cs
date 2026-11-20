// 11-18 re-composition pass (Kido: "대전앞이랑 지옥하고 폐촌하고 다시 구성해봐", rules in
// C:/Users/PC/Downloads/env-rules-refs/환경규칙.md). A runtime layer on top of each field, built after the field's own
// builders ran. It never deletes or rewrites what they made: pieces that break the rules (round dirt stamps, evenly
// spaced edge clusters, lights over the budget) are only switched off, and the new composition goes under one root
// "EnvRecompose_<room>". PlayerPrefs RR.EnvRecompose = 0 turns the whole pass off (old look back on the next room entry);
// RR.EnvRecompose.<room> = 0 turns off one field.
// Everything placed is a downloaded asset already in the project (PalaceStage / EdgeFeatures / Hell / AbandonedVillage
// sets, credits as before). No colliders, small pieces cast no shadow, nothing tall inside the arena.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace RoomRun
{
    public sealed partial class EnvRecompose : MonoBehaviour
    {
        // 11-20 (Kido: "코드로 만든 판 빼는데"): the code-made pieces of this layer are off by default - the flat ground-patch
        // quads (Flat / Band / RimBands / Skirt on ER_Quad), the palace volume fog cards (ER_Quad) and the code-mesh mounds
        // (ER_Mound, 대전 앞 + 저승, with the dressing laid on them). RR.EnvRecompose.codePlanes = 1 brings the old state back.
        // 폐촌's mounds are raised in its terrain heightmap (not a code mesh) and stay.
        public static bool CodePlanes => PlayerPrefs.GetInt("RR.EnvRecompose.codePlanes", 0) != 0;
        // 11-20 decal test (Kido: "데칼 온오프차이 보여줘"): RR.Decals = 1 lays downloaded Megascans decals (URP Decal
        // Projectors, Screen Space feature on PC_Renderer, Resources/Decals) where the code planes were: dirt-path strips
        // for the bands, dirt patches for the single patches and where the mounds stood. Default 1 (on) since 11-20; 0 = off.
        public static bool DecalsOn => PlayerPrefs.GetInt("RR.Decals", 1) != 0;   // 11-20 Kido "조금 밝게, 개수 줄여 켜기": default ON, RR.Decals = 0 off
        static bool inBand;   // a band's own pieces lay no patch decal (the band gets path strips)
        static int decals; static System.Random drng;   // own random stream: decals never shift the other pieces' layout
        static float DR(float a, float b) => a + (float)drng.NextDouble() * (b - a);
        static DecalProjector Decal(string mat, float x, float z, float w, float l, float yaw, float fade)
        {
            var m = Load<Material>("Decals/" + mat);
            if (!m) { Debug.LogWarning("[EnvRecompose] no decal material Decals/" + mat); return null; }
            var go = new GameObject("ER_Decal_" + mat);
            go.transform.SetParent(root, false);
            // projector looks straight down (local +Z), its Y along the yaw; box 1.2 m deep centred on the floor
            go.transform.SetPositionAndRotation(new Vector3(x, GroundAt(x, z), z) + off, Quaternion.Euler(90f, yaw, 0f));
            var d = go.AddComponent<DecalProjector>();
            d.material = m; d.pivot = Vector3.zero; d.size = new Vector3(w, l, 1.2f); d.fadeFactor = fade; d.drawDistance = 1000f;
            count++; decals++;
            return d;
        }
        // a band as a chain of dirt-path strips (the strip covers ~35 % of the texture width; texture is 1 x 2)
        static void PathDecal(float x, float z, float yaw, float length, float width)
        {
            if (drng.NextDouble() < 0.5) return;   // pass 3: half the bands, the wide strips read as shadows
            float w = width * 1.9f, seg = w * 1.6f;
            int n = Mathf.Max(1, Mathf.CeilToInt(length / seg));
            var dir = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
            for (int i = 0; i < n; i++)
            {
                float t = (i + 0.5f) * length / n - length * 0.5f;
                var p = new Vector3(x, 0, z) + dir * t;
                Decal("RRD_DirtPath01", p.x, p.z, w * DR(0.9f, 1.1f), w * 2f, yaw + DR(-8f, 8f) + (drng.NextDouble() < 0.5 ? 180f : 0f), DR(0.55f, 0.7f));
                if (drng.NextDouble() < 0.5) Splash(p.x, p.z, width * 2f);
            }
        }
        // where a removed code-mesh mound stood: dirt patches along its line
        // a dark mud splash beside a dirt decal (Megascans "Mud Splash", thin, dark brown)
        static void Splash(float x, float z, float r)
        {
            if (drng.NextDouble() > 0.55) return;
            float a = DR(0f, 6.283f), d = DR(0.3f, 0.6f) * r, s = DR(2.5f, 4f);
            Decal("RRD_MudSplash", x + Mathf.Cos(a) * d, z + Mathf.Sin(a) * d, s, s, DR(0, 360), DR(0.7f, 0.9f));
        }
        static void MoundSiteDecals(float cx, float cz, float yaw, float length, bool charcoal = false)
        {
            var dir = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
            int n = Mathf.Max(1, Mathf.CeilToInt(length / 8f));   // pass 3: fewer (was / 5)
            for (int i = 0; i < n; i++)
            {
                float t = (i + 0.5f) * length / n - length * 0.5f;
                var p = new Vector3(cx, 0, cz) + dir * t + Quaternion.Euler(0, yaw, 0) * Vector3.right * DR(-1.5f, 1.5f);
                float sz = DR(5f, 7.5f);
                if (charcoal) { Decal("RRD_Charcoal", p.x, p.z, sz * 1.2f, sz * 1.2f, DR(0, 360), 1f); continue; }   // 저승: dark charcoal bits, no tan earth
                Decal("RRD_DirtPatch", p.x, p.z, sz, sz, DR(0, 360), DR(0.55f, 0.7f));
                Splash(p.x, p.z, sz);
            }
        }
        public static bool Enabled(string room) => PlayerPrefs.GetInt("RR.EnvRecompose", 1) != 0 && PlayerPrefs.GetInt("RR.EnvRecompose." + room, 1) != 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var go = new GameObject("EnvRecompose");
            DontDestroyOnLoad(go);
            go.AddComponent<EnvRecompose>();
        }

        string doneFor; float waitSince;

        void LateUpdate()
        {
            var run = RunDirector.Instance;
            if (!run || run.Transitioning || run.Room == null) return;
            var scene = SceneManager.GetActiveScene();
            string id = run.Room.id, key = id + "@" + scene.handle;
            VolumeUpkeep(key);                       // 11-19 palace volume pilot: keep / put back its light state
            if (id == "palace") ShadowLogTick(run);  // editor capture stats (vertical shadows in view)
            if (key == doneFor) return;
            if (id != "palace" && id != "underworld" && id != "abandoned" && id != "grass") { if (GameObject.Find("EdgeFeatures_" + id)) MeasureEdgeTrees(id); else if (Time.frameCount % 30 != 0) return; doneFor = key; return; }
            if (!Enabled(id)) { doneFor = key; return; }
            // wait until the field's own runtime layers exist (they build in their own LateUpdate / Start)
            bool ready = id == "palace" ? GameObject.Find("PalaceStage") : id == "underworld" ? GameObject.Find("UWLook lava sea") : id == "grass" ? GameObject.Find("EdgeFeatures_grass") : true;
            if (waitSince <= 0f) waitSince = Time.unscaledTime;
            if (!ready && Time.unscaledTime - waitSince < 6f) return;
            doneFor = key; waitSince = 0f;
            var holder = new GameObject("EnvRecompose_" + id);
            SceneManager.MoveGameObjectToScene(holder, scene);
            int n = 0;
            try
            {
                Begin(holder.transform, run.Room.center, id);
                if (id != "grass") MeasureEdgeTrees(id);   // 11-19 tree-vs-player measuring (log only)
                if (id == "palace") n = Palace();
                else if (id == "underworld") n = Underworld();
                else if (id == "abandoned") n = Abandoned(scene);
                else if (id == "grass") n = Grass();   // 11-19 풀밭 dressing (EnvRecompose.Grass.cs)
            }
            catch (System.Exception e) { Debug.LogException(e); }
            Debug.Log($"[EnvRecompose] {id}: {n} pieces, hid {hidden}, lights {lightsOn} on / {lightsOff} off, decals {decals}, codePlanes {(CodePlanes ? 1 : 0)}");
        }

        // ------------------------------------------------------------------ shared placing helpers
        static Transform root; static Vector3 off; static float baseY; static System.Random rng;
        static int count, hidden, lightsOn, lightsOff;
        static Mesh quad;
        static readonly Dictionary<string, Object> cache = new Dictionary<string, Object>();

        static void Begin(Transform r, Vector3 centre, string id)
        {
            root = r; off = new Vector3(centre.x, 0f, centre.z); baseY = centre.y;
            rng = new System.Random(1118 + id.Length); count = 0; hidden = 0; lightsOn = 0; lightsOff = 0;
            mounds.Clear(); moundInGround = false;   // no mound left over from the previous field
            decals = 0; drng = new System.Random(1120 + id.Length);
        }
        static T Load<T>(string path) where T : Object
        {
            if (!cache.TryGetValue(path, out var o) || !o) { o = Resources.Load<T>(path); cache[path] = o; }
            return o as T;
        }
        static float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
        static bool Chance(double p) => rng.NextDouble() < p;

        static float GroundAt(float x, float z, float window = 4f)
        {
            var p = new Vector3(x, 0, z) + off;
            var hits = Physics.RaycastAll(p + Vector3.up * 40f, Vector3.down, 80f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            foreach (var h in hits)
                if (Mathf.Abs(h.point.y - baseY) < window && h.point.y < best && !h.collider.GetComponentInParent<PlayerController>()) best = h.point.y;
            return best < float.MaxValue ? best : baseY;
        }

        static Mesh Quad()
        {
            if (quad) return quad;
            quad = new Mesh { name = "ER_Quad" };
            quad.SetVertices(new List<Vector3> { new Vector3(-0.5f, 0, -0.5f), new Vector3(-0.5f, 0, 0.5f), new Vector3(0.5f, 0, 0.5f), new Vector3(0.5f, 0, -0.5f) });
            quad.SetUVs(0, new List<Vector2> { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) });
            quad.SetNormals(new List<Vector3> { Vector3.up, Vector3.up, Vector3.up, Vector3.up });
            quad.SetTangents(new List<Vector4> { new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1) });
            quad.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            quad.RecalculateBounds();
            return quad;
        }

        // a flat downloaded ground-patch quad (PalaceStage/PS_G_*): yaw along the band, sx across, sz along
        static void Flat(string matPath, float x, float z, float sx, float sz, float yaw, float lift)
        {
            if (!CodePlanes)   // 11-20: no code-made ground quads unless RR.EnvRecompose.codePlanes = 1
            {
                if (DecalsOn && !inBand && drng.NextDouble() < 0.7) { float d = Mathf.Max(sx, sz) * 1.3f; Decal("RRD_DirtPatch", x, z, d, d, DR(0, 360), 0.6f); Splash(x, z, d); }
                return;
            }
            var m = Load<Material>(matPath);
            if (!m) { Debug.LogWarning("[EnvRecompose] no material " + matPath); return; }
            var go = new GameObject(m.name);
            go.transform.SetParent(root, false);
            go.transform.SetPositionAndRotation(new Vector3(x, GroundAt(x, z) + lift, z) + off, Quaternion.Euler(0, yaw, 0));
            go.transform.localScale = new Vector3(sx, 1f, sz);
            go.AddComponent<MeshFilter>().sharedMesh = Quad();
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = m;
            r.shadowCastingMode = ShadowCastingMode.Off; r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            count++;
        }

        // a prop from Resources; sink = fraction of its height pushed into the ground (rule 4-2: 10-30 cm / big rocks 15-25%)
        static GameObject Prop(string path, float x, float z, float yaw, float s = 1f, float lay = 0f, float tilt = 0f, float sink = 0.05f, float window = 4f, float atY = float.NaN)
        {
            var prefab = Load<GameObject>(path);
            if (!prefab) { Debug.LogWarning("[EnvRecompose] no prefab " + path); return null; }
            return Place(Object.Instantiate(prefab, root), prefab.name, prefab.transform, x, z, yaw, s, lay, tilt, sink, window, atY);
        }
        static GameObject Place(GameObject go, string name, Transform proto, float x, float z, float yaw, float s, float lay, float tilt, float sink, float window, float atY)
        {
            go.name = name;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(lay, 0f, tilt) * proto.rotation;
            go.transform.localScale = proto.localScale * s;
            float gy = float.IsNaN(atY) ? GroundAt(x, z, window) : atY;
            go.transform.position = new Vector3(x, gy, z) + off;
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length > 0)
            {
                var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                go.transform.position += Vector3.up * (gy - b.min.y - sink * b.size.y);
                bool small = b.size.y < 0.7f;
                foreach (var r in rs) { if (small) r.shadowCastingMode = ShadowCastingMode.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off; }
            }
            foreach (var c in go.GetComponentsInChildren<Collider>()) Destroy(c);
            count++;
            return go;
        }

        static void Hide(GameObject g)
        {
            if (!g || !g.activeSelf) return;
            g.SetActive(false); hidden++;
        }

        // keep at most `budget` point/spot lights on, preferring the ones whose name contains a `keep` word
        static void LightBudget(Scene scene, int budget, string[] keep, string[] drop)
        {
            var all = new List<Light>();
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l && l.enabled && l.type != LightType.Directional && l.gameObject.scene == scene && !l.GetComponentInParent<PlayerController>() && !l.name.StartsWith("Player") && !l.name.StartsWith("Muzzle")) all.Add(l);
            int Score(Light l)
            {
                string n = l.name;
                foreach (var d in drop) if (n.Contains(d)) return -10;
                foreach (var k in keep) if (n.Contains(k)) return 10 + (l.shadows != LightShadows.None ? 1 : 0);
                return (int)Mathf.Clamp(l.intensity * l.range * 0.05f, 0, 9);
            }
            all.Sort((a, b) => Score(b).CompareTo(Score(a)));
            for (int i = 0; i < all.Count; i++)
            {
                if (i < budget && Score(all[i]) > -10) { lightsOn++; continue; }
                all[i].enabled = false; lightsOff++;
                Debug.Log($"[EnvRecompose]   light off: {all[i].name} {all[i].type} i{all[i].intensity:0.0} r{all[i].range:0}");
            }
        }

        // ================================================================== 11-19 low mounds (all three fields)
        // Kido: split only the WIDE open areas. The central combat space stays empty; where open floor runs on for more than
        // ~40 m, LOW mounds (0.4-1.2 m high, 8-20 m long, slopes <= 25 deg) with low prop clusters on them break it into 2-3
        // clearings of different sizes. Walkable: a MeshCollider that Ground.Sample stands the actors on (palace /
        // underworld) or the terrain itself (abandoned). Bullets pass: Combat.Resolve ignores terrain and any surface with
        // |normal.y| > 0.6 (cos 25 = 0.91), and no prop keeps a collider. The edge is not a drawn line: the mesh keeps going
        // 0.2 m under the floor so its rim is where the two surfaces cut each other (ragged by the noisy radius), and a skirt
        // of the field's own ground scans, small stones and grass covers it.
        // RR.EnvRecompose.mounds = 0 (all) / RR.EnvRecompose.<room>.mounds = 0 (one field) turns only this part off.
        public static bool MoundsOn(string room) => (room == "abandoned" || CodePlanes) && PlayerPrefs.GetInt("RR.EnvRecompose.mounds", 1) != 0 && PlayerPrefs.GetInt("RR.EnvRecompose." + room + ".mounds", 1) != 0;

        struct MoundDef { public Vector2 a, b; public float r, h, seed; }
        static readonly List<MoundDef> mounds = new List<MoundDef>();
        static bool moundInGround;   // abandoned: the mounds are raised in the terrain itself, so GroundAt already stands on them
        const float MaxSlope = 0.4663f;   // tan 25

        // a ridge from a to b (room-local xz), total length `length` along a->b, crown height h
        static MoundDef MakeMound(Vector2 centre, float yaw, float length, float h)
        {
            var dir = new Vector2(Mathf.Sin(yaw * Mathf.Deg2Rad), Mathf.Cos(yaw * Mathf.Deg2Rad));
            // lerp(-0.2, h, smoothstep) has a peak slope of 1.5 (h + 0.2) / r; the noisy radius can shrink to 0.78 r
            float r = 1.5f * (h + 0.2f) / (MaxSlope * 0.86f) / 0.78f;
            float seg = Mathf.Max(0.5f, length - 2f * r * 0.85f);
            var m = new MoundDef { a = centre - dir * seg * 0.5f, b = centre + dir * seg * 0.5f, r = r, h = h, seed = R(0f, 100f) };
            // the noisy radius adds slope of its own: sample the field and widen until the steepest step is <= 24 deg
            for (int pass = 0; pass < 6 && AnalyticSlope(m) > 0.445f; pass++) m.r *= 1.08f;
            mounds.Add(m);
            return m;
        }

        static float AnalyticSlope(MoundDef m)
        {
            const float st = 0.25f; float best = 0f;
            var lo = Vector2.Min(m.a, m.b) - Vector2.one * (m.r * 1.3f); var hi = Vector2.Max(m.a, m.b) + Vector2.one * (m.r * 1.3f);
            for (float z = lo.y; z < hi.y; z += st)
                for (float x = lo.x; x < hi.x; x += st)
                {
                    float h0 = MoundH(m, x, z, out _);
                    if (h0 < -0.15f) continue;
                    best = Mathf.Max(best, Mathf.Abs(MoundH(m, x + st, z, out _) - h0) / st, Mathf.Abs(MoundH(m, x, z + st, out _) - h0) / st);
                }
            return best;
        }

        // height over the floor (negative = under it); s01 = 0 at the rim, 1 on the crown line
        static float MoundH(MoundDef m, float x, float z, out float s01)
        {
            var p = new Vector2(x, z); var ab = m.b - m.a; float L2 = Mathf.Max(1e-4f, ab.sqrMagnitude);
            float t = Mathf.Clamp01(Vector2.Dot(p - m.a, ab) / L2);
            float d = (p - (m.a + ab * t)).magnitude;
            float n = Mathf.PerlinNoise(x * 0.13f + m.seed, z * 0.13f - m.seed) - 0.5f;          // ragged outline, 0.78-1.22 r
            float n2 = Mathf.PerlinNoise(x * 0.47f - m.seed, z * 0.47f + m.seed * 1.7f) - 0.5f;  // 11-19 r2: + small bites so the rim is not one clean oval
            float r = m.r * (1f + 0.36f * n + 0.14f * n2);
            float u = Mathf.Clamp01(1f - d / r);
            s01 = u * u * (3f - 2f * u);
            // the crown is not one even line: it sags and swells along the ridge (low frequency, so the slope stays gentle)
            float hv = m.h * (0.82f + 0.18f * Mathf.Sin(t * Mathf.PI)) * (0.9f + 0.2f * Mathf.PerlinNoise(t * 2.3f + m.seed, m.seed * 0.7f));
            return Mathf.Lerp(-0.2f, hv, s01);
        }
        static float MoundTop(float x, float z, out float s01)
        {
            float best = -1f; s01 = 0f;
            foreach (var m in mounds) { float h = MoundH(m, x, z, out var s); if (h > best) { best = h; s01 = s; } }
            return best;
        }

        // the mesh: a 0.5 m grid over the footprint, every vertex at floor + mound height; colours for LookStudy/StageTerrain
        // (A = forest slot = burned_ground earth, G = gravel slot = mud, B = rock slot = rubble on the crown)
        static void BuildMound(MoundDef m, Material mat, bool collider, string kind)
        {
            const float step = 0.5f;
            var lo = Vector2.Min(m.a, m.b) - Vector2.one * (m.r * 1.25f);
            var hi = Vector2.Max(m.a, m.b) + Vector2.one * (m.r * 1.25f);
            int nx = Mathf.CeilToInt((hi.x - lo.x) / step) + 1, nz = Mathf.CeilToInt((hi.y - lo.y) / step) + 1;
            var v = new Vector3[nx * nz]; var c = new Color[nx * nz]; var floor = new float[nx * nz];
            var o = new Vector3(lo.x, 0f, lo.y) + off;
            float maxH = 0f;
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    float x = lo.x + i * step, z = lo.y + j * step;
                    int k = j * nx + i;
                    floor[k] = GroundAt(x, z);
                    float h = MoundH(m, x, z, out float s);
                    maxH = Mathf.Max(maxH, h);
                    v[k] = new Vector3(x - lo.x, floor[k] + h - off.y, z - lo.y);
                    float rub = Mathf.Clamp01((Mathf.PerlinNoise(x * 0.31f + 17f + m.seed, z * 0.31f) - 0.45f) * 3f) * Mathf.Clamp01(s * 1.6f - 0.4f);
                    float mud = Mathf.Clamp01((Mathf.PerlinNoise(x * 0.22f - 5f, z * 0.22f + m.seed) - 0.5f) * 2.5f);
                    c[k] = kind == "palace" ? new Color(0f, mud * 0.6f, rub * 0.75f, 1f - mud * 0.5f) : Color.white;
                }
            // measured slope (both axes and the diagonals of each cell) - the 25 degree rule is checked, not assumed
            float slope = 0f;
            for (int j = 0; j < nz - 1; j++)
                for (int i = 0; i < nx - 1; i++)
                {
                    int k = j * nx + i;
                    if (v[k].y - floor[k] + off.y < -0.15f && v[k + 1].y - floor[k + 1] + off.y < -0.15f) continue;
                    slope = Mathf.Max(slope, Mathf.Abs(v[k + 1].y - v[k].y) / step, Mathf.Abs(v[k + nx].y - v[k].y) / step);
                }
            var tri = new List<int>((nx - 1) * (nz - 1) * 6);
            for (int j = 0; j < nz - 1; j++)
                for (int i = 0; i < nx - 1; i++)
                {
                    int k = j * nx + i;
                    // skip cells wholly under the floor
                    bool under = true;
                    foreach (int q in new[] { k, k + 1, k + nx, k + nx + 1 }) if (v[q].y - floor[q] + off.y > -0.19f) under = false;
                    if (under) continue;
                    tri.Add(k); tri.Add(k + nx); tri.Add(k + nx + 1); tri.Add(k); tri.Add(k + nx + 1); tri.Add(k + 1);
                }
            var mesh = new Mesh { name = "ER_Mound", indexFormat = v.Length > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.vertices = v; mesh.colors = c; mesh.SetTriangles(tri, 0);
            var uv = new Vector2[v.Length]; for (int k = 0; k < v.Length; k++) uv[k] = new Vector2(v[k].x + o.x, v[k].z + o.z) * 0.25f;
            mesh.uv = uv;
            mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
            var go = new GameObject("ER_Mound");
            go.transform.SetParent(root, false);
            go.transform.position = new Vector3(o.x, off.y, o.z);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.On; mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
            count++;
            Debug.Log($"[EnvRecompose]   mound {kind} at ({(m.a.x + m.b.x) * 0.5f:0},{(m.a.y + m.b.y) * 0.5f:0}) len {(m.b - m.a).magnitude + 2f * m.r * 0.85f:0.0} m, crown {maxH:0.00} m, max slope {Mathf.Atan(slope) * Mathf.Rad2Deg:0.0} deg");
        }

        // small pieces of the field that would end up inside a mound are switched off (only where it rises > 0.25 m)
        static void HideBuried(Scene scene)
        {
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (!r || !r.enabled || r.gameObject.scene != scene || r.transform.IsChildOf(root)) continue;
                if (r is ParticleSystemRenderer || r.GetComponentInParent<PlayerController>() || r.GetComponentInParent<Light>()) continue;
                var b = r.bounds; if (b.size.x > 4f || b.size.z > 4f || b.size.y > 3f) continue;
                var p = b.center - off;
                float h = MoundTop(p.x, p.z, out _);
                if (h > 0.25f && b.max.y - off.y < GroundAt(p.x, p.z) - off.y + h + 0.3f) { Hide(r.gameObject); }
            }
        }

        // the skirt and the cluster: `skirt` pieces scattered along the rim band (s 0.05-0.4), `crown` pieces on top
        static void DressMound(MoundDef m, string[] skirt, string[] crown, int nSkirt, int nCrown, float crownScale, GameObject[] clones = null, int nClones = 0)
        {
            var ab = m.b - m.a; float L = ab.magnitude; var dir = L > 1e-3f ? ab / L : Vector2.up; var side = new Vector2(dir.y, -dir.x);
            Vector2 Pick(float sLo, float sHi)
            {
                for (int tries = 0; tries < 30; tries++)
                {
                    var p = m.a + ab * R(-0.15f, 1.15f) + side * R(-1.2f, 1.2f) * m.r;
                    MoundH(m, p.x, p.y, out float s);
                    if (s >= sLo && s <= sHi) return p;
                }
                return (m.a + m.b) * 0.5f;
            }
            float Top(Vector2 p) => GroundAt(p.x, p.y) + (moundInGround ? 0f : Mathf.Max(0f, MoundTop(p.x, p.y, out _)));
            // crown cluster: 2 tight groups (rule 5-2: things in groups, never alone)
            for (int g = 0; g < 2 && crown.Length > 0; g++)
            {
                var c0 = Pick(0.55f, 1f);
                for (int i = 0; i < nCrown / 2; i++)
                {
                    var p = c0 + new Vector2(R(-1f, 1f), R(-1f, 1f)) * (1.2f + i * 0.35f);
                    Prop(crown[rng.Next(crown.Length)], p.x, p.y, R(0, 360), R(0.8f, 1.2f) * crownScale, R(-12f, 12f), R(-12f, 12f), 0.18f, 4f, Top(p));
                }
            }
            for (int i = 0; i < nSkirt && skirt.Length > 0; i++)
            {
                var p = Pick(0.0f, 0.25f);
                Prop(skirt[rng.Next(skirt.Length)], p.x, p.y, R(0, 360), R(0.8f, 1.15f), R(-15f, 15f), R(-15f, 15f), 0.12f, 4f, Top(p));
            }
            for (int i = 0; i < nClones && clones != null && clones.Length > 0; i++)
            {
                var p = Pick(0.02f, 0.75f);
                var t = clones[rng.Next(clones.Length)];
                Place(Object.Instantiate(t, root), t.name, t.transform, p.x, p.y, R(0, 360), R(0.85f, 1.2f), 0f, 0f, 0.05f, 4f, Top(p));
            }
        }

        // band patches (downloaded PS_G scans) along the rim, following the outline on both sides
        static void RimBands(MoundDef m, string kind, int pieces)
        {
            var ab = m.b - m.a; float L = ab.magnitude; var dir = L > 1e-3f ? ab / L : Vector2.up; var side = new Vector2(dir.y, -dir.x);
            float yaw = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;
            for (int sgn = -1; sgn <= 1; sgn += 2)
                for (int i = 0; i < pieces; i++)
                {
                    float t = (i + R(0.2f, 0.8f)) / pieces;
                    var c = m.a + ab * Mathf.Lerp(-0.1f, 1.1f, t);
                    // walk outward to the rim (where the mound goes under the floor)
                    float d = m.r * 0.5f;
                    for (int k = 0; k < 24; k++) { var q = c + side * sgn * d; if (MoundH(m, q.x, q.y, out _) < 0f) break; d += m.r * 0.05f; }
                    var p = c + side * sgn * (d + R(-0.9f, 0.4f));
                    Flat($"PalaceStage/PS_G_{kind}_{rng.Next(3)}", p.x, p.y, R(2.6f, 3.8f), R(3.5f, 5.5f), yaw + R(-25f, 25f), 0.02f + 0.001f * i);
                }
        }

        // clones of small, non-batched scene pieces whose name contains one of `words` (for the field's own grass / stones)
        static GameObject[] SceneTemplates(Scene scene, string[] words, int max = 8)
        {
            var list = new List<GameObject>();
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (list.Count >= max) break;
                if (!r || r.gameObject.scene != scene || r.isPartOfStaticBatch || !r.gameObject.activeInHierarchy || r.transform.IsChildOf(root)) continue;
                bool Match(GameObject g) { foreach (var w in words) if (g.name.Contains(w)) return true; return false; }
                GameObject go = null;
                if (Match(r.gameObject) && r.transform.childCount == 0) go = r.gameObject;
                else if (r.transform.parent && r.transform.parent.childCount <= 6 && Match(r.transform.parent.gameObject)) go = r.transform.parent.gameObject;
                if (!go || list.Contains(go)) continue;
                var b = r.bounds; if (b.size.y > 1.2f || b.size.x > 2.5f) continue;
                list.Add(go);
            }
            return list.ToArray();
        }

        // ================================================================== 대전 앞 (palace)
        // Floor = the approved 9ba45a8a stone scan, untouched. Court x +-52, z -47..47 (walls x +-54, north 45.75).
        // What changes (rule doc sections in brackets):
        //   - PalaceStage's ~50 round dirt stamps off [3-4]; dirt now comes in from the wall feet as long ragged bands with
        //     fingers reaching 3-6 m in, and lines the hall road shoulders [3-3 step 4, 4-4]
        //   - PalaceStage's evenly spaced wall clusters (every 4-6 m) off; instead 3-4 big rubble masses per wall, 8-14 m long
        //     and up to ~2.5 m high, with empty 8-15 m stretches between [5-2, 4-3] -> dense / empty rhythm [6.3]
        //   - asymmetry: one bay of the west wall has fallen in; its rubble, beams and roof tiles fan into the court [2.5, 5-4]
        //   - lights <= 8 [우리]
        static readonly string[] Bits = { "PalaceStage/PS_Tile", "PalaceStage/PS_Tile", "PalaceStage/PS_TileBird", "PalaceStage/PS_Rocks", "PalaceStage/PS_Pebbles", "PalaceStage/PS_Sticks" };

        static int Palace()
        {
            var scene = root.gameObject.scene;
            // 1. switch off the rule-breaking PalaceStage pieces: every PS_G_* stamp and the edge-band clusters
            //    (anything of PalaceStage standing within 7.5 m of the side / north walls, except fires, lights and the
            //    burnt gallery vignette at (45,-21))
            var ps = GameObject.Find("PalaceStage");
            if (ps)
                foreach (Transform t in ps.transform)
                {
                    var p = t.position - off;
                    string n = t.name;
                    if (n.StartsWith("PS_G_")) { Hide(t.gameObject); continue; }
                    if (n.StartsWith("PS_Fire") || n.StartsWith("PP_Brazier") || t.GetComponent<ParticleSystem>() || t.GetComponentInChildren<Light>()) continue;
                    bool burnt = (new Vector2(p.x, p.z) - new Vector2(45f, -21f)).magnitude < 9f;
                    bool supply = (new Vector2(p.x, p.z) - new Vector2(-34f, 30f)).magnitude < 7f;
                    bool nearWall = Mathf.Abs(p.x) > 46.5f || p.z > 38.5f;
                    if (nearWall && !burnt && !supply) Hide(t.gameObject);
                }

            // 2. the fallen west bay: two hanok wall pieces around z -24 come down
            const float BayZ = -24f;
            var ef = GameObject.Find("EdgeFeatures_palace");
            if (ef)
                foreach (Transform t in ef.transform)
                    if (t.name.StartsWith("EF_HanokWall") && t.position.x - off.x < -50f && Mathf.Abs(t.position.z - off.z - BayZ) < 5.5f) Hide(t.gameObject);
            FallenBay(BayZ);

            // 3. wall-foot dirt bands + fingers, road shoulders, vignette underlays
            DirtBands();

            // 4. edge masses with rhythm (west, east, north-west, north-east); spans kept open: side gates z -1..13,
            //    the fallen bay (west), the burnt gallery (east, z -30..-12), supply train (NW x -40..-28)
            Rhythm(-1, new[] { (-31f, -17f), (-2f, 13f) });     // west: fallen bay, side gate
            Rhythm(1, new[] { (-31f, -11f), (-2f, 13f) });      // east: burnt gallery, side gate
            NorthMasses(new[] { (-50f, -42f), (30f, 38f), (42f, 50f) });
            /* SouthDark(): 11-18 r2 capture - darkened pillars read as black slabs from the high camera; the LookStudyRuins south silhouettes stay */

            // 5. lights: the hall fires and the two shadowed lantern spots first, wisps never
            bool vol = VolumeOn();
            if (vol) ReadVolumeTune();
            LightBudget(scene, vol ? Mathf.RoundToInt(V("budget")) : 8, new[] { "Fire", "Shadow", "Spot" }, new[] { "Wisp", "wisp" });

            // 6. 11-19 low mounds splitting the wide court (104 x 94 m of open stone)
            if (MoundsOn("palace")) PalaceMounds(scene);
            else if (DecalsOn) { MoundSiteDecals(-30f, -31f, 12f, 17f); MoundSiteDecals(-33f, -10f, -8f, 11f); MoundSiteDecals(22f, 13f, 62f, 19f); MoundSiteDecals(37f, 26f, 150f, 10f); MoundSiteDecals(24f, -31f, 78f, 13f); }

            // 7. 11-19 volume pilot (RR.EnvRecompose.palace.volume = 1, default off): EnvRecompose.Volume.cs
            if (vol) PalaceVolume(scene);
            // 8. 11-19 court layer (RR.EnvRecompose.palace.court = 1, default off): EnvRecompose.Court.cs
            if (CourtOn()) PalaceCourt(scene);
            lastLoggedAt = new Vector3(9999f, 0f, 0f);   // capture stats: log again once the layer is built
            return count;
        }

        // The court splits into: a big central clearing (spawn, |x| < ~18, kept empty), a west strip (x < -30) behind a
        // broken ridge, a north-east pocket and a smaller south-east pocket. Every ridge has a gap of 6+ m so no clearing is
        // closed, the side-gate roads (z -1..13), the hall road (|x| < 6) and the story vignettes stay off the mounds.
        // Ground: the approved 9ba45a8a floor material itself (R_GroundPalace, LookStudy/StageTerrain) with the vertex
        // colours turned to its earth slots (burned_ground + mud, rubble scan on the crown), so the mounds are the same
        // scans heaped up, not a new texture; the floor mesh / material are not changed.
        static void PalaceMounds(Scene scene)
        {
            Material mat = null;
            // the floor may sit in the stage's own scene or be chunked (MeshChunker): look at every renderer, any scene
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (!r || !r.enabled) continue;
                foreach (var sm in r.sharedMaterials)
                    if (sm && (sm.name.StartsWith("R_GroundPalace") || sm.shader && sm.shader.name == "LookStudy/StageTerrain" && sm.name.Contains("Palace"))) { mat = sm; break; }
                if (mat) { Debug.Log($"[EnvRecompose]   palace floor = {mat.name} on {r.name} ({r.gameObject.scene.name})"); break; }
            }
            if (!mat)
            {
                // what is actually under the centre (a chunked mesh, or a Terrain whose material is materialTemplate)
                foreach (var h in Physics.RaycastAll(off + Vector3.up * (baseY + 30f), Vector3.down, 80f, ~0, QueryTriggerInteraction.Ignore))
                {
                    var tr = h.collider.GetComponent<Terrain>();
                    var rr = h.collider.GetComponent<Renderer>();
                    Material hm = tr ? tr.materialTemplate : rr ? rr.sharedMaterial : null;
                    Debug.Log($"[EnvRecompose]   palace centre hit {h.collider.name} y {h.point.y:0.00} terrain {(bool)tr} mat {(hm ? hm.name + ":" + hm.shader.name : "none")}");
                    if (!mat && hm && !h.collider.GetComponentInParent<PlayerController>()) mat = hm;
                }
            }
            if (mat) Debug.Log($"[EnvRecompose]   palace mound material {mat.name} ({mat.shader.name})");
            if (!mat)
            {
                var names = new HashSet<string>();
                foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None)) if (r && r.bounds.size.x > 30f && r.sharedMaterial) names.Add(r.name + ":" + r.sharedMaterial.name + ":" + r.sharedMaterial.shader.name);
                Debug.LogWarning("[EnvRecompose] palace floor material not found, no mounds. big renderers: " + string.Join(" | ", names));
                return;
            }
            mounds.Clear(); moundInGround = false;
            var defs = new List<MoundDef>
            {
                MakeMound(new Vector2(-30f, -31f), 12f, 17f, 1.1f),    // west ridge, south half
                MakeMound(new Vector2(-33f, -10f), -8f, 11f, 0.7f),    // west ridge, north half (gap ~7 m between; ends short of the gate road)
                MakeMound(new Vector2(22f, 13f), 62f, 19f, 1.2f),      // north-east ridge, diagonal
                MakeMound(new Vector2(37f, 26f), 150f, 10f, 0.6f),     // north-east, short spur behind it
                MakeMound(new Vector2(24f, -31f), 78f, 13f, 0.8f),     // south-east pocket
            };
            HideBuried(scene);
            string[] skirt = { "PalaceStage/PS_Pebbles", "PalaceStage/PS_Rocks", "PalaceStage/PS_Pebbles", "PalaceStage/PS_Tile", "PalaceStage/PS_Sticks" };
            // 11-19 r2: PS_Rubble on the crown caught the light as big pale square slabs -> loose stones, tiles, debris only
            string[] crown = { "PalaceStage/PS_Rocks", "PalaceStage/PS_Tile", "PalaceStage/PS_TileBird", "PalaceStage/PS_Debris", "PalaceStage/PS_Planks", "PalaceStage/PS_Rocks" };
            var grass = SceneTemplates(scene, new[] { "Grass", "grass", "Weed", "weed" });
            Debug.Log($"[EnvRecompose]   palace grass templates: {grass.Length}");
            foreach (var m in defs)
            {
                BuildMound(m, mat, true, "palace");
                RimBands(m, "Mud", Mathf.CeilToInt(((m.b - m.a).magnitude + m.r * 2f) / 2.6f));
                DressMound(m, skirt, crown, 26, 8, 1f, grass, grass.Length > 0 ? 10 : 0);
                // one beam lying over each bigger ridge (the rubble was carted here and dumped)
                if (m.h >= 0.8f) { var c = (m.a + m.b) * 0.5f; Prop("EdgeFeatures/PP_Beam", c.x, c.y, Mathf.Atan2(m.b.x - m.a.x, m.b.y - m.a.y) * Mathf.Rad2Deg + R(-30f, 30f), R(2.2f, 2.6f), 0f, R(-6f, 6f), 0.2f, 4f, GroundAt(c.x, c.y) + Mathf.Max(0f, MoundTop(c.x, c.y, out _))); }
            }
        }

        static void Band(string kind, float x, float z, float yaw, float length, float width, int pieces)
        {
            // a chain of stretched patches along yaw, overlapping 40%, each a little turned and resized, so the outline
            // follows the stone joints instead of reading as one oval
            // 11-18 r2: the leaf scan reads yellow-brown against the blue-grey stone (rule 3-1: one hue family) and as a
            // row of beads; bands are mud / ash only, twice as many pieces, wider, so the beads merge into one ragged strip
            if (!CodePlanes && DecalsOn) PathDecal(x, z, yaw, length, width);   // 11-20 decal test (the loop below still runs: same rng, Flat skips)
            inBand = true;
            if (kind == "Leaves") kind = "Mud";
            if (kind == "Dust") kind = "Ash";
            pieces = Mathf.Max(pieces * 2, Mathf.CeilToInt(length / Mathf.Max(1f, width * 0.6f)));
            var dir = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
            float step = length / Mathf.Max(1, pieces);
            for (int i = 0; i < pieces; i++)
            {
                float t = (i + 0.5f) * step - length * 0.5f;
                var p = new Vector3(x, 0, z) + dir * (t + R(-0.2f, 0.2f) * step) + Quaternion.Euler(0, yaw, 0) * Vector3.right * R(-0.25f, 0.25f) * width;
                float w = width * R(0.9f, 1.25f), l = Mathf.Max(step * R(2.2f, 2.8f), w * 0.8f);
                Flat($"PalaceStage/PS_G_{kind}_{rng.Next(3)}", p.x, p.z, w, l, yaw + R(-12f, 12f), kind == "Leaves" ? 0.03f : 0.02f + 0.001f * i);
            }
            inBand = false;
        }

        static void DirtBands()
        {
            // continuous band at each wall foot (2.5-4 m wide), mud and leaves alternating in runs
            for (int side = -1; side <= 1; side += 2)
                for (float z = -46f; z < 44f; z += R(9f, 13f))
                {
                    if (z > -2f && z < 12f) continue;                                    // side gates keep the road clean
                    float len = R(9f, 14f);
                    Band(Chance(0.5) ? "Mud" : "Leaves", side * R(49.5f, 51f), z + len * 0.5f, 0f, len, R(4.5f, 7f), 4);
                    // fingers reaching 5-10 m into the court, then breaking off
                    if (Chance(0.75)) Band(Chance(0.6) ? "Leaves" : "Mud", side * R(43f, 46f), z + R(2f, len - 2f), side * R(65f, 100f), R(6f, 10f), R(2.4f, 3.6f), 4);
                }
            for (float x = -50f; x < 50f; x += R(9f, 13f))
            {
                if (Mathf.Abs(x) < 27f) { x = 27f; continue; }                           // hall terrace front
                float len = R(9f, 13f);
                Band(Chance(0.5) ? "Leaves" : "Mud", x + len * 0.5f, R(42.5f, 44f), 90f, len, R(4f, 6f), 4);
                if (Chance(0.75)) Band("Leaves", x + R(2f, len - 2f), R(36f, 39f), R(160f, 200f), R(6f, 9f), R(2.4f, 3.4f), 4);
            }
            // south (camera) edge: dirt and leaves blown against the low gallery
            for (float x = -50f; x < 50f; x += R(10f, 15f))
            {
                if (Mathf.Abs(x) < 5f) continue;
                Band(Chance(0.5) ? "Mud" : "Leaves", x, R(-46.5f, -45.5f), 90f, R(8f, 12f), R(2.4f, 3.4f), 4);
            }
            // ash out of the burnt gallery and the yokai scorch, mud under the fallen guards / ambush / supply train: the
            // stories keep their ground, but as stretched trails (drag / spread direction) instead of round stamps
            Band("Ash", 39f, -21f, 100f, 14f, 6f, 5); Band("Ash", 33f, -16f, 70f, 7f, 3f, 3);
            Band("Ash", 25f, 24f, 35f, 10f, 5f, 4); Band("Ash", 20f, 28f, 110f, 6f, 2.5f, 3);
            Band("Mud", -21f, -15f, 60f, 10f, 4.5f, 4); Band("Mud", -14f, -10f, 30f, 6f, 2.2f, 3);
            Band("Mud", 6f, -33f, 150f, 8f, 3.5f, 3); Band("Leaves", -34f, 30f, 20f, 10f, 4.5f, 4);
            // the fallen west bay: dust and ash spread inward
            Band("Ash", -46f, -24f, 90f, 12f, 6f, 5); Band("Dust", -40f, -26f, 75f, 8f, 3f, 3);
        }

        // one rubble mass: 3-6 big pieces overlapping 20-30% into one heap, beams across it, tiles and stones spilling
        // toward the court (inward = +x for the west wall)
        static void Heap(float x, float z, float inward, float along, float length)
        {
            int big = Mathf.Clamp(Mathf.RoundToInt(length / 2.6f), 3, 6);
            var a = Quaternion.Euler(0, along, 0) * Vector3.forward;
            var inn = Quaternion.Euler(0, inward, 0) * Vector3.forward;
            for (int i = 0; i < big; i++)
            {
                float t = (i + 0.5f) / big - 0.5f;
                var p = new Vector3(x, 0, z) + a * (t * length + R(-0.6f, 0.6f)) + inn * R(-0.8f, 0.6f);
                float s = R(1.5f, 2.3f) * (1f - Mathf.Abs(t) * 0.6f);     // higher in the middle of the heap
                Prop("PalaceStage/PS_Rubble", p.x, p.z, R(0, 360), s, 0f, 0f, 0.18f);
            }
            int beams = 1 + rng.Next(3);
            for (int i = 0; i < beams; i++)
            {
                var p = new Vector3(x, 0, z) + a * R(-length * 0.4f, length * 0.4f) + inn * R(0f, 1.5f);
                Prop("EdgeFeatures/PP_Beam", p.x, p.z, along + R(-35f, 35f), R(2.4f, 3f), 0f, R(-10f, 10f), 0.15f);
            }
            if (Chance(0.5))
            {
                var p = new Vector3(x, 0, z) + a * R(-length * 0.3f, length * 0.3f) + inn * R(1.2f, 2.2f);
                Prop(Chance(0.5) ? "PalaceStage/PS_Planks" : "PalaceStage/PS_Debris", p.x, p.z, R(0, 360), R(1f, 1.2f), 0f, 0f, 0.1f);
            }
            // skirt: stones and tiles flowing inward 1-4 m (rule 4-2: 3-8 small pieces at the foot + spill to one side)
            int bits = 8 + rng.Next(6);
            for (int i = 0; i < bits; i++)
            {
                var p = new Vector3(x, 0, z) + a * R(-length * 0.55f, length * 0.55f) + inn * (1.2f + 3f * Mathf.Pow((float)rng.NextDouble(), 1.6f));
                Prop(Bits[rng.Next(Bits.Length)], p.x, p.z, R(0, 360), R(0.85f, 1.2f), R(-20f, 20f), R(-20f, 20f), 0.08f);
            }
        }

        static void Masses(int side, (float a, float b)[] spans)
        {
            foreach (var s in spans)
            {
                float len = s.b - s.a;
                Heap(side * R(49.5f, 50.5f), (s.a + s.b) * 0.5f, side < 0 ? 90f : -90f, 0f, len);
            }
        }
        // along a side wall from z -46 to 44: a heap 7-13 m long, then 8-14 m empty, skipping the kept-open spans
        static void Rhythm(int side, (float a, float b)[] open)
        {
            float z = -46f + R(0f, 5f);
            while (z < 42f)
            {
                float len = R(7f, 13f), z1 = Mathf.Min(z + len, 44f);
                bool clear = true;
                foreach (var o in open) if (z1 > o.a && z < o.b) clear = false;
                if (clear && z1 - z > 4f) Heap(side * R(48.5f, 50.5f), (z + z1) * 0.5f, side < 0 ? 90f : -90f, R(-8f, 8f), z1 - z);
                z = z1 + R(8f, 14f);
            }
        }

        // camera-side edge [1 E, 우리: bottom edge only]: near-black stone pillars and rubble just past the low south gallery,
        // so they rise into the bottom of the screen when the player comes within ~10 m of the south edge
        static readonly Dictionary<Material, Material> darkMats = new Dictionary<Material, Material>();
        static void Darken(GameObject go)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                var ms = r.sharedMaterials;
                for (int i = 0; i < ms.Length; i++)
                {
                    var m = ms[i]; if (!m) continue;
                    if (!darkMats.TryGetValue(m, out var d) || !d)
                    {
                        d = new Material(m) { name = m.name + "_ink" };
                        foreach (var c in new[] { "_BaseColor", "_Color", "_Tint" })
                            if (d.HasProperty(c)) { var t = d.GetColor(c); d.SetColor(c, new Color(t.r * 0.12f, t.g * 0.115f, t.b * 0.11f, t.a)); }
                        if (d.HasProperty("_Smoothness")) d.SetFloat("_Smoothness", 0f);
                        if (d.HasProperty("_RimScale")) d.SetFloat("_RimScale", 0f);
                        darkMats[m] = d;
                    }
                    ms[i] = d;
                }
                r.sharedMaterials = ms;
                r.shadowCastingMode = ShadowCastingMode.Off;
            }
        }
        static void SouthDark()
        {
            float x = -52f + R(0f, 4f);
            while (x < 52f)
            {
                if (Mathf.Abs(x) > 6f)
                {
                    float z = R(-53.5f, -51f);
                    var g = Chance(0.55) ? Prop("EdgeFeatures/EF_RF_StonePillar", x, z, R(0, 360), R(0.55f, 0.8f), 0f, R(-6f, 6f), 0.1f, 8f)
                                         : Prop("PalaceStage/PS_Rubble", x, z, R(0, 360), R(2.4f, 3.2f), 0f, 0f, 0.1f, 8f);
                    if (g) Darken(g);
                    if (Chance(0.5)) { var h = Prop("PalaceStage/PS_Rubble", x + R(-2f, 2f), z + R(-1.5f, 0.5f), R(0, 360), R(1.8f, 2.6f), 0f, 0f, 0.1f, 8f); if (h) Darken(h); }
                }
                x += R(6f, 13f);
            }
        }

        static void NorthMasses((float a, float b)[] spans)
        {
            foreach (var s in spans)
            {
                Heap((s.a + s.b) * 0.5f, R(41.5f, 42.5f), 180f, 90f, s.b - s.a);
                Heap(-(s.a + s.b) * 0.5f + R(-2f, 2f), R(41.5f, 42.5f), 180f, 90f, (s.b - s.a) * R(0.6f, 0.8f));   // other side shorter: not mirrored
            }
        }

        // the fallen west bay at z: the biggest heap in the court where the wall was, beams and roof tiles fanned
        // inward (dense at the wall, thinning out by ~12 m), with the bay's broken ends left standing
        static void FallenBay(float z)
        {
            Heap(-51f, z, 90f, 0f, 11f);
            Heap(-48f, z + R(-1f, 1f), 90f, R(-10f, 10f), 6f);
            for (int i = 0; i < 4; i++) Prop("EdgeFeatures/PP_Beam", R(-49f, -43f), z + R(-4f, 4f), R(40f, 140f), R(2.6f, 3.1f), 0f, R(-8f, 8f), 0.12f);
            for (int i = 0; i < 46; i++)
            {
                float d = 2f + 11f * Mathf.Pow((float)rng.NextDouble(), 1.7f);
                float ang = R(-38f, 38f) * Mathf.Deg2Rad;
                Prop(Bits[rng.Next(Bits.Length)], -50f + Mathf.Cos(ang) * d, z + Mathf.Sin(ang) * d * 1.2f, R(0, 360), R(0.85f, 1.25f), R(-25f, 25f), R(-25f, 25f), 0.08f);
            }
            Prop("PalaceStage/PS_CorpseCovered", -42.5f, z + 3.2f, 70f, 1.25f, 0f, 0f, 0.05f);
        }
    
        // ================================================================== 저승 (underworld)
        // Floor, lava sea, lighting and grade stay as approved (floor dark, no coded lava). The purple film is the original
        // 362ee353 version (restored as files). Plaza x -44..44, z -29..27 at y 0 (no collider), sea at y -6.
        //   - fire: the 78 flame patches spread evenly over the sea and lip -> only the ones in four burning stretches stay
        //     (SW corner, east middle, NW, gate), dark rest stretches between [5-2, 6.6 accent <= 5%]
        //   - bones: the ~40 single bones scattered over the floor off; piles stay, three bone mounds at the rim with
        //     corpses and skulls added [5-2 never alone, 5-4 story]
        //   - edge mass: crag masses (3-5 overlapping basalt boulders rising ~2-3 m over the lip) on the east / west / north
        //     lip in groups with gaps, instead of only the even 3-6 m bead row [4-3]
        static readonly Rect UWPlaza = new Rect(-44f, -29f, 88f, 56f);
        static float UWGround(float x, float z) => UWPlaza.Contains(new Vector2(x, z)) ? 0f : -6f;

        static GameObject UWPut(string path, float x, float z, float y, float yaw, float s, float sinkFrac, bool shadows)
        {
            var prefab = Load<GameObject>(path);
            if (!prefab) { Debug.LogWarning("[EnvRecompose] no prefab " + path); return null; }
            var go = Object.Instantiate(prefab, root);
            go.name = prefab.name;
            go.transform.SetPositionAndRotation(new Vector3(x, y, z), Quaternion.Euler(0f, yaw, 0f));
            go.transform.localScale = Vector3.one * s;
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length > 0)
            {
                var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                go.transform.position += Vector3.up * (y - b.min.y - sinkFrac * b.size.y);
                foreach (var r in rs) { r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off; }
            }
            foreach (var c in go.GetComponentsInChildren<Collider>()) Destroy(c);
            count++;
            return go;
        }

        static int Underworld()
        {
            // 1. fire rhythm
            var keep = new[] { new Vector2(-46f, -31f), new Vector2(47f, -2f), new Vector2(-34f, 30f), new Vector2(0f, 36f), new Vector2(30f, 30f) };
            var sea = GameObject.Find("UWLook fire sea");
            if (sea)
                foreach (Transform t in sea.transform)
                {
                    if (!t.name.StartsWith("HellFlames") && !t.name.StartsWith("HellFireSmoke") && !t.name.StartsWith("HellSparks")) continue;
                    var p = new Vector2(t.position.x, t.position.z);
                    bool near = false; foreach (var k in keep) if ((p - k).magnitude < 13f) near = true;
                    if (!near) Hide(t.gameObject);
                }
            // 2. single bones off (no other bone within 2.2 m)
            var bones = GameObject.Find("UWLook bones");
            if (bones)
            {
                var list = new List<Transform>(); foreach (Transform t in bones.transform) list.Add(t);
                foreach (var t in list)
                {
                    bool alone = true;
                    foreach (var o in list) if (o != t && (o.position - t.position).sqrMagnitude < 2.2f * 2.2f) { alone = false; break; }
                    if (alone) Hide(t.gameObject);
                }
            }
            // 3. crag masses on the lip: groups of 3-5 boulders overlapping, centre just outside the lip so the tops rise
            //    2-3 m over the plaza edge; gaps of 10-18 m between groups
            void Crag(float x, float z, float along, int n)
            {
                var a = Quaternion.Euler(0, along, 0) * Vector3.forward;
                for (int i = 0; i < n; i++)
                {
                    float t = (i - (n - 1) * 0.5f) * R(2.2f, 3.2f);
                    var p = new Vector3(x, 0, z) + a * t;
                    float s = R(6.5f, 8.5f) * (1f - Mathf.Abs(i - (n - 1) * 0.5f) / n * 0.5f);
                    UWPut("Hell/Boulder", p.x + R(-0.8f, 0.8f), p.z + R(-0.8f, 0.8f), -6f, R(0, 360), s, 0f, false);
                }
            }
            for (int side = -1; side <= 1; side += 2)
            {
                float x = side < 0 ? UWPlaza.xMin - 3.2f : UWPlaza.xMax + 3.2f;
                float z = UWPlaza.yMin + R(4f, 9f);
                while (z < UWPlaza.yMax - 3f)
                {
                    int n = 3 + rng.Next(3);
                    Crag(x + side * R(0f, 1.5f), z + n * 1.3f, 0f, n);
                    z += n * 2.7f + R(10f, 18f);
                }
            }
            // north lip, away from the gate approach (|x| > 16)
            foreach (float x in new[] { -38f, -24f, 27f, 40f }) Crag(x + R(-2f, 2f), UWPlaza.yMax + 3.2f, 90f, 3 + rng.Next(2));

            // 4. bone mounds: a story group at three rim spots (skull heap, bones, a burnt corpse dragged toward it)
            void Mound(float x, float z, float yaw)
            {
                UWPut("Hell/Skulls", x, z, UWGround(x, z), yaw, R(1.3f, 1.6f), 0.15f, false);
                var bp = Resources.LoadAll<GameObject>("Bones");
                for (int i = 0; i < 9 && bp.Length > 0; i++)
                {
                    float d = 1.2f + 3.2f * Mathf.Pow((float)rng.NextDouble(), 1.4f), an = R(0, Mathf.PI * 2);
                    float bx = x + Mathf.Cos(an) * d, bz = z + Mathf.Sin(an) * d;
                    if (!UWPlaza.Contains(new Vector2(bx, bz))) continue;
                    var g = Object.Instantiate(bp[rng.Next(bp.Length)], root);
                    g.transform.SetPositionAndRotation(new Vector3(bx, 0.02f, bz), Quaternion.Euler(R(-6f, 6f), R(0, 360), R(-6f, 6f)));
                    g.transform.localScale = Vector3.one * R(0.9f, 1.3f);
                    foreach (var r in g.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.Off;
                    count++;
                }
                string[] cp = { "HellFX/Corpse_Back", "HellFX/Corpse_Front", "HellFX/Corpse_Side" };
                UWPut(cp[rng.Next(3)], x + R(2.5f, 3.5f), z + R(-1.5f, 1.5f), 0f, R(0, 360), R(1f, 1.1f), 0.02f, false);
            }
            Mound(-41f, -12f, 30f); Mound(40.5f, 14f, 200f); Mound(-20f, -26.5f, 110f);

            // 5. value structure [6.5, 1 D brightest]: the plaza read as one black field (only the film's clearing showed).
            //    One wide, dim, cool moon pool from high above the main clearing, falling off toward the lip, so the fight
            //    floor is the brightest dark and the crags / sea stay darker. Light only: the floor material and grade are
            //    untouched. 11-18 capture: no visible change - the original film covers the plaza until walked through, so the
            //    pool only shows inside the cleared disc. Off by default; RR.EnvRecompose.uwMoon = 1 turns it on.
            if (PlayerPrefs.GetInt("RR.EnvRecompose.uwMoon", 0) != 0)
            {
                var lg = new GameObject("ER_ClearingMoon"); lg.transform.SetParent(root, false);
                lg.transform.SetPositionAndRotation(new Vector3(-2f, 34f, -6f), Quaternion.Euler(84f, 0f, 0f));
                var l = lg.AddComponent<Light>(); l.type = LightType.Spot; l.spotAngle = 95f; l.innerSpotAngle = 30f; l.range = 60f;
                l.color = new Color(0.62f, 0.58f, 0.78f); l.intensity = PlayerPrefs.GetFloat("RR.EnvRecompose.uwMoonI", 260f);
                l.shadows = LightShadows.None; l.renderMode = LightRenderMode.ForcePixel;
                lightsOn++;
            }

            // 6. 11-19 low mounds: the 88 x 56 m plaza splits into a west clearing (x < -27), the big central one (spawn,
            //    both river-free lanes to the gate and the portal kept clear) and an east clearing (x > 26)
            if (MoundsOn("underworld")) UnderworldMounds(root.gameObject.scene);
            else if (DecalsOn)
            {
                // pass 3: 저승 gets no decal (the charcoal specks hardly read); MoundSiteDecals(..., charcoal: true) kept for a later try
            }
            return count;
        }

        // Ground: a copy of the plaza's own UW_Floor (Underworld/Ground, world-space, same Fab scans) with more of its earth
        // / ash and rubble channels and no coded lava or soul cracks, so the heap is dark basalt and ash, never pale.
        static void UnderworldMounds(Scene scene)
        {
            Material floor = null;
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                if (r && r.gameObject.scene == scene && r.sharedMaterial && r.sharedMaterial.name.StartsWith("UW_Floor")) { floor = r.sharedMaterial; break; }
            if (!floor) { Debug.LogWarning("[EnvRecompose] UW_Floor not found, no mounds"); return; }
            // the underworld code works in world coordinates (plaza x -44..44, z -29..27 at y 0)
            Debug.Log($"[EnvRecompose]   underworld room centre {off} baseY {baseY:0.00}");
            off = Vector3.zero;
            var mat = new Material(floor) { name = "UW_Floor_ER_Mound" };
            if (mat.HasProperty("_AshAmt")) mat.SetFloat("_AshAmt", Mathf.Max(floor.GetFloat("_AshAmt"), PlayerPrefs.GetFloat("RR.EnvRecompose.uwMoundAsh", 0.6f)));
            if (mat.HasProperty("_RubAmt")) mat.SetFloat("_RubAmt", 0.85f);
            if (mat.HasProperty("_CodeFX")) mat.SetFloat("_CodeFX", 0f);
            if (mat.HasProperty("_CrackAmt")) mat.SetFloat("_CrackAmt", 0f);
            if (mat.HasProperty("_WetAmt")) mat.SetFloat("_WetAmt", 0f);
            mounds.Clear(); moundInGround = false;
            var defs = new List<MoundDef>
            {
                MakeMound(new Vector2(-23f, -15f), 10f, 16f, 1.0f),    // west, south half
                MakeMound(new Vector2(-26f, 12f), -20f, 13f, 0.7f),    // west, north half (14 m gap between)
                MakeMound(new Vector2(21f, -14f), -15f, 18f, 1.2f),    // east ridge
                MakeMound(new Vector2(31f, 11f), 25f, 11f, 0.6f),      // east, short (lane 1 passes 20+ m west of it)
            };
            HideBuried(scene);
            string[] skirt = { "Hell/Boulder", "Hell/Boulder", "LavaRocks/LR_C03", "LavaRocks/LR_C08" };
            foreach (var m in defs)
            {
                BuildMound(m, mat, true, "underworld");
                var ab = m.b - m.a; float L = ab.magnitude; var dir = L > 1e-3f ? ab / L : Vector2.up; var side = new Vector2(dir.y, -dir.x);
                // skirt: small basalt lumps half sunk along the rim (where the heap goes under the plaza)
                for (int i = 0; i < 16; i++)
                {
                    var c = m.a + ab * R(-0.1f, 1.1f); float sg = Chance(0.5) ? 1f : -1f;
                    float d = m.r * 0.5f;
                    for (int k = 0; k < 24; k++) { var q = c + side * sg * d; if (MoundH(m, q.x, q.y, out _) < 0f) break; d += m.r * 0.05f; }
                    var p = c + side * sg * (d + R(-0.8f, 0.4f));
                    string path = skirt[rng.Next(skirt.Length)];
                    var g = Prop(path, p.x, p.y, R(0, 360), 1f, R(-20f, 20f), R(-20f, 20f), 0.3f, 4f, Mathf.Max(0f, MoundTop(p.x, p.y, out _)));
                    if (g) { FitHeight(g, R(0.18f, 0.45f), 0.3f, Mathf.Max(0f, MoundTop(p.x, p.y, out _))); DarkenBy(g, 0.3f); }
                }
                // crown: a low basalt group and bones, two clusters per ridge
                for (int gi = 0; gi < 2; gi++)
                {
                    var c0 = m.a + ab * R(0.15f, 0.85f) + side * R(-0.3f, 0.3f) * m.r;
                    for (int i = 0; i < 4; i++)
                    {
                        var p = c0 + new Vector2(R(-1f, 1f), R(-1f, 1f)) * (0.9f + i * 0.4f);
                        float y = Mathf.Max(0f, MoundTop(p.x, p.y, out _));
                        var g = Prop(skirt[rng.Next(skirt.Length)], p.x, p.y, R(0, 360), 1f, R(-15f, 15f), R(-15f, 15f), 0.3f, 4f, y);
                        if (g) { FitHeight(g, i == 0 ? R(0.6f, 0.9f) : R(0.25f, 0.5f), 0.3f, y); DarkenBy(g, 0.3f); }
                    }
                    var bp = Resources.LoadAll<GameObject>("Bones");
                    for (int i = 0; i < 4 && bp.Length > 0; i++)
                    {
                        var p = c0 + new Vector2(R(-2f, 2f), R(-2f, 2f));
                        var g = Object.Instantiate(bp[rng.Next(bp.Length)], root);
                        g.transform.SetPositionAndRotation(new Vector3(p.x, Mathf.Max(0f, MoundTop(p.x, p.y, out _)) + 0.02f, p.y) + off, Quaternion.Euler(R(-10f, 10f), R(0, 360), R(-10f, 10f)));
                        g.transform.localScale = Vector3.one * R(0.9f, 1.2f);
                        foreach (var r in g.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.Off;
                        foreach (var c in g.GetComponentsInChildren<Collider>()) Destroy(c);
                        count++;
                    }
                }
            }
        }

        // a value step down for placed pieces: material copies with the base colour scaled by k (cached per material)
        static readonly Dictionary<(Material, float), Material> dimMats = new Dictionary<(Material, float), Material>();
        static void DarkenBy(GameObject go, float k)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                var ms = r.sharedMaterials;
                for (int i = 0; i < ms.Length; i++)
                {
                    var m = ms[i]; if (!m) continue;
                    if (!dimMats.TryGetValue((m, k), out var d) || !d)
                    {
                        d = new Material(m) { name = m.name + "_dim" };
                        foreach (var c in new[] { "_BaseColor", "_Color", "_Tint" })
                            if (d.HasProperty(c)) { var t = d.GetColor(c); d.SetColor(c, new Color(t.r * k, t.g * k, t.b * k, t.a)); }
                        if (d.HasProperty("_Smoothness")) d.SetFloat("_Smoothness", Mathf.Min(d.GetFloat("_Smoothness"), 0.15f));
                        dimMats[(m, k)] = d;
                    }
                    ms[i] = d;
                }
                r.sharedMaterials = ms;
            }
        }

        // scale a placed piece so it stands `height` m tall, then re-seat it at y with `sink` of it under the surface
        static void FitHeight(GameObject g, float height, float sink, float y)
        {
            var rs = g.GetComponentsInChildren<Renderer>(); if (rs.Length == 0) return;
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            if (b.size.y < 1e-3f) return;
            g.transform.localScale *= height / b.size.y;
            b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            g.transform.position += Vector3.up * (y + off.y - b.min.y - sink * b.size.y);
            foreach (var r in rs) r.shadowCastingMode = ShadowCastingMode.Off;
        }
    
        // ================================================================== 폐촌 (abandoned)
        // Everything is baked into AbandonedVillage.unity (AVBuilder); nothing there is edited. At runtime:
        //   - abandoned, no lights [Kido]: every lantern / post / hanger / brazier / wisp light off and their glow
        //     (emission) dimmed out; only the moon stays [7: accent 없음 또는 달빛, 우리: <= 8 lights]
        //   - the ground fog sheets drifting over the floor read as white streaks on the ground -> off [우리: no veil on
        //     the floor; mood stays in post]
        //   - blood decals (~400) spread evenly over the whole village -> only the ones in groups of 3+ within 3 m stay
        //     [3-5: decals in story groups]
        //   - single loose floor dressing pieces (no neighbour within 2.5 m) off, so the dressing reads as groups [5-2]
        static int Abandoned(Scene scene)
        {
            var roots = scene.GetRootGameObjects();
            Transform Find(string n)
            {
                foreach (var r in roots) { if (r.name == n) return r.transform; var t = FindDeep(r.transform, n); if (t) return t; }
                return null;
            }
            // 1. lights: everything but the moon
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (!l || !l.enabled || l.gameObject.scene != scene || l.type == LightType.Directional) continue;
                if (l.GetComponentInParent<PlayerController>()) continue;
                l.enabled = false; lightsOff++;
            }
            foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                if (mb && mb.gameObject.scene == scene && (mb.GetType().Name == "AVFlicker" || mb.GetType().Name == "AVWisp")) mb.enabled = false;
            var mpb = new MaterialPropertyBlock();
            foreach (var g in new[] { "Lanterns", "EdgeLanterns", "Wisps" })
            {
                var t = Find(g); if (!t) continue;
                if (g == "Wisps") { Hide(t.gameObject); continue; }
                foreach (var r in t.GetComponentsInChildren<Renderer>(true))
                {
                    bool glow = false;
                    foreach (var m in r.sharedMaterials) if (m && (m.IsKeywordEnabled("_EMISSION") || m.name.ToLower().Contains("glow") || m.name.ToLower().Contains("flame") || m.name.ToLower().Contains("paper"))) glow = true;
                    if (r is ParticleSystemRenderer || r.name.ToLower().Contains("glow") || r.name.ToLower().Contains("halo") || r.name.ToLower().Contains("flame")) { r.enabled = false; hidden++; continue; }
                    if (!glow) continue;
                    r.GetPropertyBlock(mpb); mpb.SetColor("_EmissionColor", Color.black); r.SetPropertyBlock(mpb);
                }
            }
            foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
            {
                if (!ps || ps.gameObject.scene != scene) continue;
                string n = ps.name.ToLower();
                if (n.Contains("flame") || n.Contains("fire") || n.Contains("ember") || n.Contains("glow")) { ps.gameObject.SetActive(false); hidden++; }
            }
            // 1b. 11-19 checkerboard light/shadow: the moon cookie AV_MoonCookie.png is a plaid (its "torus" noise is
            //     noise(u) + noise(v), so bright/dark stripes run along both texture axes and cross into square blocks,
            //     plus long streaks), tiled every 34 m and projected at 42 deg -> diagonal bands and blocky dark patches.
            //     With every lantern off and the fog gone it was the only thing modulating the floor. Replaced by a
            //     runtime copy of real 2D cloud noise (no axis), 72 m tile, same mean so the mood is not brightened.
            //     RR.EnvRecompose.avCookie: 1 new (default), 0 the old plaid, 2 no cookie (moon scaled by its mean).
            MoonCookie(scene);
            // 2. ground fog over the floor (RR.EnvRecompose.avFog = 1 keeps it, diagnostic only)
            var gf = Find("GroundFog"); if (gf && PlayerPrefs.GetInt("RR.EnvRecompose.avFog", 0) == 0) Hide(gf.gameObject);
            // 11-18 r2: the white bands along the streets are the street puddles mirroring the moon (rule 3-4: water only
            // in ruts / cracks, never a pale sheet over the floor) -> off
            var pud = Find("Puddles"); if (pud) Hide(pud.gameObject);
            // 2b. the white streaks along the streets were the moon's specular sheen: the baked terrain layers carry gloss
            //     0.35-0.6 (rule 3-2: dry ground 0-0.15, matte). Runtime copies of the terrain data and its layers with the
            //     gloss clamped; textures, colours and splat untouched, the assets on disk are not changed.
            foreach (var terr in Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None))
            {
                if (!terr || terr.gameObject.scene != scene || !terr.terrainData) continue;
                var td = Object.Instantiate(terr.terrainData); td.name = terr.terrainData.name + "_matte";
                var ls = td.terrainLayers;
                for (int i = 0; i < ls.Length; i++)
                {
                    if (!ls[i]) continue;
                    var c = Object.Instantiate(ls[i]); c.name = ls[i].name + "_matte";
                    var mx = c.maskMapRemapMax; mx.w = Mathf.Min(mx.w, 0.12f); c.maskMapRemapMax = mx;
                    var mn = c.maskMapRemapMin; mn.w = Mathf.Min(mn.w, 0f); c.maskMapRemapMin = mn;
                    c.smoothness = Mathf.Min(c.smoothness, 0.12f); c.metallic = 0f; c.specular = Color.black;
                    // 11-18 r3: gloss and puddles were not it - the dirt-road scan itself (mean albedo 0.47 vs the yard ~0.2)
                    // lit by the moon is the white band. Rule 3-2: the path is ONE step lighter than the base (0.05-0.12),
                    // so its albedo is scaled toward the base. RR.EnvRecompose.avRoad = 0 keeps the old road.
                    if (ls[i].name.Contains("DirtRoad") && PlayerPrefs.GetInt("RR.EnvRecompose.avRoad", 1) != 0)
                    {
                        float k = PlayerPrefs.GetFloat("RR.EnvRecompose.avRoadK", 0.48f);
                        var dm = c.diffuseRemapMax; c.diffuseRemapMax = new Vector4(dm.x * k, dm.y * k, dm.z * k, dm.w);
                    }
                    ls[i] = c;
                }
                td.terrainLayers = ls;
                terr.terrainData = td;
                var tc = terr.GetComponent<TerrainCollider>(); if (tc) tc.terrainData = td;
                count++;
            }
            // 3. blood in groups only; 4. lone floor dressing off
            var blood = new List<Transform>(); var dressing = new List<Transform>();
            foreach (var r in roots) Collect(r.transform, blood, dressing, false);
            IsolationHide(blood, 3f, 2);
            IsolationHide(dressing, 2.5f, 1);
            Debug.Log($"[EnvRecompose] abandoned: {blood.Count} blood decals, {dressing.Count} floor dressing pieces checked");
            // 5. 11-19 low mounds raised in the (runtime copy of the) terrain itself
            if (MoundsOn("abandoned")) AbandonedMounds(scene);
            return count;
        }

        const float OldCookieMean = 0.686f;   // measured mean of AV_MoonCookie.png
        static Texture2D cloudCookie;
        static void MoonCookie(Scene scene)
        {
            int mode = PlayerPrefs.GetInt("RR.EnvRecompose.avCookie", 1);
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (!l || l.gameObject.scene != scene || l.type != LightType.Directional || !l.cookie) continue;
                var ad = l.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>();
                string was = $"{l.cookie.name} size {(ad ? ad.lightCookieSize : Vector2.zero)}";
                if (mode == 0) { Debug.Log($"[EnvRecompose]   moon cookie kept: {was}"); continue; }
                if (mode == 2) { l.cookie = null; l.intensity *= OldCookieMean; }
                else
                {
                    if (!cloudCookie) cloudCookie = MakeCloudCookie(256, OldCookieMean);
                    l.cookie = cloudCookie;
                    if (ad) { ad.lightCookieSize = new Vector2(72f, 72f); ad.lightCookieOffset = new Vector2(13f, 7f); }
                }
                Debug.Log($"[EnvRecompose]   moon cookie {was} -> {(mode == 2 ? "none" : "cloud 72 m")}, moon i{l.intensity:0.00}");
            }
        }

        // Tileable 2D gradient noise (lattice wraps at the period), 3 octaves, remapped to a soft 0.5..1 cloud shadow and
        // scaled so its mean matches the old cookie.
        static Texture2D MakeCloudCookie(int res, float mean)
        {
            var h = new System.Random(7119);
            var grad = new Vector2[64 * 64];
            for (int i = 0; i < grad.Length; i++) { float a = (float)(h.NextDouble() * Mathf.PI * 2.0); grad[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)); }
            float Noise(float x, float y, int period)
            {
                int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
                float fx = x - x0, fy = y - y0;
                float Dot(int ix, int iy, float dx, float dy)
                {
                    var g = grad[(((ix % period) + period) % period) + 64 * ((((iy % period) + period) % period))];
                    return g.x * dx + g.y * dy;
                }
                float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);
                float u = Fade(fx), v = Fade(fy);
                float a = Mathf.Lerp(Dot(x0, y0, fx, fy), Dot(x0 + 1, y0, fx - 1f, fy), u);
                float b = Mathf.Lerp(Dot(x0, y0 + 1, fx, fy - 1f), Dot(x0 + 1, y0 + 1, fx - 1f, fy - 1f), u);
                return Mathf.Lerp(a, b, v);   // about -0.7..0.7
            }
            var val = new float[res * res]; float sum = 0f;
            for (int j = 0; j < res; j++)
                for (int i = 0; i < res; i++)
                {
                    float u = i / (float)res, v = j / (float)res;
                    float n = Noise(u * 5f, v * 5f, 5) * 0.6f + Noise(u * 11f + 3.3f, v * 11f + 1.7f, 11) * 0.28f + Noise(u * 23f + 7.1f, v * 23f + 4.9f, 23) * 0.12f;
                    float lit = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.28f, 0.3f, n));
                    float k = 0.5f + 0.5f * lit;
                    val[j * res + i] = k; sum += k;
                }
            float scale = mean / (sum / val.Length);
            var px = new Color32[val.Length];
            for (int i = 0; i < val.Length; i++) { byte c = (byte)Mathf.RoundToInt(Mathf.Clamp01(val[i] * scale) * 255f); px[i] = new Color32(c, c, c, c); }
            var tex = new Texture2D(res, res, TextureFormat.RGBA32, true, true) { name = "AV_MoonCookie_cloud", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            tex.SetPixels32(px); tex.Apply(true, false);
            return tex;
        }

        // rr_queue "call RoomRun.EnvRecompose.NextPrefs": consumes the first (by name) ArtSource/EnvRecompose/prefs_queue/*.txt
        // (lines "key int", or "key -" to delete) and appends the related switches to ArtSource/EnvRecompose/prefs_log.txt.
        // Lets A/B captures switch this layer's PlayerPrefs through the shared queue.
        public static void NextPrefs()
        {
            string dir = System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, "ArtSource", "EnvRecompose");
            var q = System.IO.Path.Combine(dir, "prefs_queue");
            var sb = new System.Text.StringBuilder(System.DateTime.Now.ToString("HH:mm:ss"));
            if (System.IO.Directory.Exists(q))
            {
                var files = new System.IO.DirectoryInfo(q).GetFiles("*.txt");
                System.Array.Sort(files, (a, b) => string.CompareOrdinal(a.Name, b.Name));
                if (files.Length > 0)
                {
                    sb.Append(' ').Append(files[0].Name).Append(':');
                    foreach (var raw in System.IO.File.ReadAllLines(files[0].FullName))
                    {
                        var p = raw.Trim().Split(' '); if (p.Length < 2) continue;
                        if (p[1] == "-") PlayerPrefs.DeleteKey(p[0]); else if (int.TryParse(p[1], out int v)) PlayerPrefs.SetInt(p[0], v); else continue;
                        sb.Append(' ').Append(raw.Trim());
                    }
                    System.IO.File.Delete(files[0].FullName);
                }
            }
            PlayerPrefs.Save();
            foreach (var k in new[] { "RR.EnvRecompose", "RR.EnvRecompose.abandoned", "RR.EnvRecompose.avCookie", "RR.EnvRecompose.avFog", "RR.EnvRecompose.mounds", "RR.EnvRecompose.abandoned.mounds", VolumeKey, CourtKey, "RR.ShadowPass", "RR.LightRadius" })
                sb.Append(" | ").Append(k).Append('=').Append(PlayerPrefs.HasKey(k) ? PlayerPrefs.GetInt(k).ToString() : "unset");
            sb.Append($" | quality shadowDist {QualitySettings.shadowDistance} cascades {QualitySettings.shadowCascades}");
            if (GraphicsSettings.currentRenderPipeline is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset urp)
                sb.Append($" | urp shadowDist {urp.shadowDistance} cascades {urp.shadowCascadeCount} res {urp.mainLightShadowmapResolution}");
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "prefs_log.txt"), sb + "\n");
        }

        // Ground: the terrain's own height + splat, so the heap IS the yard dirt (Tracks, the base layer) and has no rim at
        // all; the terrain collider carries the actors and Combat.Resolve never stops a bullet on terrain.
        static void AbandonedMounds(Scene scene)
        {
            Terrain terr = null;
            foreach (var t in Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None))
            {
                if (!t || t.gameObject.scene != scene || !t.terrainData) continue;
                var tp = t.transform.position; var sz = t.terrainData.size;
                if (off.x >= tp.x && off.x <= tp.x + sz.x && off.z >= tp.z && off.z <= tp.z + sz.z) { terr = t; break; }
            }
            if (!terr) { Debug.LogWarning("[EnvRecompose] abandoned terrain not found, no mounds"); return; }
            var td = terr.terrainData;
            if (!td.name.EndsWith("_matte")) { td = Object.Instantiate(td); td.name += "_mounds"; terr.terrainData = td; var tc = terr.GetComponent<TerrainCollider>(); if (tc) tc.terrainData = td; }
            mounds.Clear(); moundInGround = true;
            Debug.Log($"[EnvRecompose]   abandoned room centre {off}");
            var defs = new List<MoundDef>(AbandonedSpots());
            HideBuried(scene);

            var tpos = terr.transform.position; var size = td.size;
            int res = td.heightmapResolution;
            int ares = td.alphamapResolution, layers = td.alphamapLayers;
            int yard = 0;
            for (int i = 0; i < td.terrainLayers.Length; i++) if (td.terrainLayers[i] && td.terrainLayers[i].name.Contains("Tracks")) { yard = i; break; }
            foreach (var m in defs)
            {
                var lo = Vector2.Min(m.a, m.b) - Vector2.one * (m.r * 1.3f) + new Vector2(off.x, off.z);
                var hi = Vector2.Max(m.a, m.b) + Vector2.one * (m.r * 1.3f) + new Vector2(off.x, off.z);
                // heights
                int x0 = Mathf.Clamp(Mathf.FloorToInt((lo.x - tpos.x) / size.x * (res - 1)), 0, res - 1), x1 = Mathf.Clamp(Mathf.CeilToInt((hi.x - tpos.x) / size.x * (res - 1)), 0, res - 1);
                int z0 = Mathf.Clamp(Mathf.FloorToInt((lo.y - tpos.z) / size.z * (res - 1)), 0, res - 1), z1 = Mathf.Clamp(Mathf.CeilToInt((hi.y - tpos.z) / size.z * (res - 1)), 0, res - 1);
                int w = x1 - x0 + 1, hgt = z1 - z0 + 1;
                var hs = td.GetHeights(x0, z0, w, hgt);
                float maxAdd = 0f, slope = 0f;
                var add = new float[hgt, w];
                for (int j = 0; j < hgt; j++)
                    for (int i = 0; i < w; i++)
                    {
                        float wx = tpos.x + (x0 + i) / (float)(res - 1) * size.x - off.x, wz = tpos.z + (z0 + j) / (float)(res - 1) * size.z - off.z;
                        float hm = MoundH(m, wx, wz, out float s);
                        float a = Mathf.Max(0f, hm + 0.2f * (1f - s));   // = s * crown: meets the yard at 0, no sunk rim needed
                        add[j, i] = a; maxAdd = Mathf.Max(maxAdd, a);
                        hs[j, i] += a / size.y;
                    }
                float cx = size.x / (res - 1), cz = size.z / (res - 1);
                for (int j = 0; j < hgt - 1; j++)
                    for (int i = 0; i < w - 1; i++)
                        slope = Mathf.Max(slope, Mathf.Abs(add[j, i + 1] - add[j, i]) / cx, Mathf.Abs(add[j + 1, i] - add[j, i]) / cz);
                td.SetHeightsDelayLOD(x0, z0, hs);
                // splat: yard dirt over the heap, fading out into whatever the yard has around it
                int ax0 = Mathf.Clamp(Mathf.FloorToInt((lo.x - tpos.x) / size.x * ares), 0, ares - 1), ax1 = Mathf.Clamp(Mathf.CeilToInt((hi.x - tpos.x) / size.x * ares), 0, ares - 1);
                int az0 = Mathf.Clamp(Mathf.FloorToInt((lo.y - tpos.z) / size.z * ares), 0, ares - 1), az1 = Mathf.Clamp(Mathf.CeilToInt((hi.y - tpos.z) / size.z * ares), 0, ares - 1);
                var al = td.GetAlphamaps(ax0, az0, ax1 - ax0 + 1, az1 - az0 + 1);
                for (int j = 0; j < al.GetLength(0); j++)
                    for (int i = 0; i < al.GetLength(1); i++)
                    {
                        float wx = tpos.x + (ax0 + i + 0.5f) / ares * size.x - off.x, wz = tpos.z + (az0 + j + 0.5f) / ares * size.z - off.z;
                        MoundH(m, wx, wz, out float s);
                        float k = Mathf.Clamp01(s * 2.2f);
                        if (k <= 0f) continue;
                        for (int l = 0; l < layers; l++) al[j, i, l] = Mathf.Lerp(al[j, i, l], l == yard ? 1f : 0f, k);
                    }
                td.SetAlphamaps(ax0, az0, al);
                count++;
                Debug.Log($"[EnvRecompose]   mound abandoned at ({(m.a.x + m.b.x) * 0.5f:0},{(m.a.y + m.b.y) * 0.5f:0}) len {(m.b - m.a).magnitude + 2f * m.r * 0.85f:0.0} m, crown {maxAdd:0.00} m, max slope {Mathf.Atan(slope) * Mathf.Rad2Deg:0.0} deg (heightmap {cx:0.00} m)");
            }
            terr.ApplyDelayedHeightmapModification();
            Physics.SyncTransforms();

            // dressing: the village's own dry grass and stones cloned onto the skirt, low yard clutter on the crown
            var grass = SceneTemplates(scene, new[] { "WildDry", "Grass", "grass" });
            var stones = SceneTemplates(scene, new[] { "SmallRocks", "Pebbles", "RubblePatch", "Stone" });
            var clones = new List<GameObject>(grass); clones.AddRange(stones);
            Debug.Log($"[EnvRecompose]   abandoned templates: {grass.Length} grass, {stones.Length} stones");
            string[] skirt = { "PalaceStage/PS_Pebbles", "PalaceStage/PS_Rocks", "PalaceStage/PS_Sticks" };
            string[] crown = { "PalaceStage/PS_Planks", "PalaceStage/PS_Sticks", "PalaceStage/PS_Pot", "PalaceStage/PS_Jar", "PalaceStage/PS_Debris", "PalaceStage/PS_Planks" };
            int before = root.childCount;
            foreach (var m in defs) DressMound(m, skirt, crown, 10, 8, 1f, clones.ToArray(), clones.Count > 0 ? 22 : 0);
            // 11-19 r2: under the moon the stones and pots read as white specks on the dark yard -> one value step down
            for (int i = before; i < root.childCount; i++) if (!root.GetChild(i).name.Contains("WildDry") && !root.GetChild(i).name.Contains("Grass")) DarkenBy(root.GetChild(i).gameObject, 0.5f);
        }

        // room-local spots (room centre = world (0, -3.5)). The streets (AVBuilder.Streets, mapped) cut the 92 x 90 yard into
        // four quarters; the mounds stay 4+ m off every street and split the three big ones: NW (49 x 38 m), the south-east
        // (46 x 68 m, two mounds) and SW (38 x 48 m). The spawn crossing at the centre stays open.
        static MoundDef[] AbandonedSpots() => new[]
        {
            MakeMound(new Vector2(-26f, 17.5f), 20f, 16f, 1.0f),   // NW quarter
            MakeMound(new Vector2(25f, -17.5f), -30f, 15f, 0.9f),  // south-east, middle
            MakeMound(new Vector2(36f, 3.5f), 10f, 11f, 0.6f),     // south-east, toward the east street
            MakeMound(new Vector2(-30f, -24.5f), 60f, 13f, 0.8f),  // SW quarter
        };

        static Transform FindDeep(Transform t, string n)
        {
            foreach (Transform c in t) { if (c.name == n) return c; if (c.childCount > 0 && c.childCount < 400) { var f = FindDeep(c, n); if (f) return f; } }
            return null;
        }
        static void Collect(Transform t, List<Transform> blood, List<Transform> dressing, bool inDressing)
        {
            foreach (Transform c in t)
            {
                if (c.name.StartsWith("AV_BloodDecal")) { blood.Add(c); continue; }
                bool d = inDressing || c.name == "FloorDressing";
                if (inDressing && c.childCount == 0 || inDressing && c.GetComponent<Renderer>()) { dressing.Add(c); continue; }
                if (c.childCount > 0) Collect(c, blood, dressing, d);
            }
        }
        // hide pieces with fewer than `need` others within `radius` (xz)
        static void IsolationHide(List<Transform> list, float radius, int need)
        {
            float r2 = radius * radius;
            var off = new List<Transform>();
            for (int i = 0; i < list.Count; i++)
            {
                if (!list[i].gameObject.activeInHierarchy) continue;
                int n = 0; var p = list[i].position;
                for (int j = 0; j < list.Count && n < need; j++)
                {
                    if (i == j) continue;
                    var q = list[j].position; float dx = p.x - q.x, dz = p.z - q.z;
                    if (dx * dx + dz * dz < r2) n++;
                }
                if (n < need) off.Add(list[i]);
            }
            foreach (var t in off) Hide(t.gameObject);
        }
    }
}
