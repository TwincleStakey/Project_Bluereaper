using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Dokkaebi.EditorTools
{
    // Batch preview: builds a temporary scene, plays the slam deterministically and writes frames.
    // Unity -batchmode -projectPath <copy> -executeMethod Dokkaebi.EditorTools.DokkaebiPreview.Run
    // env DK_CAPTURE_DIR = output folder (frames f000.png...)
    public static class DokkaebiPreview
    {
        const int W = 960, H = 640, FPS = 30;

        public static void Run()
        {
            try { RunInner(); EditorApplication.Exit(0); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        static void RunInner()
        {
            DokkaebiBuilder.BuildAll();
            string dir = Environment.GetEnvironmentVariable("DK_CAPTURE_DIR") ?? "Capture";
            Directory.CreateDirectory(dir);
            foreach (var f in Directory.GetFiles(dir, "f*.png")) File.Delete(f);

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // light: warm low sun like the Death's Door refs
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 1.6f; sun.color = new Color(1f, 0.9f, 0.78f);
            sun.shadows = LightShadows.Soft; sun.transform.rotation = Quaternion.Euler(52, -35, 0);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.6f, 0.66f);
            RenderSettings.ambientEquatorColor = new Color(0.5f, 0.45f, 0.42f);
            RenderSettings.ambientGroundColor = new Color(0.3f, 0.25f, 0.22f);

            // ground
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground"; ground.transform.localScale = Vector3.one * 6;
            var gm = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            gm.SetColor("_BaseColor", new Color(0.72f, 0.64f, 0.55f)); gm.SetFloat("_Smoothness", 0.05f);
            ground.GetComponent<Renderer>().sharedMaterial = gm;

            // boss (static pose until the Mixamo rig arrives)
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(DokkaebiBuilder.Root + "/Models/DK_StaticPose.fbx");
            float yaw = float.Parse(Environment.GetEnvironmentVariable("DK_YAW") ?? "180");
            if (model)
            {
                var boss = (GameObject)UnityEngine.Object.Instantiate(model);
                boss.name = "Dokkaebi"; boss.transform.localScale = Vector3.one * 3.2f;
                boss.transform.rotation = Quaternion.Euler(0, yaw, 0);
                boss.transform.position = new Vector3(0, 0, 1.2f);
                var bodyTex = AssetDatabase.LoadAssetAtPath<Texture2D>(DokkaebiBuilder.Root + "/Models/DK_Dokkaebi_BaseColor.jpg");
                var bodyMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                bodyMat.SetTexture("_BaseMap", bodyTex); bodyMat.SetFloat("_Smoothness", 0.15f);
                foreach (var r in boss.GetComponentsInChildren<Renderer>())
                    if (!r.name.Contains("Club")) r.sharedMaterials = System.Array.ConvertAll(r.sharedMaterials, _ => bodyMat);
            }

            // camera, high 3/4 view
            var camGo = new GameObject("Camera");
            var pivot = new GameObject("CamPivot").transform;
            pivot.position = new Vector3(0, 0, -0.5f); pivot.rotation = Quaternion.Euler(50, 0, 0);
            camGo.transform.SetParent(pivot, false); camGo.transform.localPosition = new Vector3(0, 0, -20f);
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 30; cam.nearClipPlane = 0.5f; cam.farClipPlane = 100; cam.allowHDR = true;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.2f, 0.18f, 0.18f);
            var acd = cam.GetUniversalAdditionalCameraData(); acd.renderPostProcessing = true; acd.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            var shake = camGo.AddComponent<DokkaebiCameraShake>(); shake.manualTick = true;

            // post: bloom so HDR fire glows
            var vol = new GameObject("Volume").AddComponent<Volume>(); vol.isGlobal = true;
            var prof = ScriptableObject.CreateInstance<VolumeProfile>();
            var bloom = prof.Add<Bloom>(true); bloom.intensity.Override(1.1f); bloom.threshold.Override(1.0f); bloom.scatter.Override(0.65f);
            var tm = prof.Add<Tonemapping>(true); tm.mode.Override(TonemappingMode.Neutral);
            var ca = prof.Add<ColorAdjustments>(true); ca.saturation.Override(5f); ca.contrast.Override(8f);
            vol.sharedProfile = prof;

            // slam fx
            var fxGo = new GameObject("SlamFx");
            var fx = fxGo.AddComponent<DokkaebiSlamFx>();
            DokkaebiBuilder.Wire(fx);
            fx.manualTick = true; fx.viewCamera = camGo.transform; fx.cameraShake = shake;
            fx.Init();

            var impact = new Vector3(-1.2f, 0, -1.6f);
            float telegraphAt = 0.1f, impactAt = 0.75f, total = 3.2f;
            bool tele = false, hit = false;

            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGBHalf) { antiAliasing = 4 };
            var outRt = new RenderTexture(W, H, 0, RenderTextureFormat.ARGB32);
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            int frames = Mathf.RoundToInt(total * FPS);
            float dt = 1f / FPS;
            UnityEngine.Random.InitState(42);
            for (int i = 0; i < frames; i++)
            {
                float t = i * dt;
                if (!tele && t >= telegraphAt) { tele = true; fx.Telegraph(impact, impactAt - telegraphAt); }
                if (!hit && t >= impactAt) { hit = true; fx.Impact(impact); }
                fx.Tick(dt);
                shake.Tick(dt);
                cam.targetTexture = rt;
                cam.Render();
                Graphics.Blit(rt, outRt);
                RenderTexture.active = outRt;
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
                RenderTexture.active = null;
                File.WriteAllBytes(Path.Combine(dir, $"f{i:D3}.png"), tex.EncodeToPNG());
            }
            cam.targetTexture = null;
            Debug.Log($"[Dokkaebi] wrote {frames} frames to {dir}");
        }
    }
}
