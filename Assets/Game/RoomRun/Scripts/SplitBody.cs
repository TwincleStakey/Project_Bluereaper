// 11-15 (Kido: "옆으로 갈 때 이상하다. PoE2처럼 상하체 분리"): upper/lower body split for the berserker GS_Hero.
// Legs: the visual root turns to whichever of {move dir, move dir ±90°, move dir + 180°} is closest to the cursor (with
// hysteresis), and the Animator plays run / strafe / backpedal for it (MoveX/MoveZ in root space, GreatswordCombat).
// Upper body: after the Animator, the chest is turned the rest of the way to the cursor. Kido: hide the cut under the
// waist sash, so the Hips (sash, pants top) turn with the upper body and both thighs are turned back by the same amount;
// the visible twist sits at the hip joints below the sash, and what is left is spread over Spine/Spine1/Spine2.
//
// 11-15 v3 (Kido: "east로 움직이는데 몸과 칼은 north 보고 일직선으로 전혀 안 움직인다, 너무 어색하다" and "약간 몸이
// 기울고 칼을 바닥에 끌면 칼이 / 방향으로 향하고 약간 흔들려야 되는데 평평하게 굳어서 전혀 안 움직인다"). The split above
// is now the old style behind SplitBody.On (shots token split=1). Default (On = false): the body faces where it runs
// (PlayerController turns it at 720 deg/s; cursor when standing) and this adds, after the Animator, while not attacking:
//   look  - head/neck turn to the cursor (up to 45 deg), the chest only up to 30; the dragging arm is turned back so the
//           sword is not swung by it
//   lean  - the spine pitches into the run (eased in/out with speed) and banks into turns
//   sword - a damped spring on the blade's world yaw about the grip toward "behind the velocity, slanted out to the sword
//           side" (the / when running east), lagging on turns and overshooting a little on stops; it wags and rolls with
//           the footsteps, the tip bobs and is held just above the ground, never swung in toward the legs
//   sway  - the shoulders counter-swing against the legs
//
// 11-15 aim mode again (Kido: the game is WASD + cursor aim, so turning the body to the move direction is wrong): the split
// (On) is the default again: torso and head on the cursor always, legs run / strafe / backpedal.
// The v3 run feel now runs on top of the split: lean toward the travel direction (world axis, eased by a spring so stops
// rock back a little; back lean smaller), the sword spring behind the velocity kept out of a no-go wedge round the body
// (grip to hips line), step wag/bob, shoulders and the free arm counter-swing. Shots token feel=0 = the frozen old split.
using UnityEngine;

namespace RoomRun
{
    [DefaultExecutionOrder(50)]   // after the Animator, before LSPoseFix (100) and the eye flames (200)
    public sealed class SplitBody : MonoBehaviour
    {
        public static bool On = true;
        public static bool Feel = true;   // aim-mode run feel on top of the split (shots token feel=0 = off, the frozen old split)
        public static bool LegacyEase = false;   // limb fix A/B (shots token limb=0): the old ease-out of the twist into a swing

        public Transform visual;
        public Animator anim;
        public GreatswordCombat gs;
        public PlayerController pc;
        [Range(0f, 1f)] public float hipsShare = 0.4f;   // of the twist, at the hips (sash) with the thighs turned back
        public float maxTwist = 70f;                      // beyond this the legs pick another direction anyway
        public float switchMargin = 20f;                 // hysteresis between leg directions
        public float twistSpeed = 540f;                  // deg/s the shown twist follows

        [Header("v3 run feel (On = false)")]
        public float chestLookMax = 30f, headLookMax = 45f, lookSpeed = 360f;
        public float leanDeg = 26f, bankPerDegPerSec = 0.025f, bankMax = 9f;
        public float dragSlant = 30f;          // blade yaw out to the sword side from straight behind the velocity
        public float springHz = 1.7f, springDamp = 0.38f;
        public float wagDeg = 6f, rollDeg = 7f, bobHeight = 0.05f, tipHeight = 0.07f, shoulderSwing = 6f;
        // 11-16 calm drag (Kido: "살짝 흔들리는 건 좋은데 너무 막 좌우로 확확 돌아서 이상해"): the blade spring was underdamped
        // (1.7 Hz, damping 0.38) right next to the ~1.5 Hz step rhythm, and the step signal was clipped (flat tops, sharp
        // turns) driving a 6 deg yaw wag and a 7 deg roll wag at double frequency, so the dragged blade snapped side to side and
        // overshot on stops/turns. Now: critically damped, the step signal unclipped and low-passed (sine-like), yaw wag 4 deg,
        // roll 2 deg following the step smoothly. Shots token sway=0 = the old values for A/B.
        public static bool LegacySway = false;
        public float calmHz = 2.2f, calmDamp = 1.0f, calmWag = 4f, calmRoll = 2f, calmMaxRate = 300f;
        // 11-16 walk grip fix (Kido 17:27 KST: "걸을때 무기쥔 손이 이상한데"): the drag spring, the step roll and the tip-height
        // pitch all turned the sword INSIDE a fixed hand (the "Greatsword" grip node), 30-80 deg off the keyed grip while
        // walking: the handle swivelled through the fist and the wrist never moved (in-game log: up to 138 deg walking back,
        // 125 walking left). Now the turn is carried by the arm: the upper arm takes armShare (about the shoulder), the
        // forearm forearmShare (about the elbow), the wrist the rest, and the sword stays seated in the palm exactly as keyed.
        // The carried turn is capped at maxCarry: walking backward the spring wanted the blade 140 deg round (trailing in
        // FRONT of the body), which no wrist can hold. Shots token hand=0 = the old in-hand turn for A/B.
        public static bool HandCarries = true;
        [Range(0f, 1f)] public float armShare = 0.35f, forearmShare = 0.35f;
        public float maxCarry = 55f;
        public float GripSlip { get; private set; }    // deg the sword is turned inside the hand this frame (logs)
        public float WristBend { get; private set; }   // deg the hand is turned against the forearm by this pass (logs)
        float bearS, hwS; bool wedgeInit;

        public float Twist { get; private set; }         // shown upper-body twist (deg, + = to the left)
        public float BladeOffset { get; private set; }   // v3: spring yaw added to the animated blade (deg)
        public float LeanNow => lean * weight;
        int legChoice;                                    // 0 fwd, 1 left strafe, -1 right strafe, 2 back
        Transform hips, spine, spine1, spine2, upL, upR, neck, head, armR, footL, footR, sword;
        float bladeLen = 1.2f;

        static float Yaw(Vector3 v) => Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;

        // 18:31 Kido "래퍼런스대로 해": measured on the reference's rest frames (ref/vrising_gs_combo_frames_1118.jpg f10-17,
        // f29-35): the body stands ~65 deg off the aim toward the sword, and the blade points straight back from the AIM
        // (down the screen), so the carry pose's blade heading is 65 deg out from the body's back (BZ_CARRY in the script)
        public static float StanceYaw = 65f;   // deg the standing body is turned from the cursor (shots token stance=<deg>, 0 = square on)
        public bool Standing { get; private set; }
        float stanceHead;

        /// Leg facing for this frame: the move direction turned by 0/±90/180 to stay closest to the aim.
        public Vector3 LegDir(Vector3 moveFlat, Vector3 aim, Vector3 current)
        {
            // v3 default: run where you move, face the cursor when standing
            if (!On) return moveFlat.sqrMagnitude > 1e-4f ? moveFlat.normalized : aim;
            if (moveFlat.sqrMagnitude < 1e-4f)
            {
                legChoice = 0;
                // 11-18 18:27 side-on stance (Kido: "v rising같은경우 기본 몸이 옆으로 서있자나"): standing, the WHOLE body is
                // turned StanceYaw away from the cursor toward the sword side (sword on the right = left shoulder leads);
                // only the head looks at the cursor (AimSplit). A rigid turn of the root: nothing in the torso is twisted.
                // side-on only once the swings are over (0.2 s after the last one): between chained hits the body stays on the aim
                // 11-19 17:30: the chain's swing clips END in the stance pose with the 65 deg turn inside the clip (rest keys), while the
                // stance clip has no turn and this root turn supplies it. With the old rule (root turns 0.2 s after the swing, attack
                // layer fading over 0.27 s on its own clock) the two overlapped: mid-fade the body was turned up to twice and the blade
                // wandered ~100 deg to the front and back after every unchained swing (logs h2b / st2 / ax2). Now the root turn is
                // exactly what the attack layer has given up: turn = StanceYaw * (1 - attack layer weight). Total stays 65 deg.
                if (gs && gs.Berserker && GreatswordCombat.VRisingChain && GreatswordCombat.MixamoChain) { Standing = true; return aim; }   // Mixamo chain: the pack's own guard, facing the aim
                if (TwoHand && StanceYaw != 0f && gs && gs.Berserker && GreatswordCombat.VRisingChain)
                {
                    Standing = true;   // standing still, swinging or not: the stance layer stays posed UNDER the full-body attack layer (see Carry)
                    return Quaternion.AngleAxis(StanceYaw * gs.RestSide * (1f - gs.AttackLayerW), Vector3.up) * aim;
                }
                Standing = !gs || gs.IdleFor > 0.2f || !gs.Berserker;
                if (!Standing) return aim;
                if (TwoHand && StanceYaw != 0f) return Quaternion.AngleAxis(StanceYaw * (gs ? gs.RestSide : 1), Vector3.up) * aim;
                return aim;   // standing: the whole body faces the cursor
            }
            Standing = false;
            float mv = Yaw(moveFlat), a = Yaw(aim);
            int[] ch = { 0, 1, -1, 2 };
            float Off(int c) => Mathf.Abs(Mathf.DeltaAngle(mv + (c == 2 ? 180f : c * -90f), a));
            int best = legChoice; float bestOff = Off(legChoice);
            foreach (int c in ch) if (Off(c) < bestOff - switchMargin) { best = c; bestOff = Off(c); }
            legChoice = best;
            float y = mv + (best == 2 ? 180f : best * -90f);
            return Quaternion.Euler(0f, y, 0f) * Vector3.forward;
        }

        void Bind()
        {
            if (hips || !anim) return;
            hips = anim.GetBoneTransform(HumanBodyBones.Hips);
            spine = anim.GetBoneTransform(HumanBodyBones.Spine);
            spine1 = anim.GetBoneTransform(HumanBodyBones.Chest);
            spine2 = anim.GetBoneTransform(HumanBodyBones.UpperChest);
            upL = anim.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            upR = anim.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            neck = anim.GetBoneTransform(HumanBodyBones.Neck);
            head = anim.GetBoneTransform(HumanBodyBones.Head);
            armR = anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
            armL = anim.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            footL = anim.GetBoneTransform(HumanBodyBones.LeftFoot);
            footR = anim.GetBoneTransform(HumanBodyBones.RightFoot);
        }

        // 11-16: the "Greatsword" grip node is not animated, so whatever the drag spring (and LSPoseFix, after this) turned
        // it by stayed in its localRotation: an attack started with the blade still turned in the hand by the last drag
        // offset (20-60 deg off the keyed swing, growing with play time). Back to the grip's rest rotation every frame
        // before anything adds to it.
        Quaternion swordRest; bool swordRestSet;
        // 11-16 flat hold (Kido 21:38 KST: "칼이 면쪽이 보이게 눕히고 있다가 바로 배는게 이어지게 ... 손에 쥐고있는칼만 90도
        // 돌려"): the grip node is rolled about the blade's own long axis (local +Z) so the face lies flat for the top-down
        // camera. A fixed 90 deg roll did it in idle / walk (keyed edge-up) but stood the blade half on edge in the swing,
        // which is keyed flat already (log bladeFlat 0.06-0.5 vs 1.0). So the roll is solved every frame: the turn about the
        // blade axis that brings the face normal closest to world up (~90 in idle and walk, ~0 in the swing), eased at
        // flatRate, held while the blade points near straight up/down. The face stays up from the hold into the sweep.
        // Arms, hands and clips untouched. Shots token flat=0 = the old seat for A/B.
        // 11-17 (Kido 09:03-09:08: "좌우로 움직일때 칼이 계속 flip돼", "칼날중에 더 긴쪽이 항상 몸쪽 짧은쪽이 바깥쪽",
        // "칼끝 긴쪽 몸쪽이라고 했는데 정반대로 했자나", "뒤로갈때 칼이 스자나"): "a face up" had two answers and the solve
        // jumped between them (180 deg flips), and world up is not what the 55 deg camera sees. The blade is asymmetric:
        // its point sits on one edge (GS_Greatsword.fbx: the tip is on the +y edge), so that edge is the LONG one. Rule now:
        // the width line is turned square to the camera's view (face to the camera) and its long edge points toward the
        // body (hips), with hysteresis on that side choice and a roll-speed cap, so it can never flip. Same rule in the
        // swing. Shots token flat=0 = the old seat.
        // 11-18 (Kido: "손 이게 뭐야"): measured on GS_Hero.fbx's GS_Sword along its long axis: tip -0.89, blade to -0.18,
        // guard block -0.18..+0.33, bare handle +0.33..+0.52, pommel +0.52..+0.61; the FBX hand sits at +0.37 (top of the
        // handle). +0.10 slid the fist onto the guard block (the "hand on the box" in Kido's shot). -0.05 = the fist mid-handle.
        public static float GripSlide = -0.05f;   // m the sword moves toward the pommel through the fist (+ = hand toward the blade)
        Vector3 swordPos0; bool swordPosSet;
        public static bool FlatHold = true;
        public static bool EdgeLead = true;   // shots token edge=0|1
        public float flatRate = 1800f;    // deg/s cap on the roll in the hand: the swing turns the hand ~30 deg a frame, so 450 lagged (edge-on at contact / recovery); the side hysteresis is what stops flips
        float flatRoll; Vector3 faceAxis = Vector3.right, longAxis = Vector3.up; bool faceSet; float bodySign = 1f; bool sideSet;
        public float FlatRollDeg => flatRoll;           // logs
        public Vector3 BladeLongAxis { get { if (!faceSet && sword) MeasureBlade(); return longAxis; } }   // sword space: the point-side (long) edge; the cutting edge is the other one
        public float LongToBody { get; private set; }   // logs: long edge . (toward body), + = body side
        public float FaceToCam { get; private set; }    // logs: |face normal . view|
        // 11-19 13:00 grip seat fix: the chain clips (GS_CarryPoses.fbx) are baked with the right hand turned so its long axis
        // (the T-pose forearm line) is SeatKeep deg off the blade instead of ~square to it (the square grip bent the wrist
        // past the humanoid limits and Unity's in-between frames threw the sword for a frame). The sword is turned in the
        // hand by the same angle about the same axis, about the hand's origin, so the blade goes where the clip keys it.
        public static bool WalkCarryRight = true;   // shots token walkright=0|1
        static bool Mx => GreatswordCombat.MixamoChain && GreatswordCombat.VRisingChain;
        // Mixamo chain sword seat: the mocap's two hands hold one handle, so in the idle guard the handle lies on the line
        // right hand -> left hand. Measured once on the visible body (the suit body when it is on) after it has stood in the
        // idle for a moment: the blade points from the right hand AWAY from the left hand. Turned about the hand's origin,
        // so the right hand keeps its place on the handle.
        Quaternion mxRot; Vector3 mxPos; bool mxSeat; float mxIdleT;
        void TryMxSeat()
        {
            if (!sword || !sword.parent || !gs || gs.Attacking || pc.Rolling) { mxIdleT = 0f; return; }
            var body = sword.GetComponentInParent<Animator>();
            if (SuitBody.On && body == anim) { mxIdleT = 0f; return; }   // wait for the suit body to take the sword
            var lh = body ? body.GetBoneTransform(HumanBodyBones.LeftHand) : null;
            if (!lh) return;
            mxIdleT += Time.deltaTime;
            if (mxIdleT < 0.5f) return;
            var rh = sword.parent;
            var toPommel = rh.InverseTransformDirection((lh.position - rh.position).normalized);
            var fwdNow = (sword.localRotation * Vector3.forward).normalized;
            var turn = Quaternion.FromToRotation(fwdNow, -toPommel);
            mxRot = turn * sword.localRotation; mxPos = turn * sword.localPosition; mxSeat = true;
            Debug.Log($"[RoomRun] Mixamo sword seat: handle axis (hand space) {toPommel.ToString("0.00")}, turned {Quaternion.Angle(Quaternion.identity, turn):0} deg from the old seat, hands {Vector3.Distance(lh.position, rh.position):0.00} m apart");
        }
        public static bool HandSwap = true;         // shots token swap=0|1
        public static float SwapSlide = 0.10f;      // m, = gs_berserker_anim.py SWAP_R
        float swapS;
        public float SwapAmount => swapS;
        public static bool SeatFix = true;      // shots token seat=0|1
        public static float SeatTurn = 44f;     // deg, = gs_berserker_anim.py SEAT_TURN (printed as "SEAT ... hand turned"); shots token seatdeg=
        Quaternion seatFixQ = Quaternion.identity; bool seatFixSet;
        Quaternion SeatQ
        {
            get
            {
                if (!seatFixSet && swordRestSet && anim)
                {
                    seatFixSet = true;
                    var hand = anim.GetBoneTransform(HumanBodyBones.RightHand);
                    if (hand && anim.avatar)
                    {
                        // the hand's long axis = the forearm line in the avatar's T-pose, in hand space (same definition as the bake script)
                        foreach (var sk in anim.avatar.humanDescription.skeleton)
                        {
                            if (sk.name != hand.name) continue;
                            var h = (Quaternion.Inverse(sk.rotation) * sk.position).normalized; var b = (swordRest * Vector3.forward).normalized;
                            float ang = Vector3.Angle(h, b), turn = SeatTurn;   // the bake measures 79 deg (turn 44); the avatar skeleton gives 91, so the bake's turn is used as is
                            if (turn > 0.01f) seatFixQ = Quaternion.AngleAxis(turn, Vector3.Cross(b, h).normalized);
                            Debug.Log($"[RoomRun] grip seat: hand axis to blade {ang:0.0} deg, sword turned {turn:0.0} in the hand");
                            break;
                        }
                    }
                }
                return SeatFix && TwoHand && gs && gs.Berserker && GreatswordCombat.VRisingChain && !GreatswordCombat.MixamoChain ? seatFixQ : Quaternion.identity;
            }
        }
        Quaternion Seat => SeatQ * swordRest * Quaternion.AngleAxis(FlatHold ? flatRoll : 0f, Vector3.forward);
        void ResetSword()
        {
            if (!sword && anim)
            {
                var hand = anim.GetBoneTransform(HumanBodyBones.RightHand);
                sword = hand ? hand.Find("Greatsword") : null;
                if (sword) bladeLen = BladeTrail.MeasureBlade(sword);
            }
            if (!sword) return;
            if (!swordRestSet) { swordRest = sword.localRotation; swordRestSet = true; }
            if (Mx)
            {
                // nothing is written to the sword until the seat has been measured: the suit body re-parents it to its own hand
                // with its own scale, so the values cached on the hero's hand are not valid there
                return;   // Mixamo chain: the sword is seated by MxGrip (after the suit body has its pose); nothing is written here
            }
            if (FlatHold) SolveFlat();
            sword.localRotation = Seat;
            // 11-17 grip slide (Kido: "칼을 안쥐고있어"): the fist sat on the pommel end; slide the sword through the fist so
            // the hand wraps the handle ~1/3 below the guard (pommel out under the fist). Shots token slide=<m>.
            if (!swordPosSet) { swordPos0 = sword.localPosition; swordPosSet = true; }
            float handScale = sword.parent ? Mathf.Max(1e-4f, sword.parent.lossyScale.x) : 1f;   // GripSlide is metres; the hand bone is scaled ~100
            // 11-19 17:00 hand swap (Kido: "칼 좀 손으로 잘 잡고 팔 x자로 꼬지 좀 마"): when the blade is on the body's left the chain
            // clips have the hands change places along the handle (right hand 0.10 m toward the pommel, gs_berserker_anim.py
            // SWAP_R). The sword is slid through the right fist by the same amount, from the same measure: how far to the left
            // of the chest's facing the blade points (20..50 deg).
            float swapWant = 0f;
            if (HandSwap && TwoHand && gs && gs.Berserker && GreatswordCombat.VRisingChain && !GreatswordCombat.MixamoChain && spine2)
            {
                var cf = spine2.forward; cf.y = 0f; var bf = (sword.parent ? sword.parent.rotation : Quaternion.identity) * (Seat * Vector3.forward); bf.y = 0f;
                if (cf.sqrMagnitude > 1e-4f && bf.sqrMagnitude > 1e-4f) swapWant = Mathf.Clamp01((-Vector3.SignedAngle(cf, bf, Vector3.up) - 5f) / 25f);
            }
            swapS = Mathf.MoveTowards(swapS, swapWant, Time.deltaTime * 14f);
            sword.localPosition = SeatQ * swordPos0 - (sword.localRotation * Vector3.forward) * ((GripSlide - SwapSlide * swapS) / handScale);
            if (FlatHold) LiftTip();
        }

        // flat hold: the idle / walk-back seat ran the tip 0.1-0.27 m into the ground (it lay in the grass once flat); pitch
        // the blade up about the grip just enough to keep the tip minTipY over the root (never during the swing, tip > 0.3)
        public float minTipY = 0.08f;
        void LiftTip()
        {
            var a = sword.forward;
            float ground = transform.position.y, gy = sword.position.y - ground, ty = gy + a.y * bladeLen;
            if (ty >= minTipY || bladeLen < 0.2f) return;
            var h = new Vector3(a.x, 0f, a.z);
            if (h.sqrMagnitude < 1e-4f) return;
            h.Normalize();
            float s = Mathf.Clamp((minTipY - gy) / bladeLen, -0.95f, 0.95f), c = Mathf.Sqrt(1f - s * s);
            sword.rotation = Quaternion.FromToRotation(a, h * c + Vector3.up * s) * sword.rotation;
        }

        void SolveFlat()
        {
            if (!faceSet) MeasureBlade();
            var parent = sword.parent ? sword.parent.rotation : Quaternion.identity;
            float r = RollTo(parent * SeatQ * swordRest, flatRoll, true);
            float dt = Time.deltaTime;
            // 11-19 12:40 (Kido: "애니메이션 하면서 칼 뒤집으면 너무 티나 ... 첫프레임에 1프레임만에 뒤바뀐 채로 출발해"): the
            // edge flip for a swing the other way is not eased (it took ~3 f at flatRate); it is there on the swing's first frame
            bool snap = EdgeLead && TwoHand && gs && gs.Berserker && GreatswordCombat.VRisingChain && gs.Attacking;   // whole swing: the clip's hand rolls up to 150 deg in 2 f at the pass and the 60 deg/f cap lagged behind it (log snap1 f23-25), which showed as the blade turning over mid-swing
            flatRoll = dt > 0f && !snap ? Mathf.MoveTowardsAngle(flatRoll, r, flatRate * dt) : r;
        }

        // long edge = the side of the blade the point sits on, in sword space (vertices; the editor reads non-readable meshes)
        void MeasureBlade()
        {
            faceSet = true;
            var pts = new System.Collections.Generic.List<Vector3>(4096);
            foreach (var mf in sword.GetComponentsInChildren<MeshFilter>())
            {
                var m = mf.sharedMesh; if (!m) continue;
                Vector3[] vs;
                try { vs = m.vertices; } catch { continue; }
                foreach (var v in vs) pts.Add(sword.InverseTransformPoint(mf.transform.TransformPoint(v)));
            }
            if (pts.Count == 0) { Debug.LogWarning("[RoomRun] flat hold: sword mesh unreadable, long edge unknown"); return; }
            float zmax = float.MinValue; Vector3 tip = Vector3.zero;
            foreach (var p in pts) if (p.z > zmax) { zmax = p.z; tip = p; }
            var mn = Vector3.one * 1e9f; var mx = -mn;
            foreach (var p in pts) if (p.z > zmax * 0.55f && p.z < zmax * 0.85f) { mn = Vector3.Min(mn, p); mx = Vector3.Max(mx, p); }
            if (mx.x < mn.x) return;
            faceAxis = (mx.x - mn.x) < (mx.y - mn.y) ? Vector3.right : Vector3.up;   // thin axis
            var w = faceAxis == Vector3.right ? Vector3.up : Vector3.right;            // width axis
            float mid = Vector3.Dot((mn + mx) * 0.5f, w), off = Vector3.Dot(tip, w) - mid;
            // 11-17 09:49 Kido first said "또 칼 방향 반대자나", then "아니야 맞어": the point-side edge toward the body stays
            longAxis = w * (off >= 0f ? 1f : -1f);
            Debug.Log($"[RoomRun] flat hold: blade width {Vector3.Dot(mx - mn, w):0.000} thick {Vector3.Dot(mx - mn, faceAxis):0.000}, tip off-centre {off:0.000} -> long edge local {longAxis}");
        }

        Camera viewCam;
        // roll (deg, about the blade axis, added to rotation q) that puts the width square to the view with the long edge
        // toward the body; keep = the answer when the blade looks straight along the view
        float RollTo(Quaternion q, float keep, bool updateSide)
        {
            if (!viewCam || !viewCam.isActiveAndEnabled) viewCam = Camera.main;
            var view = viewCam ? viewCam.transform.forward : Vector3.down;
            var a = q * Vector3.forward;
            var c = Vector3.Cross(a, view);
            if (c.sqrMagnitude < 0.04f) return keep;   // blade along the view
            c.Normalize();
            var mid = sword.position + a * (bladeLen * 0.5f);
            var toBody = (hips ? hips.position : transform.position + Vector3.up) - mid;
            toBody -= a * Vector3.Dot(toBody, a);
            // 11-19 11:35 (Kido: "칼날이 짧은쪽이 베는 날 ... 항상 이동하는 방향으로 칼날의 위쪽 팁 중에 짧은쪽이 향하고
            // 있어야돼"): in a swing the SHORT edge (the cutting edge, opposite the point-side edge) leads the travel. The
            // swing's turn is known when it starts (RestSide +1 = left -> right = clockwise from above), so the roll is set
            // through the wind-up and never flips mid-pass. In the two-hand stance the edge then stays as the swing left it.
            bool chain = EdgeLead && TwoHand && gs && gs.Berserker && GreatswordCombat.VRisingChain;
            if (chain && gs.Attacking)
            {
                var trav = Vector3.Cross(Vector3.up, a) * gs.RestSide;
                float d = Vector3.Dot(c, trav);
                if (updateSide && Mathf.Abs(d) > 0.1f) { bodySign = -Mathf.Sign(d); sideSet = true; }
            }
            else if (chain && sideSet) { }
            else if (toBody.sqrMagnitude > 1e-6f)
            {
                float d = Vector3.Dot(c, toBody.normalized);
                if (updateSide && (Mathf.Abs(d) > 0.25f || !sideSet) && d != 0f) { bodySign = Mathf.Sign(d); sideSet = true; }   // hysteresis: no flip near the edge case
            }
            c *= bodySign;
            var w = q * longAxis;
            return Vector3.SignedAngle(w, c, a);
        }

        void LateUpdate()
        {
            Bind();
            ResetSword();
            if (!On) RunFeel();
            else { AimSplit(); if (Feel) RunFeel(); }
            // the drag spring pitches the blade again after the seat lift (walking down / left the tip went 5 cm under): lift once more
            if (TwoHand) Carry(); else if (OffHand) FreeArm();
            if (GripFist) Fist();
            if (FlatHold && !Mx) { if (sword) LiftTip(); FaceUp(); }
        }

        // 11-17 sword hand (Kido 09:49: "칼 쥐고 있는 손이 너무 이상해", "칼을 안쥐고있어"): the clips leave the right fingers
        // half open, so the handle sat beside an open claw. Every finger bone is curled toward the handle: the axis is
        // (bone direction x direction to the handle centre line), proximal / middle / distal by fistDeg; the thumb wraps
        // the other side by thumbDeg. Applied every frame after the arm passes; shots token fist=0 = the clip's hand.
        public static bool GripFist = true;
        public Vector3 fistDeg = new Vector3(85f, 95f, 70f);
        public Vector3 thumbDeg = new Vector3(25f, 35f, 35f);
        Transform[][] fingersR;
        void Fist()
        {
            if (!sword || !anim) return;
            if (fingersR == null)
            {
                HumanBodyBones[][] ids =
                {
                    new[] { HumanBodyBones.RightIndexProximal, HumanBodyBones.RightIndexIntermediate, HumanBodyBones.RightIndexDistal },
                    new[] { HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightMiddleIntermediate, HumanBodyBones.RightMiddleDistal },
                    new[] { HumanBodyBones.RightRingProximal, HumanBodyBones.RightRingIntermediate, HumanBodyBones.RightRingDistal },
                    new[] { HumanBodyBones.RightLittleProximal, HumanBodyBones.RightLittleIntermediate, HumanBodyBones.RightLittleDistal },
                    new[] { HumanBodyBones.RightThumbProximal, HumanBodyBones.RightThumbIntermediate, HumanBodyBones.RightThumbDistal },
                };
                fingersR = new Transform[ids.Length][];
                for (int i = 0; i < ids.Length; i++) { fingersR[i] = new Transform[3]; for (int j = 0; j < 3; j++) fingersR[i][j] = anim.GetBoneTransform(ids[i][j]); }
            }
            var a = sword.forward; var o = sword.position;
            for (int i = 0; i < fingersR.Length; i++)
            {
                var ch = fingersR[i];
                for (int j = 0; j < 3; j++)
                {
                    var b = ch[j]; if (!b) continue;
                    var tip = j < 2 && ch[j + 1] ? ch[j + 1].position : (b.childCount > 0 ? b.GetChild(0).position : b.position + (b.position - (j > 0 && ch[j - 1] ? ch[j - 1].position : b.parent.position)));
                    var dir = tip - b.position; if (dir.sqrMagnitude < 1e-8f) continue;
                    var toAxis = o + a * Vector3.Dot(b.position - o, a) - b.position;   // to the handle centre line
                    var k = Vector3.Cross(dir, toAxis); if (k.sqrMagnitude < 1e-10f) continue;
                    float deg = i == 4 ? thumbDeg[j] : fistDeg[j];
                    // only what is still missing: stop once the bone points past the handle
                    float now = Vector3.Angle(dir, toAxis);
                    float want = Mathf.Min(deg, Mathf.Max(0f, now - 15f) + 20f);
                    b.rotation = Quaternion.AngleAxis(want, k.normalized) * b.rotation;
                }
            }
        }

        // 11-17 free (left) arm (Kido 09:03: "칼 안잡은손이 너무 이상해 래퍼런스처럼해봐"): the clips kept it bent and tucked
        // against the belly. The reference (berserker_walk_ref_sheet.png, dash end A f166-178) holds it out wide for
        // balance: upper arm out to the side and down ~45 deg, a little back, elbow nearly straight, forearm carrying on
        // outward. Aimed in the body frame (visual forward) every frame outside attacks / rolls, a counter-swing with the
        // steps; eased out over 0.12 s into a swing (the clip's two-hand grip owns the arm there) and back in after.
        // Shots token offhand=0 = the clip's arm.
        public static bool OffHand = true;
        // 11-17 09:55 v2 (Kido: "a포즈자나", then the run reference "녹음 2026-11-17 095220.mp4" with "달리는것만 이거보고해
        // 왼손하고"): the reference runner keeps the left hand OFF the sword: elbow bent ~90, fist forward at chest height,
        // pumping against the legs; the right hand drags the blade low behind on the sword side. Only while running
        // (weight = speed), idle keeps the clip's arm.
        // v3 (Kido 14:03: "왼쪽 오른쪽 가든 앞으로 손 하고 있어서 너무 이상하자나"): the reference runner faces where it runs;
        // ours faces the cursor, so the pose is blended by the travel direction in the body frame and the pump swings
        // along the travel axis. Forward: fist at lower chest, close in. Sideways: elbow bent, fist by the ribs, small
        // sideways swing. Backward: arm low by the hip, small swing.
        public Vector3 offUpper = new Vector3(-0.20f, -0.88f, 0.22f);    // forward run (body frame: x right, y up, z forward)
        public Vector3 offFore = new Vector3(0.18f, 0.05f, 0.98f);
        public Vector3 sideUpper = new Vector3(-0.28f, -0.95f, -0.05f), sideFore = new Vector3(0.10f, 0.75f, 0.30f);
        public Vector3 backUpper = new Vector3(-0.22f, -0.95f, -0.22f), backFore = new Vector3(-0.05f, -0.75f, 0.30f);
        public float offSwing = 26f, sideSwing = 12f, backSwing = 8f;     // deg with the steps, along the travel axis
        public static bool RunArmV4 = true;   // shots token runarm=3 = the v3 body-frame poses
        public float runSwing = 48f;           // v4/v5: deg each way along the travel
        public float flexFwd = 100f, flexBack = 80f;   // v5c elbow flex (deg, 0 = straight); 125 put the fist on the hat brim
        public float travelLean = 0.25f;       // v5b: share of the WASD direction in the pump axis (0 = chest forward only)
        Vector3 travelS = Vector3.forward;
        float offW, stepNow; Transform foreL, handL;
        public float OffWeight => offW;
        void FreeArm()
        {
            if (!armL || !visual || !anim) return;
            if (!foreL) { foreL = anim.GetBoneTransform(HumanBodyBones.LeftLowerArm); handL = anim.GetBoneTransform(HumanBodyBones.LeftHand); }
            if (!foreL || !handL) return;
            bool busy = (gs && gs.Attacking) || (pc && pc.Rolling);
            float dt = Time.deltaTime;
            float run01 = pc ? Mathf.Clamp01(new Vector2(pc.Velocity.x, pc.Velocity.z).magnitude / Mathf.Max(0.1f, pc.moveSpeed) * 1.5f) : 0f;
            offW = Mathf.MoveTowards(offW, busy ? 0f : run01, dt / 0.12f);
            if (offW <= 0.001f) return;
            var fwd = visual.forward; fwd.y = 0f; if (fwd.sqrMagnitude < 1e-4f) return; fwd.Normalize();
            var right = Vector3.Cross(Vector3.up, fwd);
            Vector3 B(Vector3 v) => (right * v.x + Vector3.up * v.y + fwd * v.z).normalized;
            // left leg forward (step > 0 = left foot ahead) -> left arm back
            var v = pc ? pc.Velocity : Vector3.zero; v.y = 0f;
            var tl = v.sqrMagnitude > 0.04f ? new Vector3(Vector3.Dot(v, right), 0f, Vector3.Dot(v, fwd)).normalized : travelS;
            travelS = Vector3.Slerp(travelS, tl, 1f - Mathf.Exp(-dt / 0.15f));   // smooth across direction changes
            float wf = Mathf.Max(0f, travelS.z), wb = Mathf.Max(0f, -travelS.z), ws = Mathf.Abs(travelS.x), sum = Mathf.Max(1e-3f, wf + wb + ws);
            wf /= sum; wb /= sum; ws /= sum;
            Vector3 tu, tf;
            if (RunArmV4)
            {
                // 11-18 v4 (Kido 14:30: "왼손 뛸때 넘 쪼다 변태같아"; v3's sideways pose held the fist up by the face and the
                // forward pose held it stiff at the belly). A plain runner's arm in the TRAVEL frame (WASD direction): upper
                // arm hanging by the left side, elbow bent ~70 so the fist points along the travel, slightly up; the whole arm
                // pumps along the travel axis with the steps (left foot ahead -> left arm back), the elbow closing as the arm
                // comes forward. Travel toward the body's right is clamped (the fist never crosses the belly).
                // v5b (Kido 14:38: "상체는 이렇게하는데 wasd이동방향만 조금 영향받게해 가만히 있는게 아니라 앞뒤로 움직이는거야"):
                // the pump is in the UPPER BODY frame (the torso is on the cursor) in every direction; the WASD travel only
                // leans it a little (travelLean), and backward travel never turns it round
                var chest = pc ? pc.AimDir : fwd; chest.y = 0f; chest = chest.sqrMagnitude > 1e-4f ? chest.normalized : fwd;
                var cRight = Vector3.Cross(Vector3.up, chest);
                var vw = v.sqrMagnitude > 0.04f ? v.normalized : chest;
                float vx = Mathf.Min(Vector3.Dot(vw, cRight), 0.25f), vz = Mathf.Max(0f, Vector3.Dot(vw, chest));
                var Tt = cRight * vx + chest * vz; if (Tt.sqrMagnitude < 1e-4f) Tt = chest; Tt.Normalize();
                var T = Vector3.Slerp(chest, Tt, travelLean).normalized;
                var side = -cRight;
                // the feet signal peaks near +-0.45 at a run (step length / 0.9): normalise it so the arm really pumps
                float st = Mathf.Clamp(stepNow / 0.45f, -1f, 1f);
                // v5 (Kido 14:38 reference "녹음 2026-11-18 143807.mp4", Tryndamere run): the free arm pumps hard, elbow
                // folded past 90 the whole time, fist up at the chin on the forward swing and back past the hip on the back
                // swing, arm kept close to the body (not out wide)
                float sw = runSwing * st * (st < 0f ? 0.7f : 1f);   // + = arm back; forward a bit smaller (fist at the chest, not the hat)
                var ax = Vector3.Cross(Vector3.up, T);
                var q = Quaternion.AngleAxis(sw, ax);
                tu = q * (-Vector3.up * 0.97f + side * 0.12f - T * 0.05f).normalized;
                float u = Mathf.InverseLerp(-1f, 1f, st);   // 0 = arm forward, 1 = arm back
                // 11-18 v5c (Kido: "팔이 뒤로 꺾였짜나" - the elbow hyperextended): the forearm is no longer aimed freely; it is
                // the upper arm folded by an elbow angle in the elbow's own bending plane (toward the chest forward), so it
                // can only bend the right way. Flex 125 deg on the forward swing (fist to the chin), 80 on the back swing.
                var U = tu.normalized;
                var bendDir = T - U * Vector3.Dot(T, U);
                if (bendDir.sqrMagnitude < 1e-4f) bendDir = Vector3.up - U * Vector3.Dot(Vector3.up, U);
                bendDir.Normalize();
                float flex = Mathf.Lerp(flexFwd, flexBack, u) * Mathf.Deg2Rad;
                tf = (U * Mathf.Cos(flex) + bendDir * Mathf.Sin(flex) - side * 0.12f).normalized;
            }
            else
            {
                var up0 = (B(offUpper) * wf + B(sideUpper) * ws + B(backUpper) * wb).normalized;
                var fo0 = (B(offFore) * wf + B(sideFore) * ws + B(backFore) * wb).normalized;
                var travelW = right * travelS.x + fwd * travelS.z; if (travelW.sqrMagnitude < 1e-4f) travelW = fwd;
                var swing = Quaternion.AngleAxis((offSwing * wf + sideSwing * ws + backSwing * wb) * stepNow, Vector3.Cross(Vector3.up, travelW.normalized));
                tu = swing * up0; tf = swing * fo0;
            }
            var cu = foreL.position - armL.position;
            if (cu.sqrMagnitude > 1e-6f)
                armL.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(cu, tu), offW) * armL.rotation;
            var cf = handL.position - foreL.position;
            if (cf.sqrMagnitude > 1e-6f)
                foreL.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(cf, tf), offW) * foreL.rotation;
        }

        // 11-18 two-hand carry (Kido 16:00 on the V Rising recording: "바닥 안끌고 이 영상처럼 양손 상체 뒤틀림 디폴트로
        // 해봐"). Read from the reference's rest / run frames: both hands on the hilt at the sword-side hip, the blade
        // slanting down and back out to that side with the tip OFF the ground, the chest turned toward the sword. Default
        // stance (idle and moving), off at once in attacks / rolls. Replaces the drag spring and the free-arm pump:
        //   chest  - spine1/spine2 turned carryTwist toward the sword side, head turned back so it stays on the cursor
        //   right  - two-bone IK puts the hand at the right hip; the hand is turned so the blade points along carryDir
        //            (chest frame), pitched so the tip sits carryTip above the ground; a small wag / bob with the steps
        //   left   - two-bone IK onto the handle just below the right hand
        // Shots token twohand=0 = the 11-17 drag + pump.
        public static bool TwoHand = false;   // 11-20 Kido: "걷는거랑 모션이 점점 이상해지네, 예전 한손 파지법으로 바꿔, 바닥에 끌던 거" - default back to the 11-17 one-hand drag
        public float carryTwist = 0f, carryTip = 0.32f, carryWag = 5f, carryBend = 0f;   // 16:51 Kido: "몸통은 왜 죄다 뒤튼거야" - no code twist / bend on the torso at all (were 22 / 12)
        public static bool CarryIK = false;
        public float carryMax = 70f, carryWrist = 8f;   // deg the blade may be turned from the clip's hold; of that, at the wrist
        public Vector3 carryHip = new Vector3(0.26f, 0.02f, 0.06f);     // right-hand spot from the hips, in hip heights (x right, y up, z forward)
        public Vector3 carryDir = new Vector3(0.62f, 0f, -0.78f);       // blade heading in the chest frame (right, -, forward)
        public float carryLeftGrip = 0.11f;                             // m the left hand sits toward the pommel
        float layW; float carryW, carrySide = 1f; Transform foreR2, handR2, foreL2, handL2;
        int carryLayer = -2, carryPosed;
        int stanceLayer = -2, stancePosed; float standW;
        public static bool StanceFull = true;   // shots token stancefull=0 = arms-only carry while standing too
        float viaLeft, viaAt; string viaState;
        // low carry: the wind-up coil of the swing on that side (blade drawn back low, both hands); tokens carrylow=0|1, carryf=<R frame>,<L frame>
        public static bool CarryLow = true;
        public static float CarryLowFrameR = 5f, CarryLowFrameL = 5f;
        public static bool CarryMix = false;   // shots token carry=mix: Mixamo two-hand arms (moving) instead of the frozen guard poses
        public static float CarryFrameR = 19f, CarryFrameL = 27f;   // clip frames of the held guard (BZ_Slash2 20 f, BZ_Slash1 28 f)

        static void TwoBoneIK(Transform a, Transform b, Transform c, Vector3 target, Vector3 pole, float w)
        {
            float la = Vector3.Distance(a.position, b.position), lb = Vector3.Distance(b.position, c.position);
            var at = target - a.position;
            float d = Mathf.Clamp(at.magnitude, 0.02f, (la + lb) * 0.999f);
            var dir = at.normalized;
            float proj = (la * la - lb * lb + d * d) / (2f * d);
            float h = Mathf.Sqrt(Mathf.Max(0f, la * la - proj * proj));
            var pp = pole - dir * Vector3.Dot(pole, dir);
            pp = pp.sqrMagnitude > 1e-6f ? pp.normalized : Vector3.Cross(dir, Vector3.up).normalized;
            var elbow = a.position + dir * proj + pp * h;
            var wrist = a.position + dir * d;
            a.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(b.position - a.position, elbow - a.position), w) * a.rotation;
            b.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(c.position - b.position, wrist - b.position), w) * b.rotation;
        }

        void Carry()
        {
            if (!anim || !sword || !hips || !armR || !armL || !pc) return;
            if (!foreR2)
            {
                foreR2 = anim.GetBoneTransform(HumanBodyBones.RightLowerArm); handR2 = anim.GetBoneTransform(HumanBodyBones.RightHand);
                foreL2 = anim.GetBoneTransform(HumanBodyBones.LeftLowerArm); handL2 = anim.GetBoneTransform(HumanBodyBones.LeftHand);
            }
            if (!foreR2 || !handR2 || !foreL2 || !handL2) return;
            bool busy = (gs && gs.Attacking) || pc.Rolling;
            float dt = Time.deltaTime;
            carryW = busy ? 0f : Mathf.MoveTowards(carryW, 1f, dt / (gs && GreatswordCombat.VRisingChain ? GreatswordCombat.RefLower : 0.15f));
            // 11-19 17:45: the stance / carry LAYERS no longer drop to 0 during a chain swing. They used to (carryW = 0 while busy) and
            // came back over ~0.2-0.3 s after it, so while the attack layer faded out the base idle + the low walking carry showed
            // through: the blade tip sagged to -45 deg and came back after every unchained swing (log ax4 f101-107). In the chain the
            // layers stay at full weight under the attack layer (it is full-body, weight 1, and hides them) and are simply there
            // when it fades. carryW still gates the procedural blade correction below.
            bool chainLayers = gs && gs.Berserker && GreatswordCombat.VRisingChain;
            layW = pc.Rolling ? 0f : chainLayers ? Mathf.MoveTowards(layW, 1f, dt / 0.15f) : carryW;
            bool hidden = chainLayers && gs.AttackLayerW >= 0.98f;   // covered by the attack layer: poses may be swapped unseen
            // 16:56 pose layer (RoomRun.GSCarryLayer, "CarryPose"): the arms take the two-hand guard held at the end of the
            // swings (CarryR = end of SweepB, CarryL = end of SweepA) instead of being pushed onto the sword by IK
            if (carryLayer == -2) carryLayer = anim.GetLayerIndex("CarryPose");
            // 11-19 full-body standing stance (legs wide, chest bent forward, arms and blade drawn back; layer "StanceFull"):
            // only while standing still; moving keeps the arms-only carry over the running legs
            if (stanceLayer == -2) stanceLayer = anim.GetLayerIndex("StanceFull");
            standW = Mathf.MoveTowards(standW, Standing && StanceFull && stanceLayer >= 0 ? 1f : 0f, dt / 0.18f);
            if (stanceLayer >= 0)
            {
                anim.SetLayerWeight(stanceLayer, layW * standW);
                int sw01 = gs && gs.RestSide < 0 ? -1 : 1;
                if (Mx) { if (stancePosed != 9) { stancePosed = 9; anim.Play("MxIdle", stanceLayer, 0f); } }
                else if (sw01 != stancePosed && (stancePosed == 0 || layW * standW <= 0.02f || hidden)) { stancePosed = sw01; anim.Play(sw01 > 0 ? "StanceR" : "StanceL", stanceLayer, 0.5f); }
            }
            if (carryLayer >= 0)
            {
                anim.SetLayerWeight(carryLayer, Mx ? 0f : layW * (1f - standW));   // Mixamo chain: the locomotion clips carry the sword themselves
                // 11-19 15:50 (Kido: "캐릭터 걷는거 ... 왜 점점"): the walk Kido approved carries the sword low on the RIGHT with both hands.
                // When the sword's default side became the left (opener fix, 00:00) the walk silently switched to the mirrored
                // left pose (hands behind the back). Moving = always the approved right carry; only the standing rest follows RestSide.
                int want01 = WalkCarryRight ? 1 : gs && gs.RestSide < 0 ? -1 : 1;
                if (CarryMix)
                {
                    // the Mixamo two-hand set's arms, moving with the run (sword side fixed: its clips hold on the right)
                    if (carryPosed != 2) { carryPosed = 2; anim.CrossFadeInFixedTime("CarryMix", 0.15f, carryLayer, 0f); }
                }
                else if (want01 != carryPosed)
                {
                    bool first = carryPosed == 0;
                    bool viaFront = carryPosed == -want01;   // from the opposite hip
                    carryPosed = want01;
                    string st = CarryLow ? (want01 > 0 ? "CarryLowR" : "CarryLowL") : (want01 > 0 ? "CarryR" : "CarryL");
                    float at = (CarryLow ? (want01 > 0 ? CarryLowFrameR : CarryLowFrameL) : (want01 > 0 ? CarryFrameR : CarryFrameL)) / 30f;
                    // clip lengths: SweepA (BZ_Slash1) 28 f, SweepB (BZ_Slash2) 20 f; R uses B (guard) or A (low), L the other
                    float len = (CarryLow ? 10f : want01 > 0 ? 20f : 28f) / 30f;   // low poses: GS_CarryPoses.fbx, 10 f static
                    // the side only ever changes during a swing, while this layer's weight is 0: snap, nothing to see
                    if (first || layW <= 0.02f || hidden) { anim.Play(st, carryLayer, at / len); viaLeft = 0f; }
                    else if (CarryLow && viaFront)
                    {
                        // 20:58 (Kido: "3타하고 오른손이 몸뚫고 움직입니다"): a straight blend from the left-hip hold to the
                        // right-hip hold carried the hands THROUGH the torso. Side changes go round the front: first to
                        // that side's guard (hands in front of the belly, blade out to the side), then down to the low hold.
                        anim.CrossFadeInFixedTime(want01 > 0 ? "CarryR" : "CarryL", 0.12f, carryLayer, (want01 > 0 ? CarryFrameR : CarryFrameL) / 30f);
                        viaLeft = 0.16f; viaState = st; viaAt = at;
                    }
                    else anim.CrossFadeInFixedTime(st, 0.15f, carryLayer, at);
                }
            }
            if (carryLayer >= 0 && viaLeft > 0f && (viaLeft -= dt) <= 0f && !CarryMix) anim.CrossFadeInFixedTime(viaState, 0.18f, carryLayer, viaAt);
            if (carryW <= 0.001f || Mx) return;
            float w = carryW;
            var fwd = pc.AimDir; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) { fwd = visual ? visual.forward : transform.forward; fwd.y = 0f; }
            fwd.Normalize();
            var right = Vector3.Cross(Vector3.up, fwd);
            // chest toward the sword side, head back on the cursor
            // side rules (GreatswordCombat.RestSide): the stance mirrors to the side the last swing ended on, and comes
            // back to the right when the combo window closes
            carrySide = Mathf.MoveTowards(carrySide, gs ? gs.RestSide : 1f, dt / 0.09f);   // -1..1 in 0.18 s
            float sd = carrySide;
            float tw = carryTwist * w * sd;
            if (spine1) spine1.rotation = Quaternion.AngleAxis(tw * 0.5f, Vector3.up) * spine1.rotation;
            if (spine2) spine2.rotation = Quaternion.AngleAxis(tw * 0.5f, Vector3.up) * spine2.rotation;
            if (neck) neck.rotation = Quaternion.AngleAxis(-tw * 0.4f, Vector3.up) * neck.rotation;
            if (head) head.rotation = Quaternion.AngleAxis(-tw * 0.6f, Vector3.up) * head.rotation;
            // Kido 16:00: "양손파지 몸 사이드로휨" - the torso also bends sideways toward the sword hip (about the forward
            // axis: -angle tips the shoulders to the right), spread over the spine; the head stays level
            float bd = -carryBend * w * sd;
            if (spine) spine.rotation = Quaternion.AngleAxis(bd * 0.35f, fwd) * spine.rotation;
            if (spine1) spine1.rotation = Quaternion.AngleAxis(bd * 0.35f, fwd) * spine1.rotation;
            if (spine2) spine2.rotation = Quaternion.AngleAxis(bd * 0.3f, fwd) * spine2.rotation;
            if (head) head.rotation = Quaternion.AngleAxis(-bd * 0.8f, fwd) * head.rotation;

            if (carryLayer >= 0) return;   // the pose layer owns both arms
            float hh = Mathf.Max(0.2f, hips.position.y - transform.position.y);
            float bob = 0.02f * hh * stepNow;
            var handTarget = hips.position + (right * (carryHip.x * sd) + Vector3.up * carryHip.y + fwd * carryHip.z) * hh + Vector3.up * bob;
            // elbow pole: out to the right and back on the right side; down and back when the hand is across at the left hip
            var poleR = Vector3.Lerp(-fwd * 0.5f - Vector3.up * 0.8f + right * 0.2f, -fwd * 0.7f + right * 0.6f - Vector3.up * 0.2f, sd * 0.5f + 0.5f).normalized;
            // 16:51 (Kido: "오른손 손이 돌아갔자나 제발", "몸이 왜저렇게 됐어"): the hip IK plus a free hand turn broke the
            // wrist. CarryIK = false (default): the right arm keeps the clip's natural hold; only the blade heading is
            // corrected, carried by the shoulder and elbow (the wrist at most carryWrist deg), like HandCarries.
            if (CarryIK) TwoBoneIK(armR, foreR2, handR2, handTarget, poleR, w);
            // blade heading (a little wag with the steps), pitched so the tip floats carryTip over the ground
            var flat = Quaternion.AngleAxis(carryWag * stepNow, Vector3.up) * (right * (carryDir.x * sd) + fwd * carryDir.z).normalized;
            float gy = sword.position.y - transform.position.y;
            float s = Mathf.Clamp((carryTip - gy) / Mathf.Max(0.2f, bladeLen), -0.9f, 0.9f), c = Mathf.Sqrt(1f - s * s);
            var want = flat * c + Vector3.up * s;
            if (CarryIK) handR2.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(sword.forward, want), w) * handR2.rotation;
            else
            {
                var D = Quaternion.FromToRotation(sword.forward, want);
                float ang = Quaternion.Angle(Quaternion.identity, D);
                if (ang > carryMax) D = Quaternion.Slerp(Quaternion.identity, D, carryMax / ang);
                D = Quaternion.Slerp(Quaternion.identity, D, w);
                ang = Mathf.Min(ang, carryMax) * w;
                float wristShare = ang > 0.01f ? Mathf.Min(0.15f, carryWrist / ang) : 0f;
                var qU = Quaternion.Slerp(Quaternion.identity, D, 0.6f);                 // shoulder
                var qUF = Quaternion.Slerp(Quaternion.identity, D, 1f - wristShare);     // shoulder + elbow
                var qF = qUF * Quaternion.Inverse(qU);
                var qH = D * Quaternion.Inverse(qUF);
                armR.rotation = qU * armR.rotation;
                foreR2.rotation = qF * foreR2.rotation;
                handR2.rotation = qH * handR2.rotation;
            }
            // left hand onto the handle below the right hand
            float sc = hh / 0.9f;   // character size from the hip height (the model scale sits on a child)
            var lgrip = sword.position - sword.forward * (carryLeftGrip * sc);
            // only when the handle is comfortably in reach (a fully stretched arm across the chest looked broken)
            float reachL = Vector3.Distance(armL.position, foreL2.position) + Vector3.Distance(foreL2.position, handL2.position);
            float need = Vector3.Distance(armL.position, lgrip) / Mathf.Max(0.01f, reachL);
            float wl = w * Mathf.InverseLerp(0.98f, 0.85f, need);
            if (wl > 0.001f) TwoBoneIK(armL, foreL2, handL2, lgrip, (-right * 0.5f - Vector3.up * 0.7f - fwd * 0.3f).normalized, wl);
        }

        // flat hold, last: the walk drag spring's step roll / pitch turned the face back on edge every step (bladeFlat
        // dipped 0.75 -> 0.05 twice a stride). A pure roll about the blade axis puts the face up again; the tip does not move.
        void FaceUp()
        {
            if (!sword || !faceSet) return;
            float phi = Mathf.Clamp(RollTo(sword.rotation, 0f, false), -45f, 45f);   // only the drag's residual roll
            if (Mathf.Abs(phi) > 0.05f) sword.rotation = Quaternion.AngleAxis(phi, sword.forward) * sword.rotation;
            if (!viewCam) return;
            var a = sword.forward; var w = sword.rotation * longAxis;
            var mid = sword.position + a * (bladeLen * 0.5f);
            var tb = (hips ? hips.position : transform.position) - mid; tb -= a * Vector3.Dot(tb, a);
            LongToBody = tb.sqrMagnitude > 1e-6f ? Vector3.Dot(w, tb.normalized) : 0f;
            FaceToCam = Mathf.Abs(Vector3.Dot(sword.rotation * faceAxis, viewCam.transform.forward));
        }

        void AimSplit()
        {
            float want = 0f;
            bool active = On && visual && gs && gs.Berserker && pc && !pc.Rolling && !gs.Attacking;
            if (active)
            {
                var f = visual.forward; f.y = 0f;
                var a = pc.AimDir; a.y = 0f;
                if (f.sqrMagnitude > 1e-4f && a.sqrMagnitude > 1e-4f)
                    want = Mathf.Clamp(Vector3.SignedAngle(f, a, Vector3.up), -maxTwist, maxTwist);
            }
            // limb fix (11-15, Kido "칼질할 때마다 사지가 다 비틀어진다"): the visual snaps to the aim the moment a swing or
            // dash starts, so an upper-body twist (and the thighs turned back under it) still easing out over ~0.13 s
            // was stacked on top of the attack clip: chest over-turned, thighs wrung. Off at once while attacking/dashing.
            bool busy = gs && (gs.Attacking || (pc && pc.Rolling));
            // side-on stance: the chest stays side-on, the head alone turns to the cursor (neck 40 % / head 60 %, <= 60 deg)
            bool sideOn = active && Standing && TwoHand && StanceYaw != 0f && !Mx;
            float headWant = sideOn ? Mathf.Clamp(want, -60f, 60f) : 0f;
            if (sideOn) want = 0f;
            stanceHead = busy ? 0f : Mathf.MoveTowards(stanceHead, headWant, 360f * Time.deltaTime);
            if (Mathf.Abs(stanceHead) > 0.05f)
            {
                if (neck) neck.rotation = Quaternion.AngleAxis(stanceHead * 0.4f, Vector3.up) * neck.rotation;
                if (head) head.rotation = Quaternion.AngleAxis(stanceHead * 0.6f, Vector3.up) * head.rotation;
            }
            Twist = busy && !LegacyEase ? 0f : Mathf.MoveTowards(Twist, want, twistSpeed * Time.deltaTime);
            if (Mathf.Abs(Twist) < 0.05f || !hips) return;

            // hips (sash) turn with the upper body, thighs turned back in world space so the legs keep their direction
            var qh = Quaternion.AngleAxis(Twist * hipsShare, Vector3.up);
            var lq = upL ? upL.rotation : Quaternion.identity; var rq = upR ? upR.rotation : Quaternion.identity;
            hips.rotation = qh * hips.rotation;
            if (upL) upL.rotation = lq;
            if (upR) upR.rotation = rq;
            // the rest over the spine chain (world yaw about up, each bone a share)
            float rest = Twist * (1f - hipsShare);
            var bones = new[] { spine, spine1, spine2 };
            int n = 0; foreach (var b in bones) if (b) n++;
            if (n == 0) return;
            var qs = Quaternion.AngleAxis(rest / n, Vector3.up);
            foreach (var b in bones) if (b) b.rotation = qs * b.rotation;
        }

        // ---------------- v3 run feel ----------------
        float chestLook, headLook, sp, lean, bank, lastVisYaw, psi, psiVel, weight, psiPrev, uVel, stepS;
        Vector3 leanVec, leanVel;
        Transform armL;
        bool psiInit;

        void RunFeel()
        {
            float dt = Time.deltaTime;
            if (!visual || !gs || !gs.Berserker || !pc || !hips || dt <= 0f) return;
            if (!sword)
            {
                var hand = anim.GetBoneTransform(HumanBodyBones.RightHand);
                sword = hand ? hand.Find("Greatsword") : null;
                if (sword) bladeLen = BladeTrail.MeasureBlade(sword);
            }
            bool busy = gs.Attacking || pc.Rolling;
            var fwd = visual.forward; fwd.y = 0f; fwd.Normalize();
            var right = Vector3.Cross(Vector3.up, fwd);
            var v = pc.Velocity; v.y = 0f;
            float speed01 = Mathf.Clamp01(v.magnitude / Mathf.Max(0.1f, pc.moveSpeed));
            // all of it goes at once into a swing / dash (the clip owns the body there) and eases back in after
            weight = busy ? 0f : Mathf.MoveTowards(weight, 1f, 5f * dt);
            sp = Mathf.MoveTowards(sp, busy ? 0f : speed01, (speed01 > sp ? 4f : 3f) * dt);
            float visYaw = Yaw(fwd);
            float yawRate = Mathf.DeltaAngle(lastVisYaw, visYaw) / dt; lastVisYaw = visYaw;
            bank = On ? 0f : Mathf.MoveTowards(bank, Mathf.Clamp(-yawRate * bankPerDegPerSec, -bankMax, bankMax) * sp, 60f * dt);
            lean = On ? 0f : leanDeg * sp;
            // aim mode: the torso is on the cursor (split twist), so lean / sword side / "behind" go by the aim
            var aimF = pc.AimDir; aimF.y = 0f;
            if (On && aimF.sqrMagnitude > 1e-4f) { fwd = aimF.normalized; right = Vector3.Cross(Vector3.up, fwd); }
            var vdir = v.sqrMagnitude > 0.01f ? v.normalized : fwd;
            if (On)
            {
                // lean toward travel, smaller backward; a spring on the 2D lean vector (rocks back a little on stops)
                float d = Vector3.Dot(vdir, fwd);
                var leanT = busy ? Vector3.zero : vdir * (leanDeg * sp * (d >= 0f ? 0.85f + 0.15f * d : 0.85f + 0.45f * d));
                float lo = 2f * Mathf.PI * 2.2f;
                leanVel += (lo * lo * (leanT - leanVec) - 2f * 0.6f * lo * leanVel) * dt;
                leanVec += leanVel * dt;
            }

            // footstep signal from the feet along the travel (+1 left foot ahead, -1 right foot ahead), any leg clip
            float step = 0f;
            if (footL && footR) step = Mathf.Clamp(Vector3.Dot(footL.position - footR.position, On ? vdir : fwd) / (LegacySway ? 0.55f : 0.9f), -1f, 1f) * sp;
            if (!LegacySway) { stepS += (step - stepS) * (1f - Mathf.Exp(-dt / 0.07f)); step = stepS; }
            stepNow = step;

            // look: chest a little, head the rest
            var aim = pc.AimDir; aim.y = 0f;
            float twist = aim.sqrMagnitude > 1e-4f ? Vector3.SignedAngle(fwd, aim, Vector3.up) : 0f;
            float cWant = Mathf.Clamp(twist * 0.35f, -chestLookMax, chestLookMax);
            float hWant = Mathf.Clamp(twist - cWant, -headLookMax, headLookMax);
            chestLook = busy || On ? 0f : Mathf.MoveTowards(chestLook, cWant, lookSpeed * dt);
            headLook = busy || On ? 0f : Mathf.MoveTowards(headLook, hWant, lookSpeed * dt);
            if (!On) Twist = chestLook + headLook;
            if (weight <= 0.001f) { psiInit = false; BladeOffset = 0f; return; }

            float w = weight;
            if (On && leanVec.sqrMagnitude > 1e-4f)
            {
                // world-axis lean: a little at the hips (thighs held, the seam stays under the sash), the rest up the spine,
                // the head takes some back so it keeps looking ahead
                float L = leanVec.magnitude * w;
                var ax = Vector3.Cross(Vector3.up, leanVec / leanVec.magnitude);
                var lq = upL ? upL.rotation : Quaternion.identity; var rq = upR ? upR.rotation : Quaternion.identity;
                hips.rotation = Quaternion.AngleAxis(L * 0.2f, ax) * hips.rotation;
                if (upL) upL.rotation = lq;
                if (upR) upR.rotation = rq;
                if (spine) spine.rotation = Quaternion.AngleAxis(L * 0.35f, ax) * spine.rotation;
                if (spine1) spine1.rotation = Quaternion.AngleAxis(L * 0.25f, ax) * spine1.rotation;
                if (spine2) spine2.rotation = Quaternion.AngleAxis(L * 0.2f, ax) * spine2.rotation;
                if (head) head.rotation = Quaternion.AngleAxis(-L * 0.35f, ax) * head.rotation;
                // free arm counter-swings with the steps, the dragging arm gives a little
                if (armL) armL.rotation = Quaternion.AngleAxis(-w * 14f * step, right) * armL.rotation;
                if (armR) armR.rotation = Quaternion.AngleAxis(w * 4f * step, Vector3.up) * armR.rotation;
            }
            // lean + bank on the spine (shoulders ahead of the hips), shoulders counter-swing against the legs
            float chestYaw = chestLook - shoulderSwing * step;
            if (spine) spine.rotation = Quaternion.AngleAxis(w * lean * 0.5f, right) * Quaternion.AngleAxis(w * bank * 0.6f, fwd) * spine.rotation;
            if (spine1) spine1.rotation = Quaternion.AngleAxis(w * lean * 0.3f, right) * Quaternion.AngleAxis(w * bank * 0.4f, fwd) * spine1.rotation;
            if (spine2) spine2.rotation = Quaternion.AngleAxis(w * lean * 0.2f, right) * spine2.rotation;
            if (spine) spine.rotation = Quaternion.AngleAxis(w * chestYaw * 0.34f, Vector3.up) * spine.rotation;
            if (spine1) spine1.rotation = Quaternion.AngleAxis(w * chestYaw * 0.33f, Vector3.up) * spine1.rotation;
            if (spine2) spine2.rotation = Quaternion.AngleAxis(w * chestYaw * 0.33f, Vector3.up) * spine2.rotation;
            // the dragging arm does not swing round with the chest look
            if (armR && !On) armR.rotation = Quaternion.AngleAxis(-w * chestLook, Vector3.up) * armR.rotation;
            // head turns to the cursor and keeps looking ahead over the lean
            if (neck) neck.rotation = Quaternion.AngleAxis(w * headLook * 0.4f, Vector3.up) * Quaternion.AngleAxis(-w * lean * 0.4f, right) * neck.rotation;
            if (head) head.rotation = Quaternion.AngleAxis(w * headLook * 0.6f, Vector3.up) * Quaternion.AngleAxis(-w * lean * 0.4f, right) * head.rotation;

            if (!sword || TwoHand) return;   // two-hand carry owns the sword arm (no drag spring)
            var swordQ0 = sword.rotation;
            DragSword(dt, w, v, fwd, right, step);
            // hand fix: hand the sword's extra turn to the forearm + wrist, sword back to its keyed seat in the palm
            var D = sword.rotation * Quaternion.Inverse(swordQ0);
            GripSlip = Quaternion.Angle(sword.localRotation, Seat);
            WristBend = 0f;
            var fore = anim.GetBoneTransform(HumanBodyBones.RightLowerArm); var handT = anim.GetBoneTransform(HumanBodyBones.RightHand);
            if (HandCarries && fore && handT)
            {
                sword.rotation = swordQ0;
                float ang = Quaternion.Angle(Quaternion.identity, D);
                if (ang > maxCarry) D = Quaternion.Slerp(Quaternion.identity, D, maxCarry / ang);
                var up = Quaternion.Slerp(Quaternion.identity, D, armShare);
                var A = Quaternion.Slerp(Quaternion.identity, D, armShare + forearmShare);
                var Bq = D * Quaternion.Inverse(A);
                A = A * Quaternion.Inverse(up);
                if (armR) armR.rotation = up * armR.rotation; else A = up * A;
                fore.rotation = A * fore.rotation;
                handT.rotation = Bq * handT.rotation;
                WristBend = Quaternion.Angle(Quaternion.identity, Bq);
                GripSlip = Quaternion.Angle(sword.localRotation, Seat);
            }
        }

        void DragSword(float dt, float w, Vector3 v, Vector3 fwd, Vector3 right, float step)
        {
            // sword: spring on the blade's world yaw about the grip
            var bdf = sword.forward; bdf.y = 0f;
            if (bdf.sqrMagnitude < 1e-4f) return;
            float animYaw = Yaw(bdf);
            if (!psiInit) { psi = psiPrev = animYaw; psiVel = uVel = 0f; psiInit = true; }
            float target = animYaw;
            if (sp > 0.05f && v.sqrMagnitude > 0.01f)
            {
                // behind the velocity, turned out toward the sword (right) side by dragSlant
                var back = -v.normalized;
                var slanted = Vector3.RotateTowards(back, right, dragSlant * Mathf.Deg2Rad, 0f);
                float runYaw = Yaw(slanted) + (LegacySway ? wagDeg : calmWag) * step;
                target = Mathf.LerpAngle(animYaw, runYaw, Mathf.Clamp01(sp * 1.4f));
            }
            float om = 2f * Mathf.PI * (LegacySway ? springHz : calmHz);
            float damp = LegacySway ? springDamp : calmDamp;
            float err = Mathf.DeltaAngle(psi, target);
            psiVel += (om * om * err - 2f * damp * om * psiVel) * dt;
            psi += psiVel * dt;
            // never in toward the legs: at most 15 deg inward of straight behind the body, 75 out to the sword side
            var behind = -fwd;
            float outSign = Mathf.Sign(Vector3.SignedAngle(behind, right, Vector3.up));
            float baseOut = Vector3.SignedAngle(behind, bdf, Vector3.up) * outSign;   // where the clip has it
            float offOut = Mathf.Clamp(Mathf.DeltaAngle(animYaw, psi) * outSign, -15f - baseOut, 75f - baseOut);
            float off = offOut * outSign;
            if (On)
            {
                // no-go wedge: blade directions from the grip that pass within ~0.24 m of the hips (legs) in top view.
                // u = yaw measured from the grip->hips bearing in [0,360): allowed [hw, 360-hw] has no wrap, so the spring
                // can only go round the outside; a target inside the wedge goes to the edge on the back side
                var toB = hips.position - sword.position; toB.y = 0f;
                float dist = toB.magnitude;
                float hw = dist > 0.25f ? Mathf.Asin(Mathf.Clamp01(0.24f / dist)) * Mathf.Rad2Deg + 6f : 100f;
                float bear = Yaw(toB);
                if (!LegacySway)
                {
                    // 11-16: the wedge edge follows the grip->hips bearing, which swings ~20 deg with every strafe step; a
                    // blade parked on the edge (strafing toward the sword side) swung 40 deg with it. Low-passed (0.25 s).
                    float kf = 1f - Mathf.Exp(-dt / 0.25f);
                    if (!wedgeInit) { bearS = bear; hwS = hw; wedgeInit = true; }
                    bearS = Mathf.LerpAngle(bearS, bear, kf); hwS = Mathf.Lerp(hwS, hw, kf);
                    bear = bearS; hw = hwS;
                }
                float backU = Mathf.Repeat(Yaw(-fwd) - bear, 360f);
                float uT = Mathf.Repeat(target - bear, 360f);
                if (uT < hw || uT > 360f - hw) uT = backU >= 180f ? 360f - hw : hw;
                float uNow = Mathf.Repeat(psiPrev - bear, 360f);
                if (uNow < hw || uNow > 360f - hw) uNow = uNow < 180f ? hw : 360f - hw;
                uVel += (om * om * (uT - uNow) - 2f * damp * om * uVel) * dt;
                if (!LegacySway) uVel = Mathf.Clamp(uVel, -calmMaxRate, calmMaxRate);   // no snap round on a direction change
                uNow += uVel * dt;
                if (uNow < hw || uNow > 360f - hw) { uNow = Mathf.Clamp(uNow, hw, 360f - hw); uVel = 0f; }
                psi = psiPrev = bear + uNow;
                off = Mathf.DeltaAngle(animYaw, psi);
            }
            if (!On) psi = animYaw + off;
            BladeOffset = off * w;
            var gp = sword.position;
            sword.rotation = Quaternion.AngleAxis(BladeOffset, Vector3.up) * sword.rotation;
            // roll wag with the steps (the blade scrapes)
            float rollNow = LegacySway ? rollDeg * (2f * Mathf.Abs(step) - sp) : calmRoll * step;
            sword.rotation = Quaternion.AngleAxis(w * rollNow, sword.forward) * sword.rotation;
            // tip held just above the ground, bobbing between footfalls
            var b2 = sword.forward;
            var tip = gp + b2 * bladeLen;
            float wantY = pc.transform.position.y + tipHeight + bobHeight * (sp - Mathf.Abs(step));
            var k = Vector3.Cross(b2, Vector3.up);
            if (k.sqrMagnitude > 1e-4f && b2.y < 0.2f)
            {
                float a = Mathf.Asin(Mathf.Clamp((wantY - tip.y) / bladeLen, -0.5f, 0.5f)) * Mathf.Rad2Deg;
                sword.rotation = Quaternion.AngleAxis(w * a, k.normalized) * sword.rotation;
            }
        }
    }
}
