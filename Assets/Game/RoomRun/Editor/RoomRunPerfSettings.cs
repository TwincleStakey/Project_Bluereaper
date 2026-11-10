// Rendering settings for frame rate (Kido 11-10, "200 fps on a 4060"), applied through the editor API because project
// settings edited on disk are not reloaded by an open editor. Menu: Tools/RoomRun/Apply Perf Settings.
//   - GPU Resident Drawer (URP, Unity 6): static MeshRenderers are drawn GPU-driven through BatchRendererGroup instead
//     of being culled and submitted one by one on the CPU. The abandoned village has ~7,900 renderers and was CPU-bound
//     (8.7 ms main thread). Needs Forward+ (the PC renderer uses it) and the BRG shader variants kept.
using System.IO;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace RoomRun
{
    public static class RoomRunPerfSettings
    {
        [MenuItem("Tools/RoomRun/Apply Perf Settings")]
        public static void Apply() => Set(true);

        // 11-10: measured in the editor, the GPU Resident Drawer did not lower the village's CPU time and slowed other
        // fields (bamboo 319 -> 244 fps), so it is switched back off.
        [MenuItem("Tools/RoomRun/Revert Perf Settings")]
        public static void Revert() => Set(false);

        static void Set(bool on)
        {
            string log = Path.GetFullPath("ArtSource/rr_log.txt");
            void Note(string s) => File.AppendAllText(log, $"[{System.DateTime.Now:HH:mm:ss}] perfsettings: {s}\n");
            // Graphics settings > BatchRendererGroup Variants = Keep All (the property is read-only from script)
            var gs = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
            if (gs.Length > 0)
            {
                var so = new SerializedObject(gs[0]);
                var p = so.FindProperty("m_BrgStripping");
                if (p != null) { p.intValue = on ? (int)BatchRendererGroupStrippingMode.KeepAll : 0; so.ApplyModifiedProperties(); }
            }
            foreach (var guid in AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset", new[] { "Assets/Settings" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var a = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
                if (!a || !path.Contains("PC_")) continue;
                a.gpuResidentDrawerMode = on ? GPUResidentDrawerMode.InstancedDrawing : GPUResidentDrawerMode.Disabled;
                EditorUtility.SetDirty(a);
                Note($"{path}: GPU Resident Drawer = {a.gpuResidentDrawerMode}");
            }
            AssetDatabase.SaveAssets();
            Note($"BRG stripping = {(on ? "KeepAll" : "default")}");
        }
    }
}
