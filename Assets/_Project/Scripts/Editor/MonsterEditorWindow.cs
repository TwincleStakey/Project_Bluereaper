using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlueReaper.Boss;
using BlueReaper.Core;
using BlueReaper.HitBox;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace BlueReaper.Data.Editor
{
    public sealed partial class MonsterEditorWindow : EditorWindow
    {
        internal const string TemplatePath = "Assets/_Project/Prefabs/Monsters/Monster_1.prefab";
        private static readonly string[] Tabs = { "기본 정보", "페이즈", "타겟 규칙", "패턴", "공격·피격 판정", "모델·애니메이션" };
        private readonly Dictionary<ScriptableObject, (ScriptableObject Asset, string OriginalJson)> drafts = new();
        [SerializeField] private string prefabPath;
        private GameObject root, template, selectedPrefab, model;
        private int tab, actionIndex;
        private Vector2 scroll, issueScroll;
        private ColliderShape shape;
        private byte[] sourcePrefab;
        private string message;
        private string newMonsterName = "NewMonster";
        private EnemyRank newRank;

        private enum ColliderShape { Box, Sphere, Capsule }

        [MenuItem("Tools/BlueReaper/Monster Editor")]
        public static void Open() => GetWindow<MonsterEditorWindow>("Monster Editor");

        private void OnEnable()
        {
            minSize = new Vector2(705, 440);
            template = AssetDatabase.LoadAssetAtPath<GameObject>(TemplatePath);
            saveChangesMessage = "몬스터 프리팹과 편집 중인 데이터 에셋의 변경을 저장하시겠습니까?";
            if (!string.IsNullOrEmpty(prefabPath) && AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath))
                Load(prefabPath);
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            Unload();
        }

        private void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.ExitingEditMode || !hasUnsavedChanges) return;
            EditorApplication.isPlaying = false;
            message = "Play Mode 전에 몬스터의 변경을 저장하거나 취소하세요.";
            Repaint();
        }

        private void OnGUI()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorGUILayout.HelpBox("편집 모드에서 몬스터 에디터를 사용하세요.", MessageType.Info);
                return;
            }

            template = (GameObject)EditorGUILayout.ObjectField("더미 프리팹", template, typeof(GameObject), false);
            newMonsterName = EditorGUILayout.TextField("새 몬스터 이름", newMonsterName);
            newRank = (EnemyRank)EditorGUILayout.EnumPopup("새 몬스터 등급", newRank);
            string name = newMonsterName.Trim();
            bool validName = IsValidAssetName(name);
            if (validName)
            {
                EditorGUILayout.LabelField($"{PrefabFolder(newRank)}/{name}.prefab", EditorStyles.miniLabel);
                EditorGUILayout.LabelField(StatsPath(newRank, name), EditorStyles.miniLabel);
            }
            else EditorGUILayout.HelpBox("파일명으로 사용할 수 있는 몬스터 이름을 입력하세요.", MessageType.Warning);
            selectedPrefab = (GameObject)EditorGUILayout.ObjectField("기존 몬스터 프리팹", selectedPrefab, typeof(GameObject), false);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!validName || !IsMonsterPrefab(template) || hasUnsavedChanges))
                    if (GUILayout.Button("더미로 새 몬스터 만들기")) Run(Create);
                using (new EditorGUI.DisabledScope(!IsMonsterPrefab(selectedPrefab) || hasUnsavedChanges))
                    if (GUILayout.Button("기존 프리팹 열기")) Run(() => Load(AssetDatabase.GetAssetPath(selectedPrefab)));
            }

            if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, MessageType.Info);
            if (!root) return;

            EditorGUILayout.LabelField(prefabPath, EditorStyles.miniLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!hasUnsavedChanges))
                {
                    if (GUILayout.Button("변경 검토 · 저장")) Run(ReviewAndSave);
                    if (GUILayout.Button("변경 취소")) Run(() =>
                    {
                        if (EditorUtility.DisplayDialog("변경 취소", "이 창의 저장 전 편집 내용을 되돌립니다.", "되돌리기", "계속 편집"))
                            DiscardChanges();
                    });
                }
                using (new EditorGUI.DisabledScope(hasUnsavedChanges))
                    if (GUILayout.Button("Prefab Mode에서 계층 열기")) Run(() =>
                    {
                        string path = prefabPath;
                        Unload();
                        PrefabStageUtility.OpenPrefab(path);
                    });
            }

            tab = GUILayout.Toolbar(tab, Tabs);
            if (tab != 3) ReleasePatternEditor();
            using (new EditorGUILayout.HorizontalScope())
            {
                using (var area = new EditorGUILayout.ScrollViewScope(scroll, GUILayout.MinWidth(400)))
                {
                    scroll = area.scrollPosition;
                    DrawCurrentTab();
                }
                using (var issues = new EditorGUILayout.ScrollViewScope(issueScroll, GUILayout.Width(235)))
                {
                    issueScroll = issues.scrollPosition;
                    EditorGUILayout.LabelField("연결 확인", EditorStyles.boldLabel);
                    string[] warnings = GetIssues().ToArray();
                    if (warnings.Length == 0) EditorGUILayout.LabelField("확인할 연결 없음");
                    foreach (string warning in warnings) EditorGUILayout.HelpBox(warning, MessageType.Warning);
                }
            }
        }

        private void DrawCurrentTab()
        {
            var unit = root.GetComponent<Unit>();
            var enemy = root.GetComponent<Enemy>();
            var ai = root.GetComponent<EnemyAI>();
            switch (tab)
            {
                case 0:
                    DrawProperties(enemy, "rank", "rewardTable");
                    DrawAsset(DrawReference(unit, "data"), "MaxHealth", "Attack", "MoveSpeed");
                    break;
                case 1:
                    DrawAsset(DrawReference(enemy, "phaseInfo"), "PhaseTrigger_HPRatio_Permil");
                    EditorGUILayout.HelpBox("1000 = HP 100%. 인덱스 0부터 1페이즈입니다. 일반 패턴의 Require Phase에 실행할 페이즈를 지정하세요.", MessageType.Info);
                    var phases = View(GetReference<EnemyPhaseData>(enemy, "phaseInfo"));
                    int count = Mathf.Max(1, phases?.PhaseTrigger_HPRatio_Permil?.Length ?? 0);
                    for (int phase = 1; phase <= count; phase++)
                    {
                        string names = string.Join(", ", OrdinaryActions(ai).Where(action => Eligible(action, phase)).Select(action => action.name));
                        EditorGUILayout.LabelField($"{phase}페이즈", names.Length > 0 ? names : "일반 패턴 없음");
                    }
                    DrawAction(ai, "RequirePhase");
                    break;
                case 2:
                    EditorGUILayout.LabelField("AI 추격", EditorStyles.boldLabel);
                    DrawProperties(enemy, "sightRange", "outSightTrackingPercent");
                    DrawProperties(ai, "stopChasingDistance", "chaseTargetRule");
                    EditorGUILayout.LabelField("패턴 또는 시퀀스의 타겟", EditorStyles.boldLabel);
                    DrawAction(ai, "TargetMode", "TargetRule");
                    break;
                case 3:
                    DrawPatternAuthoring(ai);
                    break;
                case 4:
                    DrawColliders();
                    break;
                case 5:
                    DrawModel(ai);
                    break;
            }
        }

        private void DrawAction(EnemyAI ai, params string[] fields)
        {
            var actions = Actions(ai).Distinct().ToArray();
            if (actions.Length == 0) return;
            actionIndex = EditorGUILayout.Popup("연결된 행동", Mathf.Clamp(actionIndex, 0, actions.Length - 1),
                actions.Select(a => a.name).ToArray());
            DrawAsset(actions[actionIndex], fields);
        }

        private void DrawProperties(Object obj, params string[] fields)
        {
            if (!obj) return;
            using var serialized = new SerializedObject(obj);
            foreach (string field in fields)
            {
                var property = serialized.FindProperty(field);
                if (property == null) continue;
                EditorGUILayout.PropertyField(property, true);
            }
            if (serialized.ApplyModifiedProperties()) hasUnsavedChanges = true;
        }

        private ScriptableObject DrawReference(Object owner, string field)
        {
            using var serialized = new SerializedObject(owner);
            var property = serialized.FindProperty(field);
            EditorGUILayout.PropertyField(property);
            if (serialized.ApplyModifiedProperties()) hasUnsavedChanges = true;
            var asset = property.objectReferenceValue as ScriptableObject;
            if (asset && GUILayout.Button("복제 후 연결"))
                Run(() => DuplicateReference(serialized, property));
            return property.objectReferenceValue as ScriptableObject;
        }

        private void DuplicateReference(SerializedObject owner, SerializedProperty slot)
        {
            var asset = (ScriptableObject)slot.objectReferenceValue;
            var rank = root.GetComponent<Enemy>().Rank;
            string path = EditorUtility.SaveFilePanelInProject("데이터 복제", asset.name + "_Copy", "asset",
                $"{DataFolder(rank)} 아래에 데이터 에셋을 저장합니다. 참조는 몬스터를 저장할 때 반영합니다.", DataFolder(rank));
            if (string.IsNullOrEmpty(path)) return;
            CopyDataAndConnect(owner, slot, path, rank);
            hasUnsavedChanges = true;
        }

        internal static void CopyDataAndConnect(SerializedObject owner, SerializedProperty slot, string path, EnemyRank rank)
        {
            if (!IsUnderFolder(path, DataFolder(rank)) || !string.Equals(Path.GetExtension(path), ".asset", StringComparison.OrdinalIgnoreCase))
                throw new IOException($"{DataFolder(rank)} 아래의 .asset 경로를 지정하세요.");
            slot.objectReferenceValue = CopyAsset((ScriptableObject)slot.objectReferenceValue, path);
            owner.ApplyModifiedProperties();
        }

        private ScriptableObject BeginAssetEdit(ScriptableObject asset)
        {
            if (drafts.TryGetValue(asset, out var existing)) return existing.Asset;
            var copy = Instantiate(asset);
            drafts.Add(asset, (copy, EditorJsonUtility.ToJson(asset)));
            hasUnsavedChanges = true;
            return copy;
        }

        private void DrawAsset(ScriptableObject asset, params string[] fields)
        {
            if (!asset) return;
            EditorGUILayout.LabelField(asset.name, EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("에셋 Inspector 열기"))
                {
                    Selection.activeObject = asset;
                    EditorGUIUtility.PingObject(asset);
                }
                if (!drafts.ContainsKey(asset) && GUILayout.Button("이 에셋 편집"))
                {
                    if (EditorUtility.DisplayDialog("에셋 편집",
                        "저장하면 이 에셋을 참조하는 다른 프리팹과 패턴에도 적용됩니다.", "편집", "취소"))
                        BeginAssetEdit(asset);
                }
            }
            using (new EditorGUI.DisabledScope(!drafts.ContainsKey(asset)))
                DrawProperties(View(asset), fields);
        }

        private void DrawColliders()
        {
            int hurtLayer = LayerMask.NameToLayer("Hurtbox");
            shape = (ColliderShape)EditorGUILayout.EnumPopup("새 판정 도형", shape);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("공격 판정 추가"))
                    Run(() => { AddCollider(root, true, (int)shape); hasUnsavedChanges = true; });
                using (new EditorGUI.DisabledScope(hurtLayer < 0))
                    if (GUILayout.Button("피격 판정 추가"))
                        Run(() => { AddCollider(root, false, (int)shape); hasUnsavedChanges = true; });
            }

            foreach (var collider in root.GetComponentsInChildren<Collider>(true))
            {
                bool melee = collider.GetComponent<MeleeHitBox>();
                bool hurt = collider.gameObject.layer == hurtLayer;
                string path = AnimationUtility.CalculateTransformPath(collider.transform, root.transform);
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField($"{(melee ? "공격" : hurt ? "피격" : "기타")} · {path} · {collider.GetType().Name}",
                        EditorStyles.boldLabel);
                    if (!melee && !hurt && hurtLayer >= 0 && collider.gameObject != root && GUILayout.Button("이 오브젝트를 피격 레이어로 지정"))
                    {
                        Undo.RecordObject(collider.gameObject, "Assign Hurtbox Layer");
                        collider.gameObject.layer = hurtLayer;
                        hasUnsavedChanges = true;
                    }
                    if (melee || hurt)
                    {
                        DrawProperties(collider.transform, "m_LocalPosition", "m_LocalRotation", "m_LocalScale");
                        DrawProperties(collider, "m_Center", "m_Size", "m_Radius", "m_Height", "m_Direction", "m_IsTrigger");
                    }
                    if (hurt)
                    {
                        var part = collider.GetComponentInParent<Hurtbox>();
                        if (part) DrawProperties(part, "damageReduction");
                        else if (GUILayout.Button("독립 피격 부위로 지정"))
                        {
                            Undo.AddComponent<Hurtbox>(collider.gameObject);
                            hasUnsavedChanges = true;
                        }
                    }
                }
            }
        }

        private void DrawModel(EnemyAI ai)
        {
            DrawProperties(root.GetComponent<Animator>(), "m_Controller", "m_Avatar");
            model = (GameObject)EditorGUILayout.ObjectField("추가할 모델 프리팹·FBX", model, typeof(GameObject), false);
            using (new EditorGUI.DisabledScope(!model))
                if (GUILayout.Button("시각 모델 추가"))
                    Run(() => { AddVisualModel(root, model); hasUnsavedChanges = true; });
            EditorGUILayout.HelpBox("새 모델 추가 후 아래에서 기존 더미 렌더러를 끄세요. 본에 붙는 판정은 저장 후 Prefab Mode에서 옮길 수 있습니다.", MessageType.Info);
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is not (MeshRenderer or SkinnedMeshRenderer)) continue;
                EditorGUILayout.LabelField(AnimationUtility.CalculateTransformPath(renderer.transform, root.transform),
                    EditorStyles.boldLabel);
                if (renderer is SkinnedMeshRenderer) DrawProperties(renderer, "m_Enabled", "m_Mesh", "m_Materials");
                else
                {
                    DrawProperties(renderer, "m_Enabled", "m_Materials");
                    DrawProperties(renderer.GetComponent<MeshFilter>(), "m_Mesh");
                }
            }
            DrawAction(ai, "MotionClip");
        }

        private IEnumerable<string> GetIssues()
        {
            if (!root.GetComponent<Health>() || !root.GetComponent<NavMeshAgent>())
                yield return "Health 또는 NavMeshAgent가 없습니다.";
            if (!GetReference<UnitData>(root.GetComponent<Unit>(), "data"))
                yield return "UnitData를 지정하세요.";
            var enemy = root.GetComponent<Enemy>();
            var ai = root.GetComponent<EnemyAI>();
            foreach (string pathIssue in PathIssues(enemy, ai)) yield return pathIssue;
            var phases = View(GetReference<EnemyPhaseData>(enemy, "phaseInfo"));
            var thresholds = phases?.PhaseTrigger_HPRatio_Permil;
            if (phases && (thresholds == null || thresholds.Length == 0 || thresholds[0] != 1000
                || thresholds.Zip(thresholds.Skip(1), (a, b) => a <= b).Any(invalid => invalid)))
                yield return "페이즈 임계값은 1000부터 낮아지는 순서로 지정하세요.";
            var ordinary = OrdinaryActions(ai).ToArray();
            for (int phase = 1; phase <= Mathf.Max(1, thresholds?.Length ?? 0); phase++)
                if (!ordinary.Any(action => Eligible(action, phase)))
                    yield return $"{phase}페이즈에 등록된 일반 패턴이 없습니다.";
            int layer = LayerMask.NameToLayer("Hurtbox");
            if (layer < 0 || !root.GetComponentsInChildren<Collider>(true).Any(c => c.gameObject.layer == layer && !c.GetComponent<MeleeHitBox>()))
                yield return "Hurtbox 레이어에 피격용 Collider를 추가하세요.";
            foreach (var box in root.GetComponentsInChildren<MeleeHitBox>(true))
            {
                if (!box.GetComponent<Collider>()) yield return $"{box.name} 공격 판정에 Collider가 없습니다.";
                if (box.gameObject.layer == layer) yield return $"{box.name}에서 공격 판정과 피격 레이어를 분리하세요.";
            }
            if (Actions(ai).OfType<EnemyPatternData>().Any(pattern => View(pattern).Implementations?.Any(effect => effect is MeleeAttackPatternData) == true)
                && root.GetComponentsInChildren<MeleeHitBox>(true).Length == 0)
                yield return "근거리 패턴에 연결할 공격 판정이 없습니다.";
            var animator = root.GetComponent<Animator>();
            if (root.GetComponentsInChildren<Animator>(true).Any(component => component != animator))
                yield return "하위 Animator를 확인하세요. EnemyAI는 루트 Animator를 사용합니다.";
            var controller = animator ? animator.runtimeAnimatorController : null;
            while (controller is AnimatorOverrideController overrides) controller = overrides.runtimeAnimatorController;
            if (controller is not AnimatorController asset || asset.layers.Length == 0)
            {
                yield return "루트 Animator Controller를 지정하세요.";
                yield break;
            }
            var states = StateNames(asset.layers[0].stateMachine).ToHashSet();
            if (!states.Contains("Idle") || !states.Contains("Move") ||
                !asset.parameters.Any(p => p.name == "MoveSpeed" && p.type == AnimatorControllerParameterType.Float))
                yield return "Animator 첫 레이어의 Idle·Move 및 Float MoveSpeed를 확인하세요.";
            var death = GetReference<EnemyPatternData>(ai, "deathPattern");
            foreach (var pattern in Actions(ai).OfType<EnemyPatternData>().Distinct())
            {
                var clip = View(pattern).MotionClip;
                if (!clip || states.Contains(clip.name) || pattern == death && states.Contains("Died")) continue;
                yield return $"{pattern.name}의 첫 레이어에 {clip.name} 상태가 없습니다.";
            }
        }

        private IEnumerable<string> PathIssues(Enemy enemy, EnemyAI ai)
        {
            string prefabFolder = PrefabFolder(enemy.Rank);
            string dataFolder = DataFolder(enemy.Rank);
            if (!IsUnderFolder(prefabPath, prefabFolder))
                yield return $"{enemy.Rank} 프리팹의 권장 경로는 {prefabFolder}입니다. 현재: {prefabPath}";

            var actions = Actions(ai).Distinct().ToArray();
            var effects = actions.OfType<EnemyPatternData>()
                .SelectMany(action => View(action).Implementations ?? Array.Empty<PatternImplData>());
            var assets = actions.Cast<ScriptableObject>().Concat(effects)
                .Append(root.GetComponent<Unit>().Data)
                .Append(GetReference<EnemyPhaseData>(enemy, "phaseInfo"));
            foreach (string path in assets.Where(asset => asset).Select(AssetDatabase.GetAssetPath).Distinct())
                if (!string.IsNullOrEmpty(path) && !IsUnderFolder(path, dataFolder))
                    yield return $"{enemy.Rank} 데이터의 권장 경로는 {dataFolder}입니다. 현재: {path}";
        }

        private bool Eligible(EnemyActionData action, int phase) => View(action).RequirePhase?.Contains((uint)phase) == true;

        private static IEnumerable<string> StateNames(AnimatorStateMachine machine) =>
            machine.states.Select(item => item.state.name)
                .Concat(machine.stateMachines.SelectMany(item => StateNames(item.stateMachine)));

        private IEnumerable<EnemyActionData> Actions(EnemyAI ai)
        {
            foreach (var action in OrdinaryActions(ai)
                .Append(GetReference<EnemyActionData>(ai, "spawnPattern"))
                .Append(GetReference<EnemyPatternData>(ai, "deathPattern")).Where(action => action))
            {
                yield return action;
                if (action is EnemyPatternSequenceData sequence && View(sequence).Steps != null)
                    foreach (var step in View(sequence).Steps.Where(step => step)) yield return step;
            }
        }

        private static IEnumerable<EnemyActionData> OrdinaryActions(EnemyAI ai)
        {
            using var serialized = new SerializedObject(ai);
            var entries = serialized.FindProperty("patterns");
            for (int i = 0; i < entries.arraySize; i++)
                if (entries.GetArrayElementAtIndex(i).objectReferenceValue is EnemyActionData action && action) yield return action;
        }

        private static T GetReference<T>(Object owner, string field) where T : Object
        {
            using var serialized = new SerializedObject(owner);
            return serialized.FindProperty(field).objectReferenceValue as T;
        }

        private T View<T>(T asset) where T : ScriptableObject =>
            asset && drafts.TryGetValue(asset, out var draft) ? (T)draft.Asset : asset;

        private static bool IsMonsterPrefab(GameObject prefab) =>
            prefab && PrefabUtility.IsPartOfPrefabAsset(prefab) &&
            AssetDatabase.GetAssetPath(prefab).EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) &&
            prefab.GetComponent<Unit>() && prefab.GetComponent<Enemy>() && prefab.GetComponent<EnemyAI>();

        private void Load(string path)
        {
            if (!IsMonsterPrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path)))
                throw new InvalidOperationException("Unit, Enemy, EnemyAI가 붙은 몬스터 프리팹을 선택하세요.");
            Unload();
            prefabPath = path;
            selectedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            root = PrefabUtility.LoadPrefabContents(path);
            sourcePrefab = File.ReadAllBytes(path);
            hasUnsavedChanges = false;
        }

        private void Unload()
        {
            ClearPatternAuthoring();
            if (root) PrefabUtility.UnloadPrefabContents(root);
            root = null;
            foreach (var draft in drafts.Values) DestroyImmediate(draft.Asset);
            drafts.Clear();
        }

        private void Create()
        {
            Load(DuplicateMonster(AssetDatabase.GetAssetPath(template), newMonsterName.Trim(), newRank));
        }

        private static string PrefabFolder(EnemyRank rank) =>
            rank == EnemyRank.Boss ? "Assets/_Project/Prefabs/Boss" : "Assets/_Project/Prefabs/Monsters";

        private static string DataFolder(EnemyRank rank) =>
            rank == EnemyRank.Boss ? "Assets/_Project/Data/Bosses" : "Assets/_Project/Data/Monsters";

        private static string StatsPath(EnemyRank rank, string name) => $"{DataFolder(rank)}/{name}_Stats.asset";

        private static bool IsUnderFolder(string path, string folder) =>
            Path.GetFullPath(path).StartsWith(Path.GetFullPath(folder) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

        private static bool IsValidAssetName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || name.EndsWith(".", StringComparison.Ordinal) || name.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) return false;
            string stem = name.Split('.')[0].TrimEnd().ToUpperInvariant();
            return stem is not ("CON" or "PRN" or "AUX" or "NUL")
                && !(stem.Length == 4 && stem[3] is >= '1' and <= '9'
                    && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)));
        }

        internal static string DuplicateMonster(string templatePath, string name, EnemyRank rank)
        {
            if (!IsValidAssetName(name)) throw new IOException("파일명으로 사용할 수 있는 몬스터 이름을 입력하세요.");
            var original = AssetDatabase.LoadAssetAtPath<GameObject>(templatePath);
            if (!IsMonsterPrefab(original)) throw new InvalidOperationException("유효한 몬스터 템플릿을 선택하세요.");
            string path = $"{PrefabFolder(rank)}/{name}.prefab";
            string statsPath = StatsPath(rank, name);
            CheckNewPath(path);
            CheckNewPath(statsPath);
            var templateStats = original.GetComponent<Unit>().Data;
            if (!AssetDatabase.CopyAsset(templatePath, path)) throw new IOException("프리팹을 복제하지 못했습니다.");
            bool copiedStats = false;
            try
            {
                if (templateStats) CopyAsset(templateStats, statsPath);
                else AssetDatabase.CreateAsset(CreateInstance<UnitData>(), statsPath);
                copiedStats = true;
                var contents = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    using var enemy = new SerializedObject(contents.GetComponent<Enemy>());
                    enemy.FindProperty("rank").intValue = (int)rank;
                    enemy.ApplyModifiedPropertiesWithoutUndo();
                    using var unit = new SerializedObject(contents.GetComponent<Unit>());
                    unit.FindProperty("data").objectReferenceValue = AssetDatabase.LoadAssetAtPath<UnitData>(statsPath);
                    unit.ApplyModifiedPropertiesWithoutUndo();
                    PrefabUtility.SaveAsPrefabAsset(contents, path, out bool saved);
                    if (!saved) throw new IOException("새 스탯 참조를 저장하지 못했습니다.");
                }
                finally { PrefabUtility.UnloadPrefabContents(contents); }
            }
            catch
            {
                AssetDatabase.DeleteAsset(path);
                if (copiedStats) AssetDatabase.DeleteAsset(statsPath);
                throw;
            }
            return path;
        }

        private static void CheckNewPath(string path)
        {
            string assets = Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar;
            if (!path.StartsWith("Assets/", StringComparison.Ordinal)
                || !Path.GetFullPath(path).StartsWith(assets, StringComparison.OrdinalIgnoreCase)
                || File.Exists(path) || Directory.Exists(path))
                throw new IOException("기존 파일을 덮어쓰지 않는 Assets 하위의 새 경로를 지정하세요.");
        }

        private static ScriptableObject CopyAsset(ScriptableObject asset, string path)
        {
            CheckNewPath(path);
            if (!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(asset), path))
                throw new IOException("데이터 에셋을 복제하지 못했습니다.");
            return AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
        }

        internal static GameObject AddVisualModel(GameObject root, GameObject asset)
        {
            if (!PrefabUtility.IsPartOfPrefabAsset(asset) || asset.GetComponentsInChildren<Unit>(true).Length != 0 ||
                asset.GetComponentsInChildren<Collider>(true).Length != 0)
                throw new InvalidOperationException("Unit과 Collider가 없는 시각 모델 프리팹 또는 모델 에셋을 선택하세요.");
            var child = (GameObject)PrefabUtility.InstantiatePrefab(asset, root.scene);
            Undo.RegisterCreatedObjectUndo(child, "Add Monster Visual");
            Undo.SetTransformParent(child.transform, root.transform, "Parent Monster Visual");
            child.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            return child;
        }

        internal static Collider AddCollider(GameObject root, bool melee, int shape)
        {
            int layer = melee ? root.layer : LayerMask.NameToLayer("Hurtbox");
            if (layer < 0) throw new InvalidOperationException("Hurtbox 레이어가 없습니다.");
            string label = melee ? "Attack Hitbox" : "Hurtbox";
            string childName = ObjectNames.GetUniqueName(root.transform.Cast<Transform>().Select(child => child.name).ToArray(), label);
            var child = new GameObject(childName);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(child, root.scene);
            Undo.RegisterCreatedObjectUndo(child, "Add Monster Collider");
            Undo.SetTransformParent(child.transform, root.transform, "Parent Monster Collider");
            child.transform.localPosition = Vector3.up;
            child.layer = layer;
            Collider collider = shape switch
            {
                1 => Undo.AddComponent<SphereCollider>(child),
                2 => Undo.AddComponent<CapsuleCollider>(child),
                _ => Undo.AddComponent<BoxCollider>(child)
            };
            collider.isTrigger = true;
            if (melee) Undo.AddComponent<MeleeHitBox>(child);
            else Undo.AddComponent<Hurtbox>(child);
            return collider;
        }

        private void ReviewAndSave()
        {
            var paths = drafts.Keys.Select(AssetDatabase.GetAssetPath).Concat(PendingPatternPaths().Select(item => item.Path)).ToArray();
            string assets = paths.Length == 0 ? "" : "\n데이터 에셋\n" + string.Join("\n", paths);
            var warnings = GetIssues().ToArray();
            string caution = warnings.Length == 0 ? "" : "\n\n확인 항목\n" + string.Join("\n", warnings);
            if (EditorUtility.DisplayDialog("몬스터 변경 저장", prefabPath + assets + caution, "저장", "계속 편집"))
                SaveChanges();
        }

        public override void SaveChanges()
        {
            if (!root) return;
            var activeStage = PrefabStageUtility.GetCurrentPrefabStage();
            if ((activeStage != null && activeStage.assetPath == prefabPath) ||
                !File.Exists(prefabPath) || !sourcePrefab.SequenceEqual(File.ReadAllBytes(prefabPath)))
                throw new IOException("프리팹이 다른 편집기에서 변경됐거나 열려 있습니다. 해당 편집을 끝낸 뒤 다시 여세요.");
            if (!AssetDatabase.IsOpenForEdit(prefabPath) || drafts.Keys.Any(asset => !asset || !AssetDatabase.IsOpenForEdit(asset)))
                throw new IOException("쓰기 가능한 프리팹 및 데이터 에셋인지 확인하세요.");

            if (drafts.Any(pair => EditorJsonUtility.ToJson(pair.Key) != pair.Value.OriginalJson))
                throw new IOException("별도 Inspector에서 데이터가 변경됐습니다. 이 창의 변경을 검토하고 에셋을 다시 여세요.");

            SavePrefabWithPatterns();
            sourcePrefab = File.ReadAllBytes(prefabPath);
            foreach (var (asset, draft) in drafts)
            {
                Undo.RecordObject(asset, "Save Monster Data");
                string name = asset.name;
                EditorUtility.CopySerialized(draft.Asset, asset);
                asset.name = name;
                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssetIfDirty(asset);
            }
            ClearPatternAuthoring();
            foreach (var draft in drafts.Values) DestroyImmediate(draft.Asset);
            drafts.Clear();
            base.SaveChanges();
            message = "저장했습니다.";
        }

        public override void DiscardChanges()
        {
            if (root) Load(prefabPath);
            base.DiscardChanges();
        }

        private void Run(Action action)
        {
            try { message = null; action(); }
            catch (Exception exception) { message = exception.Message; }
        }
    }
}
