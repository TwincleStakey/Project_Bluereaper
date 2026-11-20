// 11-20 Kido's guard pose (Cascadeur sample Cascy_sword, exported by Kido as cascy_guard.fbx) -> Joe (Mixamo suit body).
// Retarget by bone directions, done here in Unity so no axis conversion is guessed:
//   for every mapped bone, Joe's world rotation = Frame(Cascy posed dir, Cascy posed secondary) * inverse(Frame(Joe rest dir,
//   Joe rest secondary)) * Joe rest rotation, where the secondary axis is the body's facing (or up, for bones that point forward,
//   like the thumbs) carried along by Cascy's own rest->pose rotation. Parents first, so children inherit.
// The Cascy instance is first turned so its rest facing is +Z: "north" (cursor up) = the scene's forward in Cascadeur.
// Output: Assets/Game/RoomRun/Scripts/GuardPoseData.cs (Joe local rotations of the upper body, the spine rotation relative to
// Joe's root, and the sword axis in that frame). Run: rr_queue "call RoomRun.GuardPoseBake.Bake".
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace RoomRun
{
    public static class GuardPoseBake
    {
        const string CascyPath = "Assets/Game/RoomRun/Art/Cascadeur/cascy_guard.fbx";
        const string JoePath = "Assets/Game/RoomRun/ThirdParty/MixamoJoe/Joe.fbx";
        const string OutPath = "Assets/Game/RoomRun/Scripts/GuardPoseData.cs";

        struct Map { public HumanBodyBones joe; public HumanBodyBones joeChild; public string from, to; public Map(HumanBodyBones j, HumanBodyBones jc, string f, string t) { joe = j; joeChild = jc; from = f; to = t; } }

        static List<Map> Maps()
        {
            var m = new List<Map>
            {
                new Map(HumanBodyBones.Hips, HumanBodyBones.Spine, "pelvis", "stomach"),
                new Map(HumanBodyBones.Spine, HumanBodyBones.Chest, "stomach", "chest"),
                new Map(HumanBodyBones.Chest, HumanBodyBones.UpperChest, "chest", "neck"),
                new Map(HumanBodyBones.UpperChest, HumanBodyBones.Neck, "chest", "neck"),
                new Map(HumanBodyBones.Neck, HumanBodyBones.Head, "neck", "head"),
                new Map(HumanBodyBones.Head, HumanBodyBones.LastBone, "neck", "head"),   // head: same line as the neck, turned by its own rotation
            };
            foreach (var side in new[] { "r", "l" })
            {
                bool r = side == "r";
                HumanBodyBones B(string n) => (HumanBodyBones)System.Enum.Parse(typeof(HumanBodyBones), (r ? "Right" : "Left") + n);
                m.Add(new Map(B("Shoulder"), B("UpperArm"), "clavicle_" + side, "arm_" + side));
                m.Add(new Map(B("UpperArm"), B("LowerArm"), "arm_" + side, "forearm_" + side));
                m.Add(new Map(B("LowerArm"), B("Hand"), "forearm_" + side, "hand_" + side));
                m.Add(new Map(B("Hand"), B("MiddleProximal"), "hand_" + side, "f_middle1_" + side));
                foreach (var (jf, cf) in new[] { ("Thumb", "thumb"), ("Index", "index"), ("Middle", "middle"), ("Ring", "ring"), ("Little", "pinky") })
                {
                    m.Add(new Map(B(jf + "Proximal"), B(jf + "Intermediate"), $"f_{cf}1_{side}", $"f_{cf}2_{side}"));
                    m.Add(new Map(B(jf + "Intermediate"), B(jf + "Distal"), $"f_{cf}2_{side}", $"f_{cf}3_{side}"));
                    m.Add(new Map(B(jf + "Distal"), HumanBodyBones.LastBone, $"f_{cf}3_{side}", $"f_{cf}4_{side}"));
                }
            }
            return m;
        }

        static Transform Find(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        public static void Bake()
        {
            var cascyAsset = AssetDatabase.LoadAssetAtPath<GameObject>(CascyPath);
            var clip = AssetDatabase.LoadAllAssetsAtPath(CascyPath).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
            var joeAsset = AssetDatabase.LoadAssetAtPath<GameObject>(JoePath);
            if (!cascyAsset || !clip || !joeAsset) { Debug.LogError($"[GuardPoseBake] missing: cascy {cascyAsset} clip {clip} joe {joeAsset}"); return; }
            var C = Object.Instantiate(cascyAsset); var J = Object.Instantiate(joeAsset);
            try
            {
                C.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                J.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var ja = J.GetComponent<Animator>();
                if (!ja || !ja.isHuman) { Debug.LogError("[GuardPoseBake] Joe has no humanoid Animator"); return; }
                // Cascy rest facing (feet -> toes) and right (left thigh -> right thigh), then turn it so rest facing = +Z
                Transform Cn(string n) { var t = Find(C.transform, n); if (!t) Debug.LogError("[GuardPoseBake] Cascy bone missing: " + n); return t; }
                var restFwdC = ((Cn("toe_r").position - Cn("foot_r").position) + (Cn("toe_l").position - Cn("foot_l").position)); restFwdC.y = 0f;
                C.transform.rotation = Quaternion.FromToRotation(restFwdC.normalized, Vector3.forward);
                var maps = Maps();
                var restRotC = new Dictionary<string, Quaternion>(); var restPosC = new Dictionary<string, Vector3>();
                foreach (var t in C.GetComponentsInChildren<Transform>(true)) { restRotC[t.name] = t.rotation; restPosC[t.name] = t.position; }
                // the true rest (bind) pose comes from the body mesh's bind poses: the FBX node transforms may already hold the pose
                var body = C.GetComponentsInChildren<SkinnedMeshRenderer>(true).OrderByDescending(r => r.bones.Length).FirstOrDefault();
                if (body && body.sharedMesh && body.sharedMesh.bindposes.Length == body.bones.Length)
                {
                    var bp = body.sharedMesh.bindposes; int n = 0;
                    for (int i = 0; i < bp.Length; i++)
                    {
                        if (!body.bones[i]) continue;
                        var m = body.transform.localToWorldMatrix * bp[i].inverse;
                        restRotC[body.bones[i].name] = m.rotation; restPosC[body.bones[i].name] = m.GetColumn(3); n++;
                    }
                    Debug.Log($"[GuardPoseBake] cascy rest from {n} bind poses of {body.name}; right hand rest y {restPosC["hand_r"].y:0.000}, node y {Cn("hand_r").position.y:0.000}");
                }
                else Debug.LogWarning("[GuardPoseBake] no bind poses: rest = node transforms");
                float handBefore = Cn("hand_r").position.y;
                clip.SampleAnimation(C, 0f);
                C.transform.rotation = Quaternion.FromToRotation(restFwdC.normalized, Vector3.forward);   // the clip may key the root
                float handAfter = Cn("hand_r").position.y;
                Debug.Log($"[GuardPoseBake] clip {clip.name} len {clip.length:0.000}: cascy right hand y rest {handBefore:0.000} -> pose {handAfter:0.000} (must differ)");
                // Joe rest
                var restRotJ = new Dictionary<HumanBodyBones, Quaternion>(); var restPosJ = new Dictionary<HumanBodyBones, Vector3>();
                for (var b = HumanBodyBones.Hips; b < HumanBodyBones.LastBone; b++) { var t = ja.GetBoneTransform(b); if (t) { restRotJ[b] = t.rotation; restPosJ[b] = t.position; } }
                Vector3 JoeDir(Map mp)
                {
                    var t = ja.GetBoneTransform(mp.joe);
                    if (mp.joeChild != HumanBodyBones.LastBone && restPosJ.ContainsKey(mp.joeChild)) return restPosJ[mp.joeChild] - restPosJ[mp.joe];
                    if (mp.joe == HumanBodyBones.Head) return restPosJ[HumanBodyBones.Head] - restPosJ[HumanBodyBones.Neck];
                    if (t.childCount > 0) return t.GetChild(0).position - t.position;            // distal finger -> its end bone
                    var p = t.parent; return t.position - p.position;
                }
                var applied = new List<HumanBodyBones>();
                foreach (var mp in maps)
                {
                    var jt = ja.GetBoneTransform(mp.joe);
                    if (!jt) { if (mp.joe != HumanBodyBones.UpperChest) Debug.LogWarning("[GuardPoseBake] Joe has no " + mp.joe); continue; }
                    var cf = Cn(mp.from); var ct = Cn(mp.to);
                    if (!cf || !ct) continue;
                    var dJ = JoeDir(mp).normalized;
                    var dC0 = (restPosC[ct.name] - restPosC[cf.name]).normalized;
                    var dC = (ct.position - cf.position).normalized;
                    bool useUp = Mathf.Abs(Vector3.Dot(dJ, Vector3.forward)) > 0.8f;     // bones that point forward (thumbs) twist about up
                    var sec = useUp ? Vector3.up : Vector3.forward;
                    if (Mathf.Abs(Vector3.Dot(dJ, sec)) > 0.95f) sec = Vector3.right;      // pointing straight up/down: use right
                    // Cascy's rest frame for the same anatomy: same secondary in Cascy's (turned) rest = the same world axis
                    var rC = cf.rotation * Quaternion.Inverse(restRotC[cf.name]);           // Cascy rest -> pose for this bone
                    var secC = rC * sec;
                    // rest-to-rest alignment of the bone line (A pose vs T pose), then Cascy's rest->pose
                    var target = Quaternion.LookRotation(dC, secC) * Quaternion.Inverse(Quaternion.LookRotation(dJ, sec)) * restRotJ[mp.joe];
                    jt.rotation = target;
                    applied.Add(mp.joe);
                    _ = dC0;
                }
                // output: locals for everything above the hips, the spine relative to Joe's root, and the sword axis (north frame)
                var sb = new StringBuilder();
                string Q(Quaternion q) => string.Format(CultureInfo.InvariantCulture, "new Quaternion({0:R}f, {1:R}f, {2:R}f, {3:R}f)", q.x, q.y, q.z, q.w);
                string V(Vector3 v) => string.Format(CultureInfo.InvariantCulture, "new Vector3({0:R}f, {1:R}f, {2:R}f)", v.x, v.y, v.z);
                var spine = ja.GetBoneTransform(HumanBodyBones.Spine);
                var bones = applied.Where(b => b != HumanBodyBones.Hips && b != HumanBodyBones.Spine).ToList();
                // sword (Cascy Sword_mesh, posed): long axis by PCA, pommel = the end nearer the hands, flat normal = thinnest axis
                Vector3 blade = Vector3.right, flat = Vector3.up; float gripR = 0.4f, swordLen = 1.8f;
                var smr = C.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(r => r.name.ToLower().Contains("sword"));
                if (smr)
                {
                    var baked = new Mesh(); smr.BakeMesh(baked, true);
                    var pts = baked.vertices.Select(v => smr.transform.TransformPoint(v)).ToArray();
                    var c = Vector3.zero; foreach (var p in pts) c += p; c /= pts.Length;
                    var cov = new float[3, 3];
                    foreach (var p in pts) { var d = p - c; for (int i = 0; i < 3; i++) for (int k = 0; k < 3; k++) cov[i, k] += d[i] * d[k]; }
                    Vector3 Mul(Vector3 v) => new Vector3(cov[0, 0] * v.x + cov[0, 1] * v.y + cov[0, 2] * v.z, cov[1, 0] * v.x + cov[1, 1] * v.y + cov[1, 2] * v.z, cov[2, 0] * v.x + cov[2, 1] * v.y + cov[2, 2] * v.z);
                    var e1 = new Vector3(0.6f, 0.3f, 0.7f).normalized; for (int it = 0; it < 200; it++) e1 = Mul(e1).normalized;
                    var e2 = Vector3.Cross(e1, new Vector3(0.1f, 0.9f, 0.2f)).normalized;
                    for (int it = 0; it < 200; it++) { e2 = Mul(e2); e2 -= e1 * Vector3.Dot(e2, e1); e2.Normalize(); }
                    var e3 = Vector3.Cross(e1, e2).normalized;   // thinnest
                    float lo = float.MaxValue, hi = float.MinValue; foreach (var p in pts) { float s = Vector3.Dot(p - c, e1); lo = Mathf.Min(lo, s); hi = Mathf.Max(hi, s); }
                    var hands = (Cn("hand_r").position + Cn("hand_l").position) * 0.5f;
                    float hs = Vector3.Dot(hands - c, e1);
                    if (Mathf.Abs(hs - lo) > Mathf.Abs(hs - hi)) { e1 = -e1; float t = lo; lo = -hi; hi = -t; }
                    blade = e1; flat = e3; swordLen = hi - lo;
                    gripR = Vector3.Dot(Cn("hand_r").position - (c + e1 * lo), e1) / swordLen;
                    Debug.Log($"[GuardPoseBake] cascy sword: len {swordLen:0.000} blade {blade:F3} flat {flat:F3} right hand at {gripR:0.000} of the length");
                }
                sb.AppendLine("// GENERATED by RoomRun.GuardPoseBake from Assets/Game/RoomRun/Art/Cascadeur/cascy_guard.fbx (Kido's Cascadeur guard pose).");
                sb.AppendLine("// Do not edit by hand: re-run the bake. North frame = the Cascadeur scene forward (cursor up).");
                sb.AppendLine("using UnityEngine;\nnamespace RoomRun\n{\n    public static class GuardPoseData\n    {");
                sb.AppendLine($"        public static readonly Quaternion SpineRoot = {Q(spine.rotation)};   // Joe Spine rotation relative to Joe's root");
                sb.AppendLine("        public static readonly HumanBodyBones[] Bones = {");
                foreach (var b in bones) sb.AppendLine($"            HumanBodyBones.{b},");
                sb.AppendLine("        };\n        public static readonly Quaternion[] Local = {");
                foreach (var b in bones) sb.AppendLine($"            {Q(ja.GetBoneTransform(b).localRotation)},");
                sb.AppendLine("        };");
                sb.AppendLine($"        public static readonly Vector3 Blade = {V(blade)};   // pommel -> tip, north frame");
                sb.AppendLine($"        public static readonly Vector3 Flat = {V(flat)};");
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "        public const float GripRight = {0:R}f;   // right hand along the sample sword (0 = pommel, 1 = tip)", gripR));
                // the same sword axes in Joe's posed right hand (the runtime ties the sword to the hand, not to the cursor frame)
                var jh = ja.GetBoneTransform(HumanBodyBones.RightHand);
                var inv = Quaternion.Inverse(jh.rotation);
                sb.AppendLine($"        public static readonly Vector3 BladeInHand = {V(inv * blade)};   // in Joe's RightHand space");
                sb.AppendLine($"        public static readonly Vector3 FlatInHand = {V(inv * flat)};");
                sb.AppendLine("    }\n}");
                System.IO.File.WriteAllText(OutPath, sb.ToString());
                Debug.Log($"[GuardPoseBake] wrote {OutPath}: {bones.Count} bones; Joe right hand {ja.GetBoneTransform(HumanBodyBones.RightHand).position:F3} left {ja.GetBoneTransform(HumanBodyBones.LeftHand).position:F3} hips {ja.GetBoneTransform(HumanBodyBones.Hips).position:F3}");
                AssetDatabase.ImportAsset(OutPath);
            }
            finally { Object.DestroyImmediate(C); Object.DestroyImmediate(J); }
        }
    }
}
