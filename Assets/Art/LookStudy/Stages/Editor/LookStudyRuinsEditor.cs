// Builds the palace and village scenes plus one test player with all stages.
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class LookStudyRuinsEditor
{
    const string Root = "Assets/Art/LookStudy/Stages";
    const string Kit = Root + "/Models/Kit/";
    const string Models = Root + "/Models/";
    const string Tex = Root + "/Textures/";

    static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }
    static Texture2D T(string file) { var t = AssetDatabase.LoadAssetAtPath<Texture2D>(Tex + file); if (!t) Debug.LogError("[LS] no texture " + file); return t; }

    static Material Mat(string name, string shader)
    {
        var path = $"{Root}/Materials/{name}.mat";
        var s = Shader.Find(shader);
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!m) { m = new Material(s); AssetDatabase.CreateAsset(m, path); }
        m.shader = s; EditorUtility.SetDirty(m);
        return m;
    }

    static Material Surf(string name, string tex, string tint, float remap, float burnOn = 1f)
    {
        var m = Mat("R_" + name, "LookStudy/StageRock");
        m.SetTexture("_MainTex", T(tex + "_diff.jpg")); m.SetTexture("_NormalTex", T(tex + "_nor_gl.jpg")); m.SetTexture("_MossTex", T("mossy_rock_diff.jpg"));
        m.SetColor("_Granite", Hex(tint)); m.SetFloat("_Remap", remap); m.SetFloat("_MossAmt", 0f); m.SetFloat("_Wet", 0f); m.SetFloat("_BurnOn", burnOn); m.SetFloat("_Cull", 0f);
        return m;
    }

    static Mesh M(string path)
    {
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path)) if (o is Mesh me) return me;
        Debug.LogError("[LS] no mesh " + path); return null;
    }

    static LookStudyRuins.Part[] Parts(params (string part, Material mat)[] list)
    {
        var r = new LookStudyRuins.Part[list.Length];
        for (int i = 0; i < list.Length; i++) r[i] = new LookStudyRuins.Part { name = list[i].part, mesh = M(Kit + list[i].part + ".fbx"), material = list[i].mat };
        return r;
    }

    static VolumeProfile Volume(string name, float sat, float con, float exp, Color filter, float vign)
    {
        var path = $"{Root}/{name}.asset";
        AssetDatabase.DeleteAsset(path);
        var vol = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(vol, path);
        var bloom = vol.Add<Bloom>(true); bloom.threshold.Override(0.9f); bloom.intensity.Override(0.8f); bloom.scatter.Override(0.74f);
        var ca = vol.Add<ColorAdjustments>(true); ca.saturation.Override(sat); ca.contrast.Override(con); ca.postExposure.Override(exp); ca.colorFilter.Override(filter);
        var vig = vol.Add<Vignette>(true); vig.intensity.Override(vign); vig.smoothness.Override(0.55f);
        var tone = vol.Add<Tonemapping>(true); tone.mode.Override(TonemappingMode.Neutral);
        foreach (var c in vol.components) AssetDatabase.AddObjectToAsset(c, vol);
        return vol;
    }

    public static string BuildStage(LookStudyRuins.Kind kind)
    {
        var path = $"{Root}/{kind}.unity";
        var thatch = Surf("Thatch", "thatch_roof_angled", "#7a6a4f", 0.35f);
        var clay = Surf("Clay", "clay_plaster", "#8a7458", 0.5f);
        var wood = Surf("Wood", "weathered_planks", "#5b4636", 0.5f);
        var stone = Surf("Stone", "rustic_stone_wall", "#6e6a62", 0.6f);
        var pot = Surf("Pot", "plastered_wall_05", "#3a2a22", 0.92f, 0f);
        var strawM = Surf("Straw", "thatch_roof_angled", "#8c7a4e", 0.5f);
        var tile = Surf("Tile", "roof_tiles_14", "#4a4c4f", 0.9f);
        var pavement = Surf("Paver", "rock_wall_05", "#8d8a82", 0.75f);
        var redWood = Surf("RedPillar", "weathered_planks", "#6e2a20", 0.85f);
        var dan = Surf("Dancheong", "raw_plank_wall", "#3c5a52", 0.9f);
        var plaster = Surf("Plaster", "yellow_plaster_02", "#8e897e", 0.8f);
        var cloth = Surf("Cloth", "plastered_wall_05", "#6b2420", 0.95f, 0f);
        var jm = Mat("R_Jang", "LookStudy/StageRock");
        jm.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(Models + "Jangseung_diff.png")); jm.SetFloat("_Remap", 0f); jm.SetFloat("_MossAmt", 0f); jm.SetFloat("_Wet", 0f); jm.SetFloat("_BurnOn", 1f);
        jm.SetTexture("_MossTex", T("mossy_rock_diff.jpg"));

        var ground = Mat(kind == LookStudyRuins.Kind.Palace ? "R_GroundPalace" : "R_GroundVillage", "LookStudy/StageTerrain");
        if (kind == LookStudyRuins.Kind.Palace)
        {
            ground.SetTexture("_PebbleTex", T("rock_wall_05_diff.jpg")); ground.SetTexture("_PebbleNor", T("rock_wall_05_nor_gl.jpg"));
            ground.SetTexture("_GravelTex", T("brown_mud_leaves_01_diff.jpg")); ground.SetTexture("_GravelNor", T("brown_mud_leaves_01_nor_gl.jpg"));
            ground.SetTexture("_RockTex", T("rock_wall_05_diff.jpg")); ground.SetTexture("_RockNor", T("rock_wall_05_nor_gl.jpg"));
            ground.SetTexture("_ForestTex", T("burned_ground_01_diff.jpg")); ground.SetTexture("_ForestNor", T("burned_ground_01_nor_gl.jpg"));
            ground.SetColor("_RockTint", Hex("#a3a097")); ground.SetColor("_PebbleTint", Hex("#a39e94")); ground.SetColor("_ForestTint", Hex("#a09690"));
            ground.SetFloat("_RockScale", 0.085f);
        }
        else
        {
            ground.SetTexture("_PebbleTex", T("dirt_diff.jpg")); ground.SetTexture("_PebbleNor", T("dirt_nor_gl.jpg"));
            ground.SetTexture("_GravelTex", T("brown_mud_leaves_01_diff.jpg")); ground.SetTexture("_GravelNor", T("brown_mud_leaves_01_nor_gl.jpg"));
            ground.SetTexture("_RockTex", T("rock_face_diff.jpg")); ground.SetTexture("_RockNor", T("rock_face_nor_gl.jpg"));
            ground.SetTexture("_ForestTex", T("burned_ground_01_diff.jpg")); ground.SetTexture("_ForestNor", T("burned_ground_01_nor_gl.jpg"));
            ground.SetColor("_RockTint", Hex("#8c887c")); ground.SetColor("_PebbleTint", Hex("#a08a6c")); ground.SetColor("_ForestTint", Hex("#8c8474"));
            ground.SetFloat("_RockScale", 0.11f);
        }

        var puff = AssetDatabase.LoadAssetAtPath<Texture2D>(Tex + "ls_puff.png");
        var dot = AssetDatabase.LoadAssetAtPath<Texture2D>(Tex + "ls_dot.png");
        var flame = Mat("R_Flame", "Universal Render Pipeline/Particles/Unlit"); SetParticle(flame, puff, true);
        var smoke = Mat("R_Smoke", "Universal Render Pipeline/Particles/Unlit"); SetParticle(smoke, puff, false);
        var ember = Mat("R_Ember", "Universal Render Pipeline/Particles/Unlit"); SetParticle(ember, dot, true);
        var wisp = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/V_Wisp.mat");

        AssetDatabase.SaveAssets();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = kind == LookStudyRuins.Kind.Palace ? Hex("#1d0f0b") : Hex("#191a22");
        cam.farClipPlane = 400; cam.nearClipPlane = 0.3f; cam.allowHDR = true; cam.fieldOfView = 38;
        var cd = cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
        cd.renderPostProcessing = true; cd.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing; cd.requiresDepthOption = CameraOverrideOption.On; cd.requiresColorOption = CameraOverrideOption.On;

        var sun = new GameObject("Key Light").AddComponent<Light>();
        sun.type = LightType.Directional; sun.color = Color.white; sun.intensity = 1f; sun.shadows = LightShadows.Soft; sun.shadowNormalBias = 0.5f; sun.shadowBias = 0.4f;
        RenderSettings.sun = sun; RenderSettings.skybox = null; RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = Color.black;

        var vp = kind == LookStudyRuins.Kind.Palace ? Volume("R_VolPalace", -26f, 18f, 0.5f, Hex("#efe4da"), 0.36f) : Volume("R_VolVillage", -24f, 16f, 0.5f, Hex("#e6e4ea"), 0.36f);
        var vgo = new GameObject("Global Volume"); var v = vgo.AddComponent<Volume>(); v.isGlobal = true; v.sharedProfile = vp;

        var go = new GameObject(kind.ToString()); go.SetActive(false);
        var boot = go.AddComponent<LookStudyRuinsBoot>();
        var w = go.AddComponent<LookStudyRuins>();
        w.kind = kind; w.ground = ground; w.flameMat = flame; w.smokeMat = smoke; w.emberMat = ember; w.wispMat = wisp;
        w.parts = Parts(("Choga_Wall", clay), ("Choga_Base", stone), ("Choga_Wood", wood), ("Choga_Roof", thatch), ("Choga_RoofHole", thatch), ("Choga_Ridge", strawM), ("Choga_Rafters", wood),
            ("Prop_Pot", pot), ("Prop_PotLid", pot), ("Prop_PotBroken", pot), ("Prop_Well", stone), ("Prop_Straw", strawM), ("Prop_Fence", wood), ("Prop_Beam", wood), ("Prop_Plank", wood), ("Prop_Cart", wood),
            ("Prop_Palanquin", redWood), ("Prop_RankStone", pavement), ("Prop_TileHeap", tile), ("Prop_BannerPole", wood), ("Prop_BannerCloth", cloth), ("Prop_Basin", dan),
            ("Hall_RoofUpper", tile), ("Hall_RoofLower", tile), ("Hall_RoofUpperHole", tile), ("Hall_Wall", plaster), ("Hall_Pillar", redWood), ("Hall_Bracket", dan),
            ("Terrace_Stone", pavement), ("Terrace_Rail", pavement), ("Gal_Roof", tile), ("Gal_Post", redWood), ("Gal_Wall", plaster), ("Gal_Plinth", pavement), ("Gal_Rafters", wood));
        var rocks = new System.Collections.Generic.List<Mesh>();
        foreach (var n in new[] { "Rock_rock07", "Rock_rock08", "Rock_rock09", "Rock_rock10", "Rock_rock12" }) rocks.Add(M(Models + n + ".fbx"));
        w.rocks = rocks.ToArray();
        var rm = Mat("R_FieldStone", "LookStudy/StageRock");
        rm.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(Models + "rock_moss_set_02_diff.png")); rm.SetTexture("_NormalTex", AssetDatabase.LoadAssetAtPath<Texture2D>(Models + "rock_moss_set_02_nor_gl.png")); rm.SetTexture("_MossTex", T("mossy_rock_diff.jpg"));
        rm.SetColor("_Granite", Hex("#7a766c")); rm.SetFloat("_Remap", 0.8f); rm.SetFloat("_MossAmt", 0.12f); rm.SetFloat("_Wet", 0f); rm.SetFloat("_BurnOn", 1f);
        w.rockMat = rm;
        w.grass = new[] { M("Assets/Art/LookStudy/Models/Real/Grass_Clump01.fbx"), M("Assets/Art/LookStudy/Models/Real/Grass_Ckump02.fbx"), M("Assets/Art/LookStudy/Models/Real/Grass_Ckump03.fbx"), M("Assets/Art/LookStudy/Models/Real/Blade_low1.fbx"), M("Assets/Art/LookStudy/Models/Real/Blade_low2.fbx") };
        w.grassMat = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/V_Grass.mat");
        w.pineBranch = M(Models + "Pine_Branches.fbx"); w.pineTrunk = M(Models + "Pine_Trunk.fbx"); w.pineDead = M(Models + "Pine_Dead.fbx");
        w.pineBranchMat = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/V_PineBranch.mat");
        w.pineTrunkMat = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/V_PineTrunk.mat");
        w.pineDeadMat = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/V_PineDead.mat");
        w.jangseung = M(Models + "Jangseung.fbx"); w.jangseungMat = jm;
        boot.stage = w;
        go.SetActive(true);

var fbx = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/LookStudy/Models/Hunter.fbx") ?? AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/DDConcept/Models/Hunter.fbx");
        var pg = new GameObject("Player");
        pg.transform.position = kind == LookStudyRuins.Kind.Palace ? new Vector3(0, 0.14f, -8) : new Vector3(0, 0, -3);
        var walker = pg.AddComponent<LookStudyWalker>();
        walker.splashMat = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/V_Splash.mat"); walker.mistMat = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/V_Mist.mat"); walker.dustMat = walker.mistMat; walker.gritMat = walker.splashMat;
        walker.cameraOffset = new Vector3(0, 22f, -16f);
        if (fbx)
        {
            var body = (GameObject)Object.Instantiate(fbx, pg.transform, false); body.name = "Body";
            foreach (var r in body.GetComponentsInChildren<Renderer>())
            {
                var src = r.sharedMaterials; var dst = new Material[src.Length];
                for (int i = 0; i < src.Length; i++)
                {
                    var name = src[i] ? src[i].name : "None";
                    dst[i] = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/LookStudy/Materials/LS_H_" + name + ".mat") ?? src[i];
                }
                r.sharedMaterials = dst;
            }
        }
        boot.walker = walker;
        cam.transform.position = pg.transform.position + walker.cameraOffset;
        cam.transform.rotation = Quaternion.LookRotation(-walker.cameraOffset);
        EditorSceneManager.SaveScene(scene, path);
        return path;
    }

    static void SetParticle(Material m, Texture2D tex, bool additive)
    {
        m.SetTexture("_BaseMap", tex);
        m.SetFloat("_Surface", 1f); m.SetFloat("_ZWrite", 0f); m.SetFloat("_Blend", additive ? 2f : 0f);
        m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        m.SetInt("_DstBlend", additive ? (int)BlendMode.One : (int)BlendMode.OneMinusSrcAlpha);
        m.renderQueue = (int)RenderQueue.Transparent;
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.SetOverrideTag("RenderType", "Transparent");
    }

    public static void BuildTestPlayer()
    {
        ShaderUtil.allowAsyncCompilation = false;
        AssetDatabase.Refresh();
        var village = BuildStage(LookStudyRuins.Kind.Village);
        var palace = BuildStage(LookStudyRuins.Kind.Palace);
        var report = BuildPipeline.BuildPlayer(new[] { village, palace }, System.Environment.GetEnvironmentVariable("LS_PLAYER"), BuildTarget.StandaloneWindows64, BuildOptions.None);
        Debug.Log("[LS] BUILD " + report.summary.result + " size " + report.summary.totalSize);
        EditorApplication.Exit(report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
    }
}
