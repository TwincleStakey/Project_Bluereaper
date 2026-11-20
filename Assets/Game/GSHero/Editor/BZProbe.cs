// 11-16 wide sweep check: samples the berserker attack clips (humanoid, retargeted) on the built RR_GSHeroPlayer in edit
// mode and logs, per clip frame, the blade tip bearing / radius / height from the model root and the arm twist, to
// ArtSource/GSHero/ingame_shots/wide_sweep/bzprobe.csv. Compare with the Blender bake log (wide_sweep_blade_log.csv) to see
// what the humanoid retarget changes (upper-arm / forearm twist limits rotate the blade about the arm).
// ls_cmd `bzprobe`. Bearing: 0 = the model's forward, + = toward the model's right.
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class BZProbe
{
    static float ChestYaw(Animator a, Transform root)
    {
        var l = a.GetBoneTransform(HumanBodyBones.LeftUpperArm); var r = a.GetBoneTransform(HumanBodyBones.RightUpperArm);
        var c = root.InverseTransformDirection(Vector3.Cross(r.position - l.position, Vector3.up));
        return Mathf.Atan2(c.x, c.z) * Mathf.Rad2Deg;
    }

    public static string Run()
    {
        const string prefabPath = "Assets/Game/GSHero/Generated/RR_GSHeroPlayer.prefab";
        const string fbx = "Assets/Game/GSHero/Art/GS_BerserkerAnims.fbx";
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (!prefab) return "bzprobe: no " + prefabPath;
        var go = (GameObject)Object.Instantiate(prefab);
        go.hideFlags = HideFlags.HideAndDontSave;
        go.transform.SetPositionAndRotation(new Vector3(0f, -3000f, 0f), Quaternion.identity);
        var sb = new StringBuilder("clip,frame,tip_bearing,tip_r,tip_h,blade_pitch,body_yaw,armR_twist\n");
        string summary = "";
        try
        {
            var anim = go.GetComponentInChildren<Animator>();
            var hand = anim.GetBoneTransform(HumanBodyBones.RightHand);
            var sword = hand ? hand.Find("Greatsword") : null;
            var hips = anim.GetBoneTransform(HumanBodyBones.Hips);
            var upR = anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
            var loR = anim.GetBoneTransform(HumanBodyBones.RightLowerArm);
            if (!sword) return "bzprobe: no Greatsword under the right hand";
            // blade length = farthest sword vertex along the grip node's +Z (the convention BladeTrail uses)
            float bladeLen = 0f;
            foreach (var mf in sword.GetComponentsInChildren<MeshFilter>())
            {
                if (!mf.sharedMesh || !mf.sharedMesh.isReadable) { if (mf.sharedMesh) { var b = mf.sharedMesh.bounds; for (int i = 0; i < 8; i++) { var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)); bladeLen = Mathf.Max(bladeLen, Vector3.Dot(mf.transform.TransformPoint(c) - sword.position, sword.forward)); } } continue; }
                foreach (var v in mf.sharedMesh.vertices) bladeLen = Mathf.Max(bladeLen, Vector3.Dot(mf.transform.TransformPoint(v) - sword.position, sword.forward));
            }
            var root = anim.transform;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(fbx))
            {
                if (!(o is AnimationClip clip) || !clip.name.StartsWith("BZ_Slash")) continue;
                int n = Mathf.RoundToInt(clip.length * 30f);
                float maxR = 0f;
                for (int f = 0; f <= n; f++)
                {
                    AnimationMode.StartAnimationMode();
                    AnimationMode.BeginSampling();
                    AnimationMode.SampleAnimationClip(anim.gameObject, clip, Mathf.Min(f / 30f, clip.length));
                    AnimationMode.EndSampling();
                    var tip = sword.position + sword.forward * bladeLen;
                    var lp = root.InverseTransformPoint(tip);
                    var ld = root.InverseTransformDirection(sword.forward);
                    float bearing = Mathf.Atan2(lp.x, lp.z) * Mathf.Rad2Deg;
                    float r = new Vector2(lp.x, lp.z).magnitude;
                    float pitch = Mathf.Asin(Mathf.Clamp(ld.y, -1f, 1f)) * Mathf.Rad2Deg;
                    var hf = root.InverseTransformDirection(hips.forward);
                    float yaw = Mathf.Atan2(hf.x, hf.z) * Mathf.Rad2Deg;
                    // upper-arm twist: the forearm's right axis against the upper arm's, about the upper arm
                    var ax = (loR.position - upR.position).normalized;
                    System.Func<Vector3, Vector3> pr = v => (v - ax * Vector3.Dot(v, ax)).normalized;
                    float tw = Vector3.SignedAngle(pr(upR.up), pr(loR.up), ax);
                    maxR = Mathf.Max(maxR, r);
                    sb.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0},{1},{2:0.0},{3:0.000},{4:0.000},{5:0.0},{6:0.0},{7:0.0},{8:0.0}",
                        clip.name, f, bearing, r, lp.y, pitch, yaw, tw, ChestYaw(anim, root)));
                }
                summary += $" {clip.name} {n} f max tip r {maxR:0.00};";
            }
        }
        finally
        {
            AnimationMode.StopAnimationMode();
            Object.DestroyImmediate(go);
        }
        string dir = Path.Combine(Application.dataPath, "..", "ArtSource", "GSHero", "ingame_shots", "wide_sweep");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "bzprobe.csv"), sb.ToString());
        return "bzprobe:" + summary;
    }
}
