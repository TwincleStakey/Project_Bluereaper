// 11-19 suit body test: imports the Mixamo "Joe" suit character (ThirdParty/MixamoJoe/Joe.fbx, Kido's Mixamo download)
// as a Humanoid with its textures pulled out of the FBX (2K cap, see the LFS diet rule) and URP Lit materials.
// rr_queue: call RoomRun.SuitBodySetup.Build
using System.IO;
using UnityEditor;
using UnityEngine;

namespace RoomRun
{
    public static class SuitBodySetup
    {
        const string Dir = "Assets/Game/RoomRun/ThirdParty/MixamoJoe";
        const string Fbx = Dir + "/Joe.fbx";

        public static void Build()
        {
            var mi = (ModelImporter)AssetImporter.GetAtPath(Fbx);
            if (!mi) { Debug.LogError("[SuitBody] missing " + Fbx); return; }
            if (!AssetDatabase.IsValidFolder(Dir + "/Textures")) AssetDatabase.CreateFolder(Dir, "Textures");
            mi.ExtractTextures(Dir + "/Textures");
            AssetDatabase.Refresh();
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Dir + "/Textures" }))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                var ti = (TextureImporter)AssetImporter.GetAtPath(p);
                bool normal = p.ToLower().Contains("normal");
                ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                ti.maxTextureSize = 2048;
                ti.textureCompression = TextureImporterCompression.Compressed;
                ti.SaveAndReimport();
            }
            mi = (ModelImporter)AssetImporter.GetAtPath(Fbx);
            mi.animationType = ModelImporterAnimationType.Human;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.importAnimation = false;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            mi.materialLocation = ModelImporterMaterialLocation.InPrefab;
            mi.SaveAndReimport();
            // report what came in
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
            var an = go ? go.GetComponent<Animator>() : null;
            Debug.Log($"[SuitBody] Joe imported: human={(an && an.avatar ? an.avatar.isHuman : false)}");
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                    Debug.Log($"[SuitBody] {r.name} mat={(m ? m.name : "null")} shader={(m ? m.shader.name : "-")} tex={(m && m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") ? m.GetTexture("_BaseMap").name : "none")}");
        }
    }
}
