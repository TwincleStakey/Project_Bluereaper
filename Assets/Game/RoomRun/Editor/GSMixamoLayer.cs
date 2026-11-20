// 11-19 greatsword restart on the suit character (Kido 17:55: "그냥 처음부터 다시해 ... 믹사모에서 받은 정장 입은 캐릭터 있어 그걸로 해").
// The hand-keyed berserker clips kept producing crossed arms and twisted wrists. This wires the Mixamo Great Sword mocap
// that is already in the project (Assets/Game/LocoSplit/Art/Mixamo*, humanoid) into RR_Greatsword.controller, next to the
// existing states (nothing is removed):
//   AttackFull: "MxLR"   = GS_SweepC      (GS_LowSlash.fbx f10-50: low sweep left -> right)
//               "MxRL"   = GS_Slash1      (down-cut from the upper right to the lower left)
//               "MxSpin" = GS_SpinAttack  (turn with a descending cut)
//   StanceFull: "MxIdle" = GS_Idle        (the pack's two-hand guard; every slash starts and ends in it)
// GreatswordCombat.MixamoChain (shots token mx=1) picks these; default off. rr_cmd: "call RoomRun.GSMixamoLayer.Add"
// (safe to repeat; run again after GSCarryLayer.Add, which rebuilds StanceFull).
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace RoomRun
{
    public static class GSMixamoLayer
    {
        const string Dir = "Assets/Game/LocoSplit/Art/";

        public static void Add() { Debug.Log("[RoomRun] " + Build()); }

        static AnimationClip Clip(string fbx, string name)
        {
            return AssetDatabase.LoadAllAssetsAtPath(Dir + fbx).OfType<AnimationClip>().FirstOrDefault(c => c.name == name);
        }

        static string Build()
        {
            var guid = AssetDatabase.FindAssets("RR_Greatsword t:AnimatorController").FirstOrDefault();
            if (guid == null) return "GSMixamoLayer: RR_Greatsword.controller not found";
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(AssetDatabase.GUIDToAssetPath(guid));
            var layers = ctrl.layers;
            var atk = layers.FirstOrDefault(l => l.name == "AttackFull");
            if (atk == null) return "GSMixamoLayer: no AttackFull layer";
            var note = new System.Text.StringBuilder("GSMixamoLayer:");
            void Atk(string state, string fbx, string clip)
            {
                var c = Clip(fbx, clip);
                if (!c) { note.Append(" " + state + " MISSING(" + clip + ")"); return; }
                var st = atk.stateMachine.states.Select(s => s.state).FirstOrDefault(s => s.name == state) ?? atk.stateMachine.AddState(state);
                st.motion = c; st.iKOnFeet = true; st.speedParameterActive = true; st.speedParameter = "AtkSpeed";
                note.Append(" " + state + "=" + clip + "(" + c.length.ToString("0.00") + "s)");
            }
            Atk("MxLR", "MixamoRR/GS_LowSlash.fbx", "GS_SweepC");
            Atk("MxRL", "Mixamo/GS_Slash1.fbx", "GS_Slash1");
            Atk("MxSpin", "MixamoRR/GS_SpinAttack.fbx", "GS_SpinAttack");
            // base layer: a second locomotion state with the pack's own idle / walks / runs (the "Locomotion" state keeps the
            // berserker idle + run with the Mixamo strafes; the UpperBZ arms layer is switched off at runtime in this mode)
            var baseSm = layers[0].stateMachine;
            var loco = baseSm.states.Select(x => x.state).FirstOrDefault(x => x.name == "MxLocomotion");
            if (loco == null)
            {
                var tree = new BlendTree { name = "MxLocomotion", blendType = BlendTreeType.FreeformDirectional2D, blendParameter = "MoveX", blendParameterY = "MoveZ", useAutomaticThresholds = false, hideFlags = HideFlags.HideInHierarchy };
                AssetDatabase.AddObjectToAsset(tree, ctrl);
                int n = 0;
                void Leg(string fbx, string clip, float x, float z) { var c = Clip(fbx, clip); if (c) { tree.AddChild(c, new Vector2(x, z)); n++; } else note.Append(" loco MISSING(" + clip + ")"); }
                const float w = 0.35f;
                Leg("Mixamo/GS_Idle.fbx", "GS_Idle", 0, 0);
                Leg("Mixamo/GS_WalkFwd.fbx", "GS_WalkFwd", 0, w); Leg("Mixamo/GS_WalkBack.fbx", "GS_WalkBack", 0, -w);
                Leg("Mixamo/GS_StrafeL.fbx", "GS_StrafeL", -w, 0); Leg("Mixamo/GS_StrafeR.fbx", "GS_StrafeR", w, 0);
                Leg("MixamoRR/GS_RunFwd.fbx", "GS_RunFwd", 0, 1); Leg("MixamoRR/GS_RunBack.fbx", "GS_RunBack", 0, -1);
                Leg("MixamoRR/GS_RunStrafeL.fbx", "GS_RunStrafeL", -1, 0); Leg("MixamoRR/GS_RunStrafeR.fbx", "GS_RunStrafeR", 1, 0);
                loco = baseSm.AddState("MxLocomotion");
                loco.motion = tree; loco.iKOnFeet = true; loco.speedParameterActive = true; loco.speedParameter = "LocoSpeed";
                note.Append(" MxLocomotion(" + n + " clips)");
            }
            else note.Append(" MxLocomotion kept");
            var stance = layers.FirstOrDefault(l => l.name == "StanceFull");
            var idle = Clip("Mixamo/GS_Idle.fbx", "GS_Idle");
            if (stance != null && idle)
            {
                var st = stance.stateMachine.states.Select(s => s.state).FirstOrDefault(s => s.name == "MxIdle") ?? stance.stateMachine.AddState("MxIdle");
                st.motion = idle; st.speed = 1f; st.iKOnFeet = true;
                note.Append(" MxIdle=GS_Idle");
            }
            else note.Append(stance == null ? " no StanceFull layer (run GSCarryLayer.Add first)" : " GS_Idle MISSING");
            EditorUtility.SetDirty(ctrl);
            AssetDatabase.SaveAssets();
            return note.ToString();
        }
    }
}
