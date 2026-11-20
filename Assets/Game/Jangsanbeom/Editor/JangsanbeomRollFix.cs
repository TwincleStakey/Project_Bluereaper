// 장산범 v2 (11-20): the floor-scrape clips have the whole trunk chain (Hips .. Head) rolled 180 degrees about the spine,
// with the legs counter-rotated so the paws still land right. The fur hid it, but the neck read upside down (Kido: "목이
// 업사이드 다운") and anything mounted on the back (big gramophone, horns) swung under the belly. Measured in the capture:
// walk Spine3 right = world down, scrape Spine3 right = world up.
// Fix per key: trunk bones get world' = world * 180 deg about their own spine axis; every other bone keeps its world pose;
// local rotations/positions are recomputed and written back. Source = <clip>_orig.anim (kept by JangsanbeomHeadLift).
// Trigger: Temp/jsb_rollfix.trigger, one clip name per line; done -> Temp/jsb_rollfix.done.
#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class JangsanbeomRollFix
{
    const string Root = "Assets/Game/Jangsanbeom/V2/Models/";
    static readonly HashSet<string> Trunk = new HashSet<string> { "Hips", "Spine1", "Spine2", "Spine3", "Neck1", "Neck2", "Head", "Jaw" };
    [InitializeOnLoadMethod] static void Watch() { EditorApplication.update += Tick; }
    static void Tick()
    {
        const string trig = "Temp/jsb_rollfix.trigger";
        if (!File.Exists(trig) || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        var lines = File.ReadAllText(trig).Split(new[] { "\n", "\r\n" }, System.StringSplitOptions.RemoveEmptyEntries); File.Delete(trig);
        string log = "";
        try { foreach (var l in lines) log += Fix(l.Trim()); }
        catch (System.Exception e) { log += "ERROR " + e; }
        File.WriteAllText("Temp/jsb_rollfix.done", log);
    }

    public static string Fix(string clipName)
    {
        string path = Root + clipName + ".anim", orig = Root + clipName + "_orig.anim";
        if (!File.Exists(orig)) AssetDatabase.CopyAsset(path, orig);
        var src = AssetDatabase.LoadAssetAtPath<AnimationClip>(orig);
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "jsb2_model.fbx");
        var inst = (GameObject)Object.Instantiate(model); inst.hideFlags = HideFlags.HideAndDontSave;
        var sb = new System.Text.StringBuilder();
        try
        {
            var all = inst.GetComponentsInChildren<Transform>().Where(t => t != inst.transform).ToArray();
            // times: every key of every rotation curve
            var times = new SortedSet<float>();
            foreach (var b in AnimationUtility.GetCurveBindings(src))
                foreach (var k in AnimationUtility.GetEditorCurve(src, b).keys) times.Add(Mathf.Round(k.time * 10000f) / 10000f);
            var tl = times.ToList();
            var bones = all.Where(t => t.GetComponent<Renderer>() == null).ToArray();
            var pathOf = bones.ToDictionary(t => t, t => AnimationUtility.CalculateTransformPath(t, inst.transform));
            var rot = bones.ToDictionary(t => t, t => new List<Quaternion>());
            var pos = bones.ToDictionary(t => t, t => new List<Vector3>());
            int flippedBefore = 0;
            foreach (var time in tl)
            {
                src.SampleAnimation(inst, time);
                var wr = bones.ToDictionary(t => t, t => t.rotation);
                var wp = bones.ToDictionary(t => t, t => t.position);
                var sp3 = bones.FirstOrDefault(t => t.name == "Spine3"); if (sp3 && sp3.right.y > 0.5f) flippedBefore++;
                foreach (var t in bones) if (Trunk.Contains(t.name)) wr[t] = wr[t] * Quaternion.AngleAxis(180f, Vector3.up);
                // world -> local, parents first (bones are in hierarchy order)
                foreach (var t in bones)
                {
                    var p = t.parent;
                    Quaternion pr = p == null ? Quaternion.identity : (wr.ContainsKey(p) ? wr[p] : p.rotation);
                    Vector3 pp = p == null ? Vector3.zero : (wp.ContainsKey(p) ? wp[p] : p.position);
                    Vector3 ps = p == null ? Vector3.one : p.lossyScale;
                    var lr = Quaternion.Inverse(pr) * wr[t];
                    var lp = Quaternion.Inverse(pr) * (wp[t] - pp); lp = new Vector3(lp.x / ps.x, lp.y / ps.y, lp.z / ps.z);
                    rot[t].Add(lr); pos[t].Add(lp);
                }
            }
            int written = 0;
            foreach (var t in bones)
            {
                bool affected = Trunk.Contains(t.name) || (t.parent != null && Trunk.Contains(t.parent.name));
                if (!affected) continue;
                string p = pathOf[t];
                string[] rc = { "x", "y", "z", "w" };
                for (int i = 0; i < 4; i++)
                {
                    var keys = new Keyframe[tl.Count];
                    for (int k = 0; k < tl.Count; k++) { var q = rot[t][k]; if (k > 0 && Quaternion.Dot(q, rot[t][k - 1]) < 0) { q = new Quaternion(-q.x, -q.y, -q.z, -q.w); rot[t][k] = q; } keys[k] = new Keyframe(tl[k], i == 0 ? q.x : i == 1 ? q.y : i == 2 ? q.z : q.w); }
                    AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(p, typeof(Transform), "m_LocalRotation." + rc[i]), Lin(keys));
                }
                for (int i = 0; i < 3; i++)
                {
                    var keys = new Keyframe[tl.Count];
                    for (int k = 0; k < tl.Count; k++) keys[k] = new Keyframe(tl[k], pos[t][k][i]);
                    AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(p, typeof(Transform), "m_LocalPosition." + rc[i]), Lin(keys));
                }
                written++;
            }
            EditorUtility.SetDirty(clip); AssetDatabase.SaveAssets();
            sb.AppendLine($"{clipName}: keys {tl.Count}, Spine3 flipped in {flippedBefore} of them, bones rewritten {written}");
        }
        finally { Object.DestroyImmediate(inst); }
        return sb.ToString();
    }

    static AnimationCurve Lin(Keyframe[] k)
    {
        var c = new AnimationCurve(k);
        for (int i = 0; i < k.Length; i++) { AnimationUtility.SetKeyLeftTangentMode(c, i, AnimationUtility.TangentMode.Linear); AnimationUtility.SetKeyRightTangentMode(c, i, AnimationUtility.TangentMode.Linear); }
        return c;
    }
}
#endif
