// 저승 피안화 (red spider lily) prefab from the CC0 Sketchfab scan by ffish.asia / floraZia (Assets/Underworld/CREDITS.txt).
// The scan (233k tris) is cut to ~4k tris in Blender by ArtSource/Underworld/build_spider_lily.py ->
// Assets/Underworld/Models/SpiderLily/UW_SpiderLily.fbx + UW_SpiderLily_Albedo.png. Imports it without
// materials or colliders, and builds Resources/Hell/SpiderLily.prefab: mesh + Underworld/Lily
// material, no collider (knee-high ground dressing, never blocks movement).
using UnityEditor;
using UnityEngine;

namespace Underworld
{
    public static class UWSpiderLilyProp
    {
        const string Fbx = "Assets/Underworld/Models/SpiderLily/UW_SpiderLily.fbx";
        const string Tex = "Assets/Underworld/Models/SpiderLily/UW_SpiderLily_Albedo.png";
        const string Mat = "Assets/Underworld/Resources/Hell/SpiderLily_M0.mat";
        const string Prefab = "Assets/Underworld/Resources/Hell/SpiderLily.prefab";

        [MenuItem("Tools/Underworld/Quality/Build Spider Lily")]
        public static void Build()
        {
            var mi = (ModelImporter)AssetImporter.GetAtPath(Fbx);
            if (!mi) { Debug.LogError("UWSpiderLilyProp: missing " + Fbx); return; }
            bool dirty = false;
            if (mi.isReadable) { mi.isReadable = false; dirty = true; }
            if (mi.materialImportMode != ModelImporterMaterialImportMode.None) { mi.materialImportMode = ModelImporterMaterialImportMode.None; dirty = true; }
            if (mi.importAnimation) { mi.importAnimation = false; dirty = true; }
            if (mi.addCollider) { mi.addCollider = false; dirty = true; }
            if (dirty) mi.SaveAndReimport();
            var ti = (TextureImporter)AssetImporter.GetAtPath(Tex);
            if (ti && ti.maxTextureSize != 1024) { ti.maxTextureSize = 1024; ti.SaveAndReimport(); }

            Mesh mesh = null;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(Fbx)) if (o is Mesh m) { mesh = m; break; }
            if (!mesh) { Debug.LogError("UWSpiderLilyProp: no mesh in " + Fbx); return; }

            var mat = AssetDatabase.LoadAssetAtPath<Material>(Mat);
            if (!mat) { mat = new Material(Shader.Find("Underworld/Lily")); AssetDatabase.CreateAsset(mat, Mat); }
            mat.shader = Shader.Find("Underworld/Lily"); mat.enableInstancing = true;   // UWLilyField draws every plant instanced
            mat.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(Tex));
            EditorUtility.SetDirty(mat);

            var go = new GameObject("SpiderLily");
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            PrefabUtility.SaveAsPrefabAsset(go, Prefab);
            Object.DestroyImmediate(go);
            AssetDatabase.SaveAssets();
            Debug.Log($"UWSpiderLilyProp: {Prefab} v{mesh.vertexCount} t{mesh.GetIndexCount(0) / 3} bounds {mesh.bounds}");
        }
    }
}
