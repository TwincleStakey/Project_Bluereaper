using System.IO;
using UnityEditor;
using UnityEngine;

namespace Dokkaebi.EditorTools
{
    // Generates the slam VFX textures, materials and debris mesh, and wires a DokkaebiSlamFx.
    public static class DokkaebiBuilder
    {
        public const string Root = "Assets/Game/DokkaebiBoss";
        const string Tex = Root + "/Textures", Mat = Root + "/Materials";

        [MenuItem("Tools/Dokkaebi/Build Slam Assets")]
        public static void BuildAll()
        {
            Directory.CreateDirectory(Tex); Directory.CreateDirectory(Mat);
            WriteTex("DK_Soft", 128, SoftCircle);
            WriteTex("DK_Spark", 128, Spark);
            WriteTex("DK_Crack", 512, null, CrackTexture(512));
            if (!File.Exists($"{Tex}/DK_CraterMask.png") || System.Environment.GetEnvironmentVariable("DK_REBUILD_CRATER") == "1") BuildCrater(1024);
            AssetDatabase.Refresh();
            ConfigureTex($"{Tex}/DK_CraterMask.png", linear: true);
            ConfigureTex($"{Tex}/DK_CraterNormal.png", normal: true);
            foreach (var n in new[] { "DK_Soft", "DK_Spark", "DK_Crack", "DK_Puff" }) ConfigureTex($"{Tex}/{n}.png");

            var soft = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Tex}/DK_Soft.png");
            var spark = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Tex}/DK_Spark.png");
            var crack = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Tex}/DK_Crack.png");
            var puff = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Tex}/DK_Puff.png");

            var fire = new Color(0.35f, 1.1f, 2.2f, 1f);
            MakeMat("DK_Glow", "Dokkaebi/Additive", m => { m.SetTexture("_MainTex", soft); m.SetColor("_Color", fire); });
            MakeMat("DK_Spark", "Dokkaebi/Additive", m => { m.SetTexture("_MainTex", spark); m.SetColor("_Color", Color.white * 1.6f); });
            MakeMat("DK_Dust", "Dokkaebi/Smoke", m =>
            {
                m.SetTexture("_MainTex", puff);
                m.SetColor("_Color", new Color(0.93f, 0.86f, 0.76f, 1f));
                m.SetColor("_ShadeColor", new Color(0.46f, 0.40f, 0.38f, 1f));
            });
            MakeMat("DK_Shockwave", "Dokkaebi/GroundRing", m =>
            {
                m.SetColor("_Color", new Color(0.2f, 0.75f, 1.7f, 1f)); m.SetFloat("_Wobble", 0.04f);
                m.SetFloat("_SrcBlend", 1); m.SetFloat("_DstBlend", 1); m.SetFloat("_AlphaMode", 0);
            });
            MakeMat("DK_DustRing", "Dokkaebi/GroundRing", m =>
            {
                m.SetColor("_Color", new Color(0.86f, 0.79f, 0.68f, 0.85f)); m.SetFloat("_Wobble", 0.09f);
                m.SetFloat("_SrcBlend", 5); m.SetFloat("_DstBlend", 10); m.SetFloat("_AlphaMode", 1);
            });
            MakeMat("DK_Telegraph", "Dokkaebi/GroundRing", m =>
            {
                m.SetColor("_Color", new Color(0.85f, 0.18f, 0.12f, 0.55f)); m.SetFloat("_Wobble", 0.0f); m.SetFloat("_Width", 0.04f);
                m.SetFloat("_SrcBlend", 5); m.SetFloat("_DstBlend", 10); m.SetFloat("_AlphaMode", 1);
            });
            MakeMat("DK_Crack", "Dokkaebi/CrackDecal", m =>
            {
                m.SetTexture("_MainTex", crack);
                m.SetColor("_GlowColor", new Color(0.15f, 0.7f, 1.8f, 1f)); m.SetColor("_GrooveColor", new Color(0.16f, 0.11f, 0.09f, 1f)); m.SetColor("_ScorchColor", new Color(0.36f, 0.3f, 0.26f, 1f));
            });
            MakeMat("DK_Crater", "Dokkaebi/CraterDecal", m =>
            {
                m.SetTexture("_Mask", AssetDatabase.LoadAssetAtPath<Texture2D>($"{Tex}/DK_CraterMask.png"));
                m.SetTexture("_Normal", AssetDatabase.LoadAssetAtPath<Texture2D>($"{Tex}/DK_CraterNormal.png"));
                m.SetColor("_GlowColor", new Color(0.15f, 0.7f, 1.8f, 1f));
            });
            MakeMat("DK_Stone", "Universal Render Pipeline/Lit", m =>
            {
                m.SetColor("_BaseColor", new Color(0.42f, 0.38f, 0.35f, 1f));
                m.SetFloat("_Smoothness", 0.08f);
            });
            MakeMat("DK_Debris", "Universal Render Pipeline/Particles/Lit", m =>
            {
                m.SetColor("_BaseColor", new Color(0.42f, 0.37f, 0.33f, 1f));
                m.SetFloat("_Smoothness", 0.1f);
            });

            var meshPath = $"{Root}/Models/DK_DebrisRock.asset";
            if (!File.Exists(meshPath)) AssetDatabase.CreateAsset(RockMesh(), meshPath);
            AssetDatabase.SaveAssets();
            Debug.Log("[Dokkaebi] slam assets built");
        }

        public static void Wire(DokkaebiSlamFx fx)
        {
            Material L(string n) => AssetDatabase.LoadAssetAtPath<Material>($"{Mat}/{n}.mat");
            fx.glowMat = L("DK_Glow"); fx.sparkMat = L("DK_Spark"); fx.smokeMat = L("DK_Dust");
            fx.ringMat = L("DK_Shockwave"); fx.dustRingMat = L("DK_DustRing"); fx.telegraphMat = L("DK_Telegraph");
            fx.crackMat = L("DK_Crack"); fx.debrisMat = L("DK_Debris");
            fx.debrisMesh = AssetDatabase.LoadAssetAtPath<Mesh>($"{Root}/Models/DK_DebrisRock.asset");
            fx.craterMat = L("DK_Crater"); fx.stoneMat = L("DK_Stone");
            var stones = new System.Collections.Generic.List<Mesh>(); var flats = new System.Collections.Generic.List<Mesh>();
            foreach (var g in AssetDatabase.FindAssets("stone_small t:Model", new[] { Root + "/ThirdParty/Kenney" }))
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(p)) if (o is Mesh mm) (p.Contains("Flat") ? flats : stones).Add(mm);
            }
            fx.debrisMeshes = stones.ToArray(); fx.rimMeshes = flats.Count > 0 ? flats.ToArray() : stones.ToArray();
        }

        // ------------------------------------------------------------------
        static void MakeMat(string name, string shader, System.Action<Material> setup)
        {
            var path = $"{Mat}/{name}.mat";
            var sh = Shader.Find(shader);
            if (!sh) { Debug.LogError("[Dokkaebi] missing shader " + shader); return; }
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            m.shader = sh; setup(m);
            if (shader.Contains("Particles/Lit")) { m.SetFloat("_Surface", 0); m.renderQueue = 2000; }
            EditorUtility.SetDirty(m);
        }

        static void WriteTex(string name, int size, System.Func<float, float, Color> f, Texture2D pre = null)
        {
            var t = pre;
            if (!t)
            {
                t = new Texture2D(size, size, TextureFormat.RGBA32, false);
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                        t.SetPixel(x, y, f((x + 0.5f) / size, (y + 0.5f) / size));
                t.Apply();
            }
            File.WriteAllBytes($"{Tex}/{name}.png", t.EncodeToPNG());
        }

        static void ConfigureTex(string path, bool linear = false, bool normal = false)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter; if (!ti) return;
            if (normal) ti.textureType = TextureImporterType.NormalMap;
            ti.alphaIsTransparency = !normal && !linear; ti.wrapMode = TextureWrapMode.Clamp; ti.mipmapEnabled = true;
            ti.sRGBTexture = !(linear || normal || path.EndsWith("DK_Crack.png"));
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.SaveAndReimport();
        }

        static Color SoftCircle(float u, float v)
        {
            float r = Mathf.Clamp01(new Vector2(u - .5f, v - .5f).magnitude * 2);
            float a = Mathf.Pow(1 - r, 2.2f);
            float core = Mathf.Pow(Mathf.Clamp01(1 - r * 2.2f), 2);
            return new Color(1, 1, 1, Mathf.Clamp01(a + core));
        }

        static Color Spark(float u, float v)
        {
            float across = Mathf.Exp(-Mathf.Pow((v - .5f) * 7f, 2));
            float along = Mathf.Clamp01(Mathf.Sin(u * Mathf.PI)) * Mathf.Lerp(0.4f, 1f, u);
            return new Color(1, 1, 1, across * along);
        }

        // Impact crater from the CC0 Poly Haven cracked_concrete height scan + radial spokes + a bowl and rim.
        static float SS(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3 - 2 * t); }

        static void BuildCrater(int n)
        {
            var src = new Texture2D(2, 2);
            src.LoadImage(File.ReadAllBytes(Root + "/ThirdParty/PolyHaven/cracked_concrete_disp_2k.png"));
            int sw = src.width; var sp = src.GetPixels();
            float Disp(float u, float v)
            {
                float x = Mathf.Clamp(u * (sw - 1), 0, sw - 1), y = Mathf.Clamp(v * (sw - 1), 0, sw - 1);
                int x0 = (int)x, y0 = (int)y, x1 = Mathf.Min(x0 + 1, sw - 1), y1 = Mathf.Min(y0 + 1, sw - 1);
                float fx = x - x0, fy = y - y0;
                float a = Mathf.Lerp(sp[y0 * sw + x0].r, sp[y0 * sw + x1].r, fx), b = Mathf.Lerp(sp[y1 * sw + x0].r, sp[y1 * sw + x1].r, fx);
                return Mathf.Lerp(a, b, fy);
            }
            var spokesTex = CrackTexture(n);
            var spokes = spokesTex.GetPixels();
            var H = new float[n * n]; var R = new float[n * n]; var G = new float[n * n]; var Bc = new float[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + .5f) / n, v = (y + .5f) / n;
                    float px = u * 2 - 1, py = v * 2 - 1, r = Mathf.Sqrt(px * px + py * py);
                    float rn = r + (Mathf.PerlinNoise(u * 5f, v * 5f) - 0.5f) * 0.22f + (Mathf.PerlinNoise(u * 17f + 9, v * 17f) - 0.5f) * 0.06f;
                    // scan sampled at 0.75 so plates read at boss scale
                    float d = Disp(0.12f + u * 0.75f, 0.1f + v * 0.75f);
                    float plate = 1 - SS(0.5f, 0.85f, rn);
                    float fiss = Mathf.Clamp01((0.48f - d) * 10f) * plate;
                    float bowlDepth = 1 - SS(0f, 0.26f, rn);
                    float bowl = -0.55f * bowlDepth + 0.22f * Mathf.Exp(-Mathf.Pow((rn - 0.3f) / 0.08f, 2));
                    int i = y * n + x;
                    H[i] = (d - 0.5f) * 1.8f * plate + bowl;
                    R[i] = fiss;
                    G[i] = Mathf.Max((1 - SS(0.05f, 0.55f, rn)) * 0.55f, bowlDepth * 0.9f);
                    Bc[i] = 1 - SS(0.7f, 0.97f, rn);
                }
            var mask = new Texture2D(n, n, TextureFormat.RGBA32, false, true);
            var nor = new Texture2D(n, n, TextureFormat.RGBA32, false, true);
            float S = n * 0.012f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int i = y * n + x;
                    float hx = H[y * n + Mathf.Min(x + 1, n - 1)] - H[y * n + Mathf.Max(x - 1, 0)];
                    float hy = H[Mathf.Min(y + 1, n - 1) * n + x] - H[Mathf.Max(y - 1, 0) * n + x];
                    var nv = new Vector3(-hx * S, -hy * S, 1).normalized;
                    nor.SetPixel(x, y, new Color(nv.x * .5f + .5f, nv.y * .5f + .5f, nv.z * .5f + .5f, 1));
                    mask.SetPixel(x, y, new Color(R[i], G[i], Bc[i], 1));
                }
            File.WriteAllBytes($"{Tex}/DK_CraterMask.png", mask.EncodeToPNG());
            File.WriteAllBytes($"{Tex}/DK_CraterNormal.png", nor.EncodeToPNG());
        }

        // R: branching radial cracks (thick near centre). G: noisy scorch disc.
        static Texture2D CrackTexture(int n)
        {
            var R = new float[n * n];
            var rng = new System.Random(1234);
            float Rand() => (float)rng.NextDouble();
            void Stamp(float x, float y, float rad, float val)
            {
                int r = Mathf.CeilToInt(rad + 1);
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        int px = (int)x + dx, py = (int)y + dy;
                        if (px < 0 || py < 0 || px >= n || py >= n) continue;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        float k = Mathf.Clamp01(rad + 0.6f - d);
                        R[py * n + px] = Mathf.Max(R[py * n + px], k * val);
                    }
            }
            void Branch(float x, float y, float ang, float len, float width, int depth)
            {
                float step = 2f;
                for (float s = 0; s < len; s += step)
                {
                    ang += (Rand() - .5f) * 0.5f;
                    x += Mathf.Cos(ang) * step; y += Mathf.Sin(ang) * step;
                    float k = 1 - s / len;
                    float dist = new Vector2(x - n / 2f, y - n / 2f).magnitude / (n / 2f);
                    if (dist > 0.96f) return;
                    Stamp(x, y, Mathf.Max(0.6f, width * (0.35f + 0.65f * k)), 1);
                    if (depth < 3 && Rand() < 0.012f)
                        Branch(x, y, ang + (Rand() < .5f ? -1 : 1) * (0.5f + Rand() * 0.6f), len * (0.3f + Rand() * 0.25f), width * 0.55f, depth + 1);
                }
            }
            int spokes = 8;
            for (int i = 0; i < spokes; i++)
            {
                float a = (i + Rand() * 0.6f) / spokes * Mathf.PI * 2;
                Branch(n / 2f + Mathf.Cos(a) * 6, n / 2f + Mathf.Sin(a) * 6, a, n * (0.26f + Rand() * 0.22f), 8f, 0);
            }
            // a broken ring a third of the way out
            for (int i = 0; i < 9; i++)
            {
                float a0 = Rand() * Mathf.PI * 2, rr = n * (0.14f + Rand() * 0.05f);
                for (float a = a0; a < a0 + 0.5f; a += 0.01f)
                    Stamp(n / 2f + Mathf.Cos(a) * rr, n / 2f + Mathf.Sin(a) * rr, 1.6f, 0.9f);
            }
            // crater centre
            Stamp(n / 2f, n / 2f, n * 0.04f, 1);

            var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + .5f) / n * 2 - 1, v = (y + .5f) / n * 2 - 1;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float ang = Mathf.Atan2(v, u);
                    float noise = Mathf.PerlinNoise(x * 0.025f, y * 0.025f) * 0.5f + Mathf.PerlinNoise(x * 0.09f + 30, y * 0.09f) * 0.25f;
                    float g = Mathf.Clamp01((0.55f + noise * 0.35f - r) * 3f);
                    t.SetPixel(x, y, new Color(R[y * n + x], g, 0, 1));
                }
            t.Apply();
            return t;
        }

        static Mesh RockMesh()
        {
            // jittered octahedron-ish chunk, flat shaded
            var rng = new System.Random(7);
            Vector3[] b = { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back,
                            new Vector3(.6f,.5f,.6f), new Vector3(-.6f,-.4f,.5f), new Vector3(.5f,-.5f,-.6f) };
            for (int i = 0; i < b.Length; i++) b[i] = Vector3.Scale(b[i], new Vector3(1f, 0.7f, 0.85f)) * (0.8f + (float)rng.NextDouble() * 0.4f);
            int[] tri = { 0,4,3, 0,3,5, 0,5,2, 0,2,4, 1,3,4, 1,5,3, 1,2,5, 1,4,2 };
            var v = new Vector3[tri.Length]; var t = new int[tri.Length];
            for (int i = 0; i < tri.Length; i++) { v[i] = b[tri[i]] * 0.5f; t[i] = i; }
            var m = new Mesh { name = "DK_DebrisRock", vertices = v, triangles = t };
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }
    }
}
