// 11-19: prints the right hand path of a whip body clip on Joe (root space: x right, y up, z forward) every 1/30 s,
// to time the blade to the clip's two hits. rr_queue: call RoomRun.WhipClipProbe.Torch03
using UnityEditor;
using UnityEngine;

namespace RoomRun
{
    public static class WhipClipProbe
    {
        public static void Torch03() => Probe("WH_Torch03");
        static void Probe(string n)
        {
            var joe = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/RoomRun/ThirdParty/MixamoJoe/Joe.fbx");
            AnimationClip clip = null;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath("Assets/Game/LocoSplit/Art/MixamoRR/" + n + ".fbx")) if (o is AnimationClip c && !c.name.StartsWith("__preview")) clip = c;
            if (!joe || !clip) { Debug.LogError("[WhipClipProbe] missing"); return; }
            var go = Object.Instantiate(joe); go.hideFlags = HideFlags.HideAndDontSave;
            var an = go.GetComponent<Animator>();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[WhipClipProbe] {n} len {clip.length:0.000}");
            var hand = an.GetBoneTransform(HumanBodyBones.RightHand); var hips = an.GetBoneTransform(HumanBodyBones.Hips);
            var chest = an.GetBoneTransform(HumanBodyBones.Chest);
            Vector3 prev = Vector3.zero;
            for (float t = 0f; t <= clip.length + 1e-4f; t += 1f / 30f)
            {
                clip.SampleAnimation(go, t);
                var h = go.transform.InverseTransformPoint(hand.position);
                var hp = go.transform.InverseTransformPoint(hips.position);
                float sp = (h - prev).magnitude * 30f; prev = h;
                float yaw = chest ? Vector3.SignedAngle(Vector3.forward, go.transform.InverseTransformDirection(chest.forward), Vector3.up) : 0f;
                sb.AppendLine($"t {t:0.000} hand {h.x:0.00} {h.y:0.00} {h.z:0.00} spd {sp:0.0} hips {hp.x:0.00} {hp.y:0.00} {hp.z:0.00} chestYaw {yaw:0}");
            }
            Object.DestroyImmediate(go);
            System.IO.File.WriteAllText(System.IO.Path.Combine(Application.dataPath, "../ArtSource/Whip/probe_" + n + ".txt"), sb.ToString());
            Debug.Log("[WhipClipProbe] wrote probe_" + n);
        }
    }
}
