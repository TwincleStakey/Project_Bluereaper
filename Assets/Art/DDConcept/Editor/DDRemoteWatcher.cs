// Lets the art pipeline be driven without clicking: drop ArtSource/dd_cmd.txt with commands
// (one per line: refresh, build, capture <name>, outline on|off, diag, yaw <deg>) and the
// editor runs them, writing results to ArtSource/dd_log.txt.
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class DDRemoteWatcher
{

    // 11-14 freeze guard (two editor freezes 11-13: a remote command opened a scene / started Play while Play was still
    // entering or exiting). Same code in every *_cmd remote (see RoomRunRemote.Gate): nothing starts while Play changes,
    // for 15 s after a Play change or domain reload, while compiling/importing, or while another remote holds
    // ArtSource/remote_play.lock (a lock older than 10 min counts as stale).
    static class Gate
    {
        static double lastChange = UnityEditor.EditorApplication.timeSinceStartup;
        static Gate() { UnityEditor.EditorApplication.playModeStateChanged += _ => lastChange = UnityEditor.EditorApplication.timeSinceStartup; }
        static string LockPath => System.IO.Path.Combine(System.IO.Directory.GetParent(UnityEngine.Application.dataPath).FullName, "ArtSource", "remote_play.lock");
        public static bool Settled() => !UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode && !UnityEditor.EditorApplication.isCompiling
            && !UnityEditor.EditorApplication.isUpdating && UnityEditor.EditorApplication.timeSinceStartup - lastChange >= 15.0;
        public static bool Ready(string me)
        {
            if (!Settled()) return false;
            try
            {
                if (System.IO.File.Exists(LockPath) && !System.IO.File.ReadAllText(LockPath).StartsWith(me + " ")
                    && (System.DateTime.Now - System.IO.File.GetLastWriteTime(LockPath)).TotalMinutes < 10) return false;
            }
            catch (System.IO.IOException) { return false; }
            return true;
        }
        public static void Take(string me) { try { System.IO.File.WriteAllText(LockPath, me + " " + System.DateTime.Now.ToString("HH:mm:ss")); } catch (System.IO.IOException) { } }
        public static void Release(string me)
        {
            try { if (System.IO.File.Exists(LockPath) && System.IO.File.ReadAllText(LockPath).StartsWith(me + " ")) System.IO.File.Delete(LockPath); } catch (System.IO.IOException) { }
        }
    }
    static double next;
    static readonly string Dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "ArtSource");
    static string CmdPath => Path.Combine(Dir, "dd_cmd.txt");
    static string LogPath => Path.Combine(Dir, "dd_log.txt");
    static readonly StringBuilder log = new StringBuilder();

    static DDRemoteWatcher()
    {
        EditorApplication.update += Tick;
        Application.logMessageReceived += (msg, st, type) =>
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Warning || msg.StartsWith("[DD]"))
                lock (log) log.AppendLine($"[{type}] {msg}");
        };
    }

    static void Tick()
    {
        if (EditorApplication.timeSinceStartup < next) return;
        next = EditorApplication.timeSinceStartup + 1.0;
        if (!File.Exists(CmdPath) || !Gate.Ready("dd")) return;
        var lines = File.ReadAllLines(CmdPath);
        File.Delete(CmdPath);
        lock (log) log.Clear();
        foreach (var raw in lines)
        {
            var l = raw.Trim(); if (l.Length == 0) continue;
            var parts = l.Split(' ');
            Note($"> {l}");
            try
            {
                switch (parts[0])
                {
                    case "refresh": AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate); break;
                    case "reimport": AssetDatabase.ImportAsset("Assets/Art/DDConcept", ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate); break;
                    case "build": DDConceptSetup.Build(); break;
                    case "capture": DDConceptSetup.Capture(parts.Length > 1 ? parts[1] : "unity_preview"); break;
                    case "outline": SetOutline(parts.Length > 1 && parts[1] == "on"); break;
                    case "diag": Diag(); break;
                    default: Note("unknown command"); break;
                }
            }
            catch (System.Exception e) { Note("EXCEPTION " + e); }
        }
        lock (log) File.WriteAllText(LogPath, log.ToString());
    }

    static void Note(string s) { lock (log) log.AppendLine(s); }

    static void SetOutline(bool on)
    {
        foreach (var g in AssetDatabase.FindAssets("t:Material", new[] { "Assets/Art/DDConcept/Materials" }))
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(g));
            if (m.shader.name == "DD/Toon") m.SetShaderPassEnabled("SRPDefaultUnlit", on);
        }
        Note("outline " + on);
    }

    static void Diag()
    {
        foreach (var n in new[] { "DD/Toon", "DD/Glow" })
        {
            var s = Shader.Find(n);
            if (!s) { Note($"{n}: NOT FOUND"); continue; }
            var msgs = ShaderUtil.GetShaderMessages(s);
            Note($"{n}: supported={s.isSupported} passes={s.passCount} messages={msgs.Length}");
            foreach (var m in msgs) Note($"   {m.severity} {m.message} line {m.line} ({m.platform})");
        }
        foreach (var c in new[] { "Hunter", "Dokkaebi" })
        {
            var mesh = AssetDatabase.LoadAllAssetsAtPath($"Assets/Art/DDConcept/Models/{c}.fbx").OfType<Mesh>().FirstOrDefault();
            if (!mesh) { Note($"{c}: no mesh"); continue; }
            var v = mesh.vertices; var nrm = mesh.normals; var t = mesh.triangles;
            int agree = 0, total = t.Length / 3;
            for (int i = 0; i < t.Length; i += 3)
            {
                var fn = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]);
                if (Vector3.Dot(fn, nrm[t[i]]) > 0) agree++;
            }
            var uv3 = new System.Collections.Generic.List<Vector3>(); mesh.GetUVs(3, uv3);
            Note($"{c}: verts={v.Length} tris={total} subMeshes={mesh.subMeshCount} windingAgree={agree}/{total} uv3={uv3.Count} bounds={mesh.bounds}");
        }
        var sun = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).FirstOrDefault(l => l.type == LightType.Directional);
        Note(sun ? $"sun: intensity={sun.intensity} color={sun.color} rot={sun.transform.eulerAngles} shadows={sun.shadows}" : "no directional light");
        var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/DDConcept/Materials/DD_Fox.mat");
        if (mat) Note($"DD_Fox base={mat.GetColor("_BaseColor")} keywords={string.Join(",", mat.shaderKeywords)}");
        var rp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
        Note("pipeline: " + (rp ? rp.name : "none") + " colorSpace=" + QualitySettings.activeColorSpace);
    }
}
