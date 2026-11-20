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
            public float startAt;      // normalized clip time the swing starts from (skips a long keyed wind-up)
            public float hitAt;        // normalized clip time of the hit
            public float cancelAt;     // the next swing may start from here
            public float radius;       // metres from the player
            public float halfAngle;    // degrees each side of the aim (180 = all round)
            public int damage;
            public float lunge;        // m/s forward before the hit
            public float vfxScale;
            public float vfxRoll;      // 180 = mirrored arc (the swing comes from the other side)
            public bool finisher;
            public float endT;         // V Rising chain: the swing ends here (normalized) and the body drops back to the low rest
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
        // 11-19 (Kido: "대검 3타 칠 때 하체 고정 -> 안 됨, WASD는 공격 중에도 항상 똑같이"): while moving, the swing plays on the
        // synced upper-body layer "AttackUpper" (GSUpperLayer) and the legs keep the WASD run / strafe / backpedal; SwingUpper
        // turns the spine so the chest matches the clip. Off: PlayerPrefs RR.SwingLegs=0 (the whole body on AttackFull again).
        public static bool LegsWalkWhileSwing = true;
        public float LegMix => legMix;              // 0 = swing on the whole body, 1 = legs from the locomotion
        public float AttackLayerW => layerW;

        PlayerController pc;
        RoomRunConfig cfg;
        Animator anim;
        Transform model;
        Transform[] feet;
        int atkLayer = -1, upLayer = -1;
        float legMix;
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
            upLayer = anim.GetLayerIndex("AttackUpper");
            LegsWalkWhileSwing = PlayerPrefs.GetInt("RR.SwingLegs", 1) != 0;
            legMix = 0f;
            clipLen.Clear();
            if (anim.runtimeAnimatorController)
                foreach (var c in anim.runtimeAnimatorController.animationClips) clipLen[c.name] = c.length;
            if (clipLen.ContainsKey("BZ_Slash1")) UseBerserker();
            if (Berserker && !GetComponent<ArmStretch>()) gameObject.AddComponent<ArmStretch>().Setup(this, anim);
            if (atkLayer >= 0 && upLayer >= 0) { var su = GetComponent<SwingUpper>(); if (!su) su = gameObject.AddComponent<SwingUpper>(); su.Setup(this, anim, atkLayer, upLayer); }
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
            if (Berserker) BladeGhost.Create(sword);   // 11-19: faint blade afterimages in the fast frames (reference)
            // v3: the ribbon is sub-sampled round the player's vertical axis (a round arc, not a wedge) and lives long
            // enough to show most of the flat sweep
            trail.pivot = pc.transform;
            if (Berserker) { trail.life = 0.2f; trail.subdiv = 1; }
            // icy soul blue of the eye flames (M_GSEyeFlame _BaseColor 0.55/1.5/3, gradient 0.85/1/1 -> 0.3/0.85/1 -> 0.08/0.35/1)
            var tm = trail.GetComponent<MeshRenderer>() ? trail.GetComponent<MeshRenderer>().sharedMaterial : null;
            // 11-18 (Kido OK'd "칼끝을 따라가는 굵은 잔상"): a wider, fuller band from the tip (and a bit past it), lives longer
            if (Berserker && ThickTrail)
            {
                trail.baseFrac = 0.3f; trail.tipOver = 1.12f; trail.life = 0.26f;
                if (tm) { tm.SetFloat("_BodyPow", 0.7f); tm.SetFloat("_EdgeWidth", 0.2f); }
                // 11-19 14:50 (Kido, frame 4 of the side-by-side: "칼 vfx 길이 넓이 거리 다 달라"): the reference's slash is a THIN crescent
                // drawn by the blade tip, exactly where the tip has been. In the chain the ribbon is that crescent (outer
                // quarter of the blade, a little past the tip) and the fat pre-drawn arc sprite is off (ChainSprite).
                if (VRisingChain && !ChainSprite) { trail.baseFrac = TipBandFrom; trail.tipOver = 1.06f; trail.life = 0.2f; if (tm) { tm.SetFloat("_BodyPow", 0.3f); tm.SetFloat("_EdgeWidth", 0.3f); } }
            }
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
            // 11-18 V Rising chain (Kido 15:51 reference "녹음 2026-11-18 155052.mp4", V Rising greatsword primary, read frame
            // by frame at 30 fps): every hit = 2-3 f wind-up, 3-4 f swing (~200 deg across the front), ~5 f hold, back to a
            // low rest; contact to contact 22 f (0.73 s); sides alternate R->L, L->R, R->L (the third from a higher lift), no
            // spin. Ours with the clips we have: hits start INSIDE the clip (startAt) so the long keyed wind-ups are skipped:
            // A from f6 (contact f9), B from f2 (contact f5), then A again from f4 as the heavier third. Cancel frames give
            // ~22 f between contacts (2 f pre-hold included). VRisingChain = false = the 11-16 chain with the spin.
            if (VRisingChain)
                steps = new[]
                {
                    // 23:58 measured in game (log bladeYaw, 0 = aim, + = right): SweepA winds back to +150 until clip f10 and
                    // only then sweeps (+123 f11, +46 f13, 0 at f14, left by f17); "contact f9" was wrong and endT f15 cut the
                    // swing off at the front. A: start f9, contact f14, end f20. SweepB: -112 until f4, 0 at f6, +80 f10.
                    new Step { state = "SweepA", startAt = 8f / 28f, hitAt = 10.5f / 28f, cancelAt = 2f, endT = 15f / 28f, radius = 5.0f, halfAngle = 110f, damage = 4, lunge = 0f, vfxScale = VfxWide * 0.85f, vfxRoll = 180f },
                    new Step { state = "SweepB", startAt = 0.25f / 20f, hitAt = 5f / 20f, cancelAt = 2f, endT = 8.5f / 20f, radius = 5.0f, halfAngle = 110f, damage = 4, lunge = 0f, vfxScale = VfxWide * 0.85f, vfxRoll = 0f },
                    new Step { state = "SweepA", startAt = 4f / 28f, hitAt = 9f / 28f, cancelAt = 2f /* never: rule 4 */, radius = 5.4f, halfAngle = 130f, damage = 6, lunge = 0f, vfxScale = VfxSingle, vfxRoll = 180f, finisher = true },
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
            // 11-18 feel (Kido OK'd: "휘두르기 직전에 짧게 멈췄다가 아주 빠르게", "0.05~0.08초 히트스톱과 약한 화면 흔들림"):
            // the pose holds preHold s at the end of the wind-up, then the blade's pass plays at strikeSpeed; hit-stop 0.065 s
            preHold = PreHoldSec; strikeSpeed = StrikeFast;
            if (VRisingChain) impactHold = 0f;   // a whiff flows through; only a real hit holds (impactHoldHit + hit-stop)
            if (VRisingChain) { comboResetTime = 0.9f; ArmStretch.On = true; }   // 11-19 11:35 on again (Kido: arms long, wide swings); ArmStretch.ChainExtra   // see ChainGap
            // 11-19 01:20 (Kido: "제발 v라이징 보고 그것만 따라하라고"): the reference pace, with no standing still. Measured
            // on the recording: the blade crosses in 3-4 f, then follows through and lowers for ~13 f while still moving, a
            // 2-5 f wind-up, 22 f contact to contact. So the pass plays at 1x and the wind-up / follow-through play SLOWER
            // (never 0): windup 0.6x, recover 0.45x, pass window widened to cover the whole crossing.
            if (VRisingChain) { windupSpeed = RefWindup; recoverSpeed = RefRecover; strikeSpeed = 1f; strikeLead = 0.12f; strikeTail = 0.1f; }
            if (VRisingChain && MixamoChain) { windupSpeed = MxWindup; recoverSpeed = 1f; strikeSpeed = 1f; ArmStretch.On = false; }   // mocap: real arms, real timing
            hitStop = 0.065f;
        }
        public static float RefWindup = 0.6f, RefRecover = 0.9f;   // 09:15: the follow-through (overshoot, hang, eased return) is KEYED in BZ_SlashLR/RL now, so the game plays it near 1x   // 07:52: the slow part is the NEXT swing's wind-up (real motion: the blade is drawn back / lifted); a slow follow-through just crept 1 deg a frame   // 07:44 Kido "천천히 흘러가게해": the follow-through drifts on slowly (was 0.35)   // 07:30: slower, the chain no longer waits in the stance (were 0.6 / 0.45)
        // 11-19 restart on the suit character (Kido 17:55): the chain plays the Mixamo Great Sword mocap instead of the hand-keyed
        // berserker clips (states added by RoomRun.GSMixamoLayer; shots token mx=1; default off so nothing else changes).
        // Hit 1 = low sweep left -> right (GS_SweepC), hit 2 = down-cut upper right -> lower left (GS_Slash1), hit 3 = spin with a
        // descending cut (GS_SpinAttack). x = start (from a chained hit), y = contact, z = where a buffered next hit takes over
        // (all normalized; contact frames read off the right hand's path in the clips: f20/40, f18.5/39, f34.5/57).
        public static bool MixamoChain = false;
        public static Vector3 MxLR = new Vector3(14f / 40f, 20f / 40f, 30f / 40f), MxRL = new Vector3(10f / 39f, 18.5f / 39f, 28f / 39f), MxSpin = new Vector3(11f / 57f, 34.5f / 57f, 46f / 57f);
        public static float MxIdleStart = 6f / 40f;     // the opener out of the idle guard starts earlier in its clip (nearer the idle pose)
        public static float MxWindup = 1.3f;            // playback speed before the strike window
        bool mxLoco, suitTried; int mxUpper = -1; MxGrip mxGrip;
        Step MxStep(int i)
        {
            i = ((i % 3) + 3) % 3;
            if (i == 0) return new Step { state = "MxLR", startAt = step < 0 ? MxIdleStart : MxLR.x, hitAt = MxLR.y, endT = MxLR.z, cancelAt = 2f, radius = 5.0f, halfAngle = 110f, damage = 4, lunge = 0f, vfxScale = VfxWide * 0.85f, vfxRoll = 0f };
            if (i == 1) return new Step { state = "MxRL", startAt = MxRL.x, hitAt = MxRL.y, endT = MxRL.z, cancelAt = 2f, radius = 5.0f, halfAngle = 110f, damage = 4, lunge = 0f, vfxScale = VfxWide * 0.85f, vfxRoll = 180f };
            return new Step { state = "MxSpin", startAt = MxSpin.x, hitAt = MxSpin.y, endT = MxSpin.z, cancelAt = 2f, radius = 5.2f, halfAngle = 180f, damage = 7, lunge = 0f, vfxScale = VfxWide, vfxRoll = 0f, finisher = true };
        }
        public static bool VRisingChain = true;   // shots token vr=0 = the 11-16 chain (big hit 1, mirror, spin)
        // 11-18 SIDE RULES (Kido 16:01: "어깨가 바뀔 때 많은데 그 규칙 잘 봐 ... 규칙으로 잘 정하고 일해"), read from the
        // reference's combo frames (ArtSource/GSHero/ref/vrising_gs_combo_frames_1118.jpg):
        //   1. the sword rests on the side where the last swing ENDED (SweepA ends left, SweepB ends right)
        //   2. a swing always starts from the rest side and crosses to the other side, so sides alternate: R->L, L->R, R->L
        //   3. the shoulder opposite the sword leads (sword at the right hip = left shoulder forward), so the lead shoulder
        //      swaps with every hit
        //   4. out of combat (comboResetTime after the last swing) the stance is back on the default side, RIGHT, and a
        //      combo always opens R->L; after the third hit the next combo waits for that reset (never two R->L in a row)
        // RestSide feeds SplitBody.Carry (the two-hand stance mirrors to it).
        // 20:58 (Kido: "3타하고 오른손이 몸뚫고 움직입니다"): rule 4's walk back to the right carried the arms across the
        // torso. Rule 4 is dropped: the sword STAYS on the side the last swing ended on, and the next swing (also a new
        // combo's first) starts from that side: right -> SweepA (R->L), left -> SweepB (L->R). The stance never changes
        // side while idle, so the arms never cross the body; the third hit is whichever of the two is due, made heavier.
        // 23:59 (Kido, on the comparison: "맨 첫공격이 래퍼런스는 왼쪽에서 오른쪽이 크게 가고 오른쪽다리가 뒤로 뻗으면서
        // 몸방향 기울어짐, 완전 반대"): I had read the recording's first visible swing (R->L, already under way at frame 0) as
        // hit 1. The combo OPENS LEFT -> RIGHT: every combo's first hit is SweepB (L->R), then R->L, then L->R heavy. The
        // sword's default side is the LEFT. If the last combo left the sword on the right, the first hit plays SweepB from
        // its keyed wind-up (f0), so the blade is drawn across to the left as part of the swing, not while standing.
        int restSide = -1;
        public int RestSide => Berserker && VRisingChain ? restSide : 1;
        Step ChainStep(int i)
        {
            if (MixamoChain) return MxStep(i);
            var s = restSide > 0 ? steps[0] : steps[1];
            if (i == 0 && restSide > 0) { s = steps[1]; s.startAt = 0.25f / 20f; }   // combo opener is always L->R
            // 11-19: the L->R swing is the new lunge clip (right leg driven back, body leaning in) when the controller has it
            if (s.state == "SweepB" && clipLen.ContainsKey("BZ_SlashLR")) { s.state = "SweepLR"; s.startAt = 1f / 24f; s.hitAt = 5f / 24f; s.endT = FlowEnd / 24f; }   // 24 f clip with the keyed follow-through
            // 11-19 07:30 (Kido: "3타는 360도 돌아봐"): the third hit is the flat 360 spin (BZ_Slash3, keyed to follow a swing that
            // ended on the left: clockwise from above, contact mid-turn f8, ends with the blade on the right)
            if (i == steps.Length - 1 && SpinThird)
                return new Step { state = "SweepC", startAt = 1f / 30f, hitAt = 8f / 30f, endT = 19f / 30f, cancelAt = 2f, radius = 5.2f, halfAngle = 180f, damage = 7, lunge = 0f, vfxScale = VfxWide, vfxRoll = 0f, finisher = true };
            if (i == steps.Length - 1)
            {
                s.finisher = true; s.damage = 6; s.radius = 5.4f; s.halfAngle = 130f; s.vfxScale = VfxWide * 1.05f; s.cancelAt = 2f;
                s.endT += 1f / (restSide > 0 ? 28f : 20f);   // the heavy one holds a frame longer
                if (restSide > 0) s.startAt = 7f / 28f;   // one more frame of coil before the heavy R->L
            }
            // 11-19 07:19 (Kido: "칼들지않기규칙은 한적없는데 우선 이거 킵하고 v라이징처럼다시"): ChainStyle 0 = the version kept
            // at that point (R->L = the old flat SweepA); ChainStyle 1 = the R->L hit re-keyed like the reference: the blade is
            // lifted over the right shoulder in the wind-up, then cut across with a lunge (BZ_SlashRL, state "SweepRL").
            if (ChainStyle == 1 && s.state == "SweepA" && clipLen.ContainsKey("BZ_SlashRL"))
            {
                s.state = "SweepRL"; s.startAt = 0.25f / 24f; s.hitAt = 5f / 24f; s.endT = FlowEnd / 24f;
            }
            return s;
        }
        public static int ChainStyle = 1;   // shots token style=0|1
        // 23:52 (Kido: "래퍼런스 보고 한거 맞어? 너무 이상해"). Frame-matched against the reference, three things were off:
        //   1. no rest between hits: ours held the blade straight out sideways ~12 f and chained from there; the reference
        //      follows through ~5 f, DROPS BACK TO THE LOW REST for ~8 f and only then winds up again. Now every swing ends
        //      at endT (contact + 6 f), the stance (carry pose, side-on body) shows, and the next hit starts ChainGap after
        //      that, which keeps the 22 f contact-to-contact pace.
        //   2. the LoL-style arm stretch (ArmStretch, 11-16) kept the arms long and stiff: off in the chain.
        //   3. the slash arc was huge, there before the blade and lingered: smaller (about the tip radius), played at contact.
        // 11-19 00:46 (Kido: "왜 또 공격마다 버퍼링이 걸리는데"): the 12 f wait in the stance between hits read as a stall on
        // every attack. A buffered hit now follows after 3 f (just the blend), so the chain never stands still; the rest
        // pose only shows when the player stops attacking. Contact to contact ~14 f.
        public static float ChainGap = 9f / 30f;   // 01:20: 9 f, all of it spent MOVING (the swing blending down into the rest over RefLower), 22 f pace
        public static float RefLower = 0.10f;
        public static bool ThickTrail = true;   // shots token thick=0 = the old thin trail
        // 11-18 15:58 (Kido on the V Rising-paced capture: "자연스럽지 않고 딱 끊겨"): the 0.07 s freeze read as a 4-frame
        // stall, the 1.8x pass left the swing 2 frames long, and hits popped into each other over 0.06 s. Now: a 1-frame
        // settle, the pass at 1.2x (3-4 frames like the reference), hits blend over ChainBlend, 1-frame impact hold.
        // 15:58 Kido: "끊기는 거는 적 때릴때 히트 스탑인데 그거를 디폴트로 가져오면 어떻게해" - no stop of any kind on a
        // swing that hits nothing (PreHoldSec 0, whiff impactHold 0); only a connecting hit stops (hit-stop).
        public static float PreHoldSec = 0f, StrikeFast = 1.2f, ChainBlend = 0.12f;   // 11-19 14:15: 0.30 s outlasted the 3 f wind-up (the pass came 2-3 f late, through a blend of two far-apart poses)   // shots tokens prehold=<s>, strike=<x>
        public static bool SpinThird = true;     // shots token spin3=0 = the heavy sweep as hit 3
        public static bool ChainFlow = true;     // shots token flow=0 = rest between every hit (the 11-19 01:20 version)
        // 09:15 (Kido: no transition between hits, "1타 뚝 바로 2타 뚝"): the clips carry the follow-through now: contact f5, the blade and
        // body run on past it to f12, hang to ~f15, ease back f15-24. A chained hit starts at FlowEnd (the end of the hang) and
        // blends in over ChainBlend with its wind-up accelerating; with nothing buffered the clip plays its eased return out.
        public static float FlowEnd = 17f;   /* 15:00: the clips end in the reference's rests; hit 2 starts from the right rest at V35 = hit 1 f19 */ //      // 11-19 14:20: 16 -> 21 = contacts 22 f apart like the reference (5 f past the cut + ~8 f lowering) now that chained swings start at f2
        public static float FlowTail = 3f;       // clip frames of extra follow-through before the swing gives way to the stance (not buffered)
        float speedSm = 1f; bool fromIdle;
        public float IdleFor => step < 0 ? sinceEnd : 0f;
        public float preHold = 0f;    // seconds the pose freezes right before the strike (anticipation)
        bool preHoldDone;

        // 11-16 single big hit (Kido 17:26-17:27 KST: "1타로 좀 크게 해봐 3타말고 단타", "지금보다 훨씬 크고 넓게 배게해",
        // "팔좀 늘려도되 롤처럼"): every click plays hit 1 only (no 2/3 chain); BZ_Slash1 is now a 28 f stand-alone heavy sweep
        // (gs_berserker_anim.py v6 W1B: coil far right, deep lunge 0.45 m, ~290 deg flat sweep right-back -> front (contact f9)
        // -> behind the left shoulder, hips ~140 deg + chest +-25, arms straight) and ArmStretch pulls both hands out along the
        // swing (LoL-style stretch, f3-f18) so the tip reaches ~2.6-2.8 m. A new click from f20 (0.67 s) restarts hit 1.
        // Hit sector widened to 150 deg each side; slash arc ~1.3x the stretched tip radius. SingleHit = false = the old
        // 3-hit combo (hit 1 is still the new big clip there).
        // 11-18 (Kido: "칼질 너무 어설프다", "준 로아 버서커 3타로 바꿔봐"): back to the 3-hit chain by default
        public static bool SingleHit = false;
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
            if (cur.state == "SweepA" && len * 30f > 24f) return f > (VRisingChain ? 8.5f : 5f) && f < 14.5f;   // v6 big hit 1: the whole ~290 deg pass
            if (!cur.finisher) return f > hf - 3.2f && f < hf + (cur.state == "SweepB" || cur.state == "SweepLR" || cur.state == "SweepRL" ? 4.5f : 3.6f);
            return f > 3.5f && f < 13.5f;
        }

        // hit 3 spin: the visual turns a full revolution (clockwise from above, on from hit 2's sweep) over clip f3-f11,
        // about the left foot planted at f3. PlayerController applies it.
        public float SpinYaw { get; private set; }
        public Vector3 SpinPivot { get; private set; }
        public const float SpinFrom = 3f, SpinTo = 11f;
        public static bool ClipSpins = true;
        public const float VfxWide = 1.75f;
        public static float ChainVfx = 1.9f, ChainVfxSpin = 1.9f, ChainVfxHeight = 1.8f;   // 11-19 13:30: tip radius 2.3 m after the reference match (arms bent)   // shots tokens cvfx=<scale>, vfxy=<m>
        // 11-19 13:10 (Kido: "vfx는 왼쪽에 있는데 여기는 앞에서 휘적이고있고 ... vfx랑 칼 싱크해야지"): the arc sprite is drawn whole
        // (~300 deg, the ring 360) the frame it starts, so at contact it sat 60-220 deg AHEAD of the blade (seat3 f25: blade 41,
        // arc to ~110; spin f63: blade 5, ring complete). It now starts when the blade has swept that range (clip frames after
        // contact); the blade trail fan, which follows the blade exactly, covers the pass itself. shots tokens vfxlate=, vfxlatespin=
        public static float VfxLate = 2f, VfxLateSpin = 3.5f;
        public static bool ChainSprite = false;      // shots token arcsprite=1 = the old fat arc sprite in the chain
        public static float TipBandFrom = 0.62f;     // the tip crescent starts this far up the blade
        public static float VfxLead = 0f;   // 11-19 13:05: chain arcs start at contact, not 0.05 clip-time before it (the full arc was on screen ahead of the blade)
        public static bool VfxFollowBlade = true;   // shots token vfxdir=0|1
        // 11-19 sword wave: every hit (1/2/3) launches a flying crescent (SwordWave.cs, Free Slash VFX projectile). Off = no wave.
        public static bool SwordWaveEnabled { get => SwordWave.Enabled; set => SwordWave.Enabled = value; }
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
            "MxLR" => "GS_SweepC", "MxRL" => "GS_Slash1", "MxSpin" => "GS_SpinAttack", "SweepA" => "BZ_Slash1", "SweepB" => "BZ_Slash2", "SweepLR" => "BZ_SlashLR", "SweepRL" => "BZ_SlashRL", "SweepC" => "BZ_Slash3", "Dash" => "BZ_Dash", _ => state,
        } : state switch
        {
            "SweepA" => "GS_SweepA", "SweepB" => "GS_SweepB", "SweepC" => "GS_SweepC", "Spin" => "GS_SpinAttack", _ => state,
        };

        /// Raised when a swing starts so the body snaps to the cursor before the blade moves.
        public event System.Action SwingStarted;

        void StartStep(int i) => StartStep(i, SingleHit && Berserker ? SingleStep : Berserker && VRisingChain ? ChainStep(i) : steps[i]);

        void StartStep(int i, Step s)
        {
            fromIdle = step < 0; speedSm = fromIdle ? Mathf.Max(windupSpeed, 0.8f) : windupSpeed; step = i; cur = s; t = s.startAt; hitDone = false; preHoldDone = false; buffered = false; vfxDone = false; lastStep = i; holdLeft = 0f;
            // 11-19: looping out of the spin the blade is on the right and the opener is L->R; a short blend takes it across as the wind-up
            bool crossOver = step >= 0 && !fromIdle && restSide > 0 && (s.state == "SweepLR" || s.state == "SweepB");
            rootPrev = RootAt(s.state, s.startAt);   // the part of the clip's root path before startAt is not travelled
            if (Berserker && s.state.StartsWith("Mx")) restSide = s.state == "MxLR" ? 1 : -1;   // the side the swing travels to (sword wave / edge rule)
            else if (Berserker) restSide = s.state == "SweepA" || s.state == "SweepRL" || (s.state == "SweepC" && VRisingChain) ? -1 : s.state == "SweepB" || s.state == "SweepLR" || s.state == "SweepC" ? 1 : restSide;   // where this swing will leave the sword
            clipLen.TryGetValue(ClipOf(s.state), out len);
            if (len <= 0f) len = 1f;
            // a start inside the clip blends a little longer (0.06 s) so the pose does not pop from the last hit's hold
            // 11-19 01:00: CrossFadeInFixedTime ignored the start offset (the state runs on the AtkSpeed parameter, which is 0
            // on the start frame), so every "startAt" swing really played from clip f0 and the contact / end frames were
            // 5-10 f off (log: the same bladeYaw run whatever startAt was). CrossFade takes a NORMALIZED offset: honoured.
            // out of the stance the layer weight is 0 and its old state is the LAST swing's end pose: cross-fading from that dragged the
            // blade round from the wrong side (log 08:00: -118 -> 162 -> 100 -> -19). Start the state clean; the layer weight ramp blends.
            if (fromIdle) anim.Play(s.state, atkLayer, s.startAt);
            else if (s.startAt > 0f) anim.CrossFade(s.state, (crossOver ? 0.12f : ChainBlend) / 0.8f, atkLayer, s.startAt);
            else anim.CrossFadeInFixedTime(s.state, 0.03f, atkLayer, 0f);
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
            if (t < cur.hitAt - strikeLead) return attackSpeed * (fromIdle ? Mathf.Max(windupSpeed, 0.8f) : windupSpeed);   // the first swing out of the stance answers the click quickly; only chained wind-ups are slow
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
                // side rule 4: after the third hit the next combo waits for the stance to come back to the right
                // V Rising chain: every hit waits ChainGap at the low rest (the third a little longer) before the next
                bool cooled = !Berserker || !VRisingChain || SingleHit || lastStep < 0 || sinceEnd >= ChainGap + (lastStep == steps.Length - 1 ? 0.1f : 0f);
                if (buffered && canAct && !rolling && cooled) StartStep(SingleHit || sinceEnd >= comboResetTime ? 0 : (lastStep + 1) % steps.Length);
            }
            else
            {
                var s = cur;
                float speed = PhaseSpeed();
                // smooth the pace changes (wind-up / pass / follow-through) instead of stepping between them
                if (Berserker && VRisingChain && ChainFlow && holdLeft <= 0f) { speedSm = Mathf.MoveTowards(speedSm, speed, (speed < speedSm ? 9f : 6f) * dt); speed = speedSm; }
                speedUsed = speed;
                if (holdLeft > 0f) holdLeft -= dt;
                t += dt * speed / len;
                if (!preHoldDone && preHold > 0f && t >= s.hitAt - strikeLead) { preHoldDone = true; holdLeft = preHold; }
                if (!hitDone && t >= s.hitAt) { hitDone = true; holdLeft = DoHit(s) > 0 ? impactHoldHit : impactHold; if (SwordWaveEnabled) SwordWave.Fire(pc.transform.position, aim, RestSide, s.state == "SweepC", s.damage, cfg.playerSize); }
                // with a pre-hold the arc waits for the hold to end (it used to appear ~4 f before the blade moved)
                if (slashVfx && (ChainSprite || !(Berserker && VRisingChain)) && !vfxDone && t >= s.hitAt - (s.endT > 0f ? VfxLead - (s.state == "SweepC" ? VfxLateSpin : VfxLate) / (len * 30f) : 0.12f) && (preHold <= 0f || (preHoldDone && holdLeft <= 0f))) { vfxDone = true; PlayVfx(s); }
                if (t >= s.cancelAt && buffered && !rolling)
                {
                    int next = SingleHit ? 0 : step + 1 < steps.Length ? step + 1 : 0;
                    StartStep(next);
                }
                // 11-19 07:30 (Kido: "동작마다 너무 뚝뚝 끊겨"): a buffered hit starts straight from the follow-through at endT
                // (one cross-fade, body stays on the aim); the stance only comes when the player stops attacking
                else if (s.endT > 0f && t >= s.endT && buffered && !rolling && ChainFlow) StartStep(step + 1 < steps.Length ? step + 1 : 0);
                else if (t >= 1f || (t >= endAt && !buffered) || (s.endT > 0f && !MixamoChain && t >= s.endT + (ChainFlow ? FlowTail / (len * 30f) : 0f))) { step = -1; sinceEnd = 0f; }   // mocap clips play out to their own idle return
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
            leanWant *= 1f - legMix;   // walking legs: no whole-body pitch into the swing (the visual root carries the legs too)
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
            layerW = Mathf.MoveTowards(layerW, layerOn ? 1f : 0f, (layerOn ? (MixamoChain ? 12f : 30f) : Berserker && VRisingChain ? 1f / RefLower : 8f) * dt);   // chain: the swing melts into the rest over RefLower (the reference lowers the blade ~8 f, moving)
            // moving (WASD, not the lunge) -> legs from the locomotion; dash = whole body at once
            float moveLeg = new Vector2(vel.x - LungeVelocity.x, vel.z - LungeVelocity.z).magnitude / Mathf.Max(0.01f, moveSpeed);
            float legWant = LegsWalkWhileSwing && upLayer >= 0 && !dashAnim && !rolling ? Mathf.Clamp01(moveLeg * 5f) : 0f;
            legMix = dashAnim || rolling ? 0f : Mathf.MoveTowards(legMix, legWant, dt / 0.1f);
            if (atkLayer >= 0) anim.SetLayerWeight(atkLayer, upLayer >= 0 ? layerW * (1f - legMix) : layerW);
            if (upLayer >= 0) anim.SetLayerWeight(upLayer, layerW);
            // Mixamo chain: legs AND arms of the base layer come from the pack's own locomotion (state "MxLocomotion"), so the
            // berserker arms layer (UpperBZ: the hand-keyed one-hand drag) is switched off
            if (Berserker && VRisingChain && MixamoChain)
            {
                if (!mxLoco)
                {
                    mxLoco = true; mxUpper = anim.GetLayerIndex("UpperBZ");
                    if (anim.HasState(0, Animator.StringToHash("MxLocomotion"))) anim.CrossFadeInFixedTime("MxLocomotion", 0.1f, 0);
                    else Debug.LogWarning("[RoomRun] Mixamo chain: no MxLocomotion state (run RoomRun.GSMixamoLayer.Add)");
                }
                if (mxUpper >= 0) anim.SetLayerWeight(mxUpper, 0f);
                // the suit body (Mixamo Joe) was only ever attached by the whip (WhipCombat); the greatsword attaches it here once the
                // blade trail has found the sword on the hero's hand (SuitBody then moves the sword to Joe's hand)
                if (!mxGrip && trail && trail.sword && (suitTried || !SuitBody.On)) { mxGrip = gameObject.AddComponent<MxGrip>(); mxGrip.Setup(trail.sword, GetComponent<SplitBody>()); }
            }
            // the suit body (Mixamo Joe) for the greatsword in every mode, not only the Mixamo chain (11-20 Kido: "기본 캐릭터 정장 입고
            // 하던 거 ... 롱코트로 그거부터 끝내자"), with the 사복검 thread's long coat (CoatTail, RR.Coat / token coat=) on Joe
            if (!suitTried && SuitBody.On && trail) { suitTried = true; var body = SuitBody.Attach(anim); if (body && body != anim) CoatTail.Sync(body); }
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
            // 11-19 12:20 (Kido: "vfx도 이제 칼이랑 반경 넓어졌으니깐 그만큼 크고 넓게 ... 아직도 전 위치다"): measured after the
            // arm stretch + raised swing (log high1): tip radius 2.67 m (hits 1-2) / 2.37 m (spin), blade 1.7-2.1 m above the
            // root; the arc was still 2.2 m wide at 1.0 m. Chain: outer edge ~1.05x the tip radius, at the blade's height.
            bool chainFx = Berserker && VRisingChain;
            if (chainFx) s.vfxScale = s.state == "SweepC" ? ChainVfxSpin : ChainVfx;
            float fxY = (chainFx ? ChainVfxHeight : vfxHeight) * cfg.playerSize;   // 11-19: the arc grows with the character (playerSize)
            float fxS = s.vfxScale * cfg.playerSize;
            tr.position = pc.transform.position + Vector3.up * fxY;
            // 11-16: the mirror is a reflection across the aim line: aim frame x flipped, i.e. yaw -45 with local X flipped
            // (flipping local Z after the +45 yaw reflected across a 45 deg line and put hit 1's arc on the left, behind)
            bool mirror = s.vfxRoll > 90f;
            // 11-19 12:00 (Kido: "칼날 향하는 방향이랑 vfx방향 다르거든 ... 통일해"): frames showed the arc growing and fading
            // counter-clockwise on the L->R hit and the spin (blade clockwise), and clockwise on the R->L hit: all three
            // reversed. In the chain the arc is mirrored so it is drawn the way the blade travels.
            bool flip = VfxFollowBlade && Berserker && VRisingChain;
            if (flip) mirror = !mirror;
            tr.rotation = Quaternion.LookRotation(aim) * Quaternion.Euler(0, mirror ? -45f : 45f, 0);
            tr.localScale = new Vector3(mirror ? -fxS : fxS, fxS, fxS);
            ps.Clear(true);
            ps.Play(true);
            if (s.finisher && (!(Berserker && VRisingChain) || s.state == "SweepC"))   // V Rising chain: the third hit is a frontal sweep, no ring
            {
                // the finisher is all round: a second arc behind
                var back = pool.Count > 1 ? pool[pool.IndexOf(ps) == 0 ? 1 : 0] : null;
                if (back)
                {
                    back.transform.position = pc.transform.position + Vector3.up * fxY;
                    back.transform.rotation = Quaternion.LookRotation(-aim) * Quaternion.Euler(0, mirror ? -45f : 45f, 0);
                    back.transform.localScale = new Vector3(mirror ? -fxS : fxS, fxS, fxS);
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
            // 11-18: "약한 화면 흔들림" - lighter for the berserker chain (was 0.55 / 0.32 / 0.18)
            if (rig) rig.Shake(Berserker ? (s.finisher ? 0.3f : hits > 0 ? 0.18f : 0.08f) : s.finisher ? 0.55f : hits > 0 ? 0.32f : 0.14f);
            if (hits > 0 && hitStop > 0f)
            {
                hitStopLeft = hitStop * (s.finisher ? (Berserker ? 1.2f : 1.6f) : 1f);   // berserker: 0.065 / 0.078 s
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
