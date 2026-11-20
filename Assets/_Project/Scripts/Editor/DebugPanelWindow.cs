using UnityEditor;
using UnityEngine;

namespace BlueReaper.DebugTools.Editor
{
    /// <summary>
    /// 플레이 중 씬의 DebugMonsterSpawner를 버튼으로 조작하는 에디터 창.
    /// 에디터 창이라 Game 창 밖(다른 모니터 포함)으로 끌어 띄우거나 도킹할 수 있다.
    /// </summary>
    public sealed class DebugPanelWindow : EditorWindow
    {
        private DebugMonsterSpawner spawner;
        private Vector2 scroll;

        // 게임 화면의 "디버그" 버튼(DebugMonsterSpawner.OnGUI)이 이 메뉴 경로로 창을 연다
        [MenuItem("Tools/BlueReaper/Debug Panel")]
        public static void Open()
        {
            var window = GetWindow<DebugPanelWindow>("디버그");
            window.minSize = new Vector2(220f, 260f);
        }

        private void OnInspectorUpdate()
        {
            if (EditorApplication.isPlaying) Repaint();
        }

        private void OnGUI()
        {
            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("플레이 중에만 쓸 수 있어요. Kibaek 씬을 플레이하세요.", MessageType.Info);
                return;
            }
            if (spawner == null) spawner = FindFirstObjectByType<DebugMonsterSpawner>();
            if (spawner == null)
            {
                EditorGUILayout.HelpBox("이 씬에 DebugMonsterSpawner가 없어요.", MessageType.Warning);
                return;
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);

            EditorGUILayout.LabelField("몬스터 소환", EditorStyles.boldLabel);
            for (int i = 0; i < spawner.MonsterCount; i++)
            {
                string key = i < 6 ? $"F{i + 1}  " : "";
                if (GUILayout.Button($"{key}{spawner.MonsterName(i)}")) spawner.SpawnMonster(i);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"웨이브   (살아 있는 몬스터 {spawner.AliveEnemies})", EditorStyles.boldLabel);
            if (GUILayout.Button($"F7  자동 웨이브: {(spawner.AutoWave ? "켜짐" : "꺼짐")}")) spawner.ToggleAutoWave();
            if (GUILayout.Button("F8  웨이브 한 번")) spawner.SpawnWave();
            if (GUILayout.Button("F12  몬스터 전부 처치")) spawner.KillAll();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("플레이어", EditorStyles.boldLabel);
            if (GUILayout.Button($"F9  무적: {(spawner.GodMode ? "켜짐" : "꺼짐")}")) spawner.ToggleGodMode();
            if (GUILayout.Button("F10  나 쓰러뜨리기")) spawner.DownPlayer();
            using (new EditorGUI.DisabledScope(!spawner.HasTeammate))
                if (GUILayout.Button("F11  더미 동료 쓰러뜨리기")) spawner.DownTeammate();

            EditorGUILayout.EndScrollView();
        }
    }
}
