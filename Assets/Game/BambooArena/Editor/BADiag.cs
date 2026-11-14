// 11-14 look-fix helper: opens the bamboo scene additively (scene setup restored afterwards), renders close-ups of the
// spots Kido flagged (pagoda base, cairns, path edge, open floor) from a temp camera to ArtSource/ba_diag, and logs what
// stands near each spot. Editor only; nothing in the scene is changed or saved.
using System.IO;
using System.Linq;
using BambooArena;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class BADiag
{
    [MenuItem("Tools/Dev/Bamboo Arena/Diag: Close-ups")]
    public static void CloseUps()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[BADiag] in play mode"); return; }
        var setup = EditorSceneManager.GetSceneManagerSetup();
        var s = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(BABuilder.ScenePath);
        bool wasOpen = s.IsValid() && s.isLoaded;
        if (!wasOpen) s = EditorSceneManager.OpenScene(BABuilder.ScenePath, OpenSceneMode.Additive);
        string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "ArtSource", "ba_diag");
        Directory.CreateDirectory(dir);
        var camGo = new GameObject("BADiagCam") { hideFlags = HideFlags.DontSave };
        var log = new System.Text.StringBuilder();
        ShaderUtil.allowAsyncCompilation = false;
        try
        {
            var f = s.GetRootGameObjects().Select(g => g.GetComponentInChildren<BambooField>()).FirstOrDefault(x => x);
            if (!f) { Debug.LogError("[BADiag] no BambooField"); return; }
            f.SetGlobals();
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = f.fog; cam.fieldOfView = 35f; cam.nearClipPlane = 0.1f; cam.farClipPlane = 300f;
            var rt = new RenderTexture(1280, 800, 24) { antiAliasing = 4 };
            var tex = new Texture2D(1280, 800, TextureFormat.RGB24, false);
            cam.targetTexture = rt;
            void Shot(string name, Vector3 target, float dist, float pitch = 50f, float yaw = 0f)
            {
                var rot = Quaternion.Euler(pitch, yaw, 0);
                cam.transform.SetPositionAndRotation(target - rot * Vector3.forward * dist, rot);
                for (int k = 0; k < 3; k++) cam.Render();
                RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 1280, 800), 0, 0); tex.Apply(); RenderTexture.active = null;
                File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
                log.Append($"{name}: near {target}: ");
                foreach (var r in s.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Renderer>()))
                {
                    var b = r.bounds; var c = b.center; c.y = 0;
                    if (Vector3.Distance(c, new Vector3(target.x, 0, target.z)) < 4.5f)
                        log.Append($"{r.name}[{(r.sharedMaterial ? r.sharedMaterial.name : "-")}] at {b.center.x:0.0},{b.center.z:0.0} y{b.min.y:0.00}..{b.max.y:0.00}; ");
                }
                log.Append("\n");
            }
            float hx = f.arena.x * 0.5f, hz = f.arena.y * 0.5f;
            var pag = new Vector3(f.shrine.x, 0, f.shrine.y);
            Shot("pagoda_top", pag, 22f, 55f);
            Shot("pagoda_low", pag + Vector3.up * 0.5f, 9f, 22f, 200f);
            Shot("cairn_west", new Vector3(-hx - 1.5f, 0, 7f), 16f, 55f);
            Shot("cairn_east", new Vector3(hx + 2.4f, 0, -9f), 16f, 55f);
            Shot("path_south", new Vector3(f.TrailX(-hz + 8f), 0, -hz + 8f), 18f, 60f);
            Shot("floor_open", new Vector3(-18f, 0, -12f), 30f, 60f);
            Shot("edge_north", new Vector3(10f, 0, hz - 4f), 30f, 55f);
            cam.targetTexture = null; Object.DestroyImmediate(rt);
            File.WriteAllText(Path.Combine(dir, "diag.txt"), log.ToString());
            Debug.Log("[BADiag] close-ups saved to " + dir);
        }
        finally
        {
            ShaderUtil.allowAsyncCompilation = true;
            Object.DestroyImmediate(camGo);
            if (!wasOpen) EditorSceneManager.CloseScene(s, true);
            if (setup.Length > 0 && !wasOpen) { /* additive scene removed; the rest of the setup is untouched */ }
        }
    }
}
