// 11-20 Kido: "흰 아웃라인 패턴 넣을라면 위에 옷에도" - the coat has white piping on its front edges and hem, the suit jacket
// was plain. Bakes thin white piping into Joe's body texture along the jacket's real garment edges: the open boundary
// edges of the Ch33_Suit mesh (lapels/front opening, hem, cuffs, collar), found by welding vertices by position (UV seams
// are not openings). Source = ArtSource/Whip/Ch33_dark_src.png (skin already darkened), output overwrites
// Ch33_1001_Diffuse_dark.png. rr_queue: call RoomRun.SuitPiping.Bake
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace RoomRun
{
    public static class SuitPiping
    {
        const string Fbx = "Assets/Game/RoomRun/ThirdParty/MixamoJoe/Joe.fbx";
        const string Out = "Assets/Game/RoomRun/ThirdParty/MixamoJoe/Textures/Ch33_1001_Diffuse_dark.png";
        public static int WidthPx = 7;
        public static Color32 Piping = new Color32(232, 232, 226, 255);

        public static void Bake()
        {
            string src = Path.Combine(Application.dataPath, "../ArtSource/Whip/Ch33_dark_src.png");
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.LoadImage(File.ReadAllBytes(src));
            int W = tex.width, H = tex.height;
            var px = tex.GetPixels32();
            Mesh mesh = null;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(Fbx)) if (o is Mesh m && m.name.Contains("Suit")) mesh = m;
            if (!mesh) { Debug.LogError("[SuitPiping] no Suit mesh"); return; }
            var v = mesh.vertices; var uv = mesh.uv; var tri = mesh.triangles;
            // weld by position
            var weld = new int[v.Length]; var map = new Dictionary<Vector3Int, int>();
            for (int i = 0; i < v.Length; i++)
            {
                var key = new Vector3Int(Mathf.RoundToInt(v[i].x * 10000f), Mathf.RoundToInt(v[i].y * 10000f), Mathf.RoundToInt(v[i].z * 10000f));
                if (!map.TryGetValue(key, out int id)) { id = map.Count; map[key] = id; }
                weld[i] = id;
            }
            var count = new Dictionary<long, int>(); var firstEdge = new Dictionary<long, (int a, int b)>();
            for (int t = 0; t < tri.Length; t += 3)
                for (int e = 0; e < 3; e++)
                {
                    int a = tri[t + e], b = tri[t + (e + 1) % 3];
                    int wa = weld[a], wb = weld[b];
                    long k = wa < wb ? ((long)wa << 32) | (uint)wb : ((long)wb << 32) | (uint)wa;
                    count[k] = count.TryGetValue(k, out int c) ? c + 1 : 1;
                    if (!firstEdge.ContainsKey(k)) firstEdge[k] = (a, b);
                }
            int edges = 0;
            foreach (var kv in count)
            {
                if (kv.Value != 1) continue;
                var (a, b) = firstEdge[kv.Key];
                Line(px, W, H, uv[a], uv[b]); edges++;
            }
            tex.SetPixels32(px); tex.Apply();
            File.WriteAllBytes(Path.Combine(Application.dataPath, "../" + Out), tex.EncodeToPNG());
            AssetDatabase.ImportAsset(Out);
            Debug.Log($"[SuitPiping] {edges} open edges piped into {Out}");
        }

        static void Line(Color32[] px, int W, int H, Vector2 a, Vector2 b)
        {
            var pa = new Vector2(a.x * W, a.y * H); var pb = new Vector2(b.x * W, b.y * H);
            float len = Vector2.Distance(pa, pb); int steps = Mathf.Max(1, Mathf.CeilToInt(len));
            int r = WidthPx / 2;
            for (int s = 0; s <= steps; s++)
            {
                var p = Vector2.Lerp(pa, pb, s / (float)steps);
                int cx = Mathf.RoundToInt(p.x), cy = Mathf.RoundToInt(p.y);
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (dx * dx + dy * dy > r * r) continue;
                        int x = cx + dx, y = cy + dy;
                        if (x < 0 || y < 0 || x >= W || y >= H) continue;
                        px[y * W + x] = Piping;
                    }
            }
        }
    }
}
