// 저승 hell pass (11-13 night, Kido: big structures for depth from EXISTING / downloaded assets, nothing self-modelled,
// nothing small to snag on). Builds prefabs in Assets/Underworld/Resources/Hell from free third-party meshes that are
// already in the project (other stages' folders are only read, never modified):
//   Sketchfab dead trees, Sketchfab stone pagoda / Korean gate / jangseung (SnowPlague/ThirdParty/Landmarks, CC licences
//   in their LICENSE.txt), Megascans Japanese stone pillar, PolyHaven boulder, Megascans skulls.
// Every prefab gets its own charred URP Lit materials (source albedo/normal, darkened), no colliders, no shadows from the
// small ones. UWLook places them at runtime around the arena rim (UWHellSet). Sizes go to ArtSource/uw_quality/hell_props.txt.
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Underworld
{
    public static class UWHellProps
    {
        const string R = "Assets/Underworld/Resources/Hell/";
        const float TreeH = 3f;
        static readonly (string name, string fbx, Color tint, float smooth)[] Sources =
        {
            // pass 2: the Dead_Object snags are twig bushes; scaled up they read as dithered black shrubs. Tall dead conifers
            // and a Megascans dead pine trunk instead, each normalised to TreeH (UWLook scales them x5.5-7.5).
            ("DeadTree0", "Assets/Game/SnowPlague/ThirdParty/Sketchfab/Coniferous/SKF_Con_Coniferous_Dead_Tree_6.fbx", new Color(0.2f, 0.17f, 0.15f), 0.1f),
            ("DeadTree1", "Assets/Game/SnowPlague/ThirdParty/Sketchfab/Coniferous/SKF_Con_Coniferous_Dead_Tree_2_7.fbx", new Color(0.2f, 0.17f, 0.15f), 0.1f),
            ("DeadTree2", "Assets/Game/SnowPlague/ThirdParty/Megascans/dead_pine_tree_trunk_tksvbajda_mid/Dead_Pine_Tree_Trunk_tksvbajda_Mid.fbx", new Color(0.22f, 0.18f, 0.16f), 0.1f),
            ("DeadTree3", "Assets/Game/SnowPlague/ThirdParty/Sketchfab/Coniferous/SKF_Con_Coniferous_Dead_Tree_6.fbx", new Color(0.16f, 0.13f, 0.12f), 0.1f),
            ("Pagoda", "Assets/Game/SnowPlague/ThirdParty/Landmarks/PagodaGame/SKF_PagodaGame.fbx", new Color(0.6f, 0.52f, 0.46f), 0.15f),
            ("Gate", "Assets/Game/SnowPlague/ThirdParty/Landmarks/KoreanGate/KCISA_SosamMun.fbx", new Color(0.62f, 0.46f, 0.4f), 0.15f),
            ("Jangseung", "Assets/Game/SnowPlague/ThirdParty/Landmarks/Jangseung/SKF_Jangseung.fbx", new Color(0.6f, 0.5f, 0.44f), 0.1f),
            ("Pillar", "Assets/Game/SnowPlague/ThirdParty/Megascans/japanese_stone_pillar_uddocimqx_mid/Japanese_Stone_Pillar_uddocimqx_Mid.fbx", new Color(0.22f, 0.21f, 0.21f), 0.2f),
            // pass 2: the PolyHaven boulder_01 scan (25k tris of deep pits) lit from below by the lava sea glow sparkled into
            // speckled "bushes" even untextured; a smooth game-ready boulder instead
            ("Boulder", "Assets/Game/SnowPlague/ThirdParty/Sketchfab/Coniferous/SKF_Con_BoulderPlainLarge_13.fbx", new Color(0.45f, 0.38f, 0.34f), 0.15f),
            ("Skulls", "Assets/Game/AbandonedVillage/ThirdParty/Megascans/Skulls/AV_MS_Skulls.fbx", new Color(0.55f, 0.5f, 0.45f), 0.2f),
        };

        static Texture FirstTex(Material m, params string[] props)
        {
            foreach (var p in props) if (m.HasProperty(p)) { var t = m.GetTexture(p); if (t) return t; }
            return null;
        }

        [MenuItem("Tools/Underworld/Quality/Build Hell Props")]
        public static void Build()
        {
            System.IO.Directory.CreateDirectory(R);
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var sb = new System.Text.StringBuilder();
            foreach (var (name, fbx, tint, smooth) in Sources)
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
                if (!model) { sb.AppendLine($"{name}: MISSING {fbx}"); continue; }
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
                PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                var go = new GameObject(name); inst.transform.SetParent(go.transform, false);
                var made = new Dictionary<Material, Material>();
                int tris = 0, mi = 0;
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    var src = r.sharedMaterials;
                    var dst = new Material[src.Length];
                    for (int i = 0; i < src.Length; i++)
                    {
                        var s = src[i];
                        if (s != null && made.TryGetValue(s, out var have)) { dst[i] = have; continue; }
                        var path = $"{R}{name}_M{mi++}.mat";
                        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                        if (!m) { m = new Material(lit); AssetDatabase.CreateAsset(m, path); }
                        m.shader = lit;
                        var albedo = s ? FirstTex(s, "_BaseMap", "_MainTex", "_BaseColorMap", "_Albedo", "_AlbedoMap", "_Diffuse") : null;
                        var normal = s ? FirstTex(s, "_BumpMap", "_NormalMap", "_Normal") : null;
                        if (!albedo && s) foreach (var tp in s.GetTexturePropertyNames()) { var t = s.GetTexture(tp); if (t && !tp.ToLower().Contains("norm") && !tp.ToLower().Contains("bump")) { albedo = t; break; } }
                        m.SetTexture("_BaseMap", albedo); m.SetColor("_BaseColor", tint);
                        if (normal) { m.SetTexture("_BumpMap", normal); m.EnableKeyword("_NORMALMAP"); } else m.DisableKeyword("_NORMALMAP");
                        // pass 2: lit from below by the lava sea, full-strength scan normals sparkle into a dithered "bush" look
                        m.SetFloat("_BumpScale", name == "Boulder" || name == "Pillar" ? 0.35f : 0.7f);
                        // pass 4: crags standing in the lava sea read as pure black holes; a faint heat glow from their own texture
                        if (name == "Boulder") { m.EnableKeyword("_EMISSION"); m.SetTexture("_EmissionMap", albedo); m.SetColor("_EmissionColor", new Color(0.6f, 0.16f, 0.05f) * 1.1f); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None; }
                        else { m.DisableKeyword("_EMISSION"); m.SetColor("_EmissionColor", Color.black); }
                        m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", 0f);
                        bool cut = albedo && AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(albedo)) is TextureImporter ti && ti.DoesSourceTextureHaveAlpha() && ti.alphaIsTransparency;
                        m.SetFloat("_AlphaClip", cut ? 1f : 0f); m.SetFloat("_Cutoff", 0.45f);
                        if (cut) m.EnableKeyword("_ALPHATEST_ON"); else m.DisableKeyword("_ALPHATEST_ON");
                        m.enableInstancing = true;
                        EditorUtility.SetDirty(m);
                        if (s) made[s] = m;
                        dst[i] = m;
                    }
                    r.sharedMaterials = dst;
                    var mf = r.GetComponent<MeshFilter>(); if (mf && mf.sharedMesh) tris += mf.sharedMesh.triangles.Length / 3;
                    if (r is SkinnedMeshRenderer smr && smr.sharedMesh) tris += smr.sharedMesh.triangles.Length / 3;
                }
                foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
                go.transform.position = Vector3.zero;
                var b = new Bounds(Vector3.zero, Vector3.zero); bool first = true;
                foreach (var r in go.GetComponentsInChildren<Renderer>()) { if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); }
                if ((name.StartsWith("DeadTree") || name == "Boulder") && b.size.y > 0.01f)
                {
                    // normalise trees: TreeH tall, base on y = 0 (the old snags sat 1-1.5 m below their pivot); boulder ~1.6 m wide like the old one
                    float k = name == "Boulder" ? 1.6f / Mathf.Max(b.size.x, b.size.z) : TreeH / b.size.y; inst.transform.localScale *= k;
                    first = true; foreach (var r in go.GetComponentsInChildren<Renderer>()) { if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); }
                    inst.transform.position += new Vector3(-b.center.x, -b.min.y, -b.center.z);
                    first = true; foreach (var r in go.GetComponentsInChildren<Renderer>()) { if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); }
                }
                PrefabUtility.SaveAsPrefabAsset(go, R + name + ".prefab");
                Object.DestroyImmediate(go);
                sb.AppendLine($"{name}: {tris} tris, size {b.size.x:0.00} x {b.size.y:0.00} x {b.size.z:0.00}, min y {b.min.y:0.00}, centre {b.center}");
            }
            AssetDatabase.SaveAssets();
            var dir = System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, "ArtSource/uw_quality");
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "hell_props.txt"), sb.ToString());
        }
    }
}
