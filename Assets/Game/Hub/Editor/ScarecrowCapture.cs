// Edit-mode hit test of the 거점 허수아비 in a private preview scene (no Play, no scene edits).
// rr_queue "call Hub.ScarecrowCapture.Run" -> ArtSource/Scarecrow/cap/{front,game}_NNN.png (30 fps, real time), log cap_log.txt
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Hub
{
    public static class ScarecrowCapture
    {
        static readonly List<string> log = new List<string>();

        public static void Run()
        {
            log.Clear();
            string outDir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "ArtSource/Scarecrow/cap");
            try { RunInner(outDir); } catch (System.Exception e) { log.Add("ERROR " + e); }
            File.WriteAllLines(Path.Combine(outDir, "..", "cap_log.txt"), log);
        }

        static void RunInner(string outDir)
        {
            if (Directory.Exists(outDir)) foreach (var f in Directory.GetFiles(outDir, "*.png")) File.Delete(f);
            Directory.CreateDirectory(outDir);
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ScarecrowSetup.Prefab);
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                var sc = go.GetComponent<TrainingScarecrow>(); sc.Init();
                var smr = go.GetComponentInChildren<SkinnedMeshRenderer>();
                // proxy: edit-mode renders do not re-skin, so bake each frame
                var proxy = new GameObject("proxy"); SceneManager_Move(proxy, scene);
                var pmf = proxy.AddComponent<MeshFilter>(); var pmr = proxy.AddComponent<MeshRenderer>();
                var baked = new Mesh(); pmf.sharedMesh = baked;
                smr.enabled = false;

                var floor = GameObject.CreatePrimitive(PrimitiveType.Plane); SceneManager_Move(floor, scene);
                floor.transform.localScale = Vector3.one * 3f;
                var fm = new Material(Shader.Find("Universal Render Pipeline/Lit")); fm.SetColor("_BaseColor", new Color(0.23f, 0.22f, 0.21f)); floor.GetComponent<Renderer>().sharedMaterial = fm;
                var lgo = new GameObject("sun"); SceneManager_Move(lgo, scene);
                var sun = lgo.AddComponent<Light>(); sun.type = LightType.Directional; sun.intensity = 1.6f; sun.color = new Color(1f, 0.95f, 0.88f); sun.shadows = LightShadows.Soft;
                lgo.transform.rotation = Quaternion.Euler(50, -150, 0);
                var cgo = new GameObject("cam"); SceneManager_Move(cgo, scene);
                var cam = cgo.AddComponent<Camera>(); cam.scene = scene; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.12f, 0.12f, 0.13f);
                cam.fieldOfView = 30f; cam.nearClipPlane = 0.1f; cam.farClipPlane = 100f;
                var rt = new RenderTexture(720, 720, 24, RenderTextureFormat.ARGB32); rt.antiAliasing = 4; cam.targetTexture = rt;
                var tex = new Texture2D(720, 720, TextureFormat.RGB24, false);
                RenderSettings.ambientLight = new Color(0.45f, 0.45f, 0.5f);

                // player stands at +Z (in front of the mask) and hits three times, then from the side
                var hits = new Dictionary<int, Vector3> { { 10, new Vector3(0, 0, 2) }, { 40, new Vector3(0, 0, 2) }, { 52, new Vector3(0, 0, 2) }, { 90, new Vector3(2, 0, 0.5f) } };
                var views = new (string name, Vector3 pos, Vector3 look)[]
                {
                    ("front", new Vector3(0, 2.0f, 11.5f), new Vector3(0, 1.8f, 0)),
                    ("side", new Vector3(11.5f, 2.0f, 0), new Vector3(0, 1.8f, 0)),
                    ("game", new Vector3(0, 24.6f * 0.5f, 21f * 0.5f) , new Vector3(0, 1.6f, 0)),   // pitch 49.5 like the hub camera, closer
                    ("close", new Vector3(-2.2f, 3.6f, 3.4f), new Vector3(-0.3f, 2.6f, -0.2f)),
                    ("wood", new Vector3(-2.6f, 4.6f, -3.2f), new Vector3(-0.5f, 2.6f, -0.4f)),
                };
                // simulate once, record bone poses per frame, then render each view
                const int N = 140; const float dt = 1f / 30f;
                var frames = new List<Mesh>();
                var flashOn = new List<bool>();
                var swayRot = new List<Quaternion>(); var smrPose = new List<(Vector3, Quaternion)>();
                for (int f = 0; f < N; f++)
                {
                    if (hits.TryGetValue(f, out var from)) { sc.Kick(go.transform.TransformPoint(from)); log.Add($"hit f{f} from {from}"); }
                    sc.Step(dt); sc.Pose(f * dt);
                    if (f % 3 == 0 && f < 70) log.Add($"f{f} head {sc.head.position:F2} legsRot {sc.legs.localEulerAngles:F1} sway {(sc.sway ? sc.sway.localEulerAngles : Vector3.zero):F1}");
                    var m = new Mesh(); smr.BakeMesh(m, false); m.RecalculateBounds(); frames.Add(m);
                    swayRot.Add(sc.sway ? sc.sway.localRotation : Quaternion.identity); smrPose.Add((smr.transform.position, smr.transform.rotation));
                    flashOn.Add(smr.sharedMaterial != null && smr.sharedMaterial.name.Contains("Flash"));
                }
                var mats = smr.sharedMaterials;
                var flash = sc.hitFlash;
                proxy.transform.SetPositionAndRotation(smr.transform.position, smr.transform.rotation);
                proxy.transform.localScale = Vector3.one;
                foreach (var v in views)
                {
                    cam.transform.position = v.pos; cam.transform.LookAt(v.look);
                    for (int f = 0; f < N; f++)
                    {
                        pmf.sharedMesh = frames[f];
                        if (sc.sway) sc.sway.localRotation = swayRot[f];
                        proxy.transform.SetPositionAndRotation(smrPose[f].Item1, smrPose[f].Item2); proxy.transform.localScale = Vector3.one;   // BakeMesh(false) already carries the model scale
                        var ms = (Material[])mats.Clone(); if (flashOn[f] && flash) for (int k = 0; k < ms.Length; k++) ms[k] = flash;
                        pmr.sharedMaterials = ms;
                        cam.Render();
                        RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 720, 720), 0, 0); tex.Apply(); RenderTexture.active = null;
                        File.WriteAllBytes(Path.Combine(outDir, $"{v.name}_{f:000}.png"), tex.EncodeToPNG());
                    }
                }
                log.Add($"frames {N} views {views.Length}; maxLean check done; flash frames {flashOn.FindAll(x => x).Count}");
                foreach (var m in frames) Object.DestroyImmediate(m);
                rt.Release();
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        static void SceneManager_Move(GameObject g, UnityEngine.SceneManagement.Scene s) => UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(g, s);
    }
}
