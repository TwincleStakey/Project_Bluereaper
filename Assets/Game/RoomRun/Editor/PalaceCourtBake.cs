// 11-19 대전 앞 court layer (Kido: "make it like a real palace", downloaded assets only). Bakes Resources/PalaceCourt/PC_*.prefab
// from the downloaded models in ThirdParty/PalaceCourt (converted by ArtSource/PalaceCourt/convert_palace_court.py; credits in
// Assets/Game/RoomRun/CREDITS.txt), which EnvRecompose.Court.cs places while RR.EnvRecompose.palace.court = 1.
// A copy of PalacePropsBake with its own folders (that pipeline is left untouched).
// Materials: SnowPlague/Prop (the hanok wall's shader) with snow off and the soft see-through on, dark, greyed and matte.
// Run (no menu item, project rule): "call RoomRun.PalaceCourtBake.Bake" in ArtSource/rr_queue/<name>.txt.
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace RoomRun
{
    public static class PalaceCourtBake
    {
        const string Src = "Assets/Game/RoomRun/ThirdParty/PalaceCourt", Out = "Assets/Game/RoomRun/Resources/PalaceCourt",
            MatDir = "Assets/Game/RoomRun/ThirdParty/PalaceCourt/Materials";

        // tint (rgb), desaturate, smoothness, double sided
        struct Look { public Color tint; public float desat, smooth; public bool twoSided; public Look(Color t, float d, float s, bool two = false) { tint = t; desat = d; smooth = s; twoSided = two; } }
        static readonly Dictionary<string, Look> Looks = new Dictionary<string, Look>
        {
            // painted wood / tile keep a little colour (red posts read as 행각), stone stays grey; all matte
            // pass 3 (review: the buildings read as untextured grey blocks): the textures were there, the 0.45 desaturate +
            // 0.62 tint (+ the court's matte x0.82 and the grade's -55 saturation) flattened them; keep their own colour
            ["PC_Hall"] = new Look(new Color(0.86f, 0.83f, 0.8f), 0.08f, 0.04f),
            ["PC_Gate"] = new Look(new Color(0.86f, 0.83f, 0.8f), 0.08f, 0.04f),
            ["PC_Stairs"] = new Look(new Color(0.8f, 0.78f, 0.76f), 0.3f, 0.03f),
            ["PC_Lion"] = new Look(new Color(0.66f, 0.64f, 0.62f), 0.5f, 0.03f),
            // pass 10: the red post cut from the 득중정 keeps its own colour (the default grey look made it a black stick)
            ["PC_Pillar"] = new Look(new Color(0.92f, 0.88f, 0.85f), 0.06f, 0.04f),
            // pass 11: the whole KCISA small gates (경선문 / 가어문) keep their own colour like the halls
            ["PC_GateS"] = new Look(new Color(0.9f, 0.86f, 0.83f), 0.06f, 0.04f),
            ["PC_GateA"] = new Look(new Color(0.9f, 0.86f, 0.83f), 0.06f, 0.04f),
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
                bool nrm = p.EndsWith("_normal.jpg");
                var type = nrm ? TextureImporterType.NormalMap : TextureImporterType.Default;
                if (ti.textureType == type && ti.maxTextureSize == 1024 && ti.sRGBTexture == !nrm) continue;
                ti.textureType = type; ti.maxTextureSize = 1024; ti.sRGBTexture = !nrm; ti.SaveAndReimport();
            }
            var shader = Shader.Find("SnowPlague/Prop");
            if (!shader) { Debug.LogError("[PalaceCourt] no SnowPlague/Prop shader"); return; }
            foreach (var dir in Directory.GetDirectories(Src))
            {
                string name = Path.GetFileName(dir);
                if (!name.StartsWith("PC_")) continue;
                var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(dir.Replace('\\', '/') + "/" + name + ".fbx");
                if (!fbx) { Debug.LogWarning("[PalaceCourt] no fbx " + name); continue; }
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
                Debug.Log($"[PalaceCourt] {name}: {b.size.x:0.00} x {b.size.y:0.00} x {b.size.z:0.00} m");
            }
            AssetDatabase.SaveAssets();
            File.AppendAllText(Path.Combine(Directory.GetParent(Application.dataPath).FullName, "ArtSource", "rr_log.txt"), $"[{System.DateTime.Now:HH:mm:ss}] PalaceCourtBake done\n");
        }

        // mean sRGB albedo cap per slot. These props are lit by the URP key light (x1.38 under the volume pilot) while the
        // floor shades itself (LookStudy), so a 0.29 tile roof in open moonlight rendered far brighter than the floor
        // (pass 4: 0.13 went to featureless dark boxes, 0.19 keeps the tile pattern; diagnosis: the roofs that read dark sat in the wall / pine shadow, the pale ones in open moonlight; the
        // soft see-through made no difference)
        // pass 5: the buildings are back to their pass-3 look (no cap) - the review preferred tile pattern in shade over
        // darkened boxes, and the brightly lit runs were removed instead
        static float MaxAlbedo(string model) => model == "PC_Hall" || model == "PC_Gate" || model == "PC_Pillar" || model == "PC_GateS" || model == "PC_GateA" ? 1f : model == "PC_Stairs" ? 0.24f : 0.26f;
        static Material Mat(string model, int idx, string dir, Shader shader, Look look)
        {
            string path = MatDir + "/" + model + "_" + idx + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            m.shader = shader;
            var alb = AssetDatabase.LoadAssetAtPath<Texture2D>(dir + "/" + model + "_" + idx + "_albedo.jpg");
            var nrm = AssetDatabase.LoadAssetAtPath<Texture2D>(dir + "/" + model + "_" + idx + "_normal.jpg");
            if (alb) m.SetTexture("_BaseMap", alb);
            if (nrm) m.SetTexture("_BumpMap", nrm);
            // pass 4 (review: south / side 행각 read as pale boxes): every slot capped (MaxAlbedo), so plaster, stone base and
            // tiles all sit at or under the floor's value and the red posts keep their hue
            float k = 1f;
            if (alb)
            {
                var t = new Texture2D(2, 2);
                if (t.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(alb))))
                {
                    var px = t.GetPixels32(); double sum = 0; int n = 0, step = Mathf.Max(1, px.Length / 4096);
                    for (int i = 0; i < px.Length; i += step) { var c = px[i]; sum += (0.2126 * c.r + 0.7152 * c.g + 0.0722 * c.b) / 255.0; n++; }
                    float lum = n > 0 ? (float)(sum / n) : 0f;
                    if (lum > MaxAlbedo(model)) k = MaxAlbedo(model) / lum;
                }
                Object.DestroyImmediate(t);
            }
            m.SetColor("_Tint", new Color(look.tint.r * k, look.tint.g * k, look.tint.b * k, 1f));
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
