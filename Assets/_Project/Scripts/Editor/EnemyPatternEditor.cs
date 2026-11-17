using System.Linq;
using BlueReaper.Stage;
using UnityEditor;
using UnityEngine;

namespace BlueReaper.Data.Editor
{
    [CustomEditor(typeof(EnemyPatternData))]
    public sealed class EnemyPatternEditor : UnityEditor.Editor
    {
        internal System.Action<PatternImplData> EmbeddedOpenEffect { get; set; }
        internal System.Func<AreaAttackPatternData, AreaAttackPatternData> EmbeddedEffectView { get; set; }
        private bool showPreview = true;
        private Transform caster;
        private StageField field;
        private float previewTime;
        private Vector3 previewFeet;
        private float previewCasterYaw;

        internal static string PlacementHelp(PatternPlacementMode mode) => mode switch
        {
            PatternPlacementMode.CaptureTargets => "전조 직전(전조가 없으면 효과 시작 직전)에 생존 대상의 발밑과 시전자 Y 회전을 확정합니다. 대상이 없으면 좌표를 비우고 이 스텝을 건너뛰고 다음 성공한 캡처까지 좌표를 재사용할 수 없습니다.",
            PatternPlacementMode.ReuseCaptured => "같은 행동에서 마지막으로 확정한 좌표를 사용합니다. 좌표가 비어 있으면 이 스텝을 건너뜁니다.",
            _ => "범위 효과는 시전자·필드 설정을 따르고 탄막은 기존 발사·조준 설정을 사용합니다."
        };

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
            var effects = OrderedEffects(pattern, EmbeddedEffectView);
            bool fixedPlacement = pattern.PlacementMode != PatternPlacementMode.Default;
            float warning = TelegraphDuration(pattern);
            EditorGUILayout.HelpBox(PlacementHelp(pattern.PlacementMode), MessageType.Info);
            if (fixedPlacement)
                EditorGUILayout.HelpBox("고정 좌표 탄막에는 AimAtTargetEachEmission이 적용되지 않습니다. 사망 패턴은 기존 사망 배치를 사용합니다.", MessageType.Info);
            if (EmbeddedOpenEffect == null && GUILayout.Button("패턴 애셋 저장")) AssetDatabase.SaveAssetIfDirty(pattern);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("범위 효과 타이밍", EditorStyles.boldLabel);
            if (effects.Length == 0)
            {
                EditorGUILayout.HelpBox("Implementations에 범위 효과(AreaAttackPatternData)를 연결하면 타이밍과 범위를 미리볼 수 있습니다.",
                    MessageType.Info);
                if (!fixedPlacement || warning <= 0f) return;
            }
            else
            {
                EditorGUILayout.HelpBox("에셋을 열어 범위를 편집할 수 있습니다. 공유 에셋 수정은 이를 참조하는 모든 패턴에 반영됩니다.", MessageType.Info);
            }

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
                    EditorGUILayout.LabelField(effect.name, $"배치 {warning + effect.PlacementDelaySeconds:0.###}초 · 종료 {warning + EndTime(effect):0.###}초"
                        + (effect.ContinueAfterPlacement ? " · 배치까지만 대기" : ""));
                }
                else
                {
                    EditorGUILayout.LabelField(effect.name, "배치·판정 시간 확인");
                }

                if (GUILayout.Button("에셋 열기", GUILayout.Width(75f)))
                {
                    if (EmbeddedOpenEffect != null) EmbeddedOpenEffect(effect);
                    else Selection.activeObject = effect;
                }
            }

            EditorGUI.BeginChangeCheck();
            showPreview = EditorGUILayout.Toggle("SceneView 미리보기", showPreview);
            if (showPreview)
            {
                if (fixedPlacement)
                {
                    previewFeet = EditorGUILayout.Vector3Field("미리보기 발밑 (월드)", previewFeet);
                    previewCasterYaw = EditorGUILayout.FloatField("미리보기 시전자 Y 회전", previewCasterYaw);
                    EditorGUILayout.HelpBox("캡처된 좌표 한 곳을 미리봅니다. 재사용 스텝은 앞선 캡처와 같은 위치·회전을 입력하세요.", MessageType.Info);
                }
                else
                {
                    if (effects.Any(effect => effect.PlacementOrigin == AreaPlacementOrigin.Caster))
                        caster = (Transform)EditorGUILayout.ObjectField("미리보기 시전자", caster, typeof(Transform), true);
                    if (effects.Any(effect => effect.PlacementOrigin == AreaPlacementOrigin.Field))
                    {
                        field = (StageField)EditorGUILayout.ObjectField("미리보기 필드", field, typeof(StageField), true);
                        if (!field) EditorGUILayout.HelpBox("필드를 지정하기 전에는 필드 기준 범위를 월드 원점에 표시합니다.", MessageType.Info);
                    }
                }

                float duration = effects.Where(HasValidTiming).Select(EndTime).DefaultIfEmpty().Max();
                if (pattern.MotionClip) duration = Mathf.Max(duration, pattern.MotionClip.length);
                previewTime = EditorGUILayout.Slider("미리보기 시간 (초)", previewTime, 0f, Mathf.Max(0.01f, warning + duration));
                if (warning > 0f) EditorGUILayout.LabelField("모션 시작", $"{warning:0.###}초 (전조 종료)");
                if (GUILayout.Button("탑뷰에서 순서 보기"))
                {
                    var placement = PreviewPlacement(pattern);
                    Bounds bounds = effects.Length == 0
                        ? new Bounds(previewFeet, Vector3.zero)
                        : AreaPatternPreview.BoundsOf(effects[0], Origin(effects[0]), placement);
                    foreach (var effect in effects.Skip(1))
                        bounds.Encapsulate(AreaPatternPreview.BoundsOf(effect, Origin(effect), placement));
                    AreaPatternPreview.FrameTop(bounds);
                }
            }
            if (EditorGUI.EndChangeCheck()) SceneView.RepaintAll();
        }

        internal static AreaAttackPatternData[] OrderedEffects(EnemyPatternData pattern,
            System.Func<AreaAttackPatternData, AreaAttackPatternData> view = null) =>
            (pattern.Implementations ?? System.Array.Empty<PatternImplData>()).OfType<AreaAttackPatternData>()
                .Where(effect => effect).Select(effect => view?.Invoke(effect) ?? effect)
                .OrderBy(effect => HasValidTiming(effect) ? HitTime(effect) : float.PositiveInfinity).ToArray();

        private static float HitTime(AreaAttackPatternData effect) => Mathf.Max(0f, effect.HitStartSeconds);

        private static float EndTime(AreaAttackPatternData effect) => HitTime(effect) + effect.HitDurationSeconds;

        private static bool HasValidTiming(AreaAttackPatternData effect) =>
            effect.GetValidationError() == null;

        internal static float TelegraphDuration(EnemyPatternData pattern) =>
            pattern.TelegraphPrefab && float.IsFinite(pattern.TelegraphDurationSeconds)
                ? Mathf.Max(0f, pattern.TelegraphDurationSeconds) : 0f;

        private Transform Origin(AreaAttackPatternData effect) =>
            effect.PlacementOrigin == AreaPlacementOrigin.Field ? AreaPatternPreview.FieldOrigin(field) : caster;

        private (Vector3 Position, Quaternion Rotation)? PreviewPlacement(EnemyPatternData pattern) =>
            pattern.PlacementMode == PatternPlacementMode.Default
                ? null : (previewFeet, Quaternion.Euler(0f, previewCasterYaw, 0f));

        internal static bool IsHighlighted(AreaAttackPatternData effect, float time) =>
            HitTime(effect) <= time && time < EndTime(effect);

        private void DrawScene(SceneView view)
        {
            var active = MonsterEditorWindow.ActivePatternPreview;
            if (!showPreview || !target || (active ? active != this : Selection.activeObject != target) || EditorApplication.isPlayingOrWillChangePlaymode
                || Event.current.type != EventType.Repaint) return;
            var pattern = (EnemyPatternData)target;
            var effects = OrderedEffects(pattern, EmbeddedEffectView).Where(HasValidTiming).ToArray();
            float warning = TelegraphDuration(pattern);
            var placement = PreviewPlacement(pattern);
            float time = previewTime - warning;
            if (time < 0f)
            {
                var first = pattern.Implementations?.OfType<AreaAttackPatternData>().FirstOrDefault(effect => effect);
                if (first)
                {
                    first = EmbeddedEffectView?.Invoke(first) ?? first;
                    var shape = AreaPatternPreview.GetShape(first, 0);
                    var origin = Origin(first);
                    AreaPatternPreview.DrawShape(shape, origin, Color.yellow, false, placement: placement);
                    Handles.Label(AreaPatternPreview.Center(shape, origin, placement), "전조 기준");
                }
                else if (placement.HasValue)
                {
                    Handles.Label(placement.Value.Position, "전조 기준");
                }
                return;
            }

            foreach (var effect in effects)
            {
                var origin = Origin(effect);
                var shape = AreaPatternPreview.GetShape(effect, 0);
                bool highlighted = IsHighlighted(effect, time);
                Color color = highlighted ? AreaPatternPreview.SelectedColor : AreaPatternPreview.OutlineColor;
                if (time < effect.PlacementDelaySeconds) color.a *= 0.45f;
                float elapsed = time - HitTime(effect);
                AreaPatternPreview.DrawShape(shape, origin, color, false,
                    evaluated: effect.EvaluateHitShape(elapsed), placement: placement);
                foreach (var additional in effect.AdditionalShapes ?? System.Array.Empty<AreaAttackShapeData>())
                    AreaPatternPreview.DrawShape(additional, origin, color, false,
                        evaluated: effect.EvaluateHitShape(elapsed, additional), placement: placement);
                if (highlighted)
                    Handles.Label(AreaPatternPreview.Center(shape, origin, placement), $"{effect.name} · {warning + HitTime(effect):0.###}초");
            }
        }
    }
}
