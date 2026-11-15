/* Greatsword hero (Tripo model) import + eye flame particles + edit-mode preview capture.
   Write one line to ArtSource/GSHero/cmd.txt: build | capture. Results go to ArtSource/GSHero/log.txt.
   No menu items. Capture uses a preview scene, so the open scene is never touched. */
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace GSHero
{
    [InitializeOnLoad]
    public static class GSHeroRemote
    {
        const string Root = "Assets/Game/GSHero";
        const string ArtDir = Root + "/Art";
        const string GenDir = Root + "/Generated";
        const string ModelPath = ArtDir + "/GS_Hero.fbx";
        const string PrefabPath = GenDir + "/GS_HeroPreview.prefab";

        static readonly string Dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "ArtSource", "GSHero");
        static string Cmd => Path.Combine(Dir, "cmd.txt");
        static string LogPath => Path.Combine(Dir, "log.txt");
        static double next;

        static GSHeroRemote() { EditorApplication.update += Tick; }

        static void Note(string s)
        {
            try { Directory.CreateDirectory(Dir); File.AppendAllText(LogPath, System.DateTime.Now.ToString("HH:mm:ss") + " " + s + "\n"); }
            catch (IOException) { }
        }

        static void Tick()
        {
            if (cap != null) { CaptureStep(); return; }
            if (EditorApplication.timeSinceStartup < next) return;
            next = EditorApplication.timeSinceStartup + 1.0;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!File.Exists(Cmd)) return;
            string cmd;
            try { cmd = File.ReadAllText(Cmd).Trim(); File.Delete(Cmd); }
            catch (IOException) { return; }
            try
            {
                if (cmd == "build") Note(Build());
                else if (cmd == "capture") { still = false; Note(CaptureStart()); }
                else if (cmd == "still") { still = true; Note(CaptureStart()); }
                else Note("unknown command: " + cmd);
            }
            catch (System.Exception e) { Note("ERROR " + cmd + ": " + e); }
        }

        static Material Mat(string name, string shader)
        {
            string p = GenDir + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (!m) { m = new Material(Shader.Find(shader)); AssetDatabase.CreateAsset(m, p); }
            return m;
        }

        static string Build()
        {
            var log = new List<string>();
            if (!AssetDatabase.IsValidFolder(GenDir)) AssetDatabase.CreateFolder(Root, "Generated");
            AssetDatabase.Refresh();

            var mi = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (!mi) return "build: model missing " + ModelPath;
            if (mi.animationType != ModelImporterAnimationType.Human || mi.materialImportMode != ModelImporterMaterialImportMode.None)
            {
                mi.animationType = ModelImporterAnimationType.Human;
                mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                mi.materialImportMode = ModelImporterMaterialImportMode.None;
                mi.importAnimation = false;
                mi.importBlendShapes = false;
                mi.SaveAndReimport();
            }
            var avatar = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Avatar>().FirstOrDefault();
            log.Add("avatar " + (avatar ? ("valid=" + avatar.isValid + " human=" + avatar.isHuman) : "NONE"));

            // v3 (11-15): the Tripo atlases are patchy islands; at the top-down distance the default mip (bias 0, aniso 1, normal
            // DXT) averaged neighbouring islands into grey mush. Sharper mip (bias -0.7), aniso 4, high-quality (BC7) compression.
            foreach (var tn in new[] { "GS_Hero_basecolor.png", "GS_Shirt_basecolor.png", "GS_Greatsword_basecolor_iron.png", "GS_Shirt_normal.png",
                                       "GS_Hero_basecolor_flat.png", "GS_Shirt_basecolor_flat.png", "GS_Greatsword_basecolor_iron_flat.png" })
            {
                var ti0 = AssetImporter.GetAtPath(ArtDir + "/" + tn) as TextureImporter;
                if (!ti0) continue;
                if (Mathf.Approximately(ti0.mipMapBias, -0.7f) && ti0.anisoLevel == 4 && ti0.textureCompression == TextureImporterCompression.CompressedHQ) continue;
                ti0.mipMapBias = -0.7f; ti0.anisoLevel = 4; ti0.textureCompression = TextureImporterCompression.CompressedHQ;
                ti0.SaveAndReimport();
                log.Add("texture import sharpened: " + tn);
            }
            // 11-15 visibility pass: the *_flat copies (ArtSource/GSHero/flatten_textures.py: colour classes blurred inside
            // themselves, pulled toward the class mean) read as big colour blocks from the top-down camera; the originals
            // stay next to them. UseFlatTextures = false puts the originals back.
            string Tex(string n) => UseFlatTextures && AssetDatabase.LoadAssetAtPath<Texture2D>(ArtDir + "/" + n + "_flat.png") ? n + "_flat.png" : n + ".png";
            var baseTex = AssetDatabase.LoadAssetAtPath<Texture2D>(ArtDir + "/" + Tex("GS_Hero_basecolor"));
            var emTex = AssetDatabase.LoadAssetAtPath<Texture2D>(ArtDir + "/GS_Hero_emission.png");
            var swTex = AssetDatabase.LoadAssetAtPath<Texture2D>(ArtDir + "/" + Tex("GS_Greatsword_basecolor_iron"));
            log.Add("textures: " + Tex("GS_Hero_basecolor") + ", " + Tex("GS_Shirt_basecolor") + ", " + Tex("GS_Greatsword_basecolor_iron"));
            log.Add("textures base=" + (baseTex != null) + " em=" + (emTex != null) + " sword=" + (swTex != null));

            // 11-15 readability pass: URP/Lit made the near-black GS textures a black/navy blob from the top-down camera in the
            // dark fields. GSHero/Readable keeps the textures, lifts the darks, wraps the moon light, adds a faint lit-side rim
            // and sky fill, and tints the 삿갓 (hat mask mesh, BakeHatMesh) a muted straw. Matte; the sword is dull iron.
            var readable = Shader.Find("GSHero/Readable");
            log.Add("materials: shader " + (readable ? readable.name : "GSHero/Readable MISSING"));
            var body = Mat("M_GSHero", "GSHero/Readable");
            if (readable) body.shader = readable;
            body.shaderKeywords = new string[0];
            body.SetTexture("_BaseMap", baseTex);
            body.SetColor("_BaseColor", Color.white);
            // v3 (11-15, Kido: "뿌옇고 잘 안 보인다"): the 0.62 lift + 2.4 ambient flattened the darks into grey; now a small lift, the
            // whole albedo scaled up instead (keeps the dark/light ratio), less desaturation
            body.SetColor("_BaseColor", new Color(1.3f, 1.3f, 1.3f)); body.SetFloat("_Lift", 0.32f); body.SetFloat("_Desat", 0.05f);
            body.SetFloat("_HatAmount", 0.85f); body.SetFloat("_Metal", 0f);
            body.SetTexture("_EmissionMap", emTex);
            body.SetColor("_EmissionColor", new Color(1.4f, 1.4f, 1.4f, 1f));
            body.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            EditorUtility.SetDirty(body);

            var sword = Mat("M_GSGreatsword", "GSHero/Readable");
            if (readable) sword.shader = readable;
            sword.shaderKeywords = new string[0];
            sword.SetTexture("_BaseMap", swTex);
            sword.SetColor("_BaseColor", Color.white);
            sword.SetColor("_BaseColor", new Color(1.2f, 1.2f, 1.2f)); sword.SetFloat("_Lift", 0.25f); sword.SetFloat("_Desat", 0.1f);
            sword.SetFloat("_HatAmount", 0f);   // no vertex colours here: COLOR may read white, so the hat tint stays off
            sword.SetFloat("_Metal", 0.4f); sword.SetFloat("_Sheen", 0.5f); sword.SetFloat("_SheenPower", 10f);
            EditorUtility.SetDirty(sword);

            var shirt = Mat("M_GSShirt", "GSHero/Readable");
            if (readable) shirt.shader = readable;
            shirt.shaderKeywords = new string[0];
            shirt.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(ArtDir + "/" + Tex("GS_Shirt_basecolor")));
            shirt.SetColor("_BaseColor", Color.white);
            shirt.SetColor("_BaseColor", new Color(1.3f, 1.3f, 1.3f)); shirt.SetFloat("_Lift", 0.3f); shirt.SetFloat("_Desat", 0.03f);
            shirt.SetFloat("_HatAmount", 0f); shirt.SetFloat("_Metal", 0f);
            EditorUtility.SetDirty(shirt);
            // shared look: neutral rim (FieldLighting skips these renderers, its cool glow made them navy), light hue mostly
            // neutralised so the shirt/hat keep their colour under the blue moon
            foreach (var m in new[] { body, sword, shirt })
            {
                m.SetFloat("_LightHue", 0.35f); m.SetFloat("_RimStrength", 0.5f); m.SetFloat("_RimWidth", 0.22f);   // v3: thin, crisp rim (was 0.3 / 0.45 wide and soft)
                m.SetColor("_RimColor", new Color(0.85f, 0.82f, 0.75f)); m.SetFloat("_AmbientBoost", 1.6f); m.SetFloat("_NoFog", 1f);
                m.SetFloat("_Wrap", 0.3f); m.SetFloat("_SkyFill", 0.25f); m.SetFloat("_ShadowFloor", 0.3f);
            }
            body.SetFloat("_HatAmount", 0.9f); body.SetColor("_HatColor", new Color(0.46f, 0.38f, 0.25f));

            // 11-15 visibility pass (hero buried in the background / among monsters): value split instead of a grey lift.
            // Less dark lift (black armour stays black), a top key that follows the hero (shader only, albedo-multiplied, so
            // hat/skin/shirt/steel come up), a brighter straw hat, a thin crisp warm-neutral rim. Sword: clean steel block
            // with a matte top highlight and a bright bevel line. VisibilityPass = false keeps the v3 values above.
            if (VisibilityPass)
            {
                foreach (var m in new[] { body, sword, shirt })
                {
                    m.SetFloat("_TopKey", 0.85f); m.SetColor("_TopKeyColor", new Color(1f, 0.95f, 0.86f)); m.SetFloat("_TopKeyPower", 1.6f);
                    m.SetFloat("_RimSharp", 0.85f); m.SetFloat("_RimWidth", 0.3f); m.SetFloat("_RimStrength", 0.75f);
                    m.SetColor("_RimColor", new Color(0.95f, 0.89f, 0.78f)); m.SetColor("_OccColor", new Color(1f, 0.9f, 0.72f));
                    m.SetFloat("_Steel", 0f); m.SetFloat("_EdgeGlint", 0f);
                }
                body.SetColor("_BaseColor", new Color(1.25f, 1.25f, 1.25f)); body.SetFloat("_Lift", 0.16f); body.SetFloat("_Desat", 0.03f);
                body.SetColor("_HatColor", new Color(0.64f, 0.52f, 0.33f)); body.SetFloat("_HatAmount", 0.95f);
                shirt.SetColor("_BaseColor", new Color(1.3f, 1.3f, 1.3f)); shirt.SetFloat("_Lift", 0.18f); shirt.SetFloat("_Desat", 0f);
                sword.SetColor("_BaseColor", Color.white); sword.SetFloat("_Lift", 0.1f); sword.SetFloat("_Desat", 0.6f);
                sword.SetFloat("_Steel", 0.85f); sword.SetColor("_SteelColor", new Color(0.66f, 0.68f, 0.70f));
                sword.SetFloat("_Metal", 0.4f); sword.SetFloat("_EdgeGlint", 0.9f); sword.SetFloat("_TopKey", 1.0f);
                sword.SetFloat("_RimStrength", 0.5f);
            }
            foreach (var m in new[] { body, sword, shirt }) EditorUtility.SetDirty(m);

            var flame = Mat("M_GSEyeFlame", "Universal Render Pipeline/Particles/Unlit");
            flame.SetTexture("_BaseMap", AssetDatabase.GetBuiltinExtraResource<Texture2D>("Default-Particle.psd"));
            flame.SetColor("_BaseColor", new Color(0.55f, 1.5f, 3.0f, 1f));
            flame.SetFloat("_Surface", 1f);
            flame.SetFloat("_Blend", 2f);
            flame.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            flame.SetFloat("_DstBlend", (float)BlendMode.One);
            flame.SetFloat("_ZWrite", 0f);
            flame.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            flame.SetOverrideTag("RenderType", "Transparent");
            flame.renderQueue = 3000;
            EditorUtility.SetDirty(flame);

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var root = new GameObject("GS_HeroPreview");
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            inst.transform.SetParent(root.transform, false);
            var hatMesh = BakeHatMesh(inst, log);
            foreach (var r in inst.GetComponentsInChildren<Renderer>())
            {
                bool isSword = r.name.Contains("Sword");
                var ms = r.sharedMaterials;
                for (int i = 0; i < ms.Length; i++) ms[i] = isSword ? sword : (r.name.Contains("Shirt") ? shirt : body);
                r.sharedMaterials = ms;
                if (hatMesh && r.name == "GS_Hero" && r is SkinnedMeshRenderer hs) hs.sharedMesh = hatMesh;
                var smr = r as SkinnedMeshRenderer;
                if (smr) smr.updateWhenOffscreen = true;
                log.Add("renderer " + r.name + " " + r.GetType().Name + " bounds " + r.bounds.size.ToString("F2") + " center " + r.bounds.center.ToString("F2"));
            }
            var anim = inst.GetComponent<Animator>();
            if (anim) { anim.applyRootMotion = false; anim.cullingMode = AnimatorCullingMode.AlwaysAnimate; }

            AttachEyeFlames(root, inst, flame, log);

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            log.Add("build: saved " + PrefabPath);
            return string.Join("\n", log);
        }

        // Eye flames + GSEyeFlameFlow on `host`. `inst` is the GS_Hero model instance (its transform may carry a size
        // scale, which the flames and the flow follow). Model-space positions are at scale 1 and get mapped through inst.
        static void AttachEyeFlames(GameObject host, GameObject inst, Material flame, List<string> log)
        {
            int eyes = 0;
            foreach (var t in inst.GetComponentsInChildren<Transform>())
            {
                if (t.name != "GS_EyeL" && t.name != "GS_EyeR") continue;
                eyes++;
                log.Add(t.name + " pos " + t.position.ToString("F3") + " lossyScale " + t.lossyScale.ToString("F3") + " parent " + t.parent.name);
                MakeFlame(t, flame, inst.transform);
            }
            log.Add("eyes " + eyes);
            Transform headT = null;
            foreach (var t in inst.GetComponentsInChildren<Transform>()) if (t.name == "mixamorig:Head") headT = t;
            if (headT)
            {
                var it = inst.transform;
                var flow = host.AddComponent<GSEyeFlameFlow>();
                flow.head = headT;
                flow.systems = host.GetComponentsInChildren<ParticleSystem>();
                flow.apexLocal = headT.InverseTransformPoint(it.TransformPoint(new Vector3(-0.006f, 1.805f, 0.016f)));
                flow.axisLocal = headT.InverseTransformDirection(it.up);
                flow.forwardLocal = headT.InverseTransformDirection(it.forward);
                flow.headCenterLocal = headT.InverseTransformPoint(it.TransformPoint(new Vector3(-0.006f, 1.635f, 0.02f)));
                flow.scaleRoot = it;
                flow.builtScale = 1f;
            }
        }

        public const string RRPrefabPath = GenDir + "/RR_GSHeroPlayer.prefab";
        // 11-15 visibility pass switches (false = the look before it, for A/B)
        public static bool VisibilityPass = true, UseFlatTextures = true;
        public const float BladeWidthScale = 1.12f;   // blade width (grip node local X) for the top-down silhouette

        // In-game RoomRun player built from GS_Hero (11-15): same controller/clips as the old 착호갑사 player (humanoid
        // retarget), GS_Sword from the FBX re-rooted under a "Greatsword" grip node on the right hand (grip at origin,
        // blade along +Z, the convention GreatswordCombat/BladeTrail/LSPoseFix look for), eye flames + flow, LSPoseFix with
        // the 삿갓 measured from the mesh. Sized so the head height matches `refModel` (the old player body). No lights.
        public static GameObject BuildRRPlayer(RuntimeAnimatorController ctrl, GameObject refModel, List<string> log)
        {
            log.Add(Build());
            var body = AssetDatabase.LoadAssetAtPath<Material>(GenDir + "/M_GSHero.mat");
            var swordM = AssetDatabase.LoadAssetAtPath<Material>(GenDir + "/M_GSGreatsword.mat");
            var shirt = AssetDatabase.LoadAssetAtPath<Material>(GenDir + "/M_GSShirt.mat");
            var flame = AssetDatabase.LoadAssetAtPath<Material>(GenDir + "/M_GSEyeFlame.mat");
            var hatMeshRR = AssetDatabase.LoadAssetAtPath<Mesh>(HatMeshPath);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);

            float HeadY(GameObject m)
            {
                var g = (GameObject)Object.Instantiate(m);
                g.hideFlags = HideFlags.HideAndDontSave;
                // top of the skinned body (hat included): the old 착호갑사 is a chibi with a big hat, so matching the head
                // bone made GS_Hero far too small next to it (11-15 first in-game shots)
                float y = 0f;
                foreach (var smr in g.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    var bm = new Mesh(); smr.BakeMesh(bm, true);   // true: these FBX renderers carry 100x, false double-scales
                    foreach (var v in bm.vertices) y = Mathf.Max(y, g.transform.InverseTransformPoint(smr.transform.TransformPoint(v)).y);
                    Object.DestroyImmediate(bm);
                }
                Object.DestroyImmediate(g);
                return y;
            }
            float refY = refModel ? HeadY(refModel) : 0f, gsY = HeadY(model);
            float k = refY > 0.3f && gsY > 0.3f ? refY / gsY : 1f;
            log.Add($"rrplayer gs: top y ref {refY:0.000} gs {gsY:0.000} -> body scale {k:0.000}");

            var root = new GameObject("RR_GSHeroPlayer");
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            inst.name = "GS_Hero";
            inst.transform.SetParent(root.transform, false);
            inst.transform.localScale = Vector3.one * k;
            foreach (var r in inst.GetComponentsInChildren<Renderer>())
            {
                bool isSword = r.name.Contains("Sword");
                var ms = r.sharedMaterials;
                for (int i = 0; i < ms.Length; i++) ms[i] = isSword ? swordM : (r.name.Contains("Shirt") ? shirt : body);
                r.sharedMaterials = ms;
                if (hatMeshRR && r.name == "GS_Hero" && r is SkinnedMeshRenderer hs) hs.sharedMesh = hatMeshRR;
                r.shadowCastingMode = ShadowCastingMode.On;
                if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
            }
            var anim = inst.GetComponent<Animator>() ?? inst.AddComponent<Animator>();
            anim.runtimeAnimatorController = ctrl;
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            // sword: long axis of the mesh (in its own space), tip = the end away from the hand
            var hand = anim.GetBoneTransform(HumanBodyBones.RightHand);
            Transform swordT = null;
            foreach (var t in inst.GetComponentsInChildren<Transform>()) if (t.name == "GS_Sword") swordT = t;
            if (swordT && hand)
            {
                var mf = swordT.GetComponent<MeshFilter>();
                var b = mf.sharedMesh.bounds;
                int ax = b.size.x >= b.size.y && b.size.x >= b.size.z ? 0 : b.size.y >= b.size.z ? 1 : 2;
                int thin = b.size.x <= b.size.y && b.size.x <= b.size.z ? 0 : b.size.y <= b.size.z ? 1 : 2;
                Vector3 e = Vector3.zero; e[ax] = 1f;
                Vector3 n = Vector3.zero; n[thin] = 1f;
                Vector3 c = swordT.TransformPoint(b.center);
                Vector3 dir = swordT.TransformDirection(e).normalized;
                Vector3 endA = swordT.TransformPoint(b.center + e * b.extents[ax]), endB = swordT.TransformPoint(b.center - e * b.extents[ax]);
                if ((endA - hand.position).sqrMagnitude < (endB - hand.position).sqrMagnitude) dir = -dir;
                Vector3 grip = c + dir * Vector3.Dot(hand.position - c, dir);   // the hand projected onto the blade axis
                var g = new GameObject("Greatsword").transform;
                g.SetParent(hand, false);
                g.SetPositionAndRotation(grip, Quaternion.LookRotation(dir, swordT.TransformDirection(n)));
                var hl = hand.lossyScale;
                g.localScale = new Vector3(k / hl.x, k / hl.y, k / hl.z);   // world scale = character scale
                // 11-15 visibility: blade ~12% wider (X = across the blade; Y = thickness, Z = length stay), reads from above
                if (VisibilityPass) g.localScale = Vector3.Scale(g.localScale, new Vector3(BladeWidthScale, 1f, 1f));
                log.Add($"rrplayer gs: sword parent was {swordT.parent.name}, axis {ax}, grip off hand {(grip - hand.position).magnitude:0.000}, hand lossy {hl.x:0.000}");
                swordT.SetParent(g, true);

                // The FBX holds the blade upright in front of the face, so LSPoseFix had to turn it 60° into the ground.
                // Grip it like LSRig's auto grip on the old player: blade along the left-to-right hand line of the idle
                // pose, flat facing the palm, grip 7 cm along the fingers (measured on a throwaway posed copy).
                // 11-15: the berserker clips were keyed in Blender with the sword in the FBX grip (GS_Sword under the right
                // hand as exported), so keep that grip; the idle-hands re-grip below is for the Mixamo set only
                var idle = LocoSplit.LSBuilder.UseBerserker ? null : FindClip("Idle");
                if (LocoSplit.LSBuilder.UseBerserker) log.Add("rrplayer gs: sword keeps the FBX grip (berserker clips)");
                if (idle)
                {
                    var tmp = Object.Instantiate(inst);
                    tmp.hideFlags = HideFlags.HideAndDontSave;
                    var ta = tmp.GetComponent<Animator>();
                    idle.SampleAnimation(tmp, 0f);
                    var r = ta.GetBoneTransform(HumanBodyBones.RightHand);
                    var l = ta.GetBoneTransform(HumanBodyBones.LeftHand);
                    Vector3 hdir = r.InverseTransformDirection((r.position - l.position).normalized);
                    Object.DestroyImmediate(tmp);
                    g.localRotation = Quaternion.LookRotation(hdir, Vector3.right);
                    g.localPosition = new Vector3(0f, 0.07f * k / hl.y, 0f);
                    log.Add($"rrplayer gs: sword grip from {idle.name} hands, dir in hand {hdir.ToString("F2")}");
                }
            }
            else log.Add("rrplayer gs: GS_Sword or right hand MISSING");

            AttachEyeFlames(root, inst, flame, log);
            // in game the head dashes and swings fast: no emission over distance (it laid metre-long streaks along every
            // dash), old particles keep following the head, and nothing drifts farther than ~0.45 m from the hat top
            // 11-15 readability: a bit smaller, thinner and shorter, so the fire does not cover the face and hat from above
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>())
            {
                // v3: 0.55 rate / 0.65 size (was 0.7 / 0.75): the additive fire still laid a pale film over the face and chest
                var em = ps.emission; em.rateOverDistance = 0f; em.rateOverTimeMultiplier = 240f * 0.55f;
                var mn = ps.main;
                mn.startSize = new ParticleSystem.MinMaxCurve(0.06f * 0.65f, 0.115f * 0.65f);   // MakeFlame: 0.06-0.115
                mn.startLifetime = new ParticleSystem.MinMaxCurve(0.5f * 0.8f, 0.85f * 0.8f);   // MakeFlame: 0.5-0.85
            }
            var flowRR = root.GetComponent<GSEyeFlameFlow>();
            if (flowRR) { flowRR.lateCarry = 0.8f; flowRR.maxDist = 0.45f; }

            var fix = root.AddComponent<LocoSplit.LSPoseFix>();
            fix.anim = anim;
            if (LocoSplit.LSBuilder.UseBerserker)
            {
                // the berserker clips are posed as wanted (forward lean in the run, the slam's bend): no upright pitch-back
                fix.uprightIdleDeg = 0f; fix.uprightRunDeg = 0f; fix.hipsIdleDeg = 0f; fix.hipsRunDeg = 0f;
            }
            {
                var tmp = Object.Instantiate(root);
                tmp.hideFlags = HideFlags.HideAndDontSave;
                var tAnim = tmp.GetComponentInChildren<Animator>();
                var tFix = tmp.GetComponent<LocoSplit.LSPoseFix>();
                tAnim.Rebind(); tAnim.Play("Locomotion", 0, 0f); tAnim.Update(0f);
                log.Add("rrplayer gs: " + LocoSplit.LSRemote.MeasureHat(tmp, tAnim, tFix));
                fix.hatCenterXZ = tFix.hatCenterXZ; fix.hatYs = tFix.hatYs; fix.hatRs = tFix.hatRs;
                Object.DestroyImmediate(tmp);
            }

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, RRPrefabPath);
            Object.DestroyImmediate(root);
            log.Add("rrplayer gs: saved " + RRPrefabPath);
            return prefab;
        }

        // 11-15 facing bug ("attacks turn round and hit behind"): every clip of the RR controller sampled on the GS_Hero and the
        // old 착호갑사 prefab (root at identity, so root forward = +Z = the aim). Logs per clip/time the yaw of the chest (shoulder
        // line), the hips and the blade tip relative to the root. 0 = facing the aim, ±180 = turned round.
        // rr_queue: call GSHero.GSHeroRemote.FacingProbe -> ArtSource/GSHero/facing_probe.txt
        // 11-15 limb fix: the berserker clips sampled through the humanoid avatar on the in-game prefab, rendered from the
        // side and from above at the frames of the Blender check sheets, so Blender and Unity can be compared frame by
        // frame (retarget / muscle clamps). rr_queue: `call GSHero.GSHeroRemote.ClipShots` -> ArtSource/GSHero/ingame_shots/limb_fix/unity/
        public static void ClipShots()
        {
            string dir = System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, "ArtSource", "GSHero", "ingame_shots", "limb_fix", "unity");
            System.IO.Directory.CreateDirectory(dir);
            var pf = AssetDatabase.LoadAssetAtPath<GameObject>(RRPrefabPath);
            var go = Object.Instantiate(pf); go.hideFlags = HideFlags.HideAndDontSave;
            go.transform.SetPositionAndRotation(new Vector3(0f, -500f, 0f), Quaternion.identity);
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 31;
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true)) ps.gameObject.SetActive(false);
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true)) mb.enabled = false;
            var anim = go.GetComponentInChildren<Animator>();
            var camGo = new GameObject("clipshot cam") { hideFlags = HideFlags.HideAndDontSave };
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true; cam.orthographicSize = 1.15f; cam.cullingMask = 1 << 31;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.22f, 0.22f, 0.24f);
            var lightGo = new GameObject("clipshot light") { hideFlags = HideFlags.HideAndDontSave };
            var li = lightGo.AddComponent<Light>(); li.type = LightType.Directional; li.intensity = 2.2f; li.cullingMask = 1 << 31;
            lightGo.transform.rotation = Quaternion.Euler(40f, -60f, 0f);
            const int S = 360;
            var rt = new RenderTexture(S, S, 24) { antiAliasing = 4 }; var tex = new Texture2D(S, S, TextureFormat.RGB24, false);
            cam.targetTexture = rt;
            var frames = new Dictionary<string, int[]>
            {
                { "BZ_Slash1", new[] { 0, 2, 3, 4, 5, 8, 12, 16 } }, { "BZ_Slash2", new[] { 0, 2, 3, 4, 5, 9, 14, 20 } },
                { "BZ_Slash3", new[] { 0, 4, 6, 7, 8, 9, 16, 28 } }, { "BZ_Dash", new[] { 0, 2, 5, 8, 11, 22, 26, 30 } },
                { "BZ_Run", new[] { 0, 3, 6, 9, 12, 15, 18, 20 } }, { "BZ_Idle", new[] { 0, 20 } },
            };
            var clips = anim.runtimeAnimatorController.animationClips.Distinct().ToDictionary(c => c.name);
            var lines = new List<string>();
            var root = go.transform.position;
            foreach (var kv in frames)
            {
                if (!clips.TryGetValue(kv.Key, out var clip)) { lines.Add("missing " + kv.Key); continue; }
                foreach (int f in kv.Value)
                {
                    clip.SampleAnimation(anim.gameObject, Mathf.Min(f / 30f, clip.length));
                    // side: from the character's right (+X in Unity = the Blender side camera at +X after the FBX axis swap)
                    foreach (var (tag, pos, rot) in new[] {
                        ("side", root + new Vector3(-6f, 0.95f, 0f), Quaternion.LookRotation(Vector3.right)),
                        ("top", root + new Vector3(0f, 0.9f, 0f) + Quaternion.Euler(55f, 180f, 0f) * Vector3.back * 6f, Quaternion.Euler(55f, 180f, 0f)) })
                    {
                        cam.transform.SetPositionAndRotation(pos, rot);
                        cam.Render();
                        RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, S, S), 0, 0); RenderTexture.active = null;
                        System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, $"{kv.Key}_{tag}_f{f:000}.png"), tex.EncodeToPNG());
                    }
                    // knee direction vs toes and thigh roll off the hips, as the humanoid retarget leaves them
                    string Leg(HumanBodyBones up, HumanBodyBones lo, HumanBodyBones foot, HumanBodyBones toes)
                    {
                        var a = anim.GetBoneTransform(up).position; var k = anim.GetBoneTransform(lo).position; var ft = anim.GetBoneTransform(foot).position;
                        var tt = anim.GetBoneTransform(toes); var toe = tt ? tt.position - ft : anim.transform.forward; toe.y = 0;
                        var line = (ft - a).normalized; var kvv = k - a; kvv -= line * Vector3.Dot(kvv, line); kvv.y = 0;
                        return kvv.sqrMagnitude < 1e-6f ? "straight" : $"{Vector3.Dot(kvv.normalized, toe.normalized):+0.00;-0.00}";
                    }
                    lines.Add($"{kv.Key} f{f}: kneeToToe L {Leg(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes)} R {Leg(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, HumanBodyBones.RightToes)}");
                }
            }
            System.IO.File.WriteAllLines(System.IO.Path.Combine(dir, "knees.txt"), lines);
            cam.targetTexture = null;
            Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
            Object.DestroyImmediate(go); Object.DestroyImmediate(camGo); Object.DestroyImmediate(lightGo);
        }

        public static void FacingProbe()
        {
            var lines = new List<string>();
            float Yaw(Vector3 v) { v.y = 0; return v.sqrMagnitude < 1e-6f ? float.NaN : Vector3.SignedAngle(Vector3.forward, v, Vector3.up); }
            foreach (var path in new[] { RRPrefabPath, "Assets/Game/LocoSplit/Generated/RR_GreatswordPlayer.prefab" })
            {
                var pf = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (!pf) { lines.Add("missing " + path); continue; }
                var go = Object.Instantiate(pf); go.hideFlags = HideFlags.HideAndDontSave;
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var anim = go.GetComponentInChildren<Animator>();
                var ctrl = anim ? anim.runtimeAnimatorController : null;
                lines.Add($"== {path} animator on {(anim ? anim.name : "none")} local rot {(anim ? anim.transform.localEulerAngles.ToString("F0") : "")} avatar {(anim && anim.avatar ? anim.avatar.name : "none")}");
                Transform sword = null;
                foreach (var t in go.GetComponentsInChildren<Transform>()) if (t.name == "Greatsword") sword = t;
                if (ctrl)
                    foreach (var clip in ctrl.animationClips.Distinct())
                    {
                        var sb = new System.Text.StringBuilder(clip.name.PadRight(22));
                        foreach (float f in new[] { 0f, 0.25f, 0.45f, 0.65f, 0.9f })
                        {
                            clip.SampleAnimation(anim.gameObject, f * clip.length);
                            var l = anim.GetBoneTransform(HumanBodyBones.LeftUpperArm); var r = anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
                            var hl = anim.GetBoneTransform(HumanBodyBones.LeftUpperLeg); var hr = anim.GetBoneTransform(HumanBodyBones.RightUpperLeg);
                            float chest = l && r ? Yaw(Vector3.Cross(r.position - l.position, Vector3.up)) : float.NaN;
                            float hips = hl && hr ? Yaw(Vector3.Cross(hr.position - hl.position, Vector3.up)) : float.NaN;
                            float blade = sword ? Yaw(sword.forward) : float.NaN;
                            sb.Append($" | t{f:0.00} chest {chest,5:0} hips {hips,5:0} blade {blade,5:0}");
                        }
                        lines.Add(sb.ToString());
                    }
                Object.DestroyImmediate(go);
            }
            File.WriteAllLines(Path.Combine(Dir, "facing_probe.txt"), lines);
        }

        // 11-15 berserker clips keyed in Blender from Kido's LoA recordings (ArtSource/GSHero/berserker_timing.md, script
        // ArtSource/GSHero/gs_berserker_anim.py, blend copy Downloads/gs_anim/gs_hero_berserker_anim_v2.blend).
        // Humanoid with its own avatar (same GS_Rig skeleton), root baked into the pose, run looped. Null when missing.
        public const string BerserkerFbx = ArtDir + "/GS_BerserkerAnims.fbx";
        public static Dictionary<string, AnimationClip> BerserkerClips(List<string> log)
        {
            var mi = AssetImporter.GetAtPath(BerserkerFbx) as ModelImporter;
            if (!mi) { log.Add("berserker: " + BerserkerFbx + " missing"); return null; }
            bool dirty = false;
            if (mi.animationType != ModelImporterAnimationType.Human || mi.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel)
            { mi.animationType = ModelImporterAnimationType.Human; mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel; dirty = true; }
            if (mi.materialImportMode != ModelImporterMaterialImportMode.None) { mi.materialImportMode = ModelImporterMaterialImportMode.None; dirty = true; }
            // limb fix 11-15: keyframe reduction blurred the 2-3 f swings and the per-frame wrist / knee corrections
            if (mi.animationCompression != ModelImporterAnimationCompression.Off) { mi.animationCompression = ModelImporterAnimationCompression.Off; dirty = true; }
            if (dirty) mi.SaveAndReimport();
            var takes = mi.defaultClipAnimations;
            var outClips = new List<ModelImporterClipAnimation>();
            foreach (var t in takes)
            {
                string n = t.takeName; int bar = n.LastIndexOf('|'); if (bar >= 0) n = n.Substring(bar + 1);
                if (!n.StartsWith("BZ_")) continue;
                t.name = n;
                t.loopTime = n == "BZ_Run" || n == "BZ_Idle";
                t.lockRootRotation = true; t.keepOriginalOrientation = true;
                t.lockRootHeightY = true; t.keepOriginalPositionY = true;
                t.lockRootPositionXZ = true; t.keepOriginalPositionXZ = true;
                outClips.Add(t);
            }
            mi.clipAnimations = outClips.ToArray();
            mi.SaveAndReimport();
            var dict = AssetDatabase.LoadAllAssetsAtPath(BerserkerFbx).OfType<AnimationClip>().Where(c => c.name.StartsWith("BZ_")).ToDictionary(c => c.name);
            log.Add("berserker clips: " + string.Join(", ", dict.Values.Select(c => $"{c.name} {c.length:0.00}s")));
            return dict.Count > 0 ? dict : null;
        }

        const string HatMeshPath = GenDir + "/GS_Hero_HatMask.asset";

        // Copy of the GS_Hero body mesh with vertex colour R = 1 on the 삿갓 (GSHero/Readable tints it straw). Hat = head-weighted
        // vertices on the brim (more than 0.11 m off the head axis) or above the forehead (0.13 m over the head bone), measured
        // in head space so the pose does not matter (LSRemote.MeasureHat profile: brim r 0.18 at y 0.06-0.12).
        static Mesh BakeHatMesh(GameObject inst, List<string> log)
        {
            SkinnedMeshRenderer smr = null;
            foreach (var r in inst.GetComponentsInChildren<SkinnedMeshRenderer>()) if (r.name == "GS_Hero") smr = r;
            Transform head = null;
            foreach (var t in inst.GetComponentsInChildren<Transform>()) if (t.name == "mixamorig:Head") head = t;
            if (!smr || !head || !smr.sharedMesh) { log.Add("hat mesh: body or head missing"); return null; }
            var src = smr.sharedMesh;
            if (src.name.EndsWith("_HatMask")) { log.Add("hat mesh: renderer already has the mask mesh"); return src; }
            int hi = System.Array.IndexOf(smr.bones, head);
            var baked = new Mesh(); smr.BakeMesh(baked, true);
            var v = baked.vertices; var bw = src.boneWeights;
            float sc = Mathf.Abs(inst.transform.lossyScale.y);
            var pts = new Vector3[v.Length]; var isHead = new bool[v.Length];
            Vector3 mn = Vector3.one * 1e9f, mx = -Vector3.one * 1e9f;
            for (int i = 0; i < v.Length && i < bw.Length; i++)
            {
                float w = (bw[i].boneIndex0 == hi ? bw[i].weight0 : 0) + (bw[i].boneIndex1 == hi ? bw[i].weight1 : 0);
                if (w < 0.5f) continue;
                isHead[i] = true;
                pts[i] = Quaternion.Inverse(head.rotation) * (smr.transform.TransformPoint(v[i]) - head.position) / sc;
                mn = Vector3.Min(mn, pts[i]); mx = Vector3.Max(mx, pts[i]);
            }
            Object.DestroyImmediate(baked);
            var c = new Vector2((mn.x + mx.x) * 0.5f, (mn.z + mx.z) * 0.5f);
            var cols = new Color[src.vertexCount];
            int n = 0;
            for (int i = 0; i < cols.Length; i++)
            {
                bool hat = i < isHead.Length && isHead[i] && (new Vector2(pts[i].x - c.x, pts[i].z - c.y).magnitude > 0.11f || pts[i].y > 0.13f);
                cols[i] = hat ? new Color(1, 0, 0, 1) : new Color(0, 0, 0, 1);
                if (hat) n++;
            }
            var m = Object.Instantiate(src);
            m.name = src.name + "_HatMask";
            m.colors = cols;
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(HatMeshPath);
            if (existing) { EditorUtility.CopySerialized(m, existing); Object.DestroyImmediate(m); m = existing; }
            else AssetDatabase.CreateAsset(m, HatMeshPath);
            log.Add($"hat mesh: {n} of {cols.Length} verts marked (head-space y {mn.y:0.00}..{mx.y:0.00}) -> {HatMeshPath}");
            return m;
        }

        static void MakeFlame(Transform eye, Material mat, Transform root)
        {
            var go = new GameObject("EyeFlame");
            go.transform.SetParent(eye, false);
            float k = Mathf.Abs(root.lossyScale.y);   // the flames take the character's scale, not the FBX eye empties' 100x
            go.transform.position = eye.position + root.forward * 0.015f * k;
            go.transform.rotation = Quaternion.LookRotation(root.forward, Vector3.up) * Quaternion.Euler(7f, 0f, 0f);
            var ls = eye.lossyScale;
            go.transform.localScale = new Vector3(k / Mathf.Max(1e-5f, Mathf.Abs(ls.x)), k / Mathf.Max(1e-5f, Mathf.Abs(ls.y)), k / Mathf.Max(1e-5f, Mathf.Abs(ls.z)));
            var ps = go.AddComponent<ParticleSystem>();

            var main = ps.main;
            main.duration = 1f;
            main.loop = true;
            main.playOnAwake = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.85f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 0.8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.115f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = Color.white;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 900;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;

            var em = ps.emission;
            em.rateOverTime = 240f;
            em.rateOverDistance = 70f;

            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Cone;
            sh.angle = 9f;
            sh.radius = 0.006f;

            var noise = ps.noise;
            noise.enabled = true;
            noise.separateAxes = false;
            noise.strength = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.01f), new Keyframe(0.35f, 0.03f), new Keyframe(1f, 0.05f)));
            noise.frequency = 2.6f;
            noise.scrollSpeed = 1.4f;
            noise.octaveCount = 2;
            noise.damping = true;
            noise.quality = ParticleSystemNoiseQuality.Medium;

            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.55f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0.8f)));

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(new Color(0.85f, 1f, 1f), 0f), new GradientColorKey(new Color(0.3f, 0.85f, 1f), 0.3f), new GradientColorKey(new Color(0.08f, 0.35f, 1f), 0.75f), new GradientColorKey(new Color(0.03f, 0.12f, 0.7f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.42f, 0.1f), new GradientAlphaKey(0.26f, 0.55f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);

            var rend = go.GetComponent<ParticleSystemRenderer>();
            rend.sharedMaterial = mat;
            rend.renderMode = ParticleSystemRenderMode.Stretch;
            rend.lengthScale = 2.4f;
            rend.velocityScale = 0.1f;
            rend.cameraVelocityScale = 0f;
            rend.shadowCastingMode = ShadowCastingMode.Off;
            rend.receiveShadows = false;
            rend.sortingFudge = -5f;
        }

        class Cap
        {
            public Scene scene;
            public GameObject root, body, camA, camB;
            public Animator anim;
            public AnimationClip run, idle;
            public ParticleSystem[] ps;
            public GSEyeFlameFlow flow;
            public RenderTexture rtA, rtB;
            public Texture2D tex;
            public int frame, total;
            public string outDir;
            public Vector3 pos;
            public float yaw;
        }
        static Cap cap;
        static bool still;
        const int W = 640, H = 640, Fps = 30;

        static AnimationClip FindClip(string key)
        {
            foreach (var g in AssetDatabase.FindAssets("t:AnimationClip", new[] { "Assets/Game/LocoSplit/Art" }))
            {
                foreach (var c in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(g)).OfType<AnimationClip>())
                    if (!c.name.StartsWith("__preview__") && c.isHumanMotion && (c.name.Contains(key) || AssetDatabase.GUIDToAssetPath(g).Contains(key))) return c;
            }
            return null;
        }

        static Camera MakeCam(Cap c, string name, RenderTexture rt, float fov)
        {
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, c.scene);
            var cam = go.AddComponent<Camera>();
            cam.scene = c.scene;
            cam.cameraType = CameraType.Game;
            cam.enabled = false;
            cam.targetTexture = rt;
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 100f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.035f, 0.04f, 0.05f);
            cam.allowHDR = true;
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.volumeLayerMask = 1 << 31;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            return cam;
        }

        static string CaptureStart()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (!prefab) return "capture: prefab missing, run build";
            var c = new Cap();
            c.scene = EditorSceneManager.NewPreviewScene();
            c.root = (GameObject)PrefabUtility.InstantiatePrefab(prefab, c.scene);
            c.anim = c.root.GetComponentInChildren<Animator>();
            c.body = c.anim ? c.anim.gameObject : c.root;
            c.ps = c.root.GetComponentsInChildren<ParticleSystem>();
            c.flow = c.root.GetComponent<GSEyeFlameFlow>();
            c.run = FindClip("RunFwd");
            c.idle = FindClip("Idle");

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            SceneManager.MoveGameObjectToScene(ground, c.scene);
            ground.transform.localScale = new Vector3(6f, 1f, 6f);
            var gm = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            gm.SetColor("_BaseColor", new Color(0.3f, 0.31f, 0.3f));
            gm.SetFloat("_Smoothness", 0.05f);
            ground.GetComponent<Renderer>().sharedMaterial = gm;

            var lgo = new GameObject("Key");
            SceneManager.MoveGameObjectToScene(lgo, c.scene);
            var l = lgo.AddComponent<Light>();
            l.type = LightType.Directional;
            l.color = new Color(0.75f, 0.82f, 1f);
            l.intensity = 3.2f;
            l.shadows = LightShadows.Soft;
            lgo.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
            var fgo = new GameObject("Fill");
            SceneManager.MoveGameObjectToScene(fgo, c.scene);
            var f = fgo.AddComponent<Light>();
            f.type = LightType.Directional;
            f.color = new Color(0.5f, 0.55f, 0.7f);
            f.intensity = 1.2f;
            f.shadows = LightShadows.None;
            fgo.transform.rotation = Quaternion.Euler(20f, 150f, 0f);

            var vgo = new GameObject("Vol");
            SceneManager.MoveGameObjectToScene(vgo, c.scene);
            vgo.layer = 31;
            var vol = vgo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 100f;
            var prof = ScriptableObject.CreateInstance<VolumeProfile>();
            var bloom = prof.Add<Bloom>(true);
            bloom.intensity.Override(1.1f);
            bloom.threshold.Override(1.0f);
            bloom.scatter.Override(0.6f);
            var tone = prof.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.Neutral);
            vol.sharedProfile = prof;

            c.rtA = new RenderTexture(W, H, 24, RenderTextureFormat.DefaultHDR);
            c.rtB = new RenderTexture(W, H, 24, RenderTextureFormat.DefaultHDR);
            c.camA = MakeCam(c, "CamClose", c.rtA, 24f).gameObject;
            c.camB = MakeCam(c, "CamGame", c.rtB, 30f).gameObject;
            c.tex = new Texture2D(W * 2, H, TextureFormat.RGB24, false);
            c.total = still ? 70 : Fps * 9;
            c.outDir = Path.Combine(Dir, "frames");
            if (Directory.Exists(c.outDir)) foreach (var p in Directory.GetFiles(c.outDir, "*.png")) File.Delete(p);
            Directory.CreateDirectory(c.outDir);
            foreach (var p in c.ps) { p.Simulate(0f, false, true, false); }
            cap = c;
            return "capture: started, run=" + (c.run ? c.run.name : "none") + " idle=" + (c.idle ? c.idle.name : "none") + " ps=" + c.ps.Length + " human=" + (c.anim && c.anim.isHuman);
        }

        static void Motion(Cap c, float t, float dt)
        {
            float speed, turn;
            if (still) { c.root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); return; }
            if (t < 1.5f) { speed = 0f; turn = 0f; }
            else if (t < 4.5f) { speed = 4.5f; turn = 105f; }
            else if (t < 5.3f) { speed = 0f; turn = 0f; }
            else if (t < 5.6f) { speed = 0f; turn = 600f; }
            else if (t < 7.6f) { speed = 5.5f; turn = 0f; }
            else { speed = 0f; turn = 0f; }
            c.yaw += turn * dt;
            var rot = Quaternion.Euler(0f, c.yaw, 0f);
            c.pos += rot * Vector3.forward * speed * dt;
            var clip = speed > 0.1f ? c.run : c.idle;
            if (clip && c.anim && c.anim.isHuman)
            {
                clip.SampleAnimation(c.body, Mathf.Repeat(t, clip.length));
                c.body.transform.localPosition = Vector3.zero;
                c.body.transform.localRotation = Quaternion.identity;
            }
            c.root.transform.SetPositionAndRotation(c.pos, rot);
        }

        static void CaptureStep()
        {
            var c = cap;
            try
            {
                float dt = 1f / Fps;
                for (int k = 0; k < 6 && c.frame < c.total; k++, c.frame++)
                {
                    float t = c.frame * dt;
                    Motion(c, t, dt);
                    foreach (var p in c.ps) p.Simulate(dt, false, false, false);
                    if (c.flow) c.flow.Step(dt);
                    if (c.frame == 0 || c.frame == 60)
                    {
                        var rt = c.root.transform;
                        string d = "diag f" + c.frame;
                        foreach (var tr in c.root.GetComponentsInChildren<Transform>())
                            if (tr.name == "GS_EyeL" || tr.name == "mixamorig:Head" || tr.name == "mixamorig:Hips" || tr.name == "mixamorig:RightHand" || tr.name == "mixamorig:LeftHand" || tr.name == "mixamorig:RightToeBase" || tr.name == "GS_Sword")
                                d += " | " + tr.name + " " + rt.InverseTransformPoint(tr.position).ToString("F2") + (tr.name == "GS_Sword" ? " up " + rt.InverseTransformDirection(tr.up).ToString("F2") + " fwd " + rt.InverseTransformDirection(tr.forward).ToString("F2") + " right " + rt.InverseTransformDirection(tr.right).ToString("F2") : "");
                        Note(d);
                    }
                    var hb = c.anim && c.anim.isHuman ? c.anim.GetBoneTransform(HumanBodyBones.Head) : null;
                    var head = hb ? hb.position + Vector3.up * 0.08f : c.pos + Vector3.up * 1.62f;
                    var fwd = Quaternion.Euler(0f, c.yaw, 0f) * Vector3.forward;
                    var side = Quaternion.Euler(0f, c.yaw + 35f, 0f) * Vector3.forward;
                    if (t >= 1.5f) side = (new Vector3(0.45f, 0f, -1f)).normalized;
                    c.camA.transform.position = head + side * 2.3f + Vector3.up * 0.45f;
                    c.camA.transform.LookAt(head + Vector3.up * 0.05f);
                    var mid = c.pos + Vector3.up * 1.0f;
                    c.camB.transform.position = mid + new Vector3(0f, 5.0f, -3.7f);
                    c.camB.transform.LookAt(mid);
                    if (still)
                    {
                        var hp = new Vector3(0f, 1.62f, 0f);
                        c.camA.transform.position = hp + new Vector3(0.5f, 0.1f, 1.9f);
                        c.camA.transform.LookAt(hp);
                        c.camB.transform.position = hp + new Vector3(1.9f, 0.1f, 0f);
                        c.camB.transform.LookAt(hp);
                    }
                    bool fog = RenderSettings.fog;
                    Unsupported.SetRenderSettingsUseFogNoDirty(false);
                    c.camA.GetComponent<Camera>().Render();
                    c.camB.GetComponent<Camera>().Render();
                    Unsupported.SetRenderSettingsUseFogNoDirty(fog);
                    RenderTexture.active = c.rtA;
                    c.tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                    RenderTexture.active = c.rtB;
                    c.tex.ReadPixels(new Rect(0, 0, W, H), W, 0);
                    RenderTexture.active = null;
                    c.tex.Apply();
                    File.WriteAllBytes(Path.Combine(c.outDir, "f_" + c.frame.ToString("D4") + ".png"), c.tex.EncodeToPNG());
                }
                if (c.frame >= c.total) { CaptureEnd(); Note("capture: done " + c.total + " frames"); }
            }
            catch (System.Exception e) { CaptureEnd(); Note("ERROR capture: " + e); }
        }

        static void CaptureEnd()
        {
            var c = cap;
            cap = null;
            if (c == null) return;
            RenderTexture.active = null;
            if (c.rtA) { c.camA.GetComponent<Camera>().targetTexture = null; c.rtA.Release(); Object.DestroyImmediate(c.rtA); }
            if (c.rtB) { c.camB.GetComponent<Camera>().targetTexture = null; c.rtB.Release(); Object.DestroyImmediate(c.rtB); }
            if (c.tex) Object.DestroyImmediate(c.tex);
            EditorSceneManager.ClosePreviewScene(c.scene);
        }
    }
}
