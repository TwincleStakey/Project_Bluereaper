// Arena-edge features (11-13, Kido relaxed the arena rule: a few big trees, 장승 and a house corner may stand at the
// edges/corners of a field). Bakes ready prefabs into Resources/EdgeFeatures from already-imported downloaded assets
// (no coded geometry), which RoomRun.EdgeFeatures places at field load:
//   EF_PineA / EF_PineB   "Japanese Black Pine" by matt z chan (CC BY 4.0) - SnowPlague/ThirdParty/Trees TR_HeroPine_A/B
//   EF_Jangseung          "Korean Totem Pole- Jangseung" by ththqkf (CC BY 4.0) - SnowPlague/ThirdParty/Landmarks/Jangseung
//   EF_ThatchHouse        "KCISA-수내동가옥_초가집02" by KCISA (CC BY 4.0) - SnowPlague/ThirdParty/Landmarks/ThatchHouse2
// Materials are copies of the snow field's (SnowPlague/Vegetation, SnowPlague/Prop: plain URP lighting, so they work in
// every field) with all snow off and the soft see-through (SP_SEETHROUGH) on; the snow field's own assets are untouched.
// Runs by itself after a script reload when the prefabs are missing or Version changes (no menu item).
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RoomRun
{
    [InitializeOnLoad]
    static class EdgeFeaturesBake
    {
        const int Version = 3;
        const string OutDir = "Assets/Game/RoomRun/Resources/EdgeFeatures", MatDir = "Assets/Game/RoomRun/EdgeFeatures/Materials";
        const string Trees = "Assets/Game/SnowPlague/ThirdParty/Trees/Prefabs/", LM = "Assets/Game/SnowPlague/ThirdParty/Landmarks/",
            SPMats = "Assets/Game/SnowPlague/Materials/";

        static EdgeFeaturesBake()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                var marker = OutDir + "/baked_v" + Version + ".txt";
                if (File.Exists(marker)) return;
                try { Bake(); File.WriteAllText(marker, "EdgeFeaturesBake v" + Version); AssetDatabase.ImportAsset(marker); }
                catch (System.Exception e) { Debug.LogError("[EdgeFeatures] bake failed: " + e); }
            };
        }

        static void Bake()
        {
            Directory.CreateDirectory(OutDir); Directory.CreateDirectory(MatDir);
            // front: the model's local direction of its face; the prefab is turned so the face looks at the camera (-Z)
            Make("EF_PineA", Trees + "TR_HeroPine_A.prefab", 0f, Vector3.back, null);
            Make("EF_PineB", Trees + "TR_HeroPine_B.prefab", 0f, Vector3.back, null);
            Make("EF_Jangseung", LM + "Jangseung/SKF_Jangseung.fbx", 2.6f, Vector3.forward, "Jangseung");
            // v2: without its yard ground tiles (they made it 19.5 m wide and would lie inside the arena)
            Make("EF_ThatchHouse", LM + "ThatchHouse2/SKF_ThatchHouse2.fbx", 4.6f, Vector3.back, "ThatchHouse2", "Ground_");
            AssetDatabase.SaveAssets();
            Debug.Log("[EdgeFeatures] baked prefabs into " + OutDir);
        }

        internal static void Make(string name, string srcPath, float height, Vector3 front, string lmFolder, string dropMats = null)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(srcPath);
            if (!src) { Debug.LogWarning("[EdgeFeatures] missing " + srcPath); return; }
            var root = new GameObject(name);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(src);
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            inst.transform.SetParent(root.transform, false);
            foreach (var c in inst.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            if (dropMats != null)
                foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
                    if (r.sharedMaterials.All(m => m && dropMats.Split('|').Any(d => m.name.StartsWith(d))))   // "a|b": any of these prefixes
                    {
                        var mf = r.GetComponent<MeshFilter>();
                        Object.DestroyImmediate(r); if (mf) Object.DestroyImmediate(mf);
                    }
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
                r.sharedMaterials = r.sharedMaterials.Select(m => Mat(m, lmFolder)).ToArray();

            // face the camera, scale to height, base on y = 0, centred on the origin
            float yaw = Vector3.SignedAngle(new Vector3(front.x, 0, front.z), Vector3.back, Vector3.up);
            inst.transform.localRotation = Quaternion.Euler(0, yaw, 0) * inst.transform.localRotation;
            var b = Bounds(root);
            if (height > 0 && b.size.y > 0.01f) { inst.transform.localScale *= height / b.size.y; b = Bounds(root); }
            inst.transform.localPosition -= new Vector3(b.center.x, b.min.y, b.center.z);
            b = Bounds(root);

            PrefabUtility.SaveAsPrefabAsset(root, OutDir + "/" + name + ".prefab");
            Object.DestroyImmediate(root);
            Debug.Log($"[EdgeFeatures] {name}: {b.size.x:0.0} x {b.size.y:0.0} x {b.size.z:0.0} m from {srcPath}");
        }

        static Bounds Bounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>(true);
            var b = rs.Length > 0 ? rs[0].bounds : new Bounds();
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        // a snow-free, see-through copy of the snow field's material for this mesh material
        static Material Mat(Material m, string lmFolder)
        {
            if (!m) return m;
            Material src = m;
            if (lmFolder != null)
            {
                // SPBuilder.LMMat names its copies SP_LM_<folder>_<material> (non letters/digits -> _)
                string safe = new string((lmFolder + "_" + m.name).Select(ch => char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_').ToArray());
                var sp = AssetDatabase.LoadAssetAtPath<Material>(SPMats + "SP_LM_" + safe + ".mat");
                if (sp) src = sp;
                else
                {
                    var sh = Shader.Find("SnowPlague/Prop");
                    if (!sh) return m;
                    src = new Material(sh) { name = m.name };
                    if (m.mainTexture) src.SetTexture("_BaseMap", m.mainTexture);
                    Debug.Log($"[EdgeFeatures] no SP_LM material for {lmFolder}/{m.name}: plain Prop from its main texture");
                }
            }
            string path = MatDir + "/EF_" + src.name.Replace("SP_LM_", "").Replace("TR_", "") + ".mat";
            var dst = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!dst) { dst = new Material(src); AssetDatabase.CreateAsset(dst, path); }
            else dst.CopyPropertiesFromMaterial(src);
            // no snow anywhere (outside the snow field the global snow amount is 0, but the biases alone still frost)
            foreach (var p in new[] { "_SnowClump", "_SnowCover", "_SnowCap", "_VertexFrost", "_SnowLift" }) if (dst.HasProperty(p)) dst.SetFloat(p, 0f);
            if (dst.HasProperty("_SnowBias")) dst.SetFloat("_SnowBias", -1f);
            if (dst.HasProperty("_GroundMelt")) dst.SetFloat("_GroundMelt", 0f);
            if (dst.HasProperty("_SeeThrough")) { dst.SetFloat("_SeeThrough", 1f); dst.EnableKeyword("SP_SEETHROUGH"); }
            // v3: the snow copies are darkened and greyed for the white snow; out of the snow they read as black blots
            if (src.name == "TR_HeroNeedles") { dst.SetColor("_Tint", new Color(0.62f, 0.78f, 0.55f)); dst.SetFloat("_Desat", 0.1f); dst.SetFloat("_Wrap", 0.65f); }
            else if (src.name == "TR_HeroBark" || src.name == "TR_HeroCore") { dst.SetColor("_Tint", dst.GetColor("_Tint") * 1.35f); dst.SetFloat("_Wrap", 0.45f); }
            else if (dst.HasProperty("_Desaturate")) { dst.SetFloat("_Desaturate", 0.2f); dst.SetColor("_Tint", new Color(0.92f, 0.88f, 0.84f)); }
            EditorUtility.SetDirty(dst);
            return dst;
        }
    }
}
