// More arena-edge trees for depth in 풀밭 (grass) and 민가 (village) (11-13, Kido, all fields: big trees/structures at
// the edges and in the foreground for depth). Same pipeline as EdgeFeaturesBake (snow-free see-through copies of the
// snow field's materials), own marker so the original EF_* prefabs are not rebaked. All sources are downloaded
// CC BY 4.0 models already in the project (credits in Assets/Game/RoomRun/CREDITS.txt). Placed by EdgeFeatures.cs.
using System.IO;
using UnityEditor;
using UnityEngine;

namespace RoomRun
{
    [InitializeOnLoad]
    static class EdgeDepthBake
    {
        const int Version = 6;   // v6: + VH_BareTreeA/B (민가 side belt); v5: + EF_HanokWall (대전 앞 court walls, 11-14); v2: KCISA village houses (dropped); v3: 폐촌 thatched houses AV_Choga_A/B/C for 민가; v4: + the collapsed one
        const string OutDir = "Assets/Game/RoomRun/Resources/EdgeFeatures";
        const string Trees = "Assets/Game/SnowPlague/ThirdParty/Trees/Prefabs/", LM = "Assets/Game/SnowPlague/ThirdParty/Landmarks/";

        static EdgeDepthBake()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                var marker = OutDir + "/baked_depth_v" + Version + ".txt";
                if (File.Exists(marker)) return;
                try { Bake(); File.WriteAllText(marker, "EdgeDepthBake v" + Version); AssetDatabase.ImportAsset(marker); }
                catch (System.Exception e) { Debug.LogError("[EdgeFeatures] depth bake failed: " + e); }
            };
        }

        static void BareTree(string name, string file, float scale, bool a)
        {
            const string Dir = "Assets/Game/AbandonedVillage/ThirdParty/PolyHaven/bare_trees/", Mats = "Assets/Game/AbandonedVillage/Materials/";
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(Dir + file);
            var bark = AssetDatabase.LoadAssetAtPath<Material>(Mats + "AV_BareBark.mat");
            var trunkB = AssetDatabase.LoadAssetAtPath<Material>(Mats + "AV_BareTrunkB.mat");
            var twig = AssetDatabase.LoadAssetAtPath<Material>(Mats + "AV_BareTwig.mat");
            if (!src || !bark || !trunkB || !twig) { Debug.LogWarning("[EdgeFeatures] bare tree sources missing for " + name); return; }
            var root = new GameObject(name);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(src);
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            inst.transform.SetParent(root.transform, false);
            inst.transform.localScale *= scale;
            foreach (var c in inst.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            var b = new Bounds(); bool any = false;
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
            {
                var ms = r.sharedMaterials;
                for (int k = 0; k < ms.Length; k++) ms[k] = a ? bark : (ms[k] && ms[k].name.Contains("branch")) ? twig : trunkB;
                r.sharedMaterials = ms; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (any) b.Encapsulate(r.bounds); else { b = r.bounds; any = true; }
            }
            inst.transform.localPosition -= new Vector3(b.center.x, b.min.y, b.center.z);
            PrefabUtility.SaveAsPrefabAsset(root, OutDir + "/" + name + ".prefab");
            Object.DestroyImmediate(root);
            Debug.Log($"[EdgeFeatures] {name}: {b.size.x:0.0} x {b.size.y:0.0} x {b.size.z:0.0} m from {file}");
        }

        static void Bake()
        {
            Directory.CreateDirectory(OutDir);
            // tall conifers (pre-scaled game heights: pine 12 m, fir 11.5 m, dead conifer 10 m)
            EdgeFeaturesBake.Make("EF_TallPine", Trees + "TR_Pine.prefab", 0f, Vector3.back, null);
            EdgeFeaturesBake.Make("EF_Fir", Trees + "TR_Fir.prefab", 0f, Vector3.back, null);
            EdgeFeaturesBake.Make("EF_DeadConiferA", Trees + "TR_DeadConifer_A.prefab", 0f, Vector3.back, null);
            // (v2 baked KCISA scan houses VH_ThatchA/B, VH_Sarangchae, VH_Anchae for 민가; replaced by the 폐촌 kit houses in v3)
            // v3 (Kido 11-14: "use the 폐촌 초가집 in 민가, the village houses look odd"): the abandoned village's kit houses,
            // front on +Z as the kit exports them (AVBuilder turns them 180), real size
            const string AV = "Assets/Game/AbandonedVillage/Models/";
            EdgeFeaturesBake.Make("VH_AV_ChogaA", AV + "AV_Choga_A.fbx", 0f, Vector3.forward, null);
            EdgeFeaturesBake.Make("VH_AV_ChogaB", AV + "AV_Choga_B.fbx", 0f, Vector3.forward, null);
            EdgeFeaturesBake.Make("VH_AV_ChogaC", AV + "AV_Choga_C.fbx", 0f, Vector3.forward, null);
            EdgeFeaturesBake.Make("VH_AV_ChogaCollapsed", AV + "AV_ChogaCollapsed.fbx", 0f, Vector3.forward, null);
            // v5 (Kido 11-14 on the coded 대전 앞 galleries: "quality is awful, download something"): the downloaded tiled hanok
            // wall ("Korea Traditional House_House Wall", chrishong, CC BY 4.0) rings the court instead; face on -Z, 3 m tall
            EdgeFeaturesBake.Make("EF_HanokWall", LM + "HanokWall/SKF_HanokWall.fbx", 3.0f, Vector3.back, "HanokWall");
            // v6 (Kido 11-14: the 민가 side pines must go, dry dead trees instead): the 폐촌 bare trees (Poly Haven, CC0) with
            // the 폐촌 materials and scale, as prefabs LookStudyRuins places along the village belt
            BareTree("VH_BareTreeA", "AV_BareTree_A.fbx", 0.52f * 1.5f, true);
            BareTree("VH_BareTreeB", "AV_BareTree_B.fbx", 2.3f * 1.5f, false);
            AssetDatabase.SaveAssets();
            Debug.Log("[EdgeFeatures] depth features baked into " + OutDir);
        }
    }
}
