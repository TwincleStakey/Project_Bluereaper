// Development player build for measuring real frame cost outside the editor (Kido 11-10, "200 fps on a 4060").
// Builds RoomRun plus every field into Builds/PerfDev (gitignored) without touching the project's build scene list, with
// frame timing stats on for the build only. Run it with:  BulletHeavenArt.exe -rrperf grass,valley,...  (PerfProbe writes
// rr_perf.csv to the player's persistentDataPath and quits). Menu: Tools/RoomRun/Perf Build (Development).
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace RoomRun
{
    public static class RoomRunPerfBuild
    {
        static readonly string[] Scenes =
        {
            "Assets/Game/RoomRun/Scenes/RoomRun.unity",
            "Assets/Game/RoomRun/Scenes/RoomRun_Placeholder.unity",
            "Assets/Scenes/LookStudy.unity",
            "Assets/Art/LookStudy/Stages/Valley.unity",
            "Assets/Art/LookStudy/Stages/Village.unity",
            "Assets/Art/LookStudy/Stages/Palace.unity",
            "Assets/Underworld/Scenes/Underworld.unity",
            "Assets/Game/AbandonedVillage/AbandonedVillage.unity",
            "Assets/Game/ReedField/ReedField.unity",
            "Assets/Game/BambooArena/BambooArena.unity",
        };

        public static string OutDir => Path.GetFullPath("Builds/PerfDev");

        [MenuItem("Tools/RoomRun/Perf Build (Development)")]
        public static void Build()
        {
            string log = Path.GetFullPath("ArtSource/rr_log.txt");
            void Note(string s) => File.AppendAllText(log, $"[{System.DateTime.Now:HH:mm:ss}] perfbuild: {s}\n");
            bool keepTiming = PlayerSettings.enableFrameTimingStats;
            PlayerSettings.enableFrameTimingStats = true;
            try
            {
                var opts = new BuildPlayerOptions
                {
                    scenes = System.Array.FindAll(Scenes, File.Exists),
                    locationPathName = Path.Combine(OutDir, "BulletHeavenArt.exe"),
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.Development,
                };
                Note($"building {opts.scenes.Length} scenes to {OutDir}");
                var report = BuildPipeline.BuildPlayer(opts);
                Note($"{report.summary.result} in {report.summary.totalTime.TotalSeconds:0}s, {report.summary.totalErrors} errors");
            }
            finally { PlayerSettings.enableFrameTimingStats = keepTiming; }
        }
    }
}
