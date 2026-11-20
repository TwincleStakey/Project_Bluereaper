// 11-19 대전 앞 volume pilot: the central dais is built from a downloaded scan, not a coded mesh (Kido's standing rule).
// Source: Quixel Megascans "Japanese Shrine Stone Floor" (udyqbjkfa, Fab, free, Standard License), a 2.14 x 2.14 x 0.18 m
// dressed-stone block with kerb stones round its edge (Assets/Game/RoomRun/ThirdParty/PalaceStage/Dais: mid FBX, BaseColor
// and Normal cut to 1K, normal green flipped to GL). Baked once into RoomRun/Resources/PalaceStage/PS_DaisSlab.prefab
// (base on y = 0, centred, flat side up, no collider, URP Lit, rough); EnvRecompose.Volume tiles it into a two-tier 월대.
// Bakes by itself when the prefab is missing; rr_queue "call RoomRun.PalaceDaisBake.Bake" re-bakes.
using System.IO;
using UnityEditor;
using UnityEngine;

namespace RoomRun
{
    [InitializeOnLoad]
    public static class PalaceDaisBake
    {
        const string Src = "Assets/Game/RoomRun/ThirdParty/PalaceStage/Dais/";
        const string Out = "Assets/Game/RoomRun/Resources/PalaceStage/PS_DaisSlab.prefab";
        const string MatPath = "Assets/Game/RoomRun/Resources/PalaceStage/PS_DaisSlab_M.mat";

        static PalaceDaisBake() { EditorApplication.update += Tick; }

        static void Tick()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            EditorApplication.update -= Tick;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(Out)) return;
            try { Bake(); } catch (System.Exception e) { Debug.LogError("[PalaceDaisBake] failed: " + e); }
        }

        public static void Bake()
        {
            var nrmPath = Src + "PS_DaisSlab_Normal.jpg";
            if (AssetImporter.GetAtPath(nrmPath) is TextureImporter ti && ti.textureType != TextureImporterType.NormalMap)
            { ti.textureType = TextureImporterType.NormalMap; ti.maxTextureSize = 1024; ti.SaveAndReimport(); }
            if (AssetImporter.GetAtPath(Src + "PS_DaisSlab_BaseColor.jpg") is TextureImporter tb && tb.maxTextureSize != 1024)
            { tb.maxTextureSize = 1024; tb.SaveAndReimport(); }

            var m = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
            if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, MatPath); }
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Src + "PS_DaisSlab_BaseColor.jpg"));
            m.SetColor("_BaseColor", new Color(1.12f, 1.1f, 1.06f));   // the scan is a dark mossy grey; under the palace grade it read darker than the floor
            var n = AssetDatabase.LoadAssetAtPath<Texture2D>(nrmPath);
            if (n) { m.SetTexture("_BumpMap", n); m.EnableKeyword("_NORMALMAP"); }
            m.SetFloat("_Smoothness", 0.06f); m.SetFloat("_Metallic", 0f);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);

            var src = AssetDatabase.LoadAssetAtPath<GameObject>(Src + "PS_DaisSlab.fbx");
            if (!src) { Debug.LogWarning("[PalaceDaisBake] missing " + Src + "PS_DaisSlab.fbx"); return; }
            var root = new GameObject("PS_DaisSlab");
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(src);
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            inst.transform.SetParent(root.transform, false);
            foreach (var c in inst.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
            {
                var ms = r.sharedMaterials; for (int i = 0; i < ms.Length; i++) ms[i] = m; r.sharedMaterials = ms;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
            var b = Bounds(root);
            // flat side up: the thinnest axis must be y
            if (b.size.z < b.size.y && b.size.z < b.size.x) { inst.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f) * inst.transform.localRotation; b = Bounds(root); }
            else if (b.size.x < b.size.y && b.size.x < b.size.z) { inst.transform.localRotation = Quaternion.Euler(0f, 0f, 90f) * inst.transform.localRotation; b = Bounds(root); }
            inst.transform.localPosition -= new Vector3(b.center.x, b.min.y, b.center.z);
            b = Bounds(root);
            Directory.CreateDirectory(Path.GetDirectoryName(Out));
            PrefabUtility.SaveAsPrefabAsset(root, Out);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            Debug.Log($"[PalaceDaisBake] PS_DaisSlab {b.size.x:0.00} x {b.size.y:0.00} x {b.size.z:0.00} m -> {Out}");
        }

        static Bounds Bounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>(true);
            var b = rs.Length > 0 ? rs[0].bounds : new Bounds();
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }
    }
}
