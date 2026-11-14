// 피안화 (red spider lily, Lycoris radiata) - procedural, no third-party asset (11-14 pass 4: no lily in the project, and
// new downloads need the owner's OK). One plant = a bare stem topped by an umbel of 6 flowers; each flower = 6 narrow
// recurved petal ribbons + 6 long stamens arching up and out. Stylised oversize (head ~0.7 m across) so the clusters read
// from the top-down camera as glowing red patches along the rim. Ribbons are single quads strips, drawn double-sided.
using System.Collections.Generic;
using UnityEngine;

namespace Underworld
{
    public static class UWSpiderLily
    {
        static Mesh plant;

        // ribbon along a curve: pts = centre line, w = half width at each point, side = width direction at each point
        static void Ribbon(List<Vector3> v, List<Vector3> n, List<Color> c, List<int> t, Vector3[] pts, float[] w, Vector3[] side, Color col0, Color col1)
        {
            int b = v.Count;
            for (int i = 0; i < pts.Length; i++)
            {
                float k = i / (float)(pts.Length - 1);
                var col = Color.Lerp(col0, col1, k);
                var fwd = (i < pts.Length - 1 ? pts[i + 1] - pts[i] : pts[i] - pts[i - 1]).normalized;
                var nn = Vector3.Cross(side[i], fwd).normalized;
                v.Add(pts[i] - side[i] * w[i]); v.Add(pts[i] + side[i] * w[i]);
                n.Add(nn); n.Add(nn); c.Add(col); c.Add(col);
            }
            for (int i = 0; i < pts.Length - 1; i++)
            {
                int a = b + i * 2;
                t.Add(a); t.Add(a + 2); t.Add(a + 1); t.Add(a + 1); t.Add(a + 2); t.Add(a + 3);
            }
        }

        public static Mesh Plant()
        {
            if (plant) return plant;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var c = new List<Color>(); var t = new List<int>();
            var rnd = new System.Random(7);
            float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
            var stemCol = new Color(0.08f, 0.12f, 0.05f, 0f);   // alpha = emission mask (stem none)
            var petalBase = new Color(0.35f, 0.0f, 0.015f, 0.5f);
            var petalTip = new Color(0.9f, 0.01f, 0.025f, 1f);   // deep crimson: the grade (sat -18, ACES, bloom) turned 1/0.08/0.06 salmon pink
            var stamen = new Color(1f, 0.04f, 0.03f, 1f);
            const float stemH = 0.55f;
            // stem: two crossed ribbons
            for (int s = 0; s < 2; s++)
            {
                var side = s == 0 ? Vector3.right : Vector3.forward;
                var pts = new[] { Vector3.zero, new Vector3(0.01f, stemH * 0.5f, 0f), new Vector3(0f, stemH, 0.01f) };
                Ribbon(v, n, c, t, pts, new[] { 0.018f, 0.015f, 0.012f }, new[] { side, side, side }, stemCol, stemCol);
            }
            var top = new Vector3(0f, stemH, 0f);
            // umbel: 6 flowers on short pedicels facing outward and up
            for (int f = 0; f < 6; f++)
            {
                float fa = f / 6f * Mathf.PI * 2f + R(-0.2f, 0.2f);
                var dir = new Vector3(Mathf.Cos(fa), 0f, Mathf.Sin(fa));
                var centre = top + dir * 0.09f + Vector3.up * R(0.02f, 0.06f);
                var axis = (dir * 0.8f + Vector3.up * 0.6f).normalized;            // flower faces out and up
                var u = Vector3.Cross(axis, Vector3.up).normalized; var w = Vector3.Cross(u, axis).normalized;
                // petals: narrow, wavy, strongly recurved
                for (int p = 0; p < 6; p++)
                {
                    float pa = p / 6f * Mathf.PI * 2f + R(-0.15f, 0.15f);
                    var rad = u * Mathf.Cos(pa) + w * Mathf.Sin(pa);
                    var sd = Vector3.Cross(rad, axis).normalized;
                    var pts = new Vector3[5]; var wd = new float[5]; var sides = new Vector3[5];
                    for (int i = 0; i < 5; i++)
                    {
                        float k = i / 4f;
                        // out along rad, forward along axis first, then curling back
                        pts[i] = centre + rad * (0.17f * k) + axis * (0.06f * Mathf.Sin(k * Mathf.PI * 0.9f) - 0.07f * k * k) + sd * (0.012f * Mathf.Sin(k * 9f));
                        wd[i] = 0.022f * Mathf.Sin(Mathf.Lerp(0.25f, 2.9f, k));
                        sides[i] = sd;
                    }
                    Ribbon(v, n, c, t, pts, wd, sides, petalBase, petalTip);
                }
                // stamens: long, thin, arching up and out past the petals
                for (int s = 0; s < 6; s++)
                {
                    float sa = s / 6f * Mathf.PI * 2f + 0.5f + R(-0.2f, 0.2f);
                    var rad = u * Mathf.Cos(sa) * 0.6f + w * Mathf.Sin(sa) * 0.6f;
                    var sd = Vector3.Cross(rad.normalized, axis).normalized;
                    var pts = new Vector3[5]; var wd = new float[5]; var sides = new Vector3[5];
                    for (int i = 0; i < 5; i++)
                    {
                        float k = i / 4f;
                        pts[i] = centre + axis * (0.26f * k) + rad * (0.22f * k * k) + Vector3.up * (0.05f * k);
                        wd[i] = 0.0045f; sides[i] = sd;
                    }
                    Ribbon(v, n, c, t, pts, wd, sides, stamen, stamen);
                }
            }
            plant = new Mesh { name = "UW_SpiderLily" };
            plant.SetVertices(v); plant.SetNormals(n); plant.SetColors(c); plant.SetTriangles(t, 0);
            plant.RecalculateBounds();
            return plant;
        }

        // one clump: count plants scattered in a disc of radius r, combined into one mesh (one draw per clump)
        public static Mesh Clump(int count, float r, int seed, float scale)
        {
            var rnd = new System.Random(seed);
            float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
            var p = Plant();
            var ci = new CombineInstance[count];
            for (int i = 0; i < count; i++)
            {
                float a = R(0f, Mathf.PI * 2f), d = Mathf.Sqrt(R(0f, 1f)) * r;
                float s = scale * R(0.75f, 1.2f);
                ci[i] = new CombineInstance
                {
                    mesh = p,
                    transform = Matrix4x4.TRS(new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d), Quaternion.Euler(R(-12f, 12f), R(0f, 360f), R(-12f, 12f)), Vector3.one * s)
                };
            }
            var m = new Mesh { name = "UW_SpiderLilyClump", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            m.CombineMeshes(ci, true, true);
            m.RecalculateBounds();
            return m;
        }
    }
}
