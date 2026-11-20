// Builds Assets/Game/Hub/GoblinSmith/GoblinSmith.prefab from the Mixamo files in GoblinSmith/Models (11-20).
// Run via rr_queue: "call Hub.GoblinSmithSetup.Build"  (no menu items: Kido rule). Log: ArtSource/Hub/goblin_setup_log.txt
//   Models/Goblin_Strike.fbx  (with skin, Mixamo auto-rig of the Tripo goblin; avatar source)
//   Models/Goblin_{Idle,Talk,Wipe,Stretch}.fbx (without skin)
// Steps: humanoid import (avatar copied from the strike file), loop flags, AnimatorController (states Strike/Idle/Talk/
// Wipe/Stretch), URP material from Textures/Goblin_Base.jpg, impact time measured on the strike clip (right hand lowest),
// hammer (hub's HB_Hammer) in the right hand, sparks at the anvil point, no anvil child (the hub places its own).
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Hub
{
    public static class GoblinSmithSetup
    {
        const string Dir = "Assets/Game/Hub/GoblinSmith";
        const string Models = Dir + "/Models";
        const string PrefabPath = Dir + "/GoblinSmith.prefab";
        const string CtrlPath = Dir + "/GoblinSmith.controller";
        const string MatPath = Dir + "/Goblin.mat";
        const string TexPath = Dir + "/Textures/Goblin_Base.jpg";
        const string HammerFbx = Models + "/Goblin_Hammer.fbx";   // Sketchfab "Lowpoly Smithing Hammer" by pixelgrapher, CC BY
        const string AnvilFbx = "Assets/Game/Hub/ThirdParty/HB_Anvil/HB_Anvil.fbx";
        // hub (11-20 v4): goblin 2.1 m (root scale), anvil top 0.98 m, anvil centre 1.0 m in front. Values below are prefab-local.
        public const float Scale = 2.4f / 1.35f;   // 11-20 v5: "조금 키우고"
        public static float AnvilTop = 0.98f / Scale, AnvilDist = 0.95f / Scale;
        // hammer seat in the right hand (hand-local), tuned by captures
        public static Vector3 HammerPos = new Vector3(0f, 0.06f, 0.02f);
        public static Vector3 HammerEuler = new Vector3(0f, 0f, 0f);
        public static float HammerLen = 0.36f;

        static readonly string[] Clips = { "Strike", "Idle", "Talk", "Wipe", "Stretch", "ChopSS", "ChopGS", "ChopBash", "ChopClub" };
        public static string ChopName = "Strike";   // Kido 11-20: the original one-hand top-down axe blow, unchanged; the anvil goes where it lands
        // clips downloaded on another Mixamo character (own skeleton): avatar from their own file
        static bool OwnAvatar(string n) => n.StartsWith("Chop");
        static StreamWriter log;
        static void Log(string s) { Debug.Log("[GoblinSmith] " + s); log?.WriteLine(s); log?.Flush(); }

        public static void Build()
        {
            var lp = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "ArtSource", "Hub", "goblin_setup_log.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(lp));
            using (log = new StreamWriter(lp, false))
            {
                try { Run(); } catch (System.Exception e) { Log("ERROR " + e); }
            }
            log = null;
        }

        static string ClipFbx(string n) => $"{Models}/Goblin_{n}.fbx";

        static void Run()
        {
            // 1. import settings
            Avatar avatar = null;
            foreach (var n in Clips)
            {
                var p = ClipFbx(n);
                var mi = AssetImporter.GetAtPath(p) as ModelImporter;
                if (!mi) { Log("missing " + p); continue; }
                mi.animationType = ModelImporterAnimationType.Human;
                mi.materialImportMode = ModelImporterMaterialImportMode.None;
                if (n == "Strike") mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                else if (OwnAvatar(n)) mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                else { mi.avatarSetup = ModelImporterAvatarSetup.CopyFromOther; mi.sourceAvatar = avatar; }
                var clips = mi.defaultClipAnimations;
                foreach (var c in clips)
                {
                    c.name = "Goblin_" + n;
                    c.loopTime = n == "Idle" || n == "Talk" || n == ChopName;
                    c.lockRootRotation = true; c.lockRootHeightY = true; c.lockRootPositionXZ = true;
                    c.keepOriginalOrientation = true; c.keepOriginalPositionY = true; c.keepOriginalPositionXZ = true;
                }
                mi.clipAnimations = clips;
                mi.SaveAndReimport();
                if (n == "Strike")
                {
                    avatar = AssetDatabase.LoadAllAssetsAtPath(p).OfType<Avatar>().FirstOrDefault();
                    Log($"avatar {(avatar ? avatar.name : "null")} human={(avatar && avatar.isHuman)}");
                }
            }
            if (!avatar) { Log("no avatar, abort"); return; }

            // 2. controller
            AssetDatabase.DeleteAsset(CtrlPath);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(CtrlPath);
            var sm = ctrl.layers[0].stateMachine;
            ctrl.layers[0].iKPass = true;
            var layers = ctrl.layers; layers[0].iKPass = true; ctrl.layers = layers;
            AnimationClip strike = null, chop = null;
            foreach (var n in Clips)
            {
                var clip = AssetDatabase.LoadAllAssetsAtPath(ClipFbx(n)).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview"));
                if (!clip) { Log("no clip " + n); continue; }
                if (n == "Strike") strike = clip;   // avatar/mesh source and the hammer blow
                var st = sm.AddState(n); st.motion = clip; st.writeDefaultValues = true;
                if (n == ChopName)
                {   // the hammer blow, played as is (looping)
                    sm.defaultState = st; chop = clip;
                }
                Log($"clip {n} {clip.length:0.00}s loop={clip.isLooping}");
            }

            // 3. material
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(TexPath);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
            if (!mat) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, MatPath); }
            mat.SetTexture("_BaseMap", tex); mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Smoothness", 0.18f); mat.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(mat);

            // 4. prefab
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(ClipFbx("Strike"));
            var root = new GameObject("GoblinSmith");
            var body = (GameObject)PrefabUtility.InstantiatePrefab(src);
            body.name = "Body"; body.transform.SetParent(root.transform, false);
            // keep the FBX instance but drop its own Animator: the NPC drives one on the root
            var bodyAnim = body.GetComponent<Animator>(); if (bodyAnim) Object.DestroyImmediate(bodyAnim);
            foreach (var r in body.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                r.sharedMaterials = Enumerable.Repeat(mat, r.sharedMaterials.Length).ToArray();
                r.updateWhenOffscreen = false;
            }
            var anim = root.AddComponent<Animator>();
            anim.avatar = avatar; anim.runtimeAnimatorController = ctrl; anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            var npc = root.AddComponent<GoblinSmithNPC>();

            // hammer: own root (origin = grip, +Z = handle toward the head, +Y = striking face); the NPC lays it in the fist
            var hamSrc = AssetDatabase.LoadAssetAtPath<GameObject>(HammerFbx);
            if (hamSrc) BuildHammer(root.transform, hamSrc, npc);
            // sparks at the strike point on the anvil
            var sp = new GameObject("Sparks").transform; sp.SetParent(root.transform, false);
            sp.localPosition = new Vector3(0f, AnvilTop + 0.02f, AnvilDist);
            npc.sparks = MakeSparks(sp.gameObject);
            // impact flash: a short point light at the sparks
            var fl = sp.gameObject.AddComponent<Light>(); fl.type = LightType.Point; fl.color = new Color(1f, 0.62f, 0.25f); fl.range = 3.5f; fl.intensity = 0f; fl.shadows = LightShadows.None;
            npc.flash = fl;

            // no anvil child: the hub places its own. In edit mode (no IK) the hammer rests in the right palm, head down.
            if (npc.hammer)
            {
                var rh = anim.GetBoneTransform(HumanBodyBones.RightHand);
                var rm = anim.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
                if (rh && rm) npc.hammer.position = Vector3.Lerp(rh.position, rm.position, 0.72f);
                npc.hammer.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            }
            root.transform.localScale = Vector3.one * Scale;
            npc.chopState = ChopName;
            if (chop) { Log("USING " + ChopName); MeasureChop(root, chop, npc); }
            BuildAnvil(root.transform, npc);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            Log("scale " + Scale.ToString("0.000"));
            Log("prefab saved " + PrefabPath);
        }

        // apex = hammer head highest in 0.3-0.55; contact = first frame after the apex where the face reaches the anvil top
        static void MeasureChop(GameObject root, AnimationClip clip, GoblinSmithNPC npc)
        {
            var anim = root.GetComponent<Animator>();
            Transform rH = anim.GetBoneTransform(HumanBodyBones.RightHand), rI = anim.GetBoneTransform(HumanBodyBones.RightIndexProximal),
                      rL = anim.GetBoneTransform(HumanBodyBones.RightLittleProximal), rM = anim.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
            var ham = npc.hammer;
            Vector3 Face(float t)
            {
                AnimationMode.BeginSampling(); AnimationMode.SampleAnimationClip(root, clip, t * clip.length); AnimationMode.EndSampling();
                ham.SetPositionAndRotation(Vector3.Lerp(rH.position, rM.position, 0.72f), Quaternion.LookRotation((rI.position - rL.position).normalized, (rM.position - rH.position).normalized));
                return ham.TransformPoint(new Vector3(0, npc.headHalf, npc.gripToHead));
            }
            float top = AnvilTopWorld;
            AnimationMode.StartAnimationMode();
            try
            {
                float apex = 0.45f, best = -9f;
                for (float t = 0.05f; t <= 0.9f; t += 0.005f) { var f = Face(t); if (f.y > best) { best = f.y; apex = t; } }
                // contact = the lowest hammer-face point after the apex (the end of the downswing): the anvil top goes there
                float contact = apex, low = 9f;
                for (float t = apex; t <= Mathf.Min(1f, apex + 0.45f); t += 0.002f) { var f = Face(t); if (f.y < low) { low = f.y; contact = t; } }
                npc.chopApex = apex; npc.chopContact = contact;
                var hf = Face(contact);
                var hips = anim.GetBoneTransform(HumanBodyBones.Hips); var chest = anim.GetBoneTransform(HumanBodyBones.Chest) ?? anim.GetBoneTransform(HumanBodyBones.Spine);
                var hfw = hips.forward; var ls = anim.GetBoneTransform(HumanBodyBones.LeftUpperArm).position; var rs = anim.GetBoneTransform(HumanBodyBones.RightUpperArm).position;
                var shoulderFwd = Vector3.Cross(rs - ls, Vector3.up).normalized;
                Log($"at contact: shoulders face yaw {Mathf.Atan2(shoulderFwd.x, shoulderFwd.z) * Mathf.Rad2Deg:0} deg (sign check: +Z=0), hand {rH.position}");
                npc.anvilLocal = root.transform.InverseTransformPoint(hf);
                Log($"chop apex t={apex:0.000} (face y {best:0.00}), contact t={contact:0.000} face world {hf.x:0.00},{hf.y:0.00},{hf.z:0.00} (anvil top {top}) -> put the anvil face centre at goblin-local x {hf.x:0.00} z {hf.z:0.00} (dist {new Vector2(hf.x, hf.z).magnitude:0.00} m, {Mathf.Atan2(hf.x, hf.z) * Mathf.Rad2Deg:0} deg)");
            }
            finally { AnimationMode.StopAnimationMode(); }
        }
        public const float AnvilTopWorld = 0.98f;
        public static float AnvilWiden = 2.8f;   // (v5, unused since Kido placed the anvil)
        public static Vector3 KidoAnvilPos = new Vector3(-0.04f, 0.0013f, 0.66f), KidoAnvilScale = new Vector3(0.589f, 0.210f, 0.589f);
        public static float KidoAnvilMul = 1.5f;

        // the goblin's own anvil (Kido 11-20: anvil goes where the unchanged blow lands; bigger is fine).
        // Top centre = the hammer face's lowest point; long axis (and the blade lying on it) along the goblin's local X,
        // which is screen up-down in the hub (he faces west). Named SmithAnvil so the hub builder does not hide it.
        static void BuildAnvil(Transform root, GoblinSmithNPC npc)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Hub/ThirdParty/HB_Anvil/HB_Anvil.fbx");
            if (!src) { Log("no anvil fbx"); return; }
            var a = (GameObject)PrefabUtility.InstantiatePrefab(src); a.name = "SmithAnvil";
            a.transform.SetParent(root, false); a.transform.localPosition = Vector3.zero; a.transform.localRotation = Quaternion.identity;
            var amat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Hub/Materials/HB_Anvil_0.mat");
            if (amat) foreach (var r in a.GetComponentsInChildren<Renderer>()) r.sharedMaterials = Enumerable.Repeat(amat, r.sharedMaterials.Length).ToArray();
            foreach (var c in a.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            // local-space bounds
            Bounds LB()
            {
                var rs = a.GetComponentsInChildren<Renderer>(); var b = new Bounds(); bool first = true;
                foreach (var r in rs) { var wb = r.bounds; foreach (var s in new[] { -1, 1 }) foreach (var t in new[] { -1, 1 }) foreach (var u in new[] { -1, 1 }) { var p = root.InverseTransformPoint(wb.center + Vector3.Scale(wb.extents, new Vector3(s, t, u))); if (first) { b = new Bounds(p, Vector3.zero); first = false; } else b.Encapsulate(p); } }
                return b;
            }
            // 11-20 v6: Kido placed it himself in the Inspector (local pos -0.04, 0.0013, 0.66, rot 0, scale 0.589/0.210/0.589)
            // and asked for 1.5x: kept on the floor. Sparks/flash burst on its top centre.
            a.transform.localPosition = KidoAnvilPos; a.transform.localRotation = Quaternion.identity;
            a.transform.localScale = KidoAnvilScale * KidoAnvilMul;
            var lb = LB();
            a.transform.localPosition += new Vector3(0f, -lb.min.y, 0f);
            lb = LB();
            npc.sparkLocal = new Vector3(lb.center.x, lb.max.y + 0.01f, lb.center.z);
            // a simple box collider so the player does not walk through it
            lb = LB();
            var col = new GameObject("SmithAnvilCollider"); col.transform.SetParent(root, false);
            var bc = col.AddComponent<BoxCollider>(); bc.center = lb.center; bc.size = lb.size * 0.9f;
            Log($"SmithAnvil local top {lb.max.y:0.000} centre {lb.center} size {lb.size} (world x{Scale:0.00})");
        }

        public static float HammerTotal = 0.55f;   // overall hammer length (m): big enough to read from the game camera

        // PCA on the hammer mesh: long axis = handle, the end with the wider cross-section = head,
        // the head's own long axis = face direction. Grip at 18% of the length from the butt.
        static void BuildHammer(Transform parent, GameObject src, GoblinSmithNPC npc)
        {
            var hr = new GameObject("Hammer").transform; hr.SetParent(parent, false);
            var ham = (GameObject)PrefabUtility.InstantiatePrefab(src); ham.name = "HammerMesh";
            ham.transform.SetParent(hr, false); ham.transform.localPosition = Vector3.zero; ham.transform.localRotation = Quaternion.identity;
            foreach (var c in ham.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            var hmat = AssetDatabase.LoadAssetAtPath<Material>(Dir + "/GoblinHammer.mat");
            if (!hmat) { hmat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(hmat, Dir + "/GoblinHammer.mat"); }
            hmat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Models + "/Goblin_Hammer_albedo.png"));
            hmat.SetColor("_BaseColor", Color.white); hmat.SetFloat("_Smoothness", 0.35f); hmat.SetFloat("_Metallic", 0.2f);
            EditorUtility.SetDirty(hmat);
            if (hmat) foreach (var r in ham.GetComponentsInChildren<Renderer>()) r.sharedMaterials = Enumerable.Repeat(hmat, r.sharedMaterials.Length).ToArray();
            // vertices in hammer-root space
            var pts = new System.Collections.Generic.List<Vector3>();
            foreach (var mf in ham.GetComponentsInChildren<MeshFilter>())
            {
                var m = mf.sharedMesh; if (!m) continue;
                var tm = hr.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                foreach (var v in m.vertices) pts.Add(tm.MultiplyPoint3x4(v));
            }
            if (pts.Count < 4) { Log("hammer: no verts"); return; }
            var mean = Vector3.zero; foreach (var v in pts) mean += v; mean /= pts.Count;
            var axis = PrincipalAxis(pts, mean, Vector3.zero);
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var v in pts) { float d = Vector3.Dot(v - mean, axis); lo = Mathf.Min(lo, d); hi = Mathf.Max(hi, d); }
            float len = hi - lo;
            // cross-section spread at each end (10% slices)
            float SpreadAt(float a0, float a1)
            {
                float mx = 0; foreach (var v in pts) { float d = Vector3.Dot(v - mean, axis); if (d >= a0 && d <= a1) mx = Mathf.Max(mx, Vector3.ProjectOnPlane(v - mean, axis).magnitude); }
                return mx;
            }
            if (SpreadAt(lo, lo + 0.15f * len) > SpreadAt(hi - 0.15f * len, hi)) { axis = -axis; float t = lo; lo = -hi; hi = -t; }
            // head = last 22%: its centre and its long (face) axis
            var headPts = pts.Where(v => Vector3.Dot(v - mean, axis) > hi - 0.22f * len).ToList();
            var hc = Vector3.zero; foreach (var v in headPts) hc += v; hc /= headPts.Count;
            var face = PrincipalAxis(headPts.Select(v => Vector3.ProjectOnPlane(v - hc, axis) + hc).ToList(), hc, axis);
            float fhalf = 0; foreach (var v in headPts) fhalf = Mathf.Max(fhalf, Mathf.Abs(Vector3.Dot(v - hc, face)));
            var grip = mean + axis * (lo + 0.18f * len);
            // re-seat: mesh so that grip -> origin, axis -> +Z, face -> +Y, scaled to HammerTotal
            float k = HammerTotal / Mathf.Max(len, 1e-4f);
            var rot = Quaternion.Inverse(Quaternion.LookRotation(axis, face));
            ham.transform.localScale = ham.transform.localScale * k;
            ham.transform.localRotation = rot;
            ham.transform.localPosition = -(rot * grip) * k;
            npc.hammer = hr;
            npc.gripToHead = Vector3.Dot(hc - grip, axis) * k;
            // the head is thinner along the face axis than its long half-length; contact = across-face half thickness
            var side = Vector3.Cross(axis, face).normalized;
            float thick = 0; foreach (var v in headPts) thick = Mathf.Max(thick, Mathf.Abs(Vector3.Dot(v - hc, side)));
            npc.headHalf = fhalf * k;
            Log($"hammer len {len:0.000} -> {HammerTotal}, gripToHead {npc.gripToHead:0.000}, face half {fhalf * k:0.000}, side half {thick * k:0.000}");
        }

        static Vector3 PrincipalAxis(System.Collections.Generic.List<Vector3> pts, Vector3 mean, Vector3 exclude)
        {
            // power iteration on the covariance
            float xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
            foreach (var p in pts) { var d = p - mean; if (exclude != Vector3.zero) d = Vector3.ProjectOnPlane(d, exclude); xx += d.x * d.x; xy += d.x * d.y; xz += d.x * d.z; yy += d.y * d.y; yz += d.y * d.z; zz += d.z * d.z; }
            var v = exclude == Vector3.zero ? new Vector3(0.577f, 0.577f, 0.577f) : Vector3.ProjectOnPlane(new Vector3(0.31f, 0.77f, 0.55f), exclude).normalized;
            for (int i = 0; i < 64; i++)
            {
                var n = new Vector3(xx * v.x + xy * v.y + xz * v.z, xy * v.x + yy * v.y + yz * v.z, xz * v.x + yz * v.y + zz * v.z);
                if (exclude != Vector3.zero) n = Vector3.ProjectOnPlane(n, exclude);
                if (n.sqrMagnitude < 1e-12f) break; v = n.normalized;
            }
            return v;
        }

        // Edit-mode pose renders (no Play): ArtSource/Hub/goblin_pose/<clip>_<t>_<view>.png
        // "call Hub.GoblinSmithSetup.PoseShots"
        public static void PoseShots()
        {
            var outDir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "ArtSource", "Hub", "goblin_pose");
            Directory.CreateDirectory(outDir);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            var lightGo = new GameObject("L"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lightGo, scene);
            var li = lightGo.AddComponent<Light>(); li.type = LightType.Directional; li.intensity = 1.6f; lightGo.transform.rotation = Quaternion.Euler(45, 150, 0);
            var camGo = new GameObject("C"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(camGo, scene);
            var cam = camGo.AddComponent<Camera>(); cam.scene = scene; cam.fieldOfView = 30; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.35f, 0.37f, 0.4f);
            var rt = new RenderTexture(640, 640, 24); cam.targetTexture = rt;
            var npc = go.GetComponent<GoblinSmithNPC>();
            // reference anvil where the hub has it (0.95 m ahead, top 0.98)
            var anvSrc = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Hub/ThirdParty/HB_Anvil/HB_Anvil.fbx");
            if (anvSrc)
            {
                var an = (GameObject)PrefabUtility.InstantiatePrefab(anvSrc, scene);
                var cpw = go.transform.TransformPoint(npc.anvilLocal);
                an.transform.position = new Vector3(cpw.x, 0, cpw.z); an.transform.rotation = Quaternion.Euler(0, 0, 0);
                var ab = Bounds(an); an.transform.localScale *= cpw.y / Mathf.Max(ab.size.y, 1e-4f); ab = Bounds(an); an.transform.position += Vector3.up * -ab.min.y;
                ab = Bounds(an); an.transform.position += new Vector3(cpw.x - ab.center.x, 0, cpw.z - ab.center.z);
                var amat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Hub/Materials/HB_Anvil_0.mat");
                if (amat) foreach (var r in an.GetComponentsInChildren<Renderer>()) r.sharedMaterial = amat;
            }
            var anim = go.GetComponent<Animator>();
            var rH = anim.GetBoneTransform(HumanBodyBones.RightHand); var rI = anim.GetBoneTransform(HumanBodyBones.RightIndexProximal);
            var rL = anim.GetBoneTransform(HumanBodyBones.RightLittleProximal); var rM = anim.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
            var sw = new StreamWriter(Path.Combine(outDir, "pose_log.txt"), false);
            var clipsToShow = new[] { ChopName };
            AnimationMode.StartAnimationMode();
            try
            {
                foreach (var cn in clipsToShow)
                {
                    var clip = AssetDatabase.LoadAllAssetsAtPath(ClipFbx(cn)).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview"));
                    if (!clip) continue;
                    for (int i = 0; i <= 20; i++)
                    {
                        float t = Mathf.Lerp(npc.chopApex - 0.12f, npc.chopContact + 0.04f, i / 20f);
                        AnimationMode.BeginSampling(); AnimationMode.SampleAnimationClip(go, clip, t * clip.length); AnimationMode.EndSampling();
                        if (npc.hammer && rH && rI && rL && rM)
                        {
                            var fr = Quaternion.LookRotation((rI.position - rL.position).normalized, (rM.position - rH.position).normalized);
                            npc.hammer.SetPositionAndRotation(Vector3.Lerp(rH.position, rM.position, 0.72f), fr);
                            var head = npc.hammer.TransformPoint(new Vector3(0, npc.headHalf, npc.gripToHead));
                            var hc = npc.hammer.TransformPoint(new Vector3(0, 0, npc.gripToHead));
                            sw.WriteLine($"{cn} t={t:0.00} face {head.x:0.00},{head.y:0.00},{head.z:0.00} headC {hc.y:0.00} hand {rH.position.x:0.00},{rH.position.y:0.00},{rH.position.z:0.00} root {go.transform.position} hips {anim.GetBoneTransform(HumanBodyBones.Hips).position}");
                        }
                        if (i % 2 != 0) continue;
                        foreach (var vw in new[] { "side", "game" })
                        {
                            var look = new Vector3(-0.3f, 1.0f, 0.4f);
                            // game camera: pitch 49.5, looking +Z in the hub; the goblin faces -X there (west), so look from his local +X side... use his right
                            cam.transform.position = vw == "side" ? look + new Vector3(5.5f, 0.4f, 0f) : look + Quaternion.Euler(49.5f, 90f, 0) * Vector3.back * 7f;
                            cam.transform.LookAt(look);
                            cam.Render();
                            RenderTexture.active = rt; var tex = new Texture2D(640, 640, TextureFormat.RGB24, false);
                            tex.ReadPixels(new Rect(0, 0, 640, 640), 0, 0); tex.Apply(); RenderTexture.active = null;
                            File.WriteAllBytes(Path.Combine(outDir, $"{cn}_{vw}_{i:00}.png"), tex.EncodeToPNG());
                            Object.DestroyImmediate(tex);
                        }
                    }
                }
            }            finally
            {
                AnimationMode.StopAnimationMode(); sw.Close();
                cam.targetTexture = null; Object.DestroyImmediate(rt);
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
            }
            Debug.Log("[GoblinSmith] pose shots -> " + outDir);
            File.WriteAllText(Path.Combine(outDir, "done.txt"), System.DateTime.Now.ToString("HH:mm:ss"));
        }

        static ParticleSystem MakeSparks(GameObject go)
        {
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.playOnAwake = false; main.loop = false; main.duration = 0.3f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.45f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 5.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.045f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.85f, 0.4f), new Color(1f, 0.45f, 0.1f));
            main.gravityModifier = 1.1f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 160;
            var em = ps.emission; em.rateOverTime = 0; em.SetBursts(new[] { new ParticleSystem.Burst(0f, 34, 52) });
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Hemisphere; sh.radius = 0.04f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.9f, 0.6f), 0f), new GradientColorKey(new Color(1f, 0.35f, 0.05f), 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.02f; r.lengthScale = 1.2f;
            var m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            m.SetFloat("_Surface", 1); m.SetFloat("_Blend", 2); // transparent additive
            m.SetColor("_BaseColor", new Color(2.2f, 1.4f, 0.6f, 1f));
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.EnableKeyword("_BLENDMODE_ADD");
            m.renderQueue = 3000;
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_ZWrite", 0);
            AssetDatabase.DeleteAsset(Dir + "/GoblinSparks.mat");
            AssetDatabase.CreateAsset(m, Dir + "/GoblinSparks.mat");
            r.sharedMaterial = m;
            return ps;
        }

        static Bounds Bounds(GameObject g)
        {
            var rs = g.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(g.transform.position, Vector3.zero);
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b;
        }
    }
}
