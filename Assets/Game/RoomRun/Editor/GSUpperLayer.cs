// 11-19 (Kido: legs keep WASD while the greatsword swings): adds the synced layer "AttackUpper" to RR_Greatsword.controller,
// synced to AttackFull (same states and timing, AttackFull's motions as overrides, timing not shared) with the mask
// BZ_UpperSwingMask (head, arms, fingers, hand IK; no root / body / legs / foot IK). GreatswordCombat drives the weights
// (AttackFull x (1 - legMix), AttackUpper x 1) and SwingUpper puts the clip's chest turn back on the spine.
// rr_cmd: "call RoomRun.GSUpperLayer.Add" (safe to repeat). Run it again after `rrplayer` (it rebuilds the controller)
// or after AttackFull states / motions change (the synced layer keeps its own copy of the motions).
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace RoomRun
{
    public static class GSUpperLayer
    {
        public const string LayerName = "AttackUpper";
        const string MaskPath = "Assets/Game/LocoSplit/Generated/BZ_UpperSwingMask.mask";

        public static void Add() { Debug.Log("[RoomRun] " + Build()); }

        static AvatarMask Mask()
        {
            var m = AssetDatabase.LoadAssetAtPath<AvatarMask>(MaskPath);
            bool fresh = !m;
            if (fresh) m = new AvatarMask();
            for (var p = AvatarMaskBodyPart.Root; p < AvatarMaskBodyPart.LastBodyPart; p++)
            {
                bool on = p == AvatarMaskBodyPart.Head || p == AvatarMaskBodyPart.LeftArm || p == AvatarMaskBodyPart.RightArm
                    || p == AvatarMaskBodyPart.LeftFingers || p == AvatarMaskBodyPart.RightFingers
                    || p == AvatarMaskBodyPart.LeftHandIK || p == AvatarMaskBodyPart.RightHandIK;
                m.SetHumanoidBodyPartActive(p, on);
            }
            if (fresh) AssetDatabase.CreateAsset(m, MaskPath); else EditorUtility.SetDirty(m);
            return m;
        }

        static string Build()
        {
            var guid = AssetDatabase.FindAssets("RR_Greatsword t:AnimatorController").FirstOrDefault();
            if (guid == null) return "GSUpperLayer: RR_Greatsword.controller not found";
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(AssetDatabase.GUIDToAssetPath(guid));
            var layers = ctrl.layers.ToList();
            int old = layers.FindIndex(l => l.name == LayerName);
            if (old >= 0) { ctrl.RemoveLayer(old); layers = ctrl.layers.ToList(); }
            int atk = layers.FindIndex(l => l.name == "AttackFull");
            if (atk < 0) return "GSUpperLayer: no AttackFull layer";
            var mask = Mask();
            var up = new AnimatorControllerLayer
            {
                name = LayerName, defaultWeight = 0f, avatarMask = mask, blendingMode = AnimatorLayerBlendingMode.Override,
                syncedLayerIndex = atk, syncedLayerAffectsTiming = false,
            };
            int n = 0;
            foreach (var cs in layers[atk].stateMachine.states)
                if (cs.state.motion) { up.SetOverrideMotion(cs.state, cs.state.motion); n++; }
            layers.Add(up);   // above AttackFull: the arms / head always take the swing
            ctrl.layers = layers.ToArray();
            EditorUtility.SetDirty(ctrl);
            AssetDatabase.SaveAssets();
            return $"GSUpperLayer: added at index {layers.Count - 1}, synced to AttackFull ({atk}), {n} motions; layers " + string.Join(", ", ctrl.layers.Select(l => l.name));
        }
    }
}
