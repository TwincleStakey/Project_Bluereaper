using BlueReaper.Data;
using BlueReaper.HitBox;
using BlueReaper.Stage;
using UnityEditor;
using UnityEngine;

namespace BlueReaper.Data.Editor
{
    [CustomEditor(typeof(AreaAttackPatternData))]
    public sealed class AreaAttackPatternEditor : UnityEditor.Editor
    {
        internal bool Embedded { get; set; }
        private const string Shapes = nameof(AreaAttackPatternData.AdditionalShapes);
        private int selectedShape;
        private bool showPreview = true;
        private bool editHeight;
        private float previewElapsed;
        private Transform caster;
        private StageField field;

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
            serializedObject.Update();
            var effect = (AreaAttackPatternData)target;
            DrawField(nameof(effect.PlacementOrigin), "배치 기준");
            var additional = serializedObject.FindProperty(Shapes);
            selectedShape = Mathf.Clamp(selectedShape, 0, additional.arraySize);
            var labels = new string[additional.arraySize + 1];
            labels[0] = "기본 도형";
            for (int i = 1; i < labels.Length; i++) labels[i] = $"추가 도형 {i}";
            int next = EditorGUILayout.Popup("편집할 도형", selectedShape, labels);
            if (next != selectedShape) SelectShape(next);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("도형 추가")) AddShape(additional);
                using (new EditorGUI.DisabledScope(selectedShape == 0))
                    if (GUILayout.Button("선택 도형 삭제"))
                    {
                        additional.DeleteArrayElementAtIndex(selectedShape - 1);
                        SelectShape(Mathf.Min(selectedShape, additional.arraySize));
                    }
            }

            ShapeField(nameof(effect.Shape), "형태");
            var kind = (AreaHitShape)ShapeProperty(nameof(effect.Shape)).enumValueIndex;
            ShapeField(nameof(effect.LocalCenter), "중심 (로컬 XYZ)");
            if (kind is AreaHitShape.Rectangle or AreaHitShape.Sector) ShapeField(nameof(effect.LocalYaw), "방향 (로컬 Y도)");
            ShapeField(nameof(effect.Height), "판정 높이");
            if (kind == AreaHitShape.Rectangle) ShapeField(nameof(effect.RectangleSize), "가로 X · 깊이 Z");
            else ShapeField(nameof(effect.Radius), kind == AreaHitShape.Donut ? "바깥 반경" : "반경");
            if (kind == AreaHitShape.Sector) ShapeField(nameof(effect.SectorAngle), "부채꼴 각도");
            if (kind == AreaHitShape.Donut)
            {
                ShapeField(nameof(effect.InnerRadius), "안쪽 반경");
                if (ShapeProperty(nameof(effect.InnerRadius)).floatValue >= ShapeProperty(nameof(effect.Radius)).floatValue)
                    EditorGUILayout.HelpBox("안쪽 반경은 바깥 반경보다 작게 설정하세요.", MessageType.Warning);
            }

            EditorGUILayout.Space();
            DrawField(nameof(effect.PlacementDelaySeconds), "배치 지연 (초)");
            DrawField(nameof(effect.ContinueAfterPlacement), "배치까지만 대기");
            DrawField(nameof(effect.HitStartSeconds), "첫 판정 (초)");
            DrawField(nameof(effect.ExpansionMode), "범위 변화");
            var expansion = (AreaExpansionMode)serializedObject.FindProperty(nameof(effect.ExpansionMode)).enumValueIndex;
            if (expansion != AreaExpansionMode.None)
            {
                DrawField(nameof(effect.ExpansionDurationSeconds), "확대 시간 (초)");
                if (expansion == AreaExpansionMode.Filled)
                    DrawField(nameof(effect.ExpansionStartScale), "시작 크기 비율");
                else
                    DrawField(nameof(effect.RingStartRadius), "시작 바깥 반경");
            }
            DrawField(nameof(effect.RepeatHits), "반복 판정");
            if (serializedObject.FindProperty(nameof(effect.RepeatHits)).boolValue)
            {
                DrawField(nameof(effect.HitDurationSeconds), "판정 유지 (초)");
                DrawField(nameof(effect.HitRepeatIntervalSeconds), "판정 간격 (초)");
            }
            DrawField(nameof(effect.DamageMultiplier), "피해 배율");
            DrawField(nameof(effect.AttackVfxPrefab), "폭발 VFX");
            DrawField(nameof(effect.HitVfxPrefab), "피격 VFX");
            DrawField(nameof(effect.HitVfxDelaySeconds), "피격 VFX 지연 (초)");
            if (serializedObject.ApplyModifiedProperties()) Refresh();
            string error = effect.GetValidationError();
            if (error != null) EditorGUILayout.HelpBox(error, MessageType.Warning);
            if (!Embedded && GUILayout.Button("범위 효과 애셋 저장")) AssetDatabase.SaveAssetIfDirty(effect);

            EditorGUILayout.Space();
            EditorGUI.BeginChangeCheck();
            showPreview = EditorGUILayout.Toggle("SceneView 미리보기", showPreview);
            if (showPreview)
            {
                editHeight = EditorGUILayout.Toggle("높이 편집", editHeight);
                if (effect.ExpansionMode != AreaExpansionMode.None && error == null)
                    previewElapsed = EditorGUILayout.Slider("확대 미리보기 (초)", previewElapsed, 0f, effect.ExpansionDurationSeconds);
                if (effect.PlacementOrigin == AreaPlacementOrigin.Field)
                    field = (StageField)EditorGUILayout.ObjectField("미리보기 필드", field, typeof(StageField), true);
                else caster = (Transform)EditorGUILayout.ObjectField("미리보기 시전자", caster, typeof(Transform), true);
                if (!Origin(effect))
                    EditorGUILayout.HelpBox("기준 오브젝트를 지정하기 전에는 월드 원점에서 표시합니다.", MessageType.Info);
                if (GUILayout.Button("탑뷰에서 범위 보기"))
                    AreaPatternPreview.FrameTop(AreaPatternPreview.BoundsOf(effect, Origin(effect)));
            }
            if (EditorGUI.EndChangeCheck()) SceneView.RepaintAll();
        }

        private Transform Origin(AreaAttackPatternData effect) =>
            effect.PlacementOrigin == AreaPlacementOrigin.Field ? AreaPatternPreview.FieldOrigin(field) : caster;

        private void SelectShape(int index)
        {
            selectedShape = index;
            Refresh();
        }

        private SerializedProperty ShapeProperty(string name) => selectedShape == 0
            ? serializedObject.FindProperty(name)
            : serializedObject.FindProperty(Shapes).GetArrayElementAtIndex(selectedShape - 1).FindPropertyRelative(name);

        private void DrawField(string name, string label) =>
            EditorGUILayout.PropertyField(serializedObject.FindProperty(name), new GUIContent(label));

        private void ShapeField(string name, string label) =>
            EditorGUILayout.PropertyField(ShapeProperty(name), new GUIContent(label));

        private void AddShape(SerializedProperty shapes)
        {
            shapes.arraySize++;
            SelectShape(shapes.arraySize);
            var defaults = new AreaAttackShapeData();
            ShapeProperty(nameof(defaults.Shape)).enumValueIndex = (int)defaults.Shape;
            ShapeProperty(nameof(defaults.LocalCenter)).vector3Value = defaults.LocalCenter;
            ShapeProperty(nameof(defaults.LocalYaw)).floatValue = defaults.LocalYaw;
            ShapeProperty(nameof(defaults.Height)).floatValue = defaults.Height;
            ShapeProperty(nameof(defaults.Radius)).floatValue = defaults.Radius;
            ShapeProperty(nameof(defaults.InnerRadius)).floatValue = defaults.InnerRadius;
            ShapeProperty(nameof(defaults.RectangleSize)).vector2Value = defaults.RectangleSize;
            ShapeProperty(nameof(defaults.SectorAngle)).floatValue = defaults.SectorAngle;
        }

        private void DrawScene(SceneView view)
        {
            var active = MonsterEditorWindow.ActivePatternPreview;
            if (!showPreview || !target || (active ? active != this : Selection.activeObject != target)
                || EditorApplication.isPlayingOrWillChangePlaymode) return;
            var effect = (AreaAttackPatternData)target;
            var origin = Origin(effect);
            int count = effect.AdditionalShapes?.Length ?? 0;
            bool showExpansion = Event.current.type == EventType.Repaint && effect.ExpansionMode != AreaExpansionMode.None
                && effect.GetValidationError() == null;
            selectedShape = Mathf.Clamp(selectedShape, 0, count);
            for (int i = 0; i <= count; i++)
            {
                int control = GUIUtility.GetControlID(FocusType.Passive);
                bool selected = i == selectedShape;
                var shape = AreaPatternPreview.GetShape(effect, i);
                AreaPatternPreview.DrawShape(shape, origin,
                    selected ? AreaPatternPreview.SelectedColor : AreaPatternPreview.OutlineColor, selected && editHeight, control);
                if (showExpansion && shape != null)
                    AreaPatternPreview.DrawShape(shape, origin, Color.yellow, false,
                        evaluated: effect.EvaluateHitShape(previewElapsed, i == 0 ? null : shape));
                var input = Event.current;
                if (input.type == EventType.MouseDown && input.button == 0 && !input.alt
                    && GUIUtility.hotControl == 0 && HandleUtility.nearestControl == control)
                {
                    SelectShape(i);
                    input.Use();
                }
            }
            DrawShapeHandles(AreaPatternPreview.GetShape(effect, selectedShape), origin);
        }

        private void DrawShapeHandles(AreaAttackShapeData shape, Transform origin)
        {
            if (shape == null) return;
            var center = AreaPatternPreview.Center(shape, origin);
            var facing = AreaPatternPreview.Facing(shape, origin);
            var forward = facing * Vector3.forward;
            var right = facing * Vector3.right;
            float size = HandleUtility.GetHandleSize(center) * 0.08f;
            using var scope = new Handles.DrawingScope(AreaPatternPreview.SelectedColor);
            Handles.Label(center, selectedShape == 0 ? "기본 도형" : $"추가 도형 {selectedShape}");
            serializedObject.Update();

            EditorGUI.BeginChangeCheck();
            Vector3 moved = editHeight ? Handles.PositionHandle(center, origin ? origin.rotation : Quaternion.identity)
                : Handles.Slider2D(center, origin ? origin.up : Vector3.up, origin ? origin.right : Vector3.right,
                    origin ? origin.forward : Vector3.forward, size, Handles.DotHandleCap, Vector2.zero);
            if (EditorGUI.EndChangeCheck())
            {
                Vector3 local = origin ? origin.InverseTransformPoint(moved) : moved;
                if (!editHeight) local.y = shape.LocalCenter.y;
                ShapeProperty(nameof(shape.LocalCenter)).vector3Value = local;
            }

            if (shape.Shape == AreaHitShape.Rectangle)
            {
                Vector2 half = Vector2.Max(Vector2.one * 0.005f, shape.RectangleSize * 0.5f);
                float width = SlideDistance(center, right, half.x, size, 0.005f);
                float depth = SlideDistance(center, forward, half.y, size, 0.005f);
                if (width != half.x || depth != half.y)
                    ShapeProperty(nameof(shape.RectangleSize)).vector2Value = 2f * new Vector2(width, depth);
            }
            else
            {
                float outer = Mathf.Max(0.01f, shape.Radius);
                float radius = SlideDistance(center, forward, outer, size,
                    shape.Shape == AreaHitShape.Donut ? Mathf.Max(0.01f, shape.InnerRadius + 0.01f) : 0.01f);
                if (radius != outer) ShapeProperty(nameof(shape.Radius)).floatValue = radius;
                if (shape.Shape == AreaHitShape.Donut)
                {
                    float inner = Mathf.Max(0f, shape.InnerRadius);
                    float adjusted = SlideDistance(center, right, inner, size, 0f, Mathf.Max(0f, radius - 0.01f));
                    if (adjusted != inner) ShapeProperty(nameof(shape.InnerRadius)).floatValue = adjusted;
                }
                if (shape.Shape == AreaHitShape.Sector)
                {
                    var edge = Quaternion.AngleAxis(shape.SectorAngle * 0.5f, Vector3.up) * forward;
                    EditorGUI.BeginChangeCheck();
                    var movedEdge = Handles.Slider2D(center + edge * outer, Vector3.up, right, forward,
                        size, Handles.RectangleHandleCap, Vector2.zero);
                    if (EditorGUI.EndChangeCheck())
                    {
                        float delta = Vector3.SignedAngle(edge, movedEdge - center, Vector3.up);
                        ShapeProperty(nameof(shape.SectorAngle)).floatValue = Mathf.Clamp(shape.SectorAngle + 2f * delta, 1f, 360f);
                    }
                }
            }

            if (shape.Shape is AreaHitShape.Rectangle or AreaHitShape.Sector)
            {
                EditorGUI.BeginChangeCheck();
                var rotated = Handles.Disc(facing, center, Vector3.up, Mathf.Max(size * 2f, 0.25f), false, 0f);
                if (EditorGUI.EndChangeCheck())
                    ShapeProperty(nameof(shape.LocalYaw)).floatValue =
                        Mathf.DeltaAngle(origin ? origin.eulerAngles.y : 0f, rotated.eulerAngles.y);
            }
            if (editHeight)
            {
                float halfHeight = Mathf.Max(0.005f, shape.Height * 0.5f);
                float adjusted = SlideDistance(center, Vector3.up, halfHeight, size, 0.005f);
                if (adjusted != halfHeight) ShapeProperty(nameof(shape.Height)).floatValue = adjusted * 2f;
            }
            if (serializedObject.ApplyModifiedProperties()) Refresh();
        }

        private static float SlideDistance(Vector3 center, Vector3 axis, float distance, float size,
            float min, float max = float.PositiveInfinity)
        {
            EditorGUI.BeginChangeCheck();
            Vector3 moved = Handles.Slider(center + axis * distance, axis, size, Handles.DotHandleCap, 0f);
            return EditorGUI.EndChangeCheck() ? Mathf.Clamp(Vector3.Dot(moved - center, axis), min, max) : distance;
        }
    }
}
