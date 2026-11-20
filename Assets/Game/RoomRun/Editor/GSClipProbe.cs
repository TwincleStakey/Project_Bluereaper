// 11-19 probe: samples BZ_SlashLR on the hero prefab in the editor and writes the sword pitch per quarter frame
// (hunting a one-frame sword pop at clip f17.4). rr command: call RoomRun.GSClipProbe.Run
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace RoomRun
{
    public static class GSClipProbe
    {
        public static Quaternion SeatProbe = Quaternion.identity;   // hand-local sword turn, set by SplitBody when it computes the seat
        public static void Run()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/GSHero/Generated/RR_GSHeroPlayer.prefab");
            var clip = AssetDatabase.LoadAllAssetsAtPath("Assets/Game/GSHero/Art/GS_CarryPoses.fbx").OfType<AnimationClip>().FirstOrDefault(c => c.name == "BZ_SlashLR");
            var sb = new StringBuilder();
            if (!prefab || !clip) { System.IO.File.WriteAllText("ArtSource/GSHero/clip_probe.txt", "missing prefab or clip"); return; }
            var go = Object.Instantiate(prefab); go.hideFlags = HideFlags.HideAndDontSave;
            var anim = go.GetComponentInChildren<Animator>();
            var hand = anim.GetBoneTransform(HumanBodyBones.RightHand);
            var fore = anim.GetBoneTransform(HumanBodyBones.RightLowerArm);
            var sword = hand.Find("Greatsword");
            sb.AppendLine("len " + clip.length.ToString(CultureInfo.InvariantCulture) + " fps " + clip.frameRate + " sword " + (sword ? "ok" : "none"));
            if (sword && anim.avatar)
                foreach (var sk in anim.avatar.humanDescription.skeleton)
                {
                    if (sk.name != hand.name) continue;
                    var hh = (Quaternion.Inverse(sk.rotation) * sk.position).normalized; var bb = (sword.localRotation * Vector3.forward).normalized;
                    SeatProbe = Quaternion.AngleAxis(SplitBody.SeatTurn, Vector3.Cross(bb, hh).normalized);
                    break;
                }
            foreach (var cn in new[] { "BZ_SlashLR", "BZ_SlashRL", "BZ_Slash3" })
            {
                var cl = AssetDatabase.LoadAllAssetsAtPath("Assets/Game/GSHero/Art/GS_CarryPoses.fbx").OfType<AnimationClip>().FirstOrDefault(c => c.name == cn);
                if (!cl) continue;
                sb.AppendLine("== " + cn);
                // the seat fix is applied the way SplitBody does it, so the pitch is the in-game blade pitch (no roll)
                Vector3 prev = Vector3.zero; int jumps = 0;
                for (float f = 0f; f <= cl.length * 30f + 0.01f; f += 0.25f)
                {
                    cl.SampleAnimation(anim.gameObject, f / 30f);
                    var d = sword ? (hand.rotation * SeatProbe * Quaternion.Inverse(hand.rotation)) * sword.forward : hand.up;
                    float step = prev == Vector3.zero ? 0f : Vector3.Angle(prev, d); prev = d;
                    bool whole = Mathf.Abs(f - Mathf.Round(f)) < 0.01f;
                    if (step > 25f) jumps++;
                    if (whole || step > 25f) sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "f {0:0.00} pitch {1:0.0} yaw {2:0.0} step {3:0}{4}", f, Mathf.Asin(d.y) * Mathf.Rad2Deg, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, step, step > 25f ? "  <-- JUMP" : ""));
                }
                sb.AppendLine("JUMPS " + cn + " " + jumps);
                foreach (var bnd in AnimationUtility.GetCurveBindings(cl))
                {
                    if (!(bnd.propertyName.StartsWith("Right Hand") || bnd.propertyName.StartsWith("Right Forearm") || bnd.propertyName.StartsWith("Right Arm") || bnd.propertyName.StartsWith("Right Shoulder") || bnd.propertyName.StartsWith("RightHand") || bnd.propertyName.StartsWith("Chest") || bnd.propertyName.StartsWith("Spine") || bnd.propertyName.StartsWith("UpperChest") || bnd.propertyName.StartsWith("Root"))) continue;
                    var c = AnimationUtility.GetEditorCurve(cl, bnd);
                    sb.Append(bnd.propertyName + ":");
                    for (float f = 0; f <= 4.01f; f += 0.5f) sb.Append(" " + c.Evaluate(f / 30f).ToString("0.00", CultureInfo.InvariantCulture));
                    sb.AppendLine();
                }
            }
            Object.DestroyImmediate(go);
            System.IO.File.WriteAllText("ArtSource/GSHero/clip_probe.txt", sb.ToString());
        }
    }
}
