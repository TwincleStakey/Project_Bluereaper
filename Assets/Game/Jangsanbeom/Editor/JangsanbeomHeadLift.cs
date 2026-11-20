// 장산범 v2 (11-20 Kido: "긁을 때 등 갈퀴가 너무 크고 얼굴이 아예 안 보여"): in the floor scrapes the head hangs low, under
// the big neck/shoulder fur, so from the high game camera the face is hidden. Lift the head: pitch Neck2 and Head up by a
// fixed angle on every key of the clip (about the body's side axis, measured on the rest pose).
// Trigger: Temp/jsb_headlift.trigger, one line per clip: "<clip name, e.g. jsb2_scrape_L_pin> <neck2Deg> <headDeg>"; done -> Temp/jsb_headlift.done.
// The untouched clip is kept once as <clip>_orig.anim and every run starts from it, so the angles do not stack.
#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

public static class JangsanbeomHeadLift
{
    const string Root = "Assets/Game/Jangsanbeom/V2/Models/";
    [InitializeOnLoadMethod] static void Watch() { EditorApplication.update += Tick; }
    static void Tick()
    {
        const string trig = "Temp/jsb_headlift.trigger";
        if (!File.Exists(trig) || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        var lines = File.ReadAllText(trig).Trim().Split(new[] { "\n", "\r\n" }, System.StringSplitOptions.RemoveEmptyEntries); File.Delete(trig);
        string log = "";
        try
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            foreach (var ln in lines) { var a = ln.Trim().Split(' '); if (a.Length >= 3) log += Lift(a[0], float.Parse(a[1], ci), float.Parse(a[2], ci)); }
        }
        catch (System.Exception e) { log = "ERROR " + e; }
        File.WriteAllText("Temp/jsb_headlift.done", log);
    }

    public static string Lift(string clipName, float neckDeg, float headDeg)
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
            Transform Find(string n) { foreach (var t in inst.GetComponentsInChildren<Transform>()) if (t.name == n) return t; return null; }
            var hips = Find("Hips"); var head = Find("Head");
            // the body's side axis on the rest pose: perpendicular to the spine line and up
            Vector3 fwd = (head.position - hips.position); fwd.y = 0; fwd.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, fwd).normalized;
            foreach (var (bone, deg) in new[] { ("Neck2", neckDeg), ("Head", headDeg) })
            {
                var t = Find(bone);
                // the rotation axis in the bone's own frame (rest pose); sign picked so + lifts the snout
                Vector3 la = Quaternion.Inverse(t.rotation) * side;
                Vector3 tip = t.position + t.rotation * Vector3.up; // probe which way + turns the bone
                string p = AnimationUtility.CalculateTransformPath(t, inst.transform);
                var cb = new[] { "x", "y", "z", "w" };
                var curves = new AnimationCurve[4];
                for (int i = 0; i < 4; i++) curves[i] = AnimationUtility.GetEditorCurve(src, EditorCurveBinding.FloatCurve(p, typeof(Transform), "m_LocalRotation." + cb[i]));
                if (curves[0] == null) { sb.AppendLine("no curve " + p); continue; }
                // + = snout up: test on the rest pose with the head's forward point
                var before = head.position + head.rotation * Vector3.zero;
                var q0 = t.localRotation;
                Vector3 snout0 = Find("Jaw") != null ? Find("Jaw").position : head.position + fwd;
                t.localRotation = q0 * Quaternion.AngleAxis(10f, la);
                Vector3 snout1 = Find("Jaw") != null ? Find("Jaw").position : head.position + fwd;
                t.localRotation = q0;
                // checked in game (11-20 capture): the jaw-height probe reads the other way round, so it is inverted here -
                // a positive angle shows the face to the top-down camera
                float sign = snout1.y > snout0.y ? -1f : 1f;
                var dq = Quaternion.AngleAxis(sign * deg, la);
                int n = curves[0].length;
                var outK = new Keyframe[4][];
                for (int i = 0; i < 4; i++) outK[i] = new Keyframe[n];
                for (int k = 0; k < n; k++)
                {
                    float time = curves[0].keys[k].time;
                    var q = new Quaternion(curves[0].Evaluate(time), curves[1].Evaluate(time), curves[2].Evaluate(time), curves[3].Evaluate(time));
                    var r = q * dq;
                    float[] v = { r.x, r.y, r.z, r.w };
                    for (int i = 0; i < 4; i++) outK[i][k] = new Keyframe(time, v[i]);
                }
                for (int i = 0; i < 4; i++)
                {
                    var c = new AnimationCurve(outK[i]);
                    for (int k = 0; k < n; k++) AnimationUtility.SetKeyLeftTangentMode(c, k, AnimationUtility.TangentMode.Linear);
                    for (int k = 0; k < n; k++) AnimationUtility.SetKeyRightTangentMode(c, k, AnimationUtility.TangentMode.Linear);
                    AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(p, typeof(Transform), "m_LocalRotation." + cb[i]), c);
                }
                sb.AppendLine($"{bone} {deg} deg (sign {sign}) keys {n}");
            }
            EditorUtility.SetDirty(clip); AssetDatabase.SaveAssets();
        }
        finally { Object.DestroyImmediate(inst); }
        return sb.ToString();
    }
}
#endif
