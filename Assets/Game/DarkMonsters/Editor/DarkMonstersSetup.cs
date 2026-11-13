// Builds the dark-fantasy swarm monsters (Mixamo characters, decimated to ~4k tris in Blender) into RoomRun enemy prefabs:
// import settings (Humanoid, in-place clips), URP Lit materials, one Animator controller per monster with the states
// EnemyController plays (Move, Run, Attack, Die), and prefabs under Prefabs/; then points the RoomRun enemy kinds at them
// and keeps the old DDConcept prefabs as each kind's classicPrefab (the in-game dev panel's 기존 몬스터 toggle).
// Kido's rule: no new Unity menu items. Run it by writing "build" to ArtSource/dm_cmd.txt (picked up when the editor is idle).
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace DarkMonsters
{
    public static class DarkMonstersSetup
    {
        const string Root = "Assets/Game/DarkMonsters";
        const string ConfigPath = "Assets/Game/RoomRun/RoomRunConfig.asset";
        const float SelfLight = 0.15f;

        struct Monster
        {
            public string name, move, run, attack;
            public float height;     // model height in metres before EnemyDef.scale (11-13 Kido: x1.6, they were too small next to the player)
        }

        static readonly Monster[] Monsters =
        {
            new Monster { name = "DM_Goblin", move = "DM_Run", run = "DM_Run", attack = "DM_Swipe", height = 2.7f },
            new Monster { name = "DM_Maw", move = "DM_Walk", run = "DM_Run", attack = "DM_Roar", height = 3.3f },
            new Monster { name = "DM_Ganfaul", move = "DM_Walk", run = "DM_Run", attack = "DM_Cast", height = 3.0f },
            new Monster { name = "DM_Warrok", move = "DM_Walk", run = "DM_Run", attack = "DM_Swipe", height = 3.2f },
        };

        // RoomRun enemy id -> monster, and the prefab it used before (for Restore)
        static readonly (string id, string monster, string old)[] Assign =
        {
            ("shade", "DM_Goblin", "Assets/Art/DDConcept/Prefabs/Eodukshini.prefab"),
            ("ghost", "DM_Ganfaul", "Assets/Art/DDConcept/Prefabs/Gwisin.prefab"),
            ("fox", "DM_Maw", "Assets/Art/DDConcept/Prefabs/Gumiho.prefab"),
            ("dokkaebi", "DM_Warrok", "Assets/Art/DDConcept/Prefabs/Dokkaebi.prefab"),
            ("dokkaebi_elite", "DM_Warrok", "Assets/Art/DDConcept/Prefabs/Dokkaebi.prefab"),
        };

        [InitializeOnLoadMethod]
        static void Watch() => EditorApplication.update += Poll;

        static double next;
        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < next) return;
            next = EditorApplication.timeSinceStartup + 1.0;
            string cmd = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "ArtSource", "dm_cmd.txt");
            if (!File.Exists(cmd) || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            string what = File.ReadAllText(cmd).Trim();
            File.Delete(cmd);
            if (what == "build") { Build(); AssignToRoomRun(); }
            else Debug.LogWarning("[DarkMonsters] unknown dm_cmd: " + what);
        }

        public static void Build()
        {
            foreach (var dir in new[] { "Materials", "Controllers", "Prefabs" })
                if (!AssetDatabase.IsValidFolder($"{Root}/{dir}")) AssetDatabase.CreateFolder(Root, dir);

            SetupTextures();
            SetupAnims();
            foreach (var m in Monsters) BuildMonster(m);
            AssetDatabase.SaveAssets();
            Debug.Log("[DarkMonsters] built " + Monsters.Length + " prefabs");
        }

        static void SetupTextures()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Root + "/Textures" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var ti = (TextureImporter)AssetImporter.GetAtPath(path);
                bool normal = path.Contains("_normal");
                var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                if (ti.textureType == type && ti.maxTextureSize == 1024) continue;
                ti.textureType = type;
                ti.maxTextureSize = 1024;
                ti.textureCompression = TextureImporterCompression.Compressed;
                ti.SaveAndReimport();
            }
        }

        static void SetupAnims()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { Root + "/Anims" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var mi = (ModelImporter)AssetImporter.GetAtPath(path);
                string name = Path.GetFileNameWithoutExtension(path);
                bool loop = name == "DM_Walk" || name == "DM_Run";
                mi.animationType = ModelImporterAnimationType.Human;
                mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                mi.importAnimation = true;
                mi.materialImportMode = ModelImporterMaterialImportMode.None;
                var src = mi.defaultClipAnimations;
                var clips = new ModelImporterClipAnimation[src.Length];
                for (int i = 0; i < src.Length; i++)
                {
                    var c = src[i];
                    c.name = name;
                    c.loopTime = loop;
                    // in place: root rotation, height and XZ all baked into the pose
                    c.lockRootRotation = true; c.keepOriginalOrientation = true;
                    c.lockRootHeightY = true; c.keepOriginalPositionY = true;
                    c.lockRootPositionXZ = true; c.keepOriginalPositionXZ = true;
                    clips[i] = c;
                }
                mi.clipAnimations = clips;
                mi.SaveAndReimport();
            }
        }

        static AnimationClip Clip(string name)
        {
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath($"{Root}/Anims/{name}.fbx"))
                if (o is AnimationClip c && !c.name.StartsWith("__preview")) return c;
            Debug.LogError("[DarkMonsters] missing clip " + name);
            return null;
        }

        static void BuildMonster(Monster m)
        {
            string fbx = $"{Root}/Models/{m.name}.fbx";
            var mi = (ModelImporter)AssetImporter.GetAtPath(fbx);
            if (mi.animationType != ModelImporterAnimationType.Human || mi.importAnimation || mi.materialImportMode != ModelImporterMaterialImportMode.None)
            {
                mi.animationType = ModelImporterAnimationType.Human;
                mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                mi.importAnimation = false;
                mi.materialImportMode = ModelImporterMaterialImportMode.None;
                mi.SaveAndReimport();
            }

            // material
            string matPath = $"{Root}/Materials/{m.name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (!mat) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, matPath); }
            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{Root}/Textures/{m.name}_albedo.png"));
            var nrm = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Root}/Textures/{m.name}_normal.png");
            mat.SetTexture("_BumpMap", nrm);
            if (nrm) mat.EnableKeyword("_NORMALMAP"); else mat.DisableKeyword("_NORMALMAP");
            mat.SetFloat("_Smoothness", 0.22f);
            mat.SetFloat("_Metallic", 0f);
            // The fields are dark at night and the stage rim washes skinned bodies out, so the monsters carry a little of their
            // own albedo as emission to stay readable (Maw's antler-glow map is too small to matter at this distance).
            mat.SetTexture("_EmissionMap", mat.GetTexture("_BaseMap"));
            mat.SetColor("_EmissionColor", Color.white * SelfLight);
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);

            // controller: Move (speed scaled by MoveMul), Run, Attack (back to Move when done), Die (holds)
            string ctrlPath = $"{Root}/Controllers/{m.name}.controller";
            AssetDatabase.DeleteAsset(ctrlPath);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ctrlPath);
            ctrl.AddParameter("MoveMul", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("RunMul", AnimatorControllerParameterType.Float);
            var p = ctrl.parameters; p[0].defaultFloat = 1f; p[1].defaultFloat = 1f; ctrl.parameters = p;
            var sm = ctrl.layers[0].stateMachine;
            var move = sm.AddState("Move"); move.motion = Clip(m.move); move.speedParameter = "MoveMul"; move.speedParameterActive = true;
            var run = sm.AddState("Run"); run.motion = Clip(m.run); run.speedParameter = "RunMul"; run.speedParameterActive = true;
            var atk = sm.AddState("Attack"); atk.motion = Clip(m.attack);
            var die = sm.AddState("Die"); die.motion = Clip("DM_Die");
            sm.defaultState = move;
            var back = atk.AddTransition(move); back.hasExitTime = true; back.exitTime = 0.85f; back.duration = 0.15f;

            // prefab: root (EnemyController scales it) > model (normalised to the monster's height, carries the Animator)
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            var root = new GameObject(m.name);
            var inst = (GameObject)Object.Instantiate(model, root.transform);
            inst.name = "Model";
            var smrs = inst.GetComponentsInChildren<SkinnedMeshRenderer>();
            var b = new Bounds(); bool first = true;
            foreach (var r in smrs)
            {
                r.sharedMaterials = new[] { mat };
                r.updateWhenOffscreen = false;
                r.skinnedMotionVectors = false;
                r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds);
            }
            float k = b.size.y > 0.01f ? m.height / b.size.y : 1f;
            inst.transform.localScale = Vector3.one * k;
            inst.transform.localPosition = new Vector3(0, -b.min.y * k, 0);

            var anim = inst.GetComponent<Animator>();
            if (!anim) anim = inst.AddComponent<Animator>();
            anim.runtimeAnimatorController = ctrl;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(fbx)) if (o is Avatar av) anim.avatar = av;
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

            // stride of the clips, so EnemyController plays them at the speed the body moves (no foot sliding)
            var gait = root.AddComponent<RoomRun.EnemyGait>();
            gait.moveStride = Stride(m.move);
            gait.runStride = Stride(m.run);

            int tris = CountTris(smrs);
            PrefabUtility.SaveAsPrefabAsset(root, $"{Root}/Prefabs/{m.name}.prefab");
            Object.DestroyImmediate(root);
            Debug.Log($"[DarkMonsters] {m.name}: bounds {b.size} scale {k:0.###} tris {tris}");
        }

        // In-place clip ground speed in hip heights per second, from the planted foot (ArtSource/DarkMonsters/stride.py, 11-13)
        static float Stride(string clip) => clip == "DM_Run" ? 2.51f : 1.352f;

        static int CountTris(SkinnedMeshRenderer[] smrs)
        {
            int t = 0;
            foreach (var r in smrs) if (r.sharedMesh) t += r.sharedMesh.triangles.Length / 3;
            return t;
        }

        static void AssignToRoomRun()
        {
            var cfg = AssetDatabase.LoadAssetAtPath<RoomRun.RoomRunConfig>(ConfigPath);
            foreach (var (id, monster, old) in Assign)
            {
                var d = cfg.Enemy(id);
                if (d == null) continue;
                d.prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/Prefabs/{monster}.prefab");
                d.classicPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(old);
            }
            EditorUtility.SetDirty(cfg);
            AssetDatabase.SaveAssets();
            Debug.Log("[DarkMonsters] RoomRun enemies -> dark monsters (classic kept for the dev panel toggle)");
        }
    }
}
