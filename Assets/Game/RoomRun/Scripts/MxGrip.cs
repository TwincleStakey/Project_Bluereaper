// 11-19 two-hand grip for the Mixamo Great Sword mocap on the suit body (greatsword restart, GreatswordCombat.MixamoChain).
//
// Measured on the mocap itself (Y Bot, scratchpad mx/grip_mx.py): in an anatomical basis of the RIGHT hand
//   e1 = wrist -> middle knuckle, e2 = index knuckle -> pinky knuckle (perpendicular to e1), e3 = e1 x e2
// the line from the right palm centre to the left palm centre (= the handle, toward the pommel) is
//   idle (-0.26, +0.86, +0.43), low slash (-0.34, +0.85, +0.40), down slash (-0.30, +0.87, +0.40), spin (-0.28, +0.87, +0.40)
// with only 9-17 deg of spread inside the slashes: the actor really holds one handle, and the palms are ~2 hand-lengths apart.
//
// So, every frame, after the suit body has copied the pose (SuitBody 255):
//   1. the sword is seated in the right hand from those landmarks (blade = away from the pommel, cutting edge toward the
//      knuckles), with the top of the handle in the right palm;
//   2. the LEFT arm is solved (two-bone IK, position only, the hand keeps the mocap rotation) so the left palm sits on the
//      handle two hand-lengths below the right one. On the stock mocap body this is a no-op (the hand is already there); it
//      is needed because the suit body has its chest and shoulders scaled up (SuitBody.BulkUp), which spreads the hands ~1.5x.
// Nothing else is posed here: no spine, no right arm, no wrists turned.
using UnityEngine;

namespace RoomRun
{
    [DefaultExecutionOrder(258)]   // after SuitBody (255), before WhipCombat (260) and the blade afterimages (300)
    public sealed class MxGrip : MonoBehaviour
    {
        public static bool LeftHandIK = true;                                   // shots token mxik=0|1
        // toward the pommel, in (e1, e2, e3) of the right hand. The e3 sign is flipped against the Blender measurement (+0.41):
        // Unity is left-handed, so e1 x e2 computed here points to the other side of the palm than it does in Blender.
        public static Vector3 HandleAxis = new Vector3(-0.30f, 0.86f, -0.41f);
        public static float PalmAt = 0.55f;                                     // palm centre along wrist -> middle knuckle
        public static float PalmGap = 2.0f;                                     // left palm below the right one, in hand lengths
        public Transform sword;
        SplitBody split;
        Animator body;
        Transform rW, rM, rI, rP, lW, lM, lU, lF;
        Vector3 gripLocal; bool gripSet; float handleLen;
        public float LeftReach { get; private set; }   // metres the left palm was moved this frame (dev log)

        public void Setup(Transform swordT, SplitBody sb) { sword = swordT; split = sb; body = null; gripSet = false; }

        bool Bind()
        {
            if (!sword || !sword.parent) return false;
            var a = sword.GetComponentInParent<Animator>();
            if (!a || !a.isHuman) return false;
            if (a == body && rW) return true;
            body = a;
            rW = a.GetBoneTransform(HumanBodyBones.RightHand); rM = a.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
            rI = a.GetBoneTransform(HumanBodyBones.RightIndexProximal); rP = a.GetBoneTransform(HumanBodyBones.RightLittleProximal);
            lW = a.GetBoneTransform(HumanBodyBones.LeftHand); lM = a.GetBoneTransform(HumanBodyBones.LeftMiddleProximal);
            lU = a.GetBoneTransform(HumanBodyBones.LeftUpperArm); lF = a.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            if (!rW || !rM || !rI || !rP || !lW || !lM || !lU || !lF) { rW = null; return false; }   // no finger bones (the old hero): nothing to do
            // the sword-space point the hand origin holds under the seat the sword arrives with = the top of the handle
            gripLocal = sword.InverseTransformPoint(sword.parent.position); gripSet = true;
            // how much handle there is below that point: the mesh's far end on the pommel side (sword space -Z), in metres.
            // Capture mx3 f66: two hand-lengths below the right palm was PAST the pommel of this sword, so the left hand held air.
            float minZ = gripLocal.z;
            foreach (var mf in sword.GetComponentsInChildren<MeshFilter>())
            {
                if (!mf.sharedMesh) continue;
                var bd = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = bd.center + Vector3.Scale(bd.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    minZ = Mathf.Min(minZ, sword.InverseTransformPoint(mf.transform.TransformPoint(corner)).z);
                }
            }
            handleLen = (gripLocal.z - minZ) * Mathf.Abs(sword.lossyScale.z);
            Debug.Log($"[RoomRun] MxGrip bound to {a.name}: hand length {(rM.position - rW.position).magnitude:0.000} m, handle point {gripLocal.ToString("0.000")}, handle below it {handleLen:0.000} m");
            return true;
        }

        void LateUpdate()
        {
            LeftReach = 0f;
            if (!GreatswordCombat.MixamoChain || !Bind() || !gripSet) return;
            // ---- 1. sword in the right hand
            Vector3 w = rW.position, m = rM.position;
            var e1 = (m - w).normalized;
            var e2 = rP.position - rI.position; e2 = (e2 - e1 * Vector3.Dot(e2, e1)).normalized;
            var e3 = Vector3.Cross(e1, e2);
            var H = (e1 * HandleAxis.x + e2 * HandleAxis.y + e3 * HandleAxis.z).normalized;   // toward the pommel
            var palmR = w + (m - w) * PalmAt;
            var edge = e1 - H * Vector3.Dot(e1, H);                                            // cutting edge: toward the knuckles
            if (edge.sqrMagnitude < 1e-6f) edge = e3;
            edge.Normalize();
            var shortLocal = split ? -split.BladeLongAxis : Vector3.up;                        // sword space: the short (cutting) edge side
            var q = Mathf.Abs(Vector3.Dot(shortLocal, Vector3.up)) > 0.999f
                ? (shortLocal.y > 0f ? Quaternion.identity : Quaternion.AngleAxis(180f, Vector3.forward))
                : Quaternion.FromToRotation(shortLocal, Vector3.up);
            sword.rotation = Quaternion.LookRotation(-H, edge) * q;
            sword.position += palmR - sword.TransformPoint(gripLocal);
            // ---- 2. left palm onto the handle
            if (!LeftHandIK) return;
            float handLen = (m - w).magnitude;
            // two hand-lengths below the right palm, but never past the pommel (the palm centre stays a third of a hand inside it)
            float gap = Mathf.Max(0.6f * handLen, Mathf.Min(PalmGap * handLen, handleLen - 0.35f * handLen));
            var target = palmR + H * gap;
            var palmL = lW.position + (lM.position - lW.position) * PalmAt;
            var move = target - palmL;
            LeftReach = move.magnitude;
            float k = 1f - Mathf.InverseLerp(handLen * 2.5f, handLen * 4.5f, LeftReach);       // far away = the clip let go: leave it
            if (k <= 0.001f) return;
            var keep = lW.rotation;
            TwoBone(lU, lF, lW, lW.position + move * k);
            lW.rotation = keep;
        }

        // analytic two-joint IK in world space (a = shoulder, b = elbow, c = wrist), keeps the elbow bend plane
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
