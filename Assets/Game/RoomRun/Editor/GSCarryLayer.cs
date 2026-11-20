// 11-18 two-hand carry pose layer (Kido: "한손끌기 대신 양손파지 ... 디폴트"). Hands dragged onto the sword by IK broke
// the wrist and the torso, so the two-hand hold is taken from poses that already have it: the held guard at the end of
// the berserker swings. Adds a "CarryPose" layer (arms + fingers mask, the UpperBZ mask) under AttackFull to
// RR_Greatsword.controller with two frozen states:
//   CarryR = end of SweepB (BZ_Slash2, the blade ends on the right), CarryL = end of SweepA (BZ_Slash1, ends on the left)
// RoomRun.SplitBody drives the layer weight and the side. rr_cmd: "call RoomRun.GSCarryLayer.Add" (safe to repeat).
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace RoomRun
{
    public static class GSCarryLayer
    {
        public const string LayerName = "CarryPose";

        public static void Add() { Debug.Log("[RoomRun] " + Build()); }

        static string Build()
        {
            var guid = AssetDatabase.FindAssets("RR_Greatsword t:AnimatorController").FirstOrDefault();
            if (guid == null) return "GSCarryLayer: RR_Greatsword.controller not found";
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(AssetDatabase.GUIDToAssetPath(guid));
            var layers = ctrl.layers.ToList();
            int atk = layers.FindIndex(l => l.name == "AttackFull");
            if (atk < 0) return "GSCarryLayer: no AttackFull layer";
            Motion Clip(string state) => layers[atk].stateMachine.states.Select(s => s.state).FirstOrDefault(s => s.name == state)?.motion;
            var a = Clip("SweepA"); var b = Clip("SweepB");
            if (!a || !b) return "GSCarryLayer: SweepA / SweepB clips missing";
            var mask = layers.FirstOrDefault(l => l.name == "UpperBZ")?.avatarMask;
            if (!mask) return "GSCarryLayer: UpperBZ mask missing";

            int old = layers.FindIndex(l => l.name == LayerName);
            if (old >= 0) { ctrl.RemoveLayer(old); layers = ctrl.layers.ToList(); atk = layers.FindIndex(l => l.name == "AttackFull"); }

            var sm = new AnimatorStateMachine { name = LayerName, hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(sm, ctrl);
            void State(string name, Motion m)
            {
                var st = sm.AddState(name);
                st.motion = m; st.speed = 0f; st.iKOnFeet = false;   // frozen: SplitBody picks the frame with Play(normalizedTime)
            }
            State("CarryR", b); State("CarryL", a);
            // low carry (Kido 17:49 OK): the coil at the START of a swing has the blade drawn back low on that side with
            // both hands: SweepA winds up on the right, SweepB on the left (SplitBody.CarryLow picks the frame)
            // 17:56: those wind-up frames put the hands at shoulder height, so the low carry is its own pose now, keyed in
            // Blender with the berserker rig solver (gs_berserker_anim.py carry_pose -> GSHero/Art/GS_CarryPoses.fbx):
            // both hands on the hilt by the hip, blade slanting back and down, tip off the floor
            var poses = AssetDatabase.LoadAllAssetsAtPath("Assets/Game/GSHero/Art/GS_CarryPoses.fbx").OfType<AnimationClip>().ToList();
            var lowR = poses.FirstOrDefault(c => c.name == "BZ_CarryR"); var lowL = poses.FirstOrDefault(c => c.name == "BZ_CarryL");
            State("CarryLowR", lowR ? lowR : a); State("CarryLowL", lowL ? lowL : b);
            string lowNote = lowR && lowL ? "low poses ok" : "low poses MISSING (wind-up frames used)";
            // 11-19 combo opener BZ_SlashLR (L->R lunge, right leg driven back): state "SweepLR" on AttackFull
            var lr = poses.FirstOrDefault(c => c.name == "BZ_SlashLR");
            var atkSm = layers[atk].stateMachine;
            var lrState = atkSm.states.Select(s => s.state).FirstOrDefault(s => s.name == "SweepLR");
            if (lr)
            {
                if (lrState == null) lrState = atkSm.AddState("SweepLR");
                lrState.motion = lr; lrState.iKOnFeet = true; lrState.speedParameterActive = true; lrState.speedParameter = "AtkSpeed";
            }
            lowNote += lr ? "; SweepLR ok" : "; SweepLR clip MISSING";
            // 11-19 hit 2 re-keyed like the reference (raised wind-up, R->L lunge): state "SweepRL"
            var rlClip = poses.FirstOrDefault(c => c.name == "BZ_SlashRL");
            var rlState = atkSm.states.Select(s => s.state).FirstOrDefault(s => s.name == "SweepRL");
            if (rlClip)
            {
                if (rlState == null) rlState = atkSm.AddState("SweepRL");
                rlState.motion = rlClip; rlState.iKOnFeet = true; rlState.speedParameterActive = true; rlState.speedParameter = "AtkSpeed";
            }
            lowNote += rlClip ? "; SweepRL ok" : "; SweepRL clip MISSING";
            // 11-19 hit 3 (360 spin) re-keyed with follow-through (overshoot, hang, eased return): existing state "SweepC"
            var spinClip = poses.FirstOrDefault(c => c.name == "BZ_Slash3");
            var spinState = atkSm.states.Select(s => s.state).FirstOrDefault(s => s.name == "SweepC");
            if (spinClip && spinState != null) spinState.motion = spinClip;
            lowNote += spinClip && spinState != null ? "; SweepC follow-through ok" : "; SweepC follow-through MISSING";
            // 11-19 full-body standing stance (legs wide, chest bent forward, arms and blade drawn back): layer "StanceFull",
            // no mask, under CarryPose; SplitBody raises it only while standing still
            var stR = poses.FirstOrDefault(c => c.name == "BZ_StanceR"); var stL = poses.FirstOrDefault(c => c.name == "BZ_StanceL");
            int oldSt = ctrl.layers.ToList().FindIndex(l => l.name == "StanceFull");
            if (oldSt >= 0) { ctrl.RemoveLayer(oldSt); layers = ctrl.layers.ToList(); atk = layers.FindIndex(l => l.name == "AttackFull"); }
            if (stR && stL)
            {
                var ssm = new AnimatorStateMachine { name = "StanceFull", hideFlags = HideFlags.HideInHierarchy };
                AssetDatabase.AddObjectToAsset(ssm, ctrl);
                foreach (var (n, m) in new[] { ("StanceR", stR), ("StanceL", stL) }) { var st = ssm.AddState(n); st.motion = m; st.speed = 0f; st.iKOnFeet = true; }
                layers.Insert(atk, new AnimatorControllerLayer { name = "StanceFull", defaultWeight = 0f, stateMachine = ssm, blendingMode = AnimatorLayerBlendingMode.Override });
                atk++;
            }
            lowNote += stR && stL ? "; StanceFull ok" : "; stance clips MISSING";
            // "CarryMix": the Mixamo great-sword set's own two-hand arms (idle + run 4 ways), blended by the same MoveX /
            // MoveZ the legs use, so the hold moves with the run instead of being a frozen pose (SplitBody.CarryMix)
            AnimationClip Mix(string dir, string file) => AssetDatabase.LoadAllAssetsAtPath($"Assets/Game/LocoSplit/Art/{dir}/{file}.fbx").OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
            var idle = Mix("Mixamo", "GS_Idle"); var rf = Mix("MixamoRR", "GS_RunFwd"); var rb = Mix("MixamoRR", "GS_RunBack");
            var rl = Mix("MixamoRR", "GS_RunStrafeL"); var rr = Mix("MixamoRR", "GS_RunStrafeR");
            string mixNote = "no CarryMix (Mixamo clips missing)";
            if (idle && rf && rb && rl && rr)
            {
                var tree = new BlendTree { name = "CarryMixTree", blendType = BlendTreeType.FreeformDirectional2D, blendParameter = "MoveX", blendParameterY = "MoveZ", useAutomaticThresholds = false, hideFlags = HideFlags.HideInHierarchy };
                AssetDatabase.AddObjectToAsset(tree, ctrl);
                tree.AddChild(idle, new Vector2(0, 0)); tree.AddChild(rf, new Vector2(0, 1)); tree.AddChild(rb, new Vector2(0, -1));
                tree.AddChild(rl, new Vector2(-1, 0)); tree.AddChild(rr, new Vector2(1, 0));
                var ms = sm.AddState("CarryMix"); ms.motion = tree; ms.iKOnFeet = false;
                ms.speedParameterActive = true; ms.speedParameter = "LocoSpeed";
                mixNote = "CarryMix ok";
            }
            var layer = new AnimatorControllerLayer { name = LayerName, defaultWeight = 0f, avatarMask = mask, stateMachine = sm, blendingMode = AnimatorLayerBlendingMode.Override };
            layers.Insert(atk, layer);   // under AttackFull: the swings still own the whole body
            ctrl.layers = layers.ToArray();
            EditorUtility.SetDirty(ctrl);
            AssetDatabase.SaveAssets();
            return $"GSCarryLayer: added at index {atk} (CarryR = {b.name}, CarryL = {a.name}; {lowNote}; {mixNote}); layers " + string.Join(", ", ctrl.layers.Select(l => l.name));
        }
    }
}
