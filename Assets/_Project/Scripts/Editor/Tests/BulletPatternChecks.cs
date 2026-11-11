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
                    pattern.Layout = BulletLayout.Manual;
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
                Check(report, "growth copies the last path without sharing edits", () =>
                {
                    pattern.Layout = BulletLayout.Manual;
                    pattern.Resize(2);
                    BulletPath first = pattern.Paths[0];
                    var last = new BulletPath { Preset = BulletPathPreset.Curve,
                        Start = new Vector3(2f, 3f, 4f), Control = new Vector3(5f, 6f, 7f), End = new Vector3(8f, 9f, 10f) };
                    pattern.Paths[1] = last;
                    pattern.Resize(5);
                    for (int i = 2; i < 5; i++)
                    {
                        BulletPath added = pattern.Paths[i];
                        Require(added.Preset == last.Preset && !ReferenceEquals(added, last), "Last path was not independently copied");
                        Near(added.Start, last.Start); Near(added.Control, last.Control); Near(added.End, last.End);
                    }
                    pattern.Paths[2].Start = Vector3.zero;
                    pattern.Paths[2].Control = Vector3.zero;
                    pattern.Paths[2].End = Vector3.zero;
                    pattern.Paths[2].Preset = BulletPathPreset.Corner;
                    Near(last.Start, new Vector3(2f, 3f, 4f));
                    Near(pattern.Paths[3].Control, last.Control); Near(pattern.Paths[4].End, last.End);
                    Require(last.Preset == BulletPathPreset.Curve && pattern.Paths[3].Preset == last.Preset, "Copied edits leaked");
                    Require(ReferenceEquals(first, pattern.Paths[0]), "Earlier path changed");
                    pattern.Resize(5);
                    Require(ReferenceEquals(last, pattern.Paths[1]), "Same-size resize changed existing paths");
                });
                Check(report, "empty path list gets independent defaults", () =>
                {
                    var empty = ScriptableObject.CreateInstance<BulletPattern>();
                    try
                    {
                        empty.Layout = BulletLayout.Manual;
                        empty.Paths.Clear(); empty.Resize(2);
                        Near(empty.Paths[0].End, new Vector3(0f, 0f, 10f));
                        Require(!ReferenceEquals(empty.Paths[0], empty.Paths[1]), "Empty list created shared defaults");
                    }
                    finally { UnityEngine.Object.DestroyImmediate(empty); }
                });
                Check(report, "nearest path picking and overlapping path cycle", () =>
                {
                    var editor = UnityEditor.Editor.CreateEditor(pattern, typeof(BulletPatternEditor));
                    try
                    {
                        Type type = editor.GetType();
                        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                        var hits = (List<(int index, float distance)>)type.GetField("pathHits", flags).GetValue(editor);
                        FieldInfo selection = type.GetField("selected", flags);
                        MethodInfo pick = type.GetMethod("PickPath", flags);
                        selection.SetValue(editor, 4);
                        Require((int)pick.Invoke(editor, null) == 4, "Empty pick changed selection");
                        hits.Add((0, 5f)); hits.Add((1, 0.5f));
                        Require((int)pick.Invoke(editor, null) == 1, "Did not select closest path");
                        hits.Clear(); hits.Add((0, 0f)); hits.Add((1, 0f)); hits.Add((2, 0f));
                        selection.SetValue(editor, 0);
                        Require((int)pick.Invoke(editor, null) == 1, "Overlapping pick did not advance");
                        selection.SetValue(editor, 2);
                        Require((int)pick.Invoke(editor, null) == 0, "Overlapping pick did not wrap");
                        hits.Clear(); hits.Add((2, 0f)); selection.SetValue(editor, 2);
                        Require((int)pick.Invoke(editor, null) == 2, "Single path pick changed selection");
                    }
                    finally { UnityEngine.Object.DestroyImmediate(editor); }
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
                    pattern.Resize(4);
                    for (int i = 0; i < pattern.MaximumCount; i++) pattern.Paths[i] = new BulletPath();
                    pattern.Layout = BulletLayout.Circle; pattern.Radius = 2f;
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
                Check(report, "fan growth preserves screenshot curve and distributes the full circle", () =>
                {
                    var fan = ScriptableObject.CreateInstance<BulletPattern>();
                    try
                    {
                        fan.Layout = BulletLayout.Fan; fan.FanAngle = 360f; fan.Radius = 2f; fan.TravelDistance = 1f;
                        Vector3 start = new Vector3(0.041008294f, 0f, -1.7129433f);
                        Vector3 control = new Vector3(1.7633494f, 0f, 2.7900472f);
                        Vector3 end = new Vector3(-3.9367795f, 0f, 7.7851334f);
                        fan.Paths[0] = new BulletPath { Preset = BulletPathPreset.Curve, Start = start, Control = control, End = end };
                        fan.Resize(8);
                        for (int pass = 0; pass < 2; pass++)
                        {
                            if (pass == 1) fan.Resize(12);
                            for (int i = 0; i < fan.MaximumCount; i++)
                            {
                                BulletPath copy = fan.Paths[i];
                                Vector3 direction = Quaternion.Euler(0f, -180f + 360f * i / fan.MaximumCount, 0f) * Vector3.forward;
                                Near(copy.Start, direction * fan.Radius);
                                Near((copy.End - copy.Start).normalized, direction);
                                Require(copy.Preset == BulletPathPreset.Curve, "Curve preset lost");
                                Close(Vector3.Distance(copy.Start, copy.Control), Vector3.Distance(start, control));
                                Close(Vector3.Distance(copy.Control, copy.End), Vector3.Distance(control, end));
                                Close(Vector3.Distance(copy.Start, copy.End), Vector3.Distance(start, end));
                                Close(Vector3.Cross(copy.End - copy.Start, copy.Control - copy.Start).y,
                                    Vector3.Cross(end - start, control - start).y);
                                Require((copy.Evaluate(0.5f) - (copy.Start + copy.End) * 0.5f).sqrMagnitude > 0.1f,
                                    "Curve was flattened to a straight line");
                            }
                            Require(Vector3.Distance(fan.Paths[0].Start, fan.Paths[fan.MaximumCount - 1].Start) > 0.1f,
                                "360-degree fan duplicated its first and last slot");
                        }
                    }
                    finally { UnityEngine.Object.DestroyImmediate(fan); }
                });
                Check(report, "line growth rebalances while retaining edited 3D offsets", () =>
                {
                    var line = ScriptableObject.CreateInstance<BulletPattern>();
                    try
                    {
                        line.LayoutCenter = new Vector3(2f, 6f, 3f); line.Spacing = 2f;
                        BulletPath original = line.Paths[0];
                        original.Preset = BulletPathPreset.Curve;
                        original.Start = new Vector3(7f, 2f, -3f);
                        original.Control = new Vector3(9f, 5f, 2f);
                        original.End = new Vector3(7f, 4f, 7f);
                        line.Resize(3);
                        Require(ReferenceEquals(original, line.Paths[0]), "Existing path object replaced");
                        for (int i = 0; i < 3; i++)
                        {
                            BulletPath path = line.Paths[i];
                            Near(path.Start, line.LayoutCenter + Vector3.right * ((i - 1) * 2f));
                            Near(path.Control - path.Start, new Vector3(2f, 3f, 5f));
                            Near(path.End - path.Start, new Vector3(0f, 2f, 10f));
                        }
                        Require(!ReferenceEquals(line.Paths[1], line.Paths[2]), "Added paths share state");
                    }
                    finally { UnityEngine.Object.DestroyImmediate(line); }
                });
                Check(report, "regeneration retains distinct curve and corner shapes and is idempotent", () =>
                {
                    var custom = ScriptableObject.CreateInstance<BulletPattern>();
                    try
                    {
                        custom.Layout = BulletLayout.Manual; custom.Resize(2);
                        custom.Paths[0] = new BulletPath { Preset = BulletPathPreset.Curve,
                            Start = new Vector3(1f, 2f, 3f), Control = new Vector3(4f, 8f, 5f), End = new Vector3(5f, 4f, 12f) };
                        custom.Paths[1] = new BulletPath { Preset = BulletPathPreset.Corner,
                            Start = new Vector3(-2f, 1f, 0f), Control = new Vector3(3f, 4f, 2f), End = new Vector3(6f, 5f, 0f) };
                        float[] lengths = { Vector3.Distance(custom.Paths[0].Start, custom.Paths[0].End),
                            Vector3.Distance(custom.Paths[1].Start, custom.Paths[1].End) };
                        custom.Layout = BulletLayout.Circle; custom.LayoutCenter = new Vector3(1f, 2f, 3f);
                        custom.GenerateLayout();
                        Require(custom.Paths[0].Preset == BulletPathPreset.Curve && custom.Paths[1].Preset == BulletPathPreset.Corner,
                            "Individual presets lost");
                        Vector3[] points = { custom.Paths[0].Start, custom.Paths[0].Control, custom.Paths[0].End,
                            custom.Paths[1].Start, custom.Paths[1].Control, custom.Paths[1].End };
                        custom.GenerateLayout();
                        for (int i = 0; i < 2; i++)
                        {
                            Near(custom.Paths[i].Start, points[i * 3]);
                            Near(custom.Paths[i].Control, points[i * 3 + 1]); Near(custom.Paths[i].End, points[i * 3 + 2]);
                            Close(Vector3.Distance(custom.Paths[i].Start, custom.Paths[i].End), lengths[i]);
                        }
                        Close(custom.Paths[0].Control.y - custom.Paths[0].Start.y, 6f);
                        Close(custom.Paths[1].End.y - custom.Paths[1].Start.y, 4f);
                    }
                    finally { UnityEngine.Object.DestroyImmediate(custom); }
                });
                Check(report, "vertical and stationary paths remain finite during placement", () =>
                {
                    var vertical = ScriptableObject.CreateInstance<BulletPattern>();
                    try
                    {
                        vertical.Layout = BulletLayout.Circle;
                        vertical.Paths[0] = new BulletPath { Preset = BulletPathPreset.Curve,
                            Start = Vector3.up, Control = new Vector3(2f, 4f, 0f), End = Vector3.up * 5f };
                        vertical.Resize(4);
                        for (int i = 0; i < 4; i++)
                        {
                            Near(vertical.Paths[i].End - vertical.Paths[i].Start, Vector3.up * 4f);
                            Close((vertical.Paths[i].Control - vertical.Paths[i].Start).magnitude, Mathf.Sqrt(13f));
                        }
                        vertical.Resize(1);
                        vertical.Paths[0] = new BulletPath { Start = Vector3.one, Control = Vector3.one, End = Vector3.one };
                        vertical.Radius = 0f; vertical.Resize(3);
                        Require(vertical.GetValidationError() == null, "Stationary path produced invalid coordinates");
                        foreach (BulletPath path in vertical.Paths) { Near(path.Start, path.Control); Near(path.Start, path.End); }
                    }
                    finally { UnityEngine.Object.DestroyImmediate(vertical); }
                });

                Check(report, "linear single bullet uses speed and expires without paths", () =>
                {
                    var linear = ScriptableObject.CreateInstance<BulletPattern>();
                    try
                    {
                        linear.MovementType = BulletMovementType.Linear; linear.Paths = null;
                        linear.LinearSpeed = 10f; linear.LinearLifetime = 2f; linear.DespawnAtEnd = false;
                        linear.TravelDuration = float.NaN; linear.RepeatCount = 3; linear.RepeatInterval = 0.3f;
                        Require(linear.GetValidationError() == null && linear.MaximumCount == 1, "Linear mode required a path");
                        Require(linear.TryEvaluate(0, 0, 1f, out Vector3 p, out _), "Linear bullet missing");
                        Near(p, Vector3.forward * 10f);
                        Require(!linear.TryEvaluate(0, 1, 0.2f, out _, out _), "Linear emission started early");
                        Require(linear.TryEvaluate(0, 1, 0.8f, out p, out _), "Repeated linear bullet missing");
                        Near(p, Vector3.forward * 5f);
                        Require(!linear.TryEvaluate(0, 0, 2f, out _, out _), "Linear lifetime did not expire");
                        Close(linear.TotalDuration, 2.6f);
                    }
                    finally { UnityEngine.Object.DestroyImmediate(linear); }
                });
                Check(report, "linear line fan and circle distribute directions", () =>
                {
                    var linear = ScriptableObject.CreateInstance<BulletPattern>();
                    try
                    {
                        linear.MovementType = BulletMovementType.Linear; linear.LinearCount = 3; linear.Spacing = 2f;
                        linear.TryGetLinearLaunch(0, out Vector3 start, out Vector3 direction);
                        Near(start, Vector3.left * 2f); Near(direction, Vector3.forward);
                        linear.Layout = BulletLayout.Fan; linear.FanAngle = 90f;
                        linear.TryGetLinearLaunch(0, out start, out direction);
                        Near(direction, Quaternion.Euler(0f, -45f, 0f) * Vector3.forward); Near(start, direction * linear.Radius);
                        linear.TryGetLinearLaunch(2, out _, out direction);
                        Near(direction, Quaternion.Euler(0f, 45f, 0f) * Vector3.forward);
                        linear.Layout = BulletLayout.Circle; linear.LinearCount = 4;
                        linear.TryGetLinearLaunch(1, out start, out direction);
                        Near(direction, Vector3.right); Near(start, Vector3.right * linear.Radius);
                        linear.Layout = BulletLayout.Fan; linear.FanAngle = 360f;
                        linear.TryGetLinearLaunch(0, out Vector3 first, out _);
                        linear.TryGetLinearLaunch(3, out Vector3 last, out _);
                        Require((first - last).sqrMagnitude > 0.1f, "Full fan duplicated endpoints");
                    }
                    finally { UnityEngine.Object.DestroyImmediate(linear); }
                });
                Check(report, "linear resize and placement preserve authored paths", () =>
                {
                    var linear = ScriptableObject.CreateInstance<BulletPattern>();
                    try
                    {
                        var path = linear.Paths[0]; path.End = new Vector3(2f, 3f, 7f);
                        linear.MovementType = BulletMovementType.Linear; linear.Resize(8); linear.GenerateLayout();
                        Require(linear.MaximumCount == 8 && linear.Paths.Count == 1, "Linear count edited path storage");
                        Near(path.End, new Vector3(2f, 3f, 7f));
                        linear.MovementType = BulletMovementType.Path;
                        Require(linear.MaximumCount == 1 && ReferenceEquals(linear.Paths[0], path), "Mode switch lost paths");
                    }
                    finally { UnityEngine.Object.DestroyImmediate(linear); }
                });
                Check(report, "linear invalid settings and visual rotation", () =>
                {
                    var linear = ScriptableObject.CreateInstance<BulletPattern>();
                    try
                    {
                        linear.MovementType = BulletMovementType.Linear; linear.Layout = BulletLayout.Manual;
                        Require(linear.GetValidationError() != null && !linear.TryEvaluate(0, 0, 0f, out _, out _), "Linear manual accepted");
                        linear.Layout = BulletLayout.Line; linear.LinearLifetime = 0f;
                        Require(linear.GetValidationError() != null && !linear.TryEvaluate(0, 0, 0f, out _, out _), "Zero lifetime accepted");
                        linear.LinearLifetime = 2f; linear.LinearSpeed = float.NaN;
                        Require(linear.GetValidationError() != null && !linear.TryEvaluate(0, 0, 0f, out _, out _), "NaN speed accepted");
                        linear.LinearSpeed = 10f; linear.LinearCount = 0;
                        Require(linear.GetValidationError() != null, "Zero count accepted");
                        linear.LinearCount = 1; linear.DirectionOffset = 90f;
                        Require(linear.TryEvaluate(0, 0, 1f, out Vector3 p, out Quaternion rotation), "Valid linear bullet rejected");
                        Near(p, Vector3.forward * 10f); Near(rotation * Vector3.forward, Vector3.right);
                    }
                    finally { UnityEngine.Object.DestroyImmediate(linear); }
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
                    pattern.StartTime = -0.1f;
                    Require(pattern.GetValidationError() != null, "Negative start delay accepted");
                    pattern.StartTime = float.NaN;
                    Require(pattern.GetValidationError() != null, "NaN start delay accepted");
                    pattern.StartTime = 0f;
                    Require(pattern.GetValidationError() == null, "Valid pattern rejected");
                });
                Check(report, "asset save and serialized data round trip", () =>
                {
                    pattern.Paths[0].Control = new Vector3(3f, 4f, 5f);
                    pattern.StartTime = 0.5f;
                    savedPath = AssetDatabase.GenerateUniqueAssetPath("Assets/BulletPatternCheck.asset");
                    AssetDatabase.CreateAsset(pattern, savedPath); AssetDatabase.SaveAssetIfDirty(pattern);
                    var copy = ScriptableObject.CreateInstance<BulletPattern>();
                    try
                    {
                        EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(pattern), copy);
                        Near(copy.Paths[0].Control, new Vector3(3f, 4f, 5f));
                        Require(copy.RepeatCount == 3 && copy.MaximumCount == 1, "Serialization lost settings");
                        Close(copy.StartTime, 0.5f);
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

        private static void Close(float actual, float expected)
        {
            Require(Mathf.Abs(actual - expected) < 0.0001f, $"Expected {expected}, got {actual}");
        }
    }
}
