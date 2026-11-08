using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Dokkaebi.EditorTools
{
    // Builds the rigged dokkaebi boss prefab (Mixamo walk + slam retargeted in Blender),
    // a small demo scene, and an offscreen preview capture. Never changes the active scene.
    public static class DokkaebiBossBuilder
    {
        const string Root = DokkaebiBuilder.Root;
        const string ModelPath = Root + "/Models/DK_Dokkaebi_Rigged.fbx";
        const string PrefabPath = Root + "/Prefabs/DK_DokkaebiBoss.prefab";
        const string ControllerPath = Root + "/Animation/DK_Boss.controller";
        const string ScenePath = Root + "/Scenes/DokkaebiDemo.unity";
        const float ModelScale = 3.2f;
        const float ImpactFrame = 29f;   // lowest club-head frame of DK_Slam at 30 fps (measured in Blender)
        static readonly Vector3 DemoOrigin = new Vector3(3000f, 0f, 3000f);

        [MenuItem("Tools/Dokkaebi/Build Boss")]
        public static void Build()
        {
            DokkaebiBuilder.BuildAll();
            Directory.CreateDirectory(Root + "/Prefabs"); Directory.CreateDirectory(Root + "/Animation"); Directory.CreateDirectory(Root + "/Scenes");

            var imp = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
            imp.animationType = ModelImporterAnimationType.Generic;
            imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            imp.importAnimation = true;
            var clips = imp.defaultClipAnimations;
            foreach (var c in clips)
            {
                bool walk = c.name.Contains("Walk");
                c.name = walk ? "DK_Walk" : c.name.Contains("Slam") ? "DK_Slam" : c.name;
                c.loopTime = walk; c.lockRootRotation = true; c.lockRootHeightY = false; c.keepOriginalOrientation = true;
            }
            imp.clipAnimations = clips;
            imp.SaveAndReimport();

            var all = AssetDatabase.LoadAllAssetsAtPath(ModelPath);
            var walkClip = all.OfType<AnimationClip>().First(c => c.name == "DK_Walk");
            var slamClip = all.OfType<AnimationClip>().First(c => c.name == "DK_Slam");

            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (ctrl) AssetDatabase.DeleteAsset(ControllerPath);
            ctrl = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            var sm = ctrl.layers[0].stateMachine;
            var sWalk = sm.AddState("Walk"); sWalk.motion = walkClip;
            var sSlam = sm.AddState("Slam"); sSlam.motion = slamClip;
            sm.defaultState = sWalk;

            var bodyMat = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/DK_Body.mat");
            if (!bodyMat) { bodyMat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(bodyMat, Root + "/Materials/DK_Body.mat"); }
            bodyMat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Models/DK_Dokkaebi_BaseColor.jpg"));
            bodyMat.SetFloat("_Smoothness", 0.15f);
            EditorUtility.SetDirty(bodyMat);

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
            go.name = "DK_DokkaebiBoss";
            go.transform.localScale = Vector3.one * ModelScale;
            Renderer club = null;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (r.name.Contains("Club")) { club = r; continue; }
                r.sharedMaterials = r.sharedMaterials.Select(_ => bodyMat).ToArray();
            }
            var anim = go.GetComponent<Animator>() ?? go.AddComponent<Animator>();
            anim.runtimeAnimatorController = ctrl; anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            var fxGo = new GameObject("SlamFx");
            fxGo.transform.SetParent(go.transform, false);
            fxGo.transform.localScale = Vector3.one / ModelScale;
            var fx = fxGo.AddComponent<DokkaebiSlamFx>();
            DokkaebiBuilder.Wire(fx);

            var boss = go.AddComponent<DokkaebiBoss>();
            boss.slamFx = fx; boss.clubRenderer = club;
            boss.impactTime = (ImpactFrame - 1f) / 30f;
            boss.slamLength = slamClip.length;
            // where the club lands, in boss space, sampled from the clip
            slamClip.SampleAnimation(go, boss.impactTime);
            var head = boss.ClubHead(); head.y = go.transform.position.y;
            boss.localImpact = go.transform.InverseTransformPoint(head);
            walkClip.SampleAnimation(go, 0f);
            Debug.Log($"[Dokkaebi] impact at {boss.impactTime:0.00}s, local {boss.localImpact}, slam {slamClip.length:0.00}s, walk {walkClip.length:0.00}s");

            PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
            Object.DestroyImmediate(go);
            BuildDemoScene();
            AssetDatabase.SaveAssets();
            Debug.Log("[Dokkaebi] boss prefab + demo scene built");
        }

        static void BuildDemoScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var rootGo = new GameObject("DokkaebiDemo");
            SceneManager.MoveGameObjectToScene(rootGo, scene);
            var root = rootGo.transform;
            root.position = DemoOrigin;

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.transform.SetParent(root, false);
            sun.type = LightType.Directional; sun.intensity = 1.6f; sun.color = new Color(1f, 0.9f, 0.78f);
            sun.shadows = LightShadows.Soft; sun.transform.rotation = Quaternion.Euler(52, -35, 0);

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground"; ground.transform.SetParent(root, false); ground.transform.localScale = Vector3.one * 4;
            var gm = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            gm.SetColor("_BaseColor", new Color(0.72f, 0.64f, 0.55f)); gm.SetFloat("_Smoothness", 0.05f);
            AssetDatabase.CreateAsset(gm, Root + "/Materials/DK_DemoGround.mat");
            ground.GetComponent<Renderer>().sharedMaterial = gm;

            var targetGo = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            targetGo.name = "Target"; targetGo.transform.SetParent(root, false); targetGo.transform.localPosition = new Vector3(0, 1, -7);
            Object.DestroyImmediate(targetGo.GetComponent<Collider>());

            var pivot = new GameObject("CamPivot").transform; pivot.SetParent(root, false);
            pivot.localPosition = new Vector3(0, 0, -2.5f); pivot.localRotation = Quaternion.Euler(50, 0, 0);
            var camGo = new GameObject("Camera"); camGo.transform.SetParent(pivot, false); camGo.transform.localPosition = new Vector3(0, 0, -20f);
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>(); cam.fieldOfView = 30; cam.nearClipPlane = 0.5f; cam.farClipPlane = 100; cam.allowHDR = true;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.2f, 0.18f, 0.18f);
            var acd = cam.GetUniversalAdditionalCameraData(); acd.renderPostProcessing = true; acd.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            var shake = camGo.AddComponent<DokkaebiCameraShake>();

            var volGo = new GameObject("Volume"); volGo.transform.SetParent(root, false);
            var vol = volGo.AddComponent<Volume>(); vol.isGlobal = true;
            var profPath = Root + "/Materials/DK_DemoVolume.asset";
            var prof = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profPath);
            if (!prof)
            {
                prof = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(prof, profPath);
                var bloom = prof.Add<Bloom>(true); bloom.intensity.Override(1.1f); bloom.threshold.Override(1f); bloom.scatter.Override(0.65f);
                var tm = prof.Add<Tonemapping>(true); tm.mode.Override(TonemappingMode.Neutral);
                foreach (var c in prof.components) AssetDatabase.AddObjectToAsset(c, prof);
            }
            vol.sharedProfile = prof;

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var boss = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            boss.transform.SetParent(root, false); boss.transform.localPosition = new Vector3(0, 0, 4); boss.transform.localRotation = Quaternion.Euler(0, 180, 0);
            var b = boss.GetComponent<DokkaebiBoss>(); b.target = targetGo.transform;
            b.slamFx.cameraShake = shake; b.slamFx.viewCamera = camGo.transform;

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorSceneManager.CloseScene(scene, true);
        }

        // Offscreen capture of the demo scene: walk in, slam. Loads the scene additively and closes it again.
        [MenuItem("Tools/Dokkaebi/Render Boss Preview")]
        public static void RenderPreview()
        {
            string dir = Path.GetFullPath("ArtSource/Dokkaebi/capture");
            Directory.CreateDirectory(dir);
            foreach (var f in Directory.GetFiles(dir, "f*.png")) File.Delete(f);
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                var boss = Object.FindObjectsByType<DokkaebiBoss>(FindObjectsSortMode.None).First(x => x.gameObject.scene == scene);
                var cam = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Camera>()).First();
                var shake = cam.GetComponent<DokkaebiCameraShake>();
                var fx = boss.slamFx; fx.transform.SetParent(null, true); SceneManager.MoveGameObjectToScene(fx.gameObject, scene);
                fx.manualTick = true; shake.manualTick = true; fx.Init();
                var all = AssetDatabase.LoadAllAssetsAtPath(ModelPath);
                var walk = all.OfType<AnimationClip>().First(c => c.name == "DK_Walk");
                var slam = all.OfType<AnimationClip>().First(c => c.name == "DK_Slam");

                const int W = 960, H = 640, FPS = 30;
                var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGBHalf) { antiAliasing = 4 };
                var outRt = new RenderTexture(W, H, 0, RenderTextureFormat.ARGB32);
                var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
                float walkTime = 2.0f, total = walkTime + boss.slamLength + 1.2f, dt = 1f / FPS;
                bool tele = false, hit = false;
                Random.InitState(7);
                int frames = Mathf.RoundToInt(total * FPS);
                for (int i = 0; i < frames; i++)
                {
                    float t = i * dt;
                    if (t < walkTime)
                    {
                        walk.SampleAnimation(boss.gameObject, t % walk.length);
                        boss.transform.position += boss.transform.forward * boss.walkSpeed * dt;
                    }
                    else
                    {
                        float st = Mathf.Min(t - walkTime, slam.length - 0.001f);
                        slam.SampleAnimation(boss.gameObject, st);
                        if (!tele && st >= boss.impactTime - boss.telegraphLead) { tele = true; var p = boss.transform.TransformPoint(boss.localImpact); p.y = boss.transform.position.y; fx.Telegraph(p, boss.telegraphLead); }
                        if (!hit && st >= boss.impactTime) { hit = true; var p = boss.ClubHead(); p.y = boss.transform.position.y; fx.Impact(p); }
                    }
                    fx.Tick(dt); shake.Tick(dt);
                    cam.targetTexture = rt; cam.Render(); cam.targetTexture = null;
                    Graphics.Blit(rt, outRt);
                    RenderTexture.active = outRt; tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply(); RenderTexture.active = null;
                    File.WriteAllBytes(Path.Combine(dir, $"f{i:D3}.png"), tex.EncodeToPNG());
                }
                Object.DestroyImmediate(rt); Object.DestroyImmediate(outRt); Object.DestroyImmediate(tex);
                Debug.Log($"[Dokkaebi] preview {frames} frames -> {dir}");
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }
    }
}
