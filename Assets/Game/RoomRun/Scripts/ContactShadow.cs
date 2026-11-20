// 11-19 ShadowPass contact shadow (Kido approved "발밑 접지 그림자 추가"): a soft dark ellipse on the ground under the
// player, every enemy and the bosses, so they sit on the floor even where the real sun shadow is washed out (snow /
// grass / 저승 / valley). Part of ShadowPass: it only runs while ShadowPass is on, and RR.ContactShadow (1 by default)
// switches just this part ("call RoomRun.ContactShadow.Off|On", shots token "off=ContactShadow").
//   draw   - a small depth-projected decal (follows the bumpy terrain mesh, skips bodies / walls / props).
//   look   - plain radial falloff, darkness ~0.55 at the centre, very soft rim (RR_ContactShadow.shader, multiply only:
//            the floor's brightness and colour are untouched outside the ellipse, 저승's floor can only go darker).
//   size   - ~1.25 x the actor's collider diameter (≈ 1.4 x the stance width); a big actor (boss) gets one sized to its
//            body. Pushed 0.15 m along the sun's ground direction. In the air it grows and fades with the height.
//   ground - a short ray down from the actor every 4th frame (staggered) gives the floor height and slope; no hit (fields
//            without floor colliders) falls back to the actor's own feet, so it never floats on water / fog / grass tops.
//   cost   - one quad mesh, one material, one instanced draw per 1023 actors, issued for the game camera only.
// Tune (editor): ArtSource/ShadowPass/tune.txt keys cs (darkness), css (size x), cso (sun offset m), csl (height fade m).
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RoomRun
{
    [DisallowMultipleComponent]
    public sealed class ContactShadow : MonoBehaviour
    {
        public const string Key = "RR.ContactShadow";
        public static bool Enabled => PlayerPrefs.GetInt(Key, 1) != 0;
        public static void On() { PlayerPrefs.SetInt(Key, 1); PlayerPrefs.Save(); Kick(); }
        public static void Off() { PlayerPrefs.SetInt(Key, 0); PlayerPrefs.Save(); Kick(); }
        static void Kick() { var sp = FindAnyObjectByType<ShadowPass>(); if (sp) sp.Reapply(); }   // ShadowPass adds / removes this part

        public float darkness = 0.55f;   // centre darkness (0..1)
        public float size = 1f;          // x the default footprint
        public float sunOffset = 0.15f;  // m along the sun's ground direction
        public float liftRange = 2.5f;   // height at which it reaches its widest / faintest

        const float FootK = 1.25f;       // footprint diameter = collider diameter x this

        struct Foot { public float diameter, aspect, groundY; public Vector3 normal; public bool hasGround; public int seen; }
        readonly Dictionary<int, Foot> feet = new Dictionary<int, Foot>();
        Matrix4x4[] mats = new Matrix4x4[256];
        int count, frameSeen, noteAt;
        Mesh quad; Material mat;
        static readonly RaycastHit[] hits = new RaycastHit[4];

        public static string LastNote = "";

        void OnEnable()
        {
            noteAt = Time.frameCount + 60;
            if (!quad) quad = BuildQuad();
            if (!mat)
            {
                var sh = Resources.Load<Shader>("RR_ContactShadow");
                if (sh) mat = new Material(sh) { enableInstancing = true, hideFlags = HideFlags.DontSave, name = "RR_ContactShadow (runtime)" };
                else Debug.LogWarning("[ContactShadow] shader RR_ContactShadow missing");
            }
            RenderPipelineManager.beginCameraRendering += Draw;
        }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= Draw;
            count = 0;
        }

        void OnDestroy()
        {
            if (mat) Destroy(mat);
            if (quad) Destroy(quad);
        }

        static Mesh BuildQuad()
        {
            var m = new Mesh { name = "RR_ContactShadowQuad", hideFlags = HideFlags.DontSave };
            m.vertices = new[] { new Vector3(-0.5f, 0, -0.5f), new Vector3(0.5f, 0, -0.5f), new Vector3(0.5f, 0, 0.5f), new Vector3(-0.5f, 0, 0.5f) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            m.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            m.bounds = new Bounds(Vector3.zero, new Vector3(1, 0.1f, 1));
            return m;
        }

        Foot Measure(Component actor)
        {
            var f = new Foot { normal = Vector3.up, aspect = 1f };
            var cc = actor.GetComponent<CharacterController>();
            var ls = actor.transform.lossyScale;
            float r = cc ? cc.radius * Mathf.Max(Mathf.Abs(ls.x), Mathf.Abs(ls.z)) : 0.38f;
            f.diameter = 2f * r * FootK;
            // a big body (boss, quadruped): size the ellipse to the body seen from above
            Bounds b = default; bool any = false;
            foreach (var rd in actor.GetComponentsInChildren<Renderer>())
            {
                if (!rd.enabled || rd is ParticleSystemRenderer || rd is TrailRenderer || rd is LineRenderer) continue;
                if (!any) { b = rd.bounds; any = true; } else b.Encapsulate(rd.bounds);
            }
            if (any)
            {
                float lo = Mathf.Min(b.size.x, b.size.z), hi = Mathf.Max(b.size.x, b.size.z);
                if (lo > 2.2f * f.diameter && b.size.y > 1.6f)
                {
                    f.diameter = Mathf.Max(f.diameter, lo * 0.7f);
                    f.aspect = Mathf.Clamp(hi * 0.7f / f.diameter, 1f, 1.8f);
                }
            }
            return f;
        }

        void LateUpdate()
        {
            count = 0;
            var run = RunDirector.Instance;
            if (!run || run.Transitioning || !mat) return;
            frameSeen = Time.frameCount;
            Vector3 sunDir = Vector3.zero;
            var sun = RenderSettings.sun;
            if (sun && sun.isActiveAndEnabled)
            {
                var d = sun.transform.forward; d.y = 0f;
                if (d.sqrMagnitude > 1e-4f) sunDir = d.normalized * sunOffset;
            }
            int c0 = count;
            if (run.Player && run.Player.gameObject.activeInHierarchy) Add(run.Player, sunDir);
            if (Time.frameCount == noteAt && count > c0 && feet.TryGetValue(run.Player.GetInstanceID(), out var pf))
                ShadowPass.Log(LastNote = $"[ContactShadow] {(run.Room != null ? run.Room.id : "?")}: player foot {pf.diameter:0.00} m x{pf.aspect:0.0}, ground {pf.groundY:0.00} feet {run.Player.transform.position.y:0.00} n {pf.normal}, sun {sunDir}, actors {run.Enemies.Count + 1}, mat {(mat ? mat.shader.name : "-")}");
            var es = run.Enemies;
            for (int i = 0; i < es.Count; i++) { var e = es[i]; if (e && e.gameObject.activeInHierarchy) Add(e, sunDir); }   // activeInHierarchy: capture poses disable the controller, not the body
            // forget the dead every few seconds
            if (Time.frameCount % 300 == 0 && feet.Count > 4 * (es.Count + 4))
            {
                var stale = new List<int>();
                foreach (var kv in feet) if (Time.frameCount - kv.Value.seen > 120) stale.Add(kv.Key);
                foreach (var k in stale) feet.Remove(k);
            }
        }

        void Add(Component actor, Vector3 sunDir)
        {
            int id = actor.GetInstanceID();
            if (!feet.TryGetValue(id, out var f)) { f = Measure(actor); f.hasGround = false; }
            var t = actor.transform;
            Vector3 p = t.position;
            // floor height and slope: every 4th frame per actor, staggered; first sighting at once
            if (!f.hasGround || ((id + Time.frameCount) & 3) == 0)
            {
                f.hasGround = true; f.groundY = p.y; f.normal = Vector3.up;
                int n = Physics.RaycastNonAlloc(p + Vector3.up * 0.5f, Vector3.down, hits, 8f, Ground.GroundMask, QueryTriggerInteraction.Ignore);
                float best = float.MinValue;
                for (int i = 0; i < n; i++)
                {
                    var h = hits[i];
                    if (h.collider.transform.IsChildOf(t)) continue;
                    if (h.point.y > best) { best = h.point.y; f.normal = h.normal.y > 0.5f ? h.normal : Vector3.up; }
                }
                // a floor collider at most a little above the feet counts; nothing found = the feet are the floor
                if (best > float.MinValue && best < p.y + 0.3f) f.groundY = best; else f.normal = Vector3.up;
            }
            f.seen = Time.frameCount;
            feet[id] = f;

            float h01 = liftRange > 0.01f ? Mathf.Clamp01((p.y - f.groundY) / liftRange) : 0f;
            float a = darkness * (1f - 0.75f * h01);
            if (a < 0.01f) return;
            float w = f.diameter * size * (1f + 0.4f * h01);
            var fwd = Vector3.ProjectOnPlane(t.forward, f.normal);
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.ProjectOnPlane(Vector3.forward, f.normal);
            var rot = Quaternion.LookRotation(fwd.normalized, f.normal);
            var pos = new Vector3(p.x + sunDir.x, f.groundY, p.z + sunDir.z);
            if (count == mats.Length) System.Array.Resize(ref mats, mats.Length * 2);
            mats[count++] = Matrix4x4.TRS(pos, rot, new Vector3(w, a, w * f.aspect));
        }

        void Draw(ScriptableRenderContext ctx, Camera cam)
        {
            var run = RunDirector.Instance;
            if (count <= 0 || !mat || !run || !run.Rig || cam != run.Rig.Cam) return;
            var rp = new RenderParams(mat) { camera = cam, layer = 0, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false, worldBounds = new Bounds(run.Player ? run.Player.transform.position : Vector3.zero, Vector3.one * 400f) };
            for (int s = 0; s < count; s += 1023) Graphics.RenderMeshInstanced(rp, quad, 0, mats, Mathf.Min(1023, count - s), s);
        }
    }
}
