using System.Linq;
using BlueReaper.Stage;
using UnityEditor;
using UnityEngine;

namespace BlueReaper.Data.Editor
{
    [CustomEditor(typeof(EnemyPatternData))]
    public sealed class EnemyPatternEditor : UnityEditor.Editor
    {
        private bool showPreview = true;
        private Transform caster;
        private StageField field;
        private float previewTime;

        private void OnEnable()
        {
            SceneView.duringSceneGui += DrawScene;
            Undo.undoRedoPerformed += Refresh;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= DrawScene;
            Undo.undoRedoPerformed -= Refresh;
            SceneView.RepaintAll();
        }

        private void Refresh()
        {
            Repaint();
            SceneView.RepaintAll();
        }

        public override void OnInspectorGUI()
        {
            if (DrawDefaultInspector()) Refresh();
            var pattern = (EnemyPatternData)target;
            var effects = OrderedEffects(pattern);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("범위 효과 타이밍", EditorStyles.boldLabel);
            if (effects.Length == 0)
            {
                EditorGUILayout.HelpBox("Implementations에 범위 효과(AreaAttackPatternData)를 연결하면 타이밍과 범위를 미리볼 수 있습니다.",
                    MessageType.Info);
                return;
            }

            if (GUILayout.Button("패턴 애셋 저장")) AssetDatabase.SaveAssetIfDirty(pattern);
            float warning = TelegraphDuration(pattern);
            EditorGUILayout.HelpBox("에셋을 열어 범위를 편집할 수 있습니다. 공유 에셋 수정은 이를 참조하는 모든 패턴에 반영됩니다.", MessageType.Info);
            foreach (var effect in effects)
            {
                using var row = new EditorGUILayout.HorizontalScope();
                if (HasValidTiming(effect))
                {
                    if (GUILayout.Button($"{warning + HitTime(effect):0.###}초", GUILayout.Width(70f)))
                    {
                        previewTime = warning + HitTime(effect);
                        Refresh();
                    }
                    EditorGUILayout.LabelField(effect.name, effect.RepeatHits
                        ? $"배치 {warning + effect.PlacementDelaySeconds:0.###}초 · 반복"
                        : $"배치 {warning + effect.PlacementDelaySeconds:0.###}초");
                }
                else
                {
                    EditorGUILayout.LabelField(effect.name, "배치·판정 시간 확인");
                }

                if (GUILayout.Button("에셋 열기", GUILayout.Width(75f)))
                    Selection.activeObject = effect;
            }

            EditorGUI.BeginChangeCheck();
            showPreview = EditorGUILayout.Toggle("SceneView 미리보기", showPreview);
            if (showPreview)
            {
                if (effects.Any(effect => effect.PlacementOrigin == AreaPlacementOrigin.Caster))
                    caster = (Transform)EditorGUILayout.ObjectField("미리보기 시전자", caster, typeof(Transform), true);
                if (effects.Any(effect => effect.PlacementOrigin == AreaPlacementOrigin.Field))
                {
                    field = (StageField)EditorGUILayout.ObjectField("미리보기 필드", field, typeof(StageField), true);
                    if (!field) EditorGUILayout.HelpBox("필드를 지정하기 전에는 필드 기준 범위를 월드 원점에 표시합니다.", MessageType.Info);
                }

                float duration = effects.Where(HasValidTiming).Select(EndTime).DefaultIfEmpty().Max();
                if (pattern.MotionClip) duration = Mathf.Max(duration, pattern.MotionClip.length);
                previewTime = EditorGUILayout.Slider("미리보기 시간 (초)", previewTime, 0f, Mathf.Max(0.01f, warning + duration));
                if (warning > 0f) EditorGUILayout.LabelField("모션 시작", $"{warning:0.###}초 (전조 종료)");
                if (GUILayout.Button("탑뷰에서 순서 보기"))
                {
                    Bounds bounds = AreaPatternPreview.BoundsOf(effects[0], Origin(effects[0]));
                    foreach (var effect in effects.Skip(1))
                        bounds.Encapsulate(AreaPatternPreview.BoundsOf(effect, Origin(effect)));
                    AreaPatternPreview.FrameTop(bounds);
                }
            }
            if (EditorGUI.EndChangeCheck()) SceneView.RepaintAll();
        }

        internal static AreaAttackPatternData[] OrderedEffects(EnemyPatternData pattern) =>
            (pattern.Implementations ?? System.Array.Empty<PatternImplData>()).OfType<AreaAttackPatternData>()
                .Where(effect => effect).OrderBy(effect => HasValidTiming(effect) ? HitTime(effect) : float.PositiveInfinity).ToArray();

        private static float HitTime(AreaAttackPatternData effect) => Mathf.Max(0f, effect.HitStartSeconds);

        private static float EndTime(AreaAttackPatternData effect) =>
            HitTime(effect) + (effect.RepeatHits ? Mathf.Max(0f, effect.HitDurationSeconds) : 0f);

        private static bool HasValidTiming(AreaAttackPatternData effect) =>
            float.IsFinite(effect.HitStartSeconds) && effect.PlacementDelaySeconds >= 0f
            && effect.PlacementDelaySeconds <= HitTime(effect)
            && (!effect.RepeatHits || (float.IsFinite(effect.HitDurationSeconds) && float.IsFinite(effect.HitRepeatIntervalSeconds)));

        internal static float TelegraphDuration(EnemyPatternData pattern) =>
            pattern.TelegraphPrefab && float.IsFinite(pattern.TelegraphDurationSeconds)
                ? Mathf.Max(0f, pattern.TelegraphDurationSeconds) : 0f;

        private Transform Origin(AreaAttackPatternData effect) =>
            effect.PlacementOrigin == AreaPlacementOrigin.Field ? AreaPatternPreview.FieldOrigin(field) : caster;

        internal static bool IsHighlighted(AreaAttackPatternData effect, float time, float lastHit)
        {
            float hit = HitTime(effect);
            return hit <= time && (hit == lastHit || (effect.RepeatHits && time < EndTime(effect)));
        }

        private void DrawScene(SceneView view)
        {
            if (!showPreview || !target || Selection.activeObject != target || EditorApplication.isPlayingOrWillChangePlaymode
                || Event.current.type != EventType.Repaint) return;
            var pattern = (EnemyPatternData)target;
            var effects = OrderedEffects(pattern).Where(HasValidTiming).ToArray();
            float warning = TelegraphDuration(pattern);
            float time = previewTime - warning;
            if (time < 0f)
            {
                var first = pattern.Implementations?.OfType<AreaAttackPatternData>().FirstOrDefault(effect => effect);
                if (first)
                {
                    var shape = AreaPatternPreview.GetShape(first, 0);
                    var origin = Origin(first);
                    AreaPatternPreview.DrawShape(shape, origin, Color.yellow, false);
                    Handles.Label(AreaPatternPreview.Center(shape, origin), "전조 기준");
                }
                return;
            }

            float lastHit = effects.Where(effect => HitTime(effect) <= time + 0.0001f)
                .Select(HitTime).DefaultIfEmpty(float.NegativeInfinity).Max();
            foreach (var effect in effects)
            {
                var origin = Origin(effect);
                var shape = AreaPatternPreview.GetShape(effect, 0);
                bool highlighted = IsHighlighted(effect, time + 0.0001f, lastHit);
                Color color = highlighted ? AreaPatternPreview.SelectedColor : AreaPatternPreview.OutlineColor;
                if (time < effect.PlacementDelaySeconds) color.a *= 0.45f;
                AreaPatternPreview.DrawShape(shape, origin, color, false);
                foreach (var additional in effect.AdditionalShapes ?? System.Array.Empty<AreaAttackShapeData>())
                    AreaPatternPreview.DrawShape(additional, origin, color, false);
                if (highlighted)
                    Handles.Label(AreaPatternPreview.Center(shape, origin), $"{effect.name} · {warning + HitTime(effect):0.###}초");
            }
        }
    }
}
