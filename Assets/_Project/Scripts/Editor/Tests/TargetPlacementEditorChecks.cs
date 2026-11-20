using System;
using System.Linq;
using BlueReaper.BulletPatterns;
using BlueReaper.Data.Editor;
using BlueReaper.HitBox;
using BlueReaper.Vfx;
using UnityEditor;
using UnityEngine;

namespace BlueReaper.Data
{
    public static class TargetPlacementEditorChecks
    {
        private const string ExamplePath =
            "Assets/_Project/Data/Bosses/TargetPlacementExample/TargetPlacementExample.asset";

        [MenuItem("Tools/BlueReaper/Check Target Placement Editors")]
        public static void RunBatch()
        {
            CheckInspector();
            CheckExample();
            CheckPreview();
        }

        private static void CheckInspector()
        {
            string path = AssetDatabase.GenerateUniqueAssetPath("Assets/TargetPlacementCheck.asset");
            var pattern = ScriptableObject.CreateInstance<EnemyPatternData>();
            AssetDatabase.CreateAsset(pattern, path);
            try
            {
                var inspector = UnityEditor.Editor.CreateEditor(pattern);
                try
                {
                    Require(inspector is EnemyPatternEditor, "Pattern custom Inspector");
                    var property = inspector.serializedObject.FindProperty(nameof(EnemyPatternData.PlacementMode));
                    Require(property.enumValueIndex == (int)PatternPlacementMode.Default
                        && property.enumNames.Length == 3
                        && Enum.GetValues(typeof(PatternPlacementMode)).Cast<PatternPlacementMode>()
                            .Select(EnemyPatternEditor.PlacementHelp).Distinct().Count() == 3, "Three placement modes");

                    Undo.IncrementCurrentGroup();
                    property.enumValueIndex = (int)PatternPlacementMode.CaptureTargets;
                    Require(inspector.serializedObject.ApplyModifiedProperties(), "Inspector serialized edit");
                    Undo.FlushUndoRecordObjects();
                    Undo.PerformUndo();
                    Require(pattern.PlacementMode == PatternPlacementMode.Default, "Undo placement mode");
                    Undo.PerformRedo();
                    Require(pattern.PlacementMode == PatternPlacementMode.CaptureTargets, "Redo placement mode");
                    AssetDatabase.SaveAssetIfDirty(pattern);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(inspector);
                }

                Resources.UnloadAsset(pattern);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                Require(AssetDatabase.LoadAssetAtPath<EnemyPatternData>(path).PlacementMode
                    == PatternPlacementMode.CaptureTargets, "Saved placement mode reloaded");
            }
            finally
            {
                AssetDatabase.DeleteAsset(path);
            }
        }

        private static void CheckExample()
        {
            var sequence = AssetDatabase.LoadAssetAtPath<EnemyPatternSequenceData>(ExamplePath);
            Require(sequence && sequence.Steps.Length == 3, "Example sequence");
            var inspector = UnityEditor.Editor.CreateEditor(sequence);
            try
            {
                Require(inspector is EnemyPatternSequenceEditor, "Sequence custom Inspector");
                var steps = inspector.serializedObject.FindProperty(nameof(EnemyPatternSequenceData.Steps));
                Require(steps.arraySize == 3 && steps.GetArrayElementAtIndex(0).objectReferenceValue == sequence.Steps[0],
                    "Sequence steps serialized");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(inspector);
            }

            var (capture, volley, blast) = (sequence.Steps[0], sequence.Steps[1], sequence.Steps[2]);
            Require(!EnemyPatternSequenceEditor.ReuseBeforeCapture(sequence.Steps).Any(), "Valid sequence has no warning");
            Require(EnemyPatternSequenceEditor.ReuseBeforeCapture(new[] { volley, null, blast, capture, volley })
                .SequenceEqual(new[] { 1, 3 }), "Warning identifies only reuse before capture");
            Require(capture.PlacementMode == PatternPlacementMode.CaptureTargets
                && capture.TelegraphPrefab && capture.TelegraphDurationSeconds > 0f
                && !capture.TelegraphPrefab.GetComponentInChildren<VfxAutoDespawn>(true)
                && capture.Implementations.Single() is AreaAttackPatternData area && area.GetValidationError() == null,
                "Capture telegraph and area wired");
            Require(volley.PlacementMode == PatternPlacementMode.ReuseCaptured
                && volley.Implementations.Single() is BulletPatternData bullets && bullets.BulletPrefab
                && !bullets.AimAtTargetEachEmission && bullets.GetValidationError() == null
                && blast.PlacementMode == PatternPlacementMode.ReuseCaptured
                && blast.Implementations.Single() is AreaAttackPatternData explosion && explosion.AttackVfxPrefab
                && explosion.GetValidationError() == null
                && explosion.HitStartSeconds > bullets.StartTime + bullets.TotalDuration,
                "Captured volley and delayed explosion wired");
        }

        private static void CheckPreview()
        {
            var shape = new AreaAttackShapeData { Shape = AreaHitShape.Rectangle,
                LocalCenter = new Vector3(2f, 1f, 0f), LocalYaw = 30f };
            var obj = new GameObject("Preview check");
            try
            {
                var origin = obj.transform;
                origin.SetPositionAndRotation(new Vector3(40f, 4f, 30f), Quaternion.Euler(0f, 15f, 0f));
                origin.localScale = Vector3.one * 2f;
                Require(Vector3.Distance(AreaPatternPreview.Center(shape, origin), origin.TransformPoint(shape.LocalCenter))
                    < 0.001f && Quaternion.Angle(AreaPatternPreview.Facing(shape, origin), Quaternion.Euler(0f, 45f, 0f))
                    < 0.01f, "Default preview retains transform and yaw");
                var placement = (new Vector3(5f, 3f, 7f), Quaternion.Euler(0f, 90f, 0f));
                Require(Vector3.Distance(AreaPatternPreview.Center(shape, origin, placement), new Vector3(5f, 4f, 5f))
                    < 0.001f && Quaternion.Angle(AreaPatternPreview.Facing(shape, origin, placement), Quaternion.Euler(0f, 120f, 0f))
                    < 0.01f, "Fixed preview uses feet and caster yaw with unit scale");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(obj);
            }
        }

        private static void Require(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException(name);
            Debug.Log($"Target placement editor PASS: {name}");
        }
    }
}
