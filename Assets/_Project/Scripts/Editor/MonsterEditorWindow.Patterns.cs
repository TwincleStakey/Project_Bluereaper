using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlueReaper.Boss;
using BlueReaper.BulletPatterns;
using BlueReaper.BulletPatterns.Editor;
using BlueReaper.Core;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BlueReaper.Data.Editor
{
    public sealed partial class MonsterEditorWindow
    {
        internal enum ActionSlot { Normal, Spawn, Death }
        private enum EffectKind { Melee, Bullet, Area, Summon }
        private static readonly Type[] EffectTypes =
        {
            typeof(MeleeAttackPatternData), typeof(BulletPatternData), typeof(AreaAttackPatternData), typeof(SummonPatternData)
        };

        private readonly Dictionary<ScriptableObject, string> newPatterns = new();
        private readonly Stack<ScriptableObject> patternHistory = new();
        private ScriptableObject selectedPattern;
        private UnityEditor.Editor patternEditor;
        internal static UnityEditor.Editor ActivePatternPreview { get; private set; }
        private ActionSlot createSlot;
        private EffectKind effectKind;
        private bool createSequence;
        private string actionName = "", childName = "";

        private void DrawPatternAuthoring(EnemyAI ai)
        {
            DrawProperties(ai, "patterns", "spawnPattern", "deathPattern");
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("새 행동 만들기", EditorStyles.boldLabel);
            createSlot = (ActionSlot)EditorGUILayout.EnumPopup("연결 슬롯", createSlot);
            using (new EditorGUI.DisabledScope(createSlot == ActionSlot.Death))
                createSequence = EditorGUILayout.Toggle("시퀀스", createSequence && createSlot != ActionSlot.Death);
            actionName = EditorGUILayout.TextField("에셋 이름", actionName);
            if (GUILayout.Button("행동 생성 후 연결"))
                Run(() =>
                {
                    bool occupied = createSlot == ActionSlot.Spawn && GetReference<EnemyActionData>(ai, "spawnPattern") ||
                        createSlot == ActionSlot.Death && GetReference<EnemyPatternData>(ai, "deathPattern");
                    if (!occupied || EditorUtility.DisplayDialog("슬롯 교체", "저장하면 이 슬롯의 연결이 새 행동으로 교체됩니다.", "교체", "취소"))
                        ChangePattern(StageAction(ai, createSlot, createSequence, actionName));
                });

            using (var owner = new SerializedObject(ai))
            {
                DrawLinks(owner.FindProperty("patterns"));
                DrawLink(owner.FindProperty("spawnPattern"), "스폰");
                DrawLink(owner.FindProperty("deathPattern"), "사망");
            }

            if (selectedPattern && !AuthoredGraph().Contains(selectedPattern)) ChangePattern(null);
            if (!selectedPattern) return;
            EditorGUILayout.Space();
            if (patternHistory.Count > 0 && GUILayout.Button("상위 편집으로 돌아가기"))
            {
                ReleasePatternEditor();
                selectedPattern = patternHistory.Pop();
                return;
            }
            EditorGUILayout.LabelField(selectedPattern.name, EditorStyles.boldLabel);
            if (newPatterns.TryGetValue(selectedPattern, out string pendingName))
                EditorGUILayout.LabelField($"{DataFolder(root.GetComponent<Enemy>().Rank)}/{pendingName}.asset", EditorStyles.miniLabel);
            var data = View(selectedPattern);
            bool editable = newPatterns.ContainsKey(selectedPattern) || drafts.ContainsKey(selectedPattern);
            if (!editable)
            {
                EditorGUILayout.HelpBox("이 에셋은 다른 패턴이나 몬스터에서도 참조할 수 있습니다. 현재 연결만 바꾸려면 해당 행에서 복제하세요.", MessageType.Info);
                if (GUILayout.Button("공유 에셋 편집") && EditorUtility.DisplayDialog("공유 에셋 편집",
                    "저장하면 이 에셋을 사용하는 모든 참조에 적용됩니다.", "편집", "취소"))
                {
                    data = BeginAssetEdit(selectedPattern);
                    editable = true;
                }
            }
            if (editable) DrawEmbeddedPattern(data);
            else ReleasePatternEditor();

            switch (data)
            {
                case EnemyPatternSequenceData:
                    DrawChildCreator(selectedPattern, typeof(EnemyPatternData), "스텝 생성 후 추가", editable);
                    using (var sequence = new SerializedObject(data)) DrawLinks(sequence.FindProperty("Steps"), editable);
                    break;
                case EnemyPatternData:
                    effectKind = (EffectKind)EditorGUILayout.EnumPopup("효과 종류", effectKind);
                    DrawChildCreator(selectedPattern, EffectTypes[(int)effectKind], "효과 생성 후 추가", editable);
                    using (var pattern = new SerializedObject(data)) DrawLinks(pattern.FindProperty("Implementations"), editable);
                    break;
            }
        }

        private void DrawChildCreator(ScriptableObject parent, Type type, string button, bool editable)
        {
            using (new EditorGUI.DisabledScope(!editable))
            {
                childName = EditorGUILayout.TextField("새 에셋 이름", childName);
                if (GUILayout.Button(button))
                    Run(() => ChangePattern(StageChild(parent, type, childName)));
            }
        }

        private void DrawLinks(SerializedProperty array, bool editable = true)
        {
            for (int index = 0; index < array.arraySize; index++)
                if (DrawLink(array.GetArrayElementAtIndex(index), (index + 1).ToString(), editable)) break;
        }

        private bool DrawLink(SerializedProperty slot, string label, bool editable = true)
        {
            if (slot.objectReferenceValue is not ScriptableObject asset || !asset) return false;
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button($"{label} · {asset.name}")) ChangePattern(asset);
                using (new EditorGUI.DisabledScope(!editable))
                {
                    if (!GUILayout.Button("복제 후 연결", GUILayout.Width(104))) return false;
                    Run(() =>
                    {
                        var copy = Instantiate(View(asset));
                        try
                        {
                            StagePattern(copy, NextCopyName(asset.name));
                            SetLink(slot, copy);
                        }
                        catch
                        {
                            newPatterns.Remove(copy);
                            DestroyImmediate(copy);
                            throw;
                        }
                        ChangePattern(copy);
                    });
                    return true;
                }
            }
        }

        private string NextCopyName(string original)
        {
            string folder = DataFolder(root.GetComponent<Enemy>().Rank);
            string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{original}_Copy.asset");
            string candidate = Path.GetFileNameWithoutExtension(path);
            int number = 2;
            while (newPatterns.Values.Contains(candidate, StringComparer.OrdinalIgnoreCase) ||
                File.Exists($"{folder}/{candidate}.asset"))
                candidate = $"{original}_Copy_{number++}";
            return candidate;
        }

        private void DrawEmbeddedPattern(ScriptableObject data)
        {
            var editor = PreparePatternEditor(data);
            if (editor is EnemyPatternEditor or AreaAttackPatternEditor or BulletPatternEditor or EnemyPatternSequenceEditor)
                editor.OnInspectorGUI();
            else editor.DrawDefaultInspector();
        }

        private UnityEditor.Editor PreparePatternEditor(ScriptableObject data)
        {
            UnityEditor.Editor.CreateCachedEditor(data, null, ref patternEditor);
            ActivePatternPreview = patternEditor;
            switch (patternEditor)
            {
                case EnemyPatternEditor pattern:
                    pattern.EmbeddedEffectView = View;
                    pattern.EmbeddedOpenEffect = effect => EditorApplication.delayCall += () =>
                    {
                        if (this && root && tab == 3 && selectedPattern && View(selectedPattern) == data)
                            ChangePattern(drafts.FirstOrDefault(pair => pair.Value.Asset == effect).Key ?? effect);
                    };
                    break;
                case AreaAttackPatternEditor area:
                    area.Embedded = true;
                    break;
                case BulletPatternEditor bullet:
                    bullet.Embedded = true;
                    break;
                case EnemyPatternSequenceEditor sequence:
                    sequence.Embedded = true;
                    sequence.EmbeddedStepView = View;
                    break;
            }
            return patternEditor;
        }

        private void ChangePattern(ScriptableObject next)
        {
            if (selectedPattern == next) return;
            ReleasePatternEditor();
            if (!next) patternHistory.Clear();
            else if (selectedPattern) patternHistory.Push(selectedPattern);
            selectedPattern = next;
            Repaint();
        }

        private void ReleasePatternEditor()
        {
            if (!patternEditor) return;
            if (ActivePatternPreview == patternEditor) ActivePatternPreview = null;
            DestroyImmediate(patternEditor);
            patternEditor = null;
            SceneView.RepaintAll();
        }

        private void ClearPatternAuthoring()
        {
            ChangePattern(null);
            patternHistory.Clear();
            ReleasePatternEditor();
            foreach (var asset in newPatterns.Keys) DestroyImmediate(asset);
            newPatterns.Clear();
        }

        internal EnemyActionData StageAction(EnemyAI ai, ActionSlot slot, bool sequence, string name)
        {
            if (slot == ActionSlot.Death && sequence) throw new InvalidOperationException("사망 슬롯은 단일 패턴만 사용할 수 있습니다.");
            var type = sequence ? typeof(EnemyPatternSequenceData) : typeof(EnemyPatternData);
            var action = (EnemyActionData)NewPattern(type, name);
            using var owner = new SerializedObject(ai);
            var property = slot switch
            {
                ActionSlot.Normal => AppendSlot(owner.FindProperty("patterns")),
                ActionSlot.Spawn => owner.FindProperty("spawnPattern"),
                _ => owner.FindProperty("deathPattern")
            };
            SetLink(property, action);
            return action;
        }

        internal ScriptableObject StageChild(ScriptableObject parent, Type type, string name)
        {
            if (!newPatterns.ContainsKey(parent) && !drafts.ContainsKey(parent))
                throw new InvalidOperationException("상위 에셋을 복제하거나 편집 모드로 여세요.");
            var data = View(parent);
            string field = data switch
            {
                EnemyPatternSequenceData when type == typeof(EnemyPatternData) => "Steps",
                EnemyPatternData when EffectTypes.Contains(type) => "Implementations",
                _ => throw new InvalidOperationException("이 항목에는 해당 에셋을 연결할 수 없습니다.")
            };
            var child = NewPattern(type, name);
            using var owner = new SerializedObject(data);
            SetLink(AppendSlot(owner.FindProperty(field)), child);
            return child;
        }

        private static SerializedProperty AppendSlot(SerializedProperty array)
        {
            int index = array.arraySize;
            array.arraySize++;
            return array.GetArrayElementAtIndex(index);
        }

        private void SetLink(SerializedProperty property, ScriptableObject asset)
        {
            property.objectReferenceValue = asset;
            property.serializedObject.ApplyModifiedProperties();
            hasUnsavedChanges = true;
        }

        private ScriptableObject NewPattern(Type type, string name)
        {
            var asset = CreateInstance(type);
            try { StagePattern(asset, name); }
            catch { DestroyImmediate(asset); throw; }
            return asset;
        }

        private void StagePattern(ScriptableObject asset, string name)
        {
            name = name.Trim();
            if (!IsValidAssetName(name) || name.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                throw new IOException("확장자를 제외한 에셋 이름을 입력하세요.");
            if (newPatterns.Values.Contains(name, StringComparer.OrdinalIgnoreCase))
                throw new IOException("저장 대기 중인 같은 이름의 에셋이 있습니다.");
            CheckNewPath($"{DataFolder(root.GetComponent<Enemy>().Rank)}/{name}.asset");
            asset.name = name;
            newPatterns.Add(asset, name);
            hasUnsavedChanges = true;
        }

        private IEnumerable<ScriptableObject> AuthoredGraph()
        {
            var actions = Actions(root.GetComponent<EnemyAI>()).Distinct().ToArray();
            return actions.Cast<ScriptableObject>().Concat(actions.OfType<EnemyPatternData>()
                .SelectMany(pattern => View(pattern).Implementations ?? Array.Empty<PatternImplData>()))
                .Where(asset => asset).Distinct();
        }

        private IEnumerable<(ScriptableObject Asset, string Path)> PendingPatternPaths()
        {
            string directory = DataFolder(root.GetComponent<Enemy>().Rank);
            var linked = AuthoredGraph().ToHashSet();
            foreach (var owner in drafts.Keys.OfType<EnemyActionData>())
            {
                var data = View(owner);
                IEnumerable<EnemyPatternData> patterns = data switch
                {
                    EnemyPatternSequenceData sequence => sequence.Steps ?? Array.Empty<EnemyPatternData>(),
                    EnemyPatternData pattern => new[] { pattern },
                    _ => Array.Empty<EnemyPatternData>()
                };
                foreach (var pattern in patterns.Where(pattern => pattern))
                {
                    linked.Add(pattern);
                    linked.UnionWith(View(pattern).Implementations ?? Array.Empty<PatternImplData>());
                }
            }
            return newPatterns.Where(item => linked.Contains(item.Key))
                .OrderBy(item => item.Key is PatternImplData ? 0 : item.Key is EnemyPatternData ? 1 : 2)
                .Select(item => (item.Key, $"{directory}/{item.Value}.asset"));
        }

        private void SavePrefabWithPatterns()
        {
            var paths = PendingPatternPaths().ToArray();
            foreach (var item in paths) CheckNewPath(item.Path);
            var savedAssets = new Dictionary<Object, Object>();
            var createdPaths = new List<string>();
            var owners = drafts.Values.Select(draft => (Object)draft.Asset)
                .Append(root.GetComponent<EnemyAI>()).ToArray();

            try
            {
                foreach (var item in paths)
                {
                    var saved = Instantiate(item.Asset);
                    saved.name = item.Asset.name;
                    RemapPatternLinks(saved, savedAssets);
                    AssetDatabase.CreateAsset(saved, item.Path);
                    if (!AssetDatabase.Contains(saved))
                    {
                        DestroyImmediate(saved);
                        throw new IOException($"데이터 에셋을 저장하지 못했습니다. {item.Path}");
                    }
                    createdPaths.Add(item.Path);
                    savedAssets.Add(item.Asset, saved);
                }
                foreach (var owner in owners) RemapPatternLinks(owner, savedAssets);
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out bool success);
                if (!success) throw new IOException("프리팹 저장에 실패했습니다.");
            }
            catch
            {
                var reverse = savedAssets.ToDictionary(item => item.Value, item => item.Key);
                foreach (var owner in owners) RemapPatternLinks(owner, reverse);
                foreach (string path in createdPaths.AsEnumerable().Reverse()) AssetDatabase.DeleteAsset(path);
                throw;
            }
        }

        private static void RemapPatternLinks(Object owner, IReadOnlyDictionary<Object, Object> replacements)
        {
            if (replacements.Count == 0) return;
            using var serialized = new SerializedObject(owner);
            var property = serialized.GetIterator();
            while (property.Next(true))
            {
                if (property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue &&
                    replacements.TryGetValue(property.objectReferenceValue, out var replacement))
                    property.objectReferenceValue = replacement;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
