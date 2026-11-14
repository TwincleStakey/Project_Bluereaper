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
        const int Version = 4;   // v2: KCISA village houses (dropped); v3: 폐촌 thatched houses AV_Choga_A/B/C for 민가; v4: + the collapsed one
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
            AssetDatabase.SaveAssets();
            Debug.Log("[EdgeFeatures] depth features baked into " + OutDir);
        }
    }
}
