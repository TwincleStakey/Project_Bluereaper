// Edit-mode captures of the BossSeal scene (no Play mode, so it never fights other threads' Play cycles).
// rr_queue: "call BossSeal.BSShots.Capture". Reads ArtSource/BossSeal/bs_shots.txt, one view per line:
//   name x z zoom [yawDeg] [key=value ...]   (camera = the RoomRun game camera offset (0,17,-14.5)*zoom looking at (x,0.3,z))
//   key=value sets a BS_Ground float/vector property for that capture only, e.g. _GlowAmt=0,0,0,0
// Writes ArtSource/BossSeal/shots/<name>.png (1600 x 900). No StageMood/LookFX grade (those run only in RoomRun).
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BossSeal
{
    public static class BSShots
    {
        public static void Capture()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "ArtSource", "BossSeal");
            string list = Path.Combine(dir, "bs_shots.txt");
            if (!File.Exists(list)) return;
            var active = EditorSceneManager.GetActiveScene();
            if (active.path != BSBuilder.ScenePath)
            {
                if (active.isDirty) return;
                EditorSceneManager.OpenScene(BSBuilder.ScenePath, OpenSceneMode.Single);
            }
            var cam = Camera.main;
            var mat = AssetDatabase.LoadAssetAtPath<Material>(BSBuilder.Root + "/Materials/BS_Ground.mat");
            var outDir = Path.Combine(dir, "shots"); Directory.CreateDirectory(outDir);
            var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            var ci = CultureInfo.InvariantCulture;
            foreach (var raw in File.ReadAllLines(list))
            {
                var line = raw.Trim(); if (line.Length == 0 || line.StartsWith("#")) continue;
                var p = line.Split(' ');
                float x = float.Parse(p[1], ci), z = float.Parse(p[2], ci), zoom = float.Parse(p[3], ci);
                float yaw = 0;
                var backups = new System.Collections.Generic.List<(string, Vector4)>();
                for (int k = 4; k < p.Length; k++)
                {
                    if (!p[k].Contains("=")) { yaw = float.Parse(p[k], ci); continue; }
                    var kv = p[k].Split('='); var v = kv[1].Split(',');
                    backups.Add((kv[0], mat.GetVector(kv[0])));
                    var vec = new Vector4(); for (int j = 0; j < v.Length && j < 4; j++) vec[j] = float.Parse(v[j], ci);
                    if (v.Length == 1) mat.SetFloat(kv[0], vec.x); else mat.SetVector(kv[0], vec);
                }
                var off = Quaternion.Euler(0, yaw, 0) * (new Vector3(0, 17f, -14.5f) * zoom);
                var target = new Vector3(x, 0.3f, z);
                cam.transform.position = target + off; cam.transform.rotation = Quaternion.LookRotation(-off);
                cam.targetTexture = rt; cam.Render(); cam.targetTexture = null;
                RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); tex.Apply(); RenderTexture.active = null;
                File.WriteAllBytes(Path.Combine(outDir, p[0] + ".png"), tex.EncodeToPNG());
                foreach (var (n, v) in backups) mat.SetVector(n, v);
            }
            Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
            File.AppendAllText(Path.Combine(dir, "bs_log.txt"), $"[{System.DateTime.Now:HH:mm:ss}] shots done\n");
        }
    }
}
