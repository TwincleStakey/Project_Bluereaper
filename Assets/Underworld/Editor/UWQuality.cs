// Underworld quality pass (11-13): scan ground textures and the floor material, without rebuilding the scene
// (the builder regenerates everything; this only touches the floor material and the new ground textures).
using UnityEditor;
using UnityEngine;

namespace Underworld
{
    // Ground scans: packed albedo+height (sRGB) and normal+rough+AO (linear), 2k, aniso 8.
    sealed class UWGroundImport : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/Underworld/Textures/Ground/")) return;
            var ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = !assetPath.Contains("_NRA");
            ti.alphaSource = TextureImporterAlphaSource.FromInput;
            ti.alphaIsTransparency = false;
            ti.mipmapEnabled = true;
            ti.maxTextureSize = 2048;
            ti.anisoLevel = 8;
            ti.wrapMode = TextureWrapMode.Repeat;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;   // BC7 on PC
        }
    }

    public static class UWQuality
    {
        const string G = "Assets/Underworld/Textures/Ground/";

        [MenuItem("Tools/Underworld/Quality/Apply Ground Material")]
        public static void ApplyGround()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>("Assets/Underworld/Materials/UW_Floor.mat");
            var sh = Shader.Find("Underworld/Ground");
            if (!m || !sh) { Debug.LogError("UWQuality: floor material or Underworld/Ground shader missing"); return; }
            Setup(m, sh);
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssets();
            Debug.Log("UWQuality: UW_Floor now uses Underworld/Ground");
        }

        public static void Setup(Material m, Shader sh)
        {
            m.shader = sh;
            void T(string prop, string file) { var t = AssetDatabase.LoadAssetAtPath<Texture2D>(G + file); if (!t) Debug.LogError("UWQuality: missing " + file); m.SetTexture(prop, t); }
            T("_FlagAH", "T_UWG_Crack_AH.png"); T("_FlagNRA", "T_UWG_Crack_NRA.png");   // 3rd pass (Kido: "저승이 그냥 일반 필드"): petrified cracked black rock
            T("_AshAH", "T_UWG_CoalAsh_AH.png"); T("_AshNRA", "T_UWG_CoalAsh_NRA.png");   // grey ash
            T("_RubAH", "T_UWG_Coal_AH.png"); T("_RubNRA", "T_UWG_Coal_NRA.png");   // black coal debris along the seams
            // Fab Megascans only (Kido 11-13): wet patches reuse the burnt floor at another scale, darker and glossy;
            // the few side faces of the plaza use it triplanar
            T("_MudAH", "T_UWG_Ash_AH.png"); T("_MudNRA", "T_UWG_Ash_NRA.png");
            T("_CliffAH", "T_UWG_Ash_AH.png"); T("_CliffNRA", "T_UWG_Ash_NRA.png");
            // Rewrite 11-13 ("too ugly"): big slabs, soft warped masks, height-blended earth/rubble seams, capped smoothness.
            // 2nd pass scans: Flag = church-ruin slabs (1 m scan, ~0.45 m slabs), Ash = dark soil (0.36 m), Rub = rocky ground (3 m),
            // Mud = burnt forest floor (2 m). Values from ArtSource/uw_quality/ref_targets.md (ref contrast 0.12-0.22, slabs ~1.5 m).
            m.SetVector("_Tiles", new Vector4(3.5f, 2.2f, 1.8f, 4.5f));   // crack 1 m, coal ash 0.36 m, coal debris 1 m, burnt 2 m
            m.SetVector("_HeightLo", new Vector4(0.26f, 0.23f, 0.28f, 0.12f));
            m.SetVector("_HeightHi", new Vector4(0.74f, 0.64f, 0.71f, 0.37f));
            m.SetFloat("_CliffTile", 4f);
            m.SetColor("_FlagTint", new Color(0.88f, 0.86f, 0.86f)); m.SetColor("_AshTint", new Color(0.4f, 0.39f, 0.4f));   // Kido 11-13: read pale/bone-white -> dark ash   // crack luma 0.048, coal ash 0.083: target ~0.05-0.06, ash slightly cold
            m.SetColor("_RubTint", new Color(1.4f, 1.4f, 1.45f));   // coal debris luma 0.011
            m.SetColor("_MudTint", new Color(1.3f, 1.25f, 1.25f)); m.SetColor("_CliffTint", new Color(1.6f, 1.6f, 1.6f));
            m.SetFloat("_Sat", 0.25f);
            m.SetFloat("_AshAmt", 0.58f); m.SetFloat("_RubAmt", 0.5f); m.SetFloat("_MudAmt", 0.35f);
            m.SetFloat("_GroutLevel", 0.4f); m.SetFloat("_AshLift", 0.28f); m.SetFloat("_BlendDepth", 0.3f);
            m.SetFloat("_SeamDark", 0.2f); m.SetFloat("_Cavity", 0.3f);
            m.SetFloat("_NrmStr", 0.5f); m.SetFloat("_Macro", 0.6f); m.SetFloat("_AOStr", 0.45f);
            m.SetFloat("_SmoothScale", 0.4f); m.SetFloat("_WetAmt", 0.4f); m.SetFloat("_WetSmooth", 0.45f); m.SetFloat("_SmoothMax", 0.4f);
            // _CrackCol was still the old violet #9B5CFF x2 from the first floor material: it painted purple patches (12:35)
            m.SetFloat("_CrackAmt", 0.3f);   // dreamy pass (Kido wants the old violet floor cracks back): seam glow, soft m.SetColor("_CrackCol", new Color(0.55f, 0.28f, 1.1f)); m.SetFloat("_RimAmt", 0f);
        }

        // shader compile messages to ArtSource/uw_quality/shader_check.txt (console is not reachable remotely)
        [MenuItem("Tools/Underworld/Quality/Check Shaders")]
        public static void CheckShaders()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var name in new[] { "Underworld/Ground", "Underworld/HeightFog" })
            {
                var sh = Shader.Find(name);
                if (!sh) { sb.AppendLine(name + ": not found"); continue; }
                ShaderUtil.CompilePass(new Material(sh), 0, true);
                var msgs = ShaderUtil.GetShaderMessages(sh);
                sb.AppendLine($"{name}: hasError={ShaderUtil.ShaderHasError(sh)} messages={msgs.Length}");
                foreach (var m in msgs) sb.AppendLine($"  {m.severity} line {m.line} {m.platform}: {m.message}");
            }
            var dir = System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, "ArtSource/uw_quality");
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "shader_check.txt"), sb.ToString());
        }

        // Fab Megascans bones (3rd pass): URP Lit materials + prefabs in Resources so UWLook can scatter them at runtime
        // without touching the scene. Writes ArtSource/uw_quality/bones.txt with triangle counts.
        [MenuItem("Tools/Underworld/Quality/Build Bone Prefabs")]
        public static void BuildBones()
        {
            const string B = "Assets/Underworld/Models/Bones/";
            const string R = "Assets/Underworld/Resources/Bones/";
            System.IO.Directory.CreateDirectory(R);
            var sb = new System.Text.StringBuilder();
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            foreach (var n in new[] { "Animal_Jaw_Bone_vilicfiaa_Mid", "Animal_Leg_Bone_vd5jei1aa_Mid" })
            {
                foreach (var map in new[] { "_2K_Normal.jpg" })
                {
                    var ti = AssetImporter.GetAtPath(B + n + map) as TextureImporter;
                    if (ti && ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; ti.SaveAndReimport(); }
                }
                foreach (var map in new[] { "_2K_Roughness.jpg", "_2K_AO.jpg" })
                {
                    var ti = AssetImporter.GetAtPath(B + n + map) as TextureImporter;
                    if (ti && ti.sRGBTexture) { ti.sRGBTexture = false; ti.SaveAndReimport(); }
                }
                var matPath = R + n + ".mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (!mat) { mat = new Material(lit); AssetDatabase.CreateAsset(mat, matPath); }
                mat.shader = lit;
                mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(B + n + "_2K_BaseColor.jpg"));
                mat.SetColor("_BaseColor", new Color(0.6f, 0.57f, 0.52f));   // Kido: less prominent
                mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(B + n + "_2K_Normal.jpg")); mat.EnableKeyword("_NORMALMAP");
                mat.SetTexture("_OcclusionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(B + n + "_2K_AO.jpg")); mat.EnableKeyword("_OCCLUSIONMAP");
                mat.SetFloat("_Smoothness", 0.25f);
                mat.enableInstancing = true;
                EditorUtility.SetDirty(mat);

                var model = AssetDatabase.LoadAssetAtPath<GameObject>(B + n + ".fbx");
                if (!model) { sb.AppendLine(n + ": fbx missing"); continue; }
                var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
                int tris = 0;
                foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
                {
                    r.sharedMaterial = mat;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    var mf = r.GetComponent<MeshFilter>(); if (mf && mf.sharedMesh) tris += mf.sharedMesh.triangles.Length / 3;
                }
                foreach (var c in go.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
                var b = new Bounds(go.transform.position, Vector3.zero);
                foreach (var r in go.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
                PrefabUtility.SaveAsPrefabAsset(go, R + n + ".prefab");
                Object.DestroyImmediate(go);
                sb.AppendLine($"{n}: {tris} tris, bounds {b.size}");
            }
            AssetDatabase.SaveAssets();
            var dir = System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, "ArtSource/uw_quality");
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "bones.txt"), sb.ToString());
        }

        [MenuItem("Tools/Underworld/Quality/Reimport Ground Textures")]
        public static void Reimport()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Underworld/Textures/Ground" }))
                AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guid), ImportAssetOptions.ForceUpdate);
            Debug.Log("UWQuality: ground textures reimported");
        }
    }
}
