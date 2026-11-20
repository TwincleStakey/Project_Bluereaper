// Whip weapon test (11-18, Kido + team planner memo). Reference: "녹음 2026-11-18 155052.mp4" (V Rising whip section,
// ~19-37 s), read frame by frame at 30 fps:
//   basic   - hand goes up over the head (~5 f), snaps forward and down (~3 f), the whip unrolls into a straight line
//             toward the cursor and cracks at the tip (flash) right as the arm finishes; next crack ~0.7 s later.
//             Planner memo: narrow long cone in front, ~30-40 deg (here 35 deg total), the main attack.
//   proc    - planner memo: on a basic hit, a chance for a wide sweep (180 deg+). Reference: the whip circling the body as
//             one big pale arc in ~10 f. Here 240 deg, left to right, hits as the whip passes.
// The whip is a bone chain (verlet points + distance constraints) drawn as a tapered tube; during the keyed phases the
// points are pulled toward a keyed shape (unroll line with a travelling loop / sweeping arc) and physics fills in the
// rest (lag, follow-through, the drop to the ground afterwards). The right arm and the spine are posed here, after the
// Animator and SplitBody; the greatsword is hidden and never attacks while the whip is on.
// Toggle: dev panel 플레이어 > 채찍 (테스트) = PlayerPrefs RR.Whip (default off).
using System.Collections.Generic;
using UnityEngine;

namespace RoomRun
{
    [DefaultExecutionOrder(260)]   // after SplitBody (50), LSPoseFix (100), ArmStretch (150), eye flames (200)
    public sealed class WhipCombat : MonoBehaviour
    {
        public const string Key = "RR.Whip";
        public static bool UseWhip => PlayerPrefs.GetInt(Key, 0) != 0;
        public static bool ForceProc;            // captures: every basic hit procs the sweep
        public static float ProcChance = 0.3f;
        public static float Length = 3.3f;       // metres, hand to tip (v7 segmented blade)
        public static bool LogFrames;
        // v7 (Kido 11-18: a blade that splits into segments and lashes long like a whip, not a leather whip): one sword
        // at rest; the blade splits into plates on a cable for each lash and snaps back together after it.
        public static float SwordLen = 1.35f;    // assembled blade, guard to tip
        public static int Plates = 9;
        // v15 (Kido: why is it not our sword): the plates are the hero's own sword mesh (the hidden "Greatsword"), cut across
        // its length; the hilt stays whole in the hand. BladeFrac = share of the sword's length (from the tip) that splits.
        public static bool RealSword = true;
        public static bool Swish = true;
        // v17 (Kido's own clip "603111cd74989630.mp4", panel 채찍, read at 30 fps -> ArtSource/Whip/ref2): the lash is a flat
        // SPIRAL round the body: out of the hand, growing while it winds one turn (tip ~3.5 m out), ~8 f to full, a ring pop
        // at the tip, then back along the same curve in ~6 f; the winding side flips every lash; a bright segmented band.
        public static bool Spiral = true;        // shots token whipspiral=0 = the v16 swish
        // v18 (Kido: do not copy that clip's look, build it new in that WAY): a driven rope. Only the handle end is moved (a
        // sweep that speeds up and stops dead); every other link is free verlet, so the stop travels down the blade and
        // the tip cracks by itself. Own move set, cycling: right-side lash, left-side lash, overhead wind-up lash.
        public static bool Free = false;         // v18 driven rope (knots, kinks: Kido "why worse than Opus") -> off; token whipfree=1
        // v19: an AUTHORED curve, always smooth. The blade is one arc whose total turn is keyed: coiled round the body
        // (more than a full turn), it uncoils toward the cursor while it grows, goes a little past straight at the crack
        // (the snap), and coils back in along the same family of curves. The tip end curls tighter than the root
        // (turn weight grows toward the tip), which gives the whip curl. Moves cycle: right, left, double coil.
        public static bool Authored = true;      // shots token whipauth=0 = v17 spiral
        public static float AuthLen = 6.5f;
        readonly Vector2[] authPts = new Vector2[N];
        public static float FreeLen = 6.5f;      // metres of blade chain at full reach
        int freeMove;                            // 0 right, 1 left, 2 overhead (set per lash)
        float SplitLen => Free ? FreeLen : Authored ? AuthLen : SpiralLen;
        public static float SpiralLen = 11f;    // metres of blade chain at full reach
        public static float SpiralR0 = 0.8f, SpiralGrow = 0.4f;   // turn radius at the hand, and how fast it opens per metre
        public const int Rep = 3;                // plate copies per sword slice when split (9 slices -> 45 links)
        float spiralSign = 1f;         // shots token whipswish=0 = the v15 straight shot
        public static float BladeFrac = 0.56f;
        public static float DevFireGap;          // captures: seconds of no new lash after one ends (shows the blade rejoin)
        float gapLeft;
        public static bool CrackFlash = true;    // v13: back on, small and short like the reference crack (v1 was 3x too big)   // v1 white star: Kido 11-18 "거지같은" -> off
        public static bool BasicRibbon = false;  // v1 grey slab behind the lash -> off            // captures: one line per frame to the console-free log list below

        // ---- timing (seconds, from the reference at 30 fps; slightly faster for the hardcore feel) ----
        // v13 (reference frame table: hand up held ~6 f, snap 3 f, crack 3 f, upright again by f15, next raise f19)
        // v15 (Kido: match the speed): the reference's 22 f per lash = 0.73 s
        // v16 (Kido: blade only, a cowboy-whip swish instead of a straight shot): the loop is given time to be seen (BUnroll .06 -> .15)
        // v24: no dead time (the reference lashes every ~0.45 s and the whip never rests)
        // v26 (Kido 15:30, screenshot of v17: the hit's body motion was fine THEN): body timing back to the v17 values exactly.
        // These drive the body clip; do not retune them for the blade.
        const float BWind = 0.20f, BSnap = 0.10f, BUnroll = 0.15f, BChain = 0.71f, BEnd = 0.75f;
        // v20 (Kido 14:17 + planner image): hit 1 narrow and long, hit 2 very wide. The sweep is now 340 deg at 7.5 m.
        const float SWind = 0.12f, SSweep = 0.30f, SEnd = 0.72f;   // v21 values (Kido: the wide hit is good)
        const float SweepFrom = -170f, SweepTo = 170f;    // degrees from the aim (negative = left)
        public static float SweepLen = 7.5f;
        public static bool SweepSpiral = true;   // v21: the wide hit is a winding spiral (token whipsweep=0 = the v20 rotating spoke)
        readonly Vector3[] spPts = new Vector3[72]; readonly float[] spLen = new float[72];
        public static bool ComboWide = true;     // narrow lash -> wide sweep -> narrow ... (token whipcombo=0 = proc chance only)
        float lashLen;
        const float BasicHalf = 17.5f, SweepLag = 40f;

        public bool Attacking => kind != 0;
        public bool Sweeping => kind == 2;
        public Vector3 AttackAim => aim;
        public bool BodyToAim => kind != 0 && !movingAtk;
        public static bool UseClipBody = true;   // shots token whipbody=0 = the v1/v2 code-only body for A/B
        // v11: a real one-handed attack clip (Kevin Iglesias "Human Melee Animations FREE", itch.io) drives the whole body
        // v12: Mixamo "Standing Melee Attack Downward" (one-handed overhead chop, Kido asked for it) / "... 360 High" for the sweep
        // v14 (pose pass against the reference, zoomed frames ArtSource/Whip/cmp/ref_zoom.png): the reference lash is a THROW:
        // free hand reaching at the target, blade hand cocked back, torso unwinds ~90 deg, blade arm goes straight out, free arm
        // swings back, both arms open on the way back. Mixamo "Goalie Throw" = WH_Throw. WH_Downward = the v12/v13 chop (token whipclip=chop).
        // v22 (Kido 14:55: wide hit is good; hit 1 must not SHOOT forward: it comes down from above, and at the moment it
        // lands it swishes across about the planner image's narrow cone). Body = the overhead chop clip again.
        public static bool Chop = true;          // shots token whipchop=0 = the v21 thrown lash (WH_Throw)
        // v23 (Kido 15:06: hit 1 still not a whip; make it NEW, not patched: a DIAGONAL lash, a somewhat wider cone, 휘리릭).
        // One idea only: every link points along the same diagonal swing (high behind the weapon shoulder -> low across the
        // front), but each link is LATE by its distance from the hand. The hand finishes first; the bend runs out along
        // the blade and the tip comes through last, past the end, and flicks back. That delay is the whip.
        public static bool Diag = true;          // shots token whipdiag=0 = the v22 overhead chop
        // v28 (Kido 15:41: "it is a whip, not a sword that hits one line: it has to RAKE the whole fan of the image").
        // The blade comes over from behind the weapon shoulder, lands full length on one edge of the planner's fan, then
        // rakes along the ground to the other edge, flicks past it and is drawn back. Every link does the same path but
        // RakeLag * s^1.3 seconds later, so the blade is bent while it rakes. Body timing constants are not touched.
        public static bool Rake = true;          // shots token whiprake=0 = the v27 single diagonal lash
        public const float RakeLand = 0.34f, RakeEnd = 0.52f, RakeLag = 0.075f, RakeHalf = 36f;   // Kido 15:42: do not narrow it -> a 72 deg fan
        float rakeYawNow;                        // where the root of the blade points now (deg from the aim), for the hits
        // v29 (Kido 15:53, two screenshots: the flat sideways rake is WRONG; the frame where the blade arches over from
        // behind is RIGHT): hit 1 is a strike from above. The blade goes up and back, then over the top and down in front
        // as a tall ARCH (links late by ArchLag * s^1.2, so the root already points down while the tip is still high),
        // drifting a little sideways while it falls (the 휘리릭), and it slaps down. No raking along the ground.
        public static bool Arch = true;          // shots token whiparch=0 = the v28 ground rake
        public const float ArchLand = 0.36f, ArchLag = 0.11f, ArchDrift = 22f, ArchHalf = 30f;
        // v31 (Kido 11-19: keep version 1 = blade only; version 2 = the WHOLE BODY like the Elden Ring clip): the body
        // bows deep into every slam. Hit 1 and 3 = the overhead chop clip (WH_Downward), hit 2 = the side slash clip;
        // legs, hips, torso and both arms come from the clip at full weight.
        public static bool FullBodyER = false;   // shots token whipbody2=1 ; dev panel 플레이어 > 채찍 온몸(2번) = PlayerPrefs RR.WhipBody2
        public const string Body2Key = "RR.WhipBody2";
        static bool Body2 => FullBodyER;         // v32: Kido "전으로 돌려 몸애니": the dev-panel toggle is gone, token only
        AnimationClip clipChop, clipSide; float cStart, cPeak, cImpact, sStart, sPeak, sImpact;
        public static bool LightChain = false;   // v30 3-hit light chain; v32: off (Kido: hit 1 then the spin). token whiplight=1
        public static bool MusaLash = true;      // v32: hit 1 = the voxel project's lash logic (token whipmusa=0 = v29 arch)
        // v33 (Kido 11-19 10:17: hit 1 still hits nothing like the 무사 clip): side by side with the clip, the 무사 whip
        // does not thrust; its hits are the repo's move A "wide forehand wrap": the driver sweeps from back-left (-125)
        // round the front to back-right (+135) in 0.34 s at 5.6 (here 6.5 m), and the delayed nodes draw the trailing
        // curl. Thick glowing band, hits whatever the chain sweeps through.
        public const float MusaDur = 0.34f, MusaStart = 0.2f, MusaLagTotal = 0.176f, MusaWobble = 7f;
        public static bool MusaWrap = true;      // token whipwrap=0 = the v32 straight lash (repo move 3)
        // v34 (Kido 11-19 11:31, two screenshots: the U-shaped spread is wrong; the long hook lying on the ground is right
        // but must lie IN FRONT): hit 1 = the same delayed-driver chain, but the driver swings in 3D: up and back over the
        // shoulder, over the top and down in front (an arc from above), then the root sweeps across the planner's fan on
        // the ground; the late outer links curl into the hook while it sweeps. Token whiparc=0 = v33 wrap.
        public static bool MusaArc = true;
        public static string[] PreviewClips; List<AnimationClip> prevLoaded; float prevT0;
        // v49 (Kido 20:53 picked Mixamo "Standing Torch Melee Attack 03" (16-23 s of the candidates): the body plays it at real
        // speed; hit A = the overhead arc down (its swing 1, impact 0.77 s), hit B = the reverse, from low behind UP through
        // the front (its swing 2, 1.42-1.8 s) with a forward lunge. token whiptorch=0 = v48 (WH_Throw body, one hit)
        public static bool TorchCombo = true;
        public static string TorchClipPath = "Assets/Game/LocoSplit/Art/MixamoRR/WH_Torch03.fbx";
        public const float TA = 0.77f, TB0 = 1.30f, TBHit = 1.62f, TEnd = 2.15f;
        public static float LungeSpeed = 3.2f;
        AnimationClip clipTorch; bool slam2;
        bool TorchOn => TorchCombo && clipTorch && MusaLash && Authored && MusaArc;
        public Vector3 LungeVelocity
        {
            get
            {
                if (!(TorchOn && kind == 1) || t < TB0 + 0.08f || t > TB0 + 0.5f) return Vector3.zero;
                float u = (t - TB0 - 0.08f) / 0.42f;
                return aim * (LungeSpeed * Mathf.Sin(u * Mathf.PI));
            }
        }
        // the blade's driver: plain hit 1 (ArcDriver), or in the torch combo hit A then hit B
        void BladeDriver(float tt, out float pitch, out float yaw, out float rad)
        {
            if (!(TorchOn && kind == 1)) { ArcDriver(tt, out pitch, out yaw, out rad); return; }
            if (tt < TB0) { ArcDriver(tt - (TA - ArcLand), out pitch, out yaw, out rad); return; }
            UpDriver(tt - TB0, out pitch, out yaw, out rad);
        }
        // hit B: comes out trailing low behind on the left, then sweeps round the front and rises (an upper-cut), drawn in
        void UpDriver(float u, out float pitch, out float yaw, out float rad)
        {
            const float Out = 0.12f, Sw = 0.38f;
            if (u < Out) { float w = EaseOut(Mathf.Clamp01(u / Out)); pitch = -4f; yaw = -150f; rad = AuthLen * Mathf.Lerp(0.15f, 1f, w); return; }
            float s = Mathf.Clamp01((u - Out) / Sw);
            float e = s < 0.5f ? 2f * s * s : 1f - Mathf.Pow(-2f * s + 2f, 2f) / 2f;
            yaw = Mathf.Lerp(-150f, 25f, e);
            pitch = Mathf.Lerp(-4f, 72f, Mathf.Pow(s, 2.2f));
            float back = u - Out - Sw;
            rad = back <= 0f ? AuthLen : Mathf.MoveTowards(AuthLen, 0f, back * 30f);
        }
        public static bool Overhead = true;
        // v46 (Kido 20:04 "직선 앞으로, 휘지 말고, 바로 앞쪽은 일반검처럼 일직선, 뒤에서 앞으로"): the blade stays a straight line
        // from the hand (per-link lag ~ k^3, was linear) and winds up from straight behind (pitch 176) before it comes over.
        public static bool Straight = true;   // token whipstraight=0 = v45
        public const float ArcLand = 0.36f, ArcSweep = 0.17f, ArcHalf = 36f, ArcLeft = 62f, ArcLag = 0.17f;
        void ArcDriver(float tt, out float pitch, out float yaw, out float rad)
        {
            if (tt <= 0f) { pitch = 40f; yaw = ArcHalf; rad = AuthLen * 0.3f; return; }
            // v42 (Kido 16:13 "약간 더 위에서 아래로"): Overhead = the blade goes up nearly vertical over the head at full length,
            // hangs there a beat, then falls accelerating (slow over the top, fast into the ground). token whipover=0 = v41
            float top = Straight ? 176f : Overhead ? 168f : 150f, radTop = Overhead ? 1f : 0.8f;
            if (tt < BWind) { float w = EaseOut(tt / BWind); pitch = Mathf.Lerp(40f, top, w); yaw = ArcHalf; rad = AuthLen * Mathf.Lerp(0.3f, radTop, w); return; }
            if (tt < ArcLand)
            {
                float w = (tt - BWind) / (ArcLand - BWind);
                float e = Overhead ? Mathf.Pow(w, 2.2f) : w * w * (3f - 2f * w);
                pitch = Mathf.Lerp(top, -4f, e); yaw = ArcHalf; rad = AuthLen * Mathf.Lerp(radTop, 1f, w); return;
            }
            pitch = -4f;
            float sw = Mathf.Clamp01((tt - ArcLand) / ArcSweep);
            // v35 (Kido 11:57): the sweep goes on past the middle all the way to the LEFT edge, then the blade comes back
            yaw = Mathf.Lerp(ArcHalf, -ArcLeft, sw < 0.5f ? 4f * sw * sw * sw : 1f - Mathf.Pow(-2f * sw + 2f, 3f) / 2f);
            float back = tt - ArcLand - ArcSweep;
            if (back > 0f) yaw = Mathf.Lerp(-ArcLeft, 0f, EaseOut(Mathf.Clamp01(back / 0.18f)));
            rad = back <= 0f ? AuthLen : Mathf.MoveTowards(AuthLen, 0f, back * 35f);
        }
        readonly Vector3[] msPts = new Vector3[64]; readonly float[] msLen = new float[64];
        // the driver: reach (m) and angle (deg from the aim) at time tt of the hit
        void MusaDriver(float tt, out float ang, out float rad)
        {
            float u = Mathf.Clamp01((tt - MusaStart) / MusaDur);
            if (tt < MusaStart) { ang = 0f; rad = 0f; return; }
            if (MusaWrap)
            {
                float e = u < 0.5f ? 4f * u * u * u : 1f - Mathf.Pow(-2f * u + 2f, 3f) / 2f;      // the repo's EaseInOut3
                float extend = Mathf.SmoothStep(0f, 1f, u / 0.22f) * (1f - 0.35f * Mathf.SmoothStep(0f, 1f, (u - 0.8f) / 0.2f));
                ang = Mathf.Lerp(-125f, 135f, e);
                rad = AuthLen * extend;
                if (tt > MusaStart + MusaDur) rad = Mathf.MoveTowards(AuthLen * 0.65f, 0f, (tt - MusaStart - MusaDur) * 30f);   // snaps back into the sword
                return;
            }
            float outT = Mathf.SmoothStep(0f, 1f, u / 0.3f), back = Mathf.SmoothStep(0f, 1f, (u - 0.62f) / 0.38f);
            ang = Mathf.Sin(u * Mathf.PI * 2f) * MusaWobble;
            rad = AuthLen * outT * (1f - back * 0.85f);
        }
        int LightVar => LightChain ? (basicCount + 2) % 3 : 0;   // 0 straight down, 1 diagonal, 2 lasso + slam
        public const float DiagSwing = 0.22f, DiagLag = 0.5f, DiagOver = 0.2f, DiagHalf = 19f;
        float extAtStart;                        // blade length when this hit began (the chain flows on, it does not fold)
        // v25 (Kido 15:27: why did the body motion he had called OK change): back to the throw body of v14/v15 (WH_Throw);
        // only the blade is new. Kevin side-slash = token-less alternative.
        public static string DiagClipPath = "Assets/Game/LocoSplit/Art/MixamoRR/WH_Throw.fbx";
        public const float ChopSwish = 0.14f;    // seconds of the sideways swish after the blade lands
        public static string BasicClipPath = "Assets/Game/LocoSplit/Art/MixamoRR/WH_Downward.fbx";
        public static string ThrowClipPath = "Assets/Game/LocoSplit/Art/MixamoRR/WH_Throw.fbx";
        public static string IdleClipPath = "Assets/Game/RoomRun/ThirdParty/KevinIglesiasFree/HumanM@CombatIdle1H01.fbx";
        public static bool ThrowProbe = true;    // v25: throw clip again -> forward-reach probe
        public static string BasicClipAlt = "Assets/Game/RoomRun/ThirdParty/KevinIglesiasFree/HumanM@Attack1H01_R.fbx";   // v11 (token whipclip=kev)
        public static bool FullClipArms = true;
        public static float LegShare = 0.6f, LeftShare = 0f;   // v36 (Kido 12:07: left arm stiff/hanging in hit 1): keyed free arm; token whipleft=0 = clip arm (v14-v35)
        // was: LeftShare = 1f;   // v14: the free arm is the clip's (it is the reference's key feature)  // shots token whiparms=0 = the v10 code-keyed arms on the basic lash
        public static string SweepClipPath = "Assets/Game/LocoSplit/Art/MixamoRR/GS_LowSlash.fbx";   // WH_Spin360.fbx is imported, not wired yet
        WhipBody body; AnimationClip clipBasic, clipSweep, clipIdle; float idleW;
        float bStart, bPeak, bImpact, bodyW, lastCt, clipArmW; bool movingAtk; AnimationClip lastClip;
        // v9 (Kido: "모션만 움직이면서"): keep running while lashing (PoE2 style), only a little slower
        public float MoveScale => kind == 0 ? 1f : kind == 1 ? 0.8f : 0.6f;
        public readonly List<string> Log = new List<string>();

        PlayerController pc; Animator anim;
        Transform rUpper, rFore, rHand, spine, chest, lUpper, lFore, lHand;
        float freeW;
        Transform sword;
        int kind;                 // 0 idle, 1 basic, 2 sweep
        int basicCount;
        float t, freeze, savedAnimSpeed = 1f;
        bool cracked, procQueued, fireWas;
        Vector3 aim = Vector3.forward;
        readonly HashSet<Health> struck = new HashSet<Health>();
        readonly Collider[] overlap = new Collider[128];
        float armW, holdW;

        // chain
        const int N = 22;
        readonly Vector3[] p = new Vector3[N], q = new Vector3[N];
        bool chainInit;
        float seg;

        // drawing
        Mesh tube; GameObject tubeGo; Material leather;
        Mesh blade; GameObject bladeGo; Material steel;
        // real-sword plates
        bool swordCut, swordCutTried;
        Vector3[] pfMid, pfDir, pfNr, pfSd;
        Transform swordT; Vector3 cutA, cutF, cutS; float cutScale, bladeLenLocal; Vector3 bladeBaseLocal;
        Vector3[] cvPos, cvNrm; Vector2[] cvUv; int[] cvPlate; Vector3[] cvOut, cvOutN; int[][] cvTris; Material[] cvMats;
        float ext;                               // 0 = assembled sword, 1 = fully split to Length
        bool lastLashSpiral;
        Vector3 swordAxisL = Vector3.forward, swordFlatL = Vector3.up; bool swordAxisKnown;
        Mesh ribbon; GameObject ribbonGo; Material ribbonMat;
        struct Snap { public Vector3 a, b; public float time; }
        readonly List<Snap> snaps = new List<Snap>(64);
        readonly List<Flash> flashes = new List<Flash>();
        class Flash { public GameObject go; public Material m; public float age, life, size; }
        Mesh starMesh;

        public void Setup(PlayerController owner, Animator animator)
        {
            animator = SuitBody.Attach(animator);   // 11-19 suit body test: the whip drives Joe's bones when on
            pc = owner; anim = animator;
            rUpper = anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
            rFore = anim.GetBoneTransform(HumanBodyBones.RightLowerArm);
            lUpper = anim.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            lFore = anim.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            lHand = anim.GetBoneTransform(HumanBodyBones.LeftHand);
            rHand = anim.GetBoneTransform(HumanBodyBones.RightHand);
            spine = anim.GetBoneTransform(HumanBodyBones.Spine);
            chest = anim.GetBoneTransform(HumanBodyBones.Chest) ?? anim.GetBoneTransform(HumanBodyBones.UpperChest);
            seg = Length / (N - 1);
            BuildDrawing();
            SetupBody();
            CoatTail.Sync(anim);   // 11-19 long coat test
            chainInit = false;
        }

        void SetupBody()
        {
            body = new WhipBody(anim);
            clipBasic = WhipBody.LoadClip(Diag ? DiagClipPath : BasicClipPath, null);
            clipSweep = WhipBody.LoadClip(SweepClipPath, null);
            clipIdle = WhipBody.LoadClip(IdleClipPath, null);
            clipTorch = TorchCombo ? WhipBody.LoadClip(TorchClipPath, null) : null;
            if (!body.Ready || !clipBasic) { Debug.LogWarning($"[RoomRun] whip body clips missing (ready={body.Ready} basic={clipBasic})"); return; }
            // timing probe: the wind-up top = highest right hand in the first 75 %, impact = lowest after it
            const int steps = 60;
            var hh = ThrowProbe ? body.HandForward(clipBasic, steps) : body.HandHeights(clipBasic, steps);
            int peak = 0, low;
            if (ThrowProbe)
            {
                // cocked = blade hand furthest back; release = furthest forward after it
                for (int i = 0; i <= steps * 3 / 4; i++) if (hh[i] < hh[peak]) peak = i;
                low = peak; for (int i = peak; i <= steps; i++) if (hh[i] > hh[low]) low = i;
            }
            else
            {
                for (int i = 0; i <= steps * 3 / 4; i++) if (hh[i] > hh[peak]) peak = i;
                low = peak; for (int i = peak; i <= steps; i++) if (hh[i] < hh[low]) low = i;
            }
            if (low <= peak) low = Mathf.Min(steps, peak + 6);
            float L = clipBasic.length;
            bPeak = L * peak / steps; bImpact = L * low / steps; bStart = bPeak * 0.4f;
            clipChop = WhipBody.LoadClip(BasicClipPath, null);
            clipSide = WhipBody.LoadClip(BasicClipAlt, null);
            ProbeHeight(clipChop, out cStart, out cPeak, out cImpact);
            ProbeHeight(clipSide, out sStart, out sPeak, out sImpact);
            Debug.Log($"[RoomRun] whip body: {clipBasic.name} {L:0.00}s peak {bPeak:0.00}s impact {bImpact:0.00}s; sweep {(clipSweep ? clipSweep.name : "none")}");
        }

        // wind-up top = highest right hand in the first 75 % of the clip, impact = lowest after it
        void ProbeHeight(AnimationClip c, out float st, out float pk, out float im)
        {
            st = pk = im = 0f;
            if (!c || body == null || !body.Ready) return;
            const int steps = 60;
            var hh = body.HandHeights(c, steps);
            int peak = 0; for (int i = 0; i <= steps * 3 / 4; i++) if (hh[i] > hh[peak]) peak = i;
            int low = peak; for (int i = peak; i <= steps; i++) if (hh[i] < hh[low]) low = i;
            if (low <= peak) low = Mathf.Min(steps, peak + 6);
            pk = c.length * peak / steps; im = c.length * low / steps; st = pk * 0.4f;
        }

        // clip time for the current attack phase (the code owns the timing; the clip owns the pose)
        float ClipTime(out AnimationClip clip)
        {
            if (kind == 1)
            {
                if (Body2 && clipChop)
                {
                    bool sideHit = LightVar == 1 && clipSide;
                    clip = sideHit ? clipSide : clipChop;
                    float st = sideHit ? sStart : cStart, pk = sideHit ? sPeak : cPeak, im = sideHit ? sImpact : cImpact;
                    float tH = ArchLand;                                    // the body lands its blow as the blade's root comes down
                    if (t < BWind) return Mathf.Lerp(st, pk, EaseOut(t / BWind));
                    if (t < tH) return Mathf.Lerp(pk, im, Ease((t - BWind) / (tH - BWind)));
                    return Mathf.Min(clip.length, im + (t - tH) * 1.5f);
                }
                if (TorchOn) { clip = clipTorch; return Mathf.Min(t, clip.length); }
                clip = clipBasic;
                float tHit = BWind + BSnap + BUnroll * 0.3f;   // v26: the body's hit time is the v17 one, whatever the blade does
                if (t < BWind) return Mathf.Lerp(bStart, bPeak, EaseOut(t / BWind));
                if (t < tHit) return Mathf.Lerp(bPeak, bImpact, Ease((t - BWind) / (tHit - BWind)));
                return Mathf.Min(clip.length, bImpact + (t - tHit) * 1.7f);   // v13: the reference is upright again 5-6 f after the crack
            }
            clip = clipSweep;
            if (!clip) return 0f;
            float Lc = clip.length;
            if (t < SWind) return Mathf.Lerp(0.05f, 0.33f, EaseOut(t / SWind)) * Lc;
            if (t < SWind + SSweep) return Mathf.Lerp(0.33f, 0.6f, Ease((t - SWind) / SSweep)) * Lc;
            return Mathf.Min(Lc, 0.6f * Lc + (t - SWind - SSweep) * 1.1f);
        }

        void BuildDrawing()
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            leather = new Material(lit) { name = "RR_WhipLeather" };
            // v2: v1 dark brown vanished on the night grass; pale rawhide with a faint self-light so the line reads
            // v7: the leather is now the thin cable between the plates (and the grip)
            leather.SetColor("_BaseColor", new Color(0.12f, 0.11f, 0.12f));
            leather.SetFloat("_Smoothness", 0.5f);
            steel = new Material(lit) { name = "RR_WhipSteel" };
            steel.SetColor("_BaseColor", new Color(0.8f, 0.86f, 0.95f));
            steel.SetFloat("_Metallic", 0.85f);
            steel.SetFloat("_Smoothness", 0.72f);
            steel.EnableKeyword("_EMISSION");
            steel.SetColor("_EmissionColor", new Color(0.26f, 0.38f, 0.62f));   // v20: bright cold steel while split, so every shard reads
            blade = new Mesh { name = "WhipBlade" }; blade.MarkDynamic();
            bladeGo = new GameObject("WhipBlade");
            bladeGo.AddComponent<MeshFilter>().sharedMesh = blade;
            steel.SetFloat("_Cull", 0f);   // plates drawn from both sides
            var br = bladeGo.AddComponent<MeshRenderer>(); br.sharedMaterial = steel;
            tube = new Mesh { name = "WhipTube" }; tube.MarkDynamic();
            tubeGo = new GameObject("WhipTube");
            tubeGo.AddComponent<MeshFilter>().sharedMesh = tube;
            var mr = tubeGo.AddComponent<MeshRenderer>(); mr.sharedMaterial = leather;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

            var trail = Shader.Find("RoomRun/BladeTrail");
            ribbonMat = new Material(trail) { name = "RR_WhipRibbon" };
            // the soul blue of the hero's eye flames / greatsword trail
            ribbonMat.SetColor("_Color", new Color(0.12f, 0.42f, 1.25f, 1f));
            ribbonMat.SetColor("_Edge", new Color(0.8f, 1.1f, 1.45f, 1f));
            ribbonMat.SetFloat("_BodyPow", 1.1f); ribbonMat.SetFloat("_EdgeWidth", 0.18f);
            ribbon = new Mesh { name = "WhipRibbon" }; ribbon.MarkDynamic();
            ghostMat = new Material(trail) { name = "RR_WhipGhost" };
            ghostMat.SetColor("_Color", new Color(0.22f, 0.5f, 1.25f, 1f));      // the hero's soul blue
            ghostMat.SetColor("_Edge", new Color(0.85f, 1.15f, 1.5f, 1f));
            ghostMat.SetFloat("_BodyPow", 1.2f); ghostMat.SetFloat("_EdgeWidth", 0.1f);
            ghost = new Mesh { name = "WhipGhost" }; ghost.MarkDynamic();
            ghostGo = new GameObject("WhipGhost");
            ghostGo.AddComponent<MeshFilter>().sharedMesh = ghost;
            var gr2 = ghostGo.AddComponent<MeshRenderer>(); gr2.sharedMaterial = ghostMat;
            gr2.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ribbonGo = new GameObject("WhipRibbon");
            ribbonGo.AddComponent<MeshFilter>().sharedMesh = ribbon;
            var rr = ribbonGo.AddComponent<MeshRenderer>(); rr.sharedMaterial = ribbonMat;
            rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // crack flash: 10 thin rays round a centre, bright at the centre (uv.y 1) fading out (uv.y 0)
            starMesh = new Mesh { name = "WhipStar" };
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var c = new List<Color>(); var tri = new List<int>();
            for (int i = 0; i < 10; i++)
            {
                float a = i * Mathf.PI * 2f / 10f + (i % 2) * 0.2f;
                float len = i % 2 == 0 ? 1f : 0.55f, w = 0.09f;
                var d = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f); var n = new Vector3(-d.y, d.x, 0f);
                int b = v.Count;
                v.Add(n * w); v.Add(-n * w); v.Add(d * len);
                uv.Add(new Vector2(0f, 1f)); uv.Add(new Vector2(0f, 1f)); uv.Add(new Vector2(0f, 0f));
                c.Add(Color.white); c.Add(Color.white); c.Add(Color.white);
                tri.Add(b); tri.Add(b + 1); tri.Add(b + 2);
            }
            // soft disc behind the rays
            int cb = v.Count; v.Add(Vector3.zero); uv.Add(new Vector2(0f, 1f)); c.Add(new Color(1, 1, 1, 0.5f));
            for (int i = 0; i <= 16; i++)
            {
                float a = i * Mathf.PI * 2f / 16f;
                v.Add(new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * 0.42f); uv.Add(new Vector2(0f, 0.15f)); c.Add(new Color(1, 1, 1, 0.5f));
                if (i > 0) { tri.Add(cb); tri.Add(cb + i); tri.Add(cb + i + 1); }
            }
            starMesh.SetVertices(v); starMesh.SetUVs(0, uv); starMesh.SetColors(c); starMesh.SetTriangles(tri, 0);
        }

        void OnDisable()
        {
            if (sword) sword.gameObject.SetActive(true);
            if (anim && freeze > 0f) anim.speed = savedAnimSpeed;
            freeze = 0f;
            if (tubeGo) tubeGo.SetActive(false);
            if (ribbonGo) ribbonGo.SetActive(false);
            if (bladeGo) bladeGo.SetActive(false);
        }

        void OnEnable()
        {
            if (tubeGo) tubeGo.SetActive(true);
            if (ribbonGo) ribbonGo.SetActive(true);
            if (bladeGo) bladeGo.SetActive(true);
            chainInit = false;
        }

        void OnDestroy()
        {
            if (sword) sword.gameObject.SetActive(true);
            if (tubeGo) Destroy(tubeGo);
            if (ribbonGo) Destroy(ribbonGo);
            if (ghostGo) Destroy(ghostGo);
            if (ghostMat) Destroy(ghostMat);
            if (bladeGo) Destroy(bladeGo);
            if (steel) Destroy(steel);
            body?.Dispose();
            foreach (var f in flashes) if (f.go) Destroy(f.go);
            if (leather) Destroy(leather);
            if (ribbonMat) Destroy(ribbonMat);
        }

        public void Cancel() { kind = 0; procQueued = false; }

        /// Called by PlayerController every frame while the whip is on (instead of the greatsword's attack input).
        public void Tick(float dt, bool fire, bool canAct, Vector3 aimDir)
        {
            if (!canAct) { kind = 0; fireWas = fire; return; }
            if (freeze > 0f)
            {
                freeze -= dt;
                if (freeze <= 0f && anim) anim.speed = savedAnimSpeed;
                fireWas = fire; return;
            }
            if (kind == 0)
            {
                gapLeft -= dt;
                if (fire && (gapLeft <= 0f || procQueued)) StartAttack(procQueued ? 2 : 1, aimDir);
            }
            else
            {
                t += dt;
                if (kind == 1 && TorchOn)
                {
                    if ((t >= TA && t < TB0) || t >= TB0 + 0.14f) SpiralHits();
                    var rigT = RunDirector.Instance ? RunDirector.Instance.Rig : null;
                    if (!slamDone && t >= TA) { slamDone = true; if (rigT) rigT.Shake(0.13f); Freeze(0.05f); }
                    if (!slam2 && t >= TBHit) { slam2 = true; if (rigT) rigT.Shake(0.1f); }
                    if (!cracked && t >= TA + 0.08f) { cracked = true; Crack(); }
                    if (t >= TEnd - 0.08f && fire && DevFireGap <= 0f) StartAttack(ComboWide ? 2 : 1, aimDir);
                    else if (t >= TEnd) { kind = 0; gapLeft = DevFireGap; }
                }
                else if (kind == 1)
                {
                    if (Rake && Diag && Authored && !Arch) RakeHits();
                    if (MusaLash && Authored && MusaArc) { if (t >= ArcLand) SpiralHits(); }
                    // v38 impact: a short hit-stop and a shake the moment the blade lands, hit or not
                    if (Violent && MusaLash && Authored && MusaArc && !slamDone && t >= ArcLand)
                    {
                        slamDone = true;
                        var rigS = RunDirector.Instance ? RunDirector.Instance.Rig : null;
                        if (rigS) rigS.Shake(0.13f);
                        Freeze(0.055f);
                    }
                    else if (MusaLash && Authored && MusaWrap) SpiralHits();
                    float crackAt = MusaLash && Authored && MusaArc ? ArcLand + ArcLag * 0.6f : MusaLash && Authored ? (MusaWrap ? MusaStart + MusaDur + MusaLagTotal : MusaStart + 0.3f * MusaDur) : Arch && Rake && Diag && Authored ? ArchLand + ArchLag : Rake && Diag && Authored ? RakeEnd : Diag && Authored ? BWind + DiagSwing * 0.8f : Chop && Authored ? BWind + BSnap + 0.01f : BWind + BSnap + BUnroll;   // diag: as the tip comes through
                    if ((Spiral || Free) && !Authored) SpiralHits();
                    if (!cracked && t >= crackAt) { cracked = true; Crack(); }
                    if (procQueued && t >= crackAt + 0.1f) StartAttack(2, aimDir);
                    else if (t >= BChain && fire && DevFireGap <= 0f) StartAttack(ComboWide && (!LightChain || basicCount % 3 == 0) ? 2 : 1, aimDir);
                    else if (t >= BEnd) { kind = 0; gapLeft = DevFireGap; }
                }
                else if (kind == 2)
                {
                    SweepHits();
                    if (t >= SEnd - 0.08f && fire && DevFireGap <= 0f) StartAttack(1, aimDir);
                    else if (t >= SEnd) { kind = 0; gapLeft = DevFireGap; }
                }
            }
            fireWas = fire;
        }

        void StartAttack(int k, Vector3 aimDir)
        {
            extAtStart = ext;
            kind = k; t = 0f; cracked = false; slamDone = false; slam2 = false; struck.Clear();
            if (k == 2) procQueued = false; else { basicCount++; spiralSign = -spiralSign; freeMove = (basicCount - 1) % (ComboWide ? 2 : 3); }
            lashLen = k == 2 ? SweepLen : (Spiral || Free) ? SplitLen : Length;
            if (kind != 0 && t == 0f) { var v = pc.Velocity; v.y = 0f; movingAtk = v.magnitude > 0.6f; }
            aim = aimDir; aim.y = 0f; if (aim.sqrMagnitude < 1e-4f) aim = pc.transform.forward; aim.Normalize();
            snaps.Clear();
        }

        Vector3 Origin => pc.transform.position;

        // v17: the spiral lash hits what the blade passes over (any link within 0.75 m), once per lash
        int spiralHitCount;
        void SpiralHits()
        {
            if (ext < 0.15f || !chainInit) return;
            float reach = SplitLen * 0.75f;
            int hits = 0;
            int n = Physics.OverlapSphereNonAlloc(Origin + Vector3.up, reach + 1f, overlap, 1 << Ground.ActorLayer, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                var hl = overlap[i].GetComponentInParent<Health>();
                if (!hl || hl.team == Team.Player || !hl.Alive || struck.Contains(hl)) continue;
                var ep = hl.transform.position; bool near = false;
                for (int j = 2; j < N && !near; j++)
                {
                    var d = p[j] - ep; d.y = 0f;
                    if (d.sqrMagnitude < 0.75f * 0.75f) near = true;
                }
                if (!near) continue;
                struck.Add(hl);
                if (hl.Hit(2)) { hits++; Fx.Burst(hl.transform.position + Vector3.up, Fx.Kind.EnemySpark, 10); }
            }
            if (hits > 0)
            {
                spiralHitCount += hits;
                var rig = RunDirector.Instance ? RunDirector.Instance.Rig : null;
                if (rig) rig.Shake(0.1f);
                if (ForceProc || Random.value < ProcChance) procQueued = true;
            }
        }

        // v28: the rake hits what it has passed: everything in the fan between the edge it landed on and the blade now
        void RakeHits()
        {
            if (t < RakeLand - 0.02f || t > RakeEnd + RakeLag + 0.05f) return;
            float reach = AuthLen + 0.5f;
            int hits = 0;
            int n = Physics.OverlapSphereNonAlloc(Origin + Vector3.up, reach + 0.6f, overlap, 1 << Ground.ActorLayer, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                var hl = overlap[i].GetComponentInParent<Health>();
                if (!hl || hl.team == Team.Player || !hl.Alive || struck.Contains(hl)) continue;
                var d = hl.transform.position - Origin; d.y = 0f;
                float dist = d.magnitude;
                if (dist > reach || dist < 0.01f) continue;
                float ang = Vector3.SignedAngle(aim, d, Vector3.up);          // + = right
                // links further out are later: allow for the lag at this enemy's distance
                float lagDeg = (RakeHalf * 2f) * (RakeLag * Mathf.Pow(Mathf.Clamp01(dist / AuthLen), 1.3f)) / (RakeEnd - RakeLand);
                float blade = rakeYawNow + lagDeg;
                if (ang > RakeHalf + 4f || ang < -RakeHalf - 4f) { if (dist > 1.0f) continue; }
                if (ang < blade - 3f && dist > 1.0f) continue;                // the blade has not reached it yet (it rakes right -> left)
                struck.Add(hl);
                if (hl.Hit(2)) { hits++; Fx.Burst(hl.transform.position + Vector3.up, Fx.Kind.EnemySpark, 14); SpawnFlash(hl.transform.position + Vector3.up * 1.1f, 0.9f, 0.12f); }
            }
            if (hits > 0)
            {
                var rig = RunDirector.Instance ? RunDirector.Instance.Rig : null;
                if (rig) rig.Shake(0.12f);
                if (struck.Count == hits) Freeze(0.045f);
                if (ForceProc || Random.value < ProcChance) procQueued = true;
            }
        }

        void Crack()
        {
            if (MusaLash && Authored && (MusaWrap || MusaArc))
            {
                if (CrackFlash) SpawnFlash(p[N - 1] + Vector3.up * 0.05f, 0.6f, 0.1f);    // the tip finishing the wrap
                return;
            }
            if (MusaLash && Authored)
            {
                // the lash's hit: a teardrop in front (cone from the feet ending in a round head), flash at the tip
                float Lm = AuthLen, Rm = 1.9f;
                if (CrackFlash) SpawnFlash(p[N - 1] + Vector3.up * 0.05f, 0.7f, 0.11f);
                var sideM = Vector3.Cross(Vector3.up, aim);
                int hitsM = 0;
                int nM = Physics.OverlapSphereNonAlloc(Origin + Vector3.up, Lm + 1f, overlap, 1 << Ground.ActorLayer, QueryTriggerInteraction.Collide);
                for (int i = 0; i < nM; i++)
                {
                    var hl = overlap[i].GetComponentInParent<Health>();
                    if (!hl || hl.team == Team.Player || !hl.Alive || struck.Contains(hl)) continue;
                    var to = hl.transform.position - Origin; to.y = 0f;
                    float along = Vector3.Dot(to, aim), lat = Mathf.Abs(Vector3.Dot(to, sideM));
                    float Lc = Lm - Rm, Rr = Rm + 0.4f;
                    bool inside = along >= -0.3f && ((along <= Lc && lat <= Mathf.Lerp(0.5f, Rr, Mathf.Clamp01(along / Lc))) || (to - aim * Lc).magnitude <= Rr);
                    if (!inside) continue;
                    struck.Add(hl);
                    if (hl.Hit(2)) { hitsM++; Fx.Burst(hl.transform.position + Vector3.up, Fx.Kind.EnemySpark, 14); SpawnFlash(hl.transform.position + Vector3.up * 1.1f, 0.9f, 0.12f); }
                }
                var rigM = RunDirector.Instance ? RunDirector.Instance.Rig : null;
                if (rigM) rigM.Shake(hitsM > 0 ? 0.14f : 0.05f);
                if (hitsM > 0) { Freeze(0.05f); if (ForceProc || Random.value < ProcChance) procQueued = true; }
                if (VegetationCut.Instance) VegetationCut.Instance.Sector(Origin, aim, Lm, 16f);
                return;
            }
            if (Arch && Rake && Diag && Authored)
            {
                // the tip slaps down: everything in the fan the falling blade drifted across
                if (CrackFlash) SpawnFlash(p[N - 1] + Vector3.up * 0.05f, 0.7f, 0.11f);
                float reachA = AuthLen + 0.5f, cosA = Mathf.Cos(ArchHalf * Mathf.Deg2Rad);
                int hitsA = 0;
                int nA = Physics.OverlapSphereNonAlloc(Origin + Vector3.up, reachA + 0.6f, overlap, 1 << Ground.ActorLayer, QueryTriggerInteraction.Collide);
                for (int i = 0; i < nA; i++)
                {
                    var hl = overlap[i].GetComponentInParent<Health>();
                    if (!hl || hl.team == Team.Player || !hl.Alive || struck.Contains(hl)) continue;
                    var d = hl.transform.position - Origin; d.y = 0f;
                    float dist = d.magnitude;
                    if (dist > reachA || dist < 0.01f) continue;
                    if (Vector3.Dot(d / dist, aim) < cosA && dist > 1.0f) continue;
                    struck.Add(hl);
                    if (hl.Hit(2)) { hitsA++; Fx.Burst(hl.transform.position + Vector3.up, Fx.Kind.EnemySpark, 14); SpawnFlash(hl.transform.position + Vector3.up * 1.1f, 0.9f, 0.12f); }
                }
                var rigA = RunDirector.Instance ? RunDirector.Instance.Rig : null;
                if (rigA) rigA.Shake(hitsA > 0 ? 0.16f : 0.06f);
                if (hitsA > 0) { Freeze(0.055f); if (ForceProc || Random.value < ProcChance) procQueued = true; }
                if (VegetationCut.Instance) VegetationCut.Instance.Sector(Origin, aim, AuthLen, ArchHalf);
                return;
            }
            if (Rake && Diag && Authored)
            {
                // the end of the rake: the tip's flick
                if (CrackFlash) SpawnFlash(p[N - 1] + Vector3.up * 0.05f, 0.6f, 0.1f);
                if (VegetationCut.Instance) VegetationCut.Instance.Sector(Origin, aim, AuthLen, RakeHalf);
                return;
            }
            if ((Spiral || Free) && !Authored)
            {
                if (CrackFlash) SpawnFlash(p[N - 1] + Vector3.up * 0.05f, 0.7f, 0.12f);
                var rg = RunDirector.Instance ? RunDirector.Instance.Rig : null;
                if (rg) rg.Shake(struck.Count > 0 ? 0.13f : 0.05f);
                if (struck.Count > 0) Freeze(0.05f);
                if (VegetationCut.Instance) VegetationCut.Instance.Sector(Origin, aim, 3.6f, 180f);
                Note($"crack(spiral) t={t:0.000} hits={struck.Count} proc={procQueued}");
                return;
            }
            if (Swish && !Authored)
            {
                // the crack: the tip third kicks back and up (verlet velocity), so it curls back before the blade rejoins
                var kick = (-aim * 0.6f + Vector3.up * 0.45f + Vector3.Cross(Vector3.up, aim) * 0.25f);
                for (int i = N * 2 / 3; i < N; i++)
                {
                    float w = (i - N * 2 / 3) / (float)(N - N * 2 / 3);
                    q[i] -= kick * (0.11f * w);
                }
            }
            var tip = p[N - 1];
            if (CrackFlash) SpawnFlash(tip + Vector3.up * 0.05f, 0.55f, 0.1f);
            int hits = 0;
            float cosHalf = Mathf.Cos((Diag && Authored ? DiagHalf : BasicHalf) * Mathf.Deg2Rad);
            float reach = (Authored ? AuthLen : Length) + 0.5f;   // v20: hit 1 = the planner's narrow long cone, as long as the blade
            int n = Physics.OverlapSphereNonAlloc(Origin + Vector3.up, reach + 0.6f, overlap, 1 << Ground.ActorLayer, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                var h = overlap[i].GetComponentInParent<Health>();
                if (!h || h.team == Team.Player || !h.Alive || struck.Contains(h)) continue;
                var d = h.transform.position - Origin; d.y = 0f;
                float dist = d.magnitude;
                if (dist > reach || dist < 0.01f) continue;
                // the cone, widened a little near the tip so a body the line passes over still counts
                if (Vector3.Dot(d / dist, aim) < cosHalf && Vector3.Cross(aim, d).magnitude > 0.6f) continue;
                struck.Add(h);
                if (h.Hit(2)) { hits++; Fx.Burst(h.transform.position + Vector3.up, Fx.Kind.EnemySpark, 14); SpawnFlash(h.transform.position + Vector3.up * 1.1f, 1.0f, 0.13f); }
            }
            var rig = RunDirector.Instance ? RunDirector.Instance.Rig : null;
            if (rig) rig.Shake(hits > 0 ? 0.13f : 0.05f);
            if (hits > 0)
            {
                Freeze(0.055f);
                if (ForceProc || Random.value < ProcChance) procQueued = true;
            }
            if (VegetationCut.Instance) VegetationCut.Instance.Sector(Origin, aim, reach, BasicHalf);
            Note($"crack t={t:0.000} hits={hits} proc={procQueued}");
        }

        float sweptDeg = float.NegativeInfinity;
        void SweepHits()
        {
            float u = Mathf.Clamp01((t - SWind) / SSweep);
            if (u <= 0f) { sweptDeg = SweepFrom; return; }
            float tipDeg = Mathf.Lerp(SweepFrom, SweepTo, Ease(u)) - SweepLag * 0.5f;
            float reach = SweepLen + 0.4f;
            int hits = 0;
            int n = Physics.OverlapSphereNonAlloc(Origin + Vector3.up, reach + 0.6f, overlap, 1 << Ground.ActorLayer, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                var h = overlap[i].GetComponentInParent<Health>();
                if (!h || h.team == Team.Player || !h.Alive || struck.Contains(h)) continue;
                var d = h.transform.position - Origin; d.y = 0f;
                float dist = d.magnitude;
                if (dist > reach) continue;
                float ang = Vector3.SignedAngle(aim, d, Vector3.up);
                if (dist > 0.8f && (ang < SweepFrom - 5f || ang > tipDeg)) continue;
                struck.Add(h);
                if (h.Hit(3)) { hits++; Fx.Burst(h.transform.position + Vector3.up, Fx.Kind.EnemySpark, 14); SpawnFlash(h.transform.position + Vector3.up * 1.1f, 1.0f, 0.13f); }
            }
            if (hits > 0)
            {
                var rig = RunDirector.Instance ? RunDirector.Instance.Rig : null;
                if (rig) rig.Shake(0.16f);
                if (struck.Count == hits) Freeze(0.045f);   // first contact only
            }
            if (VegetationCut.Instance && tipDeg > sweptDeg + 20f)
            {
                float mid = (sweptDeg + tipDeg) * 0.5f;
                VegetationCut.Instance.Sector(Origin, Quaternion.AngleAxis(mid, Vector3.up) * aim, reach, (tipDeg - sweptDeg) * 0.5f + 2f);
                sweptDeg = tipDeg;
            }
            if (u >= 1f && sweptDeg < SweepTo) sweptDeg = SweepTo;
        }

        void Freeze(float s)
        {
            if (freeze <= 0f && anim) { savedAnimSpeed = anim.speed; anim.speed = 0f; }
            freeze = Mathf.Max(freeze, s);
        }

        // wide hit: blade length over time (out during the first half of the sweep, back in after it)
        float SweepFrac(float tt)
        {
            float a = (tt - SWind * 0.5f) / (SSweep * 0.55f);
            if (a <= 0f) return extAtStart * Mathf.Clamp01(1f - tt / (SWind * 0.5f)) * 0.6f;
            float after = tt - (SWind + SSweep);
            if (after <= 0f) return EaseOut(Mathf.Clamp01(a));
            return Mathf.Clamp01(1f - (after / 0.2f) * (after / 0.2f));
        }

        // v28: the blade's heading over time (deg from the aim, + = right): right edge of the fan while it comes down,
        // across to the left edge while it rakes, a flick past it, and back
        float RakeYaw(float tt)
        {
            if (tt <= RakeLand) return RakeHalf;
            if (tt < RakeEnd) return Mathf.Lerp(RakeHalf, -RakeHalf, Ease((tt - RakeLand) / (RakeEnd - RakeLand)));
            float o = (tt - RakeEnd) / 0.1f;
            return -RakeHalf - 9f * Mathf.Sin(Mathf.Clamp01(o) * Mathf.PI);
        }

        float LenFrac(float tt)
        {
            float grow = BSnap + BUnroll;
            // v21: the blade leaves the hand as the arm comes through (half-way into the snap), not when the snap starts
            if (Authored && Diag && Rake && Arch)
            {
                if (tt < BWind) return Mathf.Lerp(Mathf.Max(0.1f, extAtStart), 0.75f, EaseOut(tt / BWind));     // already long while it goes up and back
                float up2 = (tt - BWind) / 0.08f;
                if (up2 < 1f) return 0.75f + 0.25f * EaseOut(up2);
                if (LightChain) { float k2 = (tt - 0.6f) / 0.25f; return k2 <= 0f ? 1f : 1f - 0.3f * Mathf.Clamp01(k2); }   // stays out and flows into the next hit
                float bk2 = (tt - ArchLand - ArchLag - 0.05f) / 0.15f;
                return bk2 <= 0f ? 1f : Mathf.Clamp01(1f - bk2 * bk2);
            }
            if (Authored && Diag && Rake)
            {
                if (tt < BWind) return Mathf.Lerp(Mathf.Max(0.1f, extAtStart), 0.6f, EaseOut(tt / BWind));
                float up = (tt - BWind) / ((RakeLand - BWind) * 0.7f);
                if (up < 1f) return 0.6f + 0.4f * EaseOut(up);                                   // full length before it lands
                float bk = (tt - RakeEnd - RakeLag - 0.03f) / 0.14f;
                return bk <= 0f ? 1f : Mathf.Clamp01(1f - bk * bk);
            }
            if (Authored && Diag)
            {
                if (tt < BWind) return Mathf.Lerp(Mathf.Max(0.1f, extAtStart), 0.55f, EaseOut(tt / BWind));   // flows on from the last hit's length
                float sw = (tt - BWind) / (DiagSwing * 0.6f);
                if (sw < 1f) return 0.5f + 0.5f * EaseOut(sw);                                  // full length as the swing gets going
                float bk = (tt - BWind - DiagSwing - 0.06f) / 0.2f;
                return bk <= 0f ? 1f : Mathf.Clamp01(1f - bk * bk);
            }
            if (Authored && Chop)
            {
                // chop: the blade is already going out while the hand rises, full length as it comes down; held through
                // the swish, then drawn back
                float up = tt / (BWind + BSnap);
                if (up < 1f) return 0.15f + 0.85f * EaseOut(Mathf.Clamp01(up));
                float bk = (tt - BWind - BSnap - ChopSwish) / 0.2f;
                return bk <= 0f ? 1f : Mathf.Clamp01(1f - bk * bk);
            }
            float late = Authored ? BSnap * 0.5f : 0f;
            float a = (tt - BWind - late) / (grow - late);
            if (a <= 0f) return 0f;
            if (a < 1f) return EaseOut(a);
            float b = (tt - BWind - grow - 0.04f) / 0.2f;
            return b <= 0f ? 1f : Mathf.Clamp01(1f - b * b);
        }

        static float Ease(float u) => u * u * (3f - 2f * u);
        static float EaseOut(float u) { u = Mathf.Clamp01(u); return 1f - (1f - u) * (1f - u); }

        // ---------------- pose + chain ----------------
        void LateUpdate()
        {
            if (!pc || !anim || !rHand) return;
            if (!sword)
            {
                sword = rHand.Find("Greatsword");
                if (!sword) foreach (var tr in rHand.GetComponentsInChildren<Transform>(true)) if (tr.name == "Greatsword") { sword = tr; break; }
            }
            if (sword && sword.gameObject.activeSelf) sword.gameObject.SetActive(false);

            float dt = Time.deltaTime;
            float live = freeze > 0f ? 0f : dt;

            var F = kind != 0 ? aim : Flat(anim.transform.forward);
            var U = Vector3.up;
            var R = Vector3.Cross(U, F);

            // ---- arm / spine keys (directions in the aim frame: x right, y up, z forward) ----
            Vector3 upA, foA; float yaw, pitch;
            HoldPose(out var hUp, out var hFo);
            // v10 one-handed (Kido: "래퍼런스 보고 한손으로해"). Re-read the reference at 15 fps: the FREE (left) arm goes up high,
            // open hand toward the target, while the blade hand is cocked low and back at the right hip; the body unwinds,
            // the blade arm whips forward at shoulder height across the body and the free arm drops back to the hip.
            Vector3 lUp = Vector3.zero, lFo = Vector3.zero; float lw = 0f;
            var rCock = new Vector3(0.55f, -0.55f, -0.62f); var fCock = new Vector3(0.45f, -0.3f, -0.85f);
            var rHit = new Vector3(0.05f, 0.2f, 1f); var fHit = new Vector3(-0.05f, 0.1f, 1f);
            var rThru = new Vector3(-0.45f, -0.2f, 0.85f); var fThru = new Vector3(-0.75f, -0.25f, 0.6f);
            var lRaise = new Vector3(-0.3f, 0.9f, 0.3f); var lRaiseF = new Vector3(-0.15f, 0.95f, 0.25f);
            var lBack = new Vector3(-0.35f, -0.75f, -0.5f); var lBackF = new Vector3(-0.2f, -0.45f, -0.85f);
            var lHang = new Vector3(-0.2f, -0.97f, 0.05f); var lHangF = new Vector3(-0.1f, -0.75f, 0.6f);
            // v36 hit-1 free arm: reaches forward-up at the target in the wind-up, yanks back to a fist at the waist as the
            // blade comes down (counter-pull), then a relaxed bent guard instead of a straight hanging arm
            var lReach = new Vector3(-0.3f, 0.35f, 0.9f); var lReachF = new Vector3(-0.05f, 0.45f, 0.9f);
            var lChamber = new Vector3(-0.3f, -0.8f, -0.5f); var lChamberF = new Vector3(-0.1f, -0.1f, 1f);
            var lGuard = new Vector3(-0.3f, -0.9f, 0.15f); var lGuardF = new Vector3(-0.2f, -0.35f, 0.9f);
            if (kind == 1)
            {
                if (t < BWind)
                {
                    float u = EaseOut(t / BWind);
                    upA = Vector3.Slerp(hUp, rCock, u); foA = Vector3.Slerp(hFo, fCock, u);
                    lUp = Vector3.Slerp(lHang, lReach, u); lFo = Vector3.Slerp(lHangF, lReachF, u);
                }
                else if (t < BWind + BSnap)
                {
                    float u = Ease((t - BWind) / BSnap);
                    // round the outside (not through the hip): via straight out to the right
                    var mU = new Vector3(0.9f, 0.05f, 0.35f); var mF = new Vector3(0.6f, 0.05f, 0.8f);
                    if (u < 0.5f) { upA = Vector3.Slerp(rCock, mU, u * 2f); foA = Vector3.Slerp(fCock, mF, u * 2f); }
                    else { upA = Vector3.Slerp(mU, rHit, u * 2f - 1f); foA = Vector3.Slerp(mF, fHit, u * 2f - 1f); }
                    lUp = Vector3.Slerp(lReach, lChamber, u); lFo = Vector3.Slerp(lReachF, lChamberF, u);
                }
                else
                {
                    float u = Mathf.Clamp01((t - BWind - BSnap) / (BEnd - BWind - BSnap));
                    float ft = Mathf.Sin(Mathf.Min(u * 2.2f, 1f) * Mathf.PI * 0.5f);
                    var upF = Vector3.Slerp(rHit, rThru, ft); var foF = Vector3.Slerp(fHit, fThru, ft);
                    float back = Ease(Mathf.Clamp01((u - 0.45f) / 0.55f));
                    upA = Vector3.Slerp(upF, hUp, back); foA = Vector3.Slerp(foF, hFo, back);
                    lUp = Vector3.Slerp(lChamber, lGuard, back); lFo = Vector3.Slerp(lChamberF, lGuardF, back);
                }
                yaw = 0f; pitch = 0f; lw = 1f;
            }
            else if (kind == 2)
            {
                var wUp = new Vector3(-0.7f, 0.25f, 0.6f); var wFo = new Vector3(-0.95f, 0.15f, -0.1f);   // across the chest, left
                var eUp = new Vector3(0.95f, 0.15f, -0.15f); var eFo = new Vector3(0.75f, 0.0f, -0.6f);   // out right and back
                if (t < SWind)
                {
                    float u = EaseOut(t / SWind);
                    upA = Vector3.Slerp(hUp, wUp, u); foA = Vector3.Slerp(hFo, wFo, u);
                    yaw = Mathf.Lerp(0f, -35f, u); pitch = Mathf.Lerp(0f, 6f, u);
                    lUp = Vector3.Slerp(lHang, new Vector3(-0.85f, 0.2f, -0.45f), u); lFo = Vector3.Slerp(lHangF, new Vector3(-0.8f, 0.1f, -0.55f), u); lw = 1f;
                }
                else if (t < SWind + SSweep)
                {
                    float u = Ease((t - SWind) / SSweep);
                    // go round the front (not through the body): slerp via straight ahead
                    var midU = new Vector3(0.1f, 0.15f, 1f); var midF = new Vector3(0.05f, 0.05f, 1f);
                    if (u < 0.5f) { upA = Vector3.Slerp(wUp, midU, u * 2f); foA = Vector3.Slerp(wFo, midF, u * 2f); }
                    else { upA = Vector3.Slerp(midU, eUp, u * 2f - 1f); foA = Vector3.Slerp(midF, eFo, u * 2f - 1f); }
                    yaw = Mathf.Lerp(-35f, 40f, u); pitch = Mathf.Lerp(6f, 4f, u);
                    lUp = Vector3.Slerp(new Vector3(-0.85f, 0.2f, -0.45f), lBack, u); lFo = Vector3.Slerp(new Vector3(-0.8f, 0.1f, -0.55f), lBackF, u); lw = 1f;
                }
                else
                {
                    float u = Ease(Mathf.Clamp01((t - SWind - SSweep) / (SEnd - SWind - SSweep)));
                    upA = Vector3.Slerp(eUp, hUp, u); foA = Vector3.Slerp(eFo, hFo, u);
                    yaw = Mathf.Lerp(40f, 0f, u); pitch = Mathf.Lerp(4f, 0f, u);
                    lUp = Vector3.Slerp(lBack, lHang, u); lFo = Vector3.Slerp(lBackF, lHangF, u); lw = 1f;
                }
            }
            else { upA = hUp; foA = hFo; yaw = 0f; pitch = 0f; }

            // blend in/out of the keyed arm so the idle carry and the attacks join without pops
            armW = Mathf.MoveTowards(armW, kind != 0 ? 1f : 0f, dt / 0.06f);
            // v3 (Kido: "하체 움직이지도 않고"): legs / hips / torso from the clip, at the time the whip timing asks for
            bool clipBody = UseClipBody && body != null && body.Ready && clipBasic;
            if (clipBody)
            {
                if (kind != 0) { lastCt = ClipTime(out lastClip); }
                bodyW = Mathf.MoveTowards(bodyW, kind != 0 ? 1f : 0f, dt / (kind != 0 ? 0.05f : 0.12f));
                if (bodyW > 0f && lastClip)
                {
                    body.Sample(lastClip, lastCt);
                    bool b2 = Body2 && lastClip != clipSweep && (lastClip == clipChop || lastClip == clipSide);
                    float legs = movingAtk ? 0f : bodyW * (b2 ? 1f : LegShare);   // v13: the reference stays compact, feet almost in place
                    bool full = FullClipArms && kind != 2 && (lastClip == clipBasic || b2 || (clipTorch && lastClip == clipTorch));
                    clipArmW = full ? bodyW : 0f;
                    body.Apply(legs, bodyW * (movingAtk ? 0.6f : 1f), bodyW * (b2 ? 0.7f : 0.4f), clipArmW * (b2 || (clipTorch && lastClip == clipTorch) ? 1f : LeftShare), clipArmW);
                }
                yaw = 0f; pitch = 0f;
            }
            else clipArmW = 0f;
            if (bodyW <= 0f) clipArmW = 0f;
            // v14 base stance: one-handed combat idle (clip) while standing; running keeps the locomotion body
            {
                var iv = pc.Velocity; iv.y = 0f;
                bool idleNow = clipBody && clipIdle && kind == 0 && bodyW <= 0f && iv.magnitude < 0.6f;
                idleW = Mathf.MoveTowards(idleW, idleNow ? 1f : 0f, dt / 0.15f);
                if (idleW > 0f && clipIdle && bodyW <= 0f)
                {
                    body.Sample(clipIdle, Mathf.Repeat(Time.time, clipIdle.length));
                    body.Apply(idleW, idleW * 0.8f, 0f, idleW, idleW);
                    clipArmW = idleW;
                }
            }
            // 11-19 body-clip preview (token whipprev=A,B,..): plays each candidate clip twice at real speed, in turn, on
            // the whole body (no attack), so Kido can pick the whipping motion. Order logged.
            if (PreviewClips != null && body != null && body.Ready)
            {
                if (prevLoaded == null)
                {
                    prevLoaded = new List<AnimationClip>();
                    foreach (var pn in PreviewClips)
                    {
                        var pcl = WhipBody.LoadClip("Assets/Game/LocoSplit/Art/MixamoRR/" + pn + ".fbx", null);
                        if (pcl) prevLoaded.Add(pcl);
                        Debug.Log($"[WhipPreview] {pn}: {(pcl ? pcl.length.ToString("0.00") + " s" : "missing")}");
                    }
                    prevT0 = Time.time;
                }
                if (prevLoaded.Count > 0)
                {
                    float pt = Time.time - prevT0, acc = 0f;
                    int k = 0; float local = 0f;
                    for (int guard = 0; guard < 200; guard++)
                    {
                        var c = prevLoaded[k % prevLoaded.Count];
                        float span = c.length * 2f + 0.4f;
                        if (pt < acc + span) { local = Mathf.Min(Mathf.Repeat(pt - acc, c.length + 0.2f), c.length); break; }
                        acc += span; k++;
                    }
                    body.Sample(prevLoaded[k % prevLoaded.Count], local);
                    body.Apply(1f, 1f, 1f, 1f, 1f);
                    clipArmW = 1f; freeW = 0f;
                }
            }
            holdW = 1f;
            var spineB = spine ? spine : chest;
            if (spineB && (Mathf.Abs(yaw) > 0.01f || Mathf.Abs(pitch) > 0.01f))
                spineB.rotation = Quaternion.AngleAxis(yaw * armW, U) * Quaternion.AngleAxis(pitch * armW, R) * spineB.rotation;
            BodyFollow(dt, U, R);
            Vector3 W(Vector3 a) => (R * a.x + U * a.y + F * a.z).normalized;
            Aim(rUpper, rFore, W(upA), holdW * (1f - clipArmW));
            Aim(rFore, rHand, W(foA), holdW * (1f - clipArmW));
            // free (left) arm: off the grip. Keyed while lashing; hanging while standing; running leaves it to SplitBody's arm pump
            {
                var vv = pc.Velocity; vv.y = 0f;
                float standW = vv.magnitude < 0.6f ? 1f : 0f;
                freeW = Mathf.MoveTowards(freeW, Mathf.Max(lw * armW, standW), dt / 0.1f);
                freeW = Mathf.Min(freeW, 1f - clipArmW * LeftShare);
                if (clipArmW > 0f) freeW = Mathf.Max(freeW, clipArmW * (1f - LeftShare));   // v13: free arm kept near the body (reference), not flung out
                if (PreviewClips != null || (TorchOn && kind == 1)) freeW = 0f;   // preview: the clip owns the left arm
                if (lw <= 0f) { lUp = lHang; lFo = lHangF; }
                if (freeW > 0f) { Aim(lUpper, lFore, W(lUp), freeW); Aim(lFore, lHand, W(lFo), freeW); }
            }
            // hand straight on the forearm, knuckles up-ish
            if (rHand && rFore)
            {
                var fd = (rHand.position - rFore.position).normalized;
                var child = rHand.childCount > 0 ? rHand.GetChild(0) : null;
                if (child && child != sword)
                {
                    var hd = (child.position - rHand.position).normalized;
                    rHand.rotation = Quaternion.FromToRotation(hd, fd) * rHand.rotation;
                }
            }

            // split for the lash (fast), snap back together after it
            // v13: the reference lash is gone ~5 f after the crack: rejoin right after it instead of lying out until the swing ends
            bool outNow = kind == 2 || (kind == 1 && t < BWind + BSnap + BUnroll + (Swish ? 0.14f : 0.1f));
            bool spiralNow = (Spiral || Free) && kind == 1;
            if (MusaLash && Authored && kind == 1 && MusaArc) { BladeDriver(t - ArcLag * 0.5f, out _, out _, out float ar2); ext = Mathf.Clamp01(ar2 / (AuthLen * 0.35f)); }
            else if (MusaLash && Authored && kind == 1) { MusaDriver(t - MusaLagTotal * 0.5f, out _, out float mr); ext = Mathf.Clamp01(mr / (AuthLen * 0.35f)); }
            else if (spiralNow) ext = LenFrac(t);
            else if (kind == 2 && SweepSpiral) ext = SweepFrac(t);
            else ext = Mathf.MoveTowards(ext, outNow ? 1f : 0f, dt / (outNow ? 0.07f : 0.13f));
            if (kind == 1) lastLashSpiral = spiralNow; else if (kind == 2) lastLashSpiral = false;
            if (lashLen <= 0f) lashLen = Length;
            if (!(kind == 2 && SweepSpiral && ext > 0.02f) && !(kind == 1 && MusaLash && Authored && ext > 0.02f)) seg = Mathf.Lerp(SwordLen, lashLen, ext) / (N - 1);
            SwapPlateLook(ext > 0.06f);
            MeasureSword();
            if (RealSword && !swordCut && !swordCutTried && swordAxisKnown) CutSword();
            Simulate(live, F, U, R);
            Assemble();
            DrawTube();
            if (swordCut) DrawSwordPlates(); else DrawBlade();
            UpdateRibbon(dt);
            UpdateGhost();
            UpdateFlashes(dt);
            if (LogFrames) Note($"f{Time.frameCount} kind={kind} t={t:0.000} tip={p[N - 1].x:0.00},{p[N - 1].y:0.00},{p[N - 1].z:0.00}");
        }

        // v37 (Kido 12:45: "the body looks stiff next to the weapon"): a reactive layer ON TOP of the WH_Throw clip, hit 1 only.
        // The torso is dragged by the blade: chest/waist twist after the blade's sweep angle, lean back while it goes up over
        // the shoulder, a forward slam at the landing, the head looks after the tip, and an opposite wind-back as it is drawn
        // in. Springs, so it overshoots and settles instead of following rigidly. Token whipfollow=0 = off (v36).
        public static bool BodyFollowOn = true;
        // v38 (Kido 12:45 "좀 격동적으로"): bigger twist/lean, a half-step lunge and drop at the slam, landing hit-stop. token whipviolent=0 = v37
        public static bool Violent = true;
        bool slamDone; Transform hipsB; float bfLunge, bfLungeV;
        float bfTwist, bfTwistV, bfLean, bfLeanV;
        Transform headB;
        void BodyFollow(float dt, Vector3 U, Vector3 R)
        {
            float tw = 0f, ln = 0f;
            if (BodyFollowOn && kind == 1 && MusaLash && Authored && MusaArc && !TorchOn)
            {
                ArcDriver(t - ArcLag * 0.35f, out float bp, out float by, out _);
                tw = by * (Violent ? 0.85f : 0.55f);                                                    // the blade's heading pulls the chest round
                float lb = Violent ? -18f : -10f, lf = Violent ? 32f : 20f;
                if (t < BWind) ln = lb * EaseOut(t / BWind);                         // leans back while it goes up over the shoulder
                else if (t < ArcLand + 0.03f) { float w = (t - BWind) / (ArcLand + 0.03f - BWind); ln = Mathf.Lerp(lb, lf, w * w * w); }   // slow then all at once into it
                else ln = Mathf.Lerp(lf, 6f, Ease(Mathf.Clamp01((t - ArcLand - 0.03f) / 0.35f)));
                float back = t - ArcLand - ArcSweep;
                if (back > 0f) tw += (Violent ? 22f : 14f) * Mathf.Sin(Mathf.Clamp01(back / 0.22f) * Mathf.PI);   // draw-in: winds back the other way
            }
            float lg = 0f;
            if (Violent && BodyFollowOn && kind == 1 && MusaLash && Authored && MusaArc && !TorchOn)
            {
                // half step into the slam: hips go forward and down, held, then back
                if (t > BWind) lg = t < ArcLand ? Ease((t - BWind) / (ArcLand - BWind)) : 1f - Ease(Mathf.Clamp01((t - ArcLand - 0.15f) / 0.3f));
            }
            if (dt > 0f)
            {
                bfTwist = Mathf.SmoothDamp(bfTwist, tw, ref bfTwistV, 0.045f, Mathf.Infinity, dt);
                bfLean = Mathf.SmoothDamp(bfLean, ln, ref bfLeanV, 0.05f, Mathf.Infinity, dt);
                bfLunge = Mathf.SmoothDamp(bfLunge, lg, ref bfLungeV, 0.04f, Mathf.Infinity, dt);
            }
            if (bfLunge > 0.001f)
            {
                if (!hipsB) hipsB = anim.GetBoneTransform(HumanBodyBones.Hips);
                if (hipsB) hipsB.position += Vector3.Cross(R, U) * (0.22f * bfLunge) + U * (-0.09f * bfLunge);
            }
            if (Mathf.Abs(bfTwist) < 0.05f && Mathf.Abs(bfLean) < 0.05f) return;
            // spread over waist and chest so it bends instead of turning as one block
            if (spine) spine.rotation = Quaternion.AngleAxis(bfTwist * 0.4f, U) * Quaternion.AngleAxis(bfLean * 0.5f, R) * spine.rotation;
            if (chest && chest != spine) chest.rotation = Quaternion.AngleAxis(bfTwist * 0.6f, U) * Quaternion.AngleAxis(bfLean * 0.5f, R) * chest.rotation;
            if (!headB) headB = anim.GetBoneTransform(HumanBodyBones.Head);
            if (headB) headB.rotation = Quaternion.AngleAxis(bfTwist * 0.35f, U) * Quaternion.AngleAxis(-bfLean * 0.4f, R) * headB.rotation;   // eyes stay on the tip
        }

        void HoldPose(out Vector3 up, out Vector3 fo)
        {
            // relaxed carry: upper arm hangs a little out, forearm forward-down, the whip trails on the ground
            up = new Vector3(0.28f, -0.94f, 0.12f);
            fo = new Vector3(0.18f, -0.55f, 0.8f);
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-5f ? v.normalized : Vector3.forward; }

        static void Aim(Transform bone, Transform child, Vector3 want, float w)
        {
            if (!bone || !child || w <= 0f) return;
            var cur = (child.position - bone.position).normalized;
            var rot = Quaternion.FromToRotation(cur, want);
            bone.rotation = Quaternion.Slerp(Quaternion.identity, rot, w) * bone.rotation;
        }

        Vector3 HandDir()
        {
            var d = rHand.position - rFore.position;
            return d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.down;
        }

        void Simulate(float dt, Vector3 F, Vector3 U, Vector3 R)
        {
            var hand = rHand.position;
            var hd = HandDir();
            var root = swordCut ? swordT.TransformPoint(bladeBaseLocal) : hand + hd * 0.07f;
            float ground = pc.transform.position.y + 0.025f;
            if (!chainInit)
            {
                for (int i = 0; i < N; i++)
                {
                    var pos = root - F * (i * seg * 0.6f) + R * (i * seg * 0.3f);
                    pos.y = Mathf.Max(ground, root.y - i * seg);
                    p[i] = q[i] = pos;
                }
                chainInit = true;
            }
            if (dt <= 0f) { p[0] = root; return; }

            bool freeNow = Free && kind == 1;
            int sub = freeNow ? 6 : 3;
            float h = dt / sub;
            for (int s = 0; s < sub; s++)
            {
                float ts = t - dt + h * (s + 1);
                for (int i = 1; i < N; i++)
                {
                    var v = (p[i] - q[i]) * (freeNow ? 0.996f : kind == 2 ? 0.992f : 0.985f);
                    q[i] = p[i];
                    p[i] += v + Vector3.down * (freeNow ? 5f : 16f) * h * h;
                }
                // keyed shape pull
                if (kind != 0) Pull(ts, h, root, F, U, R);
                p[0] = root;
                for (int it = 0; it < 14; it++)
                {
                    p[0] = root;
                    for (int i = 0; i < N - 1; i++)
                    {
                        var d = p[i + 1] - p[i];
                        float len = d.magnitude;
                        if (len < 1e-6f) continue;
                        float diff = (len - seg) / len;
                        if (freeNow && i == 0) continue;                    // link 1 is the driven handle end
                        if (i == 0 || (freeNow && i == 1)) p[i + 1] -= d * diff;
                        else { p[i] += d * diff * 0.5f; p[i + 1] -= d * diff * 0.5f; }
                    }
                    // v3 bend stiffness: i..i+2 may not come closer than 1.7 segments (no zigzag coils)
                    for (int i = 0; i < N - 2; i++)
                    {
                        var d2 = p[i + 2] - p[i];
                        float l2 = d2.magnitude, min2 = seg * (freeNow ? 1.88f : 1.7f);   // free rope: stiffer against folding (no knots)
                        if (l2 >= min2 || l2 < 1e-6f) continue;
                        var push = d2 * ((min2 - l2) / l2) * 0.5f;
                        if (i == 0) p[i + 2] += push * 2f; else { p[i] -= push; p[i + 2] += push; }
                    }
                    for (int i = 1; i < N; i++)
                        if (p[i].y < ground)
                        {
                            p[i].y = ground;
                            // ground friction
                            var dq = p[i] - q[i]; dq.y = 0f; q[i] += dq * 0.12f;
                        }
                }
            }
        }

        void Pull(float ts, float h, Vector3 root, Vector3 F, Vector3 U, Vector3 R)
        {
            float groundY = pc.transform.position.y;
            if (kind == 1 && Free)
            {
                // ---- the driver: where the handle end of the blade is thrown ----
                float grow = BSnap + BUnroll;
                float u = Mathf.Clamp01((ts - BWind) / grow);
                float sw = Mathf.Pow(u, 1.7f);                              // slow start, fastest right before the dead stop
                float side = freeMove == 1 ? -1f : 1f;
                float th0 = freeMove == 2 ? 480f : 150f, th1 = -10f;       // degrees from the aim, toward the lash side; overhead = 1.5 turns
                float th = Mathf.Lerp(th0, th1, sw) * side * Mathf.Deg2Rad;
                float rh = Mathf.Lerp(0.5f, 0.95f, u);
                float hh = freeMove == 2 ? Mathf.Lerp(0.55f, 0.15f, sw) : 0.12f;
                var handle = root + (F * Mathf.Cos(th) + R * Mathf.Sin(th)) * rh + U * hh;
                float g = LenFrac(ts);
                p[1] = Vector3.Lerp(root + SwordAxis * seg, handle, Mathf.Clamp01(g * 8f));
                float lenNow = Mathf.Lerp(SwordLen, FreeLen, g);
                float after = ts - (BWind + grow);
                for (int i = 2; i < N; i++)
                {
                    float sN = i / (float)(N - 1);
                    Vector3 acc = Vector3.zero;
                    // stay near a shallow plane: hand height at the root, knee height at the tip (so it reads from the top-down camera)
                    float yT = Mathf.Lerp(root.y + hh, groundY + 0.45f, Mathf.Pow(sN, 0.6f));
                    acc.y += (yT - p[i].y) * 45f;
                    // late aim assist: the outer half is drawn toward the cursor line so the crack lands where the player points
                    if (u > 0.6f && after < 0.06f)
                    {
                        var onLine = root + F * (sN * lenNow); onLine.y = p[i].y;
                        acc += (onLine - p[i]) * (18f * (u - 0.6f) / 0.4f * Mathf.Clamp01((sN - 0.3f) * 2f));
                    }
                    p[i] += acc * h * h;
                }
                return;
            }
            if (kind == 1 && MusaLash && Authored)
            {
                // node j replays the driver j*lag seconds late at reach * j/M, plus the ripple; then re-spaced by arc length
                int M3 = msPts.Length;
                float rip = Time.time * 26f;
                var hand0 = root;
                for (int j = 0; j < M3; j++)
                {
                    float kn = j / (float)(M3 - 1);
                    if (MusaArc)
                    {
                        BladeDriver(ts - ArcLag * (Straight ? 0.45f * kn * kn * kn : kn), out float pc0, out float yc0, out float rc0);   // v46 Straight: the inner blade has no lag (a sword line), only the last links trail a little
                        float pr = pc0 * Mathf.Deg2Rad, yr = yc0 * Mathf.Deg2Rad;
                        var d3 = (F * Mathf.Cos(yr) + R * Mathf.Sin(yr)) * Mathf.Cos(pr) + U * Mathf.Sin(pr);
                        var pA = root + d3 * (rc0 * kn);
                        if (pA.y < groundY + 0.14f) pA.y = groundY + 0.14f;
                        msPts[j] = pA;
                        msLen[j] = j == 0 ? 0f : msLen[j - 1] + (msPts[j] - msPts[j - 1]).magnitude;
                        continue;
                    }
                    MusaDriver(ts - MusaLagTotal * kn, out float aD, out float rD);
                    float ar = aD * Mathf.Deg2Rad;
                    var dirM = F * Mathf.Cos(ar) + R * Mathf.Sin(ar);
                    var sideM2 = Vector3.Cross(Vector3.up, dirM);
                    float ripple = Mathf.Sin(rip - kn * 13.6f) * 0.22f * kn;   // snake wave running down the chain
                    var pM = root + dirM * (rD * kn) + sideM2 * ripple;
                    pM.y = Mathf.Lerp(hand0.y, groundY + 0.55f, kn) + Mathf.Sin(rip * 0.7f - kn * 9.6f) * 0.12f * kn;
                    msPts[j] = pM;
                    msLen[j] = j == 0 ? 0f : msLen[j - 1] + (msPts[j] - msPts[j - 1]).magnitude;
                }
                float totalM = Mathf.Max(msLen[M3 - 1], SwordLen);
                seg = totalM / (N - 1);
                float aMs = 1f - Mathf.Exp(-300f * h);
                int jm = 1;
                for (int i = 1; i < N; i++)
                {
                    float want = totalM * i / (N - 1);
                    while (jm < M3 - 1 && msLen[jm] < want) jm++;
                    float l0 = msLen[jm - 1], l1 = msLen[jm];
                    var tg = Vector3.Lerp(msPts[jm - 1], msPts[jm], l1 > l0 ? (want - l0) / (l1 - l0) : 0f);
                    if (i >= 2) p[i] = Vector3.Lerp(p[i], tg, aMs);
                }
                return;
            }
            if (kind == 2 && SweepSpiral)
            {
                // The blade is a spiral arm round the hero: link angle = sweep angle - lag * s. It starts wound tight (lag 250
                // deg: wrapped round the body), the sweep carries it once round, and the lag opens to 40 deg so the wound part
                // whips out to full reach at the end. Then it winds back in. Sampled, then re-spaced evenly along its length.
                float uS = Mathf.Clamp01((ts - SWind) / SSweep);
                float afterS = Mathf.Max(0f, ts - (SWind + SSweep));
                float gS = SweepFrac(ts);
                float sweepAng = Mathf.Lerp(SweepFrom - 40f, SweepTo, Ease(uS)) + 60f * Mathf.Clamp01(afterS / 0.2f);
                float lag = Mathf.Lerp(250f, 40f, Mathf.Pow(uS, 1.15f)) + 200f * Mathf.Clamp01(afterS / 0.2f);
                float reach = SweepLen * (0.2f + 0.8f * gS);
                var cen = pc.transform.position; cen.y = root.y;
                int M2 = spPts.Length;
                for (int j = 0; j < M2; j++)
                {
                    float sj = j / (float)(M2 - 1);
                    float ang = (sweepAng - lag * sj) * Mathf.Deg2Rad;
                    var pos = cen + (F * Mathf.Cos(ang) + R * Mathf.Sin(ang)) * (0.4f + sj * reach);
                    pos.y = Mathf.Lerp(root.y, groundY + 0.5f, Mathf.Pow(sj, 0.7f));
                    spPts[j] = Vector3.Lerp(root, pos, Mathf.Clamp01(sj * 8f));
                    spLen[j] = j == 0 ? 0f : spLen[j - 1] + (spPts[j] - spPts[j - 1]).magnitude;
                }
                float total = Mathf.Max(spLen[M2 - 1], SwordLen);
                seg = total / (N - 1);
                float a3 = 1f - Mathf.Exp(-260f * h);
                int jj = 1;
                for (int i = 1; i < N; i++)
                {
                    float want = total * i / (N - 1);
                    while (jj < M2 - 1 && spLen[jj] < want) jj++;
                    float l0 = spLen[jj - 1], l1 = spLen[jj];
                    var tg = Vector3.Lerp(spPts[jj - 1], spPts[jj], l1 > l0 ? (want - l0) / (l1 - l0) : 0f);
                    if (i >= 2) p[i] = Vector3.Lerp(p[i], tg, a3 * Mathf.Clamp01(gS * 8f));
                }
                return;
            }
            if (kind == 1 && Spiral && Authored && !Free && Diag && Rake && Arch)
            {
                float gA = LenFrac(ts);
                float segA = Mathf.Lerp(SwordLen, AuthLen, gA) / (N - 1);
                var posA = root;
                float aA = 1f - Mathf.Exp(-320f * h);
                for (int i = 1; i < N; i++)
                {
                    float sN = i / (float)(N - 1);
                    float ti = ts - ArchLag * Mathf.Pow(sN, 1.2f);                 // this link is this far behind the hand
                    // pitch in the swing plane: 0 = flat forward, 90 = straight up, 150 = up and back over the shoulder
                    int lv = LightVar;
                    float pitch, yaw, roll;
                    if (lv == 2 && ti < BWind)
                    {
                        // lasso: one circle above the head before the slam
                        float az = 360f * Mathf.Clamp01(ti / BWind) * Mathf.Deg2Rad;
                        var ring = (F * Mathf.Cos(az) + R * Mathf.Sin(az)) * Mathf.Cos(62f * Mathf.Deg2Rad) + U * Mathf.Sin(62f * Mathf.Deg2Rad);
                        posA += ring.normalized * segA;
                        var tgl = posA;
                        if (i >= 2) p[i] = Vector3.Lerp(p[i], tgl, aA);
                        continue;
                    }
                    float top = lv == 0 ? 150f : lv == 1 ? 138f : 62f;      // where the wind-up leaves the blade
                    if (ti <= 0f) pitch = 40f;
                    else if (ti < BWind) pitch = Mathf.Lerp(40f, top, EaseOut(ti / BWind));
                    else if (ti < ArchLand) { float w = (ti - BWind) / (ArchLand - BWind); pitch = Mathf.Lerp(top, -4f, w * w * (3f - 2f * w)); }
                    else
                    {
                        // the slap on the ground, then the bounce: the blade springs back up as an arch (reference: every hit)
                        float bo = (ti - ArchLand) / 0.2f;
                        pitch = -4f + (LightChain ? 46f * Mathf.Sin(Mathf.Clamp01(bo) * Mathf.PI) * (1f - 0.35f * Mathf.Clamp01(bo)) : 0f);
                        if (LightChain && bo > 1f) pitch = Mathf.Lerp(-4f, 40f, Mathf.Clamp01((bo - 1f) / 0.6f));   // lifts toward the next wind-up
                    }
                    // hit 1 straight (a small drift), hit 2 on a plane tipped to the weapon side (diagonal), hit 3 tipped the other way
                    roll = lv == 0 ? 0f : lv == 1 ? 44f : -14f;
                    float drift = lv == 1 ? ArchDrift * 0.5f : lv == 0 ? 8f : 12f;
                    float dw = Mathf.Clamp01((ti - BWind - 0.04f) / (ArchLand + 0.03f - BWind - 0.04f));
                    yaw = Mathf.Lerp(drift, -drift, Ease(dw));
                    float pr = pitch * Mathf.Deg2Rad, yr = yaw * Mathf.Deg2Rad, rr = roll * Mathf.Deg2Rad;
                    var upAx = U * Mathf.Cos(rr) + R * Mathf.Sin(rr);
                    var sideAx = R * Mathf.Cos(rr) - U * Mathf.Sin(rr);
                    var dir = (F * Mathf.Cos(yr) + sideAx * Mathf.Sin(yr)) * Mathf.Cos(pr) + upAx * Mathf.Sin(pr);
                    posA += dir * segA;
                    var tgt = posA;
                    if (tgt.y < groundY + 0.14f) tgt.y = groundY + 0.14f;
                    if (i >= 2) p[i] = Vector3.Lerp(p[i], tgt, aA);
                }
                return;
            }
            if (kind == 1 && Spiral && Authored && !Free && Diag && Rake)
            {
                float gR = LenFrac(ts);
                float segR = Mathf.Lerp(SwordLen, AuthLen, gR) / (N - 1);
                var dBack = (F * -0.3f + R * 0.8f + U * 0.62f).normalized;            // high behind the weapon shoulder
                rakeYawNow = RakeYaw(ts);
                var posR = root;
                float aR = 1f - Mathf.Exp(-320f * h);
                for (int i = 1; i < N; i++)
                {
                    float sN = i / (float)(N - 1);
                    float ti = ts - RakeLag * Mathf.Pow(sN, 1.3f);                     // this link is this much behind the hand
                    Vector3 dir;
                    if (ti <= BWind) dir = dBack;
                    else
                    {
                        float yawI = RakeYaw(ti) * Mathf.Deg2Rad;
                        var flat = F * Mathf.Cos(yawI) + R * Mathf.Sin(yawI);
                        flat = (flat + U * -0.03f).normalized;
                        if (ti < RakeLand) dir = Vector3.Slerp(dBack, flat, Ease((ti - BWind) / (RakeLand - BWind))).normalized;   // coming over and down
                        else dir = flat;                                                                                          // raking the ground
                    }
                    posR += dir * segR;
                    var tgt = posR;
                    if (tgt.y < groundY + 0.14f) tgt.y = groundY + 0.14f;
                    if (i >= 2) p[i] = Vector3.Lerp(p[i], tgt, aR);
                }
                return;
            }
            if (kind == 1 && Spiral && Authored && !Free && Diag)
            {
                float ds = 1f;                                               // v25: always from the throwing (right) side, so body and blade agree
                float gD = LenFrac(ts);
                float segD = Mathf.Lerp(SwordLen, AuthLen, gD) / (N - 1);
                // the swing: from high and behind on the swing side, to low across the front on the other side
                var d0 = (F * -0.3f + R * (0.8f * ds) + U * 0.62f).normalized;
                // v27 (Kido 15:32: the planner image's range): the low part of the swing only crosses the narrow cone;
                // it ends on the cone's far edge (18 deg), the tip's overshoot flicks a little past it and comes back
                var d1 = (F * 0.95f + R * (-0.31f * ds) + U * -0.04f).normalized;
                float wS = Mathf.Clamp01((ts - BWind) / DiagSwing);
                float w = Mathf.Pow(wS, 1.35f) * (1f + DiagLag + DiagOver);  // runs past 1 so the late tip also gets there (and over)
                float afterD = ts - (BWind + DiagSwing);
                float overNow = DiagOver * (afterD <= 0f ? 1f : Mathf.Clamp01(1f - afterD / 0.07f));   // the tip's flick back
                float curlBack = afterD > 0.06f ? Mathf.Clamp01((afterD - 0.06f) / 0.2f) : 0f;          // coils toward the hand as it is drawn in
                var posD = root;
                float aD = 1f - Mathf.Exp(-320f * h);
                for (int i = 1; i < N; i++)
                {
                    float sN = i / (float)(N - 1);
                    float wi = Mathf.Clamp(w - DiagLag * Mathf.Pow(sN, 1.4f), 0f, 1f + overNow * sN);   // the outer links lag more: the bend sharpens toward the tip
                    wi -= curlBack * 0.5f * sN * sN;
                    var dir = Vector3.SlerpUnclamped(d0, d1, wi).normalized;
                    posD += dir * segD;
                    var tgt = posD;
                    if (tgt.y < groundY + 0.14f) tgt.y = groundY + 0.14f;
                    if (i >= 2) p[i] = Vector3.Lerp(p[i], tgt, aD);
                }
                return;
            }
            if (kind == 1 && Spiral && Authored && !Free && Chop)
            {
                // ---- overhead chop: the blade swings in the vertical plane of the cursor, then swishes across the cone ----
                float cs = (basicCount % 2 == 0) ? -1f : 1f;                 // which way the landing swish goes, alternating
                float gC = LenFrac(ts);
                float segC = Mathf.Lerp(SwordLen, AuthLen, gC) / (N - 1);
                float tImp = BWind + BSnap;
                float pitch, lagP;                                           // blade pitch at the hand (deg, 0 = flat forward, 90 = straight up)
                if (ts < BWind) { float w = EaseOut(ts / BWind); pitch = Mathf.Lerp(25f, 118f, w); lagP = Mathf.Lerp(0f, 35f, w); }          // up and a little back, tip trailing behind
                else if (ts < tImp) { float w = (ts - BWind) / BSnap; w = w * w; pitch = Mathf.Lerp(118f, -6f, w); lagP = Mathf.Lerp(35f, 70f, Mathf.Sin(w * Mathf.PI)); }   // comes over: the tip lags high and whips down last
                else { pitch = -6f; lagP = 0f; }
                float sw = Mathf.Clamp01((ts - tImp) / ChopSwish);
                // lands on one edge of the narrow cone and swishes to the other; the tip trails, so it reads as a flick
                float yaw = Mathf.Lerp(-BasicHalf, BasicHalf, Ease(sw)) * cs;
                float lagY = 34f * Mathf.Sin(sw * Mathf.PI) * cs;
                float after = ts - tImp - ChopSwish;
                if (after > 0f) lagY = -40f * cs * Mathf.Clamp01(after / 0.2f);          // curls the other way as it is drawn back
                var pos = root;
                float aC = 1f - Mathf.Exp(-300f * h);
                for (int i = 1; i < N; i++)
                {
                    float sN = i / (float)(N - 1);
                    float pi = (pitch + lagP * sN) * Mathf.Deg2Rad;
                    float yi = (yaw - lagY * sN * sN) * Mathf.Deg2Rad;
                    var flat = F * Mathf.Cos(yi) + R * Mathf.Sin(yi);
                    pos += (flat * Mathf.Cos(pi) + U * Mathf.Sin(pi)) * segC;
                    var tgt = pos;
                    if (tgt.y < groundY + 0.12f) tgt.y = groundY + 0.12f;
                    if (ts >= tImp) tgt.y = Mathf.Lerp(tgt.y, Mathf.Lerp(root.y, groundY + 0.35f, Mathf.Clamp01(i * segC / 1.2f)), 0.85f);
                    if (i >= 2) p[i] = Vector3.Lerp(p[i], tgt, aC);
                }
                return;
            }
            if (kind == 1 && Spiral && Authored && !Free)
            {
                float grow = BSnap + BUnroll;
                float g = LenFrac(ts);
                float segNow = Mathf.Lerp(SwordLen, AuthLen, g) / (N - 1);
                float side = freeMove == 1 ? -1f : 1f;
                // v21 (Kido: the narrow hit's body and blade bend did not belong together): no coil beside the hand any more.
                // The blade is thrown out of the hand along the arm; only its outer part is curled back (150 deg), in a plane
                // tilted between up and the throwing side, and it uncurls as it flies: a lash off an overhand throw.
                float coil = 150f;
                float u = Mathf.Clamp01((ts - BWind - BSnap * 0.5f) / (grow - BSnap * 0.5f));
                float after = ts - (BWind + grow);
                float turn;
                if (after <= 0f) turn = Mathf.Lerp(coil, -16f, Mathf.Pow(u, 1.3f));                 // uncurl, a little past straight
                else if (after < 0.04f) turn = Mathf.Lerp(-16f, 8f, after / 0.04f);                 // the snap back through straight
                else turn = Mathf.Lerp(8f, coil * 0.6f, Ease(Mathf.Clamp01((after - 0.04f) / 0.2f)));    // curls as it is drawn back
                turn *= side * Mathf.Deg2Rad;
                // integrate the arc in 2D (x = forward, y = right); turn weight grows toward the tip
                float wSum = 0f;
                for (int i = 1; i < N; i++) wSum += Mathf.Lerp(0.15f, 2.2f, i / (float)(N - 1));
                float head = 0f; var pt = Vector2.zero; authPts[0] = pt;
                for (int i = 1; i < N; i++)
                {
                    head += turn * Mathf.Lerp(0.15f, 2.2f, i / (float)(N - 1)) / wSum;
                    pt += new Vector2(Mathf.Cos(head), Mathf.Sin(head)) * segNow;
                    authPts[i] = pt;
                }
                // turn the whole curve so the tip lies on the cursor line (the lash always ends where the player points)
                var tip = authPts[N - 1];
                float rot = tip.sqrMagnitude > 1e-4f ? -Mathf.Atan2(tip.y, tip.x) : 0f;
                // while coiled the chord is short and its direction spins: ease the correction in with the uncoil
                rot *= Mathf.Clamp01(after <= 0f ? u * 1.2f : 1f) * 0.6f;   // v21: mostly leave it where the arm threw it
                float cr = Mathf.Cos(rot), sr = Mathf.Sin(rot);
                float a2 = 1f - Mathf.Exp(-260f * h);
                for (int i = 2; i < N; i++)
                {
                    var q2 = authPts[i];
                    float fx = q2.x * cr - q2.y * sr, ry = q2.x * sr + q2.y * cr;
                    // the curl's plane: half up, half to the throwing side; it settles flat and low as the lash lands
                    var curlAxis = (U * 0.75f + R * (0.65f * side)).normalized;
                    var tgt = root + F * fx + curlAxis * (ry * side);
                    float flatY = Mathf.Lerp(root.y, groundY + 0.45f, Mathf.Clamp01(i * segNow / 1.4f));
                    float land = after <= 0f ? u * u : 1f;
                    tgt.y = Mathf.Max(groundY + 0.1f, Mathf.Lerp(tgt.y, flatY, land));
                    p[i] = Vector3.Lerp(p[i], tgt, a2 * Mathf.Clamp01(g * 8f));
                }
                return;
            }
            if (kind == 1 && Spiral)
            {
                // heading turns by ds / radius, the radius opens with distance: a spiral that widens as it leaves the hand.
                float g = LenFrac(ts);
                float segNow = Mathf.Lerp(SwordLen, SpiralLen, g) / (N - 1);
                float head0 = (-70f + 45f * g) * spiralSign * Mathf.Deg2Rad;       // starts off to one side and swirls with the lash
                var pos = root; float dist = 0f, head = head0;
                float a2 = 1f - Mathf.Exp(-220f * h);
                for (int i = 1; i < N; i++)
                {
                    float rad = SpiralR0 + SpiralGrow * dist;
                    head += spiralSign * segNow / rad;
                    var dir = F * Mathf.Cos(head) + R * Mathf.Sin(head);
                    pos += dir * segNow; dist += segNow;
                    var tgt = pos;
                    tgt.y = Mathf.Lerp(root.y, groundY + 0.4f, Mathf.Clamp01(dist / 1.6f));
                    if (i >= 2) p[i] = Vector3.Lerp(p[i], tgt, a2 * Mathf.Clamp01(g * 6f));
                }
                return;
            }
            for (int i = 2; i < N; i++)
            {
                float s = i / (float)(N - 1);
                Vector3 target; float k;
                if (kind == 1)
                {
                    float side = (basicCount % 2 == 0) ? -1f : 1f;
                    if (ts < BWind + BSnap * 0.5f)
                    {
                        // wind-up: thrown up and back over the shoulder, loose
                        // v6: a clean curve up off the hand and back down behind (no straight line to fold onto)
                        var back = (-F * 0.85f - U * 0.25f + R * 0.15f).normalized;   // v12 overhead chop: up over the hand and down behind   // v10: trailing low behind the right hip
                        target = root;
                        int segs = Mathf.Max(1, i);
                        for (int j = 1; j <= segs; j++)
                        {
                            float sj = j / (float)(N - 1);
                            target += Vector3.Slerp(U, back, Mathf.SmoothStep(0f, 1f, sj * 1.6f)).normalized * seg;
                        }
                        target.y = Mathf.Max(target.y, groundY + 0.05f);
                        k = 0.45f;
                    }
                    else
                    {
                        float phi = Mathf.Clamp01((ts - BWind - BSnap * 0.5f) / (BSnap * 0.5f + BUnroll));
                        phi = Mathf.Pow(phi, Swish ? 1.8f : 1.35f);   // swish: slow start, the last third whips out
                        // the line it lays down: from the hand toward the cursor, dropping to waist height at the tip
                        Vector3 Line(float x) { var pnt = root + F * x * Length; pnt.y = Mathf.Lerp(root.y, groundY + 0.55f, Mathf.Pow(x, 0.7f)); return pnt; }
                        if (s <= phi)
                        {
                            target = Line(s); k = 1f;
                            if (Swish)
                            {
                                // an S-wave runs down the laid part behind the loop and dies out as the lash straightens
                                float amp = 0.32f * (1f - phi) * Mathf.Clamp01(s * 4f);
                                target += R * (amp * Mathf.Sin((phi - s) * 9f)) + U * (amp * 0.5f * Mathf.Sin((phi - s) * 9f + 1.2f));
                                k = 0.8f;
                            }
                        }
                        else
                        {
                            // v4 rolling loop (how a cracked whip really moves): the part not yet laid forms a circle
                            // standing on the front of the laid line; it shrinks as it rolls forward and the tip snaps
                            // out of it at the end (the crack). Circumference = the length still to lay.
                            float rest = (1f - phi) * Length;
                            float rr = Mathf.Max(rest / ((Swish ? 1.5f : 2f) * Mathf.PI), 0.06f);   // swish: a bigger, open loop
                            var front = Line(phi);
                            var lu = Swish ? (U * 0.8f + R * 0.6f).normalized : (U * 0.97f + R * 0.2f).normalized;   // v10: sidearm lash, the loop leans out to the right
                            var c = front + lu * rr;
                            float th = (s - phi) * Length / rr;            // radians along the loop from its foot
                            target = c + (-lu * Mathf.Cos(th) + F * Mathf.Sin(th)) * rr;
                            k = 0.6f;
                        }
                        float after = ts - (BWind + BSnap + BUnroll);
                        if (after > 0f) k *= Mathf.Clamp01(1f - after / (Swish ? 0.03f : 0.12f));   // let go after the crack (swish: at once, the recoil shows)
                    }
                }
                else
                {
                    if (ts < SWind)
                    {
                        var dir = Quaternion.AngleAxis(SweepFrom - 30f, U) * F;
                        target = root + dir * s * SweepLen * 0.5f; target.y = Mathf.Lerp(root.y, groundY + 0.3f, s);
                        k = 0.3f;
                    }
                    else
                    {
                        float u = Mathf.Clamp01((ts - SWind) / SSweep);
                        float ang = Mathf.Lerp(SweepFrom, SweepTo, Ease(u)) - SweepLag * s;
                        var dir = Quaternion.AngleAxis(ang, U) * F;
                        var c = pc.transform.position; c.y = root.y;
                        target = c + dir * (0.35f + s * (SweepLen - 0.1f));
                        target.y = Mathf.Lerp(root.y, groundY + 0.5f, Mathf.Pow(s, 0.8f));
                        k = 1f;
                        float after = ts - (SWind + SSweep);
                        if (after > 0f) k *= Mathf.Clamp01(1f - after / 0.1f);
                    }
                }
                if (k <= 0f) continue;
                float a = 1f - Mathf.Exp(-(k >= 0.99f ? 160f : 60f) * k * h);   // laid nodes stick to the line
                var np = Vector3.Lerp(p[i], target, a);
                p[i] = np;
            }
        }

        // ---------------- drawing ----------------
        readonly List<Vector3> tv = new List<Vector3>(1024), tn = new List<Vector3>(1024);
        readonly List<int> tt = new List<int>(4096);
        const int Sides = 6, Sub = 3;
        readonly Vector3[] smooth = new Vector3[(N - 1) * Sub + 2];

        // the hidden greatsword gives the assembled blade its axis (toward the tip) and its flat side
        void MeasureSword()
        {
            if (swordAxisKnown || !sword) return;
            var mf = sword.GetComponentInChildren<MeshFilter>(true);
            if (!mf || !mf.sharedMesh) return;
            var b = mf.sharedMesh.bounds; var e = b.extents;
            int ax = e.x >= e.y && e.x >= e.z ? 0 : e.y >= e.z ? 1 : 2;
            int fl = e.x <= e.y && e.x <= e.z ? 0 : e.y <= e.z ? 1 : 2;
            Vector3 A = ax == 0 ? Vector3.right : ax == 1 ? Vector3.up : Vector3.forward;
            Vector3 Fl = fl == 0 ? Vector3.right : fl == 1 ? Vector3.up : Vector3.forward;
            var lo = mf.transform.TransformPoint(b.center - Vector3.Scale(A, e));
            var hi = mf.transform.TransformPoint(b.center + Vector3.Scale(A, e));
            var tipW = (hi - rHand.position).sqrMagnitude > (lo - rHand.position).sqrMagnitude ? hi - lo : lo - hi;
            swordAxisL = rHand.InverseTransformDirection(tipW.normalized);
            swordFlatL = rHand.InverseTransformDirection(mf.transform.TransformDirection(Fl)).normalized;
            swordAxisKnown = true;
        }

        Vector3 SwordAxis => swordAxisKnown ? rHand.TransformDirection(swordAxisL) : HandDir();

        // v15: read the sword mesh once, sort its triangles into the hilt (rigid) and Plates slices of the blade
        void CutSword()
        {
            swordCutTried = true;
            var mf = sword.GetComponentInChildren<MeshFilter>(true);
            var mr = mf ? mf.GetComponent<MeshRenderer>() : null;
            if (!mf || !mr || !mf.sharedMesh) return;
            var src = mf.sharedMesh;
            Vector3[] v; Vector3[] n; Vector2[] uv;
            try { v = src.vertices; n = src.normals; uv = src.uv; } catch { return; }
            if (v == null || v.Length == 0) { Debug.LogWarning("[RoomRun] whip: sword mesh not readable, code plates stay"); return; }
            swordT = mf.transform;
            var b = src.bounds; var e = b.extents;
            int ax = e.x >= e.y && e.x >= e.z ? 0 : e.y >= e.z ? 1 : 2;
            int fl = e.x <= e.y && e.x <= e.z ? 0 : e.y <= e.z ? 1 : 2;
            Vector3 A = ax == 0 ? Vector3.right : ax == 1 ? Vector3.up : Vector3.forward;
            cutF = fl == 0 ? Vector3.right : fl == 1 ? Vector3.up : Vector3.forward;
            // tip end = the end further from the hand
            var lo = b.center - Vector3.Scale(A, e); var hi = b.center + Vector3.Scale(A, e);
            bool hiTip = (swordT.TransformPoint(hi) - rHand.position).sqrMagnitude > (swordT.TransformPoint(lo) - rHand.position).sqrMagnitude;
            cutA = hiTip ? A : -A;
            cutS = Vector3.Cross(cutA, cutF);
            var tipLocal = hiTip ? hi : lo;
            float total = e[ax] * 2f;
            bladeLenLocal = total * BladeFrac;
            bladeBaseLocal = tipLocal - cutA * bladeLenLocal;
            cutScale = swordT.lossyScale.x;
            SwordLen = bladeLenLocal * Mathf.Abs(swordT.lossyScale[ax]);
            swordAxisL = rHand.InverseTransformDirection(swordT.TransformDirection(cutA).normalized);
            swordFlatL = rHand.InverseTransformDirection(swordT.TransformDirection(cutF).normalized);

            var pos = new List<Vector3>(); var nrm = new List<Vector3>(); var uvs = new List<Vector2>(); var plate = new List<int>();
            var map = new Dictionary<long, int>();
            int subs = src.subMeshCount;
            cvTris = new int[subs][];
            bool hasN = n != null && n.Length == v.Length, hasUv = uv != null && uv.Length == v.Length;
            for (int sm = 0; sm < subs; sm++)
            {
                var tri = src.GetTriangles(sm);
                var outL = new List<int>(tri.Length * Rep);
                for (int i = 0; i < tri.Length; i += 3)
                {
                    var c = (v[tri[i]] + v[tri[i + 1]] + v[tri[i + 2]]) / 3f;
                    float u = Vector3.Dot(c - bladeBaseLocal, cutA) / bladeLenLocal;      // < 0 = hilt
                    int k0 = u < 0f ? -1 : Mathf.Min(Plates - 1, (int)(u * Plates));
                    int copies = k0 < 0 ? 1 : Rep;
                    for (int rc2 = 0; rc2 < copies; rc2++)
                    {
                    int k = k0 < 0 ? -1 : k0 * Rep + rc2;
                    for (int j = 0; j < 3; j++)
                    {
                        int vi = tri[i + j];
                        long key = ((long)(k + 1) << 32) | (uint)vi;
                        if (!map.TryGetValue(key, out int ni))
                        {
                            ni = pos.Count; map[key] = ni;
                            pos.Add(v[vi]); nrm.Add(hasN ? n[vi] : Vector3.up); uvs.Add(hasUv ? uv[vi] : Vector2.zero); plate.Add(k);
                        }
                        outL.Add(ni);
                    }
                    }
                }
                cvTris[sm] = outL.ToArray();
            }
            cvPos = pos.ToArray(); cvNrm = nrm.ToArray(); cvUv = uvs.ToArray(); cvPlate = plate.ToArray();
            cvOut = new Vector3[cvPos.Length]; cvOutN = new Vector3[cvPos.Length];
            cvMats = mr.sharedMaterials;
            blade.Clear();
            blade.indexFormat = cvPos.Length > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            blade.vertices = cvPos; blade.normals = cvNrm; blade.uv = cvUv;
            blade.subMeshCount = subs;
            for (int sm = 0; sm < subs; sm++) blade.SetTriangles(cvTris[sm], sm, false);
            bladeGo.GetComponent<MeshRenderer>().sharedMaterials = cvMats;
            swordCut = true; chainInit = false;
            Debug.Log($"[RoomRun] whip: real sword cut into {Plates} plates ({cvPos.Length} verts, blade {SwordLen:0.00} m of {total * Mathf.Abs(swordT.lossyScale[ax]):0.00} m)");
        }

        void DrawSwordPlates()
        {
            var l2w = swordT.localToWorldMatrix;
            var flatRest = swordT.TransformDirection(cutF).normalized;
            // one frame per plate: centre on the chain, long axis along it; flat = the sword's own flat when joined, face up when split
            int M = Plates * Rep;
            if (pfMid == null || pfMid.Length != M) { pfMid = new Vector3[M]; pfDir = new Vector3[M]; pfNr = new Vector3[M]; pfSd = new Vector3[M]; }
            var mid = pfMid; var dir = pfDir; var nr = pfNr; var sd = pfSd;
            Vector3 prevN = flatRest;
            float dupScale = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(ext * 5f));   // the extra links grow in as the blade splits
            for (int k = 0; k < M; k++)
            {
                ChainAt((k + 0.5f) / M, out mid[k], out dir[k]);
                var restN = Vector3.ProjectOnPlane(flatRest, dir[k]);
                if (restN.sqrMagnitude < 1e-4f) restN = prevN;
                var upN = Vector3.ProjectOnPlane(Vector3.up, dir[k]);
                if (upN.sqrMagnitude < 1e-4f) upN = restN;
                nr[k] = Vector3.Slerp(restN.normalized, upN.normalized, ext).normalized;
                prevN = nr[k];
                sd[k] = Vector3.Cross(dir[k], nr[k]).normalized;
            }
            float plateLocal = bladeLenLocal / Plates;
            for (int i = 0; i < cvPos.Length; i++)
            {
                int k = cvPlate[i];
                if (k < 0) { cvOut[i] = l2w.MultiplyPoint3x4(cvPos[i]); cvOutN[i] = l2w.MultiplyVector(cvNrm[i]).normalized; continue; }
                int src = k / Rep;
                float sc = cutScale * ((k % Rep) == Rep / 2 ? 1f : dupScale);
                var c = bladeBaseLocal + cutA * ((src + 0.5f) * plateLocal);
                var d = cvPos[i] - c;
                // v19: links stretch and widen as the blade splits, so the chain reads as a band and not as scattered chips
                // v20: each piece grows into a clear blade shard (x2.6 long, x2.4 wide) with a gap to the next
                float gr = ext * Mathf.Lerp(0.25f, 1f, Mathf.Clamp01(k / (float)(Plates * Rep) * 2.5f));   // v21: small by the hand (no white slab), full size further out
                float da = Vector3.Dot(d, cutA) * sc * (1f + 1.6f * gr), df = Vector3.Dot(d, cutF) * sc * (1f + 0.8f * gr), ds = Vector3.Dot(d, cutS) * sc * (1f + 1.4f * gr);
                cvOut[i] = mid[k] + dir[k] * da + nr[k] * df + sd[k] * ds;
                var nn = cvNrm[i];
                cvOutN[i] = dir[k] * Vector3.Dot(nn, cutA) + nr[k] * Vector3.Dot(nn, cutF) + sd[k] * Vector3.Dot(nn, cutS);
            }
            blade.vertices = cvOut; blade.normals = cvOutN;
            blade.RecalculateBounds();
        }

        // v20: joined = the sword's own materials; split = bright steel on every shard
        bool plateLookSplit; Material[] steelMats;
        void SwapPlateLook(bool split)
        {
            if (!swordCut || split == plateLookSplit || cvMats == null) return;
            plateLookSplit = split;
            if (steelMats == null || steelMats.Length != cvMats.Length) { steelMats = new Material[cvMats.Length]; for (int i = 0; i < steelMats.Length; i++) steelMats[i] = steel; }
            bladeGo.GetComponent<MeshRenderer>().sharedMaterials = split ? steelMats : cvMats;
        }

        // assembled: the chain is the straight blade in the hand; while it splits / rejoins it blends to that line
        void Assemble()
        {
            float w = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(ext * 1.4f));
            if (w <= 0f) return;
            var ax = SwordAxis;
            var root = swordCut ? swordT.TransformPoint(bladeBaseLocal) : rHand.position + ax * 0.07f;
            for (int i = 0; i < N; i++)
            {
                var rigid = root + ax * (i / (float)(N - 1)) * SwordLen;
                p[i] = Vector3.Lerp(p[i], rigid, w);
                if (w > 0.98f) q[i] = p[i];
            }
        }

        // point along the chain at arc fraction u (0 = hand, 1 = tip) and its tangent
        void ChainAt(float u, out Vector3 pos, out Vector3 tan)
        {
            float x = Mathf.Clamp01(u) * (N - 1);
            int i = Mathf.Min((int)x, N - 2); float f = x - i;
            pos = Vector3.Lerp(p[i], p[i + 1], f);
            tan = p[i + 1] - p[i];
            if (i > 0) tan = Vector3.Lerp(p[i] - p[i - 1], tan, 0.5f + 0.5f * f);
            if (tan.sqrMagnitude < 1e-8f) tan = SwordAxis;
            tan.Normalize();
        }

        readonly List<Vector3> bv = new List<Vector3>(512);
        readonly List<int> bt = new List<int>(1024);
        void DrawBlade()
        {
            bv.Clear(); bt.Clear();
            float plateLen = SwordLen / Plates;
            var flatRest = swordAxisKnown ? rHand.TransformDirection(swordFlatL) : Vector3.up;
            Vector3 prevNrm = flatRest;
            for (int k = 0; k < Plates; k++)
            {
                // plate k sits at the same share of the chain whether assembled or split, so the gaps open evenly
                float uc = (k + 0.5f) / Plates;
                ChainAt(uc, out var mid, out var dir);
                var back = mid - dir * plateLen * 0.48f;
                var front = mid + dir * plateLen * 0.48f;
                // flat side: the sword's own flat at rest, lying face up when split
                var flatSplit = Vector3.ProjectOnPlane(Vector3.up, dir);
                if (flatSplit.sqrMagnitude < 1e-4f) flatSplit = prevNrm;
                var restN = Vector3.ProjectOnPlane(flatRest, dir);
                if (restN.sqrMagnitude < 1e-4f) restN = flatSplit;
                var nrm = Vector3.Slerp(restN.normalized, flatSplit.normalized, ext).normalized;
                prevNrm = nrm;
                var side = Vector3.Cross(dir, nrm).normalized;
                float s0 = k / (float)Plates, s1 = (k + 1f) / Plates;
                float w0 = Mathf.Lerp(0.07f, 0.048f, s0), w1 = Mathf.Lerp(0.07f, 0.048f, s1);
                bool tip = k == Plates - 1;
                float th = 0.012f;
                // chevron plate: pointed front, notched back; the tip plate ends in a point
                var ridgeB = back + dir * w0 * 0.35f; var ridgeF = front + dir * (tip ? plateLen * 0.6f : w1 * 0.35f);
                var lB = back - side * w0; var rB = back + side * w0;
                var lF = front - side * (tip ? w1 * 0.55f : w1); var rF = front + side * (tip ? w1 * 0.55f : w1);
                var up = nrm * th;
                Quad(lB, ridgeB + up, ridgeF + up, lF); Quad(ridgeB + up, rB, rF, ridgeF + up);
                Quad(lF, ridgeF - up, ridgeB - up, lB); Quad(ridgeF - up, rF, rB, ridgeB - up);
                Tri(lF, ridgeF + up, ridgeF - up); Tri(rF, ridgeF - up, ridgeF + up);
            }
            blade.Clear();
            blade.SetVertices(bv); blade.SetTriangles(bt, 0, false);
            blade.RecalculateNormals(); blade.RecalculateBounds();
        }
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d) { Tri(a, b, c); Tri(a, c, d); }
        void Tri(Vector3 a, Vector3 b, Vector3 c)
        {
            int i = bv.Count; bv.Add(a); bv.Add(b); bv.Add(c);
            bt.Add(i); bt.Add(i + 1); bt.Add(i + 2);
        }

        void DrawTube()
        {
            // handle (rigid, from behind the fist) + the smoothed chain
            var hd = HandDir();
            int m = 0;
            smooth[m++] = swordCut ? p[0] - SwordAxis * 0.02f : rHand.position - SwordAxis * 0.16f;
            for (int i = 0; i < N - 1; i++)
            {
                var p0 = p[Mathf.Max(i - 1, 0)]; var p1 = p[i]; var p2 = p[i + 1]; var p3 = p[Mathf.Min(i + 2, N - 1)];
                for (int k = 0; k < Sub; k++)
                {
                    float u = k / (float)Sub;
                    smooth[m++] = CatmullRom(p0, p1, p2, p3, u);
                }
            }
            smooth[m++] = p[N - 1];
            tv.Clear(); tn.Clear(); tt.Clear();
            Vector3 prevN = Vector3.Cross(smooth[1] - smooth[0], Vector3.up);
            if (prevN.sqrMagnitude < 1e-6f) prevN = Vector3.right;
            prevN.Normalize();
            for (int r = 0; r < m; r++)
            {
                var tan = (r < m - 1 ? smooth[r + 1] - smooth[r] : smooth[r] - smooth[r - 1]);
                if (tan.sqrMagnitude < 1e-8f) tan = Vector3.forward;
                tan.Normalize();
                var n = prevN - tan * Vector3.Dot(prevN, tan);
                if (n.sqrMagnitude < 1e-6f) n = Vector3.Cross(tan, Vector3.up);
                n.Normalize(); prevN = n;
                var b = Vector3.Cross(tan, n);
                float s = r / (float)(m - 1);
                // handle thick, the thong tapers to a thin fall; the very tip (popper) a touch wider
                float rad = r == 0 && !swordCut ? 0.03f : 0.007f;   // v7: grip, then a thin cable between the plates
                for (int k = 0; k < Sides; k++)
                {
                    float a = k * Mathf.PI * 2f / Sides;
                    var off = n * Mathf.Cos(a) + b * Mathf.Sin(a);
                    tv.Add(smooth[r] + off * rad); tn.Add(off);
                }
                if (r > 0)
                {
                    int b0 = (r - 1) * Sides, b1 = r * Sides;
                    for (int k = 0; k < Sides; k++)
                    {
                        int k1 = (k + 1) % Sides;
                        tt.Add(b0 + k); tt.Add(b1 + k); tt.Add(b1 + k1);
                        tt.Add(b0 + k); tt.Add(b1 + k1); tt.Add(b0 + k1);
                    }
                }
            }
            tube.Clear();
            tube.SetVertices(tv); tube.SetNormals(tn); tube.SetTriangles(tt, 0, false);
            tube.RecalculateBounds();
        }

        static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        readonly List<Vector3> rv = new List<Vector3>(256);
        readonly List<Vector2> ruv = new List<Vector2>(256);
        readonly List<Color> rc = new List<Color>(256);
        readonly List<int> rt = new List<int>(512);

        void UpdateRibbon(float dt)
        {
            if (ext > 0.03f)
            {
                // v17: a bright band under the links (the reference's glowing segmented strip), so the spiral reads on any ground
                rv.Clear(); ruv.Clear(); rc.Clear(); rt.Clear();
                int m = (N - 1) * Sub + 2;
                float al = Mathf.Clamp01(ext * 3f) * 0.9f;
                for (int r = 1; r < m; r++)
                {
                    var tan = (r < m - 1 ? smooth[r + 1] - smooth[r] : smooth[r] - smooth[r - 1]);
                    if (tan.sqrMagnitude < 1e-8f) tan = Vector3.forward;
                    var side = Vector3.Cross(tan.normalized, Vector3.up);
                    if (side.sqrMagnitude < 1e-6f) side = Vector3.right;
                    side = side.normalized * ((MusaLash && kind == 1 ? 0.24f : 0.07f) * Mathf.Lerp(1f, 0.6f, r / (float)m));   // v20 thin; v33 the 무사 band is thick
                    var c0 = smooth[r] + Vector3.down * 0.02f;
                    var col = new Color(1f, 1f, 1f, al);
                    rv.Add(c0 - side); ruv.Add(new Vector2(0f, 0f)); rc.Add(col);
                    rv.Add(c0); ruv.Add(new Vector2(0f, 1f)); rc.Add(col);
                    rv.Add(c0 + side); ruv.Add(new Vector2(0f, 0f)); rc.Add(col);
                    if (r > 1)
                    {
                        int b0 = (r - 2) * 3, b1 = (r - 1) * 3;
                        rt.Add(b0); rt.Add(b1); rt.Add(b0 + 1); rt.Add(b0 + 1); rt.Add(b1); rt.Add(b1 + 1);
                        rt.Add(b0 + 1); rt.Add(b1 + 1); rt.Add(b0 + 2); rt.Add(b0 + 2); rt.Add(b1 + 1); rt.Add(b1 + 2);
                    }
                }
                ribbonMat.SetColor("_Color", new Color(1.35f, 0.5f, 0.6f, 1f));
                ribbonMat.SetColor("_Edge", new Color(1.7f, 1.45f, 1.45f, 1f));
                ribbonMat.SetFloat("_BodyPow", 0.9f); ribbonMat.SetFloat("_EdgeWidth", 0.45f);
                ribbon.Clear();
                ribbon.SetVertices(rv); ribbon.SetUVs(0, ruv); ribbon.SetColors(rc); ribbon.SetTriangles(rt, 0, false);
                ribbon.RecalculateBounds();
                snaps.Clear();
                return;
            }
            float now = Time.time;
            bool emit = ((BasicRibbon || Swish) && kind == 1 && t > BWind + BSnap * 0.3f && t < BWind + BSnap + BUnroll + 0.04f)
                     || (kind == 2 && t > SWind * 0.6f && t < SWind + SSweep + 0.05f);
            if (emit && freeze <= 0f)
            {
                int a = kind == 2 ? (N * 3) / 4 : Swish ? N - 3 : N / 2;   // swish: a thin line that draws the tip's path   // v2: a narrow band at the tip, not a slab
                snaps.Add(new Snap { a = p[a], b = p[N - 1], time = now });
            }
            float life = kind == 2 ? 0.28f : Swish ? 0.22f : 0.16f;
            snaps.RemoveAll(sn => now - sn.time > life);
            rv.Clear(); ruv.Clear(); rc.Clear(); rt.Clear();
            // v16: the frames are joined by a Catmull-Rom curve (5 steps each) so a fast swish draws a round arc, not corners
            int cnt = snaps.Count;
            for (int i = 0; i < cnt; i++)
            {
                int steps = i < cnt - 1 ? 5 : 1;
                var s0 = snaps[Mathf.Max(i - 1, 0)]; var s1 = snaps[i]; var s2 = snaps[Mathf.Min(i + 1, cnt - 1)]; var s3 = snaps[Mathf.Min(i + 2, cnt - 1)];
                for (int j = 0; j < steps; j++)
                {
                    float u = j / (float)steps;
                    var pa = CatmullRom(s0.a, s1.a, s2.a, s3.a, u); var pb = CatmullRom(s0.b, s1.b, s2.b, s3.b, u);
                    float age = Mathf.Clamp01((now - Mathf.Lerp(s1.time, s2.time, u)) / life);
                    rv.Add(pa); ruv.Add(new Vector2(age, 0f)); rc.Add(new Color(1f, 1f, 1f, 0.55f));
                    rv.Add(pb); ruv.Add(new Vector2(age, 1f)); rc.Add(new Color(1f, 1f, 1f, 0.55f));
                    int n2 = rv.Count / 2;
                    if (n2 > 1)
                    {
                        int b0 = (n2 - 2) * 2, b1 = (n2 - 1) * 2;
                        rt.Add(b0); rt.Add(b1); rt.Add(b0 + 1); rt.Add(b0 + 1); rt.Add(b1); rt.Add(b1 + 1);
                    }
                }
            }
            ribbon.Clear();
            if (rt.Count == 0) return;
            ribbon.SetVertices(rv); ribbon.SetUVs(0, ruv); ribbon.SetColors(rc); ribbon.SetTriangles(rt, 0, false);
            ribbon.RecalculateBounds();
        }

        // ---- afterimage (v24): the area the blade swept in the last GhostLife seconds, drawn as one fading sheet ----
        public static bool Ghost = false;        // v27: off. The swept sheet came out as big faceted grey slabs (the look Kido rejected in v1). token whipghost=1
        const int GhostMax = 10; const float GhostLife = 0.16f;
        Mesh ghost; GameObject ghostGo; Material ghostMat;
        readonly Vector3[][] ghP = new Vector3[GhostMax][]; readonly float[] ghT = new float[GhostMax]; int ghN;
        readonly List<Vector3> gv = new List<Vector3>(512); readonly List<Vector2> guv = new List<Vector2>(512);
        readonly List<Color> gc = new List<Color>(512); readonly List<int> gt = new List<int>(2048);
        void UpdateGhost()
        {
            if (!ghost) return;
            float now = Time.time;
            if (Ghost && ext > 0.2f && freeze <= 0f)
            {
                if (ghN == GhostMax) { var first = ghP[0]; for (int j = 1; j < GhostMax; j++) { ghP[j - 1] = ghP[j]; ghT[j - 1] = ghT[j]; } ghP[GhostMax - 1] = first; ghN--; }
                if (ghP[ghN] == null) ghP[ghN] = new Vector3[N];
                System.Array.Copy(p, ghP[ghN], N); ghT[ghN] = now; ghN++;
            }
            // drop the old ones
            int drop = 0; while (drop < ghN && now - ghT[drop] > GhostLife) drop++;
            if (drop > 0)
            {
                for (int j = drop; j < ghN; j++) { var tmp = ghP[j - drop]; ghP[j - drop] = ghP[j]; ghP[j] = tmp; ghT[j - drop] = ghT[j]; }
                ghN -= drop;
            }
            gv.Clear(); guv.Clear(); gc.Clear(); gt.Clear();
            for (int j = 0; j < ghN; j++)
            {
                float age = Mathf.Clamp01((now - ghT[j]) / GhostLife);
                for (int i = 0; i < N; i++)
                {
                    float sN = i / (float)(N - 1);
                    gv.Add(ghP[j][i]); guv.Add(new Vector2(age, 0.25f + 0.75f * sN)); gc.Add(new Color(1f, 1f, 1f, 0.6f * Mathf.Clamp01(sN * 3f)));
                }
                if (j == 0) continue;
                int b0 = (j - 1) * N, b1 = j * N;
                for (int i = 0; i < N - 1; i++)
                {
                    gt.Add(b0 + i); gt.Add(b1 + i); gt.Add(b0 + i + 1);
                    gt.Add(b0 + i + 1); gt.Add(b1 + i); gt.Add(b1 + i + 1);
                }
            }
            ghost.Clear();
            if (gt.Count == 0) return;
            ghost.SetVertices(gv); ghost.SetUVs(0, guv); ghost.SetColors(gc); ghost.SetTriangles(gt, 0, false);
            ghost.RecalculateBounds();
        }

        void SpawnFlash(Vector3 at, float size, float life)
        {
            Flash f = null;
            foreach (var x in flashes) if (!x.go.activeSelf) { f = x; break; }
            if (f == null)
            {
                f = new Flash { go = new GameObject("WhipCrack"), m = new Material(ribbonMat) };
                f.go.AddComponent<MeshFilter>().sharedMesh = starMesh;
                var r = f.go.AddComponent<MeshRenderer>(); r.sharedMaterial = f.m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                f.m.SetColor("_Color", new Color(1.4f, 1.6f, 2.2f, 1f)); f.m.SetFloat("_BodyPow", 0.6f); f.m.SetFloat("_EdgeWidth", 0.3f);
                f.m.SetColor("_Edge", new Color(2.2f, 2.3f, 2.6f, 1f));
                flashes.Add(f);
            }
            f.go.SetActive(true); f.age = 0f; f.life = life; f.size = size;
            f.go.transform.position = at;
            f.go.transform.localScale = Vector3.one * 0.01f;
        }

        void UpdateFlashes(float dt)
        {
            var cam = RunDirector.Instance && RunDirector.Instance.Rig ? RunDirector.Instance.Rig.Cam : Camera.main;
            foreach (var f in flashes)
            {
                if (!f.go.activeSelf) continue;
                f.age += dt;
                float u = f.age / f.life;
                if (u >= 1f) { f.go.SetActive(false); continue; }
                float sc = f.size * (0.55f + 0.6f * EaseOut(u * 1.6f));
                f.go.transform.localScale = Vector3.one * sc;
                if (cam) f.go.transform.rotation = Quaternion.LookRotation(f.go.transform.position - cam.transform.position) * Quaternion.Euler(0, 0, u * 25f);
                float fade = 1f - u * u;
                f.m.SetColor("_Color", new Color(1.4f, 1.6f, 2.2f, 1f) * fade);
                f.m.SetColor("_Edge", new Color(2.2f, 2.3f, 2.6f, 1f) * fade);
            }
        }

        void Note(string s) { if (Log.Count < 4000) Log.Add(s); }
    }
}
