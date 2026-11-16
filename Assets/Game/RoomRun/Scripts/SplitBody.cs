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

        /// Leg facing for this frame: the move direction turned by 0/±90/180 to stay closest to the aim.
        public Vector3 LegDir(Vector3 moveFlat, Vector3 aim, Vector3 current)
        {
            // v3 default: run where you move, face the cursor when standing
            if (!On) return moveFlat.sqrMagnitude > 1e-4f ? moveFlat.normalized : aim;
            if (moveFlat.sqrMagnitude < 1e-4f) { legChoice = 0; return aim; }   // standing: the whole body faces the cursor
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
            sword.localRotation = swordRest;
        }

        void LateUpdate()
        {
            Bind();
            ResetSword();
            if (!On) { RunFeel(); return; }
            AimSplit();
            if (Feel) RunFeel();
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

            if (!sword) return;
            var swordQ0 = sword.rotation;
            DragSword(dt, w, v, fwd, right, step);
            // hand fix: hand the sword's extra turn to the forearm + wrist, sword back to its keyed seat in the palm
            var D = sword.rotation * Quaternion.Inverse(swordQ0);
            GripSlip = Quaternion.Angle(sword.localRotation, swordRest);
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
                GripSlip = Quaternion.Angle(sword.localRotation, swordRest);
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
