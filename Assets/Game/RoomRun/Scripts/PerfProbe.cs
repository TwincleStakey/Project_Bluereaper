// Frame-cost probe (Kido 11-10: "200 fps on a 4060"). For each field: uncapped frame rate, average and 1%-low frame time,
// CPU main / render thread and GPU time (FrameTimingManager), draw calls, batches, SetPass calls, triangles; then the same
// with one system switched off at a time, so the cost of each shows as the difference:
//   shadows    - all lights cast no shadows
//   post       - the game camera's post-processing off
//   foliage    - the instanced vegetation renderers (ReedInstancer, LookStudyInstancer) off
//   cams       - every camera except the game camera off (ripple, trail, capture cameras)
//   lights     - all point/spot lights off
// Results: Application.persistentDataPath/rr_perf.csv (+ the log). Started by the editor remote ("perf <rooms>") or in a
// player build by the command line: -rrperf grass,valley,...  (the build quits when done).
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace RoomRun
{
    public sealed class PerfProbe : MonoBehaviour
    {
        public string[] rooms;
        public string outFile;
        public System.Action<string> Note;
        public System.Action Finished;
        public float settle = 1.0f, sample = 3.0f;

        static readonly string[] DefaultVariants = { "base", "casc2", "shadows", "post", "ssao", "foliage", "cams", "lights" };
        // Optional: run these instead ("off:TypeName" switches off every component of that type).
        public string[] variants;

        // Stage objects are often DontSave, which FindObjectsByType skips: walk every loaded scene (and the run's own objects).
        static List<T> All<T>(RunDirector run) where T : Component
        {
            var l = new List<T>();
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                foreach (var root in UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).GetRootGameObjects()) l.AddRange(root.GetComponentsInChildren<T>());
            l.AddRange(run.GetComponentsInChildren<T>());
            return l;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void FromCommandLine()
        {
            if (Application.isEditor) return;
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-rrperf");
            if (i < 0 || i + 1 >= args.Length) return;
            var go = new GameObject("PerfProbe");
            DontDestroyOnLoad(go);
            var p = go.AddComponent<PerfProbe>();
            p.rooms = args[i + 1].Split(',');
            p.outFile = Path.Combine(Application.persistentDataPath, "rr_perf.csv");
            p.Note = s => Debug.Log("[Perf] " + s);
            p.Finished = Application.Quit;
        }

        IEnumerator Start()
        {
            while (!RunDirector.Instance || RunDirector.Instance.Transitioning || RunDirector.Instance.Room == null) yield return null;
            var run = RunDirector.Instance;
            run.Player.DebugInvulnerable = true;
            int keepVsync = QualitySettings.vSyncCount, keepTarget = Application.targetFrameRate;
            QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;

            var sb = new StringBuilder("room,variant,fps_avg,ms_avg,ms_1pct_low,cpu_main_ms,cpu_render_ms,gpu_ms,draw_calls,batches,setpass,tris_k,cameras,lights\n");
            var rDraw = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            var rBatch = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            var rSet = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            var rTri = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            var timings = new FrameTiming[1];
            // main-thread markers, averaged over the "base" sample of each field
            string[] markerNames = { "Update.ScriptRunBehaviourUpdate", "PreLateUpdate.ScriptRunBehaviourLateUpdate", "FixedUpdate.PhysicsFixedUpdate",
                "PreUpdate.PhysicsUpdate", "PostLateUpdate.FinishFrameRendering", "PostLateUpdate.UpdateAllRenderers", "PreLateUpdate.ParticleSystemBeginUpdateAll",
                "PostLateUpdate.ParticleSystemEndUpdateAll", "PostLateUpdate.UpdateAllSkinnedMeshes", "Update.DirectorUpdate", "PostLateUpdate.PlayerSendFrameComplete",
                "PreLateUpdate.DirectorUpdateAnimationBegin", "Update.ScriptRunDelayedDynamicFrameRate", "PostLateUpdate.UpdateAudio", "EditorLoop" };
            var markers = new ProfilerRecorder[markerNames.Length];
            for (int i = 0; i < markers.Length; i++) markers[i] = ProfilerRecorder.StartNew(ProfilerCategory.Internal, markerNames[i]);

            foreach (var room in rooms)
            {
                if (string.IsNullOrEmpty(room)) continue;
                if (run.Room.id != room) run.DevJump(room);
                for (int f = 0; f < 10 && !run.Transitioning; f++) yield return null;
                while (run.Transitioning) yield return null;
                yield return new WaitForSeconds(2f);
                foreach (var v in (variants != null && variants.Length > 0 ? variants : DefaultVariants))
                {
                    var undo = Apply(v, run);
                    yield return new WaitForSeconds(settle);
                    var ms = new List<float>(); double cpuM = 0, cpuR = 0, gpu = 0; int tn = 0;
                    long dc = 0, bt = 0, sp = 0, tr = 0; int sn = 0;
                    var mk = new double[markers.Length];
                    float t0 = Time.realtimeSinceStartup;
                    while (Time.realtimeSinceStartup - t0 < sample)
                    {
                        yield return null;
                        ms.Add(Time.unscaledDeltaTime * 1000f);
                        FrameTimingManager.CaptureFrameTimings();
                        if (FrameTimingManager.GetLatestTimings(1, timings) > 0)
                        { cpuM += timings[0].cpuMainThreadFrameTime; cpuR += timings[0].cpuRenderThreadFrameTime; gpu += timings[0].gpuFrameTime; tn++; }
                        dc += rDraw.LastValue; bt += rBatch.LastValue; sp += rSet.LastValue; tr += rTri.LastValue; sn++;
                        for (int i = 0; i < markers.Length; i++) mk[i] += markers[i].LastValue * 1e-6;
                    }
                    undo?.Invoke();
                    ms.Sort();
                    float avg = ms.Average(), low = ms[Mathf.Clamp(Mathf.FloorToInt(ms.Count * 0.99f), 0, ms.Count - 1)];
                    int cams = All<Camera>(run).Count(c => c.isActiveAndEnabled);
                    int lights = All<Light>(run).Count(l => l.isActiveAndEnabled);
                    string line = $"{room},{v},{1000f / avg:0},{avg:0.00},{low:0.00},{(tn > 0 ? cpuM / tn : 0):0.00},{(tn > 0 ? cpuR / tn : 0):0.00},{(tn > 0 ? gpu / tn : 0):0.00},{dc / Mathf.Max(sn, 1)},{bt / Mathf.Max(sn, 1)},{sp / Mathf.Max(sn, 1)},{tr / Mathf.Max(sn, 1) / 1000},{cams},{lights}";
                    sb.AppendLine(line);
                    Note?.Invoke(line);
                    if (v == "base")
                    {
                        var parts = new List<string>();
                        for (int i = 0; i < markers.Length; i++) if (mk[i] / Mathf.Max(sn, 1) >= 0.05) parts.Add($"{markerNames[i]} {mk[i] / Mathf.Max(sn, 1):0.00}ms");
                        Note?.Invoke($"{room} main thread: " + string.Join(", ", parts));
                    }
                }
            }
            rDraw.Dispose(); rBatch.Dispose(); rSet.Dispose(); rTri.Dispose();
            foreach (var m in markers) m.Dispose();
            QualitySettings.vSyncCount = keepVsync; Application.targetFrameRate = keepTarget;
            if (!string.IsNullOrEmpty(outFile)) File.WriteAllText(outFile, sb.ToString());
            Finished?.Invoke();
            Destroy(this);
        }

        // Switches one system off; returns how to switch it back.
        static System.Action Apply(string v, RunDirector run)
        {
            if (v.StartsWith("off:"))
            {
                string type = v.Substring(4);
                var bs = All<MonoBehaviour>(run).Where(b => b.enabled && b.GetType().Name == type).ToList();
                foreach (var b in bs) b.enabled = false;
                return () => { foreach (var b in bs) if (b) b.enabled = true; };
            }
            switch (v)
            {
                case "casc2":
                {
                    // the audit's shadow setup: 2 cascades over 38 m, split at 0.55 (the camera sees ground at 19-33 m)
                    var a = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                    if (!a) return null;
                    int c0 = a.shadowCascadeCount; float d0 = a.shadowDistance, s0 = a.cascade2Split;
                    a.shadowCascadeCount = 2; a.shadowDistance = 38f; a.cascade2Split = 0.55f;
                    return () => { a.shadowCascadeCount = c0; a.shadowDistance = d0; a.cascade2Split = s0; };
                }
                case "shadows":
                {
                    var ls = All<Light>(run).Where(l => l.shadows != LightShadows.None).ToList();
                    var keep = ls.Select(l => l.shadows).ToList();
                    foreach (var l in ls) l.shadows = LightShadows.None;
                    return () => { for (int i = 0; i < ls.Count; i++) if (ls[i]) ls[i].shadows = keep[i]; };
                }
                case "post":
                {
                    var d = run.Rig.Cam.GetComponent<UniversalAdditionalCameraData>();
                    if (!d) return null;
                    bool k = d.renderPostProcessing; d.renderPostProcessing = false;
                    return () => d.renderPostProcessing = k;
                }
                case "ssao":
                {
                    // the renderer's ambient occlusion feature (it adds a depth-normals pass over all opaque geometry)
                    var asset = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                    var f = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var datas = asset && f != null ? f.GetValue(asset) as ScriptableRendererData[] : null;
                    var off = new List<ScriptableRendererFeature>();
                    if (datas != null)
                        foreach (var d in datas)
                            if (d) foreach (var feat in d.rendererFeatures)
                                if (feat && feat.isActive && feat.GetType().Name.Contains("AmbientOcclusion")) { feat.SetActive(false); off.Add(feat); }
                    return () => { foreach (var feat in off) feat.SetActive(true); };
                }
                case "foliage":
                {
                    var bs = All<MonoBehaviour>(run)
                        .Where(b => b.enabled && (b.GetType().Name == "ReedInstancer" || b.GetType().Name == "LookStudyInstancer")).ToList();
                    foreach (var b in bs) b.enabled = false;
                    return () => { foreach (var b in bs) if (b) b.enabled = true; };
                }
                case "cams":
                {
                    var cs = All<Camera>(run).Where(c => c.enabled && c != run.Rig.Cam).ToList();
                    foreach (var c in cs) c.enabled = false;
                    return () => { foreach (var c in cs) if (c) c.enabled = true; };
                }
                case "lights":
                {
                    var ls = All<Light>(run).Where(l => l.enabled && l.type != LightType.Directional).ToList();
                    foreach (var l in ls) l.enabled = false;
                    return () => { foreach (var l in ls) if (l) l.enabled = true; };
                }
            }
            return null;
        }
    }
}
