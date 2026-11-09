using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BlueReaper.BulletPatterns.Editor
{
    public static class BulletPatternChecks
    {
        [Serializable]
        private sealed class Report
        {
            public string unityVersion;
            public int passed;
            public string failure;
            public List<string> cases = new List<string>();
        }

        [MenuItem("Tools/BlueReaper/Check Bullet Pattern Editor")]
        public static void Run()
        {
            var report = new Report { unityVersion = Application.unityVersion };
            var pattern = ScriptableObject.CreateInstance<BulletPattern>();
            string savedPath = null;
            try
            {
                Check(report, "straight endpoints and clamping", () =>
                {
                    var path = new BulletPath();
                    Near(path.Evaluate(-1f), path.Start);
                    Near(path.Evaluate(2f), path.End);
                    Near(path.Evaluate(0.5f), new Vector3(0f, 0f, 5f));
                });
                Check(report, "corner segments", () =>
                {
                    var path = new BulletPath { Preset = BulletPathPreset.Corner, Control = new Vector3(4f, 2f, 4f) };
                    Near(path.Evaluate(0.25f), path.Control * 0.5f);
                    Near(path.Evaluate(0.75f), (path.Control + path.End) * 0.5f);
                });
                Check(report, "quadratic curve includes Y", () =>
                {
                    var path = new BulletPath { Preset = BulletPathPreset.Curve, Control = new Vector3(4f, 6f, 5f) };
                    Near(path.Evaluate(0.5f), new Vector3(2f, 3f, 5f));
                });
                Check(report, "stationary tangent is defined", () =>
                {
                    var path = new BulletPath { Start = Vector3.one, End = Vector3.one, Control = Vector3.one, Preset = BulletPathPreset.Curve };
                    Near(path.Tangent(0.5f), Vector3.forward);
                });
                Check(report, "resize preserves individually edited paths", () =>
                {
                    BulletPath original = pattern.Paths[0];
                    original.Control = new Vector3(1f, 5f, 3f);
                    pattern.Resize(5);
                    Require(ReferenceEquals(original, pattern.Paths[0]), "Existing path replaced");
                    Near(pattern.Paths[0].Control, new Vector3(1f, 5f, 3f));
                    Require(!ReferenceEquals(pattern.Paths[1], pattern.Paths[2]), "New paths share state");
                    pattern.Resize(3);
                    Require(pattern.MaximumCount == 3, "Wrong resized count");
                });
                Check(report, "zero count rejected", () =>
                {
                    bool rejected = false;
                    try { pattern.Resize(0); } catch (ArgumentOutOfRangeException) { rejected = true; }
                    Require(rejected, "Zero count accepted");
                });
                Check(report, "line layout is centered and preserves preset", () =>
                {
                    pattern.Resize(5); pattern.Layout = BulletLayout.Line; pattern.Spacing = 2f;
                    pattern.Paths[0].Preset = BulletPathPreset.Curve;
                    pattern.GenerateLayout();
                    Near(pattern.Paths[0].Start, new Vector3(-4f, 0f, 0f));
                    Near(pattern.Paths[4].Start, new Vector3(4f, 0f, 0f));
                    Require(pattern.Paths[0].Preset == BulletPathPreset.Curve, "Preset changed");
                });
                Check(report, "circle layout radial endpoints", () =>
                {
                    pattern.Resize(4); pattern.Layout = BulletLayout.Circle; pattern.Radius = 2f;
                    pattern.LayoutCenter = new Vector3(1f, 3f, 2f); pattern.GenerateLayout();
                    Near(pattern.Paths[0].Start, pattern.LayoutCenter + Vector3.forward * 2f);
                    Near(pattern.Paths[1].End, pattern.LayoutCenter + Vector3.right * 12f);
                });
                Check(report, "single fan bullet and fan boundaries", () =>
                {
                    pattern.LayoutCenter = Vector3.zero; pattern.Layout = BulletLayout.Fan; pattern.FanAngle = 90f;
                    pattern.Resize(1); pattern.GenerateLayout();
                    Near(pattern.Paths[0].Start, Vector3.forward * 2f);
                    pattern.Resize(3); pattern.GenerateLayout();
                    Near(pattern.Paths[0].Start, Quaternion.Euler(0f, -45f, 0f) * Vector3.forward * 2f);
                    Near(pattern.Paths[2].Start, Quaternion.Euler(0f, 45f, 0f) * Vector3.forward * 2f);
                });
                Check(report, "manual layout does not overwrite paths", () =>
                {
                    pattern.Layout = BulletLayout.Manual;
                    BulletPath original = pattern.Paths[0]; pattern.GenerateLayout();
                    Require(ReferenceEquals(original, pattern.Paths[0]), "Manual paths regenerated");
                });

                pattern.Resize(1); pattern.Paths[0] = new BulletPath();
                pattern.TravelDuration = 2f; pattern.RepeatCount = 3; pattern.RepeatInterval = 0.8f;
                Check(report, "not emitted before launch", () =>
                {
                    Require(!pattern.TryEvaluate(0, 0, -0.1f, out _, out _), "Negative time emitted");
                    Require(!pattern.TryEvaluate(0, 1, 0.79f, out _, out _), "Second emission early");
                });
                Check(report, "overlapping whole-pattern emissions", () =>
                {
                    Require(pattern.TryEvaluate(0, 0, 1.2f, out Vector3 a, out _), "First emission missing");
                    Require(pattern.TryEvaluate(0, 1, 1.2f, out Vector3 b, out _), "Second emission missing");
                    Near(a, new Vector3(0f, 0f, 6f)); Near(b, new Vector3(0f, 0f, 2f));
                    Require(Mathf.Abs(pattern.TotalDuration - 3.6f) < 0.0001f, "Final travel tail missing");
                });
                Check(report, "despawn on arrival", () =>
                {
                    Require(!pattern.TryEvaluate(0, 0, 2f, out _, out _), "Still alive on arrival");
                    Require(pattern.TryEvaluate(0, 2, 2f, out _, out _), "Later emission despawned early");
                    Require(!pattern.TryEvaluate(0, 2, pattern.TotalDuration, out _, out _), "Rounding left final emission alive");
                    Require(pattern.TryEvaluate(0, 2, pattern.TotalDuration - 0.001f, out _, out _), "Final emission despawned early");
                });
                Check(report, "retained bullets stop at endpoint", () =>
                {
                    pattern.DespawnAtEnd = false;
                    Require(pattern.TryEvaluate(0, 0, 100f, out Vector3 p, out _), "Retained bullet disappeared");
                    Near(p, pattern.Paths[0].End); pattern.DespawnAtEnd = true;
                });
                Check(report, "zero interval and total emission count", () =>
                {
                    pattern.RepeatInterval = 0f;
                    Require(pattern.TryEvaluate(0, 2, 0f, out _, out _), "Simultaneous emission missing");
                    Require(!pattern.TryEvaluate(0, 3, 0f, out _, out _), "Extra emission created");
                    Require(!pattern.TryEvaluate(-1, 0, 0f, out _, out _), "Invalid bullet accepted");
                    pattern.RepeatInterval = 0.8f;
                });
                Check(report, "direction offset and fixed direction", () =>
                {
                    pattern.DirectionOffset = 90f;
                    pattern.TryEvaluate(0, 0, 1f, out _, out Quaternion q); Near(q * Vector3.forward, Vector3.right);
                    pattern.Paths[0].End = Vector3.up * 10f; pattern.AlignToPath = false;
                    pattern.TryEvaluate(0, 0, 1f, out _, out q); Near(q * Vector3.forward, Vector3.right);
                    pattern.AlignToPath = true; pattern.DirectionOffset = 0f;
                });
                Check(report, "vertical paths have finite rotation", () =>
                {
                    pattern.TryEvaluate(0, 0, 1f, out _, out Quaternion q);
                    Near(q * Vector3.forward, Vector3.up);
                });
                Check(report, "invalid settings and coordinates rejected", () =>
                {
                    pattern.TravelDuration = float.NaN;
                    Require(pattern.GetValidationError() != null && !pattern.TryEvaluate(0, 0, 1f, out _, out _), "NaN duration accepted");
                    pattern.TravelDuration = 2f; pattern.RepeatInterval = -1f;
                    Require(pattern.GetValidationError() != null, "Negative period accepted");
                    pattern.RepeatInterval = 0.8f; pattern.Paths[0].Start = new Vector3(float.PositiveInfinity, 0f, 0f);
                    Require(pattern.GetValidationError() != null, "Infinite coordinate accepted");
                    pattern.Paths[0] = new BulletPath();
                    Require(pattern.GetValidationError() == null, "Valid pattern rejected");
                });
                Check(report, "asset save and serialized data round trip", () =>
                {
                    pattern.Paths[0].Control = new Vector3(3f, 4f, 5f);
                    savedPath = AssetDatabase.GenerateUniqueAssetPath("Assets/BulletPatternCheck.asset");
                    AssetDatabase.CreateAsset(pattern, savedPath); AssetDatabase.SaveAssetIfDirty(pattern);
                    var copy = ScriptableObject.CreateInstance<BulletPattern>();
                    try
                    {
                        EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(pattern), copy);
                        Near(copy.Paths[0].Control, new Vector3(3f, 4f, 5f));
                        Require(copy.RepeatCount == 3 && copy.MaximumCount == 1, "Serialization lost settings");
                        Require(File.Exists(savedPath), "Asset was not saved");
                    }
                    finally { UnityEngine.Object.DestroyImmediate(copy); }
                });
                Check(report, "Undo restores edited path", () =>
                {
                    Undo.IncrementCurrentGroup(); Undo.RecordObject(pattern, "Bullet pattern check");
                    Vector3 original = pattern.Paths[0].End; pattern.Paths[0].End = Vector3.one;
                    Undo.FlushUndoRecordObjects(); Undo.PerformUndo(); Near(pattern.Paths[0].End, original);
                });
                Check(report, "editor preview tick does not dirty asset or scene", () =>
                {
                    AssetDatabase.SaveAssetIfDirty(pattern);
                    var scene = SceneManager.GetActiveScene();
                    bool dirtyBefore = scene.isDirty;
                    int rootsBefore = scene.rootCount;
                    var editor = UnityEditor.Editor.CreateEditor(pattern, typeof(BulletPatternEditor));
                    try
                    {
                        Type type = editor.GetType();
                        type.GetField("playing", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(editor, true);
                        type.GetField("previousUpdate", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(editor, EditorApplication.timeSinceStartup - 0.2d);
                        type.GetMethod("Tick", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(editor, null);
                        float time = (float)type.GetField("previewTime", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(editor);
                        Require(time > 0f, "Preview did not advance");
                        Require(!EditorUtility.IsDirty(pattern), "Preview dirtied asset");
                        Require(scene.isDirty == dirtyBefore && scene.rootCount == rootsBefore, "Preview changed scene");
                    }
                    finally { UnityEngine.Object.DestroyImmediate(editor); }
                });
            }
            catch (Exception exception) { report.failure = exception.ToString(); throw; }
            finally
            {
                if (savedPath != null) AssetDatabase.DeleteAsset(savedPath);
                else UnityEngine.Object.DestroyImmediate(pattern);
                Directory.CreateDirectory("Logs");
                File.WriteAllText("Logs/BulletPatternChecks.json", JsonUtility.ToJson(report, true));
                Debug.Log($"BULLET_PATTERN_CHECKS: {report.passed} passed; failure: {report.failure ?? "none"}");
            }
        }

        public static void RunBatch()
        {
            try { Run(); }
            catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
        }

        private static void Check(Report report, string name, Action test)
        {
            test(); report.passed++; report.cases.Add(name);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void Near(Vector3 actual, Vector3 expected)
        {
            Require((actual - expected).sqrMagnitude < 0.00001f, $"Expected {expected}, got {actual}");
        }
    }
}
