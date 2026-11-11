using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BlueReaper.BulletPatterns.Editor
{
    [CustomEditor(typeof(BulletPattern))]
    public sealed class BulletPatternEditor : UnityEditor.Editor
    {
        // shortcut: preview at most 256 paths/instances, use instancing if larger previews are needed.
        private const int PreviewLimit = 256;
        private const float PathPickDistance = 8f;
        private readonly Vector3[] pathPoints = new Vector3[33];
        private readonly List<(int index, float distance)> pathHits = new List<(int, float)>();
        private BulletPattern pattern;
        private float PreviewDuration => pattern.StartTime + pattern.TotalDuration;
        private int selected;
        private float previewTime;
        private bool playing;
        private double previousUpdate;
        private bool heightHandles;
        private bool showHitboxes = true;
        private Vector3 previewOrigin;
        private float previewYaw;
        private GameObject cachedPrefab;
        private MeshFilter[] meshes = Array.Empty<MeshFilter>();
        private Collider[] colliders = Array.Empty<Collider>();
        private static readonly Color SelectedColor = new Color(0.2f, 0.75f, 1f);
        private static readonly Color PathColor = new Color(0.65f, 0.65f, 0.65f);

        private void OnEnable()
        {
            pattern = (BulletPattern)target;
            CachePrefab();
            SceneView.duringSceneGui += DrawScene;
            EditorApplication.update += Tick;
            EditorApplication.projectChanged += RefreshPreview;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            Undo.undoRedoPerformed += RefreshPreview;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= DrawScene;
            EditorApplication.update -= Tick;
            EditorApplication.projectChanged -= RefreshPreview;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            Undo.undoRedoPerformed -= RefreshPreview;
            playing = false;
            SceneView.RepaintAll();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.LabelField("탄막 패턴", EditorStyles.boldLabel);
            Field("BulletPrefab", "탄 프리팹 (Collider 포함)");
            Field("MovementType", "이동 방식");
            bool linear = serializedObject.FindProperty("MovementType").enumValueIndex == (int)BulletMovementType.Linear;
            Field("AimAtTargetEachEmission", "매 발사 회차 타겟 재조준 (XZ)");
            Field("StartTime", "공격 시작 후 발사 지연 (초)");
            if (linear)
            {
                Field("LinearCount", "1회 생성량");
                Field("LinearSpeed", "선형 이동 속도 (초당 거리)");
                Field("LinearLifetime", "생성 후 유지 시간 (초)");
            }
            else
            {
                Field("TravelDuration", "이동 시간 (초)");
                Field("DespawnAtEnd", "끝점 도착 시 디스폰");
            }
            Field("RepeatCount", "총 발사 횟수 (최초 포함)");
            Field("RepeatInterval", "발사 시작 간격 (초)");
            Field("AlignToPath", "진행 방향으로 회전");
            Field("DirectionOffset", "방향 보정 (로컬 Y도)");
            Field("DamageMultiplier", "데미지 배율");
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("배치", EditorStyles.boldLabel);
            if (linear)
            {
                SerializedProperty layoutProperty = serializedObject.FindProperty("Layout");
                layoutProperty.intValue = EditorGUILayout.IntPopup("배치 형태", layoutProperty.intValue,
                    new[] { "Line", "Circle", "Fan" }, new[] { 1, 2, 3 });
            }
            else Field("Layout", "배치 형태");
            Field("LayoutCenter", "배치 중심 (로컬 XYZ)");
            BulletLayout layout = (BulletLayout)serializedObject.FindProperty("Layout").enumValueIndex;
            if (layout == BulletLayout.Line) Field("Spacing", "탄 사이 간격");
            if (layout == BulletLayout.Circle || layout == BulletLayout.Fan) Field("Radius", "배치 반경");
            if (layout == BulletLayout.Fan) Field("FanAngle", "부채꼴 각도");
            ApplyInspectorChanges();

            if (!linear)
            {
                int requested = Mathf.Max(1, EditorGUILayout.DelayedIntField("최대 개수 (1회 발사량)", pattern.MaximumCount));
                if (requested != pattern.MaximumCount
                    && (requested >= pattern.MaximumCount || EditorUtility.DisplayDialog("탄 개수 줄이기",
                        "뒤쪽 탄의 개별 경로가 삭제됩니다. 계속할까요? (Undo로 복구 가능)", "줄이기", "취소")))
                {
                    Change("Change bullet count", () => pattern.Resize(requested));
                    selected = Mathf.Clamp(selected, 0, pattern.MaximumCount - 1);
                    serializedObject.Update();
                }
                using (new EditorGUI.DisabledScope(layout == BulletLayout.Manual || pattern.GetValidationError() != null))
                {
                    if (GUILayout.Button("현재 설정으로 배치 다시 생성") && EditorUtility.DisplayDialog("배치 다시 생성",
                        "경로의 길이·곡률을 유지하며 전체 탄의 위치와 회전을 현재 배치 설정에 맞춥니다. 계속할까요?", "다시 생성", "취소"))
                    {
                        Change("Generate bullet layout", pattern.GenerateLayout);
                        serializedObject.Update();
                    }
                }
                EditorGUILayout.HelpBox("개수를 늘리면 마지막 경로를 독립 복사하고 전체를 균등 재배치합니다. Manual은 배치 없이 복사합니다.\n배치와 회전은 경로 전체에 적용하며, 길이·곡률·높이 차이는 유지합니다. 경로 길이는 S/E 좌표로 조정합니다.", MessageType.Info);

                if (pattern.MaximumCount > 0)
                {
                    EditorGUILayout.Space();
                    int next = EditorGUILayout.IntSlider("편집할 탄", selected + 1, 1, pattern.MaximumCount) - 1;
                    if (next != selected) { selected = next; SceneView.RepaintAll(); }
                    selected = Mathf.Clamp(selected, 0, pattern.MaximumCount - 1);
                    SerializedProperty path = serializedObject.FindProperty("Paths").GetArrayElementAtIndex(selected);
                    EditorGUILayout.PropertyField(path.FindPropertyRelative("Preset"), new GUIContent("경로 프리셋"));
                    EditorGUILayout.PropertyField(path.FindPropertyRelative("Start"), new GUIContent("S 시작점 (XYZ)"));
                    if ((BulletPathPreset)path.FindPropertyRelative("Preset").enumValueIndex != BulletPathPreset.Straight)
                        EditorGUILayout.PropertyField(path.FindPropertyRelative("Control"), new GUIContent("C 제어점 (XYZ)"));
                    EditorGUILayout.PropertyField(path.FindPropertyRelative("End"), new GUIContent("E 끝점 (XYZ)"));
                    ApplyInspectorChanges();
                }
            }

            DrawPreviewInspector();
            if (GUILayout.Button("이 패턴 저장")) AssetDatabase.SaveAssetIfDirty(pattern);
        }

        private void Field(string name, string label)
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty(name), new GUIContent(label));
        }

        private void ApplyInspectorChanges()
        {
            if (!serializedObject.ApplyModifiedProperties()) return;
            playing = false;
            CachePrefab();
            SceneView.RepaintAll();
        }

        private void DrawPreviewInspector()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("SceneView 미리보기", EditorStyles.boldLabel);
            string error = pattern.GetValidationError();
            if (error != null) EditorGUILayout.HelpBox(error, MessageType.Error);
            if (pattern.BulletPrefab == null)
                EditorGUILayout.HelpBox("프리팹을 지정하면 메시와 Collider를 표시합니다. 미지정 시 탄 위치 마커로 미리보기합니다.", MessageType.Info);
            else if (!PrefabUtility.IsPartOfPrefabAsset(pattern.BulletPrefab))
                EditorGUILayout.HelpBox("Project 창의 프리팹 에셋을 지정해주세요.", MessageType.Warning);
            else
            {
                if (colliders.Length == 0) EditorGUILayout.HelpBox("프리팹에 3D Collider가 없습니다.", MessageType.Warning);
                if (meshes.Length == 0) EditorGUILayout.HelpBox("MeshFilter가 없어 위치 마커로 표시합니다. 애니메이션·파티클은 재생하지 않습니다.", MessageType.Info);
                foreach (Collider collider in colliders)
                    if (collider is MeshCollider)
                    { EditorGUILayout.HelpBox("MeshCollider는 공유 메시의 경계 박스로 표시합니다.", MessageType.Info); break; }
            }
            if (pattern.MaximumCount * (long)pattern.RepeatCount > PreviewLimit)
                EditorGUILayout.HelpBox($"SceneView에는 최대 {PreviewLimit}개 경로·탄 인스턴스를 표시합니다. 에셋의 발사량은 유지됩니다.", MessageType.Info);
            EditorGUI.BeginChangeCheck();
            if (pattern.MovementType == BulletMovementType.Path)
                heightHandles = EditorGUILayout.Toggle("Y 높이 포함 3D 핸들", heightHandles);
            showHitboxes = EditorGUILayout.Toggle("히트박스 표시", showHitboxes);
            previewOrigin = EditorGUILayout.Vector3Field("미리보기 원점", previewOrigin);
            previewYaw = EditorGUILayout.FloatField("미리보기 원점 회전 (Y도)", previewYaw);
            if (EditorGUI.EndChangeCheck()) SceneView.RepaintAll();
            using (new EditorGUI.DisabledScope(error != null || EditorApplication.isPlayingOrWillChangePlaymode))
            {
                float duration = error == null ? PreviewDuration : 1f;
                EditorGUI.BeginChangeCheck();
                previewTime = EditorGUILayout.Slider("전체 경과 시간 (초)", previewTime, 0f, duration);
                if (EditorGUI.EndChangeCheck()) { playing = false; SceneView.RepaintAll(); }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(playing ? "일시정지" : "재생"))
                    {
                        playing = !playing;
                        if (playing && previewTime >= duration) previewTime = 0f;
                        previousUpdate = EditorApplication.timeSinceStartup;
                    }
                    if (GUILayout.Button("처음으로")) { playing = false; previewTime = 0f; SceneView.RepaintAll(); }
                    if (GUILayout.Button("XZ 탑뷰")) FrameTopView();
                }
            }
            EditorGUILayout.HelpBox(pattern.MovementType == BulletMovementType.Linear
                ? "선형 탄막은 속도와 유지 시간으로 이동하며 만료 시 회수합니다. 선은 최대 이동 거리를 표시합니다."
                : "경로 선을 클릭해 탄을 선택합니다. 겹친 경로는 반복 클릭으로 순환 선택합니다.\nS/C/E를 드래그하고 XYZ는 위에서 입력합니다.", MessageType.Info);
            EditorGUILayout.HelpBox("미리보기 원점과 회전은 저장되지 않습니다. 타겟 재조준은 게임에서 적용하며 여기서는 원점 회전을 사용합니다.\n패턴 전체를 시작 간격마다 발사하며, 충돌·데미지·프리팹 스크립트는 실행하지 않습니다.", MessageType.Info);
        }

        private void Change(string undoName, Action change)
        {
            Undo.RecordObject(pattern, undoName);
            change();
            EditorUtility.SetDirty(pattern);
            playing = false;
            Repaint();
            SceneView.RepaintAll();
        }

        private void Tick()
        {
            if (!playing || pattern == null) return;
            double now = EditorApplication.timeSinceStartup;
            previewTime = Mathf.Min(PreviewDuration, previewTime + (float)(now - previousUpdate));
            previousUpdate = now;
            if (previewTime >= PreviewDuration || pattern.GetValidationError() != null) playing = false;
            Repaint();
            SceneView.RepaintAll();
        }

        private void OnPlayModeChanged(PlayModeStateChange state) { playing = false; SceneView.RepaintAll(); }
        private void RefreshPreview() { playing = false; CachePrefab(); Repaint(); SceneView.RepaintAll(); }

        private void CachePrefab()
        {
            cachedPrefab = pattern == null ? null : pattern.BulletPrefab;
            meshes = cachedPrefab == null ? Array.Empty<MeshFilter>() : cachedPrefab.GetComponentsInChildren<MeshFilter>(true);
            colliders = cachedPrefab == null ? Array.Empty<Collider>() : cachedPrefab.GetComponentsInChildren<Collider>(true);
        }

        private Matrix4x4 PreviewMatrix => Matrix4x4.TRS(previewOrigin, Quaternion.Euler(0f, previewYaw, 0f), Vector3.one);

        private void FrameTopView()
        {
            SceneView view = SceneView.lastActiveSceneView ?? EditorWindow.GetWindow<SceneView>();
            Bounds bounds = new Bounds(previewOrigin, Vector3.one);
            for (int i = 0; i < Mathf.Min(pattern.MaximumCount, PreviewLimit); i++)
            {
                if (pattern.MovementType == BulletMovementType.Linear)
                {
                    if (!pattern.TryGetLinearLaunch(i, out Vector3 start, out Vector3 direction)) continue;
                    bounds.Encapsulate(PreviewMatrix.MultiplyPoint3x4(start));
                    bounds.Encapsulate(PreviewMatrix.MultiplyPoint3x4(start + direction * (pattern.LinearSpeed * pattern.LinearLifetime)));
                    continue;
                }
                BulletPath path = pattern.Paths[i];
                bounds.Encapsulate(PreviewMatrix.MultiplyPoint3x4(path.Start));
                bounds.Encapsulate(PreviewMatrix.MultiplyPoint3x4(path.End));
                if (path.Preset != BulletPathPreset.Straight) bounds.Encapsulate(PreviewMatrix.MultiplyPoint3x4(path.Control));
            }
            view.in2DMode = false;
            view.orthographic = true;
            view.size = Mathf.Max(5f, bounds.extents.magnitude * 1.2f);
            view.LookAtDirect(bounds.center, Quaternion.Euler(90f, 0f, 0f));
            view.Repaint();
        }

        private void DrawScene(SceneView view)
        {
            if (pattern == null || pattern.GetValidationError() != null || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (cachedPrefab != pattern.BulletPrefab) CachePrefab();
            selected = Mathf.Clamp(selected, 0, pattern.MaximumCount - 1);
            using (new Handles.DrawingScope(PreviewMatrix))
            {
                if (pattern.MovementType == BulletMovementType.Linear)
                {
                    for (int i = 0; i < Mathf.Min(pattern.MaximumCount, PreviewLimit); i++)
                    {
                        if (!pattern.TryGetLinearLaunch(i, out Vector3 start, out Vector3 direction)) continue;
                        Handles.color = PathColor;
                        Handles.DrawLine(start, start + direction * (pattern.LinearSpeed * pattern.LinearLifetime));
                        Handles.ArrowHandleCap(0, start, Quaternion.LookRotation(direction),
                            HandleUtility.GetHandleSize(start) * 0.5f, EventType.Repaint);
                    }
                }
                else
                {
                    for (int i = 0; i < Mathf.Min(pattern.MaximumCount, PreviewLimit); i++) DrawPath(i);
                    if (selected >= PreviewLimit) DrawPath(selected);
                    HandlePathSelection();
                    BulletPath path = pattern.Paths[selected];
                    EditPoint("S", path.Start, value => path.Start = value);
                    if (path.Preset != BulletPathPreset.Straight) EditPoint("C", path.Control, value => path.Control = value);
                    EditPoint("E", path.End, value => path.End = value);
                }
            }
            if (Event.current.type == EventType.Repaint) DrawBullets();
        }

        private void DrawPath(int index)
        {
            BulletPath path = pattern.Paths[index];
            Handles.color = index == selected ? SelectedColor : PathColor;
            if (path.Preset == BulletPathPreset.Curve)
                Handles.DrawBezier(path.Start, path.End, path.Start + (path.Control - path.Start) * (2f / 3f),
                    path.End + (path.Control - path.End) * (2f / 3f), Handles.color, null, index == selected ? 3f : 2f);
            else if (path.Preset == BulletPathPreset.Corner) Handles.DrawAAPolyLine(path.Start, path.Control, path.End);
            else Handles.DrawLine(path.Start, path.End);
            float size = HandleUtility.GetHandleSize(path.Start) * 0.06f;
            if (index != selected && Handles.Button(path.Start, Quaternion.identity, size, size * 1.4f, Handles.DotHandleCap))
            { selected = index; Repaint(); }
            Handles.Label(path.Start + Vector3.left * size * 2f, $"#{index + 1}");
        }

        private void HandlePathSelection()
        {
            int control = GUIUtility.GetControlID(FocusType.Passive);
            Event current = Event.current;
            if (current.type != EventType.Layout && current.type != EventType.MouseDown) return;
            pathHits.Clear();
            for (int i = 0; i < Mathf.Min(pattern.MaximumCount, PreviewLimit); i++) AddPathHit(i);
            if (selected >= PreviewLimit) AddPathHit(selected);
            if (pathHits.Count == 0) return;
            float nearest = float.PositiveInfinity;
            foreach (var hit in pathHits) nearest = Mathf.Min(nearest, hit.distance);
            if (current.type == EventType.Layout)
            {
                // Keep point handles ahead of line picking while allowing an 8-pixel click target.
                HandleUtility.AddControl(control, nearest * 0.5f + 0.5f);
            }
            else if (current.button == 0 && !current.alt && !Tools.viewToolActive
                && GUIUtility.hotControl == 0 && HandleUtility.nearestControl == control)
            {
                selected = PickPath();
                current.Use();
                Repaint();
                SceneView.RepaintAll();
            }
        }

        private void AddPathHit(int index)
        {
            BulletPath path = pattern.Paths[index];
            for (int i = 0; i < pathPoints.Length; i++) pathPoints[i] = path.Evaluate(i / (float)(pathPoints.Length - 1));
            float distance = HandleUtility.DistanceToPolyLine(pathPoints);
            if (distance <= PathPickDistance) pathHits.Add((index, distance));
        }

        private int PickPath()
        {
            if (pathHits.Count == 0) return selected;
            float nearest = float.PositiveInfinity;
            int nearestIndex = selected;
            foreach (var hit in pathHits)
                if (hit.distance < nearest) { nearest = hit.distance; nearestIndex = hit.index; }
            pathHits.RemoveAll(hit => hit.distance > nearest + 2f);
            int current = pathHits.FindIndex(hit => hit.index == selected);
            return current < 0 ? nearestIndex : pathHits[(current + 1) % pathHits.Count].index;
        }

        private void EditPoint(string label, Vector3 point, Action<Vector3> assign)
        {
            Handles.color = SelectedColor;
            float size = HandleUtility.GetHandleSize(point) * 0.09f;
            Handles.Label(point + Vector3.right * size, label);
            EditorGUI.BeginChangeCheck();
            Vector3 value = heightHandles ? Handles.PositionHandle(point, Quaternion.identity)
                : Handles.Slider2D(point, Vector3.up, Vector3.right, Vector3.forward, size, Handles.DotHandleCap, Vector2.zero);
            if (EditorGUI.EndChangeCheck()) Change("Move bullet path point", () => assign(value));
        }

        private void DrawBullets()
        {
            float elapsed = previewTime - pattern.StartTime;
            if (elapsed < 0f) return;
            int last = pattern.RepeatInterval == 0f ? pattern.RepeatCount - 1
                : (int)Math.Min(pattern.RepeatCount - 1d, Math.Floor((double)elapsed / pattern.RepeatInterval));
            int drawn = 0;
            for (int emission = last; emission >= 0 && drawn < PreviewLimit; emission--)
            {
                if (!pattern.TryEvaluate(0, emission, elapsed, out _, out _)) break;
                for (int i = 0; i < pattern.MaximumCount && drawn < PreviewLimit; i++)
                {
                    if (!pattern.TryEvaluate(i, emission, elapsed, out Vector3 position, out Quaternion rotation)) continue;
                    Matrix4x4 pose = PreviewMatrix * Matrix4x4.TRS(position, rotation *
                        (cachedPrefab == null ? Quaternion.identity : cachedPrefab.transform.localRotation),
                        cachedPrefab == null ? Vector3.one : cachedPrefab.transform.localScale);
                    DrawPrefab(pose, PreviewMatrix.MultiplyPoint3x4(position));
                    drawn++;
                }
            }
        }

        private void DrawPrefab(Matrix4x4 pose, Vector3 fallbackPosition)
        {
            bool rendered = false;
            foreach (MeshFilter filter in meshes)
            {
                MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
                if (filter.sharedMesh == null || renderer == null || !renderer.enabled || !ActiveInPrefab(filter.transform)) continue;
                Matrix4x4 matrix = pose * cachedPrefab.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                Material[] materials = renderer.sharedMaterials;
                for (int submesh = 0; submesh < filter.sharedMesh.subMeshCount; submesh++)
                {
                    if (materials.Length == 0) break;
                    Material material = materials[Mathf.Min(submesh, materials.Length - 1)];
                    if (material == null) continue;
                    // SceneView has already rendered its camera when duringSceneGui runs.
                    if (!material.SetPass(0)) continue;
                    Graphics.DrawMeshNow(filter.sharedMesh, matrix, submesh);
                    rendered = true;
                }
            }
            if (!rendered)
            {
                using (new Handles.DrawingScope(SelectedColor))
                    Handles.SphereHandleCap(0, fallbackPosition, Quaternion.identity, HandleUtility.GetHandleSize(fallbackPosition) * 0.1f, EventType.Repaint);
            }
            if (!showHitboxes) return;
            foreach (Collider collider in colliders)
            {
                if (!collider.enabled || !ActiveInPrefab(collider.transform)) continue;
                Matrix4x4 matrix = pose * cachedPrefab.transform.worldToLocalMatrix * collider.transform.localToWorldMatrix;
                using (new Handles.DrawingScope(new Color(0.3f, 1f, 0.5f))) DrawCollider(collider, matrix);
            }
        }

        private bool ActiveInPrefab(Transform node)
        {
            for (Transform current = node; current != null; current = current.parent)
            {
                if (!current.gameObject.activeSelf) return false;
                if (current == cachedPrefab.transform) break;
            }
            return true;
        }

        private static void DrawCollider(Collider collider, Matrix4x4 matrix)
        {
            Vector3 scale = new Vector3(matrix.GetColumn(0).magnitude, matrix.GetColumn(1).magnitude, matrix.GetColumn(2).magnitude);
            if (collider is BoxCollider box)
            {
                using (new Handles.DrawingScope(matrix)) Handles.DrawWireCube(box.center, box.size);
            }
            else if (collider is SphereCollider sphere)
                DrawSphere(matrix.MultiplyPoint3x4(sphere.center), sphere.radius * Mathf.Max(scale.x, scale.y, scale.z));
            else if (collider is CapsuleCollider capsule)
            {
                Vector3 localAxis = capsule.direction == 0 ? Vector3.right : capsule.direction == 1 ? Vector3.up : Vector3.forward;
                Vector3 axis = matrix.MultiplyVector(localAxis).normalized;
                Vector3 side = Mathf.Abs(Vector3.Dot(axis, Vector3.up)) < 0.99f
                    ? Vector3.Cross(axis, Vector3.up).normalized : Vector3.right;
                Vector3 other = Vector3.Cross(axis, side);
                float axisScale = scale[capsule.direction];
                float radius = capsule.radius * Mathf.Max(scale[(capsule.direction + 1) % 3], scale[(capsule.direction + 2) % 3]);
                Vector3 offset = axis * Mathf.Max(0f, capsule.height * axisScale * 0.5f - radius);
                Vector3 center = matrix.MultiplyPoint3x4(capsule.center);
                Vector3 top = center + offset, bottom = center - offset;
                Handles.DrawWireDisc(top, axis, radius);
                Handles.DrawWireDisc(bottom, axis, radius);
                foreach (Vector3 radial in new[] { side, -side, other, -other })
                    Handles.DrawLine(top + radial * radius, bottom + radial * radius);
                Handles.DrawWireArc(top, side, other, 180f, radius);
                Handles.DrawWireArc(top, other, -side, 180f, radius);
                Handles.DrawWireArc(bottom, side, -other, 180f, radius);
                Handles.DrawWireArc(bottom, other, side, 180f, radius);
            }
            else if (collider is MeshCollider mesh && mesh.sharedMesh != null)
            {
                // shortcut: show MeshCollider bounds, add triangle wireframes when exact mesh hitboxes are required.
                using (new Handles.DrawingScope(matrix)) Handles.DrawWireCube(mesh.sharedMesh.bounds.center, mesh.sharedMesh.bounds.size);
            }
        }

        private static void DrawSphere(Vector3 center, float radius)
        {
            Handles.DrawWireDisc(center, Vector3.up, radius);
            Handles.DrawWireDisc(center, Vector3.right, radius);
            Handles.DrawWireDisc(center, Vector3.forward, radius);
        }
    }
}
