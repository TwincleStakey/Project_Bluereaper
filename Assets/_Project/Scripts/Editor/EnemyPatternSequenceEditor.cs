using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BlueReaper.Data.Editor
{
    [CustomEditor(typeof(EnemyPatternSequenceData))]
    public sealed class EnemyPatternSequenceEditor : UnityEditor.Editor
    {
        internal bool Embedded { get; set; }
        internal System.Func<EnemyPatternData, EnemyPatternData> EmbeddedStepView { get; set; }
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var sequence = (EnemyPatternSequenceData)target;
            var steps = EmbeddedStepView != null ? sequence.Steps?.Select(EmbeddedStepView).ToArray() : sequence.Steps;
            var invalidSteps = ReuseBeforeCapture(steps).ToArray();
            if (invalidSteps.Length > 0)
                EditorGUILayout.HelpBox($"앞에 CaptureTargets가 없는 ReuseCaptured 스텝 ({string.Join(", ", invalidSteps)})은 실행할 때 건너뜁니다.",
                    MessageType.Warning);
            if (!Embedded && GUILayout.Button("시퀀스 애셋 저장")) AssetDatabase.SaveAssetIfDirty(sequence);
        }

        internal static IEnumerable<int> ReuseBeforeCapture(EnemyPatternData[] steps)
        {
            if (steps == null) yield break;
            for (int i = 0; i < steps.Length; i++)
            {
                var step = steps[i];
                if (!step) continue;
                if (step.PlacementMode == PatternPlacementMode.CaptureTargets) yield break;
                if (step.PlacementMode == PatternPlacementMode.ReuseCaptured) yield return i + 1;
            }
        }
    }
}
