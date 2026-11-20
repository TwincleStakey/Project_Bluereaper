// 장산범 v2 (11-18 quadruped Tripo model, Assets/Game/Jangsanbeom/V2): edit-mode capture in the BossSeal arena with the game camera.
// Trigger: Temp/jsb2.trigger with "<clip: bite|walk> [zoom 1.5] [dim 0.6] [yawDeg 25]". Frames go to
// ArtSource/Jangsanbeom/renders/v2/vid_<clip>; done stamp Temp/jsb2.done (holds the log). No Play mode, nothing saved in the map.
// bite: the greatsword hero stands in front, gets taken in the jaws at the snap frame, rides the head through the shake and is
// tossed (timing and the stand/landing points come from Models/jsb2_bite.track.txt, written by ArtSource/Jangsanbeom/scripts/v2_bite.py).
#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class JangsanbeomV2Capture
{
    const string Root = "Assets/Game/Jangsanbeom/V2";
    static Texture2D TeethTex;
    const string MapScene = "Assets/Game/BossSeal/BossSeal.unity";
    const string HeroPrefab = "Assets/Game/GSHero/Generated/GS_HeroPreview.prefab";
    const string HeroAnims = "Assets/Game/GSHero/Art/GS_BerserkerAnims.fbx";
    const float BossScale = 25f;

    [InitializeOnLoadMethod]
    static void Watch() { EditorApplication.update += Tick; }
    static void Tick()
    {
        const string trig = "Temp/jsb2.trigger";
        if (!File.Exists(trig) || EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        var a = File.ReadAllText(trig).Trim().Split(' '); File.Delete(trig);
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        string log;
        try
        {
            log = Capture(a[0], a.Length > 1 ? float.Parse(a[1], ci) : 1.5f, a.Length > 2 ? float.Parse(a[2], ci) : 0.6f, a.Length > 3 ? float.Parse(a[3], ci) : 25f, a.Length > 4 ? float.Parse(a[4], ci) : 0f, a.Length > 5 && a[5] == "follow");
        }
        catch (System.Exception e) { log = "ERROR " + e; }
        File.WriteAllText("Temp/jsb2.done", log);
    }

    static Material BodyMat()
    {
        var path = Root + "/JSB2_Body.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        var src = AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Jangsanbeom/Materials/JSB_BodyGlow.mat");
        if (m == null) { m = new Material(src); AssetDatabase.CreateAsset(m, path); } else m.CopyPropertiesFromMaterial(src);
        var ti = (TextureImporter)AssetImporter.GetAtPath(Root + "/Textures/JSB2_Base.png");
        if (ti != null && ti.maxTextureSize != 2048) { ti.maxTextureSize = 2048; ti.SaveAndReimport(); }
        m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Textures/JSB2_Base.png"));
        var em = Root + "/Textures/JSB2_EyeMask.png";   // written by ArtSource/Jangsanbeom/scripts/v2_eyemask.py
        var eti = (TextureImporter)AssetImporter.GetAtPath(em);
        if (eti != null && eti.sRGBTexture) { eti.sRGBTexture = false; eti.SaveAndReimport(); }
        m.SetTexture("_EyeMask", AssetDatabase.LoadAssetAtPath<Texture2D>(em));
        var tm = Root + "/Textures/JSB2_TeethMask.png";   // ArtSource/Jangsanbeom/scripts/v2_teethmask.py (lit red by the howl)
        var tti = (TextureImporter)AssetImporter.GetAtPath(tm);
        if (tti != null && tti.sRGBTexture) { tti.sRGBTexture = false; tti.SaveAndReimport(); }
        var teethTex = AssetDatabase.LoadAssetAtPath<Texture2D>(tm); if (teethTex == null) Debug.LogWarning("JSB2: teeth mask not loaded " + tm);
        TeethTex = teethTex;
        // this capture is about the motion: the fur stays readable (the near-black look comes back with the glow pass)
        m.SetFloat("_AmbientBoost", 0.6f); m.SetFloat("_RimIntensity", 0.5f); m.SetFloat("_Warn", 0f);
        // 11-18 Kido: the v1 fur glints (points of light in the fur that swell with the heartbeat), same settings as v1
        m.SetFloat("_SparkleDensity", 7f); m.SetFloat("_SparkleAmount", 0.22f); m.SetFloat("_SparkleIntensity", 3f);
        m.SetColor("_SparkleColor", new Color(6f, 0.45f, 0.18f, 1f)); m.SetFloat("_SparkleIntensity", 0f);   // 11-19: dots replaced by glowing strands
        m.SetFloat("_StrandIntensity", 4f); m.SetFloat("_StrandAmount", 0.3f); m.SetFloat("_StrandHairs", 3f); m.SetFloat("_StrandWidth", 0.12f); m.SetFloat("_StrandSpeed", 0.6f); m.SetColor("_StrandColor", new Color(5f, 0.4f, 0.15f, 1f)); m.SetFloat("_SparkleAmount", 0.3f);
        m.SetColor("_RimColor", new Color(1.4f, 0.12f, 0.05f, 1f)); m.SetFloat("_RimIntensity", 1.6f);   // the fur edge glows red too (was cold blue)   // 11-18 Kido: "불게 빛나야지" - red embers in the fur, not white
        // red eyes and claws like v1. The capture draws CPU-skinned copies, so "object space" is world space here: claws = the
        // reddish parts within 2.2 m of the ground (the capture skins into the root's space like Play does).
        m.SetFloat("_EyeIntensity", 9f); m.SetFloat("_ClawIntensity", 6f); m.SetFloat("_ClawRedness", 0.06f); m.SetFloat("_ClawHeight", 2.2f);
        EditorUtility.SetDirty(m); return m;
    }

    static readonly Dictionary<SkinnedMeshRenderer, MeshRenderer> proxies = new Dictionary<SkinnedMeshRenderer, MeshRenderer>();
    // edit mode never re-skins between our renders: bake every skinned mesh and draw the baked copy
    // Skinned on the CPU straight into world space (bone.localToWorld x bindpose). BakeMesh returned vertices in a frame that
    // differs per import (node rotation, x100 scale) and guessing it put the v2 boss underground and then back to front.
    class SkinCache { public Vector3[] v, n; public BoneWeight[] w; public Matrix4x4[] bind; public Vector3[] ov, on; }
    static readonly Dictionary<SkinnedMeshRenderer, SkinCache> skinCache = new Dictionary<SkinnedMeshRenderer, SkinCache>();
    static void SyncSkin(GameObject go)
    {
        foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            var src = smr.sharedMesh; if (src == null) continue;
            if (!proxies.TryGetValue(smr, out var mr) || mr == null)
            {
                var p = new GameObject("SkinProxy_" + smr.name) { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(p, go.scene);
                var copy = Object.Instantiate(src); copy.name = smr.name + "_skinned"; copy.hideFlags = HideFlags.HideAndDontSave; copy.MarkDynamic();
                p.AddComponent<MeshFilter>().sharedMesh = copy;
                mr = p.AddComponent<MeshRenderer>(); mr.shadowCastingMode = smr.shadowCastingMode; mr.receiveShadows = smr.receiveShadows;
                proxies[smr] = mr;
                skinCache[smr] = new SkinCache { v = src.vertices, n = src.normals, w = src.boneWeights, bind = src.bindposes, ov = new Vector3[src.vertexCount], on = new Vector3[src.vertexCount] };
            }
            var c = skinCache[smr]; var bones = smr.bones; var mats = new Matrix4x4[bones.Length];
            // skinned into the space of the model's root (as the GPU skinning does in Play), so the glow shader's object-space
            // effects (fur glints, claw height) behave the same in the capture as in the game
            var toRoot = go.transform.worldToLocalMatrix;
            for (int j = 0; j < bones.Length; j++) mats[j] = bones[j] != null ? toRoot * bones[j].localToWorldMatrix * c.bind[j] : Matrix4x4.identity;
            bool hasN = c.n != null && c.n.Length == c.v.Length;
            for (int i = 0; i < c.v.Length; i++)
            {
                var bw = c.w[i]; var vp = c.v[i];
                var pos = mats[bw.boneIndex0].MultiplyPoint3x4(vp) * bw.weight0;
                if (bw.weight1 > 0) pos += mats[bw.boneIndex1].MultiplyPoint3x4(vp) * bw.weight1;
                if (bw.weight2 > 0) pos += mats[bw.boneIndex2].MultiplyPoint3x4(vp) * bw.weight2;
                if (bw.weight3 > 0) pos += mats[bw.boneIndex3].MultiplyPoint3x4(vp) * bw.weight3;
                c.ov[i] = pos;
                if (hasN)
                {
                    var vn = c.n[i];
                    var nn = mats[bw.boneIndex0].MultiplyVector(vn) * bw.weight0;
                    if (bw.weight1 > 0) nn += mats[bw.boneIndex1].MultiplyVector(vn) * bw.weight1;
                    if (bw.weight2 > 0) nn += mats[bw.boneIndex2].MultiplyVector(vn) * bw.weight2;
                    if (bw.weight3 > 0) nn += mats[bw.boneIndex3].MultiplyVector(vn) * bw.weight3;
                    c.on[i] = nn.normalized;
                }
            }
            var bm = mr.GetComponent<MeshFilter>().sharedMesh; bm.vertices = c.ov; if (hasN) bm.normals = c.on; bm.RecalculateBounds();
            mr.transform.SetPositionAndRotation(go.transform.position, go.transform.rotation); mr.transform.localScale = go.transform.lossyScale;
            mr.sharedMaterials = smr.sharedMaterials;
            var pb = new MaterialPropertyBlock(); smr.GetPropertyBlock(pb); mr.SetPropertyBlock(pb);
            smr.forceRenderingOff = true;
        }
    }
    static void ClearSkin()
    {
        skinCache.Clear();
        foreach (var kv in proxies)
        {
            if (kv.Key != null) kv.Key.forceRenderingOff = false;
            if (kv.Value != null) { Object.DestroyImmediate(kv.Value.GetComponent<MeshFilter>().sharedMesh); Object.DestroyImmediate(kv.Value.gameObject); }
        }
        proxies.Clear();
    }

    // an alpha-blended, vertex-coloured particle material (URP Particles/Unlit) with an optional sprite
    static Material FxSprite(string name, Texture2D tex)
    {
        string path = Root + "/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")); AssetDatabase.CreateAsset(m, path); }
        m.SetTexture("_BaseMap", tex); m.SetColor("_BaseColor", Color.white);
        m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f); m.SetFloat("_ZWrite", 0f); m.SetFloat("_Cull", 0f);
        m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.SetOverrideTag("RenderType", "Transparent"); m.renderQueue = 3000; EditorUtility.SetDirty(m);
        return m;
    }

    static void Shoot(Camera cam, RenderTexture rt, RenderTexture ldr, Texture2D tex, string file)
    {
        var req = new RenderPipeline.StandardRequest { destination = rt };
        if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
        else { cam.targetTexture = rt; cam.Render(); cam.targetTexture = null; }
        Graphics.Blit(rt, ldr);
        var prev = RenderTexture.active; RenderTexture.active = ldr;
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply(); RenderTexture.active = prev;
        File.WriteAllBytes(file, tex.EncodeToPNG());
    }

    static string Capture(string clipName, float zoom, float dim, float camYaw, float lapRadius, bool follow = false)
    {
        bool lineArg = lapRadius < 0; if (lineArg) lapRadius = 16f;   // negative radius = a straight walk across the arena
        var sb = new System.Text.StringBuilder();
        AssetDatabase.Refresh();
        // 11-19 Kido ("흔들지 말고 입에 문 다음 걷는 포즈로 어슬렁어슬렁 동그랗게 돌면서"): "carry" = the bite up to the jaws closing,
        // then the heavy walk on a circle with the player held in the mouth
        bool carry = clipName == "carry"; bool fogdash = clipName == "fogdash" || clipName == "fogdashb"; string clipFile = carry ? "walk_lion" : fogdash ? "idle" : clipName;
        string fbx = $"{Root}/Models/jsb2_{clipFile}.fbx";
        if (!File.Exists(fbx) && File.Exists($"{Root}/Models/jsb2_{clipFile}.anim")) fbx = $"{Root}/Models/jsb2_{clipFile}.anim";   // clips baked by JangsanbeomHeavyWalk
        string modelFbx = $"{Root}/Models/jsb2_model.fbx";   // body + gramophone + snake parasite; the per-clip files only bring the motion
        foreach (var f in Directory.GetFiles(Root + "/Models", "*.fbx"))
        {
            var fp = f.Replace('\\', '/');
            var mi = (ModelImporter)AssetImporter.GetAtPath(fp);
            if (mi == null) continue;
            var wantMat = fp == modelFbx ? ModelImporterMaterialImportMode.ImportStandard : ModelImporterMaterialImportMode.None;   // the model keeps its slot names
            if (mi.animationType != ModelImporterAnimationType.Generic || Mathf.Abs(mi.globalScale - BossScale) > 0.01f || mi.materialImportMode != wantMat)
            { mi.animationType = ModelImporterAnimationType.Generic; mi.globalScale = BossScale; mi.useFileScale = true; mi.materialImportMode = wantMat; mi.SaveAndReimport(); }
        }
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(File.Exists(modelFbx) ? modelFbx : fbx);
        var clip = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview"));
        var biteClip = carry ? AssetDatabase.LoadAllAssetsAtPath($"{Root}/Models/jsb2_bite.fbx").OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview")) : null;
        var mat = BodyMat();
        AssetDatabase.SaveAssets();

        bool openedMap = !SceneManager.GetSceneByPath(MapScene).isLoaded;
        var map = openedMap ? EditorSceneManager.OpenScene(MapScene, OpenSceneMode.Additive) : SceneManager.GetSceneByPath(MapScene);
        var prevActive = SceneManager.GetActiveScene();
        SceneManager.SetActiveScene(map);
        var hidden = new List<GameObject>(); var cleanBefore = new List<Scene>(); var made = new List<GameObject>();
        bool mapWasDirty = map.isDirty;
        bool async0 = ShaderUtil.allowAsyncCompilation; ShaderUtil.allowAsyncCompilation = false;
        Light[] mapLights = null; float[] lightI0 = null;
        var amb0 = (RenderSettings.ambientSkyColor, RenderSettings.ambientEquatorColor, RenderSettings.ambientGroundColor, RenderSettings.ambientLight, RenderSettings.fogColor);
        Camera cam = null; Vector3 camP0 = Vector3.zero; Quaternion camR0 = Quaternion.identity; float far0 = 0;
        try
        {
            for (int si = 0; si < SceneManager.sceneCount; si++)
            {
                var sc = SceneManager.GetSceneAt(si);
                if (sc == map || !sc.isLoaded) continue;
                if (!sc.isDirty) cleanBefore.Add(sc);
                foreach (var g in sc.GetRootGameObjects()) if (g.activeSelf) { g.SetActive(false); hidden.Add(g); }
            }
            var boss = (GameObject)Object.Instantiate(model); boss.name = "JSB2_" + clipName; boss.hideFlags = HideFlags.DontSave; made.Add(boss);
            SceneManager.MoveGameObjectToScene(boss, map);
            int nBody = 0, nSnake = 0, nGramo = 0;
            foreach (var r in boss.GetComponentsInChildren<Renderer>())
            {
                if (r.name.StartsWith("tripo")) { r.sharedMaterials = r.sharedMaterials.Select(_ => mat).ToArray(); nBody++; }
                else if (r.name.StartsWith("SnakeBody") || r.name.StartsWith("SnakeTop"))
                {   // gulping flesh, heartbeat veins: the v1 parasite materials, chosen by the Blender slot name
                    r.sharedMaterials = r.sharedMaterials.Select(m => JangsanbeomGlowSetup.ParasiteMat(m != null ? m.name : "")).ToArray();
                    var pbk = new MaterialPropertyBlock(); r.GetPropertyBlock(pbk); pbk.SetFloat("_WaveDir", r.name.StartsWith("SnakeTop") ? 1f : -1f); r.SetPropertyBlock(pbk);
                    nSnake++;
                }
                else { r.sharedMaterials = r.sharedMaterials.Select(JangsanbeomGlowSetup.GramoMat).ToArray(); nGramo++; }
            }
            try { WindKeySetup.AddToJangsanbeom(boss); JangsanbeomGlowSetup.BossKeyMat(boss); } catch (System.Exception e) { sb.AppendLine("windkey: " + e.Message); }   // the boss key material (slow turn, glow), as on v1
            sb.AppendLine($"renderers body {nBody} snake {nSnake} gramophone {nGramo}");
            // howl FX: glowing mouth, face light, rings of light rolling out (JangsanbeomHowlFX); every clip gets it, it only fires
            // while the jaw is open
            var howl = boss.AddComponent<JangsanbeomHowlFX>();
            var ringMat = AssetDatabase.LoadAssetAtPath<Material>(Root + "/JSB2_HowlRing.mat");
            if (ringMat == null) { ringMat = new Material(Shader.Find("BulletHeaven/JangsanbeomTrail")); AssetDatabase.CreateAsset(ringMat, Root + "/JSB2_HowlRing.mat"); }
            ringMat.SetColor("_Color", new Color(5f, 0.9f, 0.3f, 1f)); ringMat.SetFloat("_Softness", 0.8f);
            var mouthMat = AssetDatabase.LoadAssetAtPath<Material>(Root + "/JSB2_HowlMouth.mat");
            if (mouthMat == null) { mouthMat = new Material(Shader.Find("Universal Render Pipeline/Unlit")); AssetDatabase.CreateAsset(mouthMat, Root + "/JSB2_HowlMouth.mat"); }
            mouthMat.SetColor("_BaseColor", new Color(9f, 1.6f, 0.5f, 1f));
            howl.glowMaterial = ringMat; howl.mouthMaterial = mouthMat; howl.teethMask = TeethTex; howl.Build();
            // footfall dust (Kenney CC0 smoke flipbook from BossSeal/Source/Kenney_Smoke)
            var dust = boss.AddComponent<JangsanbeomFootDust>();
            var dustMat = AssetDatabase.LoadAssetAtPath<Material>(Root + "/JSB2_FootDust.mat");
            if (dustMat == null) { dustMat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")); AssetDatabase.CreateAsset(dustMat, Root + "/JSB2_FootDust.mat"); }
            dustMat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Game/BossSeal/Source/Kenney_Smoke/BS_SmokeWhite_4x4.png"));
            dustMat.SetColor("_BaseColor", Color.white); dustMat.SetFloat("_Surface", 1f); dustMat.SetFloat("_Blend", 0f); dustMat.SetFloat("_ZWrite", 0f); dustMat.SetFloat("_Cull", 0f);
            dustMat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha); dustMat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            dustMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); dustMat.SetOverrideTag("RenderType", "Transparent"); dustMat.renderQueue = 3000; EditorUtility.SetDirty(dustMat);
            dust.dustMaterial = dustMat; dust.Build();
            JangsanbeomFogDash pattern = null; JangsanbeomClawTrails dashTrails = null;
            // 11-19 Kido: the v1 scrape FX (dust, clods, sparks, gouges under the dragging claw) and red claw trails on v2
            JangsanbeomScrapeFX scrapeFx = null; JangsanbeomClawTrails clawTrails = null;
            if (clipName.Contains("scrape"))
            {
                scrapeFx = boss.AddComponent<JangsanbeomScrapeFX>();
                scrapeFx.dustMaterial = JangsanbeomGlowSetup.FxMat("JSB_ScrapeDust", false);
                scrapeFx.sparkMaterial = JangsanbeomGlowSetup.GlowLineMat("JSB_ScrapeSpark", new Color(6f, 0.7f, 0.18f, 1f));
                scrapeFx.gougeMaterial = JangsanbeomGlowSetup.GlowLineMat("JSB_ScrapeGouge", new Color(3f, 0.2f, 0.06f, 1f));
                scrapeFx.Build();
                var trailMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Jangsanbeom/Materials/JSB_ClawTrail.mat");
                if (trailMat != null) { clawTrails = boss.AddComponent<JangsanbeomClawTrails>(); clawTrails.trailMaterial = trailMat; clawTrails.minSpeed = 2.5f; clawTrails.fullSpeed = 10f; clawTrails.Build(); }
            }
            var all = boss.GetComponentsInChildren<Transform>();
            System.Func<string, Transform> B = n => all.FirstOrDefault(t => t.name == n);
            var head = B("Head"); var hips = B("Hips");
            clip.SampleAnimation(boss, 0f);
            if (carry) biteClip.SampleAnimation(boss, 0f);
            var fwd = head.position - hips.position; fwd.y = 0;
            boss.transform.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(fwd.normalized, Vector3.back, Vector3.up), Vector3.up) * boss.transform.rotation;   // face the camera side; yaw only (FromToRotation of opposite vectors flipped the boss upside down)
            boss.transform.position = new Vector3(10f, 0f, 10f);   // dirt beside the dais

            // which way the Blender axes landed in Unity: test the sign choices against the rest bones from the track file
            var trackPath = $"{Root}/Models/jsb2_{(carry ? "bite" : clipName)}.track.txt";
            var refs = new Dictionary<string, Vector3>(); var track = new List<Vector3>(); int snap = -1, toss = -1, lift = -1; float playerH = 1.8f / BossScale;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            if (File.Exists(trackPath))
                foreach (var line in File.ReadAllLines(trackPath))
                {
                    var p = line.Trim().Split(' ');
                    if (p[0] == "frames") { for (int k = 0; k + 1 < p.Length; k += 2) { if (p[k] == "snap") snap = int.Parse(p[k + 1]); if (p[k] == "toss") toss = int.Parse(p[k + 1]); if (p[k] == "lift") lift = int.Parse(p[k + 1]); } }
                    else if (p[0] == "player_height") playerH = float.Parse(p[1], ci);
                    else if (p[0] == "ref") refs[p[1]] = new Vector3(float.Parse(p[2], ci), float.Parse(p[3], ci), float.Parse(p[4], ci));
                    else if (p[0] == "t") track.Add(new Vector3(float.Parse(p[1], ci), float.Parse(p[2], ci), float.Parse(p[3], ci)));
                }
            float sx = 1, sy = 1, bestErr = float.MaxValue;
            if (refs.Count > 0)
                foreach (var cx in new[] { 1f, -1f }) foreach (var cy in new[] { 1f, -1f })
                {
                    float err = 0;
                    foreach (var kv in refs) { var t = B(kv.Key); if (t == null) continue; err += (boss.transform.InverseTransformPoint(t.position) - new Vector3(cx * kv.Value.x, kv.Value.z, cy * kv.Value.y) * BossScale).magnitude; }
                    if (err < bestErr) { bestErr = err; sx = cx; sy = cy; }
                }
            System.Func<Vector3, Vector3> toWorld = b => boss.transform.TransformPoint(new Vector3(sx * b.x, b.z, sy * b.y) * BossScale);
            sb.AppendLine($"clip {clip.name} {clip.length:F2}s; axis map x*{sx} y*{sy} err {bestErr:F2} m; track {track.Count} frames snap {snap} toss {toss}");

            // the player: the greatsword hero in its idle pose
            GameObject hero = null; Vector3 standPos = Vector3.zero; Matrix4x4 held = Matrix4x4.identity; Vector3 tossFrom = Vector3.zero, tossVel = Vector3.zero; Quaternion tossRot = Quaternion.identity;
            if (track.Count > 0 || fogdash)
            {
                var hp = AssetDatabase.LoadAssetAtPath<GameObject>(HeroPrefab);
                if (hp != null)
                {
                    hero = (GameObject)Object.Instantiate(hp); hero.name = "JSB2_Player"; hero.hideFlags = HideFlags.DontSave; made.Add(hero);
                    SceneManager.MoveGameObjectToScene(hero, map);
                    foreach (var ps in hero.GetComponentsInChildren<ParticleSystem>(true)) ps.gameObject.SetActive(false);
                    var idle = AssetDatabase.LoadAllAssetsAtPath(HeroAnims).OfType<AnimationClip>().FirstOrDefault(c => c.name.Contains("Idle"));
                    if (idle != null) idle.SampleAnimation(hero, 0f);
                    standPos = fogdash ? new Vector3(3f, 0f, -5f) : toWorld(track[0]); standPos.y = 0f;
                    hero.transform.SetPositionAndRotation(standPos, Quaternion.LookRotation(Vector3.forward));   // looking up at the boss
                    sb.AppendLine($"hero {hp.name} at {standPos}, idle {(idle ? idle.name : "none")}");
                }
            }

            if (fogdash)
            {   // 11-19 Kido: fog dash pattern (JangsanbeomFogDash); poses from JangsanbeomPounceBake
                pattern = boss.AddComponent<JangsanbeomFogDash>();
                pattern.standClip = clip;
                pattern.crouchClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(Root + "/Models/jsb2_crouch.anim");
                pattern.dashClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(Root + "/Models/jsb2_dash.anim");
                pattern.gallopClip = AssetDatabase.LoadAllAssetsAtPath(Root + "/Models/jsb2_gallop.fbx").OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview"));
                pattern.dustMaterial = dustMat;
                var dtMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Jangsanbeom/Materials/JSB_ClawTrail.mat");
                if (dtMat != null)
                {
                    dashTrails = boss.AddComponent<JangsanbeomClawTrails>();
                    dashTrails.toeBones = new[] { "FrontToes.L", "FrontToes.R", "HindToes.L", "HindToes.R" };
                    dashTrails.trailMaterial = dtMat; dashTrails.width = 2.6f; dashTrails.lifetime = 0.4f; dashTrails.minSpeed = 12f; dashTrails.fullSpeed = 40f; dashTrails.Build();
                }
                if (clipName == "fogdashb") { pattern.leapStyle = 1; pattern.dashTime = 1.0f; }
                // fog: the soft fog sprite of the downloaded RainSnowCloudEffect pack, alpha blended, vertex coloured
                pattern.fogMaterial = FxSprite("JSB2_FogSmoke", AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Textures/Fog/SmokeLoop02.tif"));   // Unity Particle Pack
                pattern.telegraphFillMaterial = FxSprite("JSB2_TelegraphFill", null);
                var edgeMat = AssetDatabase.LoadAssetAtPath<Material>(Root + "/JSB2_TelegraphEdge.mat");
                if (edgeMat == null) { edgeMat = new Material(Shader.Find("BulletHeaven/JangsanbeomTrail")); AssetDatabase.CreateAsset(edgeMat, Root + "/JSB2_TelegraphEdge.mat"); }
                edgeMat.SetColor("_Color", Color.white); edgeMat.SetFloat("_Softness", 0.12f); EditorUtility.SetDirty(edgeMat);
                pattern.telegraphMaterial = edgeMat;
                pattern.player = hero != null ? hero.transform : null; pattern.arenaRadius = 62f; pattern.fogInner = 28f;   // 11-20 Kido: room for three players to dodge a 26 m boss (12 was too tight); was: as small as in the reference
                pattern.dashTime = 1.45f; pattern.boundHeight = 7.5f;   // a wide clear floor: the boss is 26 m long
                pattern.Build();
                sb.AppendLine($"fogdash: crouch {(pattern.crouchClip ? "ok" : "MISSING")} dash {(pattern.dashClip ? "ok" : "MISSING")} duration {pattern.Duration:F1}s");
            }
            cam = map.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Camera>(true)).First(c => c.CompareTag("MainCamera"));
            camP0 = cam.transform.position; camR0 = cam.transform.rotation; far0 = cam.farClipPlane;
            // 11-18 Kido: "이동도 넣어". Locomotion clips are in place; the root travels by exactly what the planted paws push back
            // (distance table over one cycle), on a circle of lapRadius around the arena centre, turned along the path.
            float[] lapCum = null; float lapCycle = 0; Quaternion lapBase = Quaternion.identity;
            if (lapRadius > 0)
            {
                boss.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                clip.SampleAnimation(boss, 0f);
                var fL = head.position - hips.position; fL.y = 0; fL.Normalize();
                lapBase = Quaternion.Inverse(Quaternion.LookRotation(fL));
                var toes = new[] { "HindToes.L", "HindToes.R", "FrontToes.L", "FrontToes.R" }.Select(n => B(n)).Where(t => t != null).ToArray();
                const int NS = 240; lapCum = new float[NS + 1]; var prevT = new Vector3[toes.Length]; float dts = clip.length / NS;
                var hgt = new float[toes.Length, NS + 1];
                for (int k = 0; k <= NS; k++) { clip.SampleAnimation(boss, k * dts); for (int i = 0; i < toes.Length; i++) hgt[i, k] = toes[i].position.y; }
                var thr = new float[toes.Length];
                for (int i = 0; i < toes.Length; i++) { float lo = float.MaxValue, hi = float.MinValue; for (int k = 0; k <= NS; k++) { lo = Mathf.Min(lo, hgt[i, k]); hi = Mathf.Max(hi, hgt[i, k]); } thr[i] = lo + (hi - lo) * 0.18f; }
                float stepPrev = 0;
                for (int k = 0; k <= NS; k++)
                {
                    clip.SampleAnimation(boss, k * dts);
                    float vs = 0; int vn = 0;
                    for (int i = 0; i < toes.Length; i++)
                    {
                        var pp = toes[i].position;
                        if (k > 0 && hgt[i, k] <= thr[i] && hgt[i, k - 1] <= thr[i]) { vs += -Vector3.Dot(pp - prevT[i], fL); vn++; }
                        prevT[i] = pp;
                    }
                    float step = vn > 0 ? Mathf.Max(0f, vs / vn) : stepPrev; stepPrev = step;
                    if (k > 0) lapCum[k] = lapCum[k - 1] + step;
                }
                lapCycle = lapCum[NS];
                var travelFile = fbx + ".travel.txt";
                if (File.Exists(travelFile))
                {
                    var tl = File.ReadAllLines(travelFile).Where(x => x.Trim().Length > 0).Select(x => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                    float tLen = tl[0]; var tab = tl.Skip(1).ToArray(); int tn = tab.Length - 1;
                    for (int k = 0; k <= NS; k++) { float fr = Mathf.Min(k / (float)NS * tLen * 30f, tn); int i0 = Mathf.Min((int)fr, tn - 1); lapCum[k] = Mathf.Lerp(tab[i0], tab[i0 + 1], fr - i0); }
                    lapCycle = lapCum[NS];
                    sb.AppendLine($"travel from {travelFile}");
                }
                sb.AppendLine($"lap radius {lapRadius}: {lapCycle:F2} m per {clip.length:F2} s cycle = {lapCycle / clip.length:F1} m/s");
            }
            System.Action<float> place = tt =>
            {
                if (lapCum == null || lapCycle < 0.01f) return;
                float cyc = Mathf.Floor(tt / clip.length), u = (tt - cyc * clip.length) / clip.length * 240f; int i0 = Mathf.Clamp((int)u, 0, 239);
                float dist = cyc * lapCycle + Mathf.Lerp(lapCum[i0], lapCum[i0 + 1], u - i0);
                float th = -Mathf.PI * 0.5f + dist / lapRadius;   // start at the south, anticlockwise
                var tan = new Vector3(-Mathf.Sin(th), 0, Mathf.Cos(th));
                boss.transform.SetPositionAndRotation(new Vector3(Mathf.Cos(th), 0, Mathf.Sin(th)) * lapRadius, Quaternion.LookRotation(tan) * lapBase);
            };
            var target = lapRadius > 0 ? new Vector3(0f, 0.3f, 0f) : hero != null ? Vector3.Lerp(standPos, boss.transform.position, 0.35f) + Vector3.up * 2.5f : boss.transform.position + Vector3.up * 2f;
            var off = Quaternion.Euler(0, camYaw, 0) * (new Vector3(0, 17f, -14.5f) * zoom);   // the RoomRun game camera offset, turned a little so the head shake reads
            cam.transform.position = target + off; cam.transform.rotation = Quaternion.LookRotation(-off); cam.farClipPlane = Mathf.Max(far0, off.magnitude + 150f);
            foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None)) if (ps.gameObject.scene == map) ps.Simulate(12f, true, true);
            mapLights = map.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Light>(true)).ToArray(); lightI0 = mapLights.Select(l => l.intensity).ToArray();
            if (dim < 0.999f)
            {
                foreach (var l in mapLights) l.intensity *= dim;
                RenderSettings.ambientSkyColor *= dim; RenderSettings.ambientEquatorColor *= dim; RenderSettings.ambientGroundColor *= dim; RenderSettings.ambientLight *= dim; RenderSettings.fogColor *= dim;
            }

            var outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../ArtSource/Jangsanbeom/renders/v2/vid_" + clipName));
            Directory.CreateDirectory(outDir);
            foreach (var f0 in Directory.GetFiles(outDir, "*.png")) File.Delete(f0);
            var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.DefaultHDR);
            var ldr = new RenderTexture(1600, 900, 0, RenderTextureFormat.ARGB32);
            var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            Shader.SetGlobalFloat("_JSB_UseFixedTime", 1f); Shader.SetGlobalFloat("_WK_UseFixedTime", 1f);
            try
            {
                { var w = new RenderPipeline.StandardRequest { destination = rt }; if (RenderPipeline.SupportsRenderRequest(cam, w)) RenderPipeline.SubmitRenderRequest(cam, w); }
                bool loop = track.Count == 0; Vector3 camSmooth = Vector3.zero;
                Transform[] lockToes = null; Vector3[] lockPrev = null; float lockGround = 0, lockDistPrev = 0; bool lineWalk = lineArg;   // straight walk: no turning, so nothing sweeps the planted paws
                bool footLock = lapRadius > 0;
                if (footLock) place(0f);
                Transform[] probeToes = null; Vector3[] probePrev = null; Vector3 probeRoot = Vector3.zero; var probe = new System.Text.StringBuilder();
                int nf = loop ? Mathf.CeilToInt(clip.length * 30) * 3 : Mathf.CeilToInt(clip.length * 30) + 1;
                if (lapCum != null && lapCycle > 0.01f) nf = Mathf.Min(420, Mathf.CeilToInt((lineArg ? 48f : 2f * Mathf.PI * lapRadius) / (lapCycle / clip.length) * 30f));
                if (hero != null && toss > 0 && !carry) nf += 45;   // see the thrown player land and lie
                if (fogdash) nf = Mathf.CeilToInt(pattern.Duration * 30f);
                int nA = carry ? Mathf.Max(0, snap) + 6 : 0;   // carry: the bite runs until just after the jaws close
                nf += nA;
                var biteLP = new Vector3[all.Length]; var biteLR = new Quaternion[all.Length]; var bossLP = new Vector3[all.Length]; var bossLR = new Quaternion[all.Length];
                if (carry)
                {   // stand where the circle starts, and put the player in front of the jaws there
                    place(0f); biteClip.SampleAnimation(boss, 0f);
                    if (hero != null) { standPos = toWorld(track[0]); standPos.y = 0f; hero.transform.SetPositionAndRotation(standPos, Quaternion.LookRotation(boss.transform.position - standPos - Vector3.up * (boss.transform.position - standPos).y)); }
                }
                for (int f = 0; f < nf; f++)
                {
                    float t = f / 30f; int lf = f - nA; float lt = lf / 30f;
                    if (fogdash)
                    {
                        pattern.Sample(t);
                        // red streaks from all four paws while it flies (cleared whenever it is hidden in the fog)
                        if (dashTrails != null) { if (f == 0 || !pattern.Visible) dashTrails.ResetTrails(); else dashTrails.Sample(t); }
                    }
                    else if (carry && f < nA)
                    {   // the bite: the boss stands at the start of the circle
                        place(0f); biteClip.SampleAnimation(boss, t);
                        for (int q = 0; q < all.Length; q++) { biteLP[q] = all[q].localPosition; biteLR[q] = all[q].localRotation; }
                    }
                    else if (lapCum != null && lapCycle > 0.01f && footLock)
                    {
                        // 11-18 Kido: "미끄러지는 프레임이 너무 많아". Turning on a circle swept the planted paws sideways (a paw 10 m from
                        // the root moves 0.18 m per frame on a 16 m circle). Foot lock: move the root by the path, sample the pose, then
                        // shift and turn the root so the paws that were already on the ground land exactly where they were.
                        if (lf == 0)
                        {
                            lockToes = new[] { "FrontToes.L", "FrontToes.R", "HindToes.L", "HindToes.R" }.Select(n => B(n)).ToArray();
                            place(0f);
                            if (lineWalk) boss.transform.SetPositionAndRotation(new Vector3(-4f, 0f, -24f), Quaternion.LookRotation(new Vector3(0.15f, 0, 1f).normalized) * lapBase);
                            clip.SampleAnimation(boss, 0f);
                            lockGround = lockToes.Min(x => x.position.y);
                            lockPrev = lockToes.Select(x => x.position).ToArray();
                            lockDistPrev = 0f;
                        }
                        else
                        {
                            float cyc = Mathf.Floor(lt / clip.length), u = (lt - cyc * clip.length) / clip.length * 240f; int i0 = Mathf.Clamp((int)u, 0, 239);
                            float dist = cyc * lapCycle + Mathf.Lerp(lapCum[i0], lapCum[i0 + 1], u - i0);
                            float step = dist - lockDistPrev; lockDistPrev = dist;
                            // proposed move: along the heading, turning toward the circle (heading error to the tangent, plus the curve)
                            var pos = boss.transform.position; var fwdNow = boss.transform.rotation * Quaternion.Inverse(lapBase) * Vector3.forward; fwdNow.y = 0; fwdNow.Normalize();
                            var radial = new Vector3(pos.x, 0, pos.z); float rNow = Mathf.Max(0.1f, radial.magnitude);
                            var tanWant = new Vector3(-radial.z, 0, radial.x) / rNow;                      // anticlockwise tangent
                            tanWant = (tanWant + radial / rNow * Mathf.Clamp((rNow - lapRadius) * -0.15f, -0.5f, 0.5f)).normalized;   // back onto the circle
                            float turn = Mathf.Clamp(Vector3.SignedAngle(fwdNow, tanWant, Vector3.up), -1.2f, 1.2f);   // degrees per frame, gentle
                            boss.transform.position = pos + fwdNow * step;
                            clip.SampleAnimation(boss, lt % clip.length);
                            // correct with the paws that stay on the ground this frame (2-D rigid fit, yaw + shift)
                            var cur = lockToes.Select(x => x.position).ToArray();
                            var idx = Enumerable.Range(0, 4).Where(i => cur[i].y - lockGround < 0.15f && lockPrev[i].y - lockGround < 0.15f).ToArray();
                            // 11-19: a straight walk now moves exactly by the baked travel table, like the game mover will; the baked clip
                            // already pins the planted paws to that travel, so no per-frame fit (it slowed the body whenever a paw dragged)
                            if (lineWalk) idx = new int[] { -1 };
                            if (idx.Length == 0)
                            {   // all paws in the air: just turn along the path
                                boss.transform.rotation = Quaternion.AngleAxis(turn, Vector3.up) * boss.transform.rotation;
                                clip.SampleAnimation(boss, lt % clip.length);
                            }
                            if (idx.Length >= 1 && !lineWalk)
                            {
                                Vector3 ca = Vector3.zero, cb = Vector3.zero;
                                foreach (var i in idx) { ca += cur[i]; cb += lockPrev[i]; }
                                ca /= idx.Length; cb /= idx.Length; ca.y = cb.y = 0;
                                // shift so the planted paws land where they were, then turn toward the path about those paws (a turn
                                // pivots on the feet that are down, so they barely move; turning about the body centre swept them)
                                var R = Quaternion.AngleAxis(lineWalk ? 0f : turn, Vector3.up);
                                var rp = boss.transform.position + (cb - ca);
                                boss.transform.SetPositionAndRotation(cb + R * (rp - cb), R * boss.transform.rotation);
                                clip.SampleAnimation(boss, lt % clip.length);
                            }
                            lockPrev = lockToes.Select(x => x.position).ToArray();
                        }
                    }
                    else { place(t); clip.SampleAnimation(boss, loop ? t % clip.length : Mathf.Min(t, clip.length)); }
                    if (carry && lf >= 0 && lf < 15)
                    {   // ease out of the bite pose into the walk over half a second (the root keeps the walk's foot lock)
                        float w = Mathf.SmoothStep(0f, 1f, lf / 15f);
                        for (int q = 0; q < all.Length; q++) { if (all[q] == boss.transform) continue; all[q].localPosition = Vector3.Lerp(biteLP[q], all[q].localPosition, w); all[q].localRotation = Quaternion.Slerp(biteLR[q], all[q].localRotation, w); }
                    }
                    if (probeToes == null) { probeToes = new[] { "FrontToes.L", "FrontToes.R", "HindToes.L", "HindToes.R" }.Select(n => B(n)).ToArray(); probe.AppendLine("f,t,root_x,root_z,speed," + string.Join(",", probeToes.Select(x => x.name + "_y," + x.name + "_slide"))); probePrev = probeToes.Select(x => x.position).ToArray(); probeRoot = boss.transform.position; }
                    {
                        var rp = boss.transform.position; float spd = (rp - probeRoot).magnitude * 30f; probeRoot = rp;
                        var row = new System.Text.StringBuilder($"{f},{t:F3},{rp.x:F3},{rp.z:F3},{spd:F2}");
                        for (int ti = 0; ti < probeToes.Length; ti++) { var tp = probeToes[ti].position; var d = tp - probePrev[ti]; d.y = 0; row.Append($",{tp.y:F3},{d.magnitude:F3}"); probePrev[ti] = tp; }
                        probe.AppendLine(row.ToString());
                    }
                    if (clipName.Contains("howl") || clipName.Contains("roar")) howl.Sample(t);   // 11-19: the howl rings fired on every open jaw (the bite too)
                    if (f == 0) dust.ResetFX(); if (!fogdash && scrapeFx == null) dust.Sample(t);
                    if (scrapeFx != null) { if (f == 0) { scrapeFx.ResetFX(); if (clawTrails) clawTrails.ResetTrails(); } if (clawTrails) clawTrails.Sample(t); scrapeFx.Sample(t); }
                    if (hero != null)
                    {
                        if (f == snap) held = head.worldToLocalMatrix * hero.transform.localToWorldMatrix;        // the jaws close: from here the hero rides the head
                        if (f >= snap && (f <= toss || carry))
                        {
                            var m = head.localToWorldMatrix * held;
                            var np = (Vector3)m.GetColumn(3);
                            if (f > snap) tossVel = (np - tossFrom) * 30f;
                            hero.transform.SetPositionAndRotation(np, Quaternion.LookRotation(m.GetColumn(2), m.GetColumn(1)));
                            tossFrom = np; tossRot = hero.transform.rotation;
                        }
                        else if (f > toss && toss >= 0 && !carry)
                        {   // 11-19 Kido ("물고 흔든 다음에 던지는 거"): flung out of the jaws along the head's last swing - a high tumbling arc,
                            // a thud with a small bounce, a skid, and the player left lying on its side
                            var dir = tossVel; dir.y = 0;
                            if (dir.sqrMagnitude < 0.01f) { dir = toWorld(track[track.Count - 1]) - tossFrom; dir.y = 0; }
                            dir.Normalize();
                            const float flight = 0.7f, dist = 12f, peak = 5f, skid = 2.2f;
                            float tt = (f - toss) / 30f;
                            var land = tossFrom + dir * dist; land.y = 0.25f;
                            var spinAx = Vector3.Cross(Vector3.up, dir);
                            var lie = Quaternion.LookRotation(dir) * Quaternion.Euler(0, 0, 90);
                            if (tt < flight)
                            {
                                float u = tt / flight;
                                var p0 = Vector3.Lerp(tossFrom, land, u); p0.y = Mathf.Lerp(tossFrom.y, land.y, u) + peak * 4f * u * (1 - u);
                                hero.transform.SetPositionAndRotation(p0, Quaternion.Slerp(Quaternion.AngleAxis(540f * u, spinAx) * tossRot, lie, u * u));
                            }
                            else
                            {
                                float v = tt - flight;
                                float bounce = v < 0.25f ? 0.8f * Mathf.Sin(Mathf.PI * v / 0.25f) : 0f;
                                float sk = skid * (1f - Mathf.Exp(-v * 5f));
                                hero.transform.SetPositionAndRotation(land + dir * sk + Vector3.up * bounce, lie);
                            }
                        }
                        SyncSkin(hero);
                    }
                    if (fogdash && !pattern.Visible) boss.transform.position += Vector3.down * 1000f;   // hidden in the fog
                    SyncSkin(boss);
                    if (f == 0) foreach (var kv in proxies) sb.AppendLine($"  proxy {kv.Value.name} bounds c {kv.Value.bounds.center} size {kv.Value.bounds.size}");
                    Shader.SetGlobalFloat("_JSB_FixedTime", t); Shader.SetGlobalFloat("_WK_FixedTime", t);   // the key on capture time too (it ran on real time = several times too fast)
                    if (follow && hero != null)
                    {   // the game camera follows the player: the bite seen exactly as it plays
                        // (RoomRun CameraRig: locked on the character, no lag, fov 30, default zoom 1.6)
                        var ft = hero.transform.position;   // the game focuses the player's own position, height included
                        cam.fieldOfView = 30f;
                        if (pattern != null && pattern.Shake > 0f)
                        {   // landing shake, like CameraRig.Shake (deterministic for captures)
                            float sh = pattern.Shake * pattern.Shake * 2.2f;
                            ft += new Vector3(Mathf.Sin(f * 12.9898f) * sh, 0f, Mathf.Sin(f * 78.233f + 1.7f) * sh);
                        }
                        cam.transform.position = ft + off; cam.transform.rotation = Quaternion.LookRotation(-off);
                    }
                    else if (follow && !(lapCum != null && lapCycle > 0.01f))
                    {   // a clip in place (howl, roar...): the game camera (fov 30) held on the head, so the mouth reads
                        cam.fieldOfView = 30f;
                        var ft = head.position;
                        cam.transform.position = ft + off; cam.transform.rotation = Quaternion.LookRotation(-off);
                    }
                    else if (follow)
                    {   // 11-18 Kido: "제자리네, 걸어가야지" - a close camera that rides along, so the walk is seen travelling
                        var ft = boss.transform.position + Vector3.up * 3f;
                        camSmooth = f == 0 ? ft : Vector3.Lerp(camSmooth, ft, 0.15f);
                        cam.transform.position = camSmooth + off; cam.transform.rotation = Quaternion.LookRotation(-off);
                    }
                    Shoot(cam, rt, ldr, tex, Path.Combine(outDir, $"f{f:000}.png"));
                }
                sb.AppendLine($"frames {nf} -> {outDir}");
                File.WriteAllText(Path.Combine(outDir, "..", $"probe_{clipName}.csv"), probe.ToString());
            }
            finally
            {
                Shader.SetGlobalFloat("_JSB_UseFixedTime", 0f); Shader.SetGlobalFloat("_WK_UseFixedTime", 0f); ClearSkin();
                Object.DestroyImmediate(rt); Object.DestroyImmediate(ldr); Object.DestroyImmediate(tex);
            }
        }
        finally
        {
            ShaderUtil.allowAsyncCompilation = async0;
            if (cam != null) { cam.transform.position = camP0; cam.transform.rotation = camR0; cam.farClipPlane = far0; }
            if (mapLights != null) for (int li = 0; li < mapLights.Length; li++) if (mapLights[li]) mapLights[li].intensity = lightI0[li];
            (RenderSettings.ambientSkyColor, RenderSettings.ambientEquatorColor, RenderSettings.ambientGroundColor, RenderSettings.ambientLight, RenderSettings.fogColor) = amb0;
            foreach (var g in made) if (g != null) Object.DestroyImmediate(g);
            foreach (var g in hidden) if (g != null) g.SetActive(true);
            foreach (var sc in cleanBefore) if (sc.isLoaded && sc.isDirty) EditorSceneManager.SaveScene(sc);
            if (prevActive.IsValid() && prevActive.isLoaded && prevActive != map) SceneManager.SetActiveScene(prevActive);
            if (openedMap && map.isLoaded) EditorSceneManager.CloseScene(map, true);   // not saved
        }
        return sb.ToString();
    }
}
#endif
