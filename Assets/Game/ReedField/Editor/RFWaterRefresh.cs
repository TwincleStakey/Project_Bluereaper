// 11-14 water pass (Kido: cut-off water edge, white clumps, too regular waves, see-through boat floor) without a full
// RFBuilder rebuild: rewrites the RF_River / RF_Water mesh assets in place (same GUIDs, the scene keeps its references),
// sets the River material's look and mesh-edge params, and makes the boat's water-mask material in Resources.
// Opens ReedField.unity additively when it is not loaded and closes it again, so another open scene is left alone.
using Reeds;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class RFWaterRefresh
{
    const string Root = "Assets/Game/ReedField";

    public static void ApplyRiverLook(Material m)
    {
        m.SetColor("_ShallowCol", new Color(0.19f, 0.145f, 0.09f));
        m.SetColor("_DeepCol", new Color(0.016f, 0.02f, 0.022f));
        m.SetColor("_FoamCol", new Color(0.5f, 0.47f, 0.42f));
        m.SetFloat("_NormalStrength", 0.75f);
        m.SetFloat("_SurfaceScale", 4.5f);
        m.SetFloat("_Reflection", 0.3f);
        m.SetFloat("_Glint", 1.0f);
        m.SetFloat("_Streaks", 0.35f);   // now only the churn around rocks
    }

    [MenuItem("Tools/ReedField/Water Refresh (11-14)")]
    public static void Run()
    {
        const string scenePath = Root + "/ReedField.unity";
        var scene = SceneManager_GetLoaded(scenePath);
        bool opened = false;
        if (!scene.IsValid() || !scene.isLoaded) { scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive); opened = true; }
        try
        {
            ReedField field = null;
            foreach (var go in scene.GetRootGameObjects()) { field = go.GetComponentInChildren<ReedField>(true); if (field) break; }
            if (!field) { Debug.LogError("[RFWaterRefresh] no ReedField in scene"); return; }
            Overwrite($"{Root}/Generated/RF_River.asset", field.BuildRiverMesh());
            Overwrite($"{Root}/Generated/RF_Water.asset", field.BuildWaterMesh());
            var river = AssetDatabase.LoadAssetAtPath<Material>($"{Root}/Materials/RF_River.mat");
            if (river) { river.SetVector("_RiverZ", field.RiverParams); ApplyRiverLook(river); EditorUtility.SetDirty(river); }
            const string maskPath = Root + "/Resources/RF_BoatWaterMask.mat";
            if (!AssetDatabase.LoadAssetAtPath<Material>(maskPath))
                AssetDatabase.CreateAsset(new Material(Shader.Find("ReedField/WaterMask")) { name = "RF_BoatWaterMask" }, maskPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[RFWaterRefresh] done: river params {field.RiverParams}");
        }
        finally
        {
            if (opened) EditorSceneManager.CloseScene(scene, true);
        }
    }

    static UnityEngine.SceneManagement.Scene SceneManager_GetLoaded(string path)
    {
        for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
        {
            var s = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
            if (s.path == path) return s;
        }
        return default;
    }

    static void Overwrite(string path, Mesh fresh)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (!existing) { AssetDatabase.CreateAsset(fresh, path); return; }
        string name = existing.name;
        EditorUtility.CopySerialized(fresh, existing);
        existing.name = name;
        EditorUtility.SetDirty(existing);
        Object.DestroyImmediate(fresh);
    }
}
