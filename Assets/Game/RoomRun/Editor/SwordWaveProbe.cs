// 11-19 sword wave mirror probe: rr_cmd `call RoomRun.SwordWaveProbe.Run`. Renders Resources/GS_SwordWave top-down far
// below the scene (y -2000) with four mirror methods (none / roll 180 / renderer flip X / scale -X) at three sim times,
// to ArtSource/swordwave/probe.png. Edit mode only, everything it makes is destroyed again.
using System.IO;
using UnityEngine;

namespace RoomRun
{
    public static class SwordWaveProbe
    {
        public static void Run()
        {
            var prefab = Resources.Load<GameObject>("GS_SwordWave");
            if (!prefab) { Debug.LogWarning("[SwordWaveProbe] prefab missing"); return; }
            float[] times = { 0.06f, 0.12f, 0.25f };
            const int W = 360, H = 360;
            var sheet = new Texture2D(W * 4, H * times.Length, TextureFormat.RGB24, false);
            var root = new GameObject("SwordWaveProbe");
            var camGo = new GameObject("cam"); camGo.transform.SetParent(root.transform);
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true; cam.orthographicSize = 4f; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.05f, 0.08f, 0.06f);
            cam.transform.position = new Vector3(0f, -1990f, 0f); cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);   // top-down, +Z (flight) = up
            cam.nearClipPlane = 0.1f; cam.farClipPlane = 50f;
            var rt = new RenderTexture(W, H, 24); cam.targetTexture = rt;
            try
            {
                for (int v = 0; v < 4; v++)
                    for (int k = 0; k < times.Length; k++)
                    {
                        var go = Object.Instantiate(prefab, root.transform);
                        go.transform.position = new Vector3(0f, -2000f, -1f);
                        go.transform.rotation = v == 1 ? Quaternion.Euler(0f, 0f, 180f) : Quaternion.identity;
                        go.transform.localScale = v == 3 ? new Vector3(-1f, 1f, 1f) : Vector3.one;
                        foreach (var p in go.GetComponentsInChildren<ParticleSystem>(true))
                        {
                            string n = p.gameObject.name;
                            if (p.gameObject != go && !n.StartsWith("Trails")) { p.gameObject.SetActive(false); continue; }
                            if (n.StartsWith("Trails")) { p.gameObject.SetActive(false); continue; }
                            var main = p.main; main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                            if (v == 2) p.GetComponent<ParticleSystemRenderer>().flip = new Vector3(1f, 0f, 0f);
                        }
                        var ps = go.GetComponent<ParticleSystem>();
                        ps.useAutoRandomSeed = false; ps.randomSeed = 7;
                        ps.Simulate(times[k], true, true);
                        cam.Render();
                        RenderTexture.active = rt;
                        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
                        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
                        sheet.SetPixels(v * W, (times.Length - 1 - k) * H, W, H, tex.GetPixels());
                        Object.DestroyImmediate(tex);
                        RenderTexture.active = null;
                        Object.DestroyImmediate(go);
                    }
                var dir = Path.Combine(Application.dataPath, "../ArtSource/swordwave");
                Directory.CreateDirectory(dir);
                File.WriteAllBytes(Path.Combine(dir, "probe.png"), sheet.EncodeToPNG());
                Debug.Log("[SwordWaveProbe] wrote probe.png (cols: none, roll180, flipX, scale-X; rows: t " + string.Join("/", times) + ")");
            }
            finally
            {
                cam.targetTexture = null; Object.DestroyImmediate(rt); Object.DestroyImmediate(root); Object.DestroyImmediate(sheet);
            }
        }
    }
}
