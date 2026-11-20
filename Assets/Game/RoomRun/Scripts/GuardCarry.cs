// 11-20 Kido: "뛸 때도 이 상태로 옆으로 뛰어야지" + the Cascadeur sample Cascy_sword.casc (frame 0) as THE carry pose:
// both hands on the handle low in front of the pelvis, left hand on the pommel side and higher, the blade running out to the
// right side and down. The numbers below are read straight from that file (control points, cm, Cascadeur Y-up, facing +Z,
// character right = -X) by scratchpad casc/parse; nothing is made up:
//   pelvis (-2.15, 77.04, -7.08)   hand_r (-13.63, 77.19, 20.41)   hand_l (21.48, 89.30, 8.53)
// so, relative to the pelvis, in (right, up, forward):
//   right palm ( 11.48,  0.15, 27.49)   left palm (-23.63, 12.26, 15.61)   pelvis height 77.04
// Every frame while not attacking (after the suit body has its pose, SuitBody 255): both arms are solved onto those points,
// scaled by the body's own pelvis height, in the aim frame (chest follows the cursor, legs keep running their own way), the
// hands are turned so the pinkies point to the pommel, and the sword is laid along the hands. Attacks fade it out.
using System.Linq;
using UnityEngine;

namespace RoomRun
{
    [DefaultExecutionOrder(257)]   // after SuitBody (255), before MxGrip (258) and the blade afterimages (300)
    public sealed class GuardCarry : MonoBehaviour
    {
        public static bool On = true;                                            // shots token guard=0|1
        public static Vector3 RightPalm = new Vector3(11.48f, 0.15f, 27.49f);    // (right, up, forward), cm, pelvis-relative
        public static Vector3 LeftPalm = new Vector3(-23.63f, 12.26f, 15.61f);
        public static float PelvisHeight = 77.04f;
        public static float PalmAt = 0.55f;
        public static bool TipOnGround = true;
        public Transform sword;
        GreatswordCombat combat;
        PlayerController pc;
        SplitBody split;
        Animator body;
        Transform hips, spine, chest, rU, rF, rW, rM, rI, rP, lU, lF, lW, lM, lI, lP;
        float pommelZ, tipZ, w;
        bool measured;

        public void Setup(Transform swordT, GreatswordCombat gc, PlayerController p, SplitBody sb) { sword = swordT; combat = gc; pc = p; split = sb; body = null; }

        bool Bind()
        {
            if (!sword || !sword.parent) return false;
            var a = sword.GetComponentInParent<Animator>();
            if (!a || !a.isHuman) return false;
            if (a == body && rW) return true;
            body = a;
            hips = a.GetBoneTransform(HumanBodyBones.Hips); spine = a.GetBoneTransform(HumanBodyBones.Spine); chest = a.GetBoneTransform(HumanBodyBones.Chest);
            rU = a.GetBoneTransform(HumanBodyBones.RightUpperArm); rF = a.GetBoneTransform(HumanBodyBones.RightLowerArm); rW = a.GetBoneTransform(HumanBodyBones.RightHand);
            rM = a.GetBoneTransform(HumanBodyBones.RightMiddleProximal); rI = a.GetBoneTransform(HumanBodyBones.RightIndexProximal); rP = a.GetBoneTransform(HumanBodyBones.RightLittleProximal);
            lU = a.GetBoneTransform(HumanBodyBones.LeftUpperArm); lF = a.GetBoneTransform(HumanBodyBones.LeftLowerArm); lW = a.GetBoneTransform(HumanBodyBones.LeftHand);
            lM = a.GetBoneTransform(HumanBodyBones.LeftMiddleProximal); lI = a.GetBoneTransform(HumanBodyBones.LeftIndexProximal); lP = a.GetBoneTransform(HumanBodyBones.LeftLittleProximal);
            if (!hips || !rU || !rF || !rW || !rM || !rI || !rP || !lU || !lF || !lW || !lM || !lI || !lP) { rW = null; return false; }
            if (LongGrip > 0f && !stretched) StretchGrip();
            // sword extent along its local Z (+Z = toward the tip): the pommel end is the minimum
            pommelZ = float.MaxValue; tipZ = float.MinValue;
            foreach (var mf in sword.GetComponentsInChildren<MeshFilter>())
            {
                if (!mf.sharedMesh) continue;
                var bd = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = bd.center + Vector3.Scale(bd.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    float z = sword.InverseTransformPoint(mf.transform.TransformPoint(c)).z;
                    pommelZ = Mathf.Min(pommelZ, z); tipZ = Mathf.Max(tipZ, z);
                }
            }
            measured = pommelZ < tipZ;
            Debug.Log($"[RoomRun] GuardCarry bound to {a.name}: sword z {pommelZ:0.000}..{tipZ:0.000}");
            return measured;
        }

        void LateUpdate()
        {
            if (pc) SuitBody.WhipMode = pc.WhipActive;   // 11-20 (whip thread note): the static stays true once the whip spawned; follow the live weapon
            bool want = On && !GreatswordCombat.MixamoChain && combat && !combat.Attacking && !(pc && (pc.EogeomActive || pc.WhipActive));   // the 이기어검 / 사복검 drive the arms themselves
            w = Mathf.MoveTowards(w, want ? 1f : 0f, Time.deltaTime / 0.12f);
            if (w <= 0.001f || !Bind()) return;
            if (GuardPoseData.Bones.Length > 0) { PoseFromCascadeur(); return; }
            // aim frame
            // Kido 11-20: "마우스 커서 위로 향했을 때" = the sample pose; the pose turns with the cursor. The chest is turned to the
            // cursor first (spine + chest share the twist), so the shoulders face where the hands go (the aim frame alone gave
            // X-arms whenever the chest faced away from the cursor).
            var fwd = pc ? pc.AimDir : transform.forward; fwd.y = 0f;
            fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : transform.forward;
            var right = Vector3.Cross(Vector3.up, fwd);
            var sh = rU.position - lU.position; sh.y = 0f;
            if (sh.sqrMagnitude > 1e-6f)
            {
                float twist = Vector3.SignedAngle(sh.normalized, right, Vector3.up) * w;
                if (spine && chest) { spine.rotation = Quaternion.AngleAxis(twist * 0.5f, Vector3.up) * spine.rotation; chest.rotation = Quaternion.AngleAxis(twist * 0.5f, Vector3.up) * chest.rotation; }
                else if (spine) spine.rotation = Quaternion.AngleAxis(twist, Vector3.up) * spine.rotation;
            }
            float k = Mathf.Max(0.3f, hips.position.y - body.transform.position.y) / PelvisHeight;   // cm -> this body's metres
            Vector3 P(Vector3 o) => hips.position + (right * o.x + Vector3.up * o.y + fwd * o.z) * k;
            var tR = P(RightPalm); var tL = P(LeftPalm);
            var blade = (tR - tL).normalized;                         // pommel side (left hand) -> tip side (right hand)
            // both hands on the handle: keep the Cascadeur spacing but never more handle than the sword has
            float handLen = (rM.position - rW.position).magnitude;
            float handle = Mathf.Abs(sword.lossyScale.z) * 0.22f * (tipZ - pommelZ);   // the grip is ~22% of this sword's length
            float gap = Mathf.Min((tR - tL).magnitude, Mathf.Max(handLen * 1.1f, handle - handLen * 0.6f));
            var mid = (tR + tL) * 0.5f;
            tR = mid + blade * gap * 0.5f; tL = mid - blade * gap * 0.5f;
            // the sample's tip rests on the floor (screenshot): keep the hand line's heading, pitch it down until the tip touches
            // the ground (capture guard2w: the hand line alone held the blade level at the waist)
            if (TipOnGround)
            {
                var flat = new Vector3(blade.x, 0f, blade.z);
                if (flat.sqrMagnitude > 1e-6f)
                {
                    flat.Normalize();
                    float tipLen = Mathf.Max(0.2f, (tipZ - pommelZ) * Mathf.Abs(sword.lossyScale.z) - handLen * 0.45f - gap * 0.5f);
                    float h = mid.y - (body.transform.position.y + 0.03f);
                    float sn = Mathf.Clamp(h / tipLen, 0.15f, 0.95f);
                    blade = flat * Mathf.Sqrt(1f - sn * sn) + Vector3.down * sn;
                    tR = mid + blade * gap * 0.5f; tL = mid - blade * gap * 0.5f;
                }
            }

            Solve(rU, rF, rW, rM, rI, rP, tR, blade, w);
            Solve(lU, lF, lW, lM, lI, lP, tL, blade, w);

            // the sword along the hands: pommel end just past the left palm, cutting (short) edge down
            var palmR = rW.position + (rM.position - rW.position) * PalmAt;
            var palmL = lW.position + (lM.position - lW.position) * PalmAt;
            var dir = (palmR - palmL).sqrMagnitude > 1e-6f ? (palmR - palmL).normalized : blade;
            var down = Vector3.down - dir * Vector3.Dot(Vector3.down, dir);
            if (down.sqrMagnitude < 1e-6f) down = -fwd;
            down.Normalize();
            var shortLocal = split ? -split.BladeLongAxis : Vector3.up;
            var q = Mathf.Abs(Vector3.Dot(shortLocal, Vector3.up)) > 0.999f
                ? (shortLocal.y > 0f ? Quaternion.identity : Quaternion.AngleAxis(180f, Vector3.forward))
                : Quaternion.FromToRotation(shortLocal, Vector3.up);
            var rot = Quaternion.LookRotation(dir, down) * q;
            float s = Mathf.Abs(sword.lossyScale.z);
            // the pommel end (local z = pommelZ) a little behind the left palm
            var pos = palmL - dir * (handLen * 0.45f) - (rot * new Vector3(0f, 0f, pommelZ * s));
            sword.rotation = Quaternion.Slerp(sword.rotation, rot, w);
            sword.position = Vector3.Lerp(sword.position, pos, w);
        }

        // 11-20 Kido: "칼 손잡이가 짧은 거잖아 ... 갈색 손잡이 부분만 늘리면 문제 없잖아": the greatsword's handle only fits one
        // fist; the sample's sword has a two-hand handle. The handle is a plain tube with vertex rings only at its two ends
        // (profile: pommel ring at 0..0.05 m, guard from 0.30 m, nothing between), so every vertex below the middle of that
        // empty stretch moves LongGrip metres toward the pommel: the blade, guard and pommel keep their shape, only the tube
        // gets longer. Done on a runtime copy of the mesh; the model file is untouched.
        public static bool KeepBodyFacing = false;   // 11-20 19:00 Kido: back to the joe_cascadeur_forward version   // token keepface=0|1
        public static float PoseYawOffset = 0f;   // 11-20 19:00 Kido "이때 버전으로 돌려" = the joe_cascadeur_forward clip (0)   // 11-20 18:59 Kido: back to the 90 he had approved (0 and 270 both wrong)   // v3: the sample's own forward (toes) = cursor; 270 walked backward   // 11-20 v2: chest faces the cursor (Kido rule 이동조준_방향규칙); 90 had the chest facing away from it   // shots token poseyaw=
        public static float LongGrip = 0.55f;   // Kido asked: longer handle (seat unchanged: right fist under the guard)   // 11-20 v2 Kido "손잡이 좀 길게 하라니까 아직도 손잡이 못 잡네": 0.30 -> 0.55
        public static float BladeLenMul = 2.0f, WidthMul = 1.5f, BladeThin = 0.8f, GuardThin = 0.55f;   // shots tokens bladelen= bladewide=    // metres added to the handle (0 = the model as it is); shots token longgrip=
        float dbgT; bool stretched, gripLogged; float guardFromPommel = -1f;
        void StretchGrip()
        {
            stretched = true;
            var mf = sword.GetComponentsInChildren<MeshFilter>().Where(m => m.sharedMesh).OrderByDescending(m => m.sharedMesh.vertexCount).FirstOrDefault();
            if (!mf || !mf.sharedMesh.isReadable) { Debug.LogWarning("[RoomRun] GuardCarry: sword mesh missing or not readable, handle not stretched"); return; }
            var mesh = Instantiate(mf.sharedMesh); mesh.name = mf.sharedMesh.name + "_LongGrip";
            var axis = mf.transform.InverseTransformDirection(sword.forward).normalized;      // mesh space, toward the tip
            float scale = mf.transform.TransformVector(axis).magnitude;                        // mesh units -> metres
            var v = mesh.vertices;
            var p = new float[v.Length]; float lo = float.MaxValue, hi = float.MinValue;
            for (int i = 0; i < v.Length; i++) { p[i] = Vector3.Dot(v[i], axis); lo = Mathf.Min(lo, p[i]); hi = Mathf.Max(hi, p[i]); }
            // the biggest empty stretch in the pommel third = the handle tube
            var low = p.Where(x => x < lo + (hi - lo) * 0.33f).OrderBy(x => x).ToArray();
            float gap = 0f, cut = lo, top = lo;
            for (int i = 1; i < low.Length; i++) if (low[i] - low[i - 1] > gap) { gap = low[i] - low[i - 1]; cut = (low[i] + low[i - 1]) * 0.5f; top = low[i]; }
            float extra = LongGrip / Mathf.Max(1e-6f, scale);
            // 11-20 Kido: "칼 몸통이 대검에 비해 많이 작은데 비율 맞춰보고, 옆으로 뚱뚱한 부분은 살짝 누르던지": proportions of the
            // sample's sword (Cascy: handle 0.46, guard 0.10, blade 1.17 long x 0.22 wide x 0.023 thick; ours: blade 0.75 x 0.137
            // x 0.034 and a 0.45 m guard block up to 0.145 thick). Blade longer and wider, guard block pressed thin.
            var nrm = mesh.normals;
            Vector3 a1 = Vector3.right, a2 = Vector3.up;
            if (Mathf.Abs(axis.x) > 0.7f) { a1 = Vector3.up; a2 = Vector3.forward; } else if (Mathf.Abs(axis.y) > 0.7f) { a1 = Vector3.right; a2 = Vector3.forward; } else { a1 = Vector3.right; a2 = Vector3.up; }
            float L = hi - lo;
            // blade = the top 35%: its spread on a1/a2 tells thin vs wide
            float min1 = float.MaxValue, max1 = float.MinValue, min2 = float.MaxValue, max2 = float.MinValue;
            for (int i = 0; i < v.Length; i++) if (p[i] > lo + L * 0.65f) { float x1 = Vector3.Dot(v[i], a1), x2 = Vector3.Dot(v[i], a2); min1 = Mathf.Min(min1, x1); max1 = Mathf.Max(max1, x1); min2 = Mathf.Min(min2, x2); max2 = Mathf.Max(max2, x2); }
            Vector3 thinA = a1, wideA = a2; float cT = (min1 + max1) * 0.5f, cW = (min2 + max2) * 0.5f, bladeThick = max1 - min1;
            if (max1 - min1 > max2 - min2) { thinA = a2; wideA = a1; cT = (min2 + max2) * 0.5f; cW = (min1 + max1) * 0.5f; bladeThick = max2 - min2; }
            // blade start: walking up from the guard, the first slice whose thickness is back to the blade's
            float bladeStart = lo + L * 0.5f;
            {
                int n = 40; float step = (hi - top) / n;
                for (int k = 0; k < n; k++)
                {
                    float s0 = top + k * step, tMin = float.MaxValue, tMax = float.MinValue; int cnt = 0;
                    for (int i = 0; i < v.Length; i++) if (p[i] >= s0 && p[i] < s0 + step) { float t = Vector3.Dot(v[i], thinA); tMin = Mathf.Min(tMin, t); tMax = Mathf.Max(tMax, t); cnt++; }
                    if (cnt > 0 && tMax - tMin < bladeThick * 1.3f) { bladeStart = s0; break; }
                }
            }
            float ramp = L * 0.04f;
            for (int i = 0; i < v.Length; i++)
            {
                if (p[i] < cut) { v[i] -= axis * extra; continue; }
                float bk = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(bladeStart - ramp, bladeStart + ramp, p[i]));   // 0 guard, 1 blade
                float tk = Mathf.Lerp(GuardThin, BladeThin, bk);
                float x = Vector3.Dot(v[i], thinA) - cT, y = Vector3.Dot(v[i], wideA) - cW;
                float pl = p[i] > bladeStart ? bladeStart + (p[i] - bladeStart) * BladeLenMul : p[i];
                v[i] = axis * pl + thinA * (cT + x * tk) + wideA * (cW + y * WidthMul);
                if (nrm != null && i < nrm.Length)
                {
                    var nn = nrm[i];
                    float lm = p[i] > bladeStart ? BladeLenMul : 1f;
                    nrm[i] = (axis * (Vector3.Dot(nn, axis) / lm) + thinA * (Vector3.Dot(nn, thinA) / tk) + wideA * (Vector3.Dot(nn, wideA) / WidthMul)).normalized;
                }
            }
            mesh.vertices = v; if (nrm != null && nrm.Length == v.Length) mesh.normals = nrm; mesh.RecalculateBounds(); mesh.RecalculateTangents();
            Debug.Log($"[RoomRun] GuardCarry reshape: blade from {(bladeStart - lo) * scale:0.000} m, x{BladeLenMul} long, x{WidthMul} wide, thickness x{BladeThin} blade / x{GuardThin} guard (blade was {bladeThick * scale:0.000} thick)");
            mf.sharedMesh = mesh;
            guardFromPommel = (top - (lo - extra)) * scale;
            Debug.Log($"[RoomRun] GuardCarry: handle stretched {LongGrip:0.00} m (tube gap {gap * scale:0.000} m at {(cut - lo) * scale:0.000} m from the pommel); guard now {guardFromPommel:0.000} m from the pommel");
        }

        // 11-20 Kido's exported Cascadeur pose (GuardPoseData, baked by RoomRun.GuardPoseBake): upper body = the sample exactly,
        // turned to the cursor ("상체는 마우스 커서, 하체는 WASD"); the hips and legs keep the WASD walk. The spine is set in
        // world space (aim yaw * the sample's spine), so walking forward never turns the chest back to the front.
        Transform[] poseBones; int poseBound = -1;
        void PoseFromCascadeur()
        {
            if (poseBound != body.GetInstanceID())
            {
                poseBones = new Transform[GuardPoseData.Bones.Length];
                for (int i = 0; i < poseBones.Length; i++) poseBones[i] = body.GetBoneTransform(GuardPoseData.Bones[i]);
                poseBound = body.GetInstanceID();
            }
            var fwd = pc ? pc.AimDir : transform.forward; fwd.y = 0f;
            fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : transform.forward;
            // 11-20 Kido: the baked "north" (Cascadeur scene forward) was his WEST pose; his north = that pose turned +90 deg
            var yaw = Quaternion.LookRotation(fwd, Vector3.up) * Quaternion.AngleAxis(PoseYawOffset, Vector3.up);
            bool harmSpine = HumanoidArms && GuardPoseArms.Bones.Length > 0 && GuardPoseArms.SpineRoot != Quaternion.identity;
            // 11-20 19:00 Kido "앞으로 할 때 방향 또 틀렸잖아": the facing must stay the approved move/aim logic (SplitBody: root follows
            // movement, pelvis + chest twist toward the cursor). KeepBodyFacing leaves the spine as that logic set it; only the
            // pose above the spine (chest, arms, hands) comes from the Cascadeur sample.
            if (spine && !KeepBodyFacing) spine.rotation = Quaternion.Slerp(spine.rotation, yaw * (harmSpine ? GuardPoseArms.SpineRoot : GuardPoseData.SpineRoot), w);
            for (int i = 0; i < poseBones.Length; i++)
                if (poseBones[i]) poseBones[i].localRotation = Quaternion.Slerp(poseBones[i].localRotation, GuardPoseData.Local[i], w);
            // proposal (Kido "리깅으로 했는데 조인트마다 왜 저렇게"): arms/hands from Unity's humanoid retarget (GuardPoseBakeH), twist right
            bool harm = HumanoidArms && GuardPoseArms.Bones.Length > 0;
            if (harm)
            {
                if (armBones == null || armBones.Length != GuardPoseArms.Bones.Length) { armBones = new Transform[GuardPoseArms.Bones.Length]; for (int i = 0; i < armBones.Length; i++) armBones[i] = body.GetBoneTransform(GuardPoseArms.Bones[i]); }
                for (int i = 0; i < armBones.Length; i++) if (armBones[i]) armBones[i].localRotation = Quaternion.Slerp(armBones[i].localRotation, GuardPoseArms.Local[i], w);
                if (!harmLogged) { harmLogged = true; Debug.Log($"[RoomRun] GuardCarry: humanoid-retarget arms on ({armBones.Length} bones)"); }
            }
            // the sword: the sample's blade axis turned with the cursor, pommel just behind the left palm, cutting edge down
            // 11-20 Kido "칼을 왜 저렇게 잡고 있어": the sword is tied to the right hand the way the sample's sword sits in Cascy's hand
            bool inHand = GuardPoseData.BladeInHand.sqrMagnitude > 0.5f;
            var dir = inHand ? rW.rotation * GuardPoseData.BladeInHand : yaw * GuardPoseData.Blade;
            var flat = inHand ? rW.rotation * GuardPoseData.FlatInHand : yaw * GuardPoseData.Flat;
            if (harm && GuardPoseArms.BladeInHand.sqrMagnitude > 0.5f) { dir = rW.rotation * GuardPoseArms.BladeInHand; flat = rW.rotation * GuardPoseArms.FlatInHand; }
            // v2: the handle runs THROUGH both fists (left palm -> right palm), as in the sample (both hands on its axis), so the
            // hands visibly hold it; the hand-tied axis only decides the roll
            var pL = lW.position + (lM.position - lW.position) * PalmAt;
            var pR = rW.position + (rM.position - rW.position) * PalmAt;
            if (ThroughFists && (pR - pL).sqrMagnitude > 0.01f) { dir = (pR - pL).normalized; flat = Vector3.ProjectOnPlane(flat, dir); }
            // v3 (Kido "여기 손목은 왜 이래, 팔이랑 다 이상하네"): the bone-direction retarget gets the arm lines right but not the
            // wrist twist, so the fists did not close round the handle. Each hand is turned (wrist only) so the handle runs through
            // it the way the Mixamo great-sword mocap holds it: toward the pommel = MxGrip.HandleAxis in the hand's own basis
            // (e1 wrist->middle knuckle, e2 index->pinky, e3 = e1 x e2; mirrored e3 for the left hand). Then the palms are re-read.
            if (FixWrists && (pR - pL).sqrMagnitude > 0.01f)
            {
                GripHand(rW, rM, rI, rP, -dir, false);
                GripHand(lW, lM, lI, lP, -dir, true);
                pL = lW.position + (lM.position - lW.position) * PalmAt;
                pR = rW.position + (rM.position - rW.position) * PalmAt;
                if ((pR - pL).sqrMagnitude > 0.01f) { dir = (pR - pL).normalized; flat = Vector3.ProjectOnPlane(flat, dir); }
            }
            var edge = Vector3.Cross(dir, flat).normalized;
            if (edge.y > 0f) edge = -edge;
            var shortLocal = split ? -split.BladeLongAxis : Vector3.up;
            var q = Mathf.Abs(Vector3.Dot(shortLocal, Vector3.up)) > 0.999f
                ? (shortLocal.y > 0f ? Quaternion.identity : Quaternion.AngleAxis(180f, Vector3.forward))
                : Quaternion.FromToRotation(shortLocal, Vector3.up);
            var rot = Quaternion.LookRotation(dir, edge) * q;
            float s = Mathf.Abs(sword.lossyScale.z);
            // right palm on the handle where the sample holds it (GripRight of the length from the pommel), never past our handle top
            var palmR = rW.position + (rM.position - rW.position) * PalmAt;
            float len = (tipZ - pommelZ) * s;
            // right fist just under the guard (on the long handle), else where the sample holds it
            float handLenR = (rM.position - rW.position).magnitude;
            // left fist one hand-width above the pommel; the right fist then lands on the handle as long as the handle is longer
            // than the gap between the fists (logged once)
            float gapF = (pR - pL).magnitude;
            if (!gripLogged) { gripLogged = true; Debug.Log($"[RoomRun] GuardCarry grip: fists {gapF:0.000} m apart, handle (pommel->guard) {guardFromPommel:0.000} m, right fist {(handLenR * 0.6f + gapF):0.000} m from the pommel"); }
            float grip = guardFromPommel > 0f ? guardFromPommel - handLenR * 0.55f : Mathf.Min(GuardPoseData.GripRight * len, 0.25f * len);
            var pos = ThroughFists ? pL - dir * (handLenR * 0.6f) - (rot * new Vector3(0f, 0f, pommelZ * s))
                                   : palmR - dir * grip - (rot * new Vector3(0f, 0f, pommelZ * s));   // reverted to the pre-15:32 seat
            // proposal (Kido "디폴트가 바닥 안으로 들어간다"): the longer blade's tip went under the floor; pitch it up about the right
            // fist just enough to keep the tip a few cm above the ground
            if (TipClamp)
            {
                var tip = pos + rot * new Vector3(0f, 0f, tipZ * s);
                float floor = body.transform.position.y + 0.05f;
                if (tip.y < floor)
                {
                    var pivot = palmR; var v = tip - pivot; float L = v.magnitude;
                    var flatV = new Vector3(v.x, 0f, v.z);
                    float wantY = Mathf.Clamp(floor - pivot.y, -L * 0.98f, L * 0.98f);
                    var nv = flatV.normalized * Mathf.Sqrt(Mathf.Max(0f, L * L - wantY * wantY)) + Vector3.up * wantY;
                    var turn = Quaternion.FromToRotation(v, nv);
                    rot = turn * rot; pos = pivot + turn * (pos - pivot);
                    // the left fist follows the lifted handle (arm only; Kido allowed hands and arms)
                    if (ThroughFists)
                    {
                        var lPalm = lW.position + (lM.position - lW.position) * PalmAt;
                        var want = pivot + turn * (lPalm - pivot);
                        var keepW = lW.rotation;
                        TwoBone(lU, lF, lW, lW.position + (want - lPalm));
                        lW.rotation = turn * keepW;
                    }
                }
            }
            sword.rotation = Quaternion.Slerp(sword.rotation, rot, w);
            sword.position = Vector3.Lerp(sword.position, pos, w);
        }
        public static bool HumanoidArms = true, TipClamp = false;   // 11-20 Kido "억지로 맞추지 말고 원래 cascadeur 한 거": the Cascadeur pose as-is (humanoid retarget, no IK/wrist fixes); only the sword adapts   // proposals, shots tokens harm=1 tipclamp=1 (off until Kido approves)
        Transform[] armBones; bool harmLogged;

        public static bool ThroughFists = false;   // 15:3x v2 seat (sword through both fists), reverted
        public static bool FixWrists = false;   // 11-20 15:36 Kido "제대로 된 거 망치지 좀 말라고": reverted (off)   // shots token wrist=0|1
        void GripHand(Transform W, Transform M, Transform I, Transform P, Vector3 towardPommel, bool left)
        {
            for (int it = 0; it < 2; it++)
            {
                var e1 = (M.position - W.position).normalized;
                var e2 = P.position - I.position; e2 = (e2 - e1 * Vector3.Dot(e2, e1)).normalized;
                var e3 = Vector3.Cross(e1, e2);
                var h = MxGrip.HandleAxis; if (left) h.z = -h.z;
                var cur = (e1 * h.x + e2 * h.y + e3 * h.z).normalized;   // where the handle would run through this hand now
                W.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(cur, towardPommel), w) * W.rotation;
            }
        }

        // two-bone arm IK so the palm lands on t, then the hand turned so index->pinky points to the pommel (-blade)
        void Solve(Transform U, Transform F, Transform W, Transform M, Transform I, Transform Pk, Vector3 t, Vector3 blade, float wt)
        {
            var palmOff = (M.position - W.position) * PalmAt;
            var keepU = U.rotation; var keepF = F.rotation; var keepW = W.rotation;
            TwoBone(U, F, W, t - palmOff);
            // hand: pinky side toward the pommel, palm facing the handle (wrist -> knuckles across the handle)
            var e2 = Pk.position - I.position;
            if (e2.sqrMagnitude > 1e-8f) W.rotation = Quaternion.FromToRotation(e2.normalized, -blade) * W.rotation;
            // second pass: the hand turn moved the palm a little
            palmOff = (M.position - W.position) * PalmAt;
            var hand = W.rotation;
            TwoBone(U, F, W, t - palmOff);
            W.rotation = hand;
            if (wt < 0.999f)
            {
                U.rotation = Quaternion.Slerp(keepU, U.rotation, wt); F.rotation = Quaternion.Slerp(keepF, F.rotation, wt); W.rotation = Quaternion.Slerp(keepW, W.rotation, wt);
            }
        }

        static void TwoBone(Transform A, Transform B, Transform C, Vector3 t)
        {
            Vector3 a = A.position, b = B.position, c = C.position;
            float lab = (b - a).magnitude, lcb = (b - c).magnitude;
            float lat = Mathf.Clamp((t - a).magnitude, 0.01f, lab + lcb - 0.005f);
            float acab0 = Mathf.Acos(Mathf.Clamp(Vector3.Dot((c - a).normalized, (b - a).normalized), -1f, 1f));
            float babc0 = Mathf.Acos(Mathf.Clamp(Vector3.Dot((a - b).normalized, (c - b).normalized), -1f, 1f));
            float acat0 = Mathf.Acos(Mathf.Clamp(Vector3.Dot((c - a).normalized, (t - a).normalized), -1f, 1f));
            float acab1 = Mathf.Acos(Mathf.Clamp((lcb * lcb - lab * lab - lat * lat) / (-2f * lab * lat), -1f, 1f));
            float babc1 = Mathf.Acos(Mathf.Clamp((lat * lat - lab * lab - lcb * lcb) / (-2f * lab * lcb), -1f, 1f));
            var axis0 = Vector3.Cross(c - a, b - a);
            if (axis0.sqrMagnitude < 1e-8f) axis0 = Vector3.Cross(c - a, Vector3.up);
            axis0.Normalize();
            var axis1 = Vector3.Cross(c - a, t - a);
            A.rotation = Quaternion.AngleAxis((acab1 - acab0) * Mathf.Rad2Deg, axis0) * A.rotation;
            B.rotation = Quaternion.AngleAxis((babc1 - babc0) * Mathf.Rad2Deg, axis0) * B.rotation;
            if (axis1.sqrMagnitude > 1e-10f) A.rotation = Quaternion.AngleAxis(acat0 * Mathf.Rad2Deg, axis1.normalized) * A.rotation;
        }
    }
}
