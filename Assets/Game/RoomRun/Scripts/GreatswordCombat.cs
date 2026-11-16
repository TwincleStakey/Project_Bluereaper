// Greatsword player (temporary look test, 11-13): hack-and-slash combo on the Mixamo greatsword clips.
// Fire = swing. Four-step chain (three combo slashes, then a 360 spin finisher), fast playback, early cancel window,
// a short ground lunge per swing, a wide frontal sector hit (no projectiles), hit-stop, camera shake, the Free Slash VFX
// sized to the sector, and grass/reeds cut through VegetationCut. (Hops while moving were the robe's leg colliders
// catching the ground raycast; see LSRobeCloth.)
// 11-14 feel pass (Kido: more stylish, faster hack-and-slash): each swing no longer plays at one speed. The wind-up runs
// fast (windupSpeed), the blade's pass at strikeSpeed, then a short pose hold on the impact frame (impactHold, the
// animator only, even on a whiff), then a quick recovery that ends early (endAt) unless the next swing is buffered.
// The body leans into the lunge (BodyLean, applied by PlayerController), a blade trail follows the sword instead of the
// cyan slash sprite (slashVfx off by default), and attacking out of the dodge dash is a long dash slash (dashStep).
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
        // attack out of the dodge dash: a long lunging sweep (the dash's speed carries into it)
        public Step dashStep = new Step { state = "SweepC", hitAt = 0.45f, cancelAt = 0.6f, radius = 5.4f, halfAngle = 75f, damage = 5, lunge = 17f, vfxScale = 5.2f, vfxRoll = 0f };
        public float attackSpeed = 1.75f;     // base clip playback while swinging (the phase multipliers below scale it)
        [Header("Timing (11-14 feel pass)")]
        public float windupSpeed = 2.4f;      // x attackSpeed before the strike: the wind-up is barely seen
        public float strikeSpeed = 1.15f;     // x attackSpeed while the blade passes (strikeLead before the hit .. strikeTail after)
        public float recoverSpeed = 1.7f;     // x attackSpeed after the strike
        public float strikeLead = 0.1f, strikeTail = 0.06f;   // normalized clip time around hitAt
        public float impactHold = 0.05f;      // seconds the pose holds on the hit frame (animator only), even on a whiff
        public float impactHoldHit = 0.04f;   // ... when something was struck (the global hit-stop already freezes it)
        public float endAt = 0.82f;           // the swing ends here (blend back to locomotion) when nothing is buffered
        public float leanDeg = 14f;           // forward lean into the lunge / strike
        public bool slashVfx = false;         // the Free Slash VFX arc (cyan, glaring per Kido 11-13); the blade trail replaces it
        public bool bladeTrail = true;
        public float moveWhileSwing = 0.2f;   // fraction of move speed kept during a swing
        public float lungeWindow = 0.22f;     // normalized time before the hit that the lunge pushes
        public float hitStop = 0.055f;        // real seconds of near-freeze on a connecting hit
        public float comboResetTime = 0.45f;
        public float vfxHeight = 1.0f;

        public bool Attacking => step >= 0;
        public int StepIndex => step;                 // dev captures (v2_check hit-stop log)
        public float StepT => t;
        public float HitStopLeft => hitStopLeft;
        public float HoldLeft => holdLeft;
        public Vector3 LungeVelocity { get; private set; }
        public float BodyLean { get; private set; }   // degrees forward, PlayerController tilts the visual by it

        PlayerController pc;
        RoomRunConfig cfg;
        Animator anim;
        Transform model;
        Transform[] feet;
        int atkLayer = -1;
        int step = -1;
        float t, len, sinceEnd = 99f, layerW, hitStopLeft;
        bool hitDone, buffered, vfxDone, startedThisFrame, firePrev;
        float speedUsed;
        int lastStep = -1;
        Step cur;
        float holdLeft;
        BladeTrail trail;
        readonly Dictionary<string, float> clipLen = new Dictionary<string, float>();
        readonly Collider[] overlap = new Collider[96];
        readonly HashSet<Health> struck = new HashSet<Health>();
        readonly List<ParticleSystem> vfxPool = new List<ParticleSystem>(), vfxBigPool = new List<ParticleSystem>();
        CutDebris debris;
        Vector3 aim = Vector3.forward;

        static readonly int HMoveX = Animator.StringToHash("MoveX"), HMoveZ = Animator.StringToHash("MoveZ"),
            HLoco = Animator.StringToHash("LocoSpeed"), HAtk = Animator.StringToHash("AtkSpeed"), HSpeed01 = Animator.StringToHash("Speed01");

        public void Setup(PlayerController owner, RoomRunConfig config, Transform modelRoot, Animator animator)
        {
            pc = owner; cfg = config; model = modelRoot; anim = animator;
            atkLayer = anim.GetLayerIndex("AttackFull");
            clipLen.Clear();
            if (anim.runtimeAnimatorController)
                foreach (var c in anim.runtimeAnimatorController.animationClips) clipLen[c.name] = c.length;
            if (clipLen.ContainsKey("BZ_Slash1")) UseBerserker();
            if (Berserker && !GetComponent<ArmStretch>()) gameObject.AddComponent<ArmStretch>().Setup(this, anim);
            feet = new[]
            {
                anim.GetBoneTransform(HumanBodyBones.LeftFoot), anim.GetBoneTransform(HumanBodyBones.RightFoot),
                anim.GetBoneTransform(HumanBodyBones.LeftToes), anim.GetBoneTransform(HumanBodyBones.RightToes),
            };
            step = -1; layerW = 0f;
            BuildPools();
            TryTrail();
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
            if (trail) Destroy(trail.gameObject);
        }

        // LSRig puts the sword in the hand in its own Start, after Setup: look for it until it is there
        float trailLook;
        void TryTrail()
        {
            if (trail || !bladeTrail || !anim) return;
            var hand = anim.GetBoneTransform(HumanBodyBones.RightHand);
            var sword = hand ? hand.Find("Greatsword") : null;
            if (!sword) return;
            trail = BladeTrail.Create(sword, BladeTrail.MeasureBlade(sword));
            // v3: the ribbon is sub-sampled round the player's vertical axis (a round arc, not a wedge) and lives long
            // enough to show most of the flat sweep
            trail.pivot = pc.transform;
            if (Berserker) { trail.life = 0.2f; trail.subdiv = 1; }
            // icy soul blue of the eye flames (M_GSEyeFlame _BaseColor 0.55/1.5/3, gradient 0.85/1/1 -> 0.3/0.85/1 -> 0.08/0.35/1)
            var tm = trail.GetComponent<MeshRenderer>() ? trail.GetComponent<MeshRenderer>().sharedMaterial : null;
            // 11-16: deeper blue body, pale-cyan edge instead of near white (matches the slash arcs)
            if (tm) { tm.SetColor("_Color", new Color(0.12f, 0.45f, 1.3f, 1f)); tm.SetColor("_Edge", new Color(0.75f, 1.1f, 1.45f, 1f)); }
            Debug.Log($"[RoomRun] greatsword blade trail on ({trail.bladeLen:0.00} m blade)");
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
            LungeVelocity = Vector3.zero; holdLeft = 0f;
        }

        // 11-15 (Kido: "허우적 거리고 공격한다는 느낌이 전혀 없자나"): the berserker clips keyed in Blender from Kido's LoA
        // recordings (ArtSource/GSHero/berserker_timing.md) already carry the timing: 2 f wind-up, 2-3 f swing, contact
        // pose held, then recovery; so they play at 1x with only a light speed-up on the recovery. Three hits: forehand
        // sweep (contact 5/16 f), backhand diagonal (5/20), overhead slam finisher (9/36). Hit-to-hit 0.5 s / 0.7 s like the
        // reference. The dodge plays BZ_Dash (low dash, sword trailing) on the attack layer.
        public bool Berserker { get; private set; }
        void UseBerserker()
        {
            Berserker = true;
            // v3 sweep fix (11-15): hits 1-2 are flat ~230 deg sweeps (right-back -> front -> left-back and back), so the
            // sector is ~115 deg each side; cancel from frame 9 (the back foot has stepped up by then); the step is root
            // motion (RootPath), not the lunge
            // 11-16 VFX size (Kido: "칼끝보다 조금 크게"): the arc's outer edge is ~1.48 m per unit vfxScale (vfxs=1 log);
            // blade tip at contact 1.53 / 1.28 / 1.14 m, so the edge sits ~20 % past it (1.85 / 1.55 / 1.41 m).
            // Visual only: radius / halfAngle (gameplay) unchanged.
            // 11-16 wide sweep (v5, Kido: "넓게", "레퍼런스 버서커 평타처럼", "빠르기도 너무 빠르고"): whole-body turning sweeps
            // keyed every frame in gs_berserker_anim.py. Hit 1: body ~115 deg, blade ~200 deg right -> front -> left, contact f5;
            // hit 2 the mirror (~220 deg); hit 3 a flat 380 deg spin in the clip (contact mid-turn f8). Cancel frames give the
            // reference's contact-to-contact spacing: 10 + 5 = 15 f, 18 + 8 = 21 f (berserker_timing.md). Blade tip ~2.0 m
            // (Blender); in game the tip runs ~1.75-1.82 m from the root (SweepTest tipR). Slash arc outer edge ~1.48 m per unit
            // vfxScale: 1.75 -> ~2.55 m = ~1.4x the tip radius (berserker_ref_sheet.png A61/A80: the reference's arc sits
            // 1.1-1.4x its blade tip). Gameplay radius / halfAngle unchanged (4.6 m / 115 covers the ~210 deg sweep).
            steps = new[]
            {
                // v6: hit 1 is the big 28 f clip now (contact f9); in the combo it chains from f18
                new Step { state = "SweepA", hitAt = 9f / 28f, cancelAt = 18f / 28f, radius = 5.0f, halfAngle = 150f, damage = 4, lunge = 0f, vfxScale = VfxSingle, vfxRoll = 180f },
                new Step { state = "SweepB", hitAt = 5f / 20f, cancelAt = 18f / 20f, radius = 4.6f, halfAngle = 115f, damage = 3, lunge = 0f, vfxScale = VfxWide, vfxRoll = 0f },
                new Step { state = "SweepC", hitAt = 8f / 30f, cancelAt = 22f / 30f, radius = 5.2f, halfAngle = 180f, damage = 7, lunge = 0f, vfxScale = VfxWide, vfxRoll = 0f, finisher = true },
            };
            // the Free Slash arcs back on for the berserker (Kido: "칼 베는거 vfx도 넣어놨잖아"), tinted to the eye flame
            slashVfx = true;
            dashStep = new Step { state = "SweepA", hitAt = 9f / 28f, cancelAt = 20f / 28f, radius = 5.4f, halfAngle = 75f, damage = 5, lunge = 17f, vfxScale = VfxWide, vfxRoll = 180f };
            // v5: every phase at 1x (the 1.3x recovery made the combo ~30 % shorter than the reference)
            attackSpeed = 1f; windupSpeed = 1f; strikeSpeed = 1f; recoverSpeed = 1f;
            strikeLead = 0.08f; strikeTail = 0.04f;
            // v2 (11-15 2nd pass): the contact pose freezes 2 f on top of the clip's own 1 f hold (3 f slam), hit-stop a
            // touch longer, a small shake even on a whiff so the stop reads in game
            impactHold = 0.033f; impactHoldHit = 0.033f;   // v5: the reference holds ~1 f on contact
            hitStop = 0.07f;
            endAt = 1f; leanDeg = 5f; lungeWindow = 0.14f;   // v5: clips end on a held flat guard; play them out
            // v3: no steering while swinging and no BodyLean pulse (the clip leans); any root move not from RootPath slides the feet
            moveWhileSwing = 0f; leanDeg = 0f;
        }

        // 11-16 single big hit (Kido 17:26-17:27 KST: "1타로 좀 크게 해봐 3타말고 단타", "지금보다 훨씬 크고 넓게 배게해",
        // "팔좀 늘려도되 롤처럼"): every click plays hit 1 only (no 2/3 chain); BZ_Slash1 is now a 28 f stand-alone heavy sweep
        // (gs_berserker_anim.py v6 W1B: coil far right, deep lunge 0.45 m, ~290 deg flat sweep right-back -> front (contact f9)
        // -> behind the left shoulder, hips ~140 deg + chest +-25, arms straight) and ArmStretch pulls both hands out along the
        // swing (LoL-style stretch, f3-f18) so the tip reaches ~2.6-2.8 m. A new click from f20 (0.67 s) restarts hit 1.
        // Hit sector widened to 150 deg each side; slash arc ~1.3x the stretched tip radius. SingleHit = false = the old
        // 3-hit combo (hit 1 is still the new big clip there).
        public static bool SingleHit = true;
        public static float VfxSingle = 2.35f;      // ~3.5 m outer edge (1.48 m per unit)
        public static Step SingleStep => new Step { state = "SweepA", hitAt = 9f / 28f, cancelAt = 20f / 28f, radius = 5.0f, halfAngle = 150f, damage = 5, lunge = 0f, vfxScale = VfxSingle, vfxRoll = 180f };
        public float ClipFrame => step >= 0 ? t * len * 30f : -1f;   // ArmStretch / logs
        public string CurrentState => step >= 0 ? cur.state : null;

        // v3 root motion (ArtSource/GSHero/gs_berserker_anim.py ROOT, Blender metres, one value per clip frame at 30 fps):
        // how far the body has advanced along the aim. The clips keep the hips over the root and the planted feet at
        // (plant - ROOT[f]), so moving the root by the same table keeps the feet still in the world and the body only
        // ever goes forward. Scaled by leg length (Unity world / Blender 0.8078 m).
        static readonly Dictionary<string, float[]> RootPath = new Dictionary<string, float[]>
        {
            // v5 (clips.txt of the 11-16 bake): hit 1 = the right foot steps up level with the left (0.18 m), hit 2 on the spot,
            // hit 3 spins on the spot and steps into the ready stance at the end (0.16 m)
            // v6 single big hit 1 (28 f): the lunge, 0.45 m over f1-f10 (left foot out f1-5, back foot swept up f7-13)
            { "SweepA", new[] { 0f, 0f, .015f, .057f, .117f, .188f, .262f, .333f, .393f, .435f, .45f, .45f, .45f, .45f, .45f,
                                .45f, .45f, .45f, .45f, .45f, .45f, .45f, .45f, .45f, .45f, .45f, .45f, .45f, .45f } },
            { "SweepB", new[] { 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f } },
            { "SweepC", new[] { 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f,
                                .012f, .041f, .08f, .119f, .148f, .16f, .16f, .16f } },
        };
        const float BlenderLegLen = 0.8078f;
        float legScale = -1f;
        float rootPrev;
        public static bool UseRootPath = true;   // dev A/B
        float LegScale()
        {
            if (legScale > 0f) return legScale;
            var u = anim.GetBoneTransform(HumanBodyBones.LeftUpperLeg); var l = anim.GetBoneTransform(HumanBodyBones.LeftLowerLeg); var f = anim.GetBoneTransform(HumanBodyBones.LeftFoot);
            if (!u || !l || !f) return 1f;
            legScale = (Vector3.Distance(u.position, l.position) + Vector3.Distance(l.position, f.position)) / BlenderLegLen;
            Debug.Log($"[RoomRun] berserker root path scale {legScale:0.000}");
            return legScale;
        }
        float RootAt(string state, float tn)
        {
            if (!RootPath.TryGetValue(state, out var r)) return 0f;
            float fr = Mathf.Clamp01(tn) * (r.Length - 1);
            int i = Mathf.Min((int)fr, r.Length - 2);
            return Mathf.Lerp(r[i], r[i + 1], fr - i);
        }
        public float RootAdvance { get; private set; }   // metres this frame (dev log)
        // v2: trail only while the blade really travels (3 f before contact, 2 f after; the slam's lift is not traced)
        bool TrailOn()
        {
            if (step < 0) return false;
            if (!Berserker) return t > cur.hitAt - strikeLead - 0.12f && t < cur.hitAt + strikeTail + 0.12f;
            float f = t * len * 30f, hf = cur.hitAt * len * 30f;
            // v3: hits 1-2 trail over the whole flat arc (f2 -> f8, hit 2 on round to f10)
            // v5: hits 1-2 from the end of the wind-up (f2) through the follow-through (f8); spin f4-f13 (the whole turn)
            if (cur.state == "SweepA" && len * 30f > 24f) return f > 5f && f < 14.5f;   // v6 big hit 1: the whole ~290 deg pass
            if (!cur.finisher) return f > hf - 3.2f && f < hf + (cur.state == "SweepB" ? 4.5f : 3.6f);
            return f > 3.5f && f < 13.5f;
        }

        // hit 3 spin: the visual turns a full revolution (clockwise from above, on from hit 2's sweep) over clip f3-f11,
        // about the left foot planted at f3. PlayerController applies it.
        public float SpinYaw { get; private set; }
        public Vector3 SpinPivot { get; private set; }
        public const float SpinFrom = 3f, SpinTo = 11f;
        public static bool ClipSpins = true;
        public const float VfxWide = 1.75f;
        void UpdateSpin()
        {
            // v5: the 360 spin is keyed in BZ_Slash3 itself (pivot on the left foot), so the visual is not turned here
            if (ClipSpins || step < 0 || !Berserker || !cur.finisher || cur.state != "SweepC") { SpinYaw = 0f; return; }
            float f = t * len * 30f;
            if (f < SpinFrom) { SpinYaw = 0f; return; }
            if (SpinYaw == 0f)
            {
                var lf = anim.GetBoneTransform(HumanBodyBones.LeftFoot);
                var p = lf ? lf.position : pc.transform.position; p.y = pc.transform.position.y;
                SpinPivot = p;
            }
            float u = Mathf.Clamp01((f - SpinFrom) / (SpinTo - SpinFrom));
            SpinYaw = Mathf.Max(0.01f, 360f * u * u * (3f - 2f * u));
            if (u >= 1f) SpinYaw = 360f;
        }
        bool wasRolling;

        string ClipOf(string state) => Berserker ? state switch
        {
            "SweepA" => "BZ_Slash1", "SweepB" => "BZ_Slash2", "SweepC" => "BZ_Slash3", "Dash" => "BZ_Dash", _ => state,
        } : state switch
        {
            "SweepA" => "GS_SweepA", "SweepB" => "GS_SweepB", "SweepC" => "GS_SweepC", "Spin" => "GS_SpinAttack", _ => state,
        };

        /// Raised when a swing starts so the body snaps to the cursor before the blade moves.
        public event System.Action SwingStarted;

        void StartStep(int i) => StartStep(i, SingleHit && Berserker ? SingleStep : steps[i]);

        void StartStep(int i, Step s)
        {
            step = i; cur = s; t = 0f; hitDone = false; buffered = false; vfxDone = false; lastStep = i; holdLeft = 0f; rootPrev = 0f;
            clipLen.TryGetValue(ClipOf(s.state), out len);
            if (len <= 0f) len = 1f;
            anim.CrossFadeInFixedTime(s.state, 0.03f, atkLayer, 0f);
            if (trail) trail.Clear();
            SwingStarted?.Invoke();
            startedThisFrame = true;
        }

        /// Fire during the dodge dash: PlayerController ends the dash and calls this; the combo goes on from the first sweep.
        public void DashSlash()
        {
            if (!anim) return;
            StartStep(0, dashStep);
        }

        // playback multiplier for the current phase of the swing
        float PhaseSpeed()
        {
            if (holdLeft > 0f) return 0f;
            if (t < cur.hitAt - strikeLead) return attackSpeed * windupSpeed;
            if (t < cur.hitAt + strikeTail) return attackSpeed * strikeSpeed;
            return attackSpeed * recoverSpeed;
        }

        /// Called from PlayerController.Update. moveLocal = velocity / move speed in world space.
        public void Tick(float dt, bool fireHeld, bool canAct, Vector3 aimDir, Vector3 vel, float moveSpeed, bool rolling)
        {
            if (!anim) return;
            if (!trail && bladeTrail && (trailLook -= dt) <= 0f) { trailLook = 0.25f; TryTrail(); }
            if (aimDir.sqrMagnitude > 0.01f) aim = aimDir;

            // hit-stop runs on real time
            if (hitStopLeft > 0f)
            {
                hitStopLeft -= Time.unscaledDeltaTime;
                if (hitStopLeft <= 0f) Time.timeScale = 1f;
            }

            // single hit: a click buffers one swing; a held button only repeats once the swing reaches its cancel frame (a
            // 0.15 s click used to re-buffer on its later frames and play every hit twice)
            if (fireHeld && canAct && (!SingleHit || !Berserker || !firePrev || step < 0 || t >= cur.cancelAt)) buffered = true;
            firePrev = fireHeld;
            if (rolling) Cancel();
            if (!canAct) { buffered = false; if (step >= 0) Cancel(); }

            if (step < 0)
            {
                sinceEnd += dt;
                if (buffered && canAct && !rolling) StartStep(SingleHit || sinceEnd >= comboResetTime ? 0 : (lastStep + 1) % steps.Length);
            }
            else
            {
                var s = cur;
                float speed = PhaseSpeed();
                speedUsed = speed;
                if (holdLeft > 0f) holdLeft -= dt;
                t += dt * speed / len;
                if (!hitDone && t >= s.hitAt) { hitDone = true; holdLeft = DoHit(s) > 0 ? impactHoldHit : impactHold; }
                if (slashVfx && !vfxDone && t >= s.hitAt - 0.12f) { vfxDone = true; PlayVfx(s); }
                if (t >= s.cancelAt && buffered && !rolling)
                {
                    int next = SingleHit ? 0 : step + 1 < steps.Length ? step + 1 : 0;
                    StartStep(next);
                }
                else if (t >= 1f || (t >= endAt && !buffered)) { step = -1; sinceEnd = 0f; }
            }

            // lunge: a short forward push on the ground just before the hit; the body leans into it
            LungeVelocity = Vector3.zero;
            RootAdvance = 0f;
            float leanWant = 0f;
            if (step >= 0)
            {
                var s = cur;
                if (t > s.hitAt - lungeWindow && t < s.hitAt) LungeVelocity = aim * s.lunge;
                // v3: the clip's root path, applied at once this frame (the pose the Animator evaluates after this Update
                // is at the same t), so the body and the root move together
                if (Berserker && UseRootPath && RootPath.ContainsKey(s.state))
                {
                    float r = RootAt(s.state, t);
                    RootAdvance = Mathf.Max(0f, r - rootPrev) * LegScale();
                    rootPrev = Mathf.Max(rootPrev, r);
                    if (RootAdvance > 0f) pc.RootMove(aim * RootAdvance, dt);
                }
                if (t > s.hitAt - lungeWindow && t < s.hitAt + 0.18f) leanWant = leanDeg * (s.finisher ? 0.4f : 1f);
            }
            BodyLean = Mathf.MoveTowards(BodyLean, leanWant, (leanWant > BodyLean ? 260f : 90f) * dt);
            UpdateSpin();
            if (trail)
            {
                // the ribbon only while the blade is fast: from the end of the wind-up to just after the strike
                trail.Emitting = TrailOn();
            }

            // berserker dodge: the low dash clip on the attack layer for as long as the roll lasts
            bool dashAnim = Berserker && rolling && clipLen.ContainsKey("BZ_Dash");
            if (dashAnim && !wasRolling) anim.CrossFadeInFixedTime("Dash", 0.03f, atkLayer, 0f);
            wasRolling = rolling;

            bool layerOn = step >= 0 || dashAnim;
            layerW = Mathf.MoveTowards(layerW, layerOn ? 1f : 0f, (layerOn ? 30f : 8f) * dt);
            if (atkLayer >= 0) anim.SetLayerWeight(atkLayer, layerW);
            // v3: the Animator advances by exactly what t advanced this frame (speedUsed; 0 on the frame a step starts,
            // where t is 0), so the pose and the root path never disagree (a 1-frame mismatch at the hit hold slid the feet)
            float atkSpeed = step >= 0 ? (Berserker ? (startedThisFrame ? 0f : speedUsed) : PhaseSpeed()) : dashAnim ? 1f : attackSpeed;
            anim.SetFloat(HAtk, atkSpeed);
            startedThisFrame = false;

            // legs: velocity in facing space, walk clips at 0.35, runs at 1
            var local = Quaternion.Inverse(Quaternion.LookRotation(aim)) * (vel / Mathf.Max(0.01f, moveSpeed));
            // berserker: the body faces the way it runs (PlayerController), so only forward speed drives idle -> run
            float speed01 = Mathf.Clamp01(new Vector2(vel.x, vel.z).magnitude / Mathf.Max(0.01f, moveSpeed));
            if (Berserker)
            {
                // split body (SplitBody.On): the legs run in the visual root's space (run / strafe / backpedal);
                // otherwise the body faces where it runs and only forward speed matters
                var rootFwd = model && model.parent ? model.parent.forward : Vector3.forward; rootFwd.y = 0f;
                if (SplitBody.On && rootFwd.sqrMagnitude > 1e-4f)
                    local = Quaternion.Inverse(Quaternion.LookRotation(rootFwd)) * (vel / Mathf.Max(0.01f, moveSpeed));
                else local = new Vector3(0f, 0f, speed01);
                anim.SetFloat(HSpeed01, speed01, 0.08f, dt);
            }
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
            // Outer edge ~1.48 m from the centre per unit scale (11-16 measure); the berserker scales it to the blade tip.
            tr.position = pc.transform.position + Vector3.up * vfxHeight;
            // 11-16: the mirror is a reflection across the aim line: aim frame x flipped, i.e. yaw -45 with local X flipped
            // (flipping local Z after the +45 yaw reflected across a 45 deg line and put hit 1's arc on the left, behind)
            bool mirror = s.vfxRoll > 90f;
            tr.rotation = Quaternion.LookRotation(aim) * Quaternion.Euler(0, mirror ? -45f : 45f, 0);
            tr.localScale = new Vector3(mirror ? -s.vfxScale : s.vfxScale, s.vfxScale, s.vfxScale);
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

        int DoHit(Step s)
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
            if (rig) rig.Shake(s.finisher ? 0.55f : hits > 0 ? 0.32f : Berserker ? 0.18f : 0.14f);
            if (hits > 0 && hitStop > 0f)
            {
                hitStopLeft = hitStop * (s.finisher ? 1.6f : 1f);
                Time.timeScale = 0.05f;
            }
            if (VegetationCut.Instance) VegetationCut.Instance.Sector(center, aim, s.radius, s.halfAngle);
            EmitBits(center, s);
            return hits;
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
                        // 11-16: the pack's Lights module spawned a cyan point light per particle (lit the hero pale cyan
                        // on every swing); disabling Light components does not stop the module
                        var lm = p.lights; lm.enabled = false;
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
                        // 11-16 soul colour: the pack's Slash material multiplies by an HDR white _Color of 8.6, which
                        // blew any tint to white; a runtime copy at a moderate gain, and the eye-flame gradient over the
                        // particle's life (pale core -> soul blue -> deep blue) with a quick fade
                        if (m && m.shader && m.shader.name.Contains("Slash") && m.HasProperty("_Color")) pr.sharedMaterial = SoulMaterial(m);
                        var sc0 = main.startColor;
                        float a0 = sc0.mode == ParticleSystemGradientMode.Color ? sc0.color.a : 1f;
                        main.startColor = new Color(1f, 1f, 1f, a0);
                        var col = p.colorOverLifetime; col.enabled = true; col.color = new ParticleSystem.MinMaxGradient(SoulGradient);
                    }
                    // 11-15: the pack's own realtime lights off (no new realtime lights; they washed the hero white on every swing)
                    foreach (var l in go.GetComponentsInChildren<Light>(true)) { l.shadows = LightShadows.None; l.enabled = false; }
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

        // eye flame (M_GSEyeFlame gradient 0.85/1/1 -> 0.3/0.85/1 -> 0.08/0.35/1); alpha falls off early (glaring per Kido 11-13)
        static readonly Gradient SoulGradient = new Gradient
        {
            colorKeys = new[] { new GradientColorKey(new Color(0.85f, 1f, 1f), 0f), new GradientColorKey(new Color(0.3f, 0.85f, 1f), 0.15f), new GradientColorKey(new Color(0.08f, 0.35f, 1f), 0.5f) },
            alphaKeys = new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.7f, 0.3f), new GradientAlphaKey(0f, 1f) },
        };
        public static float SoulGain = 2.2f;   // the arc material's _Color gain (the pack's was 8.6)
        static readonly Dictionary<Material, Material> soulMats = new Dictionary<Material, Material>();
        static Material SoulMaterial(Material src)
        {
            if (soulMats.TryGetValue(src, out var m) && m) return m;
            m = new Material(src) { name = src.name + " (soul)" };
            m.SetColor("_Color", new Color(SoulGain, SoulGain, SoulGain, 1f));
            soulMats[src] = m;
            return m;
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
            m.SetColor("_BaseColor", new Color(0.45f, 1.0f, 1.7f, 1f));   // 11-16 soul blue (was violet 1.6/1.2/2.2)
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
            // every reed clump the cut map cuts gets its top thrown (the soft edge of the stamp reaches a few degrees past the
            // sector); single blades are thinned to CutDebris.grassPerSwing inside Spawn
            const int max = 2500;
            if (VegetationQuery.Collect(center, aim, s.radius, Mathf.Min(s.halfAngle + 6f, 180f), vegHits, max) == 0) return;
            debris.Spawn(vegHits, center, aim, s.vfxRoll > 90f ? -1f : 1f, s.radius, s.halfAngle, s.finisher);
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
