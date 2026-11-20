// Post-animation pose fixes for the greatsword 착호갑사 (11-13 Kido: idle leaned too far forward, blade through the hat).
// Runs in LateUpdate after the Animator:
//  1. Upright: pitches the spine chain (Spine up to the neck) back so the stance stands taller. Full at idle, less
//     while running, off while an attack plays (AttackFull layer weight).
//  2. Hat clearance: the head + 전립 is a stack of rings in head space (radius per height, measured from the mesh by
//     LSRemote `posefix`); if the blade passes inside, the sword turns about its grip, away from the hat, by the
//     smallest angle that clears it.
using System.Collections.Generic;
using UnityEngine;

namespace LocoSplit
{
    [DefaultExecutionOrder(100)]
    public class LSPoseFix : MonoBehaviour
    {
        public Animator anim;

        [Header("Upright")]
        public float uprightIdleDeg = 14f;     // spine chain pitch-back at idle
        public float hipsIdleDeg = 10f;        // pelvis pitch-back at idle (thighs counter-rotated, legs unchanged)
        public float hipsRunDeg = 4f;
        public float uprightRunDeg = 6f;       // at full run speed (runs keep some lean)

        [Header("Hat (head frame, metres at character scale 1)")]
        public Vector2 hatCenterXZ = Vector2.zero;
        public float[] hatYs = { 0f, 0.25f, 0.3f, 0.45f, 0.7f };
        public float[] hatRs = { 0.15f, 0.2f, 0.42f, 0.25f, 0.15f };
        public float clearance = 0.05f;
        public float maxTurnDeg = 60f;

        Transform head, hand, sword, hips, lLeg, rLeg;
        readonly List<Transform> chain = new List<Transform>();
        float bladeLen = 1.1f, turn;
        Vector3 lastAxis = Vector3.up;
        int atkLayer = -1;
        static readonly int HMoveX = Animator.StringToHash("MoveX"), HMoveZ = Animator.StringToHash("MoveZ");

        void Start() { Bind(); }

        void Bind()
        {
            if (head) return;
            if (!anim) anim = GetComponentInChildren<Animator>();
            head = anim.GetBoneTransform(HumanBodyBones.Head);
            hand = anim.GetBoneTransform(HumanBodyBones.RightHand);
            var spine = anim.GetBoneTransform(HumanBodyBones.Spine);
            hips = anim.GetBoneTransform(HumanBodyBones.Hips);
            lLeg = anim.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            rLeg = anim.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            var neck = anim.GetBoneTransform(HumanBodyBones.Neck) ?? head;
            // every bone from the spine up to (not including) the neck, root first
            chain.Clear();
            for (var t = neck ? neck.parent : null; t && spine; t = t.parent)
            {
                chain.Insert(0, t);
                if (t == spine) break;
            }
            atkLayer = anim.runtimeAnimatorController ? anim.GetLayerIndex("AttackFull") : -1;
        }

        Transform FindSword()
        {
            if (!hand) return null;
            var t = hand.Find("Greatsword");
            if (!t) return null;
            // blade tip = farthest +Z extent of the sword's meshes, in sword space
            float tip = 0f;
            foreach (var mf in t.GetComponentsInChildren<MeshFilter>())
            {
                if (!mf.sharedMesh) continue;
                var b = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                    tip = Mathf.Max(tip, t.InverseTransformPoint(mf.transform.TransformPoint(c)).z * t.lossyScale.z);
                }
            }
            if (tip > 0.2f) bladeLen = tip;
            return t;
        }

        void LateUpdate()
        {
            if (!anim || !head) return;
            float atk = atkLayer >= 0 ? anim.GetLayerWeight(atkLayer) : 0f;
            float speed = Mathf.Clamp01(new Vector2(anim.GetFloat(HMoveX), anim.GetFloat(HMoveZ)).magnitude);
            ApplyHips(Mathf.Lerp(hipsIdleDeg, hipsRunDeg, speed) * (1f - atk));
            ApplyUpright(Mathf.Lerp(uprightIdleDeg, uprightRunDeg, speed) * (1f - atk));

            if (!sword) sword = FindSword();
            if (sword)
            {
                float want = (1f - atk) * NeededTurn(sword, out _);
                // turn away at once so it never clips; ease back so it doesn't snap
                turn = Mathf.MoveTowards(turn, want, (want > turn ? 720f : 150f) * Time.deltaTime);
                if (turn > 0.01f) sword.rotation = Quaternion.AngleAxis(turn, lastAxis) * sword.rotation;
            }
        }

        // pelvis back, thighs forward by the same world angle: the torso stands up, the legs (and feet) stay as animated
        public void ApplyHips(float deg)
        {
            if (Mathf.Abs(deg) < 0.01f || !hips) return;
            var right = transform.right;
            Quaternion lRot = lLeg ? lLeg.rotation : Quaternion.identity, rRot = rLeg ? rLeg.rotation : Quaternion.identity;
            hips.rotation = Quaternion.AngleAxis(-deg, right) * hips.rotation;
            if (lLeg) lLeg.rotation = lRot;
            if (rLeg) rLeg.rotation = rRot;
        }

        public void ApplyUpright(float deg)
        {
            if (Mathf.Abs(deg) < 0.01f || chain.Count == 0) return;
            var right = transform.right;
            float per = deg / chain.Count;
            foreach (var t in chain) t.rotation = Quaternion.AngleAxis(-per, right) * t.rotation;
        }

        // ---- hat ----
        Vector3 HatLocal(Vector3 world)
        {
            float k = Mathf.Max(0.0001f, transform.lossyScale.y);
            var p = Quaternion.Inverse(head.rotation) * (world - head.position) / k;
            return new Vector3(p.x - hatCenterXZ.x, p.y, p.z - hatCenterXZ.y);
        }

        // signed distance (metres) from a head-frame point to the ring stack; negative = inside
        float HatGap(Vector3 p)
        {
            int n = Mathf.Min(hatYs.Length, hatRs.Length);
            if (n == 0) return 1f;
            float vertical = Mathf.Max(hatYs[0] - p.y, p.y - hatYs[n - 1]);
            float y = Mathf.Clamp(p.y, hatYs[0], hatYs[n - 1]);
            float r = hatRs[0];
            for (int i = 1; i < n; i++)
                if (y <= hatYs[i]) { r = Mathf.Lerp(hatRs[i - 1], hatRs[i], Mathf.InverseLerp(hatYs[i - 1], hatYs[i], y)); break; }
            return Mathf.Max(new Vector2(p.x, p.z).magnitude - r, vertical);
        }

        Vector3 HatWorld
        {
            get
            {
                int n = Mathf.Min(hatYs.Length, hatRs.Length);
                float y = n > 0 ? (hatYs[0] + hatYs[n - 1]) * 0.5f : 0.2f;
                return head.position + head.rotation * new Vector3(hatCenterXZ.x, y, hatCenterXZ.y) * transform.lossyScale.y;
            }
        }

        float BladeGap(Vector3 grip, Vector3 dir)
        {
            float best = float.MaxValue;
            for (int i = 2; i <= 20; i++) best = Mathf.Min(best, HatGap(HatLocal(grip + dir * (bladeLen * i / 20f))));
            return best;
        }

        public float MinHatGap(Transform sw) => BladeGap(sw.position, sw.forward);

        // Smallest turn (degrees, about the grip) that keeps the blade clearance away from the hat.
        public float NeededTurn(Transform sw, out Vector3 axis)
        {
            Vector3 grip = sw.position, dir = sw.forward;
            axis = Vector3.Cross(HatWorld - grip, dir);   // turns the blade away from the hat
            if (axis.sqrMagnitude < 1e-6f) axis = head.right;
            axis.Normalize();
            lastAxis = axis;
            for (float a = 0f; a <= maxTurnDeg; a += 2.5f)
                if (BladeGap(grip, Quaternion.AngleAxis(a, axis) * dir) >= clearance) return a;
            return maxTurnDeg;
        }

        /// <summary>Editor previews (no Start/LateUpdate): bind, then apply the fixes once. Returns the sword turn.</summary>
        public float PreviewApply(float speed01, bool upright = true, bool hatFix = true)
        {
            Bind();
            sword = FindSword();
            if (upright) { ApplyHips(Mathf.Lerp(hipsIdleDeg, hipsRunDeg, speed01)); ApplyUpright(Mathf.Lerp(uprightIdleDeg, uprightRunDeg, speed01)); }
            if (!hatFix || !sword) return 0f;
            float a = NeededTurn(sword, out var ax);
            if (a > 0f) sword.rotation = Quaternion.AngleAxis(a, ax) * sword.rotation;
            return a;
        }

        public int ChainLength { get { Bind(); return chain.Count; } }
    }
}
