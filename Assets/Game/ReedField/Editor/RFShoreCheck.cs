// Play-mode shoreline check: puts the player on the channel bank (RoomRun camera), logs the water/depth state and
// captures the game view with each ink filter mode into Downloads/scene-refs/ink-filter-test/v3.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Reeds;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public static class RFShoreCheck
{
    static Queue<System.Action> steps; static double next;
    public static string Dir => Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), "Downloads", "scene-refs", "ink-filter-test", "v3");

    static void Wait(double s) => steps.Enqueue(() => next = EditorApplication.timeSinceStartup + s);
    static void Do(System.Action a) => steps.Enqueue(a);
    static void Tick()
    {
        if (!EditorApplication.isPlaying || steps == null || steps.Count == 0) { EditorApplication.update -= Tick; return; }
        if (EditorApplication.timeSinceStartup < next) return;
        steps.Dequeue()();
    }

    static System.Type RunType => System.AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("RoomRun.RunDirector")).FirstOrDefault(x => x != null);

    static void Place(Vector3 p)
    {
        var t = RunType; var run = t?.GetProperty("Instance")?.GetValue(null); if (run == null) return;
        var player = t.GetProperty("Player").GetValue(run) as Component;
        player.GetType().GetMethod("PlaceAt").Invoke(player, new object[] { p, 0f });
        var rig = t.GetProperty("Rig").GetValue(run);
        rig?.GetType().GetMethod("Snap")?.Invoke(rig, null);
    }

    // A point on the dry bank next to the channel, a few metres from the water's edge.
    public static Vector3 BankPoint(float t01, float side)
    {
        var f = Object.FindAnyObjectByType<ReedField>();
        var line = f.ChannelLine;
        int i = Mathf.Clamp(Mathf.RoundToInt(t01 * (line.Count - 1)), 1, line.Count - 2);
        var d = (line[i + 1] - line[i - 1]).normalized; var n = new Vector2(-d.y, d.x);
        f.ChannelDistance(line[i], out float half);
        var p = line[i] + n * side * (half + 2.5f);
        return f.transform.TransformPoint(new Vector3(p.x, 0, p.y));
    }

    static void Log()
    {
        var f = Object.FindAnyObjectByType<ReedField>(); var cam = Camera.main;
        var d = cam ? cam.GetUniversalAdditionalCameraData() : null;
        var urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        Debug.Log($"[Shore] field pos {(f ? f.transform.position : Vector3.zero)}, waterLevel {(f ? f.waterLevel : 0)}, global _RF_WaterLevel {Shader.GetGlobalFloat("_RF_WaterLevel")}, " +
                  $"cam {(cam ? cam.name : "-")} depthOpt {(d ? d.requiresDepthOption.ToString() : "-")} requiresDepthTexture {(d ? d.requiresDepthTexture : false)}, " +
                  $"pipeline {(urp ? urp.name : "-")} supportsDepth {(urp ? urp.supportsCameraDepthTexture : false)}, quality {QualitySettings.names[QualitySettings.GetQualityLevel()]}");
    }

    // What stops the player going south (and the other ways): first solid collider hit walking out from the centre.
    static void LogBounds()
    {
        var t = RunType; var run = t?.GetProperty("Instance")?.GetValue(null);
        var walls = GameObject.Find("ArenaWalls");
        var sb = new System.Text.StringBuilder($"[Shore] area centre {t?.GetProperty("AreaCenter")?.GetValue(run)} size {t?.GetProperty("AreaSize")?.GetValue(run)}, ArenaWalls {(walls ? walls.transform.childCount.ToString() : "none")}; ");
        var f = Object.FindAnyObjectByType<ReedField>();
        var from = f.transform.position + Vector3.up * 1f;
        foreach (var d in new[] { Vector3.back, Vector3.forward, Vector3.left, Vector3.right })
        {
            var hits = Physics.RaycastAll(from, d, 80f, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance)
                .Where(h => !(h.collider is MeshCollider)).Take(2).Select(h => $"{h.collider.name}@{h.distance:0.0}");
            sb.Append($"{d}: {string.Join(", ", hits)}; ");
        }
        Debug.Log(sb.ToString());
    }

    static void Cap(string file) { Directory.CreateDirectory(Dir); ScreenCapture.CaptureScreenshot(Path.Combine(Dir, file)); }

    static void Start(System.Action<System.Action<System.Action>> build)
    {
        steps = new Queue<System.Action>(); next = 0;
        if (!Object.FindAnyObjectByType<ReedField>()) { Do(() => RFPlayJump.Jump()); Wait(4); }
        build(Do);
        EditorApplication.update -= Tick; EditorApplication.update += Tick;
    }

    [MenuItem("Tools/Dev/Reed Field/Play: Shore Check (log + shots)")]
    public static void Check()
    {
        if (!EditorApplication.isPlaying) { Debug.LogError("[Shore] enter Play first"); return; }
        Start(_ =>
        {
            Do(Log); Do(LogBounds);
            float[] at = { 0.35f, 0.6f };
            for (int k = 0; k < at.Length; k++)
            {
                int kk = k;
                Do(() => { InkFilters.InkFilter.Mode = InkFilters.InkMode.Off; Place(BankPoint(at[kk], 1)); }); Wait(1.2);
                Do(() => Cap($"check_{kk}.png")); Wait(0.4);
            }
            Do(() => Debug.Log("[Shore] check done -> " + Dir));
        });
    }

    // The four review shots at one bank spot: off / ink / sumi-e / watercolour.
    // Walks the player's CharacterController straight south from inside the arena and logs where it stops.
    [MenuItem("Tools/Dev/Reed Field/Play: South Walk Test")]
    public static void SouthWalk()
    {
        if (!EditorApplication.isPlaying) return;
        Start(_ =>
        {
            foreach (float x in new[] { -20f, 0f, 20f })
            {
                float xx = x;
                Do(() => Place(new Vector3(xx, 0, -16))); Wait(0.5);
                Do(() =>
                {
                    var p = GameObject.Find("Player"); var cc = p.GetComponent<CharacterController>();
                    for (int k = 0; k < 200; k++) cc.Move(new Vector3(0, -0.5f, -0.25f));
                    Debug.Log($"[Shore] walked south from x {xx}: stopped at {p.transform.position}");
                });
                Wait(0.8);
                Do(() => Cap($"south_{xx}.png")); Wait(0.4);
            }
        });
    }

    [MenuItem("Tools/Dev/Reed Field/Play: Shore Shots (4 filters)")]
    public static void Shots()
    {
        if (!EditorApplication.isPlaying) { Debug.LogError("[Shore] enter Play first"); return; }
        Start(_ =>
        {
            string[] names = { "shore_off.png", "shore_ink.png", "shore_sumie.png", "shore_watercolour.png" };
            Do(() => Place(BankPoint(0.35f, 1))); Wait(1.2);
            for (int m = 0; m < 4; m++)
            {
                int mm = m;
                Do(() => InkFilters.InkFilter.Mode = (InkFilters.InkMode)mm); Wait(0.6);
                Do(() => Cap(names[mm])); Wait(0.4);
            }
            // next to the dead tree (characters and trees must stay solid under every filter)
            Do(() => Place(new Vector3(15.5f, 0, 9f))); Wait(1.2);
            for (int m = 0; m < 4; m++)
            {
                int mm = m;
                Do(() => InkFilters.InkFilter.Mode = (InkFilters.InkMode)mm); Wait(0.6);
                Do(() => Cap("tree_" + names[mm].Substring(6))); Wait(0.4);
            }
            Do(() => { InkFilters.InkFilter.Mode = InkFilters.InkMode.Off; Debug.Log("[Shore] shots done -> " + Dir); });
        });
    }
}
