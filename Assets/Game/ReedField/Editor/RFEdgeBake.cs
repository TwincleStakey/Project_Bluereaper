// Edge pieces for the reed field (11-13, Kido: "the pine in the reeds is round at the bottom and awkward, does not fit
// the reeds" + Hades 2 ref: very tall dark shapes along the camera-side edge only). Baked once into
// RoomRun/Resources/EdgeFeatures (EdgeFeatures.cs places them when the field is entered), from models already in the
// project: the 폐촌 gnarled dead trees (own models, AV_Bark), and two Quixel Megascans scans (free Fab): a Japanese stone
// pillar and a dead pine trunk (credits in Assets/Game/RoomRun/CREDITS.txt). Colliders stripped, base on y = 0.
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Reeds.EditorTools
{
    [InitializeOnLoad]
    static class RFEdgeBake
    {
        const int Version = 3;   // v2: the scans came in at their real size (0.9 m pillar, 1.3 m trunk): baked tall
        const string OutDir = "Assets/Game/RoomRun/Resources/EdgeFeatures", MatDir = "Assets/Game/ReedField/Materials/Edge";
        const string MS = "Assets/Game/SnowPlague/ThirdParty/Megascans/";

        static RFEdgeBake()
        {
            // runs once the editor is idle in edit mode (other threads often hold Play; a one-shot delayCall then never baked)
            EditorApplication.update += Tick;
        }

        static void Tick()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            EditorApplication.update -= Tick;
            EnsureShaftMaterial();
            var marker = OutDir + "/baked_reeds_v" + Version + ".txt";
            if (File.Exists(marker)) return;
            try { if (!Bake()) return; File.WriteAllText(marker, "RFEdgeBake v" + Version); AssetDatabase.ImportAsset(marker); }
            catch (System.Exception e) { Debug.LogError("[RFEdgeBake] failed: " + e); }
        }

        static bool Bake()
        {
            bool ok = true;
            Directory.CreateDirectory(OutDir); Directory.CreateDirectory(MatDir);
            const string AV = "Assets/Game/AbandonedVillage/Models/";
            ok &= Make("EF_RF_DeadTreeA", AV + "AV_DeadTree_A.fbx", 0f, null);
            ok &= Make("EF_RF_DeadTreeB", AV + "AV_DeadTree_B.fbx", 0f, null);
            ok &= Make("EF_RF_DeadTreeC", AV + "AV_DeadTree_C.fbx", 0f, null);
            ok &= Make("EF_RF_StonePillar", MS + "japanese_stone_pillar_uddocimqx_mid/Japanese_Stone_Pillar_uddocimqx_Mid.fbx", 9f,
                ScanMat("RF_EF_StonePillar", MS + "japanese_stone_pillar_uddocimqx_mid/Japanese_Stone_Pillar_uddocimqx_Mid_2K_"));
            ok &= Make("EF_RF_DeadTrunk", MS + "dead_pine_tree_trunk_tksvbajda_mid/Dead_Pine_Tree_Trunk_tksvbajda_Mid.fbx", 11f,
                ScanMat("RF_EF_DeadTrunk", MS + "dead_pine_tree_trunk_tksvbajda_mid/Dead_Pine_Tree_Trunk_tksvbajda_Mid_2K_"));
            // v3 (11-14, Kido on the coded 폐촌 dead trees: "너가 만든 저 이상한 나무들 빼봐"): downloaded dead snags and a
            // dead conifer from the snow field's CC BY tree set (credits in SnowPlague/ThirdParty/Trees), snow taken off
            const string TR = "Assets/Game/SnowPlague/ThirdParty/Trees/Prefabs/";
            foreach (var n in new[] { "Snag_A", "Snag_B", "Snag_C", "Snag_D", "DeadConifer_B" })
                ok &= Make("EF_RF_" + n, TR + "TR_" + n + ".prefab", 0f, null, true);
            AssetDatabase.SaveAssets();
            Debug.Log("[RFEdgeBake] reed edge pieces baked into " + OutDir + (ok ? "" : " (some missing, will retry)"));
            return ok;
        }

        // the moonlight shaft material RFMoonShafts loads from Resources (so the shader is kept in builds)
        static void EnsureShaftMaterial()
        {
            const string dir = "Assets/Game/ReedField/Resources", path = dir + "/RF_LightShaft.mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(path)) return;
            var sh = Shader.Find("ReedField/LightShaft");
            if (!sh) return;
            Directory.CreateDirectory(dir);
            AssetDatabase.CreateAsset(new Material(sh) { name = "RF_LightShaft" }, path);
            AssetDatabase.SaveAssets();
        }

        // a copy of a snow-field material with every snow/frost amount at zero (outside the snow field it still frosts)
        static Material NoSnow(Material m)
        {
            if (!m) return m;
            string path = MatDir + "/RF_EF_" + m.name.Replace("SP_LM_", "").Replace("TR_", "") + ".mat";
            var dst = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!dst) { dst = new Material(m); AssetDatabase.CreateAsset(dst, path); } else dst.CopyPropertiesFromMaterial(m);
            foreach (var p in new[] { "_SnowClump", "_SnowCover", "_SnowCap", "_VertexFrost", "_SnowLift", "_GroundMelt" }) if (dst.HasProperty(p)) dst.SetFloat(p, 0f);
            if (dst.HasProperty("_SnowBias")) dst.SetFloat("_SnowBias", -1f);
            if (dst.HasProperty("_SeeThrough")) { dst.SetFloat("_SeeThrough", 1f); dst.EnableKeyword("SP_SEETHROUGH"); }
            EditorUtility.SetDirty(dst);
            return dst;
        }

        // URP Lit from a Megascans set (BaseColor + Normal), rough, no gloss
        static Material ScanMat(string name, string prefix)
        {
            var nrmPath = prefix + "Normal.jpg";
            var ti = AssetImporter.GetAtPath(nrmPath) as TextureImporter;
            if (ti && ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; ti.SaveAndReimport(); }
            string path = MatDir + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(prefix + "BaseColor.jpg"));
            m.SetColor("_BaseColor", new Color(0.62f, 0.6f, 0.57f));
            var n = AssetDatabase.LoadAssetAtPath<Texture2D>(nrmPath);
            if (n) { m.SetTexture("_BumpMap", n); m.EnableKeyword("_NORMALMAP"); }
            m.SetFloat("_Smoothness", 0.08f);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        static bool Make(string name, string srcPath, float height, Material mat, bool snowFree = false)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(srcPath);
            if (!src) { Debug.LogWarning("[RFEdgeBake] missing " + srcPath); return false; }
            var root = new GameObject(name);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(src);
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            inst.transform.SetParent(root.transform, false);
            foreach (var c in inst.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
            {
                if (mat) { var ms = r.sharedMaterials; for (int i = 0; i < ms.Length; i++) ms[i] = mat; r.sharedMaterials = ms; }
                if (snowFree) { var ms = r.sharedMaterials; for (int i = 0; i < ms.Length; i++) ms[i] = NoSnow(ms[i]); r.sharedMaterials = ms; }
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            var b = Bounds(root);
            if (height > 0 && b.size.y > 0.01f) { inst.transform.localScale *= height / b.size.y; b = Bounds(root); }
            inst.transform.localPosition -= new Vector3(b.center.x, b.min.y, b.center.z);
            b = Bounds(root);
            PrefabUtility.SaveAsPrefabAsset(root, OutDir + "/" + name + ".prefab");
            Object.DestroyImmediate(root);
            Debug.Log($"[RFEdgeBake] {name}: {b.size.x:0.0} x {b.size.y:0.0} x {b.size.z:0.0} m from {srcPath}");
            return true;
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
