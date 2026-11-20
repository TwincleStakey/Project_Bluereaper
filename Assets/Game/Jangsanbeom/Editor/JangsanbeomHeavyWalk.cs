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
                a.Length > 5 ? float.Parse(a[5], ci) : 0f,
                a.Length > 6 ? float.Parse(a[6], ci) : 0f,
                a.Length > 7 ? float.Parse(a[7], ci) : 0f,
                a.Length > 8 ? float.Parse(a[8], ci) : 1f,
                a.Length > 9 ? float.Parse(a[9], ci) : 0f,
                a.Length > 10 ? float.Parse(a[10], ci) : 0f,
                a.Length > 11 ? a[11] : "",
                a.Length > 12 ? int.Parse(a[12]) : 6,
                a.Length > 13 ? float.Parse(a[13], ci) : 0.55f);
        }
        catch (System.Exception e) { log = "ERROR " + e; }
        File.WriteAllText("Temp/jsb_heavy.done", log);
    }

    // two-bone IK in world space: bend a (upper) and b (lower) so the end c reaches target, keeping the bend plane
    // two-bone IK in world space: bend a (upper) and b (lower) so the end c reaches target.
    // 11-19 Kido: "무릎 꺾이고" - near full extension the bend plane from a cross product is noise and flipped, so the knee
    // snapped the wrong way for a frame. Now the knee is placed geometrically: in the plane through hip and target that
    // contains the body's side axis, always on the same side of the hip-target line as the leg bends in the source pose
    // (kneeSide = +1/-1 along cross(side, hip->target)), and the leg never quite locks straight (99.5 %).
    internal static void SolveLeg(Transform a, Transform b, Transform c, Vector3 target, Vector3 side, float kneeSide, float soft = 0.985f, float band = 0.012f)
    {
        float lab = (b.position - a.position).magnitude, lcb = (c.position - b.position).magnitude;
        // 11-19: soft reach limit. A hard clamp snapped the leg straight in one frame (25 deg) when a pinned paw went past reach
        var d = target - a.position; float full = lab + lcb, r = d.magnitude / full;
        if (r > soft) r = soft + band * (1f - Mathf.Exp(-(r - soft) / band));
        float lat = Mathf.Max(0.01f, r * full); var dn = d.normalized;
        float x = (lab * lab - lcb * lcb + lat * lat) / (2f * lat), h = Mathf.Sqrt(Mathf.Max(0f, lab * lab - x * x));
        var k = Vector3.Cross(side, dn); if (k.sqrMagnitude < 1e-8f) return; k.Normalize();
        var knee = a.position + dn * x + k * h * kneeSide;
        var end = a.position + dn * lat;
        a.rotation = Quaternion.FromToRotation(b.position - a.position, knee - a.position) * a.rotation;
        b.rotation = Quaternion.FromToRotation(c.position - b.position, end - knee) * b.rotation;
    }
    internal static float KneeSide(Transform a, Transform b, Transform c, Vector3 side)
    {
        var dn = (c.position - a.position).normalized; var k = Vector3.Cross(side, dn);
        return Vector3.Dot(b.position - a.position, k) >= 0 ? 1f : -1f;
    }

    static string Bake(string srcFbx, string outAnim, float holdFrac, float drop, float slow, float flexDeg, float rollDeg, float narrow, float pin, float strike, float frontShift, string plant = "", int trunkWin = 6, float trunkKeep = 0.55f)
    {
        var src = AssetDatabase.LoadAllAssetsAtPath(srcFbx).OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview"));
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(srcFbx);
        var go = (GameObject)Object.Instantiate(model); go.hideFlags = HideFlags.HideAndDontSave;
        var sb = new System.Text.StringBuilder();
        // plant: "1" = all four paws stay planted for the whole clip; or four digits in the order front L, front R, hind L, hind R
        // ("1011" = everything but the right front paw, which is left free to scrape)
        if (plant == "1") plant = "1111"; bool plantAll = plant.Length == 4 && plant.Contains("1");
        var plantMask = new bool[4]; for (int q = 0; q < 4; q++) plantMask[q] = plantAll && plant[q] == '1';
        try
        {
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var all = go.GetComponentsInChildren<Transform>();
            var bindLocal = all.ToDictionary(t => t, t => t.localRotation);   // rest pose, before any sampling
            var toes = new[] { "HindToes.L", "HindToes.R", "FrontToes.L", "FrontToes.R" }.Select(n => all.FirstOrDefault(t => t.name == n)).Where(t => t != null).ToArray();
            float L = src.length; const int N = 480;
            // 1) footfalls: a paw coming down to the ground (height crosses into the lowest band)
            var h = new float[toes.Length, N + 1];
            for (int k = 0; k <= N; k++) { src.SampleAnimation(go, k * L / N); for (int i = 0; i < toes.Length; i++) h[i, k] = toes[i].position.y; }
            var falls = new List<float>(); var fallsFront = new List<bool>();
            for (int i = 0; i < toes.Length; i++)
            {
                float lo = float.MaxValue, hi = float.MinValue; for (int k = 0; k <= N; k++) { lo = Mathf.Min(lo, h[i, k]); hi = Mathf.Max(hi, h[i, k]); }
                float th = lo + (hi - lo) * 0.2f;
                for (int k = 1; k <= N; k++) if (h[i, k - 1] > th && h[i, k] <= th) { falls.Add(k * L / N); fallsFront.Add(toes[i].name.StartsWith("Front")); }
            }
            sb.AppendLine($"src {src.name} len {L:F2}s toes {toes.Length} footfalls {falls.Count}: {string.Join(",", falls.OrderBy(x => x).Select(f => f.ToString("F2")))}");
            if (falls.Count == 0 && !plantAll) return sb.Append("no footfalls").ToString();

            // 2) playback rate over source time: slow right after each footfall (the hold), fast in between (the pull)
            float hold = L / Mathf.Max(1, falls.Count) * holdFrac;                 // seconds of source time spent "resting" after a footfall
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
            // 11-19 Kido ("앞다리 시퀀스 맞어? 타이밍이 이상해"): the source paired each front paw with the opposite hind paw (a trot
            // rhythm: footfalls 0.05/0.94 and 0.81/1.84). A lion walks a lateral sequence, LH -> LF -> RH -> RF a quarter cycle
            // apart, so the front legs play frontShift of a cycle ahead of the hind legs.
            System.Func<float, float> srcAtFront = to => { float tt = to + frontShift * Lout; tt -= Mathf.Floor(tt / Lout) * Lout; return srcAt(tt); };
            System.Func<string, bool> isFront = n => n.Contains("Scapula") || n.Contains("UpperArm") || n.Contains("Forearm") || n.Contains("FrontPaw") || n.Contains("FrontToes");
            var frontBones = all.Where(t => isFront(t.name)).ToArray();
            var frontRot = new Quaternion[frontBones.Length];
            System.Func<string, bool> isLeg = n => isFront(n) || n.Contains("Thigh") || n.Contains("Shin") || n.Contains("HindPaw") || n.Contains("HindToes");
            var legBones = all.Where(t => isLeg(t.name)).ToArray(); var legAcc = new Vector4[legBones.Length];
            sb.AppendLine($"front legs shifted {frontShift:F2} cycle ({frontBones.Length} bones)");
            // footfall times in output time (for the drop)
            var fallsOut = falls.Select((f, j) => { float o = tOut[Mathf.Clamp(Mathf.RoundToInt(f / L * N), 0, N)]; if (fallsFront[j]) { o -= frontShift * Lout; o -= Mathf.Floor(o / Lout) * Lout; } return o; }).ToList();

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
            // 11-17 lion-walk reference: the body leans a little toward the planted front paw, then rolls over to the other side
            var fl = all.FirstOrDefault(t => t.name == "FrontToes.L"); var fr = all.FirstOrDefault(t => t.name == "FrontToes.R");
            float sideRange = 0.01f;
            if (fl && fr) for (int k = 0; k <= N; k += 4) { src.SampleAnimation(go, k * L / N); sideRange = Mathf.Max(sideRange, Mathf.Abs(fl.position.y - fr.position.y)); }
            // 11-17 Kido: "뒷다리가 양옆으로 벌어져 있어 이상해, 사자처럼": swing each thigh in (about the body axis) so the hind paw
            // lands as wide as the front paws do, under the body; narrow = 0..1 blend toward the front width
            var thighs = new[] { "Thigh.L", "Thigh.R" }.Select(n => all.FirstOrDefault(t => t.name == n)).ToArray();
            var hindToes = new[] { "HindToes.L", "HindToes.R" }.Select(n => all.FirstOrDefault(t => t.name == n)).ToArray();
            float frontW = 0, hindW = 0; int wn = 0;
            if (fl && fr && thighs.All(t => t) && hindToes.All(t => t))
                for (int k = 0; k <= N; k += 8)
                {
                    src.SampleAnimation(go, k * L / N);
                    frontW += Mathf.Abs(Vector3.Dot(fl.position - fr.position, Vector3.Cross(Vector3.up, (headT.position - hipsT.position).normalized))) * 0.5f;
                    hindW += Mathf.Abs(Vector3.Dot(hindToes[0].position - hindToes[1].position, Vector3.Cross(Vector3.up, (headT.position - hipsT.position).normalized))) * 0.5f;
                    wn++;
                }
            if (wn > 0) { frontW /= wn; hindW /= wn; }
            bool doNarrow = narrow > 0 && wn > 0;
            sb.AppendLine($"paw half-width front {frontW:F2} hind {hindW:F2}; knee turn-in {(doNarrow ? narrow.ToString("F2") : "off")}");
            var flex = (flexDeg > 0 || rollDeg > 0 || narrow > 0) && sp1 && sp3 && nk && hind.Length > 0 && gMax - gMin > 1e-4f;

            var dst = new AnimationClip { name = Path.GetFileNameWithoutExtension(outAnim), frameRate = 30 };
            int nf = Mathf.CeilToInt(Lout * 30);
            var rig = go.transform.Cast<Transform>().FirstOrDefault(t => t.GetComponentsInChildren<Transform>().Any(x => x.name == "Hips"));
            var rigP0 = rig != null ? rig.localPosition : Vector3.zero;
            System.Func<float, float> dipAt = to =>
            {
                float d = 0;
                // 11-19 Kido ("쿵 하고 한 번 촐싹하고 다시 올라오고, 두 번 위아래로 왔다 갔다"): a sharp dip on every one of the four
                // footfalls made a thump, a hiccup and a rise per step. Now one smooth heave per hind footfall: lowest just after the
                // hind paw lands, a raised cosine half a second either side (the front footfalls add nothing)
                for (int j = 0; j < fallsOut.Count; j++)
                {
                    if (fallsFront[j]) continue;
                    for (int w = -1; w <= 1; w++)
                    {
                        float u = to - (fallsOut[j] + w * Lout) - 0.12f;
                        if (Mathf.Abs(u) < 0.5f) d = Mathf.Max(d, 0.5f * (1f + Mathf.Cos(Mathf.PI * u / 0.5f)));
                    }
                }
                return d;
            };
            // legs: upper, lower, paw, toes (two-bone IK on upper/lower so a planted paw can be held still on the ground)
            string[][] legNames = { new[] { "UpperArm.L", "Forearm.L", "FrontPaw.L", "FrontToes.L" }, new[] { "UpperArm.R", "Forearm.R", "FrontPaw.R", "FrontToes.R" },
                                    new[] { "Thigh.L", "Shin.L", "HindPaw.L", "HindToes.L" }, new[] { "Thigh.R", "Shin.R", "HindPaw.R", "HindToes.R" } };
            var legs = legNames.Select(l => l.Select(n => all.FirstOrDefault(t => t.name == n)).ToArray()).Where(l => l.All(t => t != null)).ToArray();
            bool doPin = pin > 0 && legs.Length == 4;
            var restUpper = legs.Select(l => bindLocal[l[0]]).ToArray(); var restLower = legs.Select(l => bindLocal[l[1]]).ToArray();
            flex = flex || doPin;
            var trunkBones = new[] { "Hips", "Spine1", "Spine2", "Spine3", "Neck1", "Neck2" }.Select(n => all.FirstOrDefault(t => t.name == n)).Where(t => t != null).ToArray();
            var trunkAcc = new Vector4[trunkBones.Length]; var trunkMean = new Quaternion[trunkBones.Length]; var hipsMean = Vector3.zero; bool trunkHave = false;
            if (plantAll)
            {   // the clip's average trunk pose (what the damped swing is measured from)
                var acc = new Vector4[trunkBones.Length]; int nn = 0;
                for (int k = 0; k <= N; k += 8)
                {
                    src.SampleAnimation(go, k * L / N); nn++;
                    for (int q = 0; q < trunkBones.Length; q++) { var r = trunkBones[q].localRotation; var v = new Vector4(r.x, r.y, r.z, r.w); if (Vector4.Dot(acc[q], v) < 0) v = -v; acc[q] += v; }
                    if (hipsT) hipsMean += hipsT.localPosition;
                }
                for (int q = 0; q < trunkBones.Length; q++) { var v = acc[q].normalized; trunkMean[q] = new Quaternion(v.x, v.y, v.z, v.w); }
                hipsMean /= Mathf.Max(1, nn); trunkHave = true;
            }
            // every leg bone the pose smooths (scapulae and toes too) is baked from the pose: the scapula used to come from the raw source
            // curve while the IK had been solved on the smoothed one, so the planted front paws still crept 1-9 cm a frame
            var flexBones = flex ? (new[] { sp1, sp3, nk }).Concat(legs.SelectMany(l => l.Take(3))).Concat(legBones).Concat(plantAll ? trunkBones : new Transform[0]).Where(t => t != null).Distinct().ToArray() : new Transform[0];
            var flexPaths = flex ? flexBones.Select(t => AnimationUtility.CalculateTransformPath(t, go.transform)).ToArray() : new string[0];
            foreach (var b in AnimationUtility.GetCurveBindings(src))
            {
                if (flexPaths.Contains(b.path) && (b.propertyName.StartsWith("m_LocalRotation") || b.propertyName.StartsWith("localEulerAngles"))) continue;   // rebuilt below
                var c = AnimationUtility.GetEditorCurve(src, b);
                var keys = new Keyframe[nf + 1];
                bool fb = frontShift != 0 && isFront(b.path.Substring(b.path.LastIndexOf('/') + 1));
                for (int f = 0; f <= nf; f++) { float to = Mathf.Min(f / 30f, Lout); keys[f] = new Keyframe(to, c.Evaluate(fb ? srcAtFront(to) : srcAt(to))); }
                var nc = new AnimationCurve(keys); for (int f = 0; f <= nf; f++) { AnimationUtility.SetKeyLeftTangentMode(nc, f, AnimationUtility.TangentMode.ClampedAuto); AnimationUtility.SetKeyRightTangentMode(nc, f, AnimationUtility.TangentMode.ClampedAuto); }
                AnimationUtility.SetEditorCurve(dst, b, nc);
            }
            var latDir = Vector3.Cross(Vector3.up, fwd);
            // the pose of output frame f before any paw pinning: source pose + spine flex + roll + knees in
            bool plantHave = false; var plantC0 = Vector3.zero; var plantToe0 = new Vector3[4];
            System.Action<int> pose = f =>
            {
                float to = Mathf.Min(f / 30f, Lout);
                if (rig != null) rig.localPosition = rigP0;
                // 11-19 Kido ("다리 흔들거리는 것부터"): the retargeted source jerks the forearms 15-25 deg in single frames; average every
                // leg bone over +-2 output frames (weights 1-2-3-2-1, wrapping round the loop) before anything else uses the pose
                // 11-19 Kido ("저런 동작할 때 축음기들 너무 많이 흔들려, 특히 왼발"): in the pinned attack clips the trunk (hips, spine, neck)
                // is also averaged, over a much wider window (+-trunkWin frames, triangle weights), and its swing away from the
                // clip's average pose is scaled down (trunkKeep). The things growing out of the back ride these bones.
                int win = plantAll ? Mathf.Max(2, trunkWin) : 2;
                for (int q = 0; q < trunkBones.Length; q++) trunkAcc[q] = Vector4.zero;
                var hipsAcc = Vector3.zero; float hipsW = 0;
                for (int s2 = -win; s2 <= win; s2++)
                {
                    float ts = to + s2 / 30f; ts -= Mathf.Floor(ts / Lout) * Lout;
                    if (frontShift != 0) { src.SampleAnimation(go, srcAtFront(ts)); for (int q = 0; q < frontBones.Length; q++) frontRot[q] = frontBones[q].localRotation; }
                    src.SampleAnimation(go, srcAt(ts));
                    if (frontShift != 0) for (int q = 0; q < frontBones.Length; q++) frontBones[q].localRotation = frontRot[q];
                    float w = Mathf.Max(0f, 3 - Mathf.Abs(s2));
                    if (plantAll)
                    {
                        float tw = win + 1 - Mathf.Abs(s2);
                        for (int q = 0; q < trunkBones.Length; q++)
                        {
                            var tr = trunkBones[q].localRotation; var v = new Vector4(tr.x, tr.y, tr.z, tr.w);
                            if (Vector4.Dot(trunkAcc[q], v) < 0) v = -v;
                            trunkAcc[q] += v * tw;
                        }
                        if (hipsT) { hipsAcc += hipsT.localPosition * tw; hipsW += tw; }
                    }
                    for (int q = 0; q < legBones.Length; q++)
                    {
                        var r = legBones[q].localRotation;
                        if (s2 == -win) legAcc[q] = Vector4.zero;
                        if (w <= 0f) continue;
                        if (Vector4.Dot(legAcc[q], new Vector4(r.x, r.y, r.z, r.w)) < 0) r = new Quaternion(-r.x, -r.y, -r.z, -r.w);
                        legAcc[q] += new Vector4(r.x, r.y, r.z, r.w) * w;
                    }
                }
                if (frontShift != 0)
                {
                    src.SampleAnimation(go, srcAtFront(to));
                    for (int q = 0; q < frontBones.Length; q++) frontRot[q] = frontBones[q].localRotation;
                }
                src.SampleAnimation(go, srcAt(to));
                if (frontShift != 0) for (int q = 0; q < frontBones.Length; q++) frontBones[q].localRotation = frontRot[q];
                for (int q = 0; q < legBones.Length; q++) { var v4 = legAcc[q].normalized; legBones[q].localRotation = new Quaternion(v4.x, v4.y, v4.z, v4.w); }
                if (plantAll)
                {
                    for (int q = 0; q < trunkBones.Length; q++)
                    {
                        var v4t = trunkAcc[q].normalized; var sm = new Quaternion(v4t.x, v4t.y, v4t.z, v4t.w);
                        if (trunkHave) sm = Quaternion.Slerp(trunkMean[q], sm, trunkKeep);
                        trunkBones[q].localRotation = sm;
                    }
                    if (hipsT && hipsW > 0) { var hp = hipsAcc / hipsW; hipsT.localPosition = trunkHave ? Vector3.Lerp(hipsMean, hp, trunkKeep) : hp; }
                }
                if (sp1 && sp3 && nk && hind.Length > 0 && gMax - gMin > 1e-4f)
                {
                    float sg = Mathf.Lerp(-1f, 1f, Mathf.InverseLerp(gMin, gMax, gather()));   // +1 gathered, -1 stretched
                    float th = flexDeg * sg;
                    sp1.rotation = Quaternion.AngleAxis(-th, right) * sp1.rotation;          // hips end of the back rises (hump)
                    sp3.rotation = Quaternion.AngleAxis(th * 1.2f, right) * sp3.rotation;    // chest comes back down past level
                    nk.rotation = Quaternion.AngleAxis(-th * 0.35f, right) * nk.rotation;    // head stays steadier, like a stalking cat
                    if (rollDeg > 0 && fl && fr)
                    {   // +1 when the right paw is up (left planted): lean left
                        float side = Mathf.Clamp((fr.position.y - fl.position.y) / sideRange, -1f, 1f);
                        float rl = rollDeg * side;
                        sp1.rotation = Quaternion.AngleAxis(rl, fwd) * sp1.rotation;
                        nk.rotation = Quaternion.AngleAxis(-rl * 0.8f, fwd) * nk.rotation;   // the head stays level
                    }
                }
                if (doNarrow)
                    for (int side = 0; side < 2; side++)
                    {   // the paws already land under the body; what splays is the knee. Spin the thigh about the hip->paw line
                        // (the paw stays put) so the knee points forward like a lion's stifle instead of out to the side
                        var th0 = thighs[side]; var toe = hindToes[side]; var shin = th0.childCount > 0 ? th0.GetChild(0) : null;
                        if (shin == null) continue;
                        var ax = (toe.position - th0.position).normalized;
                        var kp = Vector3.ProjectOnPlane(shin.position - th0.position, ax);
                        var fw = Vector3.ProjectOnPlane(headT.position - hipsT.position, ax);
                        if (kp.sqrMagnitude < 1e-8f || fw.sqrMagnitude < 1e-8f) continue;
                        th0.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(kp, fw, ax) * narrow, ax) * th0.rotation;
                    }
                if (plantAll && legs.Length == 4)
                {   // the source drifts over the ground while it howls (the paws slid 4 m): move the body so the four paws stay,
                    // on average, where they stood at the start; the pins take out what is left
                    var cur = Vector3.zero; int pn = 0; for (int i = 0; i < 4; i++) if (plantMask[i]) { cur += legs[i][3].position; pn++; } cur /= Mathf.Max(1, pn);
                    if (!plantHave) { plantHave = true; plantC0 = cur; for (int i = 0; i < 4; i++) plantToe0[i] = legs[i][3].position; }
                    var dlt = cur - plantC0; dlt.y = 0; rig.position -= dlt;
                }
            };
            if (flex)
            {
                // pass A: where each paw is, when it is on the ground, and how far the body travels per frame
                var T = new Vector3[4, nf + 1]; var contact = new bool[4, nf + 1]; var travel = new float[nf + 1]; var target = new Vector3[4, nf + 1]; var pinned = new bool[4, nf + 1];
                if (doPin)
                {
                    // the ground = the lowest toe of the plain source clip (before spine flex/roll, which can push a paw through it)
                    float groundY = float.MaxValue;
                    for (int f = 0; f <= nf; f += 2) { if (rig != null) rig.localPosition = rigP0; src.SampleAnimation(go, srcAt(Mathf.Min(f / 30f, Lout))); for (int i = 0; i < 4; i++) groundY = Mathf.Min(groundY, legs[i][3].position.y); }
                    var SH = new Vector3[4, nf + 1]; var maxD = new float[4];
                    for (int f = 0; f <= nf; f++) { pose(f); for (int i = 0; i < 4; i++) { var tp = legs[i][3].position; maxD[i] = Mathf.Max(maxD[i], (tp - legs[i][0].position).magnitude); tp.y = Mathf.Max(tp.y, groundY); T[i, f] = tp; SH[i, f] = legs[i][0].position; } }
                    for (int i = 0; i < 4; i++)
                    {
                        float lo = float.MaxValue, hi = float.MinValue; for (int f = 0; f <= nf; f++) { lo = Mathf.Min(lo, T[i, f].y); hi = Mathf.Max(hi, T[i, f].y); }
                        for (int f = 0; f <= nf; f++) contact[i, f] = plantAll ? plantMask[i] : T[i, f].y <= lo + (hi - lo) * 0.18f;
                        // 11-19: a short lift in the middle of a stance (the front paws rose ~0.3 m and touched down twice) is not a
                        // step: close gaps under 10 frames so the paw stays planted and only real swings get the strike timing
                        for (int f = 1; f <= nf; f++)
                        {
                            if (contact[i, f] || !contact[i, f - 1]) continue;
                            int g = f; while (g <= nf && !contact[i, g]) g++;
                            if (g <= nf && g - f < 10) for (int q = f; q < g; q++) contact[i, q] = true;
                            f = g;
                        }
                        // 11-19 Kido ("발이 내려치는 게 이상해, 아장아장"): a heavy paw swings low and long, hangs forward, then comes
                        // down late and fast, slapping the ground. Re-time the height of each swing: lift lower (x(1-0.35 strike)), keep
                        // the paw up until 82 % of the swing, then drop it with an accelerating fall into the touchdown.
                        if (strike > 0)
                            for (int f0s = 0; f0s <= nf; f0s++)
                            {
                                if (contact[i, f0s] || (f0s > 0 && !contact[i, f0s - 1])) continue;   // swing starts
                                int f1s = f0s; while (f1s + 1 <= nf && !contact[i, f1s + 1]) f1s++;
                                int len = f1s - f0s + 1; if (len < 3) continue;
                                var hSrc = new float[len]; for (int q = 0; q < len; q++) hSrc[q] = T[i, f0s + q].y - lo;
                                float hMax = 0; int qPeak = 0; for (int q = 0; q < len; q++) if (hSrc[q] > hMax) { hMax = hSrc[q]; qPeak = q; }
                                for (int q = 0; q < len; q++)
                                {
                                    float u = (q + 1) / (float)(len + 1);
                                    float hs = u < 0.82f ? (q <= qPeak ? hSrc[q] : Mathf.Lerp(hSrc[q], hMax, strike)) * (1 - 0.35f * strike) : 0f;   // past the peak the paw stays up (hangs forward)
                                    if (u >= 0.82f)
                                    {
                                        float holdH = Mathf.Lerp(hSrc[Mathf.Clamp(Mathf.RoundToInt(0.82f * (len + 1)) - 1, 0, len - 1)], hMax, strike) * (1 - 0.35f * strike);
                                        float kk = (u - 0.82f) / 0.18f; hs = holdH * (1 - Mathf.Pow(kk, 3.5f));
                                        hs = Mathf.Lerp(hSrc[q], hs, strike);
                                    }
                                    var tp = T[i, f0s + q]; tp.y = lo + Mathf.Max(0f, hs); T[i, f0s + q] = tp;
                                }
                                f0s = f1s;
                            }
                        // 11-19 (front-left 28 deg snap): the strike dropped the paw 0.8 m in the single frame before touch-down and
                        // pinned it at the ground. Cap the fall speed so the slap spreads over the last few frames of the swing.
                        const float maxFall = 0.3f;
                        for (int a0 = 1; a0 <= nf; a0++)
                        {
                            if (!contact[i, a0] || contact[i, a0 - 1]) continue;
                            for (int k = a0 - 1, n = 1; k >= 0 && !contact[i, k] && n <= 8; k--, n++)
                            { var tp = T[i, k]; tp.y = Mathf.Min(tp.y, groundY + maxFall * n); T[i, k] = tp; }
                        }
                    }
                    float vPrev = 0;
                    for (int f = 1; f <= nf; f++)
                    {
                        float vs = 0; int vn = 0;
                        for (int i = 0; i < 4; i++) if (contact[i, f] && contact[i, f - 1]) { vs += -Vector3.Dot(T[i, f] - T[i, f - 1], fwd); vn++; }
                        float step = vn > 0 ? Mathf.Max(0f, vs / vn) : vPrev; vPrev = step;
                        travel[f] = travel[f - 1] + step;
                    }
                    {   // 11-19 Kido ("한 다리 가고 몸이 잠깐 멈춘 듯한 동작"): the planted paws of the source slowed the body to 20 % of its
                        // speed twice a cycle, then it lurched on. Keep only 35 % of the speed variation, smoothed over 5 frames; the pins
                        // follow this travel, so the planted paws still stay put (the IK legs absorb the difference)
                        var sp = new float[nf + 1]; float mean = travel[nf] / nf;
                        for (int f = 1; f <= nf; f++) sp[f] = mean + 0.35f * ((travel[f] - travel[f - 1]) - mean);
                        var sm = new float[nf + 1];
                        for (int f = 1; f <= nf; f++) { float acc2 = 0; for (int k = -2; k <= 2; k++) { int g = ((f - 1 + k) % nf + nf) % nf + 1; acc2 += sp[g]; } sm[f] = acc2 / 5f; }
                        float minS = float.MaxValue, maxS = 0;
                        for (int f = 1; f <= nf; f++) { travel[f] = travel[f - 1] + sm[f]; minS = Mathf.Min(minS, sm[f]); maxS = Mathf.Max(maxS, sm[f]); }
                        sb.AppendLine($"body speed per frame after evening: {minS * 30:F1}..{maxS * 30:F1} m/s");
                        // 11-19 Kido ("소리 지를 때 앞발 미끄러지는 게 이상해"): a clip that stays on the spot (howl): all four paws are planted for
                        // the whole clip and the body does not travel, so the paws cannot slide
                        if (plantAll) { for (int f = 0; f <= nf; f++) travel[f] = 0f; sb.AppendLine("plantAll: four paws pinned for the whole clip, no travel"); }
                    }
                    // slide before the fix: how far a planted paw moves over the ground while it is down
                    float slideBefore = 0, pullMax = 0;
                    for (int i = 0; i < 4; i++)
                    {
                        int f = 0;
                        // 11-19: a stance that runs over the loop seam (planted at the clip's end and start) is one stance; it used to be
                        // pinned twice, so the paw jumped 0.36 m on the ground every cycle. Skip the leading part here and finish it from
                        // the trailing part.
                        int lead = -1; if (contact[i, 0] && contact[i, nf]) { lead = 0; while (lead + 1 <= nf && contact[i, lead + 1]) lead++; }
                        while (f <= nf)
                        {
                            if (!contact[i, f]) { f++; continue; }
                            if (f == 0 && lead >= 0 && lead < nf) { f = lead + 1; continue; }
                            int a0 = f; while (f + 1 <= nf && contact[i, f + 1]) f++;
                            float mn = float.MaxValue, mx = float.MinValue;
                            for (int k = a0; k <= f; k++) { float w = Vector3.Dot(T[i, k], fwd) + travel[k]; mn = Mathf.Min(mn, w); mx = Mathf.Max(mx, w); }
                            slideBefore = Mathf.Max(slideBefore, mx - mn);
                            // pin: world position fixed at the touch-down point for the whole stance, eased back to the source path after lift-off
                            // the touch-down point must be within reach of the shoulder/hip at touch-down (it was at 112 %, which snapped
                            // the leg straight); pull it back toward the body and ease the last swing frames onto it
                            float fwdTD = Vector3.Dot(T[i, a0], fwd), latTD = Vector3.Dot(T[i, a0], latDir), pulled = 0;
                            for (int it = 0; it < 60; it++)
                            {
                                var P = new Vector3(0, groundY, 0) + fwd * fwdTD + latDir * latTD;
                                if ((P - SH[i, a0]).magnitude <= 0.96f * maxD[i]) break;
                                float dir = Vector3.Dot(P - SH[i, a0], fwd) > 0 ? -1f : 1f; fwdTD += dir * 0.05f; pulled += 0.05f;
                            }
                            if (pulled > 0)
                            {
                                var shift = fwd * (fwdTD - Vector3.Dot(T[i, a0], fwd));
                                for (int k = 1; k <= 5 && a0 - k >= 0 && !contact[i, a0 - k]; k++) { pinned[i, a0 - k] = true; target[i, a0 - k] = T[i, a0 - k] + shift * (1f - k / 6f); }
                                pullMax = Mathf.Max(pullMax, pulled);
                            }
                            for (int k = a0; k <= f; k++) { pinned[i, k] = true; target[i, k] = new Vector3(0, groundY, 0) + fwd * (fwdTD - (travel[k] - travel[a0])) + latDir * latTD; }
                            int end = f;
                            if (f == nf && lead >= 0 && lead < nf && a0 > 0)
                            {   // carry the stance on through the loop seam: frame nf and frame 0 are the same moment
                                float tr = travel[nf] - travel[a0];
                                for (int k = 0; k <= lead; k++) { pinned[i, k] = true; target[i, k] = new Vector3(0, groundY, 0) + fwd * (fwdTD - (tr + travel[k])) + latDir * latTD; }
                                end = lead;
                            }
                            // lift-off: ease back to the source path over 5 frames, and peel the paw up off the ground at once (it slid
                            // forward along the floor for a frame before rising)
                            var off = target[i, end] - T[i, end]; const int ease = 5;
                            for (int k = 1; k <= ease && end + k <= nf && !contact[i, end + k]; k++)
                            {
                                pinned[i, end + k] = true; var tg = T[i, end + k] + off * (1f - k / (float)(ease + 1));
                                tg.y = Mathf.Max(tg.y, groundY + 0.12f * k); target[i, end + k] = tg;
                            }
                            f++;
                        }
                    }
                    sb.AppendLine($"paw slide while planted, before pinning: {slideBefore:F2} m; travel per cycle {travel[nf]:F2} m; touch-down pulled back up to {pullMax:F2} m");
                }

                var bones = flexBones; int nb = bones.Length; var qk = new Keyframe[nb, 4][];
                for (int j = 0; j < nb; j++) for (int c = 0; c < 4; c++) qk[j, c] = new Keyframe[nf + 1];
                var prevQ = new Quaternion[nb]; var after = new Vector3[4, nf + 1];
                // which way each knee/elbow bends in the source (sampled at the rest of the clip's first frame), kept fixed
                var kneeSide = new float[4]; pose(0);
                for (int i = 0; i < legs.Length; i++) kneeSide[i] = KneeSide(legs[i][0], legs[i][1], legs[i][2], latDir);
                int kneeFlipsSrc = 0; var kprev = (float[])kneeSide.Clone();
                // 11-19 Kido ("다리 흔들거리는 것부터 잡어"): per-frame record of each leg's joints for a jitter report
                var jp = new Vector3[4, 3, nf + 1]; var jr = new Quaternion[4, 3, nf + 1]; var reach = new float[4, nf + 1]; var plantPawRot = new Quaternion[4]; var rigLP = new Vector3[nf + 1]; var hipsLP = new Vector3[nf + 1]; var chestRot = new Quaternion[nf + 1]; var hipsY = new float[nf + 1]; var chestY = new float[nf + 1]; var dipY = new float[nf + 1]; var jr0 = new Quaternion[4, 3, nf + 1];
                for (int f = 0; f <= nf; f++)
                {
                    float to = Mathf.Min(f / 30f, Lout);
                    pose(f);
                    if (rig != null && drop > 0) rig.localPosition = rigP0 + Vector3.down * drop * dipAt(to);   // the body dips; pinned paws stay on the ground
                    if (plantAll && doPin && rig != null)
                    {   // when the chest rears up further than the planted legs can reach, the body comes down until they can
                        for (int it = 0; it < 80; it++)
                        {
                            bool over = false;
                            for (int i = 0; i < 4 && !over; i++)
                            {
                                if (!plantMask[i] || !pinned[i, f]) continue;
                                float full = (legs[i][1].position - legs[i][0].position).magnitude + (legs[i][2].position - legs[i][1].position).magnitude;
                                var pawT = target[i, f] - (legs[i][3].position - legs[i][2].position);
                                if ((pawT - legs[i][0].position).magnitude > full * 0.96f) over = true;
                            }
                            if (!over) break;
                            rig.position += Vector3.down * 0.05f;
                        }
                    }
                    rigLP[f] = rig != null ? rig.localPosition : Vector3.zero; hipsLP[f] = hipsT ? hipsT.localPosition : Vector3.zero; chestRot[f] = sp3 ? sp3.rotation : Quaternion.identity;
                    hipsY[f] = hipsT.position.y; chestY[f] = sp3 ? sp3.position.y : 0; dipY[f] = drop * dipAt(to);
                    for (int i = 0; i < legs.Length; i++) { float ks = KneeSide(legs[i][0], legs[i][1], legs[i][2], latDir); if (ks != kneeSide[i]) kneeFlipsSrc++; }
                    for (int i = 0; i < legs.Length; i++) for (int j = 0; j < 3; j++) jr0[i, j, f] = legs[i][j].rotation;
                    if (doPin)
                        for (int i = 0; i < 4; i++)
                        {
                            var tgt = pinned[i, f] ? target[i, f] : T[i, f];          // unpinned paws also keep their height while the body dips
                            // a planted paw does not turn on the ground: it keeps the rotation it had on the first frame (the howl's shudder
                            // turned it every frame, which swung the toes about)
                            if (plantAll && plantMask[i]) { if (f == 0) plantPawRot[i] = legs[i][2].rotation; legs[i][2].rotation = plantPawRot[i]; }
                            var pawRot = legs[i][2].rotation; var toeOff = legs[i][3].position - legs[i][2].position;   // with the paw's final rotation
                            // 11-19 Kido ("흔들거리는 것"): the source rolls the forearm 10-30 deg per frame around its own axis while the
                            // leg barely moves (swing 2-4 deg) - that roll was the wobble. Start the IK from the rest twist instead, so the
                            // upper and lower leg only swing; the paw keeps its own world rotation below.
                            legs[i][0].localRotation = restUpper[i]; legs[i][1].localRotation = restLower[i];
                            { float l1 = (legs[i][1].position - legs[i][0].position).magnitude, l2 = (legs[i][2].position - legs[i][1].position).magnitude; reach[i, f] = (legs[i][2].position + (tgt - legs[i][3].position) - legs[i][0].position).magnitude / (l1 + l2); }
                            // 11-19 Kido ("다리가 앞으로 갈 때 완전 쭉 펴고 가, 강아지류는 안 그래"): a swinging leg keeps its elbow/knee bent - soft cap
                            // at 80 % reach (never past 90 %); planted legs may straighten to 99.7 %
                            // the cap eases between the two over 4 frames on each side of a contact, or the leg snapped at lift-off
                            int dc = 99; for (int k = -4; k <= 4; k++) { int g = f + k; if (g >= 0 && g <= nf && contact[i, g]) dc = Mathf.Min(dc, Mathf.Abs(k)); }
                            float wc = Mathf.SmoothStep(0f, 1f, 1f - dc / 5f);
                            if (plantAll) wc = 1f;   // an attack clip: the free paw keeps its full reach
                            SolveLeg(legs[i][0], legs[i][1], legs[i][2], tgt - toeOff, latDir, kneeSide[i], Mathf.Lerp(0.8f, 0.985f, wc), Mathf.Lerp(0.1f, 0.012f, wc));
                            legs[i][2].rotation = pawRot;
                            after[i, f] = legs[i][3].position;
                        }
                    for (int i = 0; i < legs.Length; i++) for (int j = 0; j < 3; j++) { jp[i, j, f] = legs[i][j + 1].position; jr[i, j, f] = legs[i][j].rotation; }
                    for (int j = 0; j < nb; j++)
                    {
                        var q = bones[j].localRotation;
                        if (f > 0 && Quaternion.Dot(q, prevQ[j]) < 0) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                        prevQ[j] = q;
                        qk[j, 0][f] = new Keyframe(to, q.x); qk[j, 1][f] = new Keyframe(to, q.y); qk[j, 2][f] = new Keyframe(to, q.z); qk[j, 3][f] = new Keyframe(to, q.w);
                    }
                }
                if (plantAll && sp3) { float mx = 0, sum = 0; for (int f = 1; f <= nf; f++) { float d = Quaternion.Angle(chestRot[f - 1], chestRot[f]); mx = Mathf.Max(mx, d); sum += d; } sb.AppendLine($"CHEST turn per frame after steadying: mean {sum / nf:F2} max {mx:F2} deg (window +-{trunkWin}, keep {trunkKeep})"); }
                sb.AppendLine("HIPS_Y " + string.Join(" ", hipsY.Select(v => v.ToString("F2"))));
                sb.AppendLine("CHEST_Y " + string.Join(" ", chestY.Select(v => v.ToString("F2"))));
                sb.AppendLine("DIP " + string.Join(" ", dipY.Select(v => v.ToString("F2"))));
                {   // jitter report: second difference of knee/paw/toe positions (cm/frame^2) and frame-to-frame turn of each bone (deg)
                    string[] jn = { "knee", "paw", "toe" }, bn = { "upper", "lower", "paw" };
                    for (int i = 0; i < legs.Length; i++)
                    {
                        var line = new System.Text.StringBuilder($"JITTER {legs[i][3].name}:");
                        for (int j = 0; j < 3; j++)
                        {
                            float mx = 0; int at = 0; var acc = new List<float>();
                            for (int f = 1; f < nf; f++) { float aa = (jp[i, j, f + 1] - 2 * jp[i, j, f] + jp[i, j, f - 1]).magnitude * 100f; acc.Add(aa); if (aa > mx) { mx = aa; at = f; } }
                            acc.Sort(); line.Append($" {jn[j]} acc med {acc[acc.Count / 2]:F1} max {mx:F1}@{at}");
                        }
                        for (int j = 0; j < 3; j++)
                        {
                            float mx = 0; int at = 0; var tr = new List<float>();
                            for (int f = 1; f <= nf; f++) { float d = Quaternion.Angle(jr[i, j, f - 1], jr[i, j, f]); tr.Add(d); if (d > mx) { mx = d; at = f; } }
                            // a wobble = the turn reverses direction quickly; count frames whose turn is > 2.5x the median of its neighbours
                            int spikes = 0; for (int f = 2; f < nf; f++) { float m = (Quaternion.Angle(jr[i, j, f - 2], jr[i, j, f - 1]) + Quaternion.Angle(jr[i, j, f], jr[i, j, f + 1])) * 0.5f + 0.3f; if (Quaternion.Angle(jr[i, j, f - 1], jr[i, j, f]) > 2.5f * m) spikes++; }
                            tr.Sort(); line.Append($" | {bn[j]} turn med {tr[tr.Count / 2]:F1} max {mx:F1}@{at} spikes {spikes}");
                        }
                        sb.AppendLine(line.ToString());
                        for (int f = 1; f <= nf; f++) for (int j = 0; j < 3; j++) { float d = Quaternion.Angle(jr[i, j, f - 1], jr[i, j, f]); if (d > 12f) sb.AppendLine($"   spike {bn[j]} f{f} {d:F0}deg src {Quaternion.Angle(jr0[i, j, f - 1], jr0[i, j, f]):F0}deg pinned {(doPin && pinned[i, f] ? 1 : 0)} contact {(doPin && contact[i, f] ? 1 : 0)} reach {reach[i, f - 1]:F3}->{reach[i, f]:F3} lowerSwing {Vector3.Angle(jp[i, 1, f - 1] - jp[i, 0, f - 1], jp[i, 1, f] - jp[i, 0, f]):F0}deg"); }
                    }
                }
                // light cyclic [1 2 1] pass over the IK result of the leg bones: removes single-frame snaps where a paw is pinned
                // just past full reach (touch-down frames)
                for (int j = 0; j < nb; j++)
                {
                    if (!isLeg(bones[j].name)) continue;
                    if (plantAll) continue;   // pinned clips keep the exact IK result
                    var orig = new Vector4[nf + 1];
                    for (int f = 0; f <= nf; f++) orig[f] = new Vector4(qk[j, 0][f].value, qk[j, 1][f].value, qk[j, 2][f].value, qk[j, 3][f].value);
                    for (int f = 0; f <= nf; f++)
                    {
                        var pa = orig[(f - 1 + nf) % nf]; var pb = orig[(f + 1) % nf]; var pc = orig[f];
                        if (Vector4.Dot(pa, pc) < 0) pa = -pa; if (Vector4.Dot(pb, pc) < 0) pb = -pb;
                        var m = (pa + 2 * pc + pb).normalized;
                        for (int c = 0; c < 4; c++) qk[j, c][f].value = m[c];
                    }
                }
                if (plantAll && rig != null)
                {   // the drift correction lives on the armature's position
                    string rpath = AnimationUtility.CalculateTransformPath(rig, go.transform);
                    dst.SetCurve(rpath, typeof(Transform), "m_LocalPosition.x", new AnimationCurve(Enumerable.Range(0, nf + 1).Select(f => new Keyframe(Mathf.Min(f / 30f, Lout), rigLP[f].x)).ToArray()));
                    dst.SetCurve(rpath, typeof(Transform), "m_LocalPosition.y", new AnimationCurve(Enumerable.Range(0, nf + 1).Select(f => new Keyframe(Mathf.Min(f / 30f, Lout), rigLP[f].y)).ToArray()));
                    dst.SetCurve(rpath, typeof(Transform), "m_LocalPosition.z", new AnimationCurve(Enumerable.Range(0, nf + 1).Select(f => new Keyframe(Mathf.Min(f / 30f, Lout), rigLP[f].z)).ToArray()));
                    if (hipsT)
                    {
                        string hpath = AnimationUtility.CalculateTransformPath(hipsT, go.transform);
                        dst.SetCurve(hpath, typeof(Transform), "m_LocalPosition.x", new AnimationCurve(Enumerable.Range(0, nf + 1).Select(f => new Keyframe(Mathf.Min(f / 30f, Lout), hipsLP[f].x)).ToArray()));
                        dst.SetCurve(hpath, typeof(Transform), "m_LocalPosition.y", new AnimationCurve(Enumerable.Range(0, nf + 1).Select(f => new Keyframe(Mathf.Min(f / 30f, Lout), hipsLP[f].y)).ToArray()));
                        dst.SetCurve(hpath, typeof(Transform), "m_LocalPosition.z", new AnimationCurve(Enumerable.Range(0, nf + 1).Select(f => new Keyframe(Mathf.Min(f / 30f, Lout), hipsLP[f].z)).ToArray()));
                    }
                }
                string[] comp = { "x", "y", "z", "w" };
                for (int j = 0; j < nb; j++) for (int c = 0; c < 4; c++) dst.SetCurve(flexPaths[j], typeof(Transform), "m_LocalRotation." + comp[c], new AnimationCurve(qk[j, c]));
                sb.AppendLine($"spine flex {flexDeg} deg, roll {rollDeg} deg, knees in {narrow} (hind gather {gMin:F2}..{gMax:F2})");
                if (doPin)
                {
                    float slideAfter = 0;
                    for (int i = 0; i < 4; i++)
                    {
                        int f = 0;
                        while (f <= nf)
                        {
                            if (!contact[i, f]) { f++; continue; }
                            int a0 = f; while (f + 1 <= nf && contact[i, f + 1]) f++;
                            float mn = float.MaxValue, mx = float.MinValue;
                            for (int k = a0; k <= f; k++) { float w = Vector3.Dot(after[i, k], fwd) + travel[k]; mn = Mathf.Min(mn, w); mx = Mathf.Max(mx, w); }
                            slideAfter = Mathf.Max(slideAfter, mx - mn); f++;
                        }
                    }
                    sb.AppendLine($"paw slide while planted, after pinning: {slideAfter:F3} m");
                    sb.AppendLine($"knee/elbow frames bent the wrong way before IK: {kneeFlipsSrc} (IK keeps them on the source side)");
                    // the distance the body must travel at each frame so the pinned paws stay put (used by captures and by the game mover)
                    var ci2 = System.Globalization.CultureInfo.InvariantCulture;
                    File.WriteAllLines(outAnim + ".travel.txt", new[] { Lout.ToString("F4", ci2) }.Concat(Enumerable.Range(0, nf + 1).Select(f => travel[f].ToString("F4", ci2))).ToArray());
                }
            }

            // 4) the thump: the armature (child of the boss root, so its local Y is up) dips on each footfall
            if (rig != null && drop > 0)
            {
                string path = AnimationUtility.CalculateTransformPath(rig, go.transform);
                var ky = new Keyframe[nf + 1]; var kx = new Keyframe[nf + 1]; var kz = new Keyframe[nf + 1];
                for (int f = 0; f <= nf; f++)
                {
                    float to = Mathf.Min(f / 30f, Lout);
                    ky[f] = new Keyframe(to, rigP0.y - drop * dipAt(to)); kx[f] = new Keyframe(to, rigP0.x); kz[f] = new Keyframe(to, rigP0.z);
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
