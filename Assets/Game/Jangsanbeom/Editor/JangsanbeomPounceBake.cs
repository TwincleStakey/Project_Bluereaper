#if UNITY_EDITOR
// 장산범 v2 pounce poses (11-19 Kido: "앞은 바짝 엎드리고 엉덩이 든 사냥 전 포즈에서 달려드는 몸통박치기"). Bakes two clips from
// the standing pose with paw IK, no hand keyframing:
//   jsb2_crouch.anim : stand -> play bow (chest and elbows on the ground, rump high, tail up), then a rump wiggle while it waits
//   jsb2_dash.anim   : play bow -> launched flat and stretched (front paws reaching, hind legs trailing), held with a shudder
// Reference: the dog play bow (Wikimedia Commons photos) and the rump wiggle of a cat before it pounces.
// Trigger: Temp/jsb_pounce.trigger (any text) -> Temp/jsb_pounce.done
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class JangsanbeomPounceBake
{
    const string Root = "Assets/Game/Jangsanbeom/V2";
    static JangsanbeomPounceBake() { EditorApplication.update += Tick; }
    static void Tick()
    {
        // 11-20: rebuild the boss arena (BSBuilder.Build opens a new scene) and put the editor's scenes back as they were
        const string arenaTrig = "Temp/jsb_arena.trigger";
        if (File.Exists(arenaTrig) && !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && !EditorApplication.isUpdating)
        {
            File.Delete(arenaTrig);
            string alog;
            try
            {
                var setup = UnityEditor.SceneManagement.EditorSceneManager.GetSceneManagerSetup();
                bool dirty = false; for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++) dirty |= UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty;
                if (dirty) alog = "ABORT: an open scene has unsaved changes";
                else
                {
                    BossSeal.BSBuilder.Build();
                    if (setup != null && setup.Length > 0) UnityEditor.SceneManagement.EditorSceneManager.RestoreSceneManagerSetup(setup);
                    alog = "ARENA_OK restored " + (setup != null ? setup.Length : 0) + " scenes";
                }
            }
            catch (System.Exception e) { alog = "ERROR " + e; }
            File.WriteAllText("Temp/jsb_arena.done", alog);
            return;
        }
        const string trig = "Temp/jsb_pounce.trigger";
        if (!File.Exists(trig) || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        File.Delete(trig);
        string log; try { log = Bake(); } catch (System.Exception e) { log = "ERROR " + e; }
        File.WriteAllText("Temp/jsb_pounce.done", log);
    }

    static string Bake()
    {
        var sb = new System.Text.StringBuilder();
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Models/jsb2_model.fbx");
        var idle = AssetDatabase.LoadAllAssetsAtPath(Root + "/Models/jsb2_idle.fbx").OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview"));
        var go = (GameObject)Object.Instantiate(model); go.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var all = go.GetComponentsInChildren<Transform>();
            System.Func<string, Transform> B = n => all.FirstOrDefault(t => t.name == n);
            var hips = B("Hips"); var head = B("Head"); var sp1 = B("Spine1"); var sp3 = B("Spine3"); var nk = B("Neck1"); var tail = B("Tail1");
            var rig = go.transform.Cast<Transform>().First(t => t.GetComponentsInChildren<Transform>().Any(x => x.name == "Hips"));
            string[][] legNames = { new[] { "UpperArm.L", "Forearm.L", "FrontPaw.L", "FrontToes.L" }, new[] { "UpperArm.R", "Forearm.R", "FrontPaw.R", "FrontToes.R" },
                                    new[] { "Thigh.L", "Shin.L", "HindPaw.L", "HindToes.L" }, new[] { "Thigh.R", "Shin.R", "HindPaw.R", "HindToes.R" } };
            var legs = legNames.Select(l => l.Select(B).ToArray()).ToArray();
            idle.SampleAnimation(go, 0f);
            var rigP0 = rig.localPosition;
            var fwd = head.position - hips.position; fwd.y = 0; fwd.Normalize();
            var right = Vector3.Cross(Vector3.up, fwd);
            var toe0 = legs.Select(l => l[3].position).ToArray(); float ground = toe0.Min(p => p.y);
            var pawRot0 = legs.Select(l => l[2].rotation).ToArray();
            var toeOff0 = legs.Select(l => l[3].position - l[2].position).ToArray();
            var kneeSide = legs.Select(l => JangsanbeomHeavyWalk.KneeSide(l[0], l[1], l[2], right)).ToArray();
            float hipsY = hips.position.y, chestY = sp3.position.y;
            // which way round `right` lowers the chest / lifts the tail
            float sgnChest, sgnTail = 1f;
            { var r0 = sp1.rotation; sp1.rotation = Quaternion.AngleAxis(10f, right) * r0; sgnChest = sp3.position.y < chestY ? 1f : -1f; sp1.rotation = r0; }
            if (tail != null && tail.childCount > 0)
            { var c = tail.GetChild(0); float y0 = c.position.y; var r0 = tail.rotation; tail.rotation = Quaternion.AngleAxis(10f, right) * r0; sgnTail = c.position.y > y0 ? 1f : -1f; tail.rotation = r0; }
            sb.AppendLine($"stand hips {hipsY:F2} chest {chestY:F2} ground {ground:F2}; chest sign {sgnChest} tail sign {sgnTail}");

            // bow 0..1 = play bow; flat 0..1 = launched and stretched; wig = rump sway (-1..1); shake = flight shudder
            System.Action<float, float, float, float> pose = (bow, flat, wig, shake) =>
            {
                idle.SampleAnimation(go, 0f);
                float crouch = Mathf.Max(bow, flat);
                // body: the bow pitches the chest down about the pelvis and shifts the weight back; the dash drops the whole body low
                rig.localPosition = rigP0;
                rig.position += Vector3.down * (flat * (hipsY - ground) * 0.5f + bow * 1.6f) - fwd * (bow * 1.6f) + fwd * (flat * 2.5f) + right * (wig * 0.45f) + Vector3.up * (shake * 0.12f);
                float pitch = bow * 48f + flat * 6f;
                sp1.rotation = Quaternion.AngleAxis(sgnChest * pitch, right) * sp1.rotation;
                if (nk) nk.rotation = Quaternion.AngleAxis(-sgnChest * pitch * 0.75f, right) * nk.rotation;      // the eyes stay on the prey
                hips.rotation = Quaternion.AngleAxis(wig * 5f, fwd) * hips.rotation;                              // rump wiggle
                if (tail) tail.rotation = Quaternion.AngleAxis(sgnTail * (bow * 38f - flat * 10f), right) * Quaternion.AngleAxis(wig * 12f, Vector3.up) * tail.rotation;
                for (int i = 0; i < 4; i++)
                {
                    bool front = i < 2; float side = Vector3.Dot(toe0[i], right) >= 0 ? 1f : -1f;
                    var tgt = toe0[i];
                    if (front) tgt += fwd * (bow * 3.2f + flat * 6.5f) + right * side * (bow * 0.5f + flat * 1.2f) + Vector3.up * (flat * 1.4f);   // forelegs laid out in front
                    else tgt += -fwd * (flat * 6.0f) + right * side * (bow * 0.4f + flat * 1.0f) + Vector3.up * (flat * 1.8f);                       // hind legs trail in the leap
                    JangsanbeomHeavyWalk.SolveLeg(legs[i][0], legs[i][1], legs[i][2], tgt - toeOff0[i], right, kneeSide[i], Mathf.Lerp(0.985f, 0.9f, crouch), 0.03f);
                    legs[i][2].rotation = pawRot0[i];
                }
            };

            var bones = all.Where(t => t != go.transform && t != rig && t.IsChildOf(rig) && t.GetComponent<Renderer>() == null).ToArray();
            System.Func<string, float, System.Func<float, Vector4>, string> bake = (name, len, f) =>
            {
                var clip = new AnimationClip { name = name, frameRate = 30 };
                int nf = Mathf.RoundToInt(len * 30);
                var qk = new Keyframe[bones.Length, 4][]; var pk = new Keyframe[3][];
                for (int j = 0; j < bones.Length; j++) for (int c = 0; c < 4; c++) qk[j, c] = new Keyframe[nf + 1];
                for (int c = 0; c < 3; c++) pk[c] = new Keyframe[nf + 1];
                var prev = new Quaternion[bones.Length]; float chestMin = float.MaxValue, hipsMax = 0;
                for (int k = 0; k <= nf; k++)
                {
                    float t = k / 30f; var p = f(t); pose(p.x, p.y, p.z, p.w);
                    chestMin = Mathf.Min(chestMin, sp3.position.y); hipsMax = Mathf.Max(hipsMax, hips.position.y);
                    for (int j = 0; j < bones.Length; j++)
                    {
                        var q = bones[j].localRotation; if (k > 0 && Quaternion.Dot(q, prev[j]) < 0) q = new Quaternion(-q.x, -q.y, -q.z, -q.w); prev[j] = q;
                        qk[j, 0][k] = new Keyframe(t, q.x); qk[j, 1][k] = new Keyframe(t, q.y); qk[j, 2][k] = new Keyframe(t, q.z); qk[j, 3][k] = new Keyframe(t, q.w);
                    }
                    var lp = rig.localPosition; pk[0][k] = new Keyframe(t, lp.x); pk[1][k] = new Keyframe(t, lp.y); pk[2][k] = new Keyframe(t, lp.z);
                }
                string[] comp = { "x", "y", "z", "w" };
                for (int j = 0; j < bones.Length; j++)
                {
                    string path = AnimationUtility.CalculateTransformPath(bones[j], go.transform);
                    for (int c = 0; c < 4; c++) clip.SetCurve(path, typeof(Transform), "m_LocalRotation." + comp[c], new AnimationCurve(qk[j, c]));
                }
                string rp = AnimationUtility.CalculateTransformPath(rig, go.transform);
                for (int c = 0; c < 3; c++) clip.SetCurve(rp, typeof(Transform), "m_LocalPosition." + comp[c], new AnimationCurve(pk[c]));
                string outPath = Root + "/Models/" + name + ".anim";
                AssetDatabase.DeleteAsset(outPath); AssetDatabase.CreateAsset(clip, outPath);
                return $"{name}: {len:F2}s, {bones.Length} bones, chest lowest {chestMin:F2} (stand {chestY:F2}), hips highest {hipsMax:F2} (stand {hipsY:F2})";
            };
            // crouch: 0.4 s down into the bow, then waiting with the rump wiggling faster and faster
            sb.AppendLine(bake("jsb2_crouch", 1.2f, t =>
            {
                float b = Mathf.SmoothStep(0, 1, t / 0.4f);
                float w = t > 0.45f ? Mathf.Sin((t - 0.45f) * (14f + (t - 0.45f) * 16f)) * Mathf.Clamp01((t - 0.45f) / 0.2f) : 0f;
                return new Vector4(b, 0f, w, 0f);
            }));
            // dash: out of the bow in three frames, then flat and stretched with a shudder
            sb.AppendLine(bake("jsb2_dash", 0.6f, t =>
            {
                float fl = Mathf.SmoothStep(0, 1, t / 0.1f);
                return new Vector4(1f - fl, fl, 0f, Mathf.Sin(t * 70f) * fl);
            }));
            AssetDatabase.SaveAssets();
        }
        finally { Object.DestroyImmediate(go); }
        return sb.ToString();
    }
}
#endif
