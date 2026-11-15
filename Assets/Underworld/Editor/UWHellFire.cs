// 저승 hell fire pass (11-14, Kido: "don't draw it yourself, download one" + "a sea of fire with burning corpses and lava").
// Builds runtime materials / prefabs from the downloaded assets in Assets/Underworld/HellFX (see CREDITS.txt there) into
// Assets/Underworld/HellFX/Resources/HellFX, which UWLook loads at play time:
//   UW_LavaField.mat  - lava sea surface (Lava Texture & Material Vol. 04 maps)
//   UW_Flame.mat      - Skulmovski flame flipbook (16 x 5), additive
//   UW_FireSmoke.mat  - Skulmovski smoke layer sprite, alpha blended
//   UW_Spark.mat      - Skulmovski spark sprite, additive
//   Corpse_*.prefab   - "Zombie: The Burnt" posed lying, charred Lit material with ember emission, no colliders, no shadows
// Writes a report to ArtSource/uw_quality/hellfx.txt.
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Underworld
{
    sealed class UWHellFxImport : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/Underworld/HellFX/Textures/")) return;
            var ti = (TextureImporter)assetImporter;
            bool normal = assetPath.EndsWith("_normal.png");
            bool linear = assetPath.EndsWith("_height.png") || assetPath.EndsWith("_roughness.png");
            bool flip = assetPath.Contains("T_FireFlipbook");
            ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            ti.sRGBTexture = !normal && !linear;
            ti.mipmapEnabled = true;
            ti.anisoLevel = flip ? 1 : 8;
            ti.wrapMode = assetPath.Contains("T_Fire") ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            ti.maxTextureSize = flip ? 4096 : 2048;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
        }

        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith("Assets/Underworld/HellFX/Models/")) return;
            var mi = (ModelImporter)assetImporter;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            mi.importAnimation = false; mi.animationType = ModelImporterAnimationType.None;
            mi.addCollider = false; mi.importCameras = false; mi.importLights = false;
            mi.meshCompression = ModelImporterMeshCompression.Medium;
        }
    }

    public static class UWHellFire
    {
        const string H = "Assets/Underworld/HellFX/";
        const string R = H + "Resources/HellFX/";

        [MenuItem("Tools/Underworld/Quality/Build Hell Fire Assets")]
        public static void Build()
        {
            System.IO.Directory.CreateDirectory(R);
            AssetDatabase.Refresh();
            var sb = new System.Text.StringBuilder();
            T Load<T>(string p) where T : Object { var a = AssetDatabase.LoadAssetAtPath<T>(p); if (!a) sb.AppendLine("MISSING " + p); return a; }
            Material Mat(string name, Shader sh)
            {
                var path = R + name + ".mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (!m) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
                m.shader = sh; return m;
            }

            // lava sea
            var lf = Shader.Find("Underworld/LavaField");
            if (lf)
            {
                var m = Mat("UW_LavaField", lf);
                m.SetTexture("_BaseMap", Load<Texture2D>(H + "Textures/T_Lava04_basecolor.png"));
                m.SetTexture("_EmisMap", Load<Texture2D>(H + "Textures/T_Lava04_emissive.png"));
                m.SetTexture("_NormalMap", Load<Texture2D>(H + "Textures/T_Lava04_normal.png"));
                m.SetTexture("_HeightMap", Load<Texture2D>(H + "Textures/T_Lava04_height.png"));
                m.SetFloat("_Tile", 7f); m.SetFloat("_AlbedoMul", 0.2f);
                m.SetColor("_EmisTint", new Color(1f, 0.5f, 0.28f)); m.SetFloat("_EmisPow", 1.0f); m.SetFloat("_EmisGain", 5f);   // pass 2: sea read black with thin lines
                m.SetColor("_MoltenCol", new Color(1f, 0.26f, 0.05f) * 1.6f); m.SetFloat("_MoltenLo", 0.7f);   // height map spans 0.58-0.89: lowest ~30% molten
                m.SetFloat("_Flow", 0.12f); m.SetFloat("_Breathe", 0.25f);
                EditorUtility.SetDirty(m);
            }
            else sb.AppendLine("MISSING shader Underworld/LavaField");

            // fire sprites (Underworld/Particle: additive premultiplies by alpha; greyscale flipbook -> alpha from red)
            var ps = Shader.Find("Underworld/Particle");
            if (ps)
            {
                var fl = Mat("UW_Flame", ps);
                fl.SetTexture("_MainTex", Load<Texture2D>(H + "Textures/T_FireFlipbook_16x5.png"));
                fl.SetColor("_Tint", Color.white * 2.2f); fl.SetFloat("_Src", (float)BlendMode.One); fl.SetFloat("_Dst", (float)BlendMode.One);
                fl.SetFloat("_SoftFade", 0.6f); fl.SetFloat("_AlphaFromRed", 1f);
                EditorUtility.SetDirty(fl);
                var sm = Mat("UW_FireSmoke", ps);
                sm.SetTexture("_MainTex", Load<Texture2D>(H + "Textures/T_FireSmoke.png"));
                sm.SetColor("_Tint", new Color(0.08f, 0.065f, 0.06f, 0.5f)); sm.SetFloat("_Src", (float)BlendMode.SrcAlpha); sm.SetFloat("_Dst", (float)BlendMode.OneMinusSrcAlpha);
                sm.SetFloat("_SoftFade", 1.5f); sm.SetFloat("_AlphaFromRed", 0f);
                EditorUtility.SetDirty(sm);
                var sp = Mat("UW_Spark", ps);
                sp.SetTexture("_MainTex", Load<Texture2D>(H + "Textures/T_FireSpark.png"));
                sp.SetColor("_Tint", new Color(1f, 0.55f, 0.2f) * 3f); sp.SetFloat("_Src", (float)BlendMode.One); sp.SetFloat("_Dst", (float)BlendMode.One);
                sp.SetFloat("_SoftFade", 0.1f); sp.SetFloat("_AlphaFromRed", 0f);
                EditorUtility.SetDirty(sp);
            }
            else sb.AppendLine("MISSING shader Underworld/Particle");

            // charred corpses
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var cm = Mat("UW_CorpseCharred", lit);
            var burn = Load<Texture2D>(H + "Textures/T_CorpseBurn.png");
            cm.SetTexture("_BaseMap", burn); cm.SetColor("_BaseColor", new Color(0.16f, 0.12f, 0.1f));
            cm.SetFloat("_Smoothness", 0.12f);
            cm.SetTexture("_EmissionMap", burn); cm.SetColor("_EmissionColor", new Color(0.55f, 0.13f, 0.03f) * 0.6f); cm.EnableKeyword("_EMISSION");
            cm.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            cm.enableInstancing = true;
            EditorUtility.SetDirty(cm);
            foreach (var n in new[] { "Corpse_Back", "Corpse_Front", "Corpse_Side" })
            {
                var model = Load<GameObject>(H + "Models/" + n + ".fbx");
                if (!model) continue;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
                var b = new Bounds(); bool first = true; int tris = 0;
                foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
                {
                    r.sharedMaterial = cm; r.shadowCastingMode = ShadowCastingMode.Off;
                    if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds);
                    var mf = r.GetComponent<MeshFilter>(); if (mf && mf.sharedMesh) tris += mf.sharedMesh.triangles.Length / 3;
                }
                foreach (var c in go.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
                PrefabUtility.SaveAsPrefabAsset(go, R + n + ".prefab");
                Object.DestroyImmediate(go);
                sb.AppendLine($"{n}: {tris} tris, bounds size {b.size} min {b.min}");
            }
            AssetDatabase.SaveAssets();
            var dir = System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, "ArtSource/uw_quality");
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "hellfx.txt"), sb.ToString());
            Debug.Log("UWHellFire: built\n" + sb);
        }
    }
}
