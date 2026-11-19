using System;
using System.Linq;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BlueReaper.Stage.Editor
{
    public sealed class StageEditorWindow : EditorWindow
    {
        private const string StageFolder = "Assets/_Project/Data/Stages";
        private const string PrefabFolder = "Assets/_Project/Prefabs/Stage";

        [SerializeField] private StageData stage;
        [SerializeField] private int fieldIndex;
        private Vector2 scroll;

        [MenuItem("Tools/BlueReaper/Stage Editor")]
        public static void Open()
        {
            var window = GetWindow<StageEditorWindow>("Stage Editor");
            if (Selection.activeObject is StageData selected)
            {
                window.stage = selected;
                window.fieldIndex = 0;
            }
        }

        private void OnEnable()
        {
            minSize = new Vector2(460, 360);
            if (!stage) stage = AssetDatabase.LoadAssetAtPath<StageData>($"{StageFolder}/Stage_Dynamic.asset");
        }

        private void OnGUI()
        {
            using var playMode = new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            try
            {
                DrawStage();
                DrawSelectedField();
            }
            finally
            {
                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawStage()
        {
            EditorGUILayout.LabelField("스테이지 데이터", EditorStyles.boldLabel);
            var newStage = (StageData)EditorGUILayout.ObjectField("Stage Data", stage, typeof(StageData), false);
            if (newStage != stage)
            {
                stage = newStage;
                fieldIndex = 0;
            }
            if (GUILayout.Button("새 StageData 만들기"))
            {
                var path = EditorUtility.SaveFilePanelInProject("새 StageData", "Stage_New", "asset",
                    "스테이지 데이터를 저장할 위치를 선택하세요.", StageFolder);
                if (!string.IsNullOrEmpty(path))
                {
                    if (AssetDatabase.LoadMainAssetAtPath(path))
                    {
                        EditorUtility.DisplayDialog("새 StageData", "같은 경로에 에셋이 있습니다. 새 이름을 사용하세요.", "닫기");
                        return;
                    }
                    stage = CreateInstance<StageData>();
                    AssetDatabase.CreateAsset(stage, path);
                    fieldIndex = 0;
                }
            }
            if (!stage) return;

            using var data = new SerializedObject(stage);
            EditorGUILayout.PropertyField(data.FindProperty(nameof(StageData.Fields)), new GUIContent("필드 진행 순서"), true);
            data.ApplyModifiedProperties();
            if (GUILayout.Button("StageData 저장")) AssetDatabase.SaveAssetIfDirty(stage);
        }

        private void DrawSelectedField()
        {
            if (!stage || stage.Fields == null || stage.Fields.Length == 0)
            {
                EditorGUILayout.HelpBox("스테이지 데이터에 필드 프리팹을 순서대로 추가하세요.", MessageType.Info);
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("동적 필드 프리팹", EditorStyles.boldLabel);
            fieldIndex = Mathf.Clamp(fieldIndex, 0, stage.Fields.Length - 1);
            var names = Array.ConvertAll(stage.Fields, field => field ? field.name : "(프리팹 미지정)");
            fieldIndex = EditorGUILayout.Popup("편집할 필드", fieldIndex, names);
            var prefab = stage.Fields[fieldIndex];
            var path = AssetDatabase.GetAssetPath(prefab);
            if (!prefab || AssetDatabase.LoadMainAssetAtPath(path) != prefab.gameObject)
            {
                EditorGUILayout.HelpBox("StageField가 루트에 있는 프리팹을 연결하세요.", MessageType.Warning);
                return;
            }

            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            bool isEditing = prefabStage != null && prefabStage.assetPath == path;
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("프로젝트에서 찾기")) EditorGUIUtility.PingObject(prefab.gameObject);
                if (!isEditing && GUILayout.Button("Prefab Mode에서 편집")) PrefabStageUtility.OpenPrefab(path);
            }
            if (!isEditing)
            {
                EditorGUILayout.HelpBox("Prefab Mode에서 필드 설정과 웨이브를 편집하고 Unity의 프리팹 저장을 사용하세요.", MessageType.Info);
                return;
            }

            DrawField(prefabStage.prefabContentsRoot.GetComponent<StageField>());
        }

        private static void DrawField(StageField field)
        {
            using var fieldData = new SerializedObject(field);
            EditorGUILayout.PropertyField(fieldData.FindProperty("fieldObject"), new GUIContent("배치 기준 오브젝트"));
            var bossPrefab = fieldData.FindProperty("bossPrefab");
            var bossPoint = fieldData.FindProperty("bossSpawnPoint");
            EditorGUILayout.PropertyField(bossPrefab, new GUIContent("보스 프리팹"));
            EditorGUILayout.PropertyField(bossPoint, new GUIContent("보스 스폰 지점"));
            bool bossField = bossPrefab.objectReferenceValue || bossPoint.objectReferenceValue;
            if (!bossField)
                EditorGUILayout.PropertyField(fieldData.FindProperty("purificationGoal"), new GUIContent("정화 목표 처치 수"));
            EditorGUILayout.PropertyField(fieldData.FindProperty("openingDialogue"), new GUIContent("입장 대사"), true);
            var spawnerProperty = fieldData.FindProperty("dynamicSpawner");
            EditorGUILayout.PropertyField(spawnerProperty, new GUIContent("동적 스포너"));
            var spawner = spawnerProperty.objectReferenceValue as EnemySpawner;
            var layout = fieldData.FindProperty("fieldObject").objectReferenceValue as GameObject;
            fieldData.ApplyModifiedProperties();

            var origin = layout ? layout.transform : field.transform;
            if (origin != field.transform && !origin.IsChildOf(field.transform))
            {
                EditorGUILayout.HelpBox("배치 기준은 필드 프리팹 안의 오브젝트여야 합니다.", MessageType.Warning);
                return;
            }

            var entrances = origin.GetComponentsInChildren<PlayerSpawnEntry>(true);
            var pads = origin.GetComponentsInChildren<StageReadyPad>(true);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(entrances.Length > 0))
                    if (GUILayout.Button("중앙 입구 추가")) AddMarker(origin, "CenterEntrance");
                if (!bossField && GUILayout.Button("준비 발판 추가")) AddMarker(origin, "StageReadyPad");
                if (!bossPoint.objectReferenceValue && GUILayout.Button("보스 스폰 지점 추가"))
                {
                    var point = new GameObject("Boss Spawn Point");
                    point.transform.SetParent(origin, false);
                    Undo.RegisterCreatedObjectUndo(point, "보스 스폰 지점 추가");
                    bossPoint.objectReferenceValue = point.transform;
                    fieldData.ApplyModifiedProperties();
                    Selection.activeGameObject = point;
                    return;
                }
            }

            if (entrances.Length != 1 || !entrances[0].gameObject.activeInHierarchy)
                EditorGUILayout.HelpBox("활성 중앙 입구를 하나 배치하세요.", MessageType.Warning);
            if (!bossField && spawner && pads.Count(IsReadyPadUsable) < 3)
                EditorGUILayout.HelpBox("파티 3명까지 지원하려면 활성 Trigger 발판을 3개 배치하세요.", MessageType.Warning);
            if (!field.GetComponentsInChildren<NavMeshSurface>().Any(surface => surface.navMeshData))
                EditorGUILayout.HelpBox("스폰과 이동에 사용할 NavMesh를 Bake하세요.", MessageType.Warning);

            if (bossField)
            {
                var boss = bossPrefab.objectReferenceValue as BlueReaper.Core.Enemy;
                var point = bossPoint.objectReferenceValue as Transform;
                if (!boss || boss.Rank != BlueReaper.Core.EnemyRank.Boss || !PrefabUtility.IsPartOfPrefabAsset(boss)
                    || !point || !point.IsChildOf(field.transform))
                    EditorGUILayout.HelpBox("Boss 등급 프리팹과 필드 안의 스폰 지점을 연결하세요.", MessageType.Warning);
                if (spawner)
                    EditorGUILayout.HelpBox("보스 필드에서는 동적 스포너 연결을 비우세요.", MessageType.Warning);
                return;
            }

            if (!spawner || (spawner.transform != field.transform && !spawner.transform.IsChildOf(field.transform)))
            {
                EditorGUILayout.HelpBox("필드 안의 EnemySpawner를 연결하세요.", MessageType.Warning);
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("스폰과 웨이브", EditorStyles.boldLabel);
            using var spawnData = new SerializedObject(spawner);
            EditorGUILayout.PropertyField(spawnData.FindProperty(nameof(EnemySpawner.SpawnRadius)), new GUIContent("스폰 반경"));
            EditorGUILayout.PropertyField(spawnData.FindProperty(nameof(EnemySpawner.MaxAliveEnemiesPerPlayer)), new GUIContent("플레이어당 생존 적 정원"));
            EditorGUILayout.PropertyField(spawnData.FindProperty(nameof(EnemySpawner.SpawnInterval)), new GUIContent("스폰 반복 주기 (초)"));
            EditorGUILayout.PropertyField(spawnData.FindProperty(nameof(EnemySpawner.Waves)), new GUIContent("웨이브"), true);
            spawnData.ApplyModifiedProperties();
            if (!spawner.IsConfigured())
                EditorGUILayout.HelpBox("스폰 반경·정원·반복 주기는 양수여야 합니다. 웨이브의 시작 시간·일반/엘리트 프리팹·Count와 순차 급습 간격도 확인하세요.", MessageType.Warning);
        }

        private static bool IsReadyPadUsable(StageReadyPad pad) =>
            pad.isActiveAndEnabled && pad.TryGetComponent(out BoxCollider box) && box.enabled && box.isTrigger;

        private static void AddMarker(Transform origin, string prefabName)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabFolder}/{prefabName}.prefab");
            if (!prefab)
            {
                EditorUtility.DisplayDialog("스테이지 마커", $"{prefabName} 프리팹을 찾을 수 없습니다.", "닫기");
                return;
            }

            var marker = (GameObject)PrefabUtility.InstantiatePrefab(prefab, origin);
            Undo.RegisterCreatedObjectUndo(marker, $"{prefabName} 추가");
            Selection.activeGameObject = marker;
        }
    }
}
