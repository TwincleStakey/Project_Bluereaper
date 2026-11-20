// 11-19 (Kido: "대검 기본 3타 칠 때 하체가 고정됨 -> 안 됨. WASD는 공격 중에도 항상 똑같이"): while the player moves during
// a greatsword swing, the swing plays on the synced layer "AttackUpper" (head + arms + hands, GSUpperLayer) and the legs keep
// the WASD locomotion from the base layer (GreatswordCombat.LegsWalkWhileSwing / legMix).
// A humanoid mask cannot split the spine from the hips (both are "Body"), so with the mask the hips AND spine come from
// the run. The berserker clips turn the body mostly at the hips, so the chest would lose most of the swing's turn.
// This puts it back: once (first idle frame) every AttackFull state is sampled with the Animator (Play + Update(0)) and the
// chest's rotation relative to the model is stored per normalized time; after the Animator each frame the spine chain is
// turned as one piece so the chest ends where the full-body clip would have it. The arms/head are clip-local to the chest,
// so the upper body and the sword match the clip exactly; nothing is invented (no pose of our own, only the clip's chest).
using System.Collections.Generic;
using UnityEngine;

namespace RoomRun
{
    [DefaultExecutionOrder(45)]   // after the Animator, before SplitBody (50: sword roll / carry), ArmStretch (150)
    public sealed class SwingUpper : MonoBehaviour
    {
        public static bool ChestFromClip = true;   // dev A/B: false = spine stays on the run (upper mask only)
        static readonly string[] States = { "SweepA", "SweepB", "SweepC", "SweepLR", "SweepRL", "Spin", "Dash" };
        const int Samples = 60;

        GreatswordCombat gs;
        Animator anim;
        int atk = -1, up = -1;
        Transform spine, chest;
        readonly Dictionary<int, Quaternion[]> table = new Dictionary<int, Quaternion[]>();
        bool built;
        public float LastFixDeg { get; private set; }   // debug: how far the chest was turned this frame

        public void Setup(GreatswordCombat owner, Animator animator, int atkLayer, int upLayer)
        {
            gs = owner; anim = animator; atk = atkLayer; up = upLayer;
            spine = anim.GetBoneTransform(HumanBodyBones.Spine);
            chest = anim.GetBoneTransform(HumanBodyBones.UpperChest);
            if (!chest) chest = anim.GetBoneTransform(HumanBodyBones.Chest);
            if (!chest) chest = spine;
            built = false; table.Clear();
        }

        void Build()
        {
            built = true;
            if (!spine || !chest || atk < 0) return;
            float wAtk = anim.GetLayerWeight(atk), wUp = up >= 0 ? anim.GetLayerWeight(up) : 0f;
            anim.SetLayerWeight(atk, 1f);
            if (up >= 0) anim.SetLayerWeight(up, 0f);
            var inv = Quaternion.Inverse(anim.transform.rotation);
            foreach (var name in States)
            {
                int h = Animator.StringToHash(name);
                if (!anim.HasState(atk, h)) continue;
                var q = new Quaternion[Samples + 1];
                for (int i = 0; i <= Samples; i++)
                {
                    anim.Play(h, atk, i / (float)Samples);
                    anim.Update(0f);
                    q[i] = inv * chest.rotation;
                }
                table[h] = q;
            }
            anim.SetLayerWeight(atk, wAtk);
            if (up >= 0) anim.SetLayerWeight(up, wUp);
            anim.Update(0f);   // back to this frame's real pose before anything else reads it
        }

        bool Lookup(int hash, float nt, out Quaternion q)
        {
            q = Quaternion.identity;
            if (!table.TryGetValue(hash, out var arr)) return false;
            float x = Mathf.Clamp01(nt) * Samples;
            int i = Mathf.Min(Samples - 1, (int)x);
            q = Quaternion.Slerp(arr[i], arr[i + 1], x - i);
            return true;
        }

        void LateUpdate()
        {
            LastFixDeg = 0f;
            if (!gs || !anim || atk < 0 || up < 0 || !anim.isInitialized) return;
            if (!built) { if (!gs.Attacking) Build(); return; }
            if (!ChestFromClip || !spine || !chest) return;
            float W = gs.AttackLayerW, m = gs.LegMix;
            if (W * m < 0.001f) return;
            var st = anim.GetCurrentAnimatorStateInfo(atk);
            if (!Lookup(st.shortNameHash, st.normalizedTime, out var q)) return;
            if (anim.IsInTransition(atk))
            {
                var nx = anim.GetNextAnimatorStateInfo(atk);
                if (Lookup(nx.shortNameHash, nx.normalizedTime, out var q2)) q = Quaternion.Slerp(q, q2, anim.GetAnimatorTransitionInfo(atk).normalizedTime);
            }
            // the chest already carries AttackFull at W*(1-m); go the rest of the way to the clip's chest at weight W
            float f = W * m / Mathf.Max(1e-4f, 1f - W * (1f - m));
            var target = Quaternion.Slerp(chest.rotation, anim.transform.rotation * q, Mathf.Clamp01(f));
            var delta = target * Quaternion.Inverse(chest.rotation);
            LastFixDeg = Quaternion.Angle(Quaternion.identity, delta);
            spine.rotation = delta * spine.rotation;
        }
    }
}
