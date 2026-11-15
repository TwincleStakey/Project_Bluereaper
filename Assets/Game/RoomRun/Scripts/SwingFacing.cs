// 11-15 (Kido: "공격 누르는 순간 뒤돌아서 뒤를 공격함"): the greatsword sweep clips (GS_SweepA/B/C cut from Mixamo
// GS_ComboSlash, GS_SpinAttack) wind up with the chest turned 110-135° away from the aim, cross the aim only around the hit,
// and follow through to ~140° the other way (GSHero facing_probe.txt: SweepA chest -133° -> -15° at the hit -> +143°).
// PlayerController points the visual at the cursor, so the player saw the character turn round and swing behind it.
// After the Animator has posed the body, this turns the visual back by most of the chest's twist (shoulder line), so the
// body keeps facing the cursor and only the swing itself stays. Runs before the blade trail / eye flames read the pose.
using UnityEngine;

namespace RoomRun
{
    [DefaultExecutionOrder(-100)]
    public sealed class SwingFacing : MonoBehaviour
    {
        [Range(0f, 1f)] public float cancel = 0.8f;   // share of the chest twist taken out (1 = shoulders locked on the aim)
        public float maxDeg = 150f;
        public static bool LegacyBerserker = false;   // limb fix A/B (shots token limb=0): the old 25% chest-coil correction

        public Transform visual;
        public Animator anim;
        public GreatswordCombat gs;
        public PlayerController pc;
        public float LastTwist { get; private set; }   // measured chest yaw off the aim this frame (debug / facing shots)

        void LateUpdate()
        {
            LastTwist = 0f;
            if (!visual || !anim || !gs || !gs.Attacking || (pc && pc.Rolling)) return;
            var l = anim.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            var r = anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
            if (!l || !r) return;
            var chest = Vector3.Cross(r.position - l.position, Vector3.up); chest.y = 0f;
            var fwd = visual.forward; fwd.y = 0f;
            if (chest.sqrMagnitude < 1e-6f || fwd.sqrMagnitude < 1e-6f) return;
            float twist = Vector3.SignedAngle(fwd, chest, Vector3.up);
            LastTwist = twist;
            // the berserker clips coil the chest on purpose and never turn round: no correction for them (limb fix 11-15:
            // turning the whole visual by a share of the chest coil also turned the planted feet, which read as a wrung body)
            if (gs.Berserker && !LegacyBerserker) return;
            float k = gs.Berserker ? Mathf.Min(cancel, 0.25f) : cancel;
            visual.rotation = Quaternion.AngleAxis(-Mathf.Clamp(twist * k, -maxDeg, maxDeg), Vector3.up) * visual.rotation;
        }
    }
}
