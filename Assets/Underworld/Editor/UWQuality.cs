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

        // Lava sea seen in the void around/below the arena (300 x 300 m plane at y = -6). Lives in Resources so UWLook can Resources.Load it.
        [MenuItem("Tools/Underworld/Quality/Apply Lava Sea Material")]
        public static void ApplyLavaSea()
        {
            const string path = "Assets/Underworld/Resources/UW_LavaSea.mat";
            var sh = Shader.Find("Underworld/Ground");
            if (!sh) { Debug.LogError("UWQuality: Underworld/Ground shader missing"); return; }
            System.IO.Directory.CreateDirectory("Assets/Underworld/Resources");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            Setup(m, sh);                                  // textures + shared look, then the sea overrides
            m.SetFloat("_SeaMode", 1f);                    // whole surface molten, drifting dark crust islands
            m.SetFloat("_LavaScale", 2.5f);                // crust plates ~2.8 m, islands tens of metres: reads from far above
            m.SetFloat("_LavaIntensity", 0.45f);            // pass 7 (was 0.7). pass 6: caramel patches darker. pass 2: was 1.8, sea read as white-hot blobs. dark, far below: channel cores ~1.8, mid ~0.6, edge ~0.2
            m.SetFloat("_LavaFlow", 0.6f); m.SetFloat("_LavaPulse", 0.25f);
            m.SetFloat("_LavaAmt", 0f);                    // no extra veins, no rock to scorch
            m.SetFloat("_ScorchAmt", 0f); m.SetFloat("_ObsAmt", 0f); m.SetFloat("_LavaBounce", 0f);
            m.SetColor("_LavaColHot", new Color(1.0f, 0.34f, 0.06f)); m.SetColor("_LavaColMid", new Color(0.85f, 0.1f, 0.02f)); m.SetColor("_LavaColCool", new Color(0.45f, 0.03f, 0.01f));   // pass 8: magma red, not toffee
            m.SetFloat("_RubAmt", 0f); m.SetFloat("_AshAmt", 0f);
            m.SetVector("_Tiles", new Vector4(8f, 6f, 4f, 4.5f));
            m.SetFloat("_CrackAmt", 0f);
            m.SetFloat("_CodeFX", 1f);   // the old procedural sea keeps its coded channels (unused since 11-14: UW_LavaField)
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssets();
            Debug.Log("UWQuality: " + path + " written");
        }

        // Main rock scan used in the Flag slot. Switch to "Lava" once T_UWG_Lava_AH/NRA.png (Fab volcanic / lava rock, packed
        // AH + NRA like the others) exist; falls back to Crack automatically while the files are missing.
        const string RockSet = "Lava";
        const string RockFallback = "Crack";

        public static void Setup(Material m, Shader sh)
        {
            m.shader = sh;
            void T(string prop, string file) { var t = AssetDatabase.LoadAssetAtPath<Texture2D>(G + file); if (!t) Debug.LogError("UWQuality: missing " + file); m.SetTexture(prop, t); }
            string rock = AssetDatabase.LoadAssetAtPath<Texture2D>(G + "T_UWG_" + RockSet + "_AH.png") ? RockSet : RockFallback;
            T("_FlagAH", "T_UWG_" + rock + "_AH.png"); T("_FlagNRA", "T_UWG_" + rock + "_NRA.png");   // HELL pass: main cracked rock
            T("_AshAH", "T_UWG_CoalAsh_AH.png"); T("_AshNRA", "T_UWG_CoalAsh_NRA.png");               // cooled dark crust (crevices only)
            T("_RubAH", "T_UWG_Coal_AH.png"); T("_RubNRA", "T_UWG_Coal_NRA.png");                     // coal debris along the crack banks
            T("_MudAH", "T_UWG_Ash_AH.png"); T("_MudNRA", "T_UWG_Ash_NRA.png");                      // Mud slot unused by the hell shader (kept so the material keeps its slots)
            T("_CliffAH", "T_UWG_" + rock + "_AH.png"); T("_CliffNRA", "T_UWG_" + rock + "_NRA.png"); // plaza side faces: same rock

            // layout: crack scan 1 m -> 3 m tile, coal ash 0.36 m -> 2 m, coal debris 1 m -> 1.4 m
            m.SetVector("_Tiles", new Vector4(3.0f, 2.0f, 1.4f, 4.5f));
            m.SetVector("_HeightLo", new Vector4(0.26f, 0.23f, 0.28f, 0.12f));
            m.SetVector("_HeightHi", new Vector4(0.74f, 0.64f, 0.71f, 0.37f));
            m.SetFloat("_CliffTile", 3f);
            // albedo luma targets 0.04-0.08: crack scan luma ~0.048 -> x1.3, coal ash ~0.083 -> x0.7, coal debris ~0.011 -> x3.5
            m.SetColor("_FlagTint", new Color(1.35f, 1.27f, 1.2f));   // faint warm cast on the basalt
            m.SetColor("_AshTint", new Color(0.72f, 0.66f, 0.62f));
            m.SetColor("_RubTint", new Color(3.6f, 3.4f, 3.2f));
            m.SetColor("_MudTint", new Color(1.3f, 1.25f, 1.25f)); m.SetColor("_CliffTint", new Color(1.2f, 1.15f, 1.1f));
            m.SetFloat("_Sat", 0.3f);
            m.SetFloat("_AshAmt", 0.22f); m.SetFloat("_RubAmt", 0.8f); m.SetFloat("_MudAmt", 0f);   // crust only a little, in crevices
            m.SetFloat("_GroutLevel", 0.3f); m.SetFloat("_AshLift", 0.2f); m.SetFloat("_BlendDepth", 0.2f);
            m.SetFloat("_SeamDark", 0.25f); m.SetFloat("_Cavity", 0.5f);
            m.SetFloat("_NrmStr", 0.9f); m.SetFloat("_Macro", 0.6f); m.SetFloat("_AOStr", 0.7f);
            m.SetFloat("_SmoothScale", 0.35f); m.SetFloat("_WetAmt", 0f); m.SetFloat("_WetSmooth", 0.45f); m.SetFloat("_SmoothMax", 0.3f);   // moderate roughness
            // lava (post: bloom threshold ~0.95, +1.6 EV): hot core 3.5 x ~1 = HDR 3-4, deep red edge stays just under bloom
            // Diablo-IV hell: a few wide rifts (1-3 m) with dark cooling crust plates, wide red halo, rare dim veins.
            // Colours are normalised (max channel 1); the HDR level comes from the shader: edge 0.4, mid 1.2, hot core 3.5 x _LavaIntensity/3.5.
            m.SetFloat("_LavaAmt", 0.15f);        // vein region coverage (rare short veins branching off rifts); 0 = almost none
            m.SetFloat("_LavaWidth", 0.5f);   // pass 2: rivers dominated the arena      // rift half-width in m (rifts come out 0.5-1.5 half = 1-3 m wide, pinched into segments)
            m.SetFloat("_LavaScale", 1f); m.SetFloat("_SeaMode", 0f);
            m.SetColor("_LavaColHot", new Color(1.0f, 0.52f, 0.14f));   // pass 3: no white cores (review_02 #3)
            m.SetColor("_LavaColMid", new Color(1.0f, 0.28f, 0.04f));
            m.SetColor("_LavaColCool", new Color(0.9f, 0.06f, 0.015f));
            m.SetFloat("_LavaIntensity", 1.6f);   // pass 2 (was 2.7): cores still blew to near white
            m.SetFloat("_LavaPulse", 0.3f); m.SetFloat("_LavaFlow", 1f);
            m.SetFloat("_LavaBounce", 0.28f); m.SetFloat("_ScorchAmt", 0.6f); m.SetFloat("_ObsAmt", 0.1f);   // final_03: softer black bank outline
            m.SetVector("_EdgeRect", new Vector4(-44f, -29f, 44f, 27f));   // pass 4: broken glowing rim along the UW_Plaza bounds
            m.SetVector("_ClearZone", new Vector4(-2f, -2f, 6f, 8f));
            m.SetVector("_Lane0", new Vector4(-2f, -2f, -2f, 27f));    // pass 6: spawn -> burning gate hall
            m.SetVector("_Lane1", new Vector4(-2f, -2f, 20f, 26f));    // spawn -> portal plateau
            m.SetVector("_LaneWidth", new Vector4(5f, 7f, 0f, 0f));   // pass 7: wider, so no stray fragment survives in the wedge between the lanes   // rivers taper out over 8 m   // spawn / boss centre without rivers
            m.SetFloat("_CrackAmt", 0f);         // old violet soul cracks OFF (set 0.3 to bring them back)
            m.SetFloat("_RimAmt", 0f);
            // 11-14 Kido ("don't draw these yourself, download one"): every coded glow line / voronoi seam on the floor OFF.
            // The floor is now only the downloaded scans: Hawaiian Lava Stone (4 m scan) as the rock, coal ash in the low
            // crevices and in soft patches, coal debris in large drifts; macro brightness variation breaks up repetition.
            if (rock == RockSet)
            {
                m.SetFloat("_CodeFX", 0f);
                m.SetVector("_Tiles", new Vector4(4.0f, 2.2f, 1.6f, 4.5f));
                m.SetVector("_HeightLo", new Vector4(0.2f, 0.23f, 0.28f, 0.12f));
                m.SetVector("_HeightHi", new Vector4(0.8f, 0.64f, 0.71f, 0.37f));
                m.SetColor("_FlagTint", new Color(1.1f, 1.03f, 0.96f));   // Hawaiian scan luma ~0.085 linear: keep near black but readable
                m.SetColor("_CliffTint", new Color(0.9f, 0.85f, 0.8f));
                m.SetFloat("_Sat", 0.22f);
                m.SetFloat("_AshAmt", 0.42f); m.SetFloat("_RubAmt", 0.75f);
                m.SetFloat("_Macro", 0.75f); m.SetFloat("_Cavity", 0.55f); m.SetFloat("_SeamDark", 0.2f);
            }
            else m.SetFloat("_CodeFX", 1f);
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
