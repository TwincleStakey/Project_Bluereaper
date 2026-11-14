// Builds the upper/lower split test: Mixamo import settings (Humanoid, Y Bot avatar), avatar masks, the animator
// controller, materials, the greatsword prefab and the test scene. Run through ArtSource/LocoSplit/ls_cmd.txt
// ("build"), see LSRemote. No menu items.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace LocoSplit
{
    public static class LSBuilder
    {
        public const string Root = "Assets/Game/LocoSplit";
        const string MixDir = Root + "/Art/Mixamo";
        const string SwordDir = Root + "/Art/Greatsword";
        const string GenDir = Root + "/Generated";
        public const string ScenePath = Root + "/LocoSplit.unity";

        // Mixamo file name (without .fbx) for each role. Missing optional clips are skipped.
        static readonly string Character = "YBot";
        static readonly string[] LoopClips = { "GS_Idle", "GS_WalkFwd", "GS_WalkBack", "GS_StrafeL", "GS_StrafeR",
            "GS_WalkFwdL", "GS_WalkFwdR", "GS_WalkBackL", "GS_WalkBackR" };

        public static string Build()
        {
            var log = new List<string>();
            Directory.CreateDirectory(GenDir);
            if (!SetupImports(log)) return string.Join("\n", log);

            SetupHunterRig(log);
            var upper = MakeMask("UpperMask", true);
            var ctrl = MakeController(log, upper);
            var mats = MakeMaterials();
            var sword = MakeSword(log, mats.sword);
            MakeScene(log, ctrl, sword, mats);
            AssetDatabase.SaveAssets();
            log.Add("build done");
            return string.Join("\n", log);
        }

        // ---------- imports ----------
        static bool SetupImports(List<string> log)
        {
            string charPath = $"{MixDir}/{Character}.fbx";
            var ci = AssetImporter.GetAtPath(charPath) as ModelImporter;
            if (ci == null) { log.Add("missing " + charPath); return false; }
            bool dirty = false;
            if (ci.animationType != ModelImporterAnimationType.Human || ci.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel)
            {
                ci.animationType = ModelImporterAnimationType.Human;
                ci.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                ci.importAnimation = false;
                ci.SaveAndReimport();
            }
            var avatar = AssetDatabase.LoadAllAssetsAtPath(charPath).OfType<Avatar>().FirstOrDefault();
            if (!avatar || !avatar.isHuman) { log.Add("Y Bot avatar not human"); return false; }

            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { MixDir }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (p == charPath) continue;
                var mi = AssetImporter.GetAtPath(p) as ModelImporter;
                if (mi == null) continue;
                string name = Path.GetFileNameWithoutExtension(p);
                bool loop = LoopClips.Contains(name);
                dirty = false;
                if (mi.animationType != ModelImporterAnimationType.Human || mi.sourceAvatar != avatar)
                {
                    mi.animationType = ModelImporterAnimationType.Human;
                    mi.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                    mi.sourceAvatar = avatar;
                    dirty = true;
                }
                if (mi.materialImportMode != ModelImporterMaterialImportMode.None) { mi.materialImportMode = ModelImporterMaterialImportMode.None; dirty = true; }
                var clips = mi.clipAnimations;
                if (clips == null || clips.Length == 0) clips = mi.defaultClipAnimations;
                foreach (var c in clips)
                {
                    if (c.name != name || c.loopTime != loop || !c.lockRootRotation)
                    {
                        c.name = name;
                        c.loopTime = loop;
                        c.lockRootRotation = true; c.keepOriginalOrientation = true;
                        c.lockRootHeightY = true; c.keepOriginalPositionY = true;
                        c.lockRootPositionXZ = true; c.keepOriginalPositionXZ = true;
                        dirty = true;
                    }
                }
                if (dirty) { mi.clipAnimations = clips; mi.SaveAndReimport(); }
            }
            log.Add("imports ok");
            return true;
        }

        // The 착호갑사 rebuilt on a Mixamo-named skeleton (ArtSource/LocoSplit/hunter_rig.py). Used instead of Y Bot's mesh.
        const string HunterRigPath = Root + "/Art/HunterRig/Hunter_Rig.fbx";
        static void SetupHunterRig(List<string> log)
        {
            var hi = AssetImporter.GetAtPath(HunterRigPath) as ModelImporter;
            if (hi == null) return;
            if (hi.animationType != ModelImporterAnimationType.Human || hi.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel || hi.importAnimation)
            {
                hi.animationType = ModelImporterAnimationType.Human;
                hi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                hi.importAnimation = false;
                hi.SaveAndReimport();
            }
            var av = AssetDatabase.LoadAllAssetsAtPath(HunterRigPath).OfType<Avatar>().FirstOrDefault();
            log.Add("hunter rig avatar " + (av && av.isValid && av.isHuman ? "ok" : "NOT HUMAN"));
        }

        static AnimationClip Clip(string name)
        {
            string p = $"{MixDir}/{name}.fbx";
            return AssetDatabase.LoadAllAssetsAtPath(p).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
        }

        // ---------- masks + controller ----------
        static AvatarMask MakeMask(string name, bool upperBody)
        {
            string p = $"{GenDir}/{name}.mask";
            var m = AssetDatabase.LoadAssetAtPath<AvatarMask>(p);
            if (!m) { m = new AvatarMask(); AssetDatabase.CreateAsset(m, p); }
            for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
            {
                var part = (AvatarMaskBodyPart)i;
                bool up = part == AvatarMaskBodyPart.Body || part == AvatarMaskBodyPart.Head ||
                          part == AvatarMaskBodyPart.LeftArm || part == AvatarMaskBodyPart.RightArm ||
                          part == AvatarMaskBodyPart.LeftFingers || part == AvatarMaskBodyPart.RightFingers ||
                          part == AvatarMaskBodyPart.LeftHandIK || part == AvatarMaskBodyPart.RightHandIK;
                m.SetHumanoidBodyPartActive(part, upperBody ? up : !up);
            }
            EditorUtility.SetDirty(m);
            return m;
        }

        static AnimatorController MakeController(List<string> log, AvatarMask upper)
        {
            string p = $"{GenDir}/LocoSplit.controller";
            AssetDatabase.DeleteAsset(p);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(p);
            ctrl.AddParameter("MoveX", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("MoveZ", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
            ctrl.AddParameter("FullAttack", AnimatorControllerParameterType.Bool);

            // Base: 8-direction locomotion (freeform directional).
            var baseSm = ctrl.layers[0].stateMachine;
            var tree = new BlendTree { name = "Locomotion", blendType = BlendTreeType.FreeformDirectional2D,
                blendParameter = "MoveX", blendParameterY = "MoveZ", useAutomaticThresholds = false };
            AssetDatabase.AddObjectToAsset(tree, ctrl);
            void Add(string clip, float x, float z)
            {
                var c = Clip(clip);
                if (c) tree.AddChild(c, new Vector2(x, z)); else log.Add("no clip " + clip);
            }
            Add("GS_Idle", 0, 0);
            Add("GS_WalkFwd", 0, 1);
            Add("GS_WalkBack", 0, -1);
            Add("GS_StrafeL", -1, 0);
            Add("GS_StrafeR", 1, 0);
            if (Clip("GS_WalkFwdL")) Add("GS_WalkFwdL", -0.707f, 0.707f);
            if (Clip("GS_WalkFwdR")) Add("GS_WalkFwdR", 0.707f, 0.707f);
            if (Clip("GS_WalkBackL")) Add("GS_WalkBackL", -0.707f, -0.707f);
            if (Clip("GS_WalkBackR")) Add("GS_WalkBackR", 0.707f, -0.707f);
            var loco = baseSm.AddState("Locomotion");
            loco.motion = tree;

            // Upper: guard pose over the legs, slashes on Attack (upper-body only mode).
            ctrl.AddLayer("Upper");
            var layers = ctrl.layers;
            layers[1].avatarMask = upper;
            layers[1].blendingMode = AnimatorLayerBlendingMode.Override;
            layers[1].defaultWeight = 1f;
            ctrl.layers = layers;
            var upSm = ctrl.layers[1].stateMachine;
            var guard = upSm.AddState("Guard"); guard.motion = Clip("GS_Idle");
            AddCombo(upSm, guard, "Up", false);

            // AttackFull: no mask, weight driven by LSPlayer while a full-body slash plays.
            ctrl.AddLayer("AttackFull");
            layers = ctrl.layers;
            layers[2].defaultWeight = 0f;
            ctrl.layers = layers;
            var fullSm = ctrl.layers[2].stateMachine;
            var empty = fullSm.AddState("Empty");
            AddCombo(fullSm, empty, "Full", true);

            EditorUtility.SetDirty(ctrl);
            log.Add("controller ok, loco children " + tree.children.Length);
            return ctrl;
        }

        static void AddCombo(AnimatorStateMachine sm, AnimatorState rest, string tag, bool full)
        {
            var s1c = Clip("GS_Slash1"); var s2c = Clip("GS_Slash2");
            if (!s1c) return;
            var s1 = sm.AddState(tag + "Slash1"); s1.motion = s1c;
            var t = rest.AddTransition(s1);
            t.hasExitTime = false; t.duration = 0.08f;
            t.AddCondition(AnimatorConditionMode.If, 0, "Attack");
            t.AddCondition(full ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0, "FullAttack");
            var back1 = s1.AddTransition(rest); back1.hasExitTime = true; back1.exitTime = 0.9f; back1.duration = 0.15f;
            if (s2c)
            {
                var s2 = sm.AddState(tag + "Slash2"); s2.motion = s2c;
                var c = s1.AddTransition(s2); c.hasExitTime = true; c.exitTime = 0.55f; c.duration = 0.1f;
                c.AddCondition(AnimatorConditionMode.If, 0, "Attack");
                var back2 = s2.AddTransition(rest); back2.hasExitTime = true; back2.exitTime = 0.9f; back2.duration = 0.15f;
            }
        }

        // ---------- materials + sword ----------
        struct Mats { public Material sword, robe, ground, sash, body, joints, hat; }

        static Material Lit(string name, Color c)
        {
            string p = $"{GenDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, p); }
            m.SetColor("_BaseColor", c);
            return m;
        }

        static Mats MakeMaterials()
        {
            var mats = new Mats();
            mats.sword = Lit("M_Greatsword", Color.white);
            var alb = AssetDatabase.LoadAssetAtPath<Texture2D>($"{SwordDir}/DefaultMaterial_albedo.jpg");
            var nrm = $"{SwordDir}/DefaultMaterial_normal.png";
            if (AssetImporter.GetAtPath(nrm) is TextureImporter ti && ti.textureType != TextureImporterType.NormalMap)
            { ti.textureType = TextureImporterType.NormalMap; ti.SaveAndReimport(); }
            mats.sword.SetTexture("_BaseMap", alb);
            mats.sword.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(nrm));
            mats.sword.EnableKeyword("_NORMALMAP");
            mats.sword.SetTexture("_OcclusionMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{SwordDir}/DefaultMaterial_AO.jpg"));
            mats.sword.EnableKeyword("_OCCLUSIONMAP");
            mats.sword.SetFloat("_Metallic", 0.7f);
            mats.sword.SetFloat("_Smoothness", 0.55f);

            mats.robe = Lit("M_Robe", new Color(0.83f, 0.80f, 0.72f));
            mats.robe.SetFloat("_Cull", (float)CullMode.Off);
            mats.robe.SetFloat("_Smoothness", 0.15f);
            mats.robe.doubleSidedGI = true;

            mats.robe.SetColor("_BaseColor", new Color(0.13f, 0.2f, 0.36f));   // indigo, like the 착호갑사
            mats.sash = Lit("M_Sash", new Color(0.62f, 0.1f, 0.08f));
            mats.body = Lit("M_Body", new Color(0.07f, 0.07f, 0.08f));
            mats.body.SetFloat("_Smoothness", 0.35f);
            mats.joints = Lit("M_Joints", new Color(0.85f, 0.82f, 0.74f));
            mats.hat = Lit("M_Hat", Color.white);
            mats.hat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Art/KHS/T_Hat_BC.png"));
            mats.hat.SetFloat("_Smoothness", 0.3f);
            foreach (var m in new[] { mats.sash, mats.body, mats.joints, mats.hat }) EditorUtility.SetDirty(m);
            mats.ground = Lit("M_Ground", new Color(0.36f, 0.38f, 0.36f));
            mats.ground.SetFloat("_Smoothness", 0.1f);
            foreach (var m in new[] { mats.sword, mats.robe, mats.ground }) EditorUtility.SetDirty(m);
            return mats;
        }

        // Wraps the downloaded model so the grip sits at the origin and the blade points along +Z.
        static GameObject MakeSword(List<string> log, Material mat)
        {
            string src = $"{SwordDir}/ChevalierSword.dae";
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(src);
            if (!model) { log.Add("missing sword " + src); return null; }
            var rootGo = new GameObject("Greatsword");
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            inst.transform.SetParent(rootGo.transform, false);
            foreach (var r in inst.GetComponentsInChildren<Renderer>()) r.sharedMaterial = mat;

            // Collect world-space verts to find the long axis, the guard (widest slice) and the pommel end.
            var pts = new List<Vector3>();
            foreach (var mf in inst.GetComponentsInChildren<MeshFilter>())
                foreach (var v in mf.sharedMesh.vertices) pts.Add(mf.transform.TransformPoint(v));
            var b = new Bounds(pts[0], Vector3.zero);
            foreach (var p in pts) b.Encapsulate(p);
            int axis = b.size.x >= b.size.y && b.size.x >= b.size.z ? 0 : b.size.y >= b.size.z ? 1 : 2;
            float lo = b.min[axis], hi = b.max[axis], len = hi - lo;
            const int slices = 40;
            var width = new float[slices];
            foreach (var p in pts)
            {
                int s = Mathf.Clamp((int)((p[axis] - lo) / len * slices), 0, slices - 1);
                Vector3 off = p - b.center; off[axis] = 0;
                width[s] = Mathf.Max(width[s], off.magnitude);
            }
            int guard = System.Array.IndexOf(width, width.Max());
            bool pommelAtLo = guard < slices / 2;
            float guardPos = lo + (guard + 0.5f) / slices * len;
            float pommelPos = pommelAtLo ? lo : hi;
            float gripPos = Mathf.Lerp(guardPos, pommelPos, 0.35f);
            Vector3 grip = b.center; grip[axis] = gripPos;
            Vector3 bladeDir = Vector3.zero; bladeDir[axis] = pommelAtLo ? 1 : -1;

            // Scale to a 1.45 m greatsword; grip to origin; blade to +Z.
            float scale = 1.45f / len;
            inst.transform.localScale *= scale;
            var rot = Quaternion.FromToRotation(bladeDir, Vector3.forward);
            inst.transform.localRotation = rot * inst.transform.localRotation;
            inst.transform.localPosition = -(rot * (grip * scale));
            string pp = $"{GenDir}/Greatsword.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(rootGo, pp);
            Object.DestroyImmediate(rootGo);
            log.Add($"sword axis {axis} len {len:0.00} guard slice {guard} pommelLo {pommelAtLo}");
            return prefab;
        }

        static GameObject MakeHat(List<string> log, Material mat)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Art/KHS/Hat_Jurib.fbx");
            if (!model) { log.Add("missing hat"); return null; }
            var rootGo = new GameObject("Hat");
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            inst.transform.SetParent(rootGo.transform, false);
            foreach (var r in inst.GetComponentsInChildren<Renderer>()) r.sharedMaterial = mat;
            var prefab = PrefabUtility.SaveAsPrefabAsset(rootGo, $"{GenDir}/Hat.prefab");
            Object.DestroyImmediate(rootGo);
            return prefab;
        }

        const string RRMixDir = Root + "/Art/MixamoRR";

        // Grounded greatsword set for the RoomRun player (11-13). Root Y baked into the pose based on the feet, root
        // rotation baked, root XZ left as root motion (discarded: applyRootMotion off) so attacks never drift or hop.
        static void SetupRRImports(List<string> log)
        {
            var avatar = AssetDatabase.LoadAllAssetsAtPath($"{MixDir}/{Character}.fbx").OfType<Avatar>().FirstOrDefault();
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { RRMixDir }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                var mi = AssetImporter.GetAtPath(p) as ModelImporter;
                if (mi == null) continue;
                string name = Path.GetFileNameWithoutExtension(p);
                mi.animationType = ModelImporterAnimationType.Human;
                mi.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                mi.sourceAvatar = avatar;
                mi.materialImportMode = ModelImporterMaterialImportMode.None;
                var src = mi.defaultClipAnimations[0];
                var list = new List<ModelImporterClipAnimation>();
                // bakeTurn false: the clip's body yaw becomes root motion (discarded), so the body stays on the aim
                void Part(string clipName, float a, float b, bool loop, bool mirror = false, bool bakeTurn = true)
                {
                    list.Add(new ModelImporterClipAnimation
                    {
                        takeName = src.takeName, name = clipName,
                        firstFrame = a < 0 ? src.firstFrame : a, lastFrame = b < 0 ? src.lastFrame : b,
                        loopTime = loop, loopPose = loop, mirror = mirror,
                        lockRootRotation = bakeTurn, keepOriginalOrientation = true,
                        lockRootHeightY = true, keepOriginalPositionY = false, heightFromFeet = true,
                        lockRootPositionXZ = false, keepOriginalPositionXZ = false,
                    });
                }
                if (name == "GS_ComboSlash")
                {
                    // hand speed peaks near frames 23, 50, 79; only the middle swing is a flat sideways sweep (11-13 Kido:
                    // horizontal, alternating sides), so it is used as is and mirrored
                    Part("GS_SweepA", 42, 66, false, false, false);
                    Part("GS_SweepB", 42, 66, false, true, false);
                }
                else if (name == "GS_LowSlash") Part("GS_SweepC", 10, 50, false, false, false);
                else if (name == "GS_SpinAttack") Part(name, -1, -1, false);
                else Part(name, -1, -1, name.StartsWith("GS_Run"));
                mi.clipAnimations = list.ToArray();
                mi.SaveAndReimport();
            }
            log.Add("rr imports ok");
        }

        static AnimationClip RRClip(string file, string clip)
        {
            return AssetDatabase.LoadAllAssetsAtPath($"{RRMixDir}/{file}.fbx").OfType<AnimationClip>().FirstOrDefault(c => c.name == clip);
        }

        // Base: idle/walk (LocoSplit set) at 0.35, runs at 1 (MoveX/MoveZ = velocity / move speed in facing space).
        // AttackFull: unmasked, states driven by RoomRun.GreatswordCombat with CrossFade; speed from AtkSpeed.
        static RuntimeAnimatorController MakeRRController(List<string> log)
        {
            string p = $"{GenDir}/RR_Greatsword.controller";
            AssetDatabase.DeleteAsset(p);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(p);
            ctrl.AddParameter("MoveX", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("MoveZ", AnimatorControllerParameterType.Float);
            ctrl.AddParameter(new AnimatorControllerParameter { name = "LocoSpeed", type = AnimatorControllerParameterType.Float, defaultFloat = 1f });
            ctrl.AddParameter(new AnimatorControllerParameter { name = "AtkSpeed", type = AnimatorControllerParameterType.Float, defaultFloat = 1f });

            var tree = new BlendTree { name = "Locomotion", blendType = BlendTreeType.FreeformDirectional2D,
                blendParameter = "MoveX", blendParameterY = "MoveZ", useAutomaticThresholds = false };
            AssetDatabase.AddObjectToAsset(tree, ctrl);
            void Add(AnimationClip c, float x, float z) { if (c) tree.AddChild(c, new Vector2(x, z)); else log.Add($"rr: no clip for {x},{z}"); }
            const float w = 0.35f;
            Add(Clip("GS_Idle"), 0, 0);
            Add(Clip("GS_WalkFwd"), 0, w); Add(Clip("GS_WalkBack"), 0, -w);
            Add(Clip("GS_StrafeL"), -w, 0); Add(Clip("GS_StrafeR"), w, 0);
            Add(RRClip("GS_RunFwd", "GS_RunFwd"), 0, 1); Add(RRClip("GS_RunBack", "GS_RunBack"), 0, -1);
            Add(RRClip("GS_RunStrafeL", "GS_RunStrafeL"), -1, 0); Add(RRClip("GS_RunStrafeR", "GS_RunStrafeR"), 1, 0);
            var baseSm = ctrl.layers[0].stateMachine;
            var loco = baseSm.AddState("Locomotion");
            loco.motion = tree; loco.iKOnFeet = true;
            loco.speedParameterActive = true; loco.speedParameter = "LocoSpeed";

            ctrl.AddLayer("AttackFull");
            var layers = ctrl.layers;
            layers[1].defaultWeight = 0f;
            ctrl.layers = layers;
            var sm = ctrl.layers[1].stateMachine;
            sm.AddState("Empty");
            void Atk(string state, AnimationClip c)
            {
                if (!c) { log.Add("rr: missing attack " + state); return; }
                var st = sm.AddState(state);
                st.motion = c; st.iKOnFeet = true;
                st.speedParameterActive = true; st.speedParameter = "AtkSpeed";
            }
            Atk("SweepA", RRClip("GS_ComboSlash", "GS_SweepA"));
            Atk("SweepB", RRClip("GS_ComboSlash", "GS_SweepB"));
            Atk("SweepC", RRClip("GS_LowSlash", "GS_SweepC"));
            Atk("Spin", RRClip("GS_SpinAttack", "GS_SpinAttack"));
            EditorUtility.SetDirty(ctrl);
            log.Add("rr controller ok, loco children " + tree.children.Length);
            return ctrl;
        }

        // ---------- RoomRun player (temporary look test, 11-13) ----------
        // The greatsword 착호갑사 as a prefab without LSPlayer (RoomRun's PlayerController + GreatswordCombat drive it),
        // assigned to RoomRunConfig.greatswordPrefab with the slash VFX. Needs a prior "build" (sword prefab, clips).
        public static string BuildRoomRunPlayer()
        {
            var log = new List<string>();
            SetupRRImports(log);
            var ctrl = MakeRRController(log);
            var sword = AssetDatabase.LoadAssetAtPath<GameObject>($"{GenDir}/Greatsword.prefab");
            var robeMat = AssetDatabase.LoadAssetAtPath<Material>($"{GenDir}/M_Robe.mat");
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(HunterRigPath);
            if (!ctrl || !sword || !model) { log.Add("rrplayer: missing controller/sword/hunter rig, run build first"); return string.Join("\n", log); }

            var root = new GameObject("RR_GreatswordPlayer");
            var body = (GameObject)PrefabUtility.InstantiatePrefab(model);
            body.transform.SetParent(root.transform, false);
            foreach (var r in body.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var ms = r.sharedMaterials;
                for (int i = 0; i < ms.Length; i++)
                {
                    var dd = ms[i] ? AssetDatabase.LoadAssetAtPath<Material>($"Assets/Art/DDConcept/Materials/{ms[i].name}.mat") : null;
                    if (dd) ms[i] = dd;
                }
                r.sharedMaterials = ms;
                r.updateWhenOffscreen = true;
            }
            var anim = body.GetComponent<Animator>() ?? body.AddComponent<Animator>();
            anim.runtimeAnimatorController = ctrl;
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var rig = root.AddComponent<LSRig>();
            rig.anim = anim; rig.swordPrefab = sword; rig.robeMaterial = robeMat;
            var robe = root.AddComponent<LSRobeCloth>();
            robe.anim = anim; robe.material = robeMat;
            robe.thighRadius = 0.06f; robe.shinRadius = 0.05f; robe.maxSwing = 0.2f;   // heavy cloth (11-13), see LSRobeCloth
            var fix = root.AddComponent<LSPoseFix>();   // upright idle + blade clears the hat (11-13)
            fix.anim = anim;

            string pp = $"{GenDir}/RR_GreatswordPlayer.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, pp);
            Object.DestroyImmediate(root);

            var cfg = AssetDatabase.LoadAssetAtPath<RoomRun.RoomRunConfig>("Assets/Game/RoomRun/RoomRunConfig.asset");
            if (!cfg) { log.Add("rrplayer: prefab saved, RoomRunConfig not found"); return string.Join("\n", log); }
            var so = new SerializedObject(cfg);
            so.FindProperty("greatswordPrefab").objectReferenceValue = prefab;
            // 11-13 Kido picked Slash Water laid flat: a Local-aligned copy made by ls_cmd `waterflat` (pack prefab untouched)
            var vfx = AssetDatabase.LoadAssetAtPath<GameObject>($"{GenDir}/GS_SlashWater_Flat.prefab")
                      ?? AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Free Slash VFX/Prefabs/Slash Eletric VFX.prefab");
            var vfxBig = vfx;
            var grassTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/LookStudy/Models/Real/GrassGrass_MAT_baseColor.png");
            so.FindProperty("slashVfx").objectReferenceValue = vfx;
            so.FindProperty("slashVfxFinisher").objectReferenceValue = vfxBig ? vfxBig : vfx;
            so.FindProperty("cutBitsTexture").objectReferenceValue = grassTex;
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            log.Add($"rrplayer: saved {pp}; vfx {(vfx ? "ok" : "MISSING")}, finisher {(vfxBig ? "ok" : "MISSING")}, grass tex {(grassTex ? "ok" : "MISSING")}");
            return string.Join("\n", log);
        }

        // ---------- scene ----------
        static void MakeScene(List<string> log, AnimatorController ctrl, GameObject sword, Mats mats)
        {
            if (EditorSceneManager.GetActiveScene().isDirty) { log.Add("open scene has unsaved changes, scene not rebuilt"); return; }
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var light = new GameObject("Sun").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.6f;
            light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(50, -35, 0);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.55f, 0.6f, 0.7f);
            RenderSettings.ambientEquatorColor = new Color(0.4f, 0.4f, 0.4f);
            RenderSettings.ambientGroundColor = new Color(0.2f, 0.2f, 0.2f);

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(6, 1, 6);
            ground.GetComponent<Renderer>().sharedMaterial = mats.ground;
            // a few markers so movement reads on screen
            for (int x = -5; x <= 5; x++)
                for (int z = -5; z <= 5; z++)
                {
                    if ((x + z) % 2 != 0) continue;
                    var m = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    m.name = "Marker";
                    Object.DestroyImmediate(m.GetComponent<Collider>());
                    m.transform.position = new Vector3(x * 4, 0.01f, z * 4);
                    m.transform.localScale = new Vector3(0.25f, 0.02f, 0.25f);
                    m.GetComponent<Renderer>().sharedMaterial = Lit("M_Marker", new Color(0.55f, 0.55f, 0.5f));
                }

            var player = new GameObject("Player");
            var hunterRig = AssetDatabase.LoadAssetAtPath<GameObject>(HunterRigPath);
            bool isHunter = hunterRig != null;
            var charModel = isHunter ? hunterRig : AssetDatabase.LoadAssetAtPath<GameObject>($"{MixDir}/{Character}.fbx");
            var body = (GameObject)PrefabUtility.InstantiatePrefab(charModel);
            body.transform.SetParent(player.transform, false);
            var anim = body.GetComponent<Animator>() ?? body.AddComponent<Animator>();
            anim.runtimeAnimatorController = ctrl;
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 40;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.2f, 0.22f, 0.25f);
            camGo.AddComponent<AudioListener>();
            var camRig = camGo.AddComponent<LSCamera>();
            camRig.target = player.transform;
            camRig.distance = 7f;

            // dark jacket look on the base mesh (joints read as white undershirt/leggings)
            if (isHunter)
            {
                // the concept's own flat colours, matched by material slot name
                foreach (var r in body.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    var ms = r.sharedMaterials;
                    for (int i = 0; i < ms.Length; i++)
                    {
                        string n = ms[i] ? ms[i].name : "";
                        var dd = AssetDatabase.LoadAssetAtPath<Material>($"Assets/Art/DDConcept/Materials/{n}.mat");
                        if (dd) ms[i] = dd; else log.Add("no DD material for " + n);
                    }
                    r.sharedMaterials = ms;
                }
            }
            else
                foreach (var r in body.GetComponentsInChildren<SkinnedMeshRenderer>())
                    r.sharedMaterial = r.name.ToLower().Contains("joint") ? mats.joints : mats.body;

            var lp = player.AddComponent<LSPlayer>();
            lp.anim = anim; lp.cam = cam;
            var rig = player.AddComponent<LSRig>();
            rig.anim = anim; rig.swordPrefab = sword; rig.robeMaterial = mats.robe;
            rig.hatPrefab = isHunter ? null : MakeHat(log, mats.hat);
            var robe = player.AddComponent<LSRobeCloth>();
            robe.anim = anim; robe.material = mats.robe; robe.sashMaterial = isHunter ? null : mats.sash;
            if (isHunter) { robe.thighRadius = 0.06f; robe.shinRadius = 0.05f; robe.maxSwing = 0.3f; }
            var panel = player.AddComponent<LSDevPanel>();
            panel.player = lp; panel.rig = rig; panel.camRig = camRig; panel.robe = robe;
            var demo = player.AddComponent<LSDemo>();
            demo.player = lp; demo.cam = cam;

            // The 착호갑사 concept model as a robe-length reference, standing to the side.
            var hunter = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/DDConcept/Prefabs/Hunter.prefab");
            if (hunter)
            {
                var h = (GameObject)PrefabUtility.InstantiatePrefab(hunter);
                h.name = "Ref_Hunter";
                h.transform.position = new Vector3(-2.2f, 0, 1.5f);
                h.transform.rotation = Quaternion.Euler(0, 180, 0);
            }

            EditorSceneManager.SaveScene(scene, ScenePath);
            log.Add("scene saved " + ScenePath);
        }
    }
}
