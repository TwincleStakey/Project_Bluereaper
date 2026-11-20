using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BlueReaper.Core;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

namespace BlueReaper.Player.Editor
{
    /// <summary>
    /// 플레이어 값을 한 창에서 고치는 에디터 창 (Tools/BlueReaper/Player Editor).
    /// 편집 모드: Player.prefab을 고친다. 플레이 중: 지금 내 플레이어를 바로 고치고, [프리팹에 저장]을 눌러야 남는다.
    /// 스탯표(Warrior.asset)와 스킬 노드는 에셋이라 플레이 중에 고쳐도 그대로 남는다.
    /// </summary>
    public sealed class PlayerEditorWindow : EditorWindow
    {
        private const string PrefabPath = "Assets/_Project/Prefabs/Player/Player.prefab";
        private const string NodeFolder = "Assets/_Project/Data/Players/SkillNodes";
        // 팀 공유 프리셋 (git에 올라간다). 파일 하나 = 프리셋 하나
        private const string PresetFolder = "Assets/_Project/Data/Players/Presets";

        [Serializable]
        private sealed class PresetFile
        {
            public List<PresetSection> sections = new();
        }

        [Serializable]
        private sealed class PresetSection
        {
            public string component;
            public List<PresetValue> values = new();
        }

        [Serializable]
        private sealed class PresetValue
        {
            public string path;
            public string value;
        }

        // 빈사·구조(PlayerLife 등)는 팀 목숨 공유제로 바뀔 예정이라 뺐다
        private static readonly (string Title, Type Type)[] Sections =
        {
            ("평타", typeof(SwordAttack)),
            ("조작", typeof(PlayerController)),
            ("이동·대시", typeof(PlayerMotor)),
            ("자동 조준", typeof(AutoAim)),
            ("흡혈", typeof(PlayerLifesteal)),
            ("혼 보상", typeof(PlayerSouls)),
            ("타격 연출", typeof(PlayerVfx)),
        };
        private static readonly string[] Tabs = { "능력치·전투", "스킬트리" };

        private readonly Dictionary<UnityEngine.Object, SerializedObject> cache = new();
        private readonly HashSet<SkillNodeData> openNodes = new();
        private GameObject prefab;
        private GameObject target;
        private int tab;
        private int pick;
        private Vector2 scroll;
        private string presetName = "";
        private int presetPick;
        private string[] presetNames;

        [MenuItem("Tools/BlueReaper/Player Editor")]
        public static void Open() => GetWindow<PlayerEditorWindow>("플레이어");

        private void OnInspectorUpdate()
        {
            if (EditorApplication.isPlaying) Repaint();
        }

        // 다른 사람이 git으로 받은 프리셋도 보이게 창을 볼 때마다 목록을 다시 읽는다
        private void OnFocus() => presetNames = null;

        private void OnGUI()
        {
            if (!prefab) prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (!prefab)
            {
                EditorGUILayout.HelpBox($"{PrefabPath}를 찾지 못했어요.", MessageType.Error);
                return;
            }

            bool playing = EditorApplication.isPlaying;
            GameObject next = playing ? PickLivePlayer() : prefab;
            if (next != target)
            {
                target = next;
                cache.Clear();
            }
            if (!target)
            {
                EditorGUILayout.HelpBox("씬에 내 플레이어가 아직 없어요.", MessageType.Info);
                return;
            }

            EditorGUILayout.HelpBox(playing
                ? "플레이 중: 지금 플레이어를 바로 고칩니다. 플레이가 끝나면 되돌아가니 남길 값은 [프리팹에 저장]을 누르세요."
                : "Player.prefab을 고칩니다. 다 고치면 [프리팹 저장]을 누르세요.", MessageType.None);
            // 값은 네트워크로 안 보낸다. 세션 중에 고치면 이 PC에서만 바뀌어 팀원과 어긋난다
            if (playing && NetworkManager.Singleton && NetworkManager.Singleton.IsListening)
                EditorGUILayout.HelpBox("네트워크 세션 중: 여기서 바꾼 값은 이 PC에만 적용되고 팀원 PC는 그대로예요.", MessageType.Warning);

            tab = GUILayout.Toolbar(tab, Tabs);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            if (tab == 0) DrawCombat();
            else DrawSkills(playing);
            EditorGUILayout.EndScrollView();

            if (GUILayout.Button(playing ? "프리팹에 저장 (지금 값 남기기)" : "프리팹 저장", GUILayout.Height(28f)))
                SaveToPrefab();
        }

        // 플레이 중 내 플레이어 후보: 멀티면 내 것, 혼자 테스트(스폰 안 됨)면 씬의 플레이어 전부
        private GameObject PickLivePlayer()
        {
            var candidates = new List<PlayerController>();
            foreach (var player in FindObjectsByType<PlayerController>(FindObjectsSortMode.InstanceID))
                if (!player.IsSpawned || player.IsOwner) candidates.Add(player);
            if (candidates.Count == 0) return null;
            if (candidates.Count > 1)
            {
                var names = candidates.ConvertAll(player => player.name).ToArray();
                pick = EditorGUILayout.Popup("대상", Mathf.Min(pick, candidates.Count - 1), names);
            }
            return candidates[Mathf.Clamp(pick, 0, candidates.Count - 1)].gameObject;
        }

        private void DrawCombat()
        {
            DrawPresets();
            foreach (var (key, owner) in PresetTargets())
            {
                Header(owner is UnitData ? $"능력치 ({owner.name} 스탯표 · 에셋이라 바로 남음)"
                    : Sections.First(section => section.Type.Name == key).Title);
                DrawFields(owner);
            }
        }

        // 프리셋과 화면이 같은 목록을 쓴다: 스탯표 + Sections 컴포넌트
        private IEnumerable<(string Key, UnityEngine.Object Owner)> PresetTargets()
        {
            var unit = target.GetComponent<Unit>();
            if (unit && unit.Data) yield return (nameof(UnitData), unit.Data);
            foreach (var (_, type) in Sections)
            {
                var component = target.GetComponent(type);
                if (component) yield return (type.Name, component);
            }
        }

        private void DrawPresets()
        {
            Header("프리셋");
            using (new EditorGUILayout.HorizontalScope())
            {
                presetName = EditorGUILayout.TextField("이름", presetName);
                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(presetName)))
                    if (GUILayout.Button("저장", GUILayout.Width(70f))) SavePreset(presetName);
            }

            presetNames ??= Directory.Exists(PresetFolder)
                ? Directory.GetFiles(PresetFolder, "*.json").Select(Path.GetFileNameWithoutExtension).OrderBy(name => name).ToArray()
                : Array.Empty<string>();
            if (presetNames.Length == 0)
            {
                EditorGUILayout.LabelField("저장된 프리셋이 없어요.", EditorStyles.miniLabel);
                return;
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                presetPick = EditorGUILayout.Popup("불러올 프리셋", Mathf.Min(presetPick, presetNames.Length - 1), presetNames);
                if (GUILayout.Button("불러오기", GUILayout.Width(70f))) LoadPreset(presetNames[presetPick]);
            }
        }

        private void SavePreset(string rawName)
        {
            string name = string.Concat(rawName.Trim().Where(c => !Path.GetInvalidFileNameChars().Contains(c)));
            if (name.Length == 0) return;
            string path = $"{PresetFolder}/{name}.json";
            if (File.Exists(path) && !EditorUtility.DisplayDialog("프리셋 덮어쓰기", $"'{name}' 프리셋이 이미 있어요. 덮어쓸까요?", "덮어쓰기", "취소"))
                return;

            var file = new PresetFile();
            foreach (var (key, owner) in PresetTargets())
            {
                var section = new PresetSection { component = key };
                SerializedProperty property = new SerializedObject(owner).GetIterator();
                for (bool enter = true; property.NextVisible(enter); enter = false)
                    if (IsValue(property) && TryRead(property, out string value))
                        section.values.Add(new PresetValue { path = property.propertyPath, value = value });
                file.sections.Add(section);
            }

            Directory.CreateDirectory(PresetFolder);
            File.WriteAllText(path, JsonUtility.ToJson(file, true));
            AssetDatabase.ImportAsset(path);
            presetNames = null;
            ShowNotification(new GUIContent($"'{name}' 저장했어요"));
        }

        // 편집 모드: 프리팹·스탯표에 바로 저장. 플레이 중: 지금 플레이어에만 적용 (남기려면 [프리팹에 저장]). 스탯표는 에셋이라 늘 남는다
        private void LoadPreset(string name)
        {
            string path = $"{PresetFolder}/{name}.json";
            if (!File.Exists(path)) return;
            PresetFile file;
            try
            {
                file = JsonUtility.FromJson<PresetFile>(File.ReadAllText(path));
            }
            catch (Exception exception)
            {
                file = null;
                Debug.LogWarning($"프리셋을 읽지 못했어요: {path}\n{exception.Message}");
            }
            if (file?.sections == null)
            {
                ShowNotification(new GUIContent($"'{name}' 파일이 깨져서 불러오지 않았어요"));
                return;
            }
            var owners = PresetTargets().ToDictionary(entry => entry.Key, entry => entry.Owner);

            // 손으로 고친 파일에 빈 칸·틀린 값이 있어도 예외 없이 그 값만 건너뛴다 (중간에 멈춰 일부만 바뀌는 일 방지)
            int applied = 0, missing = 0;
            foreach (PresetSection section in file.sections)
            {
                if (section?.values == null) continue;
                if (section.component == null || !owners.TryGetValue(section.component, out UnityEngine.Object owner))
                {
                    missing += section.values.Count;
                    continue;
                }
                SerializedObject serialized = Serialized(owner);
                serialized.Update();
                foreach (PresetValue entry in section.values)
                {
                    SerializedProperty property = entry?.path == null ? null : serialized.FindProperty(entry.path);
                    if (property != null && entry.value != null && TryWrite(property, entry.value)) applied++;
                    else missing++;
                }
                serialized.ApplyModifiedProperties();
            }

            if (target == prefab) PrefabUtility.SavePrefabAsset(prefab);
            AssetDatabase.SaveAssets();
            // 프리셋 저장 뒤에 이름이 바뀌거나 지워진 값은 건너뛰고 개수만 알린다
            ShowNotification(new GUIContent(missing == 0 ? $"'{name}' 불러왔어요" : $"'{name}' 불러왔어요 (못 찾은 값 {missing}개)"));
        }

        private static bool TryRead(SerializedProperty property, out string value)
        {
            CultureInfo invariant = CultureInfo.InvariantCulture;
            value = property.propertyType switch
            {
                SerializedPropertyType.Float => property.floatValue.ToString("R", invariant),
                SerializedPropertyType.Integer or SerializedPropertyType.LayerMask => property.intValue.ToString(invariant),
                SerializedPropertyType.Boolean => property.boolValue ? "true" : "false",
                SerializedPropertyType.Enum when property.enumValueIndex >= 0 => property.enumNames[property.enumValueIndex],
                SerializedPropertyType.String => property.stringValue,
                SerializedPropertyType.Color => string.Join(",", new[] { property.colorValue.r, property.colorValue.g,
                    property.colorValue.b, property.colorValue.a }.Select(channel => channel.ToString("R", invariant))),
                _ => null,
            };
            return value != null;
        }

        private static bool TryWrite(SerializedProperty property, string value)
        {
            CultureInfo invariant = CultureInfo.InvariantCulture;
            switch (property.propertyType)
            {
                case SerializedPropertyType.Float:
                    if (!float.TryParse(value, NumberStyles.Float, invariant, out float number)) return false;
                    property.floatValue = number;
                    return true;
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.LayerMask:
                    if (!int.TryParse(value, NumberStyles.Integer, invariant, out int integer)) return false;
                    property.intValue = integer;
                    return true;
                case SerializedPropertyType.Boolean:
                    if (!bool.TryParse(value, out bool flag)) return false;
                    property.boolValue = flag;
                    return true;
                case SerializedPropertyType.Enum:
                    int index = Array.IndexOf(property.enumNames, value);
                    if (index < 0) return false;
                    property.enumValueIndex = index;
                    return true;
                case SerializedPropertyType.String:
                    property.stringValue = value;
                    return true;
                case SerializedPropertyType.Color:
                    float[] channels = value.Split(',').Select(part =>
                        float.TryParse(part, NumberStyles.Float, invariant, out float channel) ? channel : float.NaN).ToArray();
                    if (channels.Length != 4 || channels.Any(float.IsNaN)) return false;
                    property.colorValue = new Color(channels[0], channels[1], channels[2], channels[3]);
                    return true;
                default:
                    return false;
            }
        }

        private void DrawSkills(bool playing)
        {
            var tree = target.GetComponent<PlayerSkillTree>();
            var souls = target.GetComponent<PlayerSouls>();
            if (!tree)
            {
                EditorGUILayout.HelpBox("PlayerSkillTree가 없어요.", MessageType.Warning);
                return;
            }

            bool cheats = playing && tree.DebugAllowed;
            if (playing)
            {
                Header($"혼 {souls.Souls}");
                using (new EditorGUI.DisabledScope(!cheats))
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("혼 +10")) souls.DebugAddSouls(10);
                    if (GUILayout.Button("혼 +100")) souls.DebugAddSouls(100);
                    if (GUILayout.Button("혼 0으로")) souls.DebugAddSouls(-souls.Souls);
                }
                EditorGUILayout.LabelField(
                    $"배율  공격 ×{tree.GetMultiplier(SkillStat.Attack):0.00}   " +
                    $"사거리 ×{tree.GetMultiplier(SkillStat.AttackRange):0.00}   " +
                    $"이동 ×{tree.GetMultiplier(SkillStat.MoveSpeed):0.00}");
                EditorGUILayout.HelpBox(cheats
                    ? "[넣기]는 혼·선행 노드 없이 바로 적용됩니다."
                    : tree.IsSpawned && !tree.IsServer
                        ? "팀원 PC에서는 혼·노드를 못 바꿔요. 호스트 창에서 바꾸세요."
                        : "저장 파일과 이어진 판(로비에서 캐릭터로 들어옴)이라 혼·노드 치트를 막았어요. 저장 파일에 섞이기 때문이에요. Kibaek 씬에서 쓰세요.",
                    cheats ? MessageType.None : MessageType.Info);
            }

            Header("노드");
            SerializedObject treeObject = Serialized(tree);
            treeObject.Update();
            SerializedProperty list = treeObject.FindProperty("nodes");
            for (int i = 0; i < list.arraySize; i++)
            {
                var node = list.GetArrayElementAtIndex(i).objectReferenceValue as SkillNodeData;
                if (!node) continue;
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        bool open = EditorGUILayout.Foldout(openNodes.Contains(node),
                            $"{node.DisplayName}  ({node.Kind}, 혼 {node.Cost}, {node.Stat} +{node.Percent}%)", true);
                        if (open) openNodes.Add(node);
                        else openNodes.Remove(node);

                        if (playing)
                        {
                            bool taken = tree.DebugIsTaken(i);
                            using (new EditorGUI.DisabledScope(!cheats))
                                if (GUILayout.Button(taken ? "빼기" : "넣기", GUILayout.Width(50f)))
                                    tree.DebugSetTaken(i, !taken);
                        }
                    }
                    if (openNodes.Contains(node)) DrawFields(node);
                }
            }

            if (cheats && GUILayout.Button("노드 전부 빼기"))
                for (int i = 0; i < list.arraySize; i++) tree.DebugSetTaken(i, false);

            // 노드 목록은 네트워크로 안 보낸다. 세션 중에 붙이면 팀원 PC엔 그 노드가 없어서 효과가 어긋난다
            bool online = playing && NetworkManager.Singleton && NetworkManager.Singleton.IsListening;
            using (new EditorGUI.DisabledScope(online))
                if (GUILayout.Button("새 노드 만들기")) CreateNode(tree);
            if (online)
                EditorGUILayout.HelpBox("네트워크 세션 중에는 새 노드를 못 만들어요 (팀원 PC에 안 생김). 플레이를 멈추거나 세션 밖에서 만드세요.", MessageType.Info);
        }

        // 새 노드 에셋을 만들고 프리팹 목록 끝에 붙인다. 플레이 중이면 지금 플레이어에도 붙여서 바로 써 볼 수 있다
        private void CreateNode(PlayerSkillTree liveTree)
        {
            var node = CreateInstance<SkillNodeData>();
            var nodeObject = new SerializedObject(node);
            nodeObject.FindProperty("displayName").stringValue = "새 노드";
            nodeObject.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(node, AssetDatabase.GenerateUniqueAssetPath($"{NodeFolder}/Warrior_New.asset"));

            AppendNode(prefab.GetComponent<PlayerSkillTree>(), node);
            PrefabUtility.SavePrefabAsset(prefab);
            if (EditorApplication.isPlaying) AppendNode(liveTree, node);
            openNodes.Add(node);
        }

        private void AppendNode(PlayerSkillTree tree, SkillNodeData node)
        {
            SerializedObject treeObject = Serialized(tree);
            treeObject.Update();
            SerializedProperty list = treeObject.FindProperty("nodes");
            list.arraySize++;
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = node;
            treeObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private void SaveToPrefab()
        {
            if (target != prefab)
            {
                foreach (var (_, type) in Sections)
                {
                    var live = target.GetComponent(type);
                    var saved = prefab.GetComponent(type);
                    if (live && saved) CopyFields(live, saved);
                }
            }
            PrefabUtility.SavePrefabAsset(prefab);
            AssetDatabase.SaveAssets();
            ShowNotification(new GUIContent("Player.prefab에 저장했어요"));
        }

        // 숫자·색 같은 값만 옮긴다. 다른 오브젝트 참조는 씬 쪽 것이라 프리팹에 넣으면 안 된다
        private static void CopyFields(UnityEngine.Object from, UnityEngine.Object to)
        {
            var source = new SerializedObject(from);
            var destination = new SerializedObject(to);
            SerializedProperty property = source.GetIterator();
            for (bool enter = true; property.NextVisible(enter); enter = false)
                if (IsValue(property)) destination.CopyFromSerializedProperty(property);
            destination.ApplyModifiedPropertiesWithoutUndo();
        }

        private void DrawFields(UnityEngine.Object owner)
        {
            SerializedObject serialized = Serialized(owner);
            serialized.Update();
            SerializedProperty property = serialized.GetIterator();
            for (bool enter = true; property.NextVisible(enter); enter = false)
                if (IsValue(property) || property.name == "parent") EditorGUILayout.PropertyField(property, true);
            serialized.ApplyModifiedProperties();
        }

        private static bool IsValue(SerializedProperty property) =>
            property.name != "m_Script" && property.propertyType != SerializedPropertyType.ObjectReference;

        private SerializedObject Serialized(UnityEngine.Object owner)
        {
            if (!cache.TryGetValue(owner, out SerializedObject serialized) || serialized.targetObject == null)
                cache[owner] = serialized = new SerializedObject(owner);
            return serialized;
        }

        private static void Header(string text)
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField(text, EditorStyles.boldLabel);
        }
    }
}
