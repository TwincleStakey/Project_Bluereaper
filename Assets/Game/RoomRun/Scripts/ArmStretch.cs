// 11-16 single big hit (Kido 17:27 KST: "팔좀 늘려도되 롤처럼"): stylised arm stretch during the berserker's hit 1, so the
// flat sweep reaches far past what the short chibi arms allow. After every other pose pass (SplitBody 50, LSPoseFix 100),
// both hands are pushed out by the same horizontal offset (chest -> right hand direction): the elbows take half, the hands
// the rest, so the skin stretches along upper arm and forearm (League-style) while the two-hand grip and the sword in it
// move rigidly together. Height is never changed (the blade stays flat at the keyed height). Eased in over the wind-up,
// full through the pass, back to normal length in the recovery. A humanoid Animator does not write bone positions (only
// rotations), so the rest local positions are put back at the start of every LateUpdate before the offset is applied.
using UnityEngine;

namespace RoomRun
{
    [DefaultExecutionOrder(150)]
    public sealed class ArmStretch : MonoBehaviour
    {
        public static bool On = true;
        public static float Extra = 0.55f;                     // metres added to the reach at full stretch (world)
        public static float InFrom = 3f, InTo = 7f, OutFrom = 12f, OutTo = 19f;   // BZ_Slash1 clip frames

        GreatswordCombat gs;
        Animator anim;
        Transform chest, elbowR, elbowL, handR, handL;
        Vector3 restER, restEL, restHR, restHL;
        public float Amount { get; private set; }               // 0..1 this frame (logs)
        public Vector3 Offset { get; private set; }

        public void Setup(GreatswordCombat combat, Animator animator) { gs = combat; anim = animator; chest = null; }

        static float Ease(float u) { u = Mathf.Clamp01(u); return u * u * (3f - 2f * u); }

        void LateUpdate()
        {
            Amount = 0f; Offset = Vector3.zero;
            if (!gs || !anim) return;
            if (!chest)
            {
                chest = anim.GetBoneTransform(HumanBodyBones.UpperChest) ?? anim.GetBoneTransform(HumanBodyBones.Chest);
                elbowR = anim.GetBoneTransform(HumanBodyBones.RightLowerArm); elbowL = anim.GetBoneTransform(HumanBodyBones.LeftLowerArm);
                handR = anim.GetBoneTransform(HumanBodyBones.RightHand); handL = anim.GetBoneTransform(HumanBodyBones.LeftHand);
                if (!chest || !elbowR || !elbowL || !handR || !handL) { chest = null; return; }
                restER = elbowR.localPosition; restEL = elbowL.localPosition; restHR = handR.localPosition; restHL = handL.localPosition;
            }
            elbowR.localPosition = restER; elbowL.localPosition = restEL; handR.localPosition = restHR; handL.localPosition = restHL;
            if (!On || !gs.Attacking || !gs.Berserker) return;
            if (gs.CurrentState != "SweepA") return;
            float f = gs.ClipFrame;
            float a = f < OutFrom ? Ease((f - InFrom) / (InTo - InFrom)) : 1f - Ease((f - OutFrom) / (OutTo - OutFrom));
            if (a <= 0.001f) return;
            var d = handR.position - chest.position; d.y = 0f;
            if (d.sqrMagnitude < 1e-4f) return;
            var off = d.normalized * (Extra * a * transform.lossyScale.x);
            // both elbows half way, then both hands the rest (hands are children of the elbows)
            elbowR.position += off * 0.5f; elbowL.position += off * 0.5f;
            handR.position += off * 0.5f; handL.position += off * 0.5f;
            Amount = a; Offset = off;
        }
    }
}
