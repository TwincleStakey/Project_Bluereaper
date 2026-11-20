// 11-16 대전 앞 props (Kido: "이것도 애셋들좀 채워넣어봐 너무 심심해"). Bakes Resources/EdgeFeatures/PP_*.prefab from the
// downloaded models in ThirdParty/PalaceProps (converted by ArtSource/PalaceProps/convert_palace_props.py; credits in
// Assets/Game/RoomRun/CREDITS.txt), which RoomRun.EdgeFeatures.PalaceProps places when the palace field is entered.
// Materials: SnowPlague/Prop (the hanok wall's shader) with snow off and the soft see-through on, dark, greyed and matte.
// Run (no menu item, project rule): "call RoomRun.PalacePropsBake.Bake" in ArtSource/rr_queue/<name>.txt.
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace RoomRun
{
    public static class PalacePropsBake
    {
        const string Src = "Assets/Game/RoomRun/ThirdParty/PalaceProps", Out = "Assets/Game/RoomRun/Resources/EdgeFeatures",
            MatDir = "Assets/Game/RoomRun/ThirdParty/PalaceProps/Materials";

        // tint (rgb), desaturate, smoothness, double sided
        struct Look { public Color tint; public float desat, smooth; public bool twoSided; public Look(Color t, float d, float s, bool two = false) { tint = t; desat = d; smooth = s; twoSided = two; } }
        static readonly Dictionary<string, Look> Looks = new Dictionary<string, Look>
        {
            ["PP_Dumu"] = new Look(new Color(0.62f, 0.6f, 0.58f), 0.5f, 0.12f),
            ["PP_IncenseB"] = new Look(new Color(0.55f, 0.52f, 0.5f), 0.6f, 0.1f),
            ["PP_IncenseC"] = new Look(new Color(0.6f, 0.58f, 0.55f), 0.55f, 0.1f),
            ["PP_Haetae"] = new Look(new Color(0.62f, 0.6f, 0.58f), 0.5f, 0.04f),
            ["PP_Brazier"] = new Look(new Color(0.55f, 0.52f, 0.5f), 0.5f, 0.1f),
            ["PP_Flag"] = new Look(new Color(1.25f, 1.12f, 1.02f), 0.45f, 0.02f),   // the dark blue cloth went black at 0.6 under the night grade
            ["PP_Hwacha"] = new Look(new Color(0.4f, 0.36f, 0.33f), 0.6f, 0.02f),   // charred
            ["PP_Spear"] = new Look(new Color(0.55f, 0.52f, 0.5f), 0.5f, 0.15f),
            ["PP_Hwando"] = new Look(new Color(0.55f, 0.52f, 0.5f), 0.5f, 0.15f),
            ["PP_Woldo"] = new Look(new Color(0.55f, 0.52f, 0.5f), 0.5f, 0.15f),
            ["PP_Beam"] = new Look(new Color(0.46f, 0.4f, 0.36f), 0.6f, 0.02f),     // burnt
        };

        public static void Bake()
        {
            Directory.CreateDirectory(Out); Directory.CreateDirectory(MatDir);
            // textures in at 1K (LFS diet), normal maps flagged
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Src }))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                var ti = AssetImporter.GetAtPath(p) as TextureImporter;
                if (!ti) continue;
                bool nrm = p.EndsWith("_normal.png");
                var type = nrm ? TextureImporterType.NormalMap : TextureImporterType.Default;
                if (ti.textureType == type && ti.maxTextureSize == 1024 && ti.sRGBTexture == !nrm) continue;
                ti.textureType = type; ti.maxTextureSize = 1024; ti.sRGBTexture = !nrm; ti.SaveAndReimport();
            }
            // the banner cloth is a single plane that was double-sided in the glTF: normals rebuilt from its winding
            var fi = AssetImporter.GetAtPath(Src + "/PP_Flag/PP_Flag.fbx") as ModelImporter;
            if (fi && fi.importNormals != ModelImporterNormals.Calculate)
            {
                fi.importNormals = ModelImporterNormals.Calculate; fi.normalSmoothingAngle = 60f;
                fi.importTangents = ModelImporterTangents.CalculateMikk; fi.SaveAndReimport();
            }
            var shader = Shader.Find("SnowPlague/Prop");
            if (!shader) { Debug.LogError("[PalaceProps] no SnowPlague/Prop shader"); return; }
            foreach (var dir in Directory.GetDirectories(Src))
            {
                string name = Path.GetFileName(dir);
                if (!name.StartsWith("PP_")) continue;
                var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(dir.Replace('\\', '/') + "/" + name + ".fbx");
                if (!fbx) { Debug.LogWarning("[PalaceProps] no fbx " + name); continue; }
                var look = Looks.TryGetValue(name, out var l) ? l : new Look(new Color(0.6f, 0.58f, 0.55f), 0.5f, 0.1f);
                var root = new GameObject(name);
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
                PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                inst.transform.SetParent(root.transform, false);
                foreach (var c in inst.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
                foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
                {
                    var ms = r.sharedMaterials;
                    for (int i = 0; i < ms.Length; i++)
                    {
                        // slots are named <model>_<index> by the converter
                        string slot = ms[i] ? ms[i].name : name + "_" + i;
                        int idx = i; int us = slot.LastIndexOf('_');
                        if (us > 0 && int.TryParse(slot.Substring(us + 1), out var k)) idx = k;
                        ms[i] = Mat(name, idx, dir.Replace('\\', '/'), shader, look);
                    }
                    r.sharedMaterials = ms;
                }
                // base on y = 0, centred on the origin (real size kept)
                var b = Bounds(root);
                inst.transform.localPosition -= new Vector3(b.center.x, b.min.y, b.center.z);
                b = Bounds(root);
                PrefabUtility.SaveAsPrefabAsset(root, Out + "/" + name + ".prefab");
                Object.DestroyImmediate(root);
                Debug.Log($"[PalaceProps] {name}: {b.size.x:0.00} x {b.size.y:0.00} x {b.size.z:0.00} m");
            }
            AssetDatabase.SaveAssets();
            File.AppendAllText(Path.Combine(Directory.GetParent(Application.dataPath).FullName, "ArtSource", "rr_log.txt"), $"[{System.DateTime.Now:HH:mm:ss}] PalacePropsBake done\n");
        }

        static Material Mat(string model, int idx, string dir, Shader shader, Look look)
        {
            string path = MatDir + "/" + model + "_" + idx + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            m.shader = shader;
            var alb = AssetDatabase.LoadAssetAtPath<Texture2D>(dir + "/" + model + "_" + idx + "_albedo.png");
            var nrm = AssetDatabase.LoadAssetAtPath<Texture2D>(dir + "/" + model + "_" + idx + "_normal.png");
            if (alb) m.SetTexture("_BaseMap", alb);
            if (nrm) m.SetTexture("_BumpMap", nrm);
            m.SetColor("_Tint", look.tint);
            m.SetColor("_TintVar", new Color(1, 1, 1, 0.12f));
            m.SetFloat("_Desaturate", look.desat);
            m.SetFloat("_Smoothness", look.smooth);
            m.SetFloat("_NormalStrength", 1f);
            m.SetFloat("_VertexFrost", 0f); m.SetFloat("_AOStrength", 0f);
            m.SetFloat("_SnowBias", -1f); m.SetFloat("_GroundMelt", 0f); m.SetFloat("_SnowCap", 0f);
            m.SetFloat("_Cull", look.twoSided ? 0f : 2f);
            m.SetFloat("_SeeThrough", 1f); m.EnableKeyword("SP_SEETHROUGH");
            EditorUtility.SetDirty(m);
            return m;
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
