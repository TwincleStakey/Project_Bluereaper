// 11-17 대전 앞 D4-style staging (Kido: "아직도휑해 디아식으로 스테이지해봐"; refs C:/Users/PC/Downloads/palace_stage/refs).
// Bakes Resources/PalaceStage/* from assets already downloaded for other fields (no new geometry is made here):
//   - PS_G_<kind>_<v>.mat: soft ground patches (mud / leaves / ash / dust) from ThirdParty/PalaceStage/Ground, made by
//     ArtSource/PalaceStage/make_ground_patches.py out of the Fab Megascans / Poly Haven scans
//   - PS_<name>.prefab: Megascans + KHS scans from 폐촌 (AbandonedVillage/ThirdParty/Megascans, materials AV_MS_*),
//     straw-mat covered corpses (SnowPlague Landmarks, Sketchfab CC BY), blood / leaf decals (AV_BloodDecal_*, AV_LeafDecal)
//   Each prefab is centred, rests on y = 0 and is normalised so its longest side is the size below (game-camera readable),
//   colliders stripped. RoomRun.PalaceStage places them.
// Run (no menu item, project rule): "call RoomRun.PalaceStageBake.Bake" in ArtSource/rr_queue/<name>.txt.
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace RoomRun
{
    public static class PalaceStageBake
    {
        const string Out = "Assets/Game/RoomRun/Resources/PalaceStage", Ground = "Assets/Game/RoomRun/ThirdParty/PalaceStage/Ground",
            AV = "Assets/Game/AbandonedVillage", MS = AV + "/ThirdParty/Megascans/";

        // prefab name, model, material, longest side (m)
        static readonly (string name, string fbx, string mat, float size)[] Props =
        {
            ("PS_Tile", MS + "Tile/AV_MS_Tile.fbx", AV + "/Materials/AV_MS_Tile.mat", 0.55f),
            ("PS_TileBird", MS + "KTileBird/AV_MS_KTileBird.fbx", AV + "/Materials/AV_MS_KTileBird.mat", 0.6f),
            ("PS_TileFace", MS + "KTileFace/AV_MS_KTileFace.fbx", AV + "/Materials/AV_MS_KTileFace.mat", 0.6f),
            ("PS_Rubble", MS + "RubblePatch/AV_MS_RubblePatch.fbx", AV + "/Materials/AV_MS_RubblePatch.mat", 3.6f),
            ("PS_Debris", MS + "Debris/AV_MS_Debris.fbx", AV + "/Materials/AV_MS_Debris.mat", 1.8f),
            ("PS_Planks", MS + "Planks/AV_MS_Planks.fbx", AV + "/Materials/AV_MS_Planks.mat", 2.0f),
            ("PS_Sticks", MS + "Sticks/AV_MS_Sticks.fbx", AV + "/Materials/AV_MS_Sticks.mat", 1.4f),
            ("PS_Rocks", MS + "SmallRocks/AV_MS_SmallRocks.fbx", AV + "/Materials/AV_MS_SmallRocks.mat", 0.9f),
            ("PS_Pebbles", MS + "Pebbles/AV_MS_Pebbles.fbx", AV + "/Materials/AV_MS_Pebbles.mat", 0.6f),
            ("PS_Skulls", MS + "Skulls/AV_MS_Skulls.fbx", AV + "/Materials/AV_MS_Skulls.mat", 1.1f),
            ("PS_Sack", MS + "Sack/AV_MS_Sack.fbx", AV + "/Materials/AV_MS_Sack.mat", 1.2f),
            ("PS_Jar", MS + "KJar/AV_MS_KJar.fbx", AV + "/Materials/AV_MS_KJar.mat", 1.2f),
            ("PS_StrawJar", MS + "KStrawJar/AV_MS_KStrawJar.fbx", AV + "/Materials/AV_MS_KStrawJar.mat", 1.0f),
            ("PS_Pot", MS + "Pot/AV_MS_Pot.fbx", AV + "/Materials/AV_MS_Pot.mat", 0.9f),
            ("PS_StrawMat", MS + "KStrawMat/AV_MS_KStrawMat.fbx", AV + "/Materials/AV_MS_KStrawMat.mat", 2.6f),
            ("PS_StrawBag", MS + "KStrawBag/AV_MS_KStrawBag.fbx", AV + "/Materials/AV_MS_KStrawBag.mat", 1.2f),
            ("PS_Basket", MS + "KBasket/AV_MS_KBasket.fbx", AV + "/Materials/AV_MS_KBasket.mat", 0.9f),
            ("PS_CorpseCovered", "Assets/Game/SnowPlague/ThirdParty/Landmarks/CorpseCovered/SKF_CorpseCovered.fbx", "Assets/Game/SnowPlague/Materials/SP_LM_CorpseCovered.mat", 1.9f),
            ("PS_CorpseCloth", "Assets/Game/SnowPlague/ThirdParty/Landmarks/CorpseCloth/SKF_CorpseCloth.fbx", "Assets/Game/SnowPlague/Materials/SP_LM_CorpseCloth.mat", 1.9f),
        };

        public static void Bake()
        {
            Directory.CreateDirectory(Out);
            var log = new System.Text.StringBuilder();
            // ground patches
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            foreach (var f in Directory.GetFiles(Ground, "PS_G_*.png"))
            {
                var p = f.Replace('\\', '/');
                var ti = AssetImporter.GetAtPath(p) as TextureImporter;
                if (!ti) continue;
                bool nrm = p.EndsWith("_N.png");
                var type = nrm ? TextureImporterType.NormalMap : TextureImporterType.Default;
                if (ti.textureType != type || ti.maxTextureSize != 1024 || ti.wrapMode != TextureWrapMode.Clamp || (!nrm && !ti.alphaIsTransparency))
                {
                    ti.textureType = type; ti.maxTextureSize = 1024; ti.wrapMode = TextureWrapMode.Clamp;
                    if (!nrm) { ti.alphaIsTransparency = true; ti.sRGBTexture = true; }
                    ti.SaveAndReimport();
                }
            }
            foreach (var kind in new[] { "Mud", "Leaves", "Ash", "Dust" })
                for (int v = 0; v < 3; v++)
                {
                    var alb = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Ground}/PS_G_{kind}_{v}_A.png");
                    var nrm = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Ground}/PS_G_{kind}_N.png");
                    if (!alb) { log.Append($"no patch {kind}{v}; "); continue; }
                    var m = MatAt($"{Out}/PS_G_{kind}_{v}.mat", lit);
                    m.SetTexture("_BaseMap", alb);
                    // 11-17 r2: at 1.0 the patches read as black ink blots on the lifted stone floor; ~2x brings them within a
                    // value step of the stone (mud / ash a step darker, dust a step lighter)
                    float k = kind == "Dust" ? 1.3f : kind == "Ash" ? 1.6f : 2.0f;
                    m.SetColor("_BaseColor", new Color(k, k, k, 1f));
                    if (nrm) { m.SetTexture("_BumpMap", nrm); m.SetFloat("_BumpScale", 1f); m.EnableKeyword("_NORMALMAP"); }
                    m.SetFloat("_Smoothness", kind == "Mud" ? 0.32f : 0.04f);
                    m.SetFloat("_Metallic", 0f); m.SetFloat("_SpecularHighlights", 0f); m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
                    m.SetFloat("_EnvironmentReflections", 0f); m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
                    Transparent(m, (int)RenderQueue.Transparent - 80 + (kind == "Dust" ? 0 : kind == "Leaves" ? 3 : 1));
                    EditorUtility.SetDirty(m);
                }
            // decals: copies of the 폐촌 blood / leaf materials (kept in step with them), drawn above the patches
            string[] dec = { "AV_BloodDecal_0", "AV_BloodDecal_1", "AV_BloodDecal_2", "AV_BloodDecal_3", "AV_LeafDecal" };
            foreach (var d in dec)
            {
                var src = AssetDatabase.LoadAssetAtPath<Material>($"{AV}/Materials/{d}.mat");
                if (!src) { log.Append($"no {d}; "); continue; }
                var m = MatAt($"{Out}/PS_{d.Substring(3)}.mat", src.shader);
                m.CopyPropertiesFromMaterial(src); m.shaderKeywords = src.shaderKeywords; m.renderQueue = src.renderQueue;
                // 11-17 r3: under the palace grade (saturation -42) the 폐촌 brown-red read as tan paper: darker, deeper red
                if (d.StartsWith("AV_Blood")) m.SetColor("_BaseColor", new Color(0.22f, 0.03f, 0.025f, 1f));
                EditorUtility.SetDirty(m);
            }
            // props
            foreach (var (name, fbxPath, matPath, size) in Props)
            {
                var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (!fbx) { log.Append($"no fbx {name}; "); continue; }
                var root = new GameObject(name);
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
                PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                inst.transform.SetParent(root.transform, false);
                foreach (var c in inst.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
                // Megascans packs come with LOD children: keep LOD0 only
                foreach (var t in inst.GetComponentsInChildren<Transform>(true))
                    if (t && t != inst.transform && System.Text.RegularExpressions.Regex.IsMatch(t.name, "_LOD[1-9]$")) Object.DestroyImmediate(t.gameObject);
                foreach (var lg in inst.GetComponentsInChildren<LODGroup>(true)) Object.DestroyImmediate(lg);
                int nr = 0;
                foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
                {
                    nr++;
                    if (mat) { var ms = r.sharedMaterials; for (int i = 0; i < ms.Length; i++) ms[i] = Lifted(name, mat); r.sharedMaterials = ms; }
                }
                var b = Bounds(root);
                float mx = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
                if (mx > 1e-4f) inst.transform.localScale *= size / mx;
                b = Bounds(root);
                // the rolled straw mat stood up as a 2 m log: pressed flat like 폐촌 does (~12 cm)
                if (name == "PS_StrawMat" && b.size.y > 0.12f) { var ls = inst.transform.localScale; inst.transform.localScale = new Vector3(ls.x, ls.y * 0.12f / b.size.y, ls.z); b = Bounds(root); }
                inst.transform.localPosition -= new Vector3(b.center.x, b.min.y, b.center.z);
                b = Bounds(root);
                PrefabUtility.SaveAsPrefabAsset(root, Out + "/" + name + ".prefab");
                Object.DestroyImmediate(root);
                log.Append($"{name} {b.size.x:0.00}x{b.size.y:0.00}x{b.size.z:0.00} r{nr}; ");
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[PalaceStage] bake: " + log);
            File.AppendAllText(Path.Combine(Directory.GetParent(Application.dataPath).FullName, "ArtSource", "rr_log.txt"), $"[{System.DateTime.Now:HH:mm:ss}] PalaceStageBake done: {log}\n");
        }

        // 11-17 r2: the 폐촌 materials are tuned for that lit village and read as black lumps on the palace floor: a copy per
        // prop with the base colour lifted ~2x and no gloss
        static Material Lifted(string name, Material src)
        {
            // 11-17 r5: the snow-field prop shader drew the straw-mat corpses as faint translucent ghosts on this floor:
            // anything on SnowPlague/Prop becomes plain URP Lit with the same maps, its _Tint as the base colour
            if (src.shader.name.StartsWith("SnowPlague"))
            {
                var u = MatAt($"{Out}/{name}_M.mat", Shader.Find("Universal Render Pipeline/Lit"));
                u.SetTexture("_BaseMap", src.GetTexture("_BaseMap"));
                var bn = src.HasProperty("_BumpMap") ? src.GetTexture("_BumpMap") : null;
                if (bn) { u.SetTexture("_BumpMap", bn); u.EnableKeyword("_NORMALMAP"); } else u.DisableKeyword("_NORMALMAP");
                var t = src.HasProperty("_Tint") ? src.GetColor("_Tint") : Color.white;
                // 11-17 r7: still dark maroon blobs at x2.2 under the grade; straw mats are the one light thing in a dark court (D4 value
                // contrast: the story pieces are the brightest shapes on the floor)
                u.SetColor("_BaseColor", new Color(Mathf.Min(t.r * 3.4f, 1.35f), Mathf.Min(t.g * 3.4f, 1.2f), Mathf.Min(t.b * 3.4f, 0.95f), 1f));
                u.SetFloat("_Smoothness", 0.05f); u.SetFloat("_Surface", 0f); u.renderQueue = -1;
                EditorUtility.SetDirty(u);
                return u;
            }
            var m = MatAt($"{Out}/{name}_M.mat", src.shader);
            m.CopyPropertiesFromMaterial(src); m.shaderKeywords = src.shaderKeywords; m.renderQueue = src.renderQueue;
            float k = name.StartsWith("PS_Corpse") ? 2.2f : 2.0f;   // straw mats over the bodies: straw, not a dark lump
            foreach (var prop in new[] { "_BaseColor", "_Color", "_Tint" })
                if (m.HasProperty(prop)) { var c = m.GetColor(prop); m.SetColor(prop, new Color(Mathf.Min(c.r * k, 1.3f), Mathf.Min(c.g * k, 1.3f), Mathf.Min(c.b * k, 1.3f), c.a)); }
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", Mathf.Min(m.GetFloat("_Smoothness"), 0.15f));
            // flat things on the floor must not dissolve in the soft see-through ring around the player (SP_SEETHROUGH)
            if (m.HasProperty("_SeeThrough")) { m.SetFloat("_SeeThrough", 0f); m.DisableKeyword("SP_SEETHROUGH"); }
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material MatAt(string path, Shader shader)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            if (m.shader != shader) m.shader = shader;
            return m;
        }

        static void Transparent(Material m, int queue)
        {
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f); m.SetFloat("_ZWrite", 0f); m.SetFloat("_Cull", 2f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One); m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.SetOverrideTag("RenderType", "Transparent");
            m.SetFloat("_BlendModePreserveSpecular", 0f); m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.SetShaderPassEnabled("DepthOnly", false); m.SetShaderPassEnabled("ShadowCaster", false);
            m.renderQueue = queue;
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
