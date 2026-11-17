// 11-16 Kido ("각 발마다 조금씩 딜레이, 한 발 쉬고 한 발, 당길 때 더 움직이고 쉴 때 쿵 살짝 아래로"):
// bakes a heavy version of a walk clip. Each paw's touch-down becomes a short hold (time slows right after a footfall),
// the swing/pull in between plays faster, and the whole rig drops a little on every footfall and comes back up.
// Trigger: Temp/jsb_heavy.trigger with "srcFbx outAnim [holdFrac 0.45] [drop 0.5] [slow 1.25] [flexDeg 0]". Done stamp Temp/jsb_heavy.done.
// flexDeg (11-17 Kido: "몸이 뻣뻣해"): the spine humps when the hind paws gather under the body and sags/extends when they
// reach back, driven by where the hind paws are, so it stays in time with the gait; the neck counters to keep the head steady.
#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class JangsanbeomHeavyWalk
{
    [InitializeOnLoadMethod]
    static void Watch() { EditorApplication.update += Tick; }
    static void Tick()
    {
        const string trig = "Temp/jsb_heavy.trigger";
        if (!File.Exists(trig) || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        var a = File.ReadAllText(trig).Trim().Split(' '); File.Delete(trig);
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        string log;
        try
        {
            log = Bake(a[0], a[1],
                a.Length > 2 ? float.Parse(a[2], ci) : 0.45f,
                a.Length > 3 ? float.Parse(a[3], ci) : 0.5f,
                a.Length > 4 ? float.Parse(a[4], ci) : 1.25f,
                a.Length > 5 ? float.Parse(a[5], ci) : 0f);
        }
        catch (System.Exception e) { log = "ERROR " + e; }
        File.WriteAllText("Temp/jsb_heavy.done", log);
    }

    static string Bake(string srcFbx, string outAnim, float holdFrac, float drop, float slow, float flexDeg)
    {
        var src = AssetDatabase.LoadAllAssetsAtPath(srcFbx).OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview"));
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(srcFbx);
        var go = (GameObject)Object.Instantiate(model); go.hideFlags = HideFlags.HideAndDontSave;
        var sb = new System.Text.StringBuilder();
        try
        {
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var all = go.GetComponentsInChildren<Transform>();
            var toes = new[] { "HindToes.L", "HindToes.R", "FrontToes.L", "FrontToes.R" }.Select(n => all.FirstOrDefault(t => t.name == n)).Where(t => t != null).ToArray();
            float L = src.length; const int N = 480;
            // 1) footfalls: a paw coming down to the ground (height crosses into the lowest band)
            var h = new float[toes.Length, N + 1];
            for (int k = 0; k <= N; k++) { src.SampleAnimation(go, k * L / N); for (int i = 0; i < toes.Length; i++) h[i, k] = toes[i].position.y; }
            var falls = new List<float>();
            for (int i = 0; i < toes.Length; i++)
            {
                float lo = float.MaxValue, hi = float.MinValue; for (int k = 0; k <= N; k++) { lo = Mathf.Min(lo, h[i, k]); hi = Mathf.Max(hi, h[i, k]); }
                float th = lo + (hi - lo) * 0.2f;
                for (int k = 1; k <= N; k++) if (h[i, k - 1] > th && h[i, k] <= th) falls.Add(k * L / N);
            }
            falls.Sort();
            sb.AppendLine($"src {src.name} len {L:F2}s toes {toes.Length} footfalls {falls.Count}: {string.Join(",", falls.Select(f => f.ToString("F2")))}");
            if (falls.Count == 0) return sb.Append("no footfalls").ToString();

            // 2) playback rate over source time: slow right after each footfall (the hold), fast in between (the pull)
            float hold = L / falls.Count * holdFrac;                 // seconds of source time spent "resting" after a footfall
            System.Func<float, float> rate = t =>
            {
                float r = 1.6f;                                       // pull: faster than the source
                foreach (var f in falls) for (int w = -1; w <= 1; w++) { float d = t - (f + w * L); if (d >= 0 && d < hold) r = Mathf.Min(r, Mathf.Lerp(0.25f, 1.6f, Mathf.SmoothStep(0, 1, d / hold))); }
                return r;
            };
            // output time for each source time, scaled so the whole cycle lasts slow x the source
            var tOut = new float[N + 1];
            for (int k = 1; k <= N; k++) { float ts = (k - 0.5f) * L / N; tOut[k] = tOut[k - 1] + (L / N) / rate(ts); }
            float scale = L * slow / tOut[N]; for (int k = 0; k <= N; k++) tOut[k] *= scale;
            float Lout = tOut[N];
            System.Func<float, float> srcAt = to =>
            {
                int k = System.Array.BinarySearch(tOut, to); if (k >= 0) return k * L / N;
                k = ~k; if (k <= 0) return 0; if (k > N) return L;
                return Mathf.Lerp((k - 1) * L / N, k * L / N, (to - tOut[k - 1]) / (tOut[k] - tOut[k - 1]));
            };
            // footfall times in output time (for the drop)
            var fallsOut = falls.Select(f => tOut[Mathf.Clamp(Mathf.RoundToInt(f / L * N), 0, N)]).ToList();

            // 3) resample every curve at the warped times
            // spine flex signal: hind paws' forward offset from the hips, normalised to -1..1 over the cycle
            var hipsT = all.FirstOrDefault(t => t.name == "Hips"); var headT = all.FirstOrDefault(t => t.name == "Head");
            var sp1 = all.FirstOrDefault(t => t.name == "Spine1"); var sp3 = all.FirstOrDefault(t => t.name == "Spine3"); var nk = all.FirstOrDefault(t => t.name == "Neck1");
            var hind = toes.Where(t => t.name.StartsWith("Hind")).ToArray();
            src.SampleAnimation(go, 0f);
            var fwd = headT.position - hipsT.position; fwd.y = 0; fwd.Normalize();
            var right = Vector3.Cross(Vector3.up, fwd);
            System.Func<float> gather = () => Vector3.Dot(hind.Aggregate(Vector3.zero, (acc, t) => acc + t.position) / hind.Length - hipsT.position, fwd);
            float gMin = float.MaxValue, gMax = float.MinValue;
            for (int k = 0; k <= N; k += 4) { src.SampleAnimation(go, k * L / N); float g = gather(); gMin = Mathf.Min(gMin, g); gMax = Mathf.Max(gMax, g); }
            bool flex = flexDeg > 0 && sp1 && sp3 && nk && hind.Length > 0 && gMax - gMin > 1e-4f;

            var dst = new AnimationClip { name = Path.GetFileNameWithoutExtension(outAnim), frameRate = 30 };
            int nf = Mathf.CeilToInt(Lout * 30);
            var flexPaths = flex ? new[] { sp1, sp3, nk }.Select(t => AnimationUtility.CalculateTransformPath(t, go.transform)).ToArray() : new string[0];
            foreach (var b in AnimationUtility.GetCurveBindings(src))
            {
                if (flexPaths.Contains(b.path) && (b.propertyName.StartsWith("m_LocalRotation") || b.propertyName.StartsWith("localEulerAngles"))) continue;   // rebuilt below
                var c = AnimationUtility.GetEditorCurve(src, b);
                var keys = new Keyframe[nf + 1];
                for (int f = 0; f <= nf; f++) { float to = Mathf.Min(f / 30f, Lout); keys[f] = new Keyframe(to, c.Evaluate(srcAt(to))); }
                var nc = new AnimationCurve(keys); for (int f = 0; f <= nf; f++) { AnimationUtility.SetKeyLeftTangentMode(nc, f, AnimationUtility.TangentMode.ClampedAuto); AnimationUtility.SetKeyRightTangentMode(nc, f, AnimationUtility.TangentMode.ClampedAuto); }
                AnimationUtility.SetEditorCurve(dst, b, nc);
            }
            if (flex)
            {
                var bones = new[] { sp1, sp3, nk }; var qk = new Keyframe[3, 4][];
                for (int j = 0; j < 3; j++) for (int c = 0; c < 4; c++) qk[j, c] = new Keyframe[nf + 1];
                var prevQ = new Quaternion[3];
                for (int f = 0; f <= nf; f++)
                {
                    float to = Mathf.Min(f / 30f, Lout);
                    src.SampleAnimation(go, srcAt(to));
                    float s = Mathf.Lerp(-1f, 1f, Mathf.InverseLerp(gMin, gMax, gather()));   // +1 gathered, -1 stretched
                    float th = flexDeg * s;
                    sp1.rotation = Quaternion.AngleAxis(-th, right) * sp1.rotation;          // hips end of the back rises (hump)
                    sp3.rotation = Quaternion.AngleAxis(th * 1.2f, right) * sp3.rotation;    // chest comes back down past level
                    nk.rotation = Quaternion.AngleAxis(-th * 0.35f, right) * nk.rotation;    // head stays steadier, like a stalking cat
                    for (int j = 0; j < 3; j++)
                    {
                        var q = bones[j].localRotation;
                        if (f > 0 && Quaternion.Dot(q, prevQ[j]) < 0) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                        prevQ[j] = q;
                        qk[j, 0][f] = new Keyframe(to, q.x); qk[j, 1][f] = new Keyframe(to, q.y); qk[j, 2][f] = new Keyframe(to, q.z); qk[j, 3][f] = new Keyframe(to, q.w);
                    }
                }
                string[] comp = { "x", "y", "z", "w" };
                for (int j = 0; j < 3; j++) for (int c = 0; c < 4; c++) dst.SetCurve(flexPaths[j], typeof(Transform), "m_LocalRotation." + comp[c], new AnimationCurve(qk[j, c]));
                sb.AppendLine($"spine flex {flexDeg} deg on Spine1/Spine3/Neck1 (hind gather {gMin:F2}..{gMax:F2})");
            }

            // 4) the thump: the armature (child of the boss root, so its local Y is up) dips on each footfall
            var rig = go.transform.Cast<Transform>().FirstOrDefault(t => t.GetComponentsInChildren<Transform>().Any(x => x.name == "Hips"));
            if (rig != null && drop > 0)
            {
                string path = AnimationUtility.CalculateTransformPath(rig, go.transform);
                var p0 = rig.localPosition;
                var ky = new Keyframe[nf + 1]; var kx = new Keyframe[nf + 1]; var kz = new Keyframe[nf + 1];
                for (int f = 0; f <= nf; f++)
                {
                    float to = Mathf.Min(f / 30f, Lout), d = 0;
                    foreach (var fo in fallsOut) for (int w = -1; w <= 1; w++)
                    {
                        float u = to - (fo + w * Lout);
                        if (u > -0.05f && u < 0.45f) d = Mathf.Max(d, u < 0.06f ? Mathf.SmoothStep(0, 1, (u + 0.05f) / 0.11f) : Mathf.SmoothStep(1, 0, (u - 0.06f) / 0.39f));
                    }
                    ky[f] = new Keyframe(to, p0.y - drop * d); kx[f] = new Keyframe(to, p0.x); kz[f] = new Keyframe(to, p0.z);
                }
                dst.SetCurve(path, typeof(Transform), "m_LocalPosition.x", new AnimationCurve(kx));
                dst.SetCurve(path, typeof(Transform), "m_LocalPosition.y", new AnimationCurve(ky));
                dst.SetCurve(path, typeof(Transform), "m_LocalPosition.z", new AnimationCurve(kz));
                sb.AppendLine($"drop {drop} on '{path}' at {fallsOut.Count} footfalls");
            }
            var st = AnimationUtility.GetAnimationClipSettings(dst); st.loopTime = true; AnimationUtility.SetAnimationClipSettings(dst, st);
            Directory.CreateDirectory(Path.GetDirectoryName(outAnim));
            AssetDatabase.DeleteAsset(outAnim); AssetDatabase.CreateAsset(dst, outAnim); AssetDatabase.SaveAssets();
            sb.AppendLine($"baked {outAnim}: {Lout:F2}s (source {L:F2}s), hold {hold:F2}s after each footfall");
        }
        finally { Object.DestroyImmediate(go); }
        return sb.ToString();
    }
}
#endif
