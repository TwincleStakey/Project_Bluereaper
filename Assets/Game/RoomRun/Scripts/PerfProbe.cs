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
            int vi = System.Array.IndexOf(args, "-rrvariants"); // e.g. -rrvariants base,zoom:1.8,zoom:3.5
            if (vi >= 0 && vi + 1 < args.Length) p.variants = args[vi + 1].Split(',');
            p.outFile = Path.Combine(Application.persistentDataPath, "rr_perf.csv");
            p.Note = s => Debug.Log("[Perf] " + s);
            p.Finished = Application.Quit;
        }

        IEnumerator Start()
        {
            while (!RunDirector.Instance || RunDirector.Instance.Transitioning || RunDirector.Instance.Room == null) yield return null;
            var run = RunDirector.Instance;
            noteStatic = Note;
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
            markerNames = markerNames.Concat(new[] { "Enemy.Separation", "Enemy.Ground", "Enemy.Move", "Reed.Draw" }).ToArray();
            var markers = new ProfilerRecorder[markerNames.Length];
            for (int i = 0; i < markers.Length; i++)
                markers[i] = (markerNames[i].StartsWith("Enemy.") || markerNames[i].StartsWith("Reed.")) ? ProfilerRecorder.StartNew(ProfilerCategory.Scripts, markerNames[i]) : ProfilerRecorder.StartNew(ProfilerCategory.Internal, markerNames[i]);

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
                    yield return new WaitForSeconds(v.StartsWith("swarm:") ? settle + 3f : settle);
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
                    string line = $"{room},{v} [enemies {run.Enemies.Count} bullets {run.Bullets.LiveCount}],{1000f / avg:0},{avg:0.00},{low:0.00},{(tn > 0 ? cpuM / tn : 0):0.00},{(tn > 0 ? cpuR / tn : 0):0.00},{(tn > 0 ? gpu / tn : 0):0.00},{dc / Mathf.Max(sn, 1)},{bt / Mathf.Max(sn, 1)},{sp / Mathf.Max(sn, 1)},{tr / Mathf.Max(sn, 1) / 1000},{cams},{lights}";
                    sb.AppendLine(line);
                    Note?.Invoke(line);
                    if (v == "base" || v.StartsWith("swarm:"))
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
        // "gpu": same as base, plus the GPU time of every render marker (Sampler/Recorder), top 25 logged
        static System.Action<string> noteStatic;
        sealed class GpuMarkers : MonoBehaviour
        {
            readonly List<(string name, UnityEngine.Profiling.Recorder rec)> recs = new List<(string, UnityEngine.Profiling.Recorder)>();
            readonly Dictionary<string, double> sum = new Dictionary<string, double>();
            int frames;
            void OnEnable()
            {
                var names = new List<string>();
                UnityEngine.Profiling.Sampler.GetNames(names);
                foreach (var n in names)
                {
                    var s = UnityEngine.Profiling.Sampler.Get(n);
                    if (s == null || !s.isValid) continue;
                    var r = s.GetRecorder(); if (r == null || !r.isValid) continue;
                    r.enabled = true; recs.Add((n, r));
                }
            }
            void LateUpdate()
            {
                frames++;
                foreach (var (n, r) in recs) { long g = r.gpuElapsedNanoseconds; if (g > 0) sum[n] = (sum.TryGetValue(n, out var o) ? o : 0) + g * 1e-6; }
            }
            public void Report(System.Action<string> note)
            {
                foreach (var (_, r) in recs) r.enabled = false;
                var top = sum.OrderByDescending(k => k.Value).Take(30).Select(k => $"{k.Key} {k.Value / Mathf.Max(frames, 1):0.00}");
                note?.Invoke($"gpu markers ms ({frames} frames, nested markers overlap): " + string.Join(" | ", top));
            }
        }

        static System.Action Apply(string v, RunDirector run)
        {
            // "a&b" applies both
            if (v.Contains("&"))
            {
                var undos = v.Split('&').Select(p => Apply(p, run)).ToList();
                return () => { for (int i = undos.Count - 1; i >= 0; i--) undos[i]?.Invoke(); };
            }
            // "hide:SP_Ground" switches off every renderer whose object name starts with that prefix (cost of one stage layer)
            if (v.StartsWith("hide:"))
            {
                string pre = v.Substring(5);
                var rs = All<Renderer>(run).Where(r => r.enabled && r.name.StartsWith(pre)).ToList();
                foreach (var r in rs) r.enabled = false;
                noteStatic?.Invoke($"hide:{pre} -> {rs.Count} renderers");
                return () => { foreach (var r in rs) if (r) r.enabled = true; };
            }
            // "under:Border" switches off every renderer below an object of that name
            if (v.StartsWith("under:"))
            {
                string nm = v.Substring(6);
                var rs = All<Renderer>(run).Where(r => { if (!r.enabled) return false; for (var t = r.transform.parent; t; t = t.parent) if (t.name == nm) return true; return false; }).ToList();
                foreach (var r in rs) r.enabled = false;
                noteStatic?.Invoke($"under:{nm} -> {rs.Count} renderers");
                return () => { foreach (var r in rs) if (r) r.enabled = true; };
            }
            // "orphans": renderers that belong to no scene (other fields' DontSave leftovers in a shared editor) off, listed by name
            if (v == "orphans")
            {
                var rs = Resources.FindObjectsOfTypeAll<Renderer>().Where(r => r && r.enabled && r.gameObject.activeInHierarchy && !r.gameObject.scene.IsValid()).ToList();
                var cam = run.Rig.Cam; var planes = GeometryUtility.CalculateFrustumPlanes(cam);
                var groups = rs.GroupBy(r => new string(r.name.TakeWhile(ch => ch != '_' ).ToArray()) + "_" + (GeometryUtility.TestPlanesAABB(planes, r.bounds) ? "inview" : "out"))
                    .Select(g => $"{g.Key} x{g.Count()}");
                noteStatic?.Invoke($"orphans {rs.Count}: " + string.Join(", ", groups.Take(40)));
                foreach (var r in rs) r.enabled = false;
                return () => { foreach (var r in rs) if (r) r.enabled = true; };
            }
            // "scenes": loaded scenes with their visible renderer counts
            if (v == "scenes")
            {
                var cam = run.Rig.Cam; var planes = GeometryUtility.CalculateFrustumPlanes(cam);
                for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                {
                    var sc = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                    var rs = sc.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Renderer>()).Where(r => r.enabled && r.gameObject.activeInHierarchy).ToList();
                    var vis = rs.Where(r => GeometryUtility.TestPlanesAABB(planes, r.bounds)).ToList();
                    noteStatic?.Invoke($"scene {sc.name}: roots {string.Join(",", sc.GetRootGameObjects().Select(g => g.name).Take(20))}; renderers {rs.Count}, in view {vis.Count}: " +
                        string.Join(", ", vis.GroupBy(r => (r.sharedMaterial ? r.sharedMaterial.shader.name : "-")).Select(g => $"{g.Key} x{g.Count()}").Take(25)));
                }
                return null;
            }
            // "nopack": LookStudyInstancer.packVisible off (A/B of the packed instanced draw)
            if (v == "nopack")
            {
                var bs = All<MonoBehaviour>(run).Where(b => b.GetType().Name == "LookStudyInstancer").ToList();
                var f = bs.Count > 0 ? bs[0].GetType().GetField("packVisible") : null;
                var on = f == null ? new List<MonoBehaviour>() : bs.Where(b => (bool)f.GetValue(b)).ToList();
                foreach (var b in on) f.SetValue(b, false);
                return () => { foreach (var b in on) if (b) f.SetValue(b, true); };
            }
            if (v == "gpu")
            {
                var gm = run.gameObject.AddComponent<GpuMarkers>();
                return () => { gm.Report(noteStatic); Destroy(gm); };
            }
            // "zoom:2.5" measures at that wheel zoom (1 = default, 3.5 = furthest out); "zoom:2.5+shadows" etc. combines.
            if (v.StartsWith("zoom:"))
            {
                string rest = v.Substring(5);
                int plus = rest.IndexOf('+');
                float z = float.Parse(plus < 0 ? rest : rest.Substring(0, plus), System.Globalization.CultureInfo.InvariantCulture);
                float keep = run.Rig.Zoom;
                run.Rig.Zoom = z; run.Rig.Snap();
                var inner = plus < 0 ? null : Apply(rest.Substring(plus + 1), run);
                return () => { inner?.Invoke(); run.Rig.Zoom = keep; run.Rig.Snap(); };
            }
            // "swarm:200" adds 200 enemies (they shoot, so bullets pile up too) and leaves them for the later variants;
            // "swarm:200@3" waits 3 s after spawning before sampling (inside the settle time it is ~1 s).
            if (v.StartsWith("swarm:"))
            {
                var rest = v.Substring(6);
                int plus = rest.IndexOf('+');
                run.DevSwarm(int.Parse(plus < 0 ? rest : rest.Substring(0, plus)));
                return plus < 0 ? null : Apply(rest.Substring(plus + 1), run);
            }
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
                case "rfold":
                {
                    // 11-13 A/B: the reed field's old per-chunk RenderMeshInstanced path instead of the one GPU buffer
                    var bs = All<MonoBehaviour>(run).Where(b => b.enabled && b.GetType().Name == "ReedInstancer").ToList();
                    var fld = bs.Count > 0 ? bs[0].GetType().GetField("gpuBuffer") : null;
                    if (fld == null) return null;
                    foreach (var b in bs) fld.SetValue(b, false);
                    return () => { foreach (var b in bs) if (b) fld.SetValue(b, true); };
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
