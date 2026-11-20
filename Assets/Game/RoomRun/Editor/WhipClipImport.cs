// 11-19 whip body candidates (Kido: "slave-whipping style: diagonal down from the upper right, then a backhand from behind").
// Mixamo one-hand 2-hit combos downloaded on Joe (without skin) into LocoSplit/Art/MixamoRR/WH_*.fbx; this imports them like
// WH_Throw (Humanoid, avatar copied from WH_Throw's source, in place). rr_queue: call RoomRun.WhipClipImport.Run
using UnityEditor;
using UnityEngine;

namespace RoomRun
{
    public static class WhipClipImport
    {
        const string Dir = "Assets/Game/LocoSplit/Art/MixamoRR/";
        static readonly string[] Names = { "WH_AxeCombo1", "WH_AxeCombo3", "WH_Torch03", "WH_Torch05", "WH_Backhand" };

        public static void Run()
        {
            var refMi = (ModelImporter)AssetImporter.GetAtPath(Dir + "WH_Throw.fbx");
            var refClip = refMi.clipAnimations.Length > 0 ? refMi.clipAnimations[0] : null;
            foreach (var n in Names)
            {
                var mi = (ModelImporter)AssetImporter.GetAtPath(Dir + n + ".fbx");
                if (!mi) { Debug.LogWarning("[WhipClipImport] missing " + n); continue; }
                mi.animationType = ModelImporterAnimationType.Human;
                mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;   // skinless Mixamo FBX: its own avatar (copying WH_Throw's gave no take)
                mi.importAnimation = true;
                mi.materialImportMode = ModelImporterMaterialImportMode.None;
                mi.SaveAndReimport();
                mi = (ModelImporter)AssetImporter.GetAtPath(Dir + n + ".fbx");
                var src = mi.defaultClipAnimations;
                if (src.Length == 0) { var ci = mi.importedTakeInfos; Debug.Log($"[WhipClipImport] {n}: takes={ci.Length} avatarErr? setup={mi.avatarSetup}"); }
                if (src.Length == 0) { Debug.LogWarning("[WhipClipImport] no take in " + n); continue; }
                var c = src[0];
                c.name = n;
                c.loopTime = false;
                if (refClip != null)
                {
                    c.lockRootRotation = refClip.lockRootRotation; c.keepOriginalOrientation = refClip.keepOriginalOrientation;
                    c.lockRootHeightY = refClip.lockRootHeightY; c.keepOriginalPositionY = refClip.keepOriginalPositionY;
                    c.lockRootPositionXZ = refClip.lockRootPositionXZ; c.keepOriginalPositionXZ = refClip.keepOriginalPositionXZ;
                    c.heightFromFeet = refClip.heightFromFeet;
                }
                mi.clipAnimations = new[] { c };
                mi.SaveAndReimport();
                AnimationClip clip = null;
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(Dir + n + ".fbx")) if (o is AnimationClip ac && !ac.name.StartsWith("__preview")) clip = ac;
                Debug.Log($"[WhipClipImport] {n}: human={mi.animationType} len={(clip ? clip.length : 0f):0.00}s");
            }
        }
    }
}
