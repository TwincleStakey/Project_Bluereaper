// 11-19 sword wave (Kido: "검기도 날려야 돼서 일반 3타 공격에 괜찮은 vfx 검기형 ... 좀 크고 넓게 날아가야돼").
// Each greatsword hit (1, 2, 3) launches a flying crescent from the blade's height toward the aim. The look is the
// asset's: Free Slash VFX "Slash Projectile VFX Water" (Asset Store 309498), copied to Resources/GS_SwordWave.prefab with
// its two missing-script components stripped (the pack's mover/decal scripts were never imported). Nothing is drawn by
// code here: this only pools the prefab, fixes the pack's Unity 6 material problems the same way GreatswordCombat does
// for the slash arcs (Slash World graph -> off, Trail graph -> URP particle copy, Slash graph -> moderate gain), moves it,
// fades it out and hits enemies it passes through (each enemy once per wave).
// Direction: the crescent is mirrored to the swing (L->R / R->L, from GreatswordCombat.RestSide after the step starts).
// Hit 3 (the 360 spin) sends one bigger wave forward (SpinScale); SpinRing = true sends SpinRingCount waves all round.
// Toggle: SwordWave.Enabled (PlayerPrefs "RR.SwordWave", default 1).
using System.Collections.Generic;
using UnityEngine;

namespace RoomRun
{
    public sealed class SwordWave : MonoBehaviour
    {
        public static bool Enabled
        {
            get => PlayerPrefs.GetInt("RR.SwordWave", 1) == 1;
            set => PlayerPrefs.SetInt("RR.SwordWave", value ? 1 : 0);
        }

        // tuning (world metres / seconds; Scale and Height are multiplied by RoomRunConfig.playerSize)
        public static float Scale = 2.2f;          // prefab scale; the pack's crescent is ~2.2 m wide at 1 -> ~7 m at playerSize 1.5
        public static float SpinScale = 1.35f;     // hit 3 wave relative to Scale
        public static float Height = 1.15f;        // blade height, just under the shoulder (x playerSize)
        public static float Forward = 1.0f;        // spawn this far in front of the player
        public static float Speed = 24f;           // m/s
        public static float Range = 10f;           // flight distance before it fades
        public static float FadeTime = 0.22f;      // shrink-out after Range
        public static float HitHalfWidth = 0.42f;  // fraction of the visual width that hits (each side)
        public static float HitRadius = 0.9f;
        public static float DamageScale = 0.5f;    // of the swing's damage
        public static float Tilt = 0f;             // extra pitch (deg) if the crescent needs tilting toward the camera
        public static bool MirrorBase = false;     // flip if the pack's crescent sweeps the other way
        public static bool SpinRing = false;
        public static int SpinRingCount = 6;
        public static float SoulGain = 2.6f;       // brighter than the arc (2.2): weapons / waves may be bright

        const string PrefabPath = "GS_SwordWave";
        static GameObject prefab;
        static readonly List<SwordWave> pool = new List<SwordWave>();
        static readonly Collider[] overlap = new Collider[64];

        ParticleSystem ps;
        Vector3 dir;
        float travelled, fade, baseScale;
        bool flying, mirror;
        int damage;
        readonly HashSet<Health> struck = new HashSet<Health>();

        /// Called by GreatswordCombat at the swing's contact frame.
        /// side: +1 = the swing ends on the right (L->R), -1 = ends on the left (R->L). spin = the 360 third hit.
        public static void Fire(Vector3 playerPos, Vector3 aim, int side, bool spin, int swingDamage, float playerSize)
        {
            if (!Enabled) return;
            aim.y = 0f;
            if (aim.sqrMagnitude < 1e-4f) return;
            aim.Normalize();
            int dmg = Mathf.Max(1, Mathf.RoundToInt(swingDamage * DamageScale));
            float s = Scale * playerSize * (spin ? SpinScale : 1f);
            if (spin && SpinRing)
            {
                for (int i = 0; i < SpinRingCount; i++)
                    Launch(playerPos, Quaternion.Euler(0f, 360f * i / SpinRingCount, 0f) * aim, side > 0, s * 0.8f, dmg, playerSize);
                return;
            }
            Launch(playerPos, aim, spin || side > 0, s, dmg, playerSize);   // the spin turns clockwise from above: crosses the front L->R (blade log)
        }

        static void Launch(Vector3 playerPos, Vector3 dir, bool lToR, float scale, int dmg, float playerSize)
        {
            var w = Get();
            if (!w) return;
            w.dir = dir;
            w.travelled = 0f; w.fade = 0f; w.flying = true; w.damage = dmg; w.baseScale = scale;
            w.mirror = lToR ^ MirrorBase;   // in game (capture swave2 + fire log) the unrolled crescent appears on the RIGHT and grows left = an R->L swing; L->R is rolled
            w.struck.Clear();
            var tr = w.transform;
            tr.position = playerPos + Vector3.up * (Height * playerSize) + dir * (Forward * playerSize);
            // mirror = roll 180 about the flight axis: the crescent is flat and two-sided, so seen from above it sweeps the other way
            // (a negative X scale did not flip the pack's mesh particles: hit 2 still grew from the left, capture legs 11-19 17:03)
            tr.rotation = Quaternion.LookRotation(dir) * Quaternion.Euler(Tilt, 0f, w.mirror ? 180f : 0f);
            w.ApplyScale(1f);
            w.gameObject.SetActive(true);
            w.ps.Clear(true);
            w.ps.Play(true);
            if (LogLeft-- > 0) Debug.Log($"[SwordWave] fire lToR={lToR} mirror={w.mirror} rot={tr.rotation.eulerAngles} scale={scale:0.00} pos={tr.position}");
        }
        public static int LogLeft = 12;

        void ApplyScale(float k)
        {
            float s = baseScale * k;
            transform.localScale = new Vector3(s, s, s);
        }

        void Update()
        {
            if (!flying) { if (!ps.IsAlive(true)) gameObject.SetActive(false); return; }
            float dt = Time.deltaTime;
            float step = Speed * dt;
            transform.position += dir * step;
            travelled += step;
            HitAlong();
            if (travelled >= Range)
            {
                fade += dt / Mathf.Max(0.01f, FadeTime);
                ApplyScale(Mathf.Lerp(1f, 0.6f, fade));
                if (fade >= 1f)
                {
                    flying = false;
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    gameObject.SetActive(false);
                }
                else if (fade > 0.01f && ps.isEmitting) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        void HitAlong()
        {
            var side = Vector3.Cross(Vector3.up, dir) * (baseScale * HitHalfWidth);
            var c = transform.position; c.y -= 0.5f;
            int n = Physics.OverlapCapsuleNonAlloc(c - side, c + side, HitRadius, overlap, 1 << Ground.ActorLayer, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                var h = overlap[i].GetComponentInParent<Health>();
                if (!h || h.team == Team.Player || !h.Alive || struck.Contains(h)) continue;
                struck.Add(h);
                if (h.Hit(damage)) Fx.Burst(h.transform.position + Vector3.up, Fx.Kind.EnemySpark, 8);
            }
        }

        // ---------- pool ----------
        static SwordWave Get()
        {
            pool.RemoveAll(p => !p);
            foreach (var p in pool) if (!p.flying && !p.gameObject.activeSelf) return p;
            if (pool.Count >= 12) return null;
            if (!prefab) prefab = Resources.Load<GameObject>(PrefabPath);
            if (!prefab) { Debug.LogWarning("[SwordWave] Resources/" + PrefabPath + " missing"); return null; }
            var go = Instantiate(prefab);
            go.name = "GS_SwordWave (pool)";
            DontDestroyOnLoad(go);
            var w = go.AddComponent<SwordWave>();
            w.ps = go.GetComponent<ParticleSystem>();
            Prepare(go);
            go.SetActive(false);
            pool.Add(w);
            return w;
        }

        static void Prepare(GameObject go)
        {
            foreach (var p in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = p.main; main.playOnAwake = false;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                var lm = p.lights; lm.enabled = false;
                var pr = p.GetComponent<ParticleSystemRenderer>();
                var m = pr ? pr.sharedMaterial : null;
                string sh = m && m.shader ? m.shader.name : "";
                // ground marks / puddles / distortion: the wave flies at blade height, nothing lands on the floor.
                // Slash World and ParallaxOcclusion graphs also render magenta in Unity 6 here.
                // Trails: the pack's long streaks sit ~4 units behind the crescent; scaled to the wave they drew a white plume
                // 13 m long over the player (capture legs 17:03) -> off. Flying / Falling Droplets (stretched sparks, world space) piled
                // into a white clump over the hero at this scale (swave1 17:10) -> off too: only the crescent flies
                if (p.gameObject.name.StartsWith("Decal") || p.gameObject.name.StartsWith("Puddle") || p.gameObject.name.StartsWith("Trails") || p.gameObject.name.StartsWith("Flying") || p.gameObject.name.StartsWith("Falling") || sh.Contains("Slash World") || sh.Contains("Parallax"))
                {
                    p.gameObject.SetActive(false);
                    continue;
                }
                if (sh.Contains("Trail")) { pr.sharedMaterial = SparkMaterial(m); continue; }
                if (sh.Contains("Slash") && m.HasProperty("_Color")) pr.sharedMaterial = SoulMaterial(m);
            }
            foreach (var l in go.GetComponentsInChildren<Light>(true)) l.enabled = false;
        }

        static readonly Dictionary<Material, Material> mats = new Dictionary<Material, Material>();
        static Material SoulMaterial(Material src)
        {
            if (mats.TryGetValue(src, out var m) && m) return m;
            m = new Material(src) { name = src.name + " (wave)" };
            m.SetColor("_Color", new Color(SoulGain, SoulGain, SoulGain, 1f));
            mats[src] = m;
            return m;
        }

        static Material SparkMaterial(Material src)
        {
            if (mats.TryGetValue(src, out var m) && m) return m;
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            m = new Material(sh) { name = src.name + " (wave URP)" };
            Texture tex = null;
            foreach (var n in src.GetTexturePropertyNames()) { tex = src.GetTexture(n); if (tex) break; }
            if (tex) m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", new Color(0.55f, 1.1f, 1.8f, 1f));
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 2f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = 3000;
            mats[src] = m;
            return m;
        }
    }
}
