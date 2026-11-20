// 장산범 quadruped move set (retargeted free clips, ArtSource/QuadAnim). Builds an Animator with every FBX in
// QuadAnim/Models, a boss prefab with the same materials as the glow test (JangsanbeomGlowSetup), and a Play-mode
// showcase scene in the BossSeal arena. Driven by trigger files (no menus needed):
//   Temp/jsbq_build.trigger            -> Build()            done stamp Temp/jsbq_build.done
//   Temp/jsbq_play.trigger  [record]   -> opens the showcase scene and enters Play (record = capture frames, then stop)
#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class JSBQuadAnimSetup
{
    const string Root = "Assets/Game/Jangsanbeom";
    const string Q = Root + "/QuadAnim";
    const float BossScale = 25f;
    const string ScenePath = Q + "/JSBQ_Showcase.unity";
    const string MapScene = "Assets/Game/BossSeal/BossSeal.unity";
    static readonly string[] Loops = { "idle", "walk", "trot", "gallop", "run" };

    [InitializeOnLoadMethod]
    static void Watch()
    {
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += s =>
        {   // our Play run used the showcase as start scene; give the start scene back to RoomRunPlayStart's setting
            if (s == PlayModeStateChange.EnteredEditMode && EditorPrefs.GetBool("JSBQ_Playing", false))
            {
                EditorPrefs.SetBool("JSBQ_Playing", false);
                if (EditorPrefs.HasKey("JSBQ_RoomRunPref")) { EditorPrefs.SetBool("RoomRun.AlwaysPlayFromRoomRun", EditorPrefs.GetBool("JSBQ_RoomRunPref")); EditorPrefs.DeleteKey("JSBQ_RoomRunPref"); }
                EditorSceneManager.playModeStartScene = EditorPrefs.GetBool("RoomRun.AlwaysPlayFromRoomRun", true) ? AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Game/RoomRun/Scenes/RoomRun.unity") : null;
                var back = EditorPrefs.GetString("JSBQ_ReturnScene", "");   // reopen the scene that was open before
                if (back.Length > 0 && back != ScenePath && File.Exists(back)) EditorApplication.delayCall += () => EditorSceneManager.OpenScene(back, OpenSceneMode.Single);
            }
        };
    }
    static void Tick()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (File.Exists("Temp/jsbq_build.trigger"))
        {
            File.Delete("Temp/jsbq_build.trigger");
            try { Build(); File.WriteAllText("Temp/jsbq_build.done", System.DateTime.Now.ToString("o")); }
            catch (System.Exception e) { Debug.LogError("[JSBQ] " + e); File.WriteAllText("Temp/jsbq_build.done", "ERR " + e); }
        }
        if (File.Exists("Temp/jsbq_play.trigger"))
        {
            string arg;
            try { arg = File.ReadAllText("Temp/jsbq_play.trigger").Trim(); File.Delete("Temp/jsbq_play.trigger"); } catch (IOException) { return; }   // still being written
            Play(arg.Contains("record"));
        }
    }

    [MenuItem("Tools/Jangsanbeom/Quad Anim/Build")]
    public static void Build()
    {
        AssetDatabase.Refresh();
        var fbx = Directory.GetFiles(Q + "/Models", "*.fbx").Select(p => p.Replace('\\', '/')).OrderBy(p => p).ToArray();
        foreach (var p in fbx)
        {
            var mi = (ModelImporter)AssetImporter.GetAtPath(p);
            var name = Path.GetFileNameWithoutExtension(p);
            mi.animationType = ModelImporterAnimationType.Generic; mi.globalScale = BossScale; mi.useFileScale = true;
            mi.importBlendShapes = false; mi.resampleCurves = true; mi.animationCompression = ModelImporterAnimationCompression.Off;   // resample: FBX stores Euler keys, +-180 wraps spun the body
            var clips = mi.defaultClipAnimations;
            foreach (var c in clips) { c.name = name; c.loopTime = Loops.Any(l => name.Contains(l)); }
            mi.clipAnimations = clips; mi.SaveAndReimport();
        }
        // animator: one state per clip, named like the clip
        var ctrlPath = Q + "/JSBQ_MoveSet.controller";
        AssetDatabase.DeleteAsset(ctrlPath);
        var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ctrlPath);
        var sm = ctrl.layers[0].stateMachine;
        foreach (var p in fbx)
        {
            var clip = AssetDatabase.LoadAllAssetsAtPath(p).OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview"));
            var st = sm.AddState(clip.name); st.motion = clip;
            if (clip.name.Contains("idle")) sm.defaultState = st;
        }
        // boss prefab: mesh from the first clip FBX, materials as in the glow test
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx.FirstOrDefault(p => p.Contains("idle")) ?? fbx[0]);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
        try
        {
            go.name = "JSBQ_Boss";
            var body = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/JSB_BodyGlow.mat");
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (r.name.StartsWith("tripo")) r.sharedMaterial = body;
                else if (r.name.StartsWith("SnakeBody") || r.name.StartsWith("SnakeTop"))
                {
                    r.sharedMaterials = r.sharedMaterials.Select(m => Mat("JSB_Parasite_" + ParasiteKey(m ? m.name : ""))).ToArray();
                    var pb = new MaterialPropertyBlock(); r.GetPropertyBlock(pb); pb.SetFloat("_WaveDir", r.name.StartsWith("SnakeTop") ? 1f : -1f); r.SetPropertyBlock(pb);
                }
                else r.sharedMaterials = r.sharedMaterials.Select(GramoMat).ToArray();
                if (r is SkinnedMeshRenderer smr) { smr.updateWhenOffscreen = true; smr.quality = SkinQuality.Bone4; }
            }
            WindKeySetup.AddToJangsanbeom(go);
            var keyMat = Mat("JSB_WindKey_Boss");
            if (keyMat != null) foreach (var r in go.GetComponentsInChildren<Renderer>(true)) if (r.sharedMaterial != null && r.sharedMaterial.shader.name == "BulletHeaven/WindKey") r.sharedMaterial = keyMat;
            var an = go.GetComponent<Animator>(); if (an == null) an = go.AddComponent<Animator>(); an.runtimeAnimatorController = ctrl; an.applyRootMotion = false;
            an.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var ct = go.GetComponent<JangsanbeomClawTrails>(); if (ct == null) ct = go.AddComponent<JangsanbeomClawTrails>();
            ct.trailMaterial = Mat("JSB_ClawTrail"); ct.minSpeed = 2.5f; ct.fullSpeed = 10f;
            if (go.GetComponent<JangsanbeomTelegraph>() == null) go.AddComponent<JangsanbeomTelegraph>();
            PrefabUtility.SaveAsPrefabAsset(go, Q + "/JSBQ_Boss.prefab");
        }
        finally { Object.DestroyImmediate(go); }
        BuildScene();
        AssetDatabase.SaveAssets();
        Debug.Log($"[JSBQ] built {fbx.Length} clips: " + string.Join(", ", fbx.Select(Path.GetFileNameWithoutExtension)));
    }

    static void BuildScene()
    {
        var prev = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        try
        {
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
            var boss = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Q + "/JSBQ_Boss.prefab"), scene);
            boss.transform.SetPositionAndRotation(new Vector3(0f, 0f, 10f), Quaternion.Euler(0, 180, 0));   // model faces -Y in Blender -> +Z in Unity; turned to face the camera
            var loader = new GameObject("JSBQ_Showcase");
            var sc = loader.AddComponent<JSBQuadShowcase>(); sc.animator = boss.GetComponent<Animator>(); sc.follow = boss.transform;
            loader.AddComponent<JSBQMapLoader>().mapScene = MapScene;
            EditorSceneManager.SaveScene(scene, ScenePath);
        }
        finally
        {
            if (prev.IsValid() && prev.isLoaded) UnityEngine.SceneManagement.SceneManager.SetActiveScene(prev);
            EditorSceneManager.CloseScene(scene, true);
        }
    }

    public static void Play(bool record)
    {
        for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty) { Debug.LogError("[JSBQ] an open scene has unsaved changes; not switching scenes"); File.WriteAllText("Temp/jsbq_play.err", "dirty scene"); return; }
        EditorPrefs.SetString("JSBQ_ReturnScene", UnityEngine.SceneManagement.SceneManager.GetActiveScene().path);
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var sc = Object.FindFirstObjectByType<JSBQuadShowcase>(); sc.record = record; sc.trailMoves = "swipe,slam,bite";
        sc.animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(Q + "/JSBQ_MoveSet.controller");   // Build recreates the controller; a stale scene keeps the dead reference
        sc.follow.SetPositionAndRotation(new Vector3(0f, 0f, 10f), Quaternion.Euler(0, 180, 0));   // the scene may be stale if it was open during Build
        var mv = Temp("jsbq_moves.txt");   // optional move list: lines "state seconds [speed] [hit,hit] [turn]"
        if (mv != null)
        {
            sc.moves.Clear();
            foreach (var line in mv.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("#")))
            {
                var p = line.Split(' '); var ci = System.Globalization.CultureInfo.InvariantCulture;
                var m = new JSBQuadShowcase.Move { state = p[0], seconds = float.Parse(p[1], ci) };
                if (p.Length > 2) m.speed = float.Parse(p[2], ci);
                if (p.Length > 3 && p[3] != "-") m.hits = p[3].Split(',').Select(s => float.Parse(s, ci)).ToArray();
                if (p.Length > 4) m.turn = float.Parse(p[4], ci);
                sc.moves.Add(m);
            }
        }
        EditorSceneManager.SaveScene(sc.gameObject.scene);
        // RoomRunPlayStart boots the room run on Play (and re-applies after a reload): switch its pref off for this run, restored on exit
        if (!EditorPrefs.HasKey("JSBQ_RoomRunPref")) EditorPrefs.SetBool("JSBQ_RoomRunPref", EditorPrefs.GetBool("RoomRun.AlwaysPlayFromRoomRun", true));
        EditorPrefs.SetBool("RoomRun.AlwaysPlayFromRoomRun", false);
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
        EditorPrefs.SetBool("JSBQ_Playing", true);
        EditorApplication.isPlaying = true;
    }
    static string Temp(string f) => File.Exists("Temp/" + f) ? File.ReadAllText("Temp/" + f) : null;

    static Material Mat(string n) => AssetDatabase.LoadAssetAtPath<Material>($"{Root}/Materials/{n}.mat");
    static string ParasiteKey(string slot) => slot.Contains("Lip") ? "Lip" : slot.Contains("HoleInside") ? "Hole" : slot.Contains("Blood") ? "Blood" : slot.Contains("Vein") ? "Vein" : "Flesh";
    static Material GramoMat(Material src)
    {
        string n = src != null ? src.name : "";
        if (n.Contains("Banner") || n.Contains("Cloth") || n.Contains("Talisman")) return src;
        string key = n.Contains("Rope") ? "Rope" : (n.Contains("Bronze") || n.Contains("Gold") || n.Contains("Brass")) ? "Bronze" : n.Contains("Inside") ? "Inside" : "Rust";
        var gm = Mat("JSB_Gramo_" + key); return gm != null ? gm : src;
    }
}
#endif
