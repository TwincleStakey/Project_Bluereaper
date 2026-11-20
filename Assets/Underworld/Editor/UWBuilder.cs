using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Underworld.EditorTools
{
    // Builds the underworld (저승) courtyard stage: materials, layout, lights, fog, VFX, post.
    // Batch preview: Unity -batchmode -projectPath <copy> -executeMethod Underworld.EditorTools.UWBuilder.Preview
    // env UW_CAPTURE_DIR = output folder, UW_FRAMES = animation frame count (0 = stills only)
    public static class UWBuilder
    {
        public const string Root = "Assets/Underworld";
        const string ScenePath = Root + "/Scenes/Underworld.unity";

        static readonly Dictionary<string, Material> M = new();
        static Texture2D texNoise, texDot, texPuff;
        static Transform root;

        // ------------------------------------------------------------------ assets
        [MenuItem("Tools/Underworld/Build Scene")]
        public static void BuildMenu() { Build(); }

        static Texture2D SaveTex(string name, int size, Func<float, float, Color> f, bool alpha, TextureWrapMode wrap)
        {
            string path = $"{Root}/Textures/{name}.png";
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    t.SetPixel(x, y, f((x + 0.5f) / size, (y + 0.5f) / size));
            File.WriteAllBytes(path, t.EncodeToPNG());
            AssetDatabase.ImportAsset(path);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.wrapMode = wrap; imp.alphaIsTransparency = alpha; imp.sRGBTexture = !name.Contains("Noise"); imp.mipmapEnabled = true;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // periodic value-noise fbm so the texture tiles
        static float PNoise(float x, float y, int period)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float fx = x - xi, fy = y - yi;
            float H(int a, int b) { a = ((a % period) + period) % period; b = ((b % period) + period) % period; uint h = (uint)(a * 374761393 + b * 668265263); h = (h ^ (h >> 13)) * 1274126177; return (h & 0xffff) / 65535f; }
            float u = fx * fx * (3 - 2 * fx), v = fy * fy * (3 - 2 * fy);
            return Mathf.Lerp(Mathf.Lerp(H(xi, yi), H(xi + 1, yi), u), Mathf.Lerp(H(xi, yi + 1), H(xi + 1, yi + 1), u), v);
        }

        static void Textures()
        {
            Directory.CreateDirectory($"{Root}/Textures");
            texNoise = SaveTex("T_UWNoise", 256, (u, v) =>
            {
                float n = 0, a = 0.5f; int p = 4;
                for (int o = 0; o < 5; o++) { n += a * PNoise(u * p, v * p, p); p *= 2; a *= 0.5f; }
                return new Color(n, n, n, 1);
            }, false, TextureWrapMode.Repeat);
            texDot = SaveTex("T_UWDot", 64, (u, v) =>
            {
                float r = Mathf.Sqrt((u - 0.5f) * (u - 0.5f) + (v - 0.5f) * (v - 0.5f)) * 2f;
                float a = Mathf.Exp(-r * r * 5f) * Mathf.Clamp01(1 - r);
                return new Color(1, 1, 1, a);
            }, true, TextureWrapMode.Clamp);
            foreach (var p in new[] { "rock_tile_floor_nor_gl_2k.jpg", "rock_boulder_cracked_nor_gl_2k.jpg" })
            {
                var imp = (TextureImporter)AssetImporter.GetAtPath($"{Root}/Textures/{p}");
                if (imp && imp.textureType != TextureImporterType.NormalMap) { imp.textureType = TextureImporterType.NormalMap; imp.SaveAndReimport(); }
            }
            var puffImp = (TextureImporter)AssetImporter.GetAtPath($"{Root}/Textures/whitePuff03.png");
            if (puffImp) { puffImp.alphaIsTransparency = true; puffImp.wrapMode = TextureWrapMode.Clamp; puffImp.SaveAndReimport(); }
            texPuff = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Root}/Textures/whitePuff03.png");
        }

        static Texture2D T(string f) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{Root}/Textures/{f}");
        static Color Hex(string h, float mul = 1f) { ColorUtility.TryParseHtmlString(h, out var c); return new Color(c.r * mul, c.g * mul, c.b * mul, c.a); }
        static Color HexA(string h, float a) { var c = Hex(h); c.a = a; return c; }

        static Material Mat(string name, string shader, Action<Material> set)
        {
            Directory.CreateDirectory($"{Root}/Materials");
            string path = $"{Root}/Materials/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            var sh = Shader.Find(shader);
            if (!sh) throw new Exception("shader missing: " + shader);
            if (!m) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            m.shader = sh;
            set(m);
            EditorUtility.SetDirty(m);
            M[name] = m;
            return m;
        }

        static void Materials()
        {
            var topA = T("rock_tile_floor_diff_2k.jpg"); var topN = T("rock_tile_floor_nor_gl_2k.jpg");
            var rockA = T("rock_boulder_cracked_diff_2k.jpg"); var rockN = T("rock_boulder_cracked_nor_gl_2k.jpg");
            void StoneTex(Material m) { m.SetTexture("_TopTex", topA); m.SetTexture("_TopNrm", topN); m.SetTexture("_SideTex", rockA); m.SetTexture("_SideNrm", rockN); }

            Mat("UW_Floor", "Underworld/Stone", m => { StoneTex(m); m.SetFloat("_TopMode", 1); m.SetColor("_Tint", Hex("#4C4652")); m.SetColor("_SideTint", Hex("#443E50"));
                m.SetFloat("_TopTile", 3.4f); m.SetFloat("_CrackAmt", 0.55f); m.SetFloat("_WetAmt", 0.5f); m.SetFloat("_StainAmt", 0.5f); m.SetColor("_CrackCol", Hex("#9B5CFF", 2.0f)); m.SetFloat("_RimAmt", 0.05f); });
            Mat("UW_StoneProp", "Underworld/Stone", m => { StoneTex(m); m.SetFloat("_TopMode", 0); m.SetColor("_SideTint", Hex("#4E4760")); m.SetFloat("_SideTile", 2.0f); m.SetFloat("_Smooth", 0.3f); m.SetFloat("_RimAmt", 0.12f); m.SetFloat("_CrackAmt", 0); });
            Mat("UW_Rock", "Underworld/Stone", m => { StoneTex(m); m.SetFloat("_TopMode", 0); m.SetColor("_SideTint", Hex("#2E2738")); m.SetFloat("_SideTile", 4.0f); m.SetFloat("_Smooth", 0.22f); m.SetFloat("_RimAmt", 0.2f); m.SetFloat("_CrackAmt", 0); });
            Mat("UW_RedLacquer", "Underworld/Stone", m => { StoneTex(m); m.SetFloat("_TopMode", 0); m.SetColor("_SideTint", Hex("#8A2C26")); m.SetFloat("_Remap", 0.9f); m.SetFloat("_SideTile", 1.5f); m.SetFloat("_Smooth", 0.35f); m.SetFloat("_RimAmt", 0.08f); m.SetFloat("_CrackAmt", 0); });
            Mat("UW_DarkRoof", "Underworld/Stone", m => { StoneTex(m); m.SetFloat("_TopMode", 0); m.SetColor("_SideTint", Hex("#1C1820")); m.SetFloat("_Smooth", 0.4f); m.SetFloat("_RimAmt", 0.15f); m.SetFloat("_CrackAmt", 0); });
            Mat("UW_Black", "Underworld/Stone", m => { StoneTex(m); m.SetFloat("_TopMode", 0); m.SetColor("_SideTint", Hex("#050406")); m.SetFloat("_Smooth", 0.1f); m.SetFloat("_RimAmt", 0f); m.SetFloat("_CrackAmt", 0); });
            Mat("UW_Wood", "Underworld/Stone", m => { StoneTex(m); m.SetFloat("_TopMode", 0); m.SetColor("_SideTint", Hex("#3A2A26")); m.SetFloat("_SideTile", 1.2f); m.SetFloat("_Smooth", 0.15f); m.SetFloat("_RimAmt", 0.12f); m.SetFloat("_CrackAmt", 0); });
            Mat("UW_Metal", "Underworld/Stone", m => { StoneTex(m); m.SetFloat("_TopMode", 0); m.SetColor("_SideTint", Hex("#2A2830")); m.SetFloat("_Smooth", 0.65f); m.SetFloat("_RimAmt", 0.25f); m.SetFloat("_CrackAmt", 0); });
            Mat("UW_Leaf", "Underworld/Stone", m => { StoneTex(m); m.SetFloat("_TopMode", 0); m.SetColor("_SideTint", Hex("#4A1F28")); m.SetFloat("_SideTile", 1.0f); m.SetFloat("_Smooth", 0.2f); m.SetFloat("_RimAmt", 0.25f); m.SetColor("_RimCol", Hex("#B03A50")); m.SetFloat("_CrackAmt", 0); });
            Mat("UW_LanternFire", "Underworld/Emit", m => { m.SetColor("_Col", Hex("#FF7A2A", 2.6f)); m.SetFloat("_Flicker", 0.3f); });
            Mat("UW_PaperLantern", "Underworld/Emit", m => { m.SetColor("_Col", Hex("#FF3A1A", 2.2f)); m.SetFloat("_Flicker", 0.25f); });
            Mat("UW_Ghost", "Underworld/Ghost", m => { m.SetColor("_Col", HexA("#C8C2D8", 0.62f)); m.SetColor("_Rim", Hex("#B48CFF", 1.1f)); m.SetColor("_Edge", Hex("#9B4DFF", 4f)); m.SetFloat("_Height", 1.85f); });
            Mat("UW_GhostTall", "Underworld/Ghost", m => { m.SetColor("_Col", HexA("#B8B0C8", 0.55f)); m.SetColor("_Rim", Hex("#B48CFF", 1.2f)); m.SetColor("_Edge", Hex("#9B4DFF", 4f)); m.SetFloat("_Height", 2.2f); });
            Mat("UW_Portal", "Underworld/Portal", m => { m.SetTexture("_NoiseTex", texNoise); m.SetColor("_Core", Hex("#D8B8FF", 2.0f)); m.SetColor("_Mid", Hex("#7A30E0", 1.5f)); m.SetColor("_Edge", Hex("#2A0F4A")); });
            Mat("UW_FogEdge", "Underworld/FogCard", m => { m.SetTexture("_NoiseTex", texNoise); m.SetColor("_Col", HexA("#7A6C96", 1.0f)); m.SetFloat("_Scale", 0.06f); m.SetFloat("_DepthFade", 3f); });
            Mat("UW_FogVoid", "Underworld/FogCard", m => { m.SetTexture("_NoiseTex", texNoise); m.SetColor("_Col", HexA("#4A3E62", 0.95f)); m.SetFloat("_Scale", 0.035f); m.SetFloat("_DepthFade", 6f); m.SetFloat("_EdgeSoft", 0.15f); m.SetFloat("_Contrast", 0.8f); });
            Mat("UW_FogGround", "Underworld/FogCard", m => { m.SetTexture("_NoiseTex", texNoise); m.SetColor("_Col", HexA("#6A5C80", 0.5f)); m.SetFloat("_Scale", 0.07f); m.SetFloat("_DepthFade", 0.8f); m.SetFloat("_EdgeSoft", 0.25f); m.SetFloat("_Contrast", 3.2f); });
            Mat("UW_OrbViolet", "Underworld/Glow", m => { m.SetColor("_Core", Hex("#F2E6FF", 3.5f)); m.SetColor("_Halo", Hex("#9B5CFF", 2.2f)); m.SetFloat("_Size", 0.75f); m.SetFloat("_Flare", 0.08f); });
            Mat("UW_OrbSmall", "Underworld/Glow", m => { m.SetColor("_Core", Hex("#F2E6FF", 4f)); m.SetColor("_Halo", Hex("#B07CFF", 1.4f)); m.SetFloat("_Size", 0.5f); m.SetFloat("_Flare", 0.05f); });
            Mat("UW_HaloLantern", "Underworld/Glow", m => { m.SetColor("_Core", Hex("#FFB060", 1.2f)); m.SetColor("_Halo", Hex("#E0602A", 0.9f)); m.SetFloat("_Size", 2.2f); m.SetFloat("_Flare", 0f); m.SetFloat("_Bob", 0f); });
            Mat("UW_HaloPaper", "Underworld/Glow", m => { m.SetColor("_Core", Hex("#FF6A3A", 1.0f)); m.SetColor("_Halo", Hex("#D02A1A", 0.9f)); m.SetFloat("_Size", 2.0f); m.SetFloat("_Flare", 0f); m.SetFloat("_Bob", 0f); });
            Mat("UW_PtEmber", "Underworld/Particle", m => { m.SetTexture("_MainTex", texDot); m.SetColor("_Tint", Hex("#FFFFFF", 3f)); m.SetFloat("_Src", (float)BlendMode.One); m.SetFloat("_Dst", (float)BlendMode.One); m.SetFloat("_SoftFade", 0.2f); });
            Mat("UW_PtAsh", "Underworld/Particle", m => { m.SetTexture("_MainTex", texDot); m.SetColor("_Tint", Color.white); m.SetFloat("_Src", (float)BlendMode.SrcAlpha); m.SetFloat("_Dst", (float)BlendMode.OneMinusSrcAlpha); m.SetFloat("_SoftFade", 0.2f); });
            Mat("UW_PtMist", "Underworld/Particle", m => { m.SetTexture("_MainTex", texPuff); m.SetColor("_Tint", Color.white); m.SetFloat("_Src", (float)BlendMode.SrcAlpha); m.SetFloat("_Dst", (float)BlendMode.OneMinusSrcAlpha); m.SetFloat("_SoftFade", 1.2f); });
            Mat("UW_Talisman", "Underworld/Stone", m => { StoneTex(m); m.SetFloat("_TopMode", 0); m.SetColor("_SideTint", Hex("#C8B88A")); m.SetFloat("_Remap", 0.6f); m.SetFloat("_RimAmt", 0.1f); m.SetFloat("_CrackAmt", 0); m.SetColor("_Emission", Hex("#C8B88A", 0.15f)); });
            Mat("UW_SoulVeil", "Underworld/FogCard", m => { m.SetTexture("_NoiseTex", texNoise); m.SetColor("_Col", HexA("#B8A0FF", 0.8f)); m.SetFloat("_Scale", 0.22f); m.SetFloat("_Speed", 0.09f); m.SetFloat("_DepthFade", 0.4f); m.SetFloat("_EdgeSoft", 0.2f); m.SetFloat("_Contrast", 1.4f); });
            AssetDatabase.SaveAssets();
        }

        static Material SlotMat(string slot, string model)
        {
            bool ground = model is "UW_Plaza" or "UW_PlatformBack" or "UW_GateTerrace" or "UW_Steps";
            bool ghost = model.StartsWith("UW_Ghost");
            slot = slot.Split(' ')[0];
            switch (slot)
            {
                case "M_Stone": return ground ? M["UW_Floor"] : M["UW_StoneProp"];
                case "M_Rock": return ground ? M["UW_Floor"] : M["UW_Rock"];
                case "M_Red": return M["UW_RedLacquer"];
                case "M_Roof": return M["UW_DarkRoof"];
                case "M_Dark": return M["UW_Black"];
                case "M_Wood": return M["UW_Wood"];
                case "M_Metal": return M["UW_Metal"];
                case "M_Leaf": return M["UW_Leaf"];
                case "M_Emit": return M["UW_LanternFire"];
                case "M_Paper": return M["UW_PaperLantern"];
                case "M_Tal": return M["UW_Talisman"];
                case "M_Cloth": return ghost ? (model == "UW_GhostTall" ? M["UW_GhostTall"] : M["UW_Ghost"]) : M["UW_StoneProp"];
            }
            return M["UW_StoneProp"];
        }

        // ------------------------------------------------------------------ placement helpers
        static GameObject Group(string name) { var g = new GameObject(name); g.transform.SetParent(root, false); return g; }

        static GameObject Place(string model, Transform parent, Vector3 pos, float yaw = 0, float scale = 1, Vector3? euler = null)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/Models/{model}.fbx");
            if (!src) throw new Exception("model missing " + model);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = euler.HasValue ? Quaternion.Euler(euler.Value) : Quaternion.Euler(0, yaw, 0);
            go.transform.localScale = Vector3.one * scale;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = SlotMat(mats[i] ? mats[i].name : "", model);
                r.sharedMaterials = mats;
                bool trans = model.StartsWith("UW_Ghost");
                r.shadowCastingMode = trans ? ShadowCastingMode.Off : ShadowCastingMode.On;
            }
            return go;
        }

        // Stretch a model that spans its local X axis (chain, fence) between two world points.
        static GameObject Span(string model, Transform parent, Vector3 a, Vector3 b)
        {
            var go = Place(model, parent, Vector3.zero);
            var bnd = go.GetComponentInChildren<Renderer>().bounds;
            float sign = bnd.max.x > -bnd.min.x ? 1f : -1f;
            float len = sign > 0 ? bnd.max.x : -bnd.min.x;
            var d = b - a; var h = new Vector3(d.x, 0, d.z);
            var q1 = Quaternion.FromToRotation(Vector3.right * sign, h.normalized);
            var q2 = Quaternion.FromToRotation(h.normalized, d.normalized);
            go.transform.localPosition = a;
            go.transform.localRotation = q2 * q1;
            go.transform.localScale = new Vector3(d.magnitude / len, 1, 1);
            return go;
        }

        static Light PointLight(Transform parent, string name, Vector3 pos, Color c, float intensity, float range, bool shadows = false, float flicker = 0.15f)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.localPosition = pos;
            var l = go.AddComponent<Light>(); l.type = LightType.Point; l.color = c; l.intensity = intensity; l.range = range;
            l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            if (flicker > 0) { var f = go.AddComponent<UWFlicker>(); f.baseIntensity = intensity; f.amount = flicker; f.phase = pos.x * 1.7f + pos.z; }
            return l;
        }

        static GameObject Billboard(Transform parent, string name, Vector3 pos, Material m, float phase)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.localPosition = pos;
            go.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            float size = m.GetFloat("_Size"); go.transform.localScale = Vector3.one * size * 2f;
            var mpb = new MaterialPropertyBlock(); mpb.SetFloat("_Phase", phase); r.SetPropertyBlock(mpb);
            return go;
        }

        static GameObject Card(Transform parent, string name, Vector3 pos, Vector3 euler, Vector2 size, Material m)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            go.transform.localPosition = pos; go.transform.localRotation = Quaternion.Euler(euler); go.transform.localScale = new Vector3(size.x, size.y, 1);
            go.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            return go;
        }

        static ParticleSystem PS(Transform parent, string name, Vector3 pos, Material m, int max)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.localPosition = pos;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.loop = true; main.prewarm = true; main.maxParticles = max; main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World; main.startSpeed = 0;
            ps.useAutoRandomSeed = false; ps.randomSeed = (uint)Mathf.Abs(name.GetHashCode());
            var r = go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            return ps;
        }

        static ParticleSystem.MinMaxGradient Fade(Color c, float inAt = 0.15f, float outAt = 0.7f)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(c, 0), new GradientColorKey(c, 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(c.a, inAt), new GradientAlphaKey(c.a, outAt), new GradientAlphaKey(0, 1) });
            return new ParticleSystem.MinMaxGradient(g);
        }

        // ------------------------------------------------------------------ scene
        public static Camera Build()
        {
            UWClock.Manual = true;
            Textures();
            Materials();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            root = new GameObject("Underworld").transform;
            root.gameObject.AddComponent<UWClock>();

            // ground (mirror if the FBX axis conversion flipped X)
            var ground = Group("Ground");
            Place("UW_Plaza", ground.transform, Vector3.zero);
            var back = Place("UW_PlatformBack", ground.transform, Vector3.zero);
            Place("UW_GateTerrace", ground.transform, Vector3.zero);
            Place("UW_Steps", ground.transform, Vector3.zero);
            // Blender FBX import lands rotated 180 deg about Y here (x and z both negated); undo it, then mirror if still off
            if (back.GetComponentInChildren<Renderer>().bounds.center.z < 0) { ground.transform.localRotation = Quaternion.Euler(0, 180, 0); Debug.Log("[UW] rotated ground 180"); }
            if (back.GetComponentInChildren<Renderer>().bounds.center.x < 0) { ground.transform.localScale = new Vector3(-1, 1, 1); Debug.Log("[UW] mirrored ground X"); }
            var bc = back.GetComponentInChildren<Renderer>().bounds.center; Debug.Log("[UW] platform center " + bc);
            foreach (var r in ground.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.On;

            Architecture();
            Ghosts();
            Lighting();
            Fog();
            Vfx();
            var cam = CameraAndPost();
            Directory.CreateDirectory($"{Root}/Scenes");
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);
            return cam;
        }

        // Layout v3 (11-10, Kido: every arena 2x per side): 88 x 56 m plaza, was 44 x 28. The old layout points are moved
        // three ways. Edge and open-floor points spread out (M: 2x inside the old plaza; outside it they keep their
        // distance from the plaza edge, so cliff rocks and fog stay on the rim). The soul-gate terrace with its bridge (L)
        // and the portal platform (B) move as rigid groups, so they keep their size. Scattered props (rubble, ghosts,
        // motes, fog banks) are tiled 2 x 2 (T) so the open floor keeps the same density.
        const float OldHX = 22f, OldCZ = -1f, OldHZ = 14f;
        static float MapX(float x) => Mathf.Abs(x) <= OldHX ? x * 2f : x + Mathf.Sign(x) * OldHX;
        static float MapZ(float z) { float d = z - OldCZ; return OldCZ + (Mathf.Abs(d) <= OldHZ ? d * 2f : d + Mathf.Sign(d) * OldHZ); }
        static Vector3 Mv(Vector3 p) => new(MapX(p.x), p.y, MapZ(p.z));
        static Vector3 Mv(float x, float y, float z) => Mv(new Vector3(x, y, z));
        static readonly Vector3 LShift = new(-22f, 0f, -6f), BShift = new(10f, 0f, 14f);
        static Vector3 L(Vector3 p) => p + LShift;
        static Vector3 L(float x, float y, float z) => L(new Vector3(x, y, z));
        static Vector3 B(Vector3 p) => p + BShift;
        static Vector3 B(float x, float y, float z) => B(new Vector3(x, y, z));
        static readonly Vector3[] Tiles = { new(-OldHX, 0, -OldHZ), new(OldHX, 0, -OldHZ), new(-OldHX, 0, OldHZ), new(OldHX, 0, OldHZ) };
        // a tiled copy is dropped where the gate terrace or the portal platform now stands, or off the plaza
        static bool OnFloor(Vector3 p)
        {
            if (Mathf.Abs(p.x) > 42.5f || p.z < -27.5f || p.z > 25.5f) return false;
            if (p.x < -34.5f && p.z > -16.5f && p.z < -5f) return false;       // gate terrace
            if (p.x > 11f && p.x < 29f && p.z > 22f) return false;            // portal platform
            return true;
        }
        static IEnumerable<Vector3> T(Vector3 p) { foreach (var o in Tiles) { var q = p + o; if (OnFloor(q)) yield return q; } }

        static readonly Vector3 PortalC = B(10f, 5.0f, 14.3f);
        // matches the reference: soul gate stands on the back edge at the back-left, facing into the arena;
        // the bridge leaves the left balcony (terrace) out into the abyss. Main camera yaws -35 so the arena reads diagonally.
        static readonly Vector3 GateP = L(-26.0f, 0f, 6.0f), BridgeA = L(-26.0f, 0.02f, 5.0f), BridgeB = L(-26.0f, -0.28f, -25.0f);
        static float BridgeYaw => Mathf.Atan2(BridgeB.x - BridgeA.x, BridgeB.z - BridgeA.z) * Mathf.Rad2Deg;
        const float CamYaw = -35f;

        static void Architecture()
        {
            var g = Group("Architecture").transform;
            var rnd = new System.Random(3);
            float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);

            // soul gate on the left terrace, lined up with the bridge that leaves into the abyss
            Vector3 bridgeA = BridgeA, bridgeB = BridgeB;
            Place("UW_Gate", g, GateP, 180f, 1.25f);
            var gb = GameObject.CreatePrimitive(PrimitiveType.Cube); Object.DestroyImmediate(gb.GetComponent<Collider>());
            gb.name = "GateDais"; gb.transform.SetParent(g, false); gb.transform.localPosition = GateP + new Vector3(0, 0.08f, 0.3f); gb.transform.localScale = new Vector3(11f, 0.16f, 3.4f);
            gb.GetComponent<Renderer>().sharedMaterial = M["UW_Floor"];
            Span("UW_BridgeWide", g, bridgeA, bridgeB);
            for (float z = 6f; z > -22f; z -= 4.5f) Place("UW_Rock_" + rnd.Next(5), g, L(R(-28f, -24f), R(-6.5f, -4.5f), z), R(0, 360), R(1.4f, 2.0f));
            Place("UW_Statue", g, B(10f, 1.2f, 13.2f), 180);
            Place("UW_PortalFrame", g, B(10f, 1.2f, 15.6f), 180);
            Place("UW_RockPillar", g, B(3.2f, 1.0f, 16.6f), 20, 0.95f);
            Place("UW_RockPillar", g, B(20.8f, -0.5f, 13.4f), -40, 1.05f);

            var ch = Group("Chains").transform;
            Span("UW_Chain", ch, B(4.2f, 7.8f, 16.2f), B(7.2f, 8.0f, 16.3f));
            Span("UW_Chain", ch, B(4.0f, 5.4f, 16.0f), B(7.6f, 5.2f, 15.7f));
            Span("UW_Chain", ch, B(19.8f, 8.0f, 13.6f), B(13.0f, 8.6f, 15.9f));
            Span("UW_Chain", ch, B(20.0f, 5.0f, 13.4f), B(13.2f, 4.8f, 15.3f));
            Span("UW_ChainShort", ch, B(3.5f, 4.0f, 15.8f), B(1.0f, 0.1f, 12.5f));
            Span("UW_ChainShort", ch, B(20.2f, 3.0f, 12.8f), B(18.2f, 1.25f, 10.6f));

            // lanterns
            var lg = Group("Lanterns").transform;
            var fire = Hex("#FF8A3D");
            void Tall(Vector3 p, float yaw) { Place("UW_LanternTall", lg, p, yaw); PointLight(lg, "LanternLight", p + Vector3.up * 1.9f, fire, 4f, 6.5f); Billboard(lg, "LanternHalo", p + Vector3.up * 1.86f, M["UW_HaloLantern"], p.x); }
            void Low(Vector3 p, float yaw) { Place("UW_LanternLow", lg, p, yaw); PointLight(lg, "LanternLight", p + Vector3.up * 0.6f, fire, 2.2f, 4f); Billboard(lg, "LanternHalo", p + Vector3.up * 0.48f, M["UW_HaloLantern"], p.z); }
            void Pole(Vector3 p, float yaw)
            {
                var go = Place("UW_LanternPole", lg, p, yaw);
                var rb = go.GetComponentInChildren<Renderer>().bounds;
                var local = go.transform.InverseTransformPoint(rb.center);
                var arm = go.transform.TransformPoint(new Vector3(Mathf.Sign(local.x) * 1.0f, 2.4f, 0));
                PointLight(lg, "PaperLight", arm, Hex("#FF4A2A"), 3.0f, 5.5f);
                Billboard(lg, "PaperHalo", arm, M["UW_HaloPaper"], p.x + 3);
            }
            Tall(L(-31.0f, 0f, 5.0f), 0); Tall(L(-21.0f, 0f, 5.0f), 0); Tall(L(-20.8f, 0.9f, -0.6f), 0); Tall(L(-20.8f, 0.9f, -9.0f), 0);
            Tall(B(6.2f, 1.2f, 9.6f), 0); Tall(B(13.8f, 1.2f, 9.6f), 0); Tall(B(17.4f, 1.2f, 11.0f), 30);
            Tall(Mv(20.6f, 0, -3.6f), 15); Tall(Mv(-9.0f, 0, 11.6f), 0);
            Low(Mv(-6.0f, 0, -14.2f), 20); Low(L(-29.2f, 0, -3.0f), 0); Low(L(-29.2f, 0, -13.0f), 0); Low(Mv(14.6f, 0, -14.2f), 40);
            Low(Mv(21.0f, 0, 5.0f), 0); Low(L(-12.6f, 0, -1.2f), 0); Low(L(-12.6f, 0, 6.4f), 0); Low(B(2.0f, 0, 12.2f), 0);
            Pole(L(-15.0f, 0.9f, -0.2f), 90); Pole(Mv(21.2f, 0, -9.6f), 180); Pole(L(-29.4f, 0, -8.0f), 0); Pole(L(-29.4f, 0, 2.0f), 0); Pole(Mv(-4.0f, 0, 12.5f), 90);
            // the longer rim gets more light: lanterns spaced along the front, right and back edges
            Tall(new Vector3(42.0f, 0, 14.0f), 0); Tall(new Vector3(-40.5f, 0, 24.0f), 0); Tall(new Vector3(-20.0f, 0, -27.0f), 10); Tall(new Vector3(30.0f, 0, -27.2f), 20);
            Low(new Vector3(-30.0f, 0, -27.6f), 0); Low(new Vector3(8.0f, 0, -27.8f), 30); Low(new Vector3(42.4f, 0, -16.0f), 0); Low(new Vector3(-30.0f, 0, 25.4f), 0); Low(new Vector3(4.0f, 0, 25.6f), 0);
            Pole(new Vector3(42.6f, 0, 2.0f), 180); Pole(new Vector3(-12.0f, 0, -27.6f), 90); Pole(new Vector3(-24.0f, 0, 25.6f), 90);

            // walls / balustrades
            var wg = Group("Walls").transform;
            int wi = 0;
            void Wall(Vector3 p, float yaw) { Place("UW_Wall_" + (wi++ % 3), wg, p, yaw); }

            Wall(L(-21.2f, 0.9f, 0.2f), 0); Wall(L(-16.4f, 0.9f, 0.2f), 0);
            Wall(L(-20.6f, 0.9f, -9.7f), 180); Wall(L(-16.0f, 0.9f, -9.7f), 180);
            Wall(B(3.2f, 1.2f, 9.25f), 0); Wall(B(5.2f, 1.2f, 9.25f), 180); Wall(B(15.2f, 1.2f, 9.25f), 0); Wall(B(17.2f, 1.2f, 9.25f), 180);
            Wall(B(1.8f, 1.2f, 11.2f), 90); Wall(B(1.7f, 1.2f, 13.6f), 90);
            // broken balustrade along the right rim, gaps for the lanterns and the poles
            for (float z = -26f; z < 22f; z += 2.4f) if (Mathf.Abs(z + 16f) > 1.6f && Mathf.Abs(z - 2f) > 1.6f && Mathf.Abs(z - 14f) > 1.4f && Mathf.Abs(z - 9f) > 1.2f && Mathf.Abs(z + 7.2f) > 1.2f && rnd.NextDouble() > 0.22) Wall(new Vector3(43.4f, 0, z), -90);
            // front rim: runs of balustrade with gaps (the front edge stays readable from the camera)
            for (float x = 4f; x < 40f; x += 2.3f) if (Mathf.Abs(x - 8f) > 1.4f && Mathf.Abs(x - 30f) > 1.4f && Mathf.Abs(x - 29.2f) > 1.2f && Mathf.Sin(x * 0.45f) > -0.35f) Wall(new Vector3(x, 0, -28.4f), 180);
            Wall(new Vector3(-26.0f, 0, -28.4f), 180); Wall(new Vector3(-23.8f, 0, -28.4f), 180);
            // back rim, left of the portal platform
            for (float x = -32f; x < 8f; x += 2.3f) if (Mathf.Abs(x + 24f) > 1.4f && Mathf.Abs(x - 4f) > 1.4f && Mathf.Abs(x + 30f) > 1.4f && Mathf.Abs(x + 18f) > 1.2f && Mathf.Abs(x + 8f) > 1.4f && Mathf.Sin(x * 0.5f + 1f) > -0.4f) Wall(new Vector3(x, 0, 26.2f), 0);

            var fg = Group("Fences").transform;
            // fence model is about 8.6 m long: two runs instead of one stretched one
            Span("UW_Fence", fg, new Vector3(-34.8f, 0, -28.2f), new Vector3(-26.2f, 0, -28.2f));
            Span("UW_Fence", fg, new Vector3(-22.4f, 0, -28.2f), new Vector3(-13.8f, 0, -28.2f));
            Span("UW_Fence", fg, new Vector3(-43.2f, 0, -20.0f), new Vector3(-39.6f, 0, -23.6f));

            // cliffs and background rock, all the way round the bigger plaza
            var rg = Group("Rocks").transform;
            void Rock(Vector3 p, float s) => Place("UW_Rock_" + rnd.Next(5), rg, p, R(0, 360), s, new Vector3(R(-15, 15), R(0, 360), R(-15, 15)));
            for (float z = -2; z < 27; z += 3.2f) Rock(new Vector3(R(-46.5f, -45f), R(-7f, -5.5f), z), R(1.6f, 2.6f));
            for (float z = -21; z < -16; z += 3.4f) Rock(new Vector3(R(-45.6f, -44.8f), R(-6.5f, -5f), z), R(1.5f, 2.2f));
            for (float x = -40; x < 44; x += 3.6f) Rock(new Vector3(x, R(-3.6f, -2f), R(-31f, -30f)), R(1.6f, 2.4f));
            for (float z = -27; z < 23; z += 3.4f) Rock(new Vector3(R(45.2f, 46.5f), R(-3f, -1.2f), z), R(1.5f, 2.4f));
            for (float x = -42; x < 46; x += 4.2f) Rock(new Vector3(x, R(-2f, 1.5f), R(32.5f, 35f)), R(2.2f, 3.6f));
            Rock(B(0.5f, 0.6f, 17.4f), 1.6f); Rock(B(18.6f, 1.0f, 17.0f), 2.0f); Rock(Mv(-6.5f, 0.3f, 14.4f), 1.6f); Rock(Mv(-17f, 0.2f, 12.6f), 1.8f);
            Rock(new Vector3(-36f, 0.2f, 27.2f), 1.7f); Rock(new Vector3(38f, 0.3f, 26.4f), 1.9f); Rock(new Vector3(-6f, 0.1f, 27.6f), 1.5f);

            var dg = Group("Debris").transform;
            Vector3[] rub = { new(-8, 0, -3), new(4, 0, -7), new(14, 0, 4), new(-16, 0, -12), new(9, 0, -13), new(-3, 0, 7), new(17f, 0, -10), new(-11f, 0, -6f), new(6f, 0, 4.4f), new(-1f, 0, -12.8f), new(19f, 0, 1f) };
            int ri = 0;
            foreach (var p0 in rub) foreach (var p in T(p0)) Place("UW_Rubble_" + (ri++ % 4), dg, p + new Vector3(R(-1.5f, 1.5f), 0, R(-1.5f, 1.5f)), R(0, 360), R(0.8f, 1.3f));
            Place("UW_Rubble_1", dg, L(-18.4f, 0.9f, -2.0f), R(0, 360), R(0.8f, 1.3f));

            var vg = Group("Vegetation").transform;
            Place("UW_DeadTree_2", vg, L(-21.0f, 0.9f, 6.6f), 30, 1.15f);
            Place("UW_DeadTree_0", vg, Mv(-21.2f, 0, -12.6f), 100, 1.0f);
            Place("UW_DeadTree_1", vg, Mv(21.4f, 0, -12.8f), 200, 1.1f);
            Place("UW_DeadTree_2", vg, B(0.6f, 1.2f, 16.6f), 300, 1.2f);
            Place("UW_DeadTree_0", vg, B(19.6f, 1.2f, 16.2f), 40, 1.3f);
            Place("UW_DeadTree_1", vg, Mv(-19.5f, 0, 11.6f), 140, 1.0f);
            Place("UW_DeadTree_2", vg, new Vector3(42.6f, 0, 20.0f), 250, 1.1f);
            Place("UW_DeadTree_0", vg, new Vector3(2.0f, 0, -27.8f), 60, 1.05f);
            Place("UW_DeadTree_1", vg, new Vector3(-42.0f, 0, 12.0f), 170, 1.15f);
            Vector3[] sh = { new(-19.2f, 0, -13.6f), new(-12.5f, 0, -14.9f), new(18.0f, 0, -14.3f), new(21.6f, 0, 2.2f), new(-21.6f, 0, -6.2f), new(-1.5f, 0, -15.1f),
                new(-19.0f, 0f, 9.0f), new(21.5f, 0, 7.8f), new(4.5f, 0, -15.0f), new(-8.0f, 0, 12.8f), new(-16.4f, 0, 12.2f) };
            Vector3[] shMore = { new(-42.6f, 0, 4f), new(-42.8f, 0, 18f), new(42.8f, 0, -8f), new(42.6f, 0, 10f), new(-24f, 0, -28.6f), new(20f, 0, -28.8f),
                new(-36f, 0, 26f), new(-12f, 0, 26.4f), new(38f, 0, -26.6f), new(42f, 0, -24f) };
            int si = 0;
            foreach (var p in sh) Place("UW_Shrub_" + (si++ % 3), vg, Mv(p), R(0, 360), R(0.9f, 1.5f));
            foreach (var p in shMore) Place("UW_Shrub_" + (si++ % 3), vg, p, R(0, 360), R(0.9f, 1.5f));
            Place("UW_Shrub_1", vg, B(1.0f, 1.2f, 16.0f), R(0, 360), R(0.9f, 1.5f));

            var fl = Group("FloatingRuins").transform;
            void Pag(Vector3 p, float s, float ph) { var go = Place("UW_Pagoda", fl, p, R(0, 360), s); var f = go.AddComponent<UWFloat>(); f.bob = 0.4f; f.spin = 2f; f.phase = ph; f.swayDeg = 2f; }
            Pag(Mv(-31f, -5f, 9f), 1.5f, 0); Pag(L(-29f, -9f, -14f), 1.1f, 1.3f); Pag(Mv(-36f, 1f, 18f), 1.6f, 2.1f);
            Pag(Mv(31f, -3f, 6f), 1.3f, 0.7f); Pag(Mv(28f, -9f, -12f), 1.0f, 2.9f);
            Pag(new Vector3(-20f, -6f, -42f), 1.2f, 1.7f); Pag(new Vector3(24f, -4f, -40f), 1.4f, 0.4f); Pag(new Vector3(56f, -2f, -24f), 1.1f, 2.4f);

            var og = Group("OrbPosts").transform;
            foreach (var p in OrbPosts)
            {
                Place("UW_LanternLow", og, p, 45, 1.25f);
                var post = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Object.DestroyImmediate(post.GetComponent<Collider>());
                post.transform.SetParent(og, false); post.transform.localPosition = p + Vector3.up * 1.6f; post.transform.localScale = new Vector3(0.22f, 0.5f, 0.22f);
                post.GetComponent<Renderer>().sharedMaterial = M["UW_StoneProp"];
            }
        }
        static readonly Vector3[] OrbPosts = { Mv(16.4f, 0, -0.8f), B(17.0f, 1.2f, 14.0f), Mv(-1.0f, 0f, 10.4f), Mv(-6.0f, 0, -9.0f), new(-24f, 0, 4f), new(14f, 0, -18f) };

        static void Ghosts()
        {
            var g = Group("Ghosts").transform;
            var rnd = new System.Random(11);
            (Vector3 p, float yaw, bool tall)[] gs =
            {
                (new(-12.5f, 0, 2.0f), 150, false), (new(6.5f, 0, 6.0f), 200, true), (new(16.0f, 0, 2.2f), 230, false), (new(-7.5f, 0, -8.5f), 170, false),
                (new(1.5f, 0, -11.5f), 190, true), (new(17.0f, 0, -7.0f), 220, false), (new(-3.5f, 0, 3.5f), 175, false), (new(12.5f, 0, 6.8f), 210, false),
                (new(-14.0f, 0, -9.0f), 160, false), (new(9.0f, 0, -4.0f), 200, true),
            };
            foreach (var (p0, yaw, tall) in gs) foreach (var p in T(p0)) Ghost(g, p, yaw + (float)rnd.NextDouble() * 30f - 15f, tall, (float)rnd.NextDouble() * 10f);
            Ghost(g, L(-16.4f, 0.9f, -6.0f), 120, true, (float)rnd.NextDouble() * 10f);
            Ghost(g, B(6.0f, 1.2f, 12.0f), 180, false, (float)rnd.NextDouble() * 10f);
            // a procession of souls crossing the bridge toward the gate
            Vector3 a = BridgeA, b = BridgeB;
            float yawIn = Mathf.Atan2(a.x - b.x, a.z - b.z) * Mathf.Rad2Deg;
            for (int i = 0; i < 4; i++)
            {
                var p = Vector3.Lerp(a, b, 0.18f + i * 0.17f) + Vector3.up * 0.15f + Vector3.Cross(b - a, Vector3.up).normalized * (i % 2 == 0 ? 0.4f : -0.4f);
                Ghost(g, p, yawIn, i % 2 == 1, i * 2.1f);
            }
        }

        static void Ghost(Transform g, Vector3 p, float yaw, bool tall, float ph)
        {
            var go = Place(tall ? "UW_GhostTall" : "UW_Ghost", g, p + Vector3.up * 0.15f, yaw, 1.35f);
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                var mpb = new MaterialPropertyBlock(); mpb.SetFloat("_Phase", ph); r.SetPropertyBlock(mpb);
            }
        }

        static void Lighting()
        {
            var g = Group("Lighting").transform;
            var moon = new GameObject("Moon"); moon.transform.SetParent(g, false);
            var l = moon.AddComponent<Light>(); l.type = LightType.Directional; l.color = Hex("#A9A2C4"); l.intensity = 1.1f;
            l.shadows = LightShadows.Soft; l.shadowStrength = 0.75f; moon.transform.rotation = Quaternion.Euler(48, 155, 0);
            RenderSettings.sun = l;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Hex("#5E5872");
            RenderSettings.ambientEquatorColor = Hex("#443E50");
            RenderSettings.ambientGroundColor = Hex("#1E1826");
            var sh = new SphericalHarmonicsL2(); sh.AddAmbientLight(Hex("#443E50")); sh.AddDirectionalLight(Vector3.up, Hex("#2E2A3A"), 1f); RenderSettings.ambientProbe = sh;
            RenderSettings.skybox = null;
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = Hex("#251E33"); RenderSettings.fogDensity = 0.011f;
            DynamicGI.UpdateEnvironment();

            PointLight(g, "PortalKey", PortalC + new Vector3(0, 0, -3.9f), Hex("#9B5CFF"), 16f, 18f, true, 0.1f);
            PointLight(g, "PortalBack", PortalC + new Vector3(0, 0, -0.6f), Hex("#B07CFF"), 8f, 9f, false, 0.1f);
            PointLight(g, "SoulGateLight", GateP + new Vector3(0, 3.0f, -1.6f), Hex("#9C80FF"), 14f, 15f, false, 0.12f);
            PointLight(g, "GateWarmFill", GateP + new Vector3(0, 2.2f, -5.0f), Hex("#FFB070"), 5f, 11f, false, 0.08f);
            PointLight(g, "BridgeFillA", Vector3.Lerp(BridgeA, BridgeB, 0.35f) + Vector3.up * 3f, Hex("#B49CFF"), 5f, 12f, false, 0.06f);
            PointLight(g, "BridgeFillB", Vector3.Lerp(BridgeA, BridgeB, 0.7f) + Vector3.up * 3f, Hex("#B49CFF"), 4f, 12f, false, 0.06f);
        }

        static void Fog()
        {
            var g = Group("FogCards").transform;
            Card(g, "VoidFog_A", new Vector3(0, -3.0f, 0), new Vector3(90, 0, 0), new Vector2(300, 300), M["UW_FogVoid"]);
            Card(g, "VoidFog_B", new Vector3(0, -7.0f, 0), new Vector3(90, 0, 0), new Vector2(320, 320), M["UW_FogVoid"]);
            Card(g, "EdgeFog_L1", Mv(-34.0f, -1.0f, -6), new Vector3(10, 75, 0), new Vector2(48, 10), M["UW_FogEdge"]);
            Card(g, "EdgeFog_L2", Mv(-30.0f, 0.0f, 0), new Vector3(10, 62, 0), new Vector2(60, 12), M["UW_FogEdge"]);
            Card(g, "EdgeFog_F1", Mv(-10.0f, -1.2f, -18.0f), new Vector3(25, 0, 0), new Vector2(64, 8), M["UW_FogEdge"]);
            Card(g, "EdgeFog_F2", Mv(12.0f, -1.6f, -18.5f), new Vector3(25, 0, 0), new Vector2(64, 8), M["UW_FogEdge"]);
            Card(g, "EdgeFog_R1", Mv(25.0f, -1.0f, 0), new Vector3(10, -75, 0), new Vector2(72, 9), M["UW_FogEdge"]);
            Card(g, "EdgeFog_B1", Mv(4.0f, 0.0f, 21.5f), new Vector3(-10, 0, 0), new Vector2(120, 12), M["UW_FogEdge"]);
            void Rim(string n, Vector3 p, float yaw, float w) => Card(g, n, p, new Vector3(-18, yaw, 0), new Vector2(w, 7), M["UW_FogEdge"]);
            for (float z = -34; z <= 22; z += 7.5f) Rim("RimFog_L", new Vector3(-53.0f, -1.6f, z), -90, 10);
            for (float x = -32; x <= 42; x += 8f) Rim("RimFog_F", new Vector3(x, -1.6f, -31.2f), 180, 11);
            for (float z = -24; z <= 22; z += 7.5f) Rim("RimFog_R", new Vector3(46.2f, -1.6f, z), 90, 10);
            for (float x = -38; x <= 8; x += 8f) Rim("RimFog_B", new Vector3(x, -1.6f, 29.2f), 0, 11);
            Card(g, "GroundMist_A", Mv(-6.0f, 0.35f, -4.0f), new Vector3(90, 0, 0), new Vector2(68, 44), M["UW_FogGround"]);
            Card(g, "GroundMist_B", Mv(9.0f, 0.55f, 2.0f), new Vector3(90, 20, 0), new Vector2(56, 40), M["UW_FogGround"]);
            Card(g, "GroundMist_C", L(-18.0f, 1.2f, -4.5f), new Vector3(90, 0, 0), new Vector2(10, 12), M["UW_FogGround"]);
            // soul veil hanging inside the gate opening, plus mist swallowing the far end of the bridge
            Card(g, "SoulVeil", GateP + new Vector3(0, 3.4f, 0), new Vector3(0, 0, 0), new Vector2(6.8f, 6.4f), M["UW_SoulVeil"]);
            Card(g, "GateBackFog", GateP + new Vector3(0, 0.5f, 4.5f), new Vector3(-10, 0, 0), new Vector2(22, 9), M["UW_FogEdge"]);
            Card(g, "BridgeFog", Vector3.Lerp(BridgeA, BridgeB, 0.85f) + Vector3.up * 1.0f, new Vector3(0, BridgeYaw, 0), new Vector2(12, 8), M["UW_FogEdge"]);
        }

        static void Vfx()
        {
            var g = Group("VFX").transform;

            Card(g, "PortalVortex", PortalC + new Vector3(0, 0, 0), new Vector3(12, 0, 0), new Vector2(9.5f, 9.5f), M["UW_Portal"]);
            var sp = PS(g, "PortalSpiral", PortalC + new Vector3(0, 0, -0.2f), M["UW_PtEmber"], 120);
            { var main = sp.main; main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 1.8f); main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.14f);
              main.startColor = Hex("#D9B3FF", 1.5f); main.simulationSpace = ParticleSystemSimulationSpace.Local;
              var em = sp.emission; em.rateOverTime = 40;
              var shp = sp.shape; shp.shapeType = ParticleSystemShapeType.Circle; shp.radius = 4.0f; shp.radiusThickness = 0f;
              var vel = sp.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.Local; vel.radial = -1.8f; vel.orbitalZ = 1.6f; vel.x = 0; vel.y = 0; vel.z = 0;
              var col = sp.colorOverLifetime; col.enabled = true; col.color = Fade(Hex("#D9B3FF"), 0.2f, 0.6f);
              sp.transform.localRotation = Quaternion.Euler(10, 0, 0); }

            // soul wisps drifting up the bridge and in through the gate
            Vector3 a = BridgeA + Vector3.up * 0.6f, b = BridgeB + Vector3.up * 0.6f;
            var souls = PS(g, "SoulStream", b, M["UW_PtEmber"], 120);
            { var main = souls.main; main.startLifetime = new ParticleSystem.MinMaxCurve(7f, 9f); main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.22f); main.startColor = Hex("#C8A8FF", 1.6f);
              var em = souls.emission; em.rateOverTime = 10;
              var shp = souls.shape; shp.shapeType = ParticleSystemShapeType.Sphere; shp.radius = 1.4f;
              var d = (a - b) / 8f;
              var vel = souls.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World; vel.x = d.x; vel.y = d.y + 0.05f; vel.z = d.z;
              var nz = souls.noise; nz.enabled = true; nz.strength = 0.35f; nz.frequency = 0.4f;
              var col = souls.colorOverLifetime; col.enabled = true; col.color = Fade(Hex("#C8A8FF"), 0.15f, 0.8f); }
            Billboard(g, "SoulGateGlow", GateP + new Vector3(0, 2.6f, 0), M["UW_OrbSmall"], 4.2f);

            var og = Group("Orbs").transform;
            // hero orbs sit on the orb posts and light the floor; the loose ones are tiled over the plaza (no lights)
            for (int i = 0; i < OrbPosts.Length; i++)
            {
                var p = OrbPosts[i] + Vector3.up * 2.35f;
                Billboard(og, "Orb", p, M["UW_OrbViolet"], i * 1.7f);
                PointLight(og, "OrbLight", p, Hex("#A060FF"), 3.0f, 5.5f, false, 0.08f);
            }
            Vector3[] orbs = { new(14.0f, 2.9f, 9.8f), new(4.6f, 2.2f, 2.2f), new(-4.4f, 2.5f, -2.4f), new(9.0f, 1.9f, -6.4f), new(-11.0f, 2.3f, 4.6f), new(18.6f, 2.4f, 6.0f), new(-15.0f, 2.0f, -11.0f), new(0.0f, 2.8f, -6.0f), new(11.0f, 2.2f, -11.5f), new(-9.5f, 3.0f, 9.5f) };
            int oi = 0;
            foreach (var p0 in orbs) foreach (var p in T(p0)) Billboard(og, "Orb", p, M["UW_OrbSmall"], (oi++) * 1.7f);
            var motes = new Vector3[] { new(-1, 1.6f, 6), new(3.5f, 1.2f, -3), new(11.5f, 1.4f, -2), new(-9, 1.7f, -5), new(7.5f, 2.6f, 8.5f), new(-14, 2.2f, -8), new(14, 1.3f, -10), new(0, 2.1f, -13), new(-18, 2.4f, -4), new(20, 1.8f, -6) };
            int mi = 0;
            foreach (var p0 in motes) foreach (var p in T(p0)) Billboard(og, "Mote", p, M["UW_OrbSmall"], 20 + (mi++) * 2.3f);

            var emb = PS(g, "EmbersViolet", new Vector3(0, 0.4f, -1), M["UW_PtEmber"], 2400);
            { var main = emb.main; main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 7f); main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.1f); main.startColor = Hex("#C08BFF", 1.8f);
              var em = emb.emission; em.rateOverTime = 320;
              var shp = emb.shape; shp.shapeType = ParticleSystemShapeType.Box; shp.scale = new Vector3(92, 0.5f, 60);
              var vel = emb.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World; vel.x = new ParticleSystem.MinMaxCurve(-0.1f, 0.1f); vel.y = new ParticleSystem.MinMaxCurve(0.25f, 0.6f); vel.z = new ParticleSystem.MinMaxCurve(-0.1f, 0.1f);
              var nz = emb.noise; nz.enabled = true; nz.strength = 0.3f; nz.frequency = 0.5f;
              var col = emb.colorOverLifetime; col.enabled = true; col.color = Fade(Hex("#C08BFF")); }
            var red = PS(g, "EmbersRed", new Vector3(0, 0.4f, -1), M["UW_PtEmber"], 480);
            { var main = red.main; main.startLifetime = new ParticleSystem.MinMaxCurve(3f, 5f); main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.08f); main.startColor = Hex("#FF5A2A", 2.2f);
              var em = red.emission; em.rateOverTime = 56;
              var shp = red.shape; shp.shapeType = ParticleSystemShapeType.Box; shp.scale = new Vector3(92, 0.5f, 60);
              var vel = red.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World; vel.x = new ParticleSystem.MinMaxCurve(-0.1f, 0.1f); vel.y = new ParticleSystem.MinMaxCurve(0.4f, 0.9f); vel.z = new ParticleSystem.MinMaxCurve(-0.1f, 0.1f);
              var nz = red.noise; nz.enabled = true; nz.strength = 0.5f; nz.frequency = 0.8f;
              var col = red.colorOverLifetime; col.enabled = true; col.color = Fade(Hex("#FF5A2A")); }

            var ash = PS(g, "Ash", new Vector3(0, 11f, 0), M["UW_PtAsh"], 1200);
            { var main = ash.main; main.startLifetime = new ParticleSystem.MinMaxCurve(9f, 12f); main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.07f); main.startColor = HexA("#8A8296", 0.7f); main.gravityModifier = 0.012f;
              var em = ash.emission; em.rateOverTime = 112;
              var shp = ash.shape; shp.shapeType = ParticleSystemShapeType.Box; shp.scale = new Vector3(100, 1, 72);
              var nz = ash.noise; nz.enabled = true; nz.strength = 0.4f; nz.frequency = 0.3f;
              var col = ash.colorOverLifetime; col.enabled = true; col.color = Fade(HexA("#8A8296", 0.7f), 0.1f, 0.8f); }

            void Mist(string name, Vector3 pos, Vector3 box, float rate, Vector2 size, Color c, bool flat, int max = 80)
            {
                var ps = PS(g, name, pos, M["UW_PtMist"], max);
                var main = ps.main; main.startLifetime = new ParticleSystem.MinMaxCurve(12f, 18f); main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y); main.startColor = c;
                main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
                var em = ps.emission; em.rateOverTime = rate;
                var shp = ps.shape; shp.shapeType = ParticleSystemShapeType.Box; shp.scale = box;
                var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World; vel.x = new ParticleSystem.MinMaxCurve(0.1f, 0.3f); vel.y = new ParticleSystem.MinMaxCurve(0f, 0f); vel.z = new ParticleSystem.MinMaxCurve(-0.05f, 0.1f);
                var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.12f, 0.12f);
                var col = ps.colorOverLifetime; col.enabled = true; col.color = Fade(c, 0.25f, 0.65f);
                if (flat) ps.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            }
            Mist("GroundMist", new Vector3(0, 0.6f, -1), new Vector3(84, 0.4f, 52), 36f, new Vector2(6, 11), HexA("#5A4C6C", 0.3f), true, 320);
            // patchy fog banks: clumps in corners, between props and low spots, leaving gaps so detail stays readable
            Vector3[] banks = { new(-14, 0.6f, -6), new(-6, 0.6f, 8), new(4, 0.6f, -10), new(13, 0.6f, -2), new(18, 0.8f, 8), new(-19, 0.6f, -14),
                new(1, 0.6f, 3), new(17, 0.6f, -13), new(-9, 0.6f, -12), new(20, 0.6f, 2) };
            int bi = 0;
            foreach (var p0 in banks) foreach (var p in T(p0)) { Mist("FogBank", p, new Vector3(5f, 1.0f, 5f), 1.6f, new Vector2(6, 10f), HexA(bi % 2 == 0 ? "#7A6E90" : "#685E7C", 0.5f), false); bi++; }
            Mist("FogBank", B(8, 1.6f, 10.5f), new Vector3(5f, 1.0f, 5f), 1.6f, new Vector2(6, 10f), HexA("#7A6E90", 0.5f), false);
            Mist("FogBank", L(-26, 0.6f, -5), new Vector3(5f, 1.0f, 5f), 1.6f, new Vector2(6, 10f), HexA("#685E7C", 0.5f), false);
            Mist("FogBank", L(-26, 0.4f, -19), new Vector3(5f, 1.0f, 5f), 1.6f, new Vector2(6, 10f), HexA("#7A6E90", 0.5f), false);
            Mist("BridgeGroundMist", L(-26, 0.5f, -10), new Vector3(7, 0.4f, 30), 4f, new Vector2(5, 9), HexA("#6A5C80", 0.32f), true);
            Mist("GateMist", GateP + new Vector3(0, 0.8f, -1.5f), new Vector3(10, 1.2f, 3), 3f, new Vector2(4, 7), HexA("#8A78B0", 0.35f), false);
            Mist("EdgeSpillLeft", Mv(-35, -0.5f, -4), new Vector3(4, 2, 36), 6f, new Vector2(7, 12), HexA("#4B3A5E", 0.4f), false, 160);
            Mist("EdgeSpillFront", Mv(-8, -0.8f, -17), new Vector3(56, 2, 3), 6f, new Vector2(7, 11), HexA("#4B3A5E", 0.35f), false, 160);
            Mist("RimSpillL", Mv(-31.0f, -0.6f, -6), new Vector3(2, 1.5f, 36), 8f, new Vector2(5, 9), HexA("#7A6C96", 0.45f), false, 160);
            Mist("RimSpillF", Mv(2, -0.6f, -16.5f), new Vector3(80, 1.5f, 2), 14f, new Vector2(5, 9), HexA("#7A6C96", 0.42f), false, 160);
            Mist("RimSpillR", Mv(23.5f, -0.6f, -1), new Vector3(2, 1.5f, 48), 10f, new Vector2(5, 9), HexA("#7A6C96", 0.42f), false, 160);
            Mist("RimSpillB", Mv(-8, -0.4f, 14.5f), new Vector3(40, 1.5f, 2), 6f, new Vector2(5, 9), HexA("#7A6C96", 0.4f), false, 160);
            Mist("BridgeMist", L(-26, -1.2f, -24f), new Vector3(10, 2, 6), 3f, new Vector2(6, 10), HexA("#5A4878", 0.4f), false);
            Mist("PortalHaze", PortalC + new Vector3(0, -3f, -2.8f), new Vector3(9, 1.5f, 3), 2f, new Vector2(4, 7), HexA("#6A3A9A", 0.3f), false);
        }

        static Camera CameraAndPost()
        {
            var camGo = new GameObject("Main Camera"); camGo.tag = "MainCamera";
            camGo.transform.SetParent(root, false);
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 44; cam.nearClipPlane = 0.5f; cam.farClipPlane = 200; cam.allowHDR = true;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Hex("#0E0C14");
            var acd = cam.GetUniversalAdditionalCameraData(); acd.renderPostProcessing = true; acd.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            acd.antialiasingQuality = AntialiasingQuality.High;
            Aim(cam, new Vector3(-4.5f, 0, 3.5f), 60, 52, CamYaw);

            var volGo = new GameObject("PostVolume"); volGo.transform.SetParent(root, false);
            var vol = volGo.AddComponent<Volume>(); vol.isGlobal = true;
            string profPath = $"{Root}/Settings/UW_PostProfile.asset";
            Directory.CreateDirectory($"{Root}/Settings");
            AssetDatabase.DeleteAsset(profPath);
            var prof = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(prof, profPath);
            T Add<T>() where T : VolumeComponent { var c = prof.Add<T>(true); c.name = typeof(T).Name; AssetDatabase.AddObjectToAsset(c, prof); return c; }
            var tm = Add<Tonemapping>(); tm.mode.Override(TonemappingMode.ACES);
            var bloom = Add<Bloom>(); bloom.threshold.Override(0.9f); bloom.intensity.Override(1.1f); bloom.scatter.Override(0.72f); bloom.tint.Override(Hex("#D8C0FF")); bloom.highQualityFiltering.Override(true);
            var ca = Add<ColorAdjustments>(); ca.postExposure.Override(0.9f); ca.contrast.Override(16f); ca.colorFilter.Override(Hex("#F2EAFF")); ca.hueShift.Override(-3f); ca.saturation.Override(-16f);
            var wb = Add<WhiteBalance>(); wb.temperature.Override(0f); wb.tint.Override(4f);
            var st = Add<SplitToning>(); st.shadows.Override(Hex("#2A1450")); st.highlights.Override(Hex("#B8789A")); st.balance.Override(-20f);
            var vg = Add<Vignette>(); vg.color.Override(Hex("#0B0414")); vg.intensity.Override(0.38f); vg.smoothness.Override(0.45f);
            var fg = Add<FilmGrain>(); fg.type.Override(FilmGrainLookup.Medium1); fg.intensity.Override(0.15f); fg.response.Override(0.8f);
            var chr = Add<ChromaticAberration>(); chr.intensity.Override(0.06f);
            Add<LiftGammaGain>();
            EditorUtility.SetDirty(prof); AssetDatabase.SaveAssets();
            vol.sharedProfile = prof;
            var br = root.gameObject.AddComponent<UWBrightness>(); br.volume = vol; br.moon = RenderSettings.sun; br.Apply();
            var env = root.gameObject.AddComponent<UWEnvironment>(); env.volume = vol;
            UWEnvironment.FogMat FM(string n, string grey) { var m = M[n]; return new UWEnvironment.FogMat { mat = m, violet = m.GetColor("_Col"), grey = HexA(grey, m.GetColor("_Col").a) }; }
            env.fogMaterials = new[] { FM("UW_FogEdge", "#8C8A92"), FM("UW_FogVoid", "#4E4C56"), FM("UW_FogGround", "#7C7A84"), FM("UW_SoulVeil", "#A49CBC") };
            env.fogDensity = RenderSettings.fogDensity; env.fogViolet = RenderSettings.fogColor;
            env.haze = 0.3f;
            env.Apply();
            return cam;
        }

        static void Aim(Camera cam, Vector3 target, float dist, float pitch, float yaw)
        {
            var rot = Quaternion.Euler(pitch, yaw, 0);
            cam.transform.SetPositionAndRotation(target - rot * Vector3.forward * dist, rot);
        }

        // ------------------------------------------------------------------ preview
        public static void Preview()
        {
            try { PreviewInner(); EditorApplication.Exit(0); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        static void SimAll(float t)
        {
            UWClock.Set(t);
            foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
                if (ps.transform.parent == null || ps.transform.parent.GetComponent<ParticleSystem>() == null)
                    ps.Simulate(t, true, true, true);
            foreach (var f in Object.FindObjectsByType<UWFlicker>(FindObjectsSortMode.None)) f.Tick(t);
            foreach (var f in Object.FindObjectsByType<UWFloat>(FindObjectsSortMode.None)) f.Tick(t);
        }

        static void Shot(Camera cam, string file, int w, int h)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGBHalf) { antiAliasing = 1 };
            var outRt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            cam.targetTexture = rt; cam.Render(); cam.targetTexture = null;
            Graphics.Blit(rt, outRt);
            RenderTexture.active = outRt; tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply(); RenderTexture.active = null;
            File.WriteAllBytes(file, tex.EncodeToPNG());
            rt.Release(); outRt.Release();
        }

        static void PreviewInner()
        {
            var cam = Build();
            string dir = Environment.GetEnvironmentVariable("UW_CAPTURE_DIR") ?? "Capture";
            Directory.CreateDirectory(dir);
            int frames = int.Parse(Environment.GetEnvironmentVariable("UW_FRAMES") ?? "0");
            SimAll(12f);
            var shots = new (string name, Vector3 target, float dist, float pitch, float yaw, float fov)[]
            {
                ("01_wide", new Vector3(-4.5f, 0, 3.5f), 37, 48, CamYaw, 44),
                ("02_portal", new Vector3(10, 3.2f, 11.5f), 16, 28, -20, 40),
                ("03_gate", new Vector3(-24, 1.2f, 0f), 22, 36, CamYaw, 44),
                ("04_game", new Vector3(0, 0, -1), 44, 58, CamYaw, 38),
                ("05_low", new Vector3(0, 1.5f, -1), 24, 16, -45, 46),
            };
            foreach (var s in shots)
            {
                cam.fieldOfView = s.fov; Aim(cam, s.target, s.dist, s.pitch, s.yaw);
                Shot(cam, Path.Combine(dir, s.name + ".png"), 1600, 900);
            }
            // brightness range check: same wide shot at darker / brighter settings
            var bright = Object.FindFirstObjectByType<UWBrightness>();
            cam.fieldOfView = 44; Aim(cam, new Vector3(-4.5f, 0, 3.5f), 60, 52, CamYaw);
            foreach (var b in new[] { -0.6f, 0.6f }) { bright.Set(b); Shot(cam, Path.Combine(dir, $"06_bright_{(b < 0 ? "minus" : "plus")}.png"), 1600, 900); }
            bright.Set(0f);
            // mood range check: violet (default) vs toned-down grey with haze
            var envc = Object.FindFirstObjectByType<UWEnvironment>();
            foreach (var (tone, hz, tag) in new[] { (0.5f, 0.4f, "mid"), (1f, 0.8f, "grey") })
            { envc.Set(tone, hz); Shot(cam, Path.Combine(dir, $"07_mood_{tag}.png"), 1600, 900); }
            cam.fieldOfView = 46; Aim(cam, new Vector3(-24, 1.2f, 0f), 22, 36, CamYaw);
            Shot(cam, Path.Combine(dir, "07_mood_grey_gate.png"), 1600, 900);
            envc.Set(0f, 0.3f);
            cam.fieldOfView = 44; Aim(cam, new Vector3(-4.5f, 0, 3.5f), 60, 52, CamYaw);
            if (frames > 0)
            {
                cam.fieldOfView = 44; Aim(cam, new Vector3(-4.5f, 0, 3.5f), 60, 52, CamYaw);
                for (int i = 0; i < frames; i++)
                {
                    SimAll(12f + i / 24f);
                    Shot(cam, Path.Combine(dir, $"f{i:D3}.png"), 1280, 720);
                }
            }
            Debug.Log("[UW] preview written to " + dir);
        }
    }
}
