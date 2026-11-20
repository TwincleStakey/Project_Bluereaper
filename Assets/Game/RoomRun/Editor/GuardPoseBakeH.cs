// 11-20 Kido "리깅으로 했는데 조인트마다 왜 저렇게 된 거야": the bone-direction retarget (GuardPoseBake) guesses each bone's twist
// (A-pose Cascadeur rest vs T-pose Joe rest), so wrists/elbows/shoulders twisted. This bake uses Unity's humanoid retarget
// instead (muscle space carries the twist): cascy_guard_h.fbx (a copy of Kido's export) is imported as Humanoid, its pose clip
// is sampled on a Joe instance, and ONLY the arm/hand/finger bones are written (Kido allowed hands and arms only; body,
// head, hat and coat keep the current pose). Output: Scripts/GuardPoseArms.cs; runtime opt-in GuardCarry.HumanoidArms
// (token harm=1) until Kido approves. Run: rr_queue "call RoomRun.GuardPoseBakeH.Bake".
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace RoomRun
{
    public static class GuardPoseBakeH
    {
        const string CascyPath = "Assets/Game/RoomRun/Art/Cascadeur/cascy_guard_h.fbx";
        const string JoePath = "Assets/Game/RoomRun/ThirdParty/MixamoJoe/Joe.fbx";
        const string OutPath = "Assets/Game/RoomRun/Scripts/GuardPoseArms.cs";

        public static void Bake()
        {
            var imp = AssetImporter.GetAtPath(CascyPath) as ModelImporter;
            if (!imp) { Debug.LogError("[GuardPoseBakeH] no importer for " + CascyPath); return; }
            if (imp.animationType != ModelImporterAnimationType.Human)
            {
                imp.animationType = ModelImporterAnimationType.Human;
                imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                imp.SaveAndReimport();
            }
            var cascyAsset = AssetDatabase.LoadAssetAtPath<GameObject>(CascyPath);
            var avatar = AssetDatabase.LoadAllAssetsAtPath(CascyPath).OfType<Avatar>().FirstOrDefault();
            var clip = AssetDatabase.LoadAllAssetsAtPath(CascyPath).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
            var joeAsset = AssetDatabase.LoadAssetAtPath<GameObject>(JoePath);
            Debug.Log($"[GuardPoseBakeH] avatar {(avatar ? avatar.name : "null")} valid={(avatar && avatar.isValid)} human={(avatar && avatar.isHuman)}; clip {(clip ? clip.name : "null")} humanMotion={(clip && clip.humanMotion)}");
            if (!cascyAsset || !avatar || !avatar.isHuman || !clip || !joeAsset) { Debug.LogError("[GuardPoseBakeH] cannot bake (see line above)"); return; }
            var J = Object.Instantiate(joeAsset); var C = Object.Instantiate(cascyAsset);
            try
            {
                J.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                C.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var ja = J.GetComponent<Animator>();
                var ca = C.GetComponent<Animator>(); if (!ca) ca = C.AddComponent<Animator>();
                ca.avatar = avatar;
                clip.SampleAnimation(J, 0f);
                clip.SampleAnimation(C, 0f);
                string Q(Quaternion q) => string.Format(CultureInfo.InvariantCulture, "new Quaternion({0:R}f, {1:R}f, {2:R}f, {3:R}f)", q.x, q.y, q.z, q.w);
                string V(Vector3 v) => string.Format(CultureInfo.InvariantCulture, "new Vector3({0:R}f, {1:R}f, {2:R}f)", v.x, v.y, v.z);
                var bones = Enumerable.Range(0, (int)HumanBodyBones.LastBone).Select(i => (HumanBodyBones)i)
                    .Where(b => { var n = b.ToString(); return n == "Chest" || n == "UpperChest" || n == "Neck" || n == "Head" || n.Contains("Shoulder") || n.Contains("Arm") || n.Contains("Hand") || n.Contains("Thumb") || n.Contains("Index") || n.Contains("Middle") || n.Contains("Ring") || n.Contains("Little"); })
                    .Where(b => ja.GetBoneTransform(b)).ToList();
                // the sword axis in Joe's right hand: the Cascy sword, posed by the same humanoid clip on Cascy itself
                Vector3 blade = Vector3.zero, flat = Vector3.zero;
                var smr = C.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(r => r.name.ToLower().Contains("sword"));
                var cr = ca.GetBoneTransform(HumanBodyBones.RightHand); var cl = ca.GetBoneTransform(HumanBodyBones.LeftHand);
                if (smr && cr && cl)
                {
                    var m = new Mesh(); smr.BakeMesh(m, true);
                    var pts = m.vertices.Select(v => smr.transform.position + smr.transform.rotation * v).ToArray();
                    var c = Vector3.zero; foreach (var p in pts) c += p; c /= pts.Length;
                    var cov = new float[3, 3];
                    foreach (var p in pts) { var d = p - c; for (int i = 0; i < 3; i++) for (int k = 0; k < 3; k++) cov[i, k] += d[i] * d[k]; }
                    System.Func<Vector3, Vector3> Mul = v => new Vector3(cov[0, 0] * v.x + cov[0, 1] * v.y + cov[0, 2] * v.z, cov[1, 0] * v.x + cov[1, 1] * v.y + cov[1, 2] * v.z, cov[2, 0] * v.x + cov[2, 1] * v.y + cov[2, 2] * v.z);
                    var e1 = new Vector3(0.6f, 0.3f, 0.7f).normalized; for (int it = 0; it < 200; it++) e1 = Mul(e1).normalized;
                    var e2 = Vector3.Cross(e1, new Vector3(0.1f, 0.9f, 0.2f)).normalized;
                    for (int it = 0; it < 200; it++) { e2 = Mul(e2); e2 -= e1 * Vector3.Dot(e2, e1); e2.Normalize(); }
                    var hands = (cr.position + cl.position) * 0.5f;
                    float lo = pts.Min(p => Vector3.Dot(p - c, e1)), hi = pts.Max(p => Vector3.Dot(p - c, e1));
                    float hs = Vector3.Dot(hands - c, e1);
                    if (Mathf.Abs(hs - lo) > Mathf.Abs(hs - hi)) e1 = -e1;
                    blade = e1; flat = Vector3.Cross(e1, e2).normalized;
                    Debug.Log($"[GuardPoseBakeH] cascy sword blade {blade:F3}; Joe hands R {ja.GetBoneTransform(HumanBodyBones.RightHand).position:F3} L {ja.GetBoneTransform(HumanBodyBones.LeftHand).position:F3}; cascy hands R {cr.position:F3} L {cl.position:F3}");
                }
                var inv = Quaternion.Inverse(ja.GetBoneTransform(HumanBodyBones.RightHand).rotation);
                // v2 (Kido "원래 cascadeur 한 거 그대로"): the whole upper body as retargeted. The spine is stored relative to Joe's
                // root, turned about up so its heading equals the old bake's (GuardPoseData.SpineRoot), so north (PoseYawOffset) holds
                var jSpine = ja.GetBoneTransform(HumanBodyBones.Spine);
                var tmpJ = Object.Instantiate(joeAsset); tmpJ.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var restSpine = tmpJ.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Spine).rotation; Object.DestroyImmediate(tmpJ);
                var fLocal = Quaternion.Inverse(restSpine) * Vector3.forward;
                Vector3 Flat(Vector3 v) { v.y = 0f; return v.normalized; }
                float dYaw = Vector3.SignedAngle(Flat(jSpine.rotation * fLocal), Flat(GuardPoseData.SpineRoot * fLocal), Vector3.up);
                var spineRoot = Quaternion.AngleAxis(dYaw, Vector3.up) * jSpine.rotation;
                Debug.Log($"[GuardPoseBakeH] spine heading matched to the old bake: turned {dYaw:0.0} deg");
                var sb = new StringBuilder();
                sb.AppendLine("// GENERATED by RoomRun.GuardPoseBakeH (humanoid retarget of Kido's Cascadeur guard pose, arms/hands only). Re-run the bake; do not edit.");
                sb.AppendLine("using UnityEngine;\nnamespace RoomRun\n{\n    public static class GuardPoseArms\n    {\n        public static readonly HumanBodyBones[] Bones = {");
                foreach (var b in bones) sb.AppendLine($"            HumanBodyBones.{b},");
                sb.AppendLine("        };\n        public static readonly Quaternion[] Local = {");
                foreach (var b in bones) sb.AppendLine($"            {Q(ja.GetBoneTransform(b).localRotation)},");
                sb.AppendLine("        };");
                sb.AppendLine($"        public static readonly Vector3 BladeInHand = {V(blade.sqrMagnitude > 0.5f ? inv * blade : Vector3.zero)};");
                sb.AppendLine($"        public static readonly Quaternion SpineRoot = {Q(spineRoot)};");
                sb.AppendLine($"        public static readonly Vector3 FlatInHand = {V(flat.sqrMagnitude > 0.5f ? inv * flat : Vector3.zero)};");
                sb.AppendLine("    }\n}");
                System.IO.File.WriteAllText(OutPath, sb.ToString());
                Debug.Log($"[GuardPoseBakeH] wrote {OutPath}: {bones.Count} arm/hand bones");
                AssetDatabase.ImportAsset(OutPath);
            }
            finally { Object.DestroyImmediate(J); Object.DestroyImmediate(C); }
        }
    }
}
