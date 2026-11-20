// Whip test body motion (11-18): the lower body / torso of each lash comes from a real clip (Kido via the greatsword
// thread: no code-made spine/wrist poses), the code only picks the clip time (timing table from the reference) and the
// blend weight. The clip is evaluated on a hidden, stripped copy of the hero's skeleton through a manual PlayableGraph,
// then the chosen bones' local rotations (and the hips position) are copied onto the real skeleton after the Animator.
// Nothing in the shared RR_Greatsword controller is touched.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace RoomRun
{
    public sealed class WhipBody
    {
        GameObject proxy;
        Animator pAnim;
        PlayableGraph graph;
        AnimationClipPlayable play;
        AnimationClip current;
        readonly Animator real;

        static readonly HumanBodyBones[] Legs =
        {
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes,
            HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, HumanBodyBones.RightToes,
        };
        static readonly HumanBodyBones[] Torso = { HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest };
        static readonly HumanBodyBones[] Head = { HumanBodyBones.Neck, HumanBodyBones.Head };
        static readonly HumanBodyBones[] LeftArm = { HumanBodyBones.LeftShoulder, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand };
        static readonly HumanBodyBones[] RightArm = { HumanBodyBones.RightShoulder, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand };

        readonly Dictionary<HumanBodyBones, (Transform r, Transform p)> map = new Dictionary<HumanBodyBones, (Transform, Transform)>();

        public bool Ready => proxy && pAnim;

        public WhipBody(Animator source)
        {
            real = source;
            // instantiate under an inactive holder so none of the copy's scripts wake up, strip it to bones + Animator
            var holder = new GameObject("WhipBodyProxyHolder");
            holder.SetActive(false);
            holder.hideFlags = HideFlags.HideInHierarchy;
            proxy = Object.Instantiate(source.gameObject, holder.transform);
            proxy.name = "WhipBodyProxy";
            foreach (var mb in proxy.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(mb);
            foreach (var r in proxy.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            foreach (var c in proxy.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            pAnim = proxy.GetComponent<Animator>();
            if (!pAnim) { Object.Destroy(holder); proxy = null; return; }
            pAnim.runtimeAnimatorController = null;
            pAnim.applyRootMotion = false;
            pAnim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            proxy.transform.SetParent(null, false);
            proxy.transform.position = new Vector3(0f, -3000f, 0f);
            Object.Destroy(holder);
            proxy.SetActive(true);

            void Add(HumanBodyBones b)
            {
                var r = real.GetBoneTransform(b); var p = pAnim.GetBoneTransform(b);
                if (r && p) map[b] = (r, p);
            }
            Add(HumanBodyBones.Hips);
            foreach (var b in Legs) Add(b);
            foreach (var b in Torso) Add(b);
            foreach (var b in Head) Add(b);
            foreach (var b in LeftArm) Add(b);
            foreach (var b in RightArm) Add(b);

            graph = PlayableGraph.Create("WhipBody");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        }

        void Use(AnimationClip clip)
        {
            if (current == clip) return;
            if (play.IsValid()) play.Destroy();
            var output = graph.GetOutputCount() > 0 ? (AnimationPlayableOutput)graph.GetOutput(0) : AnimationPlayableOutput.Create(graph, "whip", pAnim);
            play = AnimationClipPlayable.Create(graph, clip);
            play.SetApplyFootIK(true);
            play.Pause();
            output.SetSourcePlayable(play);
            current = clip;
        }

        /// Pose the proxy at clipTime (seconds) and return it (bones readable right after).
        public void Sample(AnimationClip clip, float clipTime)
        {
            if (!Ready || !clip) return;
            Use(clip);
            play.SetTime(Mathf.Clamp(clipTime, 0f, clip.length));
            graph.Evaluate(0f);
        }

        public Transform ProxyBone(HumanBodyBones b) => map.TryGetValue(b, out var m) ? m.p : null;

        /// Copy the sampled pose onto the real skeleton: legs + hips at wLegs, spine at wTorso, head at wHead,
        /// left arm at wLeft, right arm at wRight.
        public void Apply(float wLegs, float wTorso, float wHead, float wLeft, float wRight)
        {
            if (!Ready) return;
            if (wLegs > 0f && map.TryGetValue(HumanBodyBones.Hips, out var h))
            {
                h.r.localRotation = Quaternion.Slerp(h.r.localRotation, h.p.localRotation, wLegs);
                h.r.localPosition = Vector3.Lerp(h.r.localPosition, h.p.localPosition, wLegs);
            }
            Copy(Legs, wLegs); Copy(Torso, wTorso); Copy(Head, wHead); Copy(LeftArm, wLeft); Copy(RightArm, wRight);
        }

        void Copy(HumanBodyBones[] set, float w)
        {
            if (w <= 0f) return;
            foreach (var b in set)
                if (map.TryGetValue(b, out var m)) m.r.localRotation = Quaternion.Slerp(m.r.localRotation, m.p.localRotation, w);
        }

        /// Timing probe: right-hand height above the hips over the clip, in steps. Used to find the wind-up top and the
        /// impact frame automatically.
        public float[] HandHeights(AnimationClip clip, int steps, HumanBodyBones hand = HumanBodyBones.RightHand)
        {
            var res = new float[steps + 1];
            if (!Ready || !clip) return res;
            var hb = pAnim.GetBoneTransform(hand); var hips = pAnim.GetBoneTransform(HumanBodyBones.Hips);
            for (int i = 0; i <= steps; i++)
            {
                Sample(clip, clip.length * i / steps);
                res[i] = hb && hips ? hb.position.y - hips.position.y : 0f;
            }
            return res;
        }

        /// Timing probe for throw-like clips: how far the hand is in front of the hips (proxy forward), per step.
        public float[] HandForward(AnimationClip clip, int steps, HumanBodyBones hand = HumanBodyBones.RightHand)
        {
            var res = new float[steps + 1];
            if (!Ready || !clip) return res;
            var hb = pAnim.GetBoneTransform(hand); var hips = pAnim.GetBoneTransform(HumanBodyBones.Hips);
            for (int i = 0; i <= steps; i++)
            {
                Sample(clip, clip.length * i / steps);
                res[i] = hb && hips ? Vector3.Dot(hb.position - hips.position, proxy.transform.forward) : 0f;
            }
            return res;
        }

        public void Dispose()
        {
            if (graph.IsValid()) graph.Destroy();
            if (proxy) Object.Destroy(proxy);
        }

        // ---- clips (editor only for the test; a build needs them referenced from RoomRunConfig) ----
        public static AnimationClip LoadClip(string fbxPath, string clipName)
        {
#if UNITY_EDITOR
            foreach (var o in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(fbxPath))
                if (o is AnimationClip c && !c.name.StartsWith("__preview__") && (string.IsNullOrEmpty(clipName) || c.name == clipName)) return c;
#endif
            return null;
        }
    }
}
