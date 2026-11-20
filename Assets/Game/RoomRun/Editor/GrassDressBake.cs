// 11-19 풀밭 dressing (EnvRecompose.Grass): copies already-downloaded models into Resources/GrassDress as prefabs with
// their existing materials, so the runtime layer can load them. Nothing new is downloaded or generated here.
//   rocks:  the valley's round rock set (Assets/Art/LookStudy/Stages/Models Rock_*, Poly Haven boulder_01 / rock set,
//           materials V_Rock_B1 / B3 / Set1 / Set2) - round, no moss
//   shrubs: Poly Haven shrub_01..03 (D4Field/Source, materials D4_shrub_0x)
//   stumps: Poly Haven tree_stump_01 / 02 (SnowPlague/ThirdParty/PolyHaven, materials D4_tree_stump_0x)
// Run: rr_queue `call RoomRun.GrassDressBake.Run`
using UnityEditor;
using UnityEngine;

namespace RoomRun
{
    public static class GrassDressBake
    {
        const string Out = "Assets/Game/RoomRun/Resources/GrassDress";
        const string LS = "Assets/Art/LookStudy/Stages/";

        public static void Run()
        {
            if (!AssetDatabase.IsValidFolder(Out)) AssetDatabase.CreateFolder("Assets/Game/RoomRun/Resources", "GrassDress");
            int n = 0;
            n += Make("GD_Boulder1", LS + "Models/Rock_boulder01.fbx", LS + "Materials/V_Rock_B1.mat");
            n += Make("GD_Boulder3", LS + "Models/Rock_boulder03.fbx", LS + "Materials/V_Rock_B3.mat");
            for (int i = 1; i <= 6; i++) n += Make($"GD_Rock{i:00}", LS + $"Models/Rock_rock{i:00}.fbx", LS + "Materials/V_Rock_Set1.mat");
            for (int i = 7; i <= 13; i++) n += Make($"GD_Small{i:00}", LS + $"Models/Rock_rock{i:00}.fbx", LS + "Materials/V_Rock_Set2.mat");
            for (int i = 1; i <= 3; i++) n += Make($"GD_Shrub{i:00}", $"Assets/Game/D4Field/Source/PH_shrub_{i:00}/shrub_{i:00}.fbx", $"Assets/Game/D4Field/Materials/D4_shrub_{i:00}.mat");
            n += Make("GD_Stump01", "Assets/Game/SnowPlague/ThirdParty/PolyHaven/tree_stump_01/tree_stump_01.fbx", "Assets/Game/D4Field/Materials/D4_tree_stump_01.mat");
            n += Make("GD_Stump02", "Assets/Game/SnowPlague/ThirdParty/PolyHaven/tree_stump_02/tree_stump_02.fbx", "Assets/Game/D4Field/Materials/D4_tree_stump_02.mat");
            AssetDatabase.SaveAssets();
            Debug.Log($"[GrassDressBake] {n} prefabs in {Out}");
        }

        static int Make(string name, string fbx, string mat)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            var m = AssetDatabase.LoadAssetAtPath<Material>(mat);
            if (!model || !m) { Debug.LogWarning($"[GrassDressBake] missing {(model ? mat : fbx)}"); return 0; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = name;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                var ms = r.sharedMaterials; for (int i = 0; i < ms.Length; i++) ms[i] = m; r.sharedMaterials = ms;
            }
            foreach (var c in go.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            var b = new Bounds(); bool any = false;
            foreach (var r in go.GetComponentsInChildren<Renderer>()) { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
            PrefabUtility.SaveAsPrefabAsset(go, $"{Out}/{name}.prefab");
            Object.DestroyImmediate(go);
            Debug.Log($"[GrassDressBake]   {name}: size {b.size.x:0.00} x {b.size.y:0.00} x {b.size.z:0.00} m");
            return 1;
        }
    }
}
