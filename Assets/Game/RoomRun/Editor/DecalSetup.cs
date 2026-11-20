// 11-20 decal test (Kido: "데칼 온오프차이 보여줘"). Editor helpers run from the remote queue:
//   call RoomRun.DecalSetup.AddFeature   - adds URP's Decal renderer feature (Screen Space) to PC_Renderer, once
//   call RoomRun.DecalSetup.Materials    - texture import settings + Resources/Decals/RRD_*.mat on RoomRun/RR_DecalOpacity
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace RoomRun
{
    public static class DecalSetup
    {
        const string RendererPath = "Assets/Settings/PC_Renderer.asset";
        const string Tex = "Assets/Game/RoomRun/ThirdParty/Decals/";
        const string Out = "Assets/Game/RoomRun/Resources/Decals/";

        public static void AddFeature()
        {
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (!data) { Debug.LogError("[Decals] no " + RendererPath); return; }
            foreach (var f in data.rendererFeatures) if (f is DecalRendererFeature) { Debug.Log("[Decals] feature already there"); return; }
            var feat = ScriptableObject.CreateInstance<DecalRendererFeature>();
            feat.name = "Decal";
            var fs = new SerializedObject(feat);
            fs.FindProperty("m_Settings.technique").enumValueIndex = 2;          // Automatic 0, DBuffer 1, Screen Space 2
            fs.FindProperty("m_Settings.maxDrawDistance").floatValue = 1000f;
            fs.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.AddObjectToAsset(feat, data);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feat, out _, out long localId);
            var so = new SerializedObject(data);
            var list = so.FindProperty("m_RendererFeatures"); var map = so.FindProperty("m_RendererFeatureMap");
            list.arraySize++; list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = feat;
            map.arraySize++; map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(data); AssetDatabase.SaveAssets();
            Debug.Log($"[Decals] added Decal feature (Screen Space) to PC_Renderer, features now {data.rendererFeatures.Count}");
        }

        public static void Materials()
        {
            var sh = Shader.Find("RoomRun/RR_DecalOpacity");
            if (!sh) { Debug.LogError("[Decals] shader RoomRun/RR_DecalOpacity not found"); return; }
            Directory.CreateDirectory(Out);
            // _Tint (material value, the jpgs stay as downloaded): the tan scans were ~2x the moonlit stone's value and turned
            // olive where only URP's ambient lights them -> darker and a little toward the stone's blue-grey
            Make(sh, "RRD_DirtPatch", Tex + "DirtPatch/Dirt_Patch_ufiqbdvcw_1K_BaseColor.jpg", Tex + "DirtPatch/Dirt_Patch_ufiqbdvcw_1K_Opacity.jpg", new Color(0.50f, 0.50f, 0.55f));   // pass 3: x0.4 read as shadow in places -> 0.5
            Make(sh, "RRD_DirtPath01", Tex + "DirtPath01/ydytafl_2K_Basecolor.jpg", Tex + "DirtPath01/ydytafl_2K_Opacity.jpg", new Color(0.48f, 0.48f, 0.53f));
            Make(sh, "RRD_MudSplash", Tex + "MudSplash/Mud_Splash_vlpgefev_1K_BaseColor.jpg", Tex + "MudSplash/Mud_Splash_vlpgefev_1K_Opacity.jpg", new Color(0.8f, 0.8f, 0.85f));
            Make(sh, "RRD_Charcoal", Tex + "Charcoal/Charcoal_Scatter_smnhxxh_1K_BaseColor.jpg", Tex + "Charcoal/Charcoal_Scatter_smnhxxh_1K_Opacity.jpg", Color.white);
            AssetDatabase.SaveAssets();
        }

        static void Make(Shader sh, string name, string baseTex, string opTex, Color tint)
        {
            Import(baseTex, true); Import(opTex, false);
            string p = Out + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (!m) { m = new Material(sh) { name = name }; AssetDatabase.CreateAsset(m, p); }
            m.shader = sh;
            m.SetTexture("Base_Map", AssetDatabase.LoadAssetAtPath<Texture2D>(baseTex));
            m.SetTexture("_OpacityMap", AssetDatabase.LoadAssetAtPath<Texture2D>(opTex));
            if (m.HasProperty("Normal_Blend")) m.SetFloat("Normal_Blend", 0f);
            if (m.HasProperty("_Tint")) m.SetColor("_Tint", tint); else Debug.LogWarning("[Decals] shader has no _Tint (graph not reimported?)");
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            Debug.Log($"[Decals] {name}: tint {tint} base {m.GetTexture("Base_Map")} opacity {m.GetTexture("_OpacityMap")}");
        }

        static void Import(string path, bool srgb)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (!ti) { Debug.LogError("[Decals] no texture " + path); return; }
            ti.sRGBTexture = srgb; ti.wrapMode = TextureWrapMode.Clamp; ti.mipmapEnabled = true;
            ti.maxTextureSize = path.Contains("2K") ? 2048 : 1024;   // the path strip is 1024 x 2048 as downloaded
            ti.textureCompression = TextureImporterCompression.Compressed;
            ti.SaveAndReimport();
        }
    }
}
