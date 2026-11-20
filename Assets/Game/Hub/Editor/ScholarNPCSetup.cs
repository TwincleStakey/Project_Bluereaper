// Builds Assets/Game/Hub/ScholarNPC/ScholarNPC.prefab from Models/Scholar.fbx (11-20).
// Pivot = chair seat surface, facing +Z local, root scaled 1.3 (anchor must be unscaled). At 1.3x: seat 0.62, desk top at +0.39,
// near desk edge at +0.39 in front of the hips (paper, inkstone and brush come with the model).
// Run (no menu item, project rule): rr_queue "call Hub.ScholarNPCSetup.Build". Log: ArtSource/DeskMan/setup_log.txt
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Hub
{
    public static class ScholarNPCSetup
    {
        const string Dir = "Assets/Game/Hub/ScholarNPC";
        const string Fbx = Dir + "/Models/ScholarMx.fbx";   // 11-20: Mixamo auto-rig + Writing mocap (ArtSource/DeskMan/scripts/mx_build.py); old hand-keyed rig = Models/Scholar.fbx
        const string Tex = Dir + "/Textures/Scholar_Base.jpg";
        const string Prefab = Dir + "/ScholarNPC.prefab";
        const string Ctrl = Dir + "/Scholar.controller";
        const float HipAboveSeat = 0.07f;   // model units: hip joints above the seat surface
        const float Scale = 1.7f;       // 11-20 Kido: too small next to the 2.85 m player (1.3 -> 1.7); hub scales desk/chair the same
        static readonly string[] Loops = { "Sit_Write", "Sit_Attend" };
        static readonly List<string> log = new List<string>();
        static void Log(string s) { log.Add(s); Debug.Log("[ScholarNPC] " + s); }

        public static void Build()
        {
            log.Clear();
            try { BuildInner(); }
            catch (System.Exception e) { Log("ERROR " + e); }
            File.WriteAllLines(Path.Combine(Directory.GetParent(Application.dataPath).FullName, "ArtSource/DeskMan/setup_log.txt"), log);
        }

        static void BuildInner()
        {
            // --- importer ---
            var imp = (ModelImporter)AssetImporter.GetAtPath(Fbx);
            imp.animationType = ModelImporterAnimationType.Generic;
            imp.bakeAxisConversion = true;
            imp.importBlendShapes = false;
            imp.importCameras = false; imp.importLights = false;
            imp.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            imp.animationCompression = ModelImporterAnimationCompression.KeyframeReduction;
            imp.SaveAndReimport();
            var takes = imp.defaultClipAnimations;
            var clips = new List<ModelImporterClipAnimation>();
            foreach (var t in takes)
            {
                var n = t.name.Contains("|") ? t.name.Substring(t.name.LastIndexOf('|') + 1) : t.name;
                if (!n.StartsWith("Sit_")) { Log("skip take " + t.name); continue; }
                t.name = n;
                t.loopTime = Loops.Contains(n);
                t.loopPose = false;
                t.keepOriginalPositionY = t.keepOriginalOrientation = t.keepOriginalPositionXZ = true;
                clips.Add(t);
                Log($"clip {n} frames {t.firstFrame}-{t.lastFrame} loop {t.loopTime}");
            }
            imp.clipAnimations = clips.ToArray();
            imp.SaveAndReimport();

            var objs = AssetDatabase.LoadAllAssetsAtPath(Fbx);
            var anims = objs.OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")).ToDictionary(c => c.name);
            Log("anims: " + string.Join(", ", anims.Keys));

            // --- materials ---
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(Tex);
            var ti = (TextureImporter)AssetImporter.GetAtPath(Tex);
            if (ti.maxTextureSize != 2048) { ti.maxTextureSize = 2048; ti.SaveAndReimport(); }
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            Material Mat(string name, Color c, Texture2D t, float smooth)
            {
                var path = $"{Dir}/Materials/{name}.mat";
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (!m) { m = new Material(lit); AssetDatabase.CreateAsset(m, path); }
                m.shader = lit;
                m.SetColor("_BaseColor", c); m.SetTexture("_BaseMap", t);
                m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", 0f);
                EditorUtility.SetDirty(m);
                return m;
            }
            var body = Mat("Scholar_Body", Color.white, tex, 0.12f);
            var props = new Dictionary<string, Material>
            {
                { "SCH_Bamboo", Mat("SCH_Bamboo", new Color(0.42f, 0.30f, 0.16f), null, 0.3f) },
                { "SCH_BrushHair", Mat("SCH_BrushHair", new Color(0.03f, 0.03f, 0.03f), null, 0.4f) },
                { "SCH_Paper", Mat("SCH_Paper", new Color(0.86f, 0.80f, 0.66f), null, 0.05f) },
                { "SCH_Inkstone", Mat("SCH_Inkstone", new Color(0.06f, 0.06f, 0.07f), null, 0.6f) },
                { "SCH_Ink", Mat("SCH_Ink", new Color(0.01f, 0.01f, 0.012f), null, 0.9f) },
            };

            // --- controller ---
            AssetDatabase.DeleteAsset(Ctrl);
            var ac = AnimatorController.CreateAnimatorControllerAtPath(Ctrl);
            var sm = ac.layers[0].stateMachine;
            foreach (var n in new[] { "Sit_Write", "Sit_Dip", "Sit_Pause", "Sit_Attend" })
            {
                if (!anims.TryGetValue(n, out var c))
                {
                    if (n != "Sit_Attend") { Log("no clip " + n + " (state skipped)"); continue; }
                    // no talk clip yet: hold the writing pose still while the look-at turns him
                    c = anims["Sit_Write"]; Log("Sit_Attend = Sit_Write held still");
                    var hs = sm.AddState(n); hs.motion = c; hs.speed = 0f; continue;
                }
                var s = sm.AddState(n); s.motion = c;
                if (n == "Sit_Write") sm.defaultState = s;
            }

            // --- prefab ---
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
            var root = new GameObject("ScholarNPC");
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            inst.name = "Scholar";
            inst.transform.SetParent(root.transform, false);
            // facing: the paper must sit in front (+Z)
            var paper = inst.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Desk_Paper");
            if (paper && paper.position.z < 0) { inst.transform.localRotation = Quaternion.Euler(0, 180, 0); Log("rotated 180 to face +Z"); }
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var mn = mats[i] ? mats[i].name : "";
                    var key = props.Keys.FirstOrDefault(k => mn.StartsWith(k));
                    mats[i] = key != null ? props[key] : body;
                }
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
            var anim = inst.GetComponent<Animator>(); if (!anim) anim = inst.AddComponent<Animator>();
            anim.runtimeAnimatorController = ac;
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            var smr = inst.GetComponentInChildren<SkinnedMeshRenderer>();
            if (smr) smr.updateWhenOffscreen = false;

            var npc = root.AddComponent<ScholarNPC>();
            npc.animator = anim;
            Transform Find(string n) => inst.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == n);
            npc.chest = Find("mixamorig:Spine2") ?? Find("Chest"); npc.neck = Find("mixamorig:Neck") ?? Find("Neck"); npc.head = Find("mixamorig:Head") ?? Find("Head");
            Log($"bones chest {(bool)npc.chest} neck {(bool)npc.neck} head {(bool)npc.head}");

            // sample frame 0 of Sit_Write to report where things are (seat-relative)
            if (anims.TryGetValue("Sit_Write", out var w)) w.SampleAnimation(inst, 0f);
            // pivot = seat surface under the hips
            var hipsT = Find("mixamorig:Hips") ?? Find("Hips");
            var hl = root.transform.InverseTransformPoint(hipsT.position);
            inst.transform.localPosition -= new Vector3(hl.x, hl.y - HipAboveSeat, hl.z);
            Log($"hips were at {hl}, model offset {inst.transform.localPosition}");
            Log($"head local {root.transform.InverseTransformPoint(npc.head.position)}  paper local {(paper ? root.transform.InverseTransformPoint(paper.position) : Vector3.zero)}");
            var brush = Find("Brush");
            if (brush) Log($"brush local {root.transform.InverseTransformPoint(brush.position)}");
            var b = new Bounds(root.transform.position, Vector3.zero);
            foreach (var r in inst.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
            Log($"bounds {b.center} size {b.size}");

            root.transform.localScale = Vector3.one * Scale;
            PrefabUtility.SaveAsPrefabAsset(root, Prefab);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            Log("PREFAB_OK " + Prefab);
        }
    }
}
