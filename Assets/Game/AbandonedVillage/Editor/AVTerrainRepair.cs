// 11-10: AV_TerrainData.asset is saved binary, but the repo's text rule for *.asset (eol=lf) stripped every CR LF byte pair
// when it was committed, so Unity can no longer load it and the abandoned village had no ground at all ("폐촌 너무 어두워").
// .gitattributes now marks *TerrainData*.asset binary. This rebuilds the same heights and splat weights as AVBuilder's
// BuildTerrain (it calls AVBuilder.TerrainHeight / SplatAt, so the two cannot drift apart) and writes them over the broken file, keeping its .meta, so the scene,
// the Terrain and its collider keep pointing at it. Menu: Tools/Dev/Abandoned Village/Repair Terrain Data.
using System.IO;
using UnityEditor;
using UnityEngine;

public static class AVTerrainRepair
{
    [MenuItem("Tools/Dev/Abandoned Village/Repair Terrain Data")]
    public static void Repair()
    {
        string root = AVBuilder.Root, dataPath = root + "/AV_TerrainData.asset", tmpPath = root + "/AV_TerrainData_rebuild.asset";
        const float size = AVBuilder.TerrainSize, hRange = AVBuilder.TerrainHRange, baseY = AVBuilder.TerrainBaseY;
        var layers = new TerrainLayer[5];
        string[] names = { "AV_L_Mud", "AV_L_Tracks", "AV_L_Leaves", "AV_L_Stony", "AV_L_Paving" };
        for (int k = 0; k < names.Length; k++)
        {
            layers[k] = AssetDatabase.LoadAssetAtPath<TerrainLayer>($"{root}/Materials/{names[k]}.terrainlayer");
            if (!layers[k]) { Debug.LogError("[AV] repair: missing terrain layer " + names[k]); return; }
        }

        AssetDatabase.DeleteAsset(tmpPath);
        var td = new TerrainData { heightmapResolution = 1025, alphamapResolution = 1024 };
        td.size = new Vector3(size, hRange, size);
        AssetDatabase.CreateAsset(td, tmpPath);
        td.terrainLayers = layers;

        int hr = td.heightmapResolution;
        var h = new float[hr, hr];
        for (int j = 0; j < hr; j++)
            for (int i = 0; i < hr; i++)
            {
                float x = -size / 2 + size * i / (hr - 1), z = -size / 2 + size * j / (hr - 1);
                float y = AVBuilder.TerrainHeight(x, z);
                h[j, i] = Mathf.Clamp01((y - baseY) / hRange);
            }
        td.SetHeights(0, 0, h);

        int ar = td.alphamapResolution;
        var a = new float[ar, ar, layers.Length];
        var wk = new float[layers.Length];
        for (int j = 0; j < ar; j++)
            for (int i = 0; i < ar; i++)
            {
                float x = -size / 2 + size * (i + 0.5f) / ar, z = -size / 2 + size * (j + 0.5f) / ar;
                AVBuilder.SplatAt(x, z, wk);
                for (int k = 0; k < wk.Length; k++) a[j, i, k] = wk[k];
            }
        td.SetAlphamaps(0, 0, a);
        EditorUtility.SetDirty(td);
        AssetDatabase.SaveAssets();

        string proj = Directory.GetParent(Application.dataPath).FullName;
        File.Copy(Path.Combine(proj, tmpPath), Path.Combine(proj, dataPath), true);
        AssetDatabase.DeleteAsset(tmpPath);
        AssetDatabase.ImportAsset(dataPath, ImportAssetOptions.ForceUpdate);
        var check = AssetDatabase.LoadAssetAtPath<TerrainData>(dataPath);
        Debug.Log($"[AV] terrain data repaired: {(check ? $"{check.heightmapResolution} heights, {check.terrainLayers.Length} layers" : "STILL BROKEN")}");
    }
}
