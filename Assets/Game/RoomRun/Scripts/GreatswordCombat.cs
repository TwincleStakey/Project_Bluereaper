// Greatsword player (temporary look test, 11-13): hack-and-slash combo on the Mixamo greatsword clips.
// Fire = swing. Four-step chain (three combo slashes, then a 360 spin finisher), fast playback, early cancel window,
// a short ground lunge per swing, a wide frontal sector hit (no projectiles), hit-stop, camera shake, the Free Slash VFX
// sized to the sector, and grass/reeds cut through VegetationCut. (Hops while moving were the robe's leg colliders
// catching the ground raycast; see LSRobeCloth.)
using System.Collections.Generic;
using UnityEngine;

namespace RoomRun
{
    public sealed class GreatswordCombat : MonoBehaviour
    {
        [System.Serializable]
        public struct Step
        {
            public string state;
            public float hitAt;        // normalized clip time of the hit
            public float cancelAt;     // the next swing may start from here
            public float radius;       // metres from the player
            public float halfAngle;    // degrees each side of the aim (180 = all round)
            public int damage;
            public float lunge;        // m/s forward before the hit
            public float vfxScale;
            public float vfxRoll;      // 180 = mirrored arc (the swing comes from the other side)
            public bool finisher;
        }

        public Step[] steps =
        {
            // sideways sweeps, alternating (A and C one way, B mirrored); hit at the hand-speed peak of each clip
            new Step { state = "SweepA", hitAt = 0.42f, cancelAt = 0.62f, radius = 4.6f, halfAngle = 80f, damage = 3, lunge = 7f, vfxScale = 4.6f, vfxRoll = 0f },
            new Step { state = "SweepB", hitAt = 0.42f, cancelAt = 0.62f, radius = 4.6f, halfAngle = 80f, damage = 3, lunge = 6f, vfxScale = 4.6f, vfxRoll = 180f },
            new Step { state = "SweepC", hitAt = 0.45f, cancelAt = 0.62f, radius = 5.0f, halfAngle = 90f, damage = 4, lunge = 7f, vfxScale = 5.0f, vfxRoll = 0f },
            new Step { state = "Spin",   hitAt = 0.58f, cancelAt = 0.88f, radius = 5.6f, halfAngle = 180f, damage = 6, lunge = 3f, vfxScale = 5.6f, vfxRoll = 0f, finisher = true },
        };
        public float attackSpeed = 1.75f;     // clip playback while swinging
        public float moveWhileSwing = 0.2f;   // fraction of move speed kept during a swing
        public float lungeWindow = 0.22f;     // normalized time before the hit that the lunge pushes
        public float hitStop = 0.055f;        // real seconds of near-freeze on a connecting hit
        public float comboResetTime = 0.45f;
        public float vfxHeight = 1.0f;

        public bool Attacking => step >= 0;
        public Vector3 LungeVelocity { get; private set; }

        PlayerController pc;
        RoomRunConfig cfg;
        Animator anim;
        Transform model;
        Transform[] feet;
        int atkLayer = -1;
        int step = -1;
        float t, len, sinceEnd = 99f, layerW, hitStopLeft;
        bool hitDone, buffered, vfxDone;
        int lastStep = -1;
        readonly Dictionary<string, float> clipLen = new Dictionary<string, float>();
        readonly Collider[] overlap = new Collider[96];
        readonly HashSet<Health> struck = new HashSet<Health>();
        readonly List<ParticleSystem> vfxPool = new List<ParticleSystem>(), vfxBigPool = new List<ParticleSystem>();
        CutDebris debris;
        Vector3 aim = Vector3.forward;

        static readonly int HMoveX = Animator.StringToHash("MoveX"), HMoveZ = Animator.StringToHash("MoveZ"),
            HLoco = Animator.StringToHash("LocoSpeed"), HAtk = Animator.StringToHash("AtkSpeed");

        public void Setup(PlayerController owner, RoomRunConfig config, Transform modelRoot, Animator animator)
        {
            pc = owner; cfg = config; model = modelRoot; anim = animator;
            atkLayer = anim.GetLayerIndex("AttackFull");
            clipLen.Clear();
            if (anim.runtimeAnimatorController)
                foreach (var c in anim.runtimeAnimatorController.animationClips) clipLen[c.name] = c.length;
            feet = new[]
            {
                anim.GetBoneTransform(HumanBodyBones.LeftFoot), anim.GetBoneTransform(HumanBodyBones.RightFoot),
                anim.GetBoneTransform(HumanBodyBones.LeftToes), anim.GetBoneTransform(HumanBodyBones.RightToes),
            };
            step = -1; layerW = 0f;
            BuildPools();
        }

        void OnDisable()
        {
            if (hitStopLeft > 0f) { Time.timeScale = 1f; hitStopLeft = 0f; }
        }

        void OnDestroy()
        {
            foreach (var p in vfxPool) if (p) Destroy(p.gameObject);
            foreach (var p in vfxBigPool) if (p) Destroy(p.gameObject);
            if (debris) Destroy(debris.gameObject);
        }

        float LowestFoot()
        {
            float y = float.MaxValue;
            foreach (var f in feet) if (f) y = Mathf.Min(y, f.position.y);
            return y == float.MaxValue ? model.position.y : y;
        }

        public void Cancel()
        {
            if (step < 0) return;
            step = -1; sinceEnd = 99f; buffered = false;
            LungeVelocity = Vector3.zero;
        }

        string ClipOf(string state) => state switch
        {
            "SweepA" => "GS_SweepA", "SweepB" => "GS_SweepB", "SweepC" => "GS_SweepC", "Spin" => "GS_SpinAttack", _ => state,
        };

        /// Raised when a swing starts so the body snaps to the cursor before the blade moves.
        public event System.Action SwingStarted;

        void StartStep(int i)
        {
            step = i; t = 0f; hitDone = false; buffered = false; vfxDone = false; lastStep = i;
            clipLen.TryGetValue(ClipOf(steps[i].state), out len);
            if (len <= 0f) len = 1f;
            anim.CrossFadeInFixedTime(steps[i].state, 0.05f, atkLayer, 0f);
            SwingStarted?.Invoke();
        }

        /// Called from PlayerController.Update. moveLocal = velocity / move speed in world space.
        public void Tick(float dt, bool fireHeld, bool canAct, Vector3 aimDir, Vector3 vel, float moveSpeed, bool rolling)
        {
            if (!anim) return;
            if (aimDir.sqrMagnitude > 0.01f) aim = aimDir;

            // hit-stop runs on real time
            if (hitStopLeft > 0f)
            {
                hitStopLeft -= Time.unscaledDeltaTime;
                if (hitStopLeft <= 0f) Time.timeScale = 1f;
            }

            if (fireHeld && canAct) buffered = true;
            if (rolling) Cancel();
            if (!canAct) { buffered = false; if (step >= 0) Cancel(); }

            if (step < 0)
            {
                sinceEnd += dt;
                if (buffered && canAct && !rolling) StartStep(sinceEnd < comboResetTime ? (lastStep + 1) % steps.Length : 0);
            }
            else
            {
                var s = steps[step];
                t += dt * attackSpeed / len;
                if (!hitDone && t >= s.hitAt) { hitDone = true; DoHit(s); }
                if (!vfxDone && t >= s.hitAt - 0.12f) { vfxDone = true; PlayVfx(s); }
                if (t >= s.cancelAt && buffered && !rolling)
                {
                    int next = step + 1 < steps.Length ? step + 1 : 0;
                    StartStep(next);
                }
                else if (t >= 1f) { step = -1; sinceEnd = 0f; }
            }

            // lunge: a short forward push on the ground just before the hit
            LungeVelocity = Vector3.zero;
            if (step >= 0)
            {
                var s = steps[step];
                if (t > s.hitAt - lungeWindow && t < s.hitAt) LungeVelocity = aim * s.lunge;
            }

            layerW = Mathf.MoveTowards(layerW, step >= 0 ? 1f : 0f, (step >= 0 ? 25f : 5f) * dt);
            if (atkLayer >= 0) anim.SetLayerWeight(atkLayer, layerW);
            anim.SetFloat(HAtk, attackSpeed);

            // legs: velocity in facing space, walk clips at 0.35, runs at 1
            var local = Quaternion.Inverse(Quaternion.LookRotation(aim)) * (vel / Mathf.Max(0.01f, moveSpeed));
            anim.SetFloat(HMoveX, local.x, 0.08f, dt);
            anim.SetFloat(HMoveZ, local.z, 0.08f, dt);
            anim.SetFloat(HLoco, 1.15f);
        }

        void PlayVfx(Step s)
        {
            var pool = s.finisher && vfxBigPool.Count > 0 ? vfxBigPool : vfxPool;
            if (pool.Count == 0) return;
            ParticleSystem ps = null;
            pool.RemoveAll(p => !p);
            if (pool.Count == 0) return;
            foreach (var p in pool) if (!p.IsAlive(true)) { ps = p; break; }
            if (!ps) ps = pool[0];
            var tr = ps.transform;
            // GS_SlashWater_Flat (Free Slash VFX Slash Water, Local-aligned copy): the arc mesh already lies in the prefab's
            // XZ plane; at t 0.2 it spans ~300 deg starting behind-left, so +45 about Y centres the swept part on the aim (ls_cmd rotprobe / waterflat views;
            // the old -90 about X stood it up as a wall). Mirror = flip local Z, which is the world axis across the aim.
            // ~1 m across at scale 1, so the scale is the hit radius.
            tr.position = pc.transform.position + Vector3.up * vfxHeight;
            tr.rotation = Quaternion.LookRotation(aim) * Quaternion.Euler(0, 45f, 0);
            tr.localScale = new Vector3(s.vfxScale, s.vfxScale, s.vfxRoll > 90f ? -s.vfxScale : s.vfxScale);
            ps.Clear(true);
            ps.Play(true);
            if (s.finisher)
            {
                // the finisher is all round: a second arc behind
                var back = pool.Count > 1 ? pool[pool.IndexOf(ps) == 0 ? 1 : 0] : null;
                if (back)
                {
                    back.transform.position = pc.transform.position + Vector3.up * vfxHeight;
                    back.transform.rotation = Quaternion.LookRotation(-aim) * Quaternion.Euler(0, 45f, 0);
                    back.transform.localScale = Vector3.one * s.vfxScale;
                    back.Clear(true); back.Play(true);
                }
            }
        }

        void DoHit(Step s)
        {
            var center = pc.transform.position;
            struck.Clear();
            float cosHalf = Mathf.Cos(Mathf.Min(s.halfAngle, 180f) * Mathf.Deg2Rad);
            int n = Physics.OverlapSphereNonAlloc(center + Vector3.up, s.radius + 0.6f, overlap, 1 << Ground.ActorLayer, QueryTriggerInteraction.Collide);
            int hits = 0;
            for (int i = 0; i < n; i++)
            {
                var h = overlap[i].GetComponentInParent<Health>();
                if (!h || h.team == Team.Player || !h.Alive || struck.Contains(h)) continue;
                var d = h.transform.position - center; d.y = 0;
                float dist = d.magnitude;
                if (dist > s.radius + 0.6f) continue;
                if (s.halfAngle < 180f && dist > 0.6f && Vector3.Dot(d / dist, aim) < cosHalf) continue;
                struck.Add(h);
                if (h.Hit(s.damage))
                {
                    hits++;
                    Fx.Burst(h.transform.position + Vector3.up, Fx.Kind.EnemySpark, 10);
                }
            }
            var rig = RunDirector.Instance ? RunDirector.Instance.Rig : null;
            if (rig) rig.Shake(s.finisher ? 0.5f : hits > 0 ? 0.3f : 0.14f);
            if (hits > 0 && hitStop > 0f)
            {
                hitStopLeft = hitStop * (s.finisher ? 1.6f : 1f);
                Time.timeScale = 0.05f;
            }
            if (VegetationCut.Instance) VegetationCut.Instance.Sector(center, aim, s.radius, s.halfAngle);
            EmitBits(center, s);
        }

        // ---------- pools ----------
        void BuildPools()
        {
            void Fill(GameObject prefab, List<ParticleSystem> pool, int count)
            {
                if (!prefab) return;
                for (int i = 0; i < count; i++)
                {
                    var go = Instantiate(prefab);
                    go.name = prefab.name + " (pool)";
                    DontDestroyOnLoad(go);   // fields load as scenes; the pool lives as long as the player
                    var ps = go.GetComponent<ParticleSystem>() ?? go.GetComponentInChildren<ParticleSystem>();
                    if (!ps) { Destroy(go); return; }
                    foreach (var p in go.GetComponentsInChildren<ParticleSystem>(true))
                    {
                        var main = p.main; main.playOnAwake = false;
                        var pr = p.GetComponent<ParticleSystemRenderer>();
                        var m = pr ? pr.sharedMaterial : null;
                        if (m && m.shader && m.shader.name.Contains("Slash World"))
                        {
                            // ground slash marks: that shader graph renders magenta in Unity 6 here; leave them out
                            p.gameObject.SetActive(false);
                            continue;
                        }
                        if (m && m.shader && m.shader.name.Contains("Trail"))
                        {
                            // the pack's Trail shader graph renders magenta in Unity 6: same texture on URP's particle shader
                            pr.sharedMaterial = SparkMaterial(m);
                            continue;   // sparks keep their own (small) scaling
                        }
                        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                    }
                    foreach (var l in go.GetComponentsInChildren<Light>(true)) l.shadows = LightShadows.None;
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    pool.Add(ps);
                }
            }
            Fill(cfg.slashVfx, vfxPool, 3);
            Fill(cfg.slashVfxFinisher, vfxBigPool, 2);

            {
                var go = new GameObject("GS Cut Debris");
                DontDestroyOnLoad(go);
                debris = go.AddComponent<CutDebris>();
            }
        }

        static readonly Dictionary<Material, Material> sparkMats = new Dictionary<Material, Material>();
        static Material SparkMaterial(Material src)
        {
            if (sparkMats.TryGetValue(src, out var m) && m) return m;
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            m = new Material(sh) { name = src.name + " (URP)" };
            Texture tex = null;
            foreach (var n in src.GetTexturePropertyNames()) { tex = src.GetTexture(n); if (tex) break; }
            if (tex) m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", new Color(1.6f, 1.2f, 2.2f, 1f));
            // additive transparent
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 2f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = 3000;
            sparkMats[src] = m;
            return m;
        }

        // Cut debris only where the swing actually cut grass or reeds (VegetationQuery over the instanced plants), from those
        // plants, in their colour: green for grass, dry straw for reeds. Nothing in grass-less stages.
        readonly List<VegetationQuery.Hit> vegHits = new List<VegetationQuery.Hit>();
        void EmitBits(Vector3 center, Step s)
        {
            if (!debris) return;
            int max = s.finisher ? 40 : 28;
            if (VegetationQuery.Collect(center, aim, s.radius, s.halfAngle, vegHits, max) == 0) return;
            debris.Spawn(vegHits, center, aim, s.vfxRoll > 90f ? -1f : 1f);
        }

        // Debug trace (set by RoomRunRemote's 8-direction test): one CSV row per frame.
        public static string TracePath;
        System.IO.StreamWriter trace;
        void Trace()
        {
            if (string.IsNullOrEmpty(TracePath)) { if (trace != null) { trace.Dispose(); trace = null; } return; }
            if (trace == null) { trace = new System.IO.StreamWriter(TracePath, false); trace.WriteLine("t,playerY,modelLocalY,hipsAbovePlayer,footAbovePlayer,moveX,moveZ,velX,velZ,attacking,dt"); }
            var hips = anim.GetBoneTransform(HumanBodyBones.Hips);
            float py = pc.transform.position.y;
            var v = pc.Velocity;
            trace.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0.000},{1:0.0000},{2:0.0000},{3:0.0000},{4:0.0000},{5:0.00},{6:0.00},{7:0.00},{8:0.00},{9},{10:0.0000}",
                Time.time, py, model.localPosition.y, hips.position.y - py, LowestFoot() - py, anim.GetFloat(HMoveX), anim.GetFloat(HMoveZ), v.x, v.z, step, Time.deltaTime));
        }

        void LateUpdate()
        {
            if (anim && model) Trace();
        }
    }
}
