// Builds Assets/Game/Hub/Scarecrow/Scarecrow.prefab (거점 허수아비 training dummy, 11-20) from Models/Scarecrow.fbx.
// The FBX comes from ArtSource/Scarecrow/scarecrow_rig.py (Tripo GLB, 1.8 m, bones Root/Legs/Spine/Chest/Head/ArmL/ArmR/TalL/TalR).
// Pivot = between the feet on the ground, facing +Z. HubBuilder places it on Scarecrow_Slot.
// Run (no menu item, project rule): rr_queue "call Hub.ScarecrowSetup.Build". Log: ArtSource/Scarecrow/setup_log.txt
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using RoomRun;

namespace Hub
{
    public static class ScarecrowSetup
    {
        const string Dir = "Assets/Game/Hub/Scarecrow";
        const string Fbx = Dir + "/Models/Scarecrow.fbx";
        const string Tex = Dir + "/Textures/Scarecrow_Base.jpg";
        public const string Prefab = Dir + "/Scarecrow.prefab";
        public const float Height = 2.85f;   // 11-20: match the player (~2.85 m); the FBX is 1.8 m
        const float S = Height / 1.8f;
        public static float FaceYaw = 180f;   // capture 11-20: the mask faced -Z after import
        // 11-20 Kido: hung on a broken weathered wooden cross like the hub concept (ArtSource/Scarecrow/scarecrow_cross.py)
        const string CrossFbx = Dir + "/Models/ScarecrowCross.fbx";
        const string PlankA = "Assets/Game/AbandonedVillage/ThirdParty/Megascans/Planks/AV_MS_Planks_A.jpg";
        const string PlankN = "Assets/Game/AbandonedVillage/ThirdParty/Megascans/Planks/AV_MS_Planks_N.jpg";
        public const float Lift = 0.6f;      // feet above the ground (must match LIFT in scarecrow_cross.py)
        const string Flash = "Assets/Game/RoomRun/Materials/RR_HitFlash.mat";
        static readonly List<string> log = new List<string>();
        static void Log(string s) { log.Add(s); Debug.Log("[Scarecrow] " + s); }

        public static void Build()
        {
            log.Clear();
            try { BuildInner(); }
            catch (System.Exception e) { Log("ERROR " + e); }
            File.WriteAllLines(Path.Combine(Directory.GetParent(Application.dataPath).FullName, "ArtSource/Scarecrow/setup_log.txt"), log);
        }

        static void BuildInner()
        {
            var imp = (ModelImporter)AssetImporter.GetAtPath(Fbx);
            imp.animationType = ModelImporterAnimationType.Generic;
            imp.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
            imp.importAnimation = false;
            imp.bakeAxisConversion = true;
            imp.importBlendShapes = false; imp.importCameras = false; imp.importLights = false;
            imp.materialImportMode = ModelImporterMaterialImportMode.None;
            imp.optimizeGameObjects = false;
            imp.SaveAndReimport();

            var ti = (TextureImporter)AssetImporter.GetAtPath(Tex);
            if (ti.maxTextureSize != 2048) { ti.maxTextureSize = 2048; ti.SaveAndReimport(); }
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(Tex);
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var matPath = Dir + "/Materials/Scarecrow_Straw.mat";
            Directory.CreateDirectory(Path.GetDirectoryName(matPath));
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (!mat) { mat = new Material(lit); AssetDatabase.CreateAsset(mat, matPath); }
            mat.shader = lit;
            mat.SetTexture("_BaseMap", tex); mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Smoothness", 0.08f); mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_Cull", 0f);   // straw cards and paper strips are single-sided
            mat.doubleSidedGI = true;
            // ghost glow: talisman glyphs pale cyan, paper + mask faint (map from ArtSource/Scarecrow/scarecrow_emission.py)
            const string EmisTex = Dir + "/Textures/Scarecrow_Emission.png";
            var eti = (TextureImporter)AssetImporter.GetAtPath(EmisTex);
            if (eti && eti.maxTextureSize != 2048) { eti.maxTextureSize = 2048; eti.SaveAndReimport(); }
            mat.SetTexture("_EmissionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(EmisTex));
            mat.SetColor("_EmissionColor", new Color(0.55f, 1f, 0.95f) * 2.2f);
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            EditorUtility.SetDirty(mat);

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
            var root = new GameObject("Scarecrow");
            // everything that wobbles hangs under Sway, which pivots at the foot of the post
            var sway = new GameObject("Sway").transform; sway.SetParent(root.transform, false);
            var crossImp = (ModelImporter)AssetImporter.GetAtPath(CrossFbx);
            crossImp.bakeAxisConversion = true; crossImp.importAnimation = false; crossImp.animationType = ModelImporterAnimationType.None;
            crossImp.materialImportMode = ModelImporterMaterialImportMode.None; crossImp.importCameras = false; crossImp.importLights = false;
            crossImp.SaveAndReimport();
            var cross = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(CrossFbx));
            cross.name = "Cross"; cross.transform.SetParent(sway, false); cross.transform.localRotation = Quaternion.Euler(0, FaceYaw, 0);
            var wood = Mat("Cross_Wood", lit, m =>
            {
                m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(PlankA)); m.SetColor("_BaseColor", new Color(0.78f, 0.74f, 0.70f));
                var nrm = AssetDatabase.LoadAssetAtPath<Texture2D>(PlankN);
                if (nrm) { m.SetTexture("_BumpMap", nrm); m.SetFloat("_BumpScale", 1.4f); m.EnableKeyword("_NORMALMAP"); }
                m.SetFloat("_Smoothness", 0.06f); m.SetFloat("_Metallic", 0f);
            });
            var rope = Mat("Cross_Rope", lit, m => { m.SetTexture("_BaseMap", tex); m.SetColor("_BaseColor", new Color(0.62f, 0.52f, 0.40f)); m.SetFloat("_Smoothness", 0.05f); });
            foreach (var r in cross.GetComponentsInChildren<Renderer>(true))
            {
                r.sharedMaterial = r.name.Contains("Lash") ? rope : wood;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
            // pivot = foot of the post (where it enters the ground)
            var post = cross.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r => r.name.Contains("Post"));
            Vector3 foot = post ? new Vector3(post.bounds.center.x, 0, post.bounds.min.z + (post.bounds.size.z) * 0.5f) : Vector3.zero;
            if (post) foot.z = post.bounds.center.z;
            sway.localPosition = foot; cross.transform.localPosition = -foot;
            Log($"post foot {foot}");
            // (11-20: a straw heap at the foot was tried with the Megascans straw/dry-grass cards; it did not read, left out)
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            inst.name = "Model";
            inst.transform.SetParent(sway, false);
            Transform Find(string n) => inst.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == n);
            // facing: the mask (head front) must be toward +Z. The face sits in front of the head bone; check with mesh bounds of the head band.
            var smr = inst.GetComponentInChildren<SkinnedMeshRenderer>();
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
            {
                r.sharedMaterials = Enumerable.Repeat(mat, Mathf.Max(1, r.sharedMaterials.Length)).ToArray();
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
            var b = smr.bounds;
            Log($"bounds {b.min} .. {b.max}");
            // Blender front is -Y (checked in ArtSource/Scarecrow/prev_front_tex.png); FaceYaw fixes it if the import turns it around
            inst.transform.localRotation = Quaternion.Euler(0, FaceYaw, 0);
            inst.transform.localScale = Vector3.one * S;
            inst.transform.localPosition = new Vector3(-foot.x, Lift, -foot.z);
            Log($"scaled height {smr.bounds.size.y:F2} m");
            smr.updateWhenOffscreen = false;
            smr.localBounds = new Bounds(smr.localBounds.center, smr.localBounds.size * 1.4f);   // leaning goes past the bind-pose box

            var sc = root.AddComponent<TrainingScarecrow>();
            sc.legs = Find("Legs"); sc.spine = Find("Spine"); sc.chest = Find("Chest"); sc.head = Find("Head");
            sc.armL = Find("ArmL"); sc.armR = Find("ArmR"); sc.talL = Find("TalL"); sc.talR = Find("TalR");
            sc.renderers = inst.GetComponentsInChildren<Renderer>(true);   // only the straw body flashes
            sc.sway = sway;
            sc.armFlap = 7f;          // tied to the beam: small flap only
            sc.kick = 18f;
            // dim cold point light in front of the chest, rocks with the cross, no shadows (hub light budget)
            var lg = new GameObject("GhostLight"); lg.transform.SetParent(sway, false);
            lg.transform.localPosition = new Vector3(-foot.x, Lift + 0.35f * Height, -foot.z + 0.8f);
            var gl = lg.AddComponent<Light>(); gl.type = LightType.Point; gl.color = new Color(0.62f, 0.92f, 1f);
            gl.range = 4.0f; gl.intensity = 1.0f; gl.shadows = LightShadows.None; gl.renderMode = LightRenderMode.ForcePixel;
            sc.glowLight = gl; sc.lightIntensity = 1.0f;            // a loose post rocks less than a free-standing dummy
            sc.hitFlash = AssetDatabase.LoadAssetAtPath<Material>(Flash);
            Log($"bones legs {(bool)sc.legs} spine {(bool)sc.spine} chest {(bool)sc.chest} head {(bool)sc.head} arms {(bool)sc.armL}/{(bool)sc.armR} tal {(bool)sc.talL}/{(bool)sc.talR} flash {(bool)sc.hitFlash}");
            root.AddComponent<Health>();
            root.layer = Ground.ActorLayer;
            // hit volume (actor layer, what swords and bullets look for)
            var cap = root.AddComponent<CapsuleCollider>();
            cap.center = new Vector3(0, Lift + 0.5f * Height, 0); cap.height = Height + 0.4f; cap.radius = 0.42f * S;
            // body block on the default layer so the player walks around it
            var blk = new GameObject("Block"); blk.transform.SetParent(root.transform, false);
            var bc = blk.AddComponent<CapsuleCollider>(); bc.center = new Vector3(0, 0.5f * (Lift + Height), foot.z * 0.5f); bc.height = Lift + Height; bc.radius = 0.45f;

            Directory.CreateDirectory(Dir);
            PrefabUtility.SaveAsPrefabAsset(root, Prefab);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            Log("saved " + Prefab);
        }

        static Bounds WorldBounds(MeshFilter mf)
        {
            var b = mf.sharedMesh.bounds; var m = mf.transform.localToWorldMatrix;
            var wb = new Bounds(m.MultiplyPoint3x4(b.min), Vector3.zero);
            for (int i = 0; i < 8; i++) wb.Encapsulate(m.MultiplyPoint3x4(new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z)));
            return wb;
        }
        static Material Mat(string name, Shader sh, System.Action<Material> set)
        {
            var path = $"{Dir}/Materials/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            m.shader = sh; set(m); EditorUtility.SetDirty(m);
            return m;
        }
    }
}
