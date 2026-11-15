// Light accents in the dark (11-13 overnight pass, Kido: "true dark fantasy, POE2/Tarae; glowing things should stand out
// in the dark: moonlight shafts / god rays, lanterns, 도깨비불"). A drawing-only layer over the LookStudy fields and the
// snow field, no realtime lights, no GameObjects, no colliders (nothing in the arena blocks or stands up):
//   - moonlight shafts: a few soft cones leaning toward the moon (the field's directional light), with a moonlit patch
//     projected on the ground where each lands (RR_LightShaft pool: brightens the floor by its own colour) and faint dust
//     motes drifting in the beam;
//   - 도깨비불: small blue-green fire wisps drifting at the arena edges, corners and here and there in the field, each with a faint pool on the ground;
//   - lantern glow: a warm halo around the firebox of every StageMood stone lantern, so the lanterns read as glowing
//     sources from far off (the bloom picks them up), not only as light on the floor.
// Per-field numbers in Looks. Off: PlayerPrefs RR.StageAccents = 0, or perf variant off:StageAccents.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RoomRun
{
    public sealed class StageAccents : MonoBehaviour
    {
        sealed class Look
        {
            public int shafts = 4;
            public Color shaft = new Color(0.20f, 0.28f, 0.46f);   // additive, HDR; moon-blue, not grey (11-14 review: grey read as smoke)
            public Color pool = new Color(3.0f, 3.4f, 4.3f);       // DstColor multiplier added on top (1 = doubles; the floor is very dark, so x3-4)
            public float shaftRadius = 2.5f;
            public int wisps = 10;
            public Color wisp = new Color(0.37f, 0.84f, 0.75f);
            public float wispGlow = 7f;
            public float lanternGlow = 1f;
        }

        static readonly Dictionary<string, Look> Looks = new Dictionary<string, Look>
        {
            ["grass"]   = new Look { shafts = 4, wisps = 12 },
            ["valley"]  = new Look { shafts = 3, wisps = 10 },
            ["village"] = new Look { shafts = 3, wisps = 0 },   /* 11-14 Kido: the wisp read as a white glowing cone on the grass; abandoned village, no ghost lights */
            ["palace"]  = new Look { shafts = 3, wisps = 0, /* 11-14: the wisps read as white glowing cones (Kido in 민가) */ shaft = new Color(0.17f, 0.24f, 0.40f), pool = new Color(1.8f, 2.1f, 2.9f) },
            // the reed field draws its own moon shafts (Reeds.RFMoonShafts, they also light the reed tops): wisps and lantern halos only
            ["reeds"]   = new Look { shafts = 0, wisps = 10, wisp = new Color(0.36f, 0.6f, 0.4f), wispGlow = 0.45f },   // 11-14 reed QA: white-cyan read as bullets/pickups; pale marsh green, dimmer
            // snow: no accents (11-14 overnight: on the bright snow a wisp read as a glitchy sparkle and the shafts added nothing)
        };

        public static bool Enabled => PlayerPrefs.GetInt("RR.StageAccents", 1) != 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var go = new GameObject("StageAccents");
            DontDestroyOnLoad(go);
            go.AddComponent<StageAccents>();
        }

        struct Shaft { public Matrix4x4 cone, pool; public Material coneMat, poolMat; public Vector3 basePos, axis; public float r, h; public Color col; }
        struct Wisp { public Vector3 anchor; public float seed, roam; }

        readonly List<Shaft> shafts = new List<Shaft>();
        readonly List<Wisp> wisps = new List<Wisp>();
        readonly List<Vector3> lanterns = new List<Vector3>();
        Look look;
        string doneFor;
        float lanternScan;

        Mesh cone, cube, quad;
        Material wispHalo, wispCore, wispFlame, wispPool, dust, lanternHalo, lanternCore;
        Shader shaftSh;
        const int MaxInst = 512;
        readonly Matrix4x4[] haloM = new Matrix4x4[MaxInst], coreM = new Matrix4x4[MaxInst], flameM = new Matrix4x4[MaxInst], sparkM = new Matrix4x4[MaxInst], lanternCoreM = new Matrix4x4[MaxInst], dustM = new Matrix4x4[MaxInst], poolM = new Matrix4x4[64];

        void Awake()
        {
            shaftSh = Resources.Load<Shader>("RR_LightShaft");
            cone = MakeCone(24, 0.42f);
            cube = MakeCube();
            quad = new Mesh { name = "StageAccents quad", hideFlags = HideFlags.DontSave };
            quad.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(-0.5f, 0.5f, 0), new Vector3(0.5f, 0.5f, 0) };
            quad.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            quad.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            quad.bounds = new Bounds(Vector3.zero, Vector3.one * 2f);
            var glow = Resources.Load<Shader>("RR_AddGlow");
            if (glow)
            {
                Material M(string tex, float soft, int mode = 2)
                {
                    var m = new Material(glow) { enableInstancing = true, hideFlags = HideFlags.DontSave };
                    m.SetFloat("_Streak", mode); m.SetFloat("_Soft", soft);
                    var t = Resources.Load<Texture2D>("VFX/" + tex); if (t) m.SetTexture("_MainTex", t);
                    return m;
                }
                // procedural soft discs (mode 0) and a tapered tongue (mode 1): the Kenney "light" sprites have rings and read as bubbles
                wispHalo = M(null, 2.6f, 0); wispCore = M(null, 1.4f, 0); wispFlame = M(null, 1.0f, 1); dust = M("light_03", 2f);
                lanternHalo = M(null, 2.4f, 0); lanternCore = M(null, 1.3f, 0);
                dust.SetColor("_Color", new Color(0.55f, 0.62f, 0.75f) * 0.5f);
                lanternHalo.SetColor("_Color", new Color(1f, 0.52f, 0.2f) * 1.5f);
                lanternCore.SetColor("_Color", new Color(1f, 0.72f, 0.42f) * 2.4f);
            }
            if (shaftSh)
            {
                wispPool = new Material(shaftSh) { hideFlags = HideFlags.DontSave, enableInstancing = true };
                wispPool.SetFloat("_Mode", 1); wispPool.SetFloat("_Edge", 1.6f);
            }
        }

        void LateUpdate()
        {
            var run = RunDirector.Instance;
            if (!run || run.Transitioning || run.Room == null || !Enabled) return;
            var scene = SceneManager.GetActiveScene();
            string key = run.Room.id + "@" + scene.handle;
            if (key != doneFor) { doneFor = key; Build(run); }
            if (look == null) return;
            if (Time.unscaledTime > lanternScan) { lanternScan = Time.unscaledTime + 2f; ScanLanterns(scene); }
            ready = true;
        }

        bool ready;
        void OnEnable() { UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering += OnCam; }
        void OnDisable() { UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering -= OnCam; }
        // drawn per camera (like LookFX), so the remote shots camera that renders by hand gets them too
        void OnCam(UnityEngine.Rendering.ScriptableRenderContext ctx, Camera cam)
        {
            if (!ready || look == null || !cam || cam.cameraType != CameraType.Game) return;
            var run = RunDirector.Instance;
            if (!run || run.Transitioning || run.Room == null || !Enabled) { ready = false; return; }
            Draw(cam);
        }

        void Build(RunDirector run)
        {
            foreach (var o in shafts) { if (o.coneMat) Destroy(o.coneMat); if (o.poolMat) Destroy(o.poolMat); }   // per-visit materials, not leaked
            shafts.Clear(); wisps.Clear(); lanterns.Clear(); lanternScan = 0;
            look = Looks.TryGetValue(run.Room.id, out var l) ? l : null;
            if (look == null || !shaftSh) return;
            Vector3 c = run.Room.center; float hx = run.Room.size.x * 0.5f, hz = run.Room.size.y * 0.5f;
            var rnd = new System.Random(run.Room.id.GetHashCode() ^ 0x5eed);
            float R() => (float)rnd.NextDouble();

            // moon direction: the brightest directional light, leaning at most 28 deg off vertical and always away from the
            // camera (north), so the columns rise up the screen instead of toward the lens
            Light moon = null;
            foreach (var li in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (li.type == LightType.Directional && li.isActiveAndEnabled && (!moon || li.intensity > moon.intensity)) moon = li;
            Vector3 toMoon = moon ? -moon.transform.forward : new Vector3(-0.3f, 1f, 0.5f);
            var flat = new Vector3(toMoon.x, 0, toMoon.z);
            if (flat.sqrMagnitude < 1e-4f) flat = new Vector3(-0.4f, 0, 1f);
            flat.Normalize();
            if (flat.z < 0.35f) { flat.z = Mathf.Abs(flat.z) + 0.35f; flat.Normalize(); }
            var axis = (Vector3.up * Mathf.Cos(28f * Mathf.Deg2Rad) + flat * Mathf.Sin(28f * Mathf.Deg2Rad)).normalized;

            // shafts: spread over the arena on a jittered ring (never one dead centre on the player's start)
            var spots = new List<Vector2> { new Vector2(-0.55f, 0.4f), new Vector2(0.45f, 0.5f), new Vector2(0.25f, -0.35f), new Vector2(-0.4f, -0.5f), new Vector2(0.75f, -0.1f), new Vector2(-0.8f, -0.05f) };
            for (int i = 0; i < spots.Count && shafts.Count < look.shafts; i++)
            {
                var s = spots[i] + new Vector2(R() - 0.5f, R() - 0.5f) * 0.18f;
                var p = new Vector3(c.x + s.x * hx, c.y, c.z + s.y * hz);
                p.y = Ground.Sample(p, c.y);
                // 11-14 review: a shaft beside the stream read as a waterfall; skip spots on or near water (a hit named *ater*
                // or *iver*, or a bed sunk well below the arena floor)
                if (NearWater(p, c.y)) continue;
                float r = look.shaftRadius * (0.8f + R() * 0.5f), h = 24f;
                var sh = new Shaft { basePos = p, axis = axis, r = r, h = h };
                sh.cone = Matrix4x4.TRS(p - axis * 1f, Quaternion.FromToRotation(Vector3.up, axis), new Vector3(r, h, r));
                // the patch on the ground: a flat box stretched a little along the lean
                var yaw = Quaternion.LookRotation(flat);
                sh.pool = Matrix4x4.TRS(p, yaw, new Vector3(r * 2.3f, 5f, r * 2.9f));
                sh.coneMat = new Material(shaftSh) { hideFlags = HideFlags.DontSave };
                sh.col = look.shaft * (0.8f + R() * 0.4f); sh.coneMat.SetColor("_Color", sh.col);
                sh.coneMat.SetFloat("_Mode", 0); sh.coneMat.SetFloat("_Seed", R() * 50f);
                sh.poolMat = new Material(shaftSh) { hideFlags = HideFlags.DontSave };
                sh.poolMat.SetColor("_Color", look.pool);
                sh.poolMat.SetFloat("_Mode", 1); sh.poolMat.SetFloat("_Edge", 1.4f); sh.poolMat.SetFloat("_Seed", R() * 50f);
                shafts.Add(sh);
            }

            // wisps: along the edges and in the corners, just inside the arena (they float, nothing to bump into)
            for (int i = 0, tries = 0; i < look.wisps && tries < look.wisps * 8; tries++)
            {
                float u, v;
                if (i % 3 == 0) { u = (R() < 0.5f ? -1f : 1f) * (0.8f + R() * 0.12f); v = (R() < 0.5f ? -1f : 1f) * (0.75f + R() * 0.12f); }      // corners
                else if (R() < 0.5f) { u = R() < 0.5f ? -0.93f : 0.93f; v = R() * 1.6f - 0.8f; }       // sides
                else if (R() < 0.45f) { u = R() * 1.6f - 0.8f; v = R() < 0.5f ? -0.9f : 0.92f; }               // top / bottom
                else { do { u = R() * 1.6f - 0.8f; v = R() * 1.6f - 0.8f; } while (u * u + v * v < 0.12f); }   // loose in the field, never on the start spot
                var a = new Vector3(c.x + u * hx, 0, c.z + v * hz);
                a.y = Ground.Sample(a, c.y);
                // 11-14 (Kido: "물속에 왜 들어있어"): the reed field's south edge is river; a wisp anchored on the river bed
                // sat under the water with its glow pool lighting the water. Only dry ground (not well below the arena floor).
                if (a.y < c.y - 0.3f) continue;
                wisps.Add(new Wisp { anchor = a, seed = R() * 100f, roam = 1.2f + R() * 1.8f });
                i++;
            }
            // 11-14 review: core near white-mint (#D8FFF4), body teal (#5FD6C0), halo the deep edge teal (#2E8F86), halo only ~2x the core
            if (wispHalo) { wispHalo.SetColor("_Color", new Color(0.18f, 0.56f, 0.52f) * look.wispGlow * 0.5f); wispCore.SetColor("_Color", Color.Lerp(look.wisp, Color.white, 0.35f) * look.wispGlow * 1.3f); wispFlame.SetColor("_Color", look.wisp * look.wispGlow * 1.0f); }
            if (wispPool) wispPool.SetColor("_Color", look.wisp * 0.6f);
            var sp = new System.Text.StringBuilder(); foreach (var q in shafts) sp.Append(q.basePos.x.ToString("0")).Append(',').Append(q.basePos.z.ToString("0")).Append(' ');
            var wp = new System.Text.StringBuilder(); foreach (var q in wisps) wp.Append(q.anchor.x.ToString("0")).Append(',').Append(q.anchor.z.ToString("0")).Append(' ');
            Debug.Log("[StageAccents] " + run.Room.id + ": " + shafts.Count + " moon shafts (axis " + axis + ") at " + sp + "/ " + wisps.Count + " wisps at " + wp);
        }

        // StageMood's stone lanterns appear a frame or so after the scene; their lights sit 0.9 m in front of the firebox
        void ScanLanterns(Scene scene)
        {
            lanterns.Clear();
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name != "StageMood") continue;
                foreach (Transform t in root.transform)
                    if (t.name == "StoneLantern")
                    {
                        var li = t.Find("Light");
                        if (li) lanterns.Add(t.position + Vector3.up * li.localPosition.y);
                    }
            }
        }

        void Draw(Camera cam)
        {
            var camRot = cam.transform.rotation;
            float t = Time.time;

            int nd = 0;
            var run = RunDirector.Instance;
            var pp = run && run.Player ? run.Player.transform.position : new Vector3(1e6f, 0, 0);
            foreach (var s in shafts)
            {
                // 11-14 review: a shaft over the player hid it; it thins to 30% while the player stands in it
                float dpl = new Vector2(pp.x - s.basePos.x, pp.z - s.basePos.z).magnitude;
                s.coneMat.SetColor("_Color", s.col * Mathf.Lerp(0.3f, 1f, Mathf.InverseLerp(s.r * 0.6f, s.r * 2.6f, dpl)));
                Graphics.RenderMesh(new RenderParams(s.coneMat) { camera = cam, shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = false }, cone, 0, s.cone);
                Graphics.RenderMesh(new RenderParams(s.poolMat) { camera = cam, shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = false }, cube, 0, s.pool);
                // dust motes drifting slowly in the beam
                if (dust)
                    for (int k = 0; k < 22 && nd < MaxInst; k++)
                    {
                        float h01 = Frac(k * 0.137f + t * 0.012f + s.r);
                        float ang = k * 2.39996f + t * 0.05f;
                        float rr = s.r * 0.75f * Mathf.Sqrt(Frac(k * 0.618f));
                        var p = s.basePos + s.axis * (0.3f + h01 * 7f) + new Vector3(Mathf.Cos(ang) * rr, 0, Mathf.Sin(ang) * rr);
                        float tw = 0.5f + 0.5f * Mathf.Sin(t * 1.3f + k * 1.7f);
                        dustM[nd++] = Matrix4x4.TRS(p, camRot, Vector3.one * (0.07f + 0.05f * tw) * Mathf.Sin(h01 * Mathf.PI));
                    }
            }
            if (dust && nd > 0) Graphics.RenderMeshInstanced(new RenderParams(dust) { camera = cam, shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = false }, quad, 0, dustM, nd);

            int n = 0, np = 0, ns = 0;
            if (wispHalo)
            {
                foreach (var w in wisps)
                {
                    float s0 = w.seed;
                    var p = w.anchor + new Vector3(Mathf.Sin(t * 0.21f + s0) * w.roam, 1.1f + 0.35f * Mathf.Sin(t * 0.9f + s0 * 1.3f), Mathf.Sin(t * 0.17f + s0 * 2.1f) * w.roam);
                    float fl = 0.82f + 0.12f * Mathf.Sin(t * 7.3f + s0) + 0.06f * Mathf.Sin(t * 17.1f + s0 * 3f);
                    haloM[n] = Matrix4x4.TRS(p, camRot, Vector3.one * 2.0f * fl);
                    coreM[n] = Matrix4x4.TRS(p + Vector3.up * 0.05f, camRot, new Vector3(0.36f, 0.56f, 1f) * fl);
                    flameM[n] = Matrix4x4.TRS(p + camRot * Vector3.up * 0.32f * fl, camRot * Quaternion.Euler(0, 0, 180f + 8f * Mathf.Sin(t * 3.1f + s0)), new Vector3(0.5f, 1.5f, 1f) * fl);
                    for (int k = 0; k < 2 && ns < sparkM.Length; k++)
                    {
                        // two sparks drifting up off the flame and dying out
                        float ph = Frac(t * 0.55f + k * 0.5f + s0 * 0.37f);
                        var sp = p + Vector3.up * (0.35f + ph * 1.5f) + new Vector3(Mathf.Sin(ph * 6f + s0 + k) * 0.25f, 0, Mathf.Cos(ph * 5f + s0) * 0.2f);
                        sparkM[ns++] = Matrix4x4.TRS(sp, camRot, Vector3.one * 0.09f * (1f - ph) * fl);
                    }
                    n++;
                    if (wispPool && np < poolM.Length) poolM[np++] = Matrix4x4.TRS(new Vector3(p.x, w.anchor.y, p.z), Quaternion.identity, new Vector3(3f, 4f, 3f));
                }
                foreach (var lp in lanterns)
                {
                    if (n >= MaxInst) break;
                    float fl = 0.88f + 0.08f * Mathf.Sin(t * 9.1f + lp.x) + 0.04f * Mathf.Sin(t * 23f + lp.z);
                    haloM[n] = Matrix4x4.TRS(lp, camRot, Vector3.one * 2.6f * fl * look.lanternGlow);
                    n++;
                }
                int nw = wisps.Count;
                if (nw > 0)
                {
                    Graphics.RenderMeshInstanced(new RenderParams(wispHalo) { camera = cam, shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = false }, quad, 0, haloM, nw);
                    Graphics.RenderMeshInstanced(new RenderParams(wispCore) { camera = cam, shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = false }, quad, 0, coreM, nw);
                    if (ns > 0) Graphics.RenderMeshInstanced(new RenderParams(wispCore) { camera = cam, shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = false }, quad, 0, sparkM, ns);
                    Graphics.RenderMeshInstanced(new RenderParams(wispFlame) { camera = cam, shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = false }, quad, 0, flameM, nw);
                }
                if (n > nw)
                {
                    // lantern halos and a small hot core in the firebox
                    // no per-frame arrays: the halos are the tail of haloM, the cores a scaled copy in a kept array
                    int nl = n - nw;
                    Graphics.RenderMeshInstanced(new RenderParams(lanternHalo) { camera = cam, shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = false }, quad, 0, haloM, nl, nw);
                    for (int i = 0; i < nl; i++) lanternCoreM[i] = haloM[nw + i] * Matrix4x4.Scale(new Vector3(0.22f, 0.22f, 1f));
                    Graphics.RenderMeshInstanced(new RenderParams(lanternCore) { camera = cam, shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = false }, quad, 0, lanternCoreM, nl);
                }
                if (np > 0) Graphics.RenderMeshInstanced(new RenderParams(wispPool) { camera = cam, shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = false }, cube, 0, poolM, np);
            }
        }

        static float Frac(float x) => x - Mathf.Floor(x);

        static bool NearWater(Vector3 p, float floorY)
        {
            if (p.y < floorY - 0.6f) return true;
            foreach (var o in new[] { Vector3.zero, new Vector3(4, 0, 0), new Vector3(-4, 0, 0), new Vector3(0, 0, 4), new Vector3(0, 0, -4) })
                foreach (var h in Physics.RaycastAll(p + o + Vector3.up * 30f, Vector3.down, 60f, ~0, QueryTriggerInteraction.Collide))
                {
                    string nm = h.collider.name.ToLowerInvariant();
                    if (nm.Contains("water") || nm.Contains("river") || nm.Contains("stream")) return true;
                }
            return false;
        }

        // open cone along +Y (0..1), radius 1 at the bottom, topR at the top, uv.x around, uv.y along
        static Mesh MakeCone(int seg, float topR)
        {
            var v = new List<Vector3>(); var nrm = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                v.Add(d); v.Add(d * topR + Vector3.up);
                var nn = (d + Vector3.up * (1f - topR)).normalized;
                nrm.Add(nn); nrm.Add(nn);
                uv.Add(new Vector2(i / (float)seg, 0)); uv.Add(new Vector2(i / (float)seg, 1));
                if (i < seg) { int b = i * 2; tri.AddRange(new[] { b, b + 1, b + 2, b + 2, b + 1, b + 3 }); }
            }
            var m = new Mesh { name = "StageAccents cone", hideFlags = HideFlags.DontSave };
            m.SetVertices(v); m.SetNormals(nrm); m.SetUVs(0, uv); m.SetTriangles(tri, 0);
            m.bounds = new Bounds(new Vector3(0, 0.5f, 0), new Vector3(2.2f, 1.2f, 2.2f));
            return m;
        }

        static Mesh MakeCube() => Resources.GetBuiltinResource<Mesh>("Cube.fbx");
    }
}
