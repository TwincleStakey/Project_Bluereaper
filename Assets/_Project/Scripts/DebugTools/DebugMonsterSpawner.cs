using BlueReaper.Core;
using BlueReaper.Player;
using BlueReaper.Stage;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BlueReaper.DebugTools
{
    /// <summary>
    /// 샌드박스 전용 테스트 도구. 게임 키 배치가 아직 미정(기획: 평타·액티브·회피 버튼 3개 + K 스킬트리)이라
    /// 나중에 어떤 키로 정해져도 겹치지 않게 F1~F12만 쓴다.
    /// F1~F6 몬스터 소환, F7 웨이브 자동 소환 켜기/끄기, F8 웨이브 1번 바로 소환 (플레이어 둘레 원형으로 몰려옴),
    /// F9 플레이어 무적, F10 플레이어 쓰러뜨리기, F11 더미 동료 쓰러뜨리기, F12 전부 처치.
    /// Kibaek 씬에만 둔다 (Prototype 씬·프리팹에는 넣지 않음).
    /// </summary>
    public class DebugMonsterSpawner : MonoBehaviour
    {
        [Tooltip("F1, F2, F3...에 대응하는 몬스터 프리팹 (최대 6개)")]
        [SerializeField] Enemy[] monsters;
        [SerializeField] StageController stage;
        [SerializeField] Health player;
        [Tooltip("구조 테스트용 더미 동료 (조작 컴포넌트를 끈 Player)")]
        [SerializeField] Health teammate;
        [Tooltip("플레이어 앞 몇 m에 소환할지")]
        [SerializeField] float spawnDistance = 6f;

        [Header("웨이브 (F7 자동 / F8 한 번)")]
        [Tooltip("자동 웨이브 간격(초)")]
        [SerializeField] float waveInterval = 6f;
        [Tooltip("웨이브 한 번에 나오는 수")]
        [SerializeField] int waveSize = 8;
        [Tooltip("플레이어에서 이 거리의 원 위에 나온다")]
        [SerializeField] float waveRadius = 9f;
        [Tooltip("씬에 살아 있는 몬스터가 이 수 이상이면 웨이브를 건너뜀 (프레임 보호)")]
        [SerializeField] int maxAlive = 40;

        bool godMode;
        bool autoWave;
        float waveTimer;

        // 디버그 창(에디터 창)이 이 목록을 보고 버튼을 만든다. 키와 버튼은 같은 메서드를 부른다.
        public int MonsterCount => monsters.Length;
        public string MonsterName(int index) => monsters[index] ? monsters[index].name : "(비어 있음)";
        public bool GodMode => godMode;
        public bool AutoWave => autoWave;
        public bool HasTeammate => teammate;
        public int AliveEnemies => FindObjectsByType<Enemy>(FindObjectsSortMode.None).Length;

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                for (int i = 0; i < monsters.Length && i < 6; i++)
                    if (keyboard[Key.F1 + i].wasPressedThisFrame) SpawnMonster(i);
                if (keyboard.f7Key.wasPressedThisFrame) ToggleAutoWave();
                if (keyboard.f8Key.wasPressedThisFrame) SpawnWave();
                if (keyboard.f9Key.wasPressedThisFrame) ToggleGodMode();
                if (keyboard.f10Key.wasPressedThisFrame) DownPlayer();
                if (keyboard.f11Key.wasPressedThisFrame) DownTeammate();
                if (keyboard.f12Key.wasPressedThisFrame) KillAll();
            }

            // 대시가 끝날 때 PlayerMotor가 무적을 끄므로, 무적 모드면 매 프레임 다시 켠다
            if (godMode) player.Invincible = true;

            if (autoWave)
            {
                waveTimer -= Time.deltaTime;
                if (waveTimer <= 0f)
                {
                    waveTimer = waveInterval;
                    SpawnWave();
                }
            }
        }

        public void SpawnMonster(int index) => Spawn(monsters[index]);

        public void ToggleAutoWave()
        {
            autoWave = !autoWave;
            waveTimer = 0f;   // 켜자마자 바로 한 번
            Debug.Log($"[Debug] 자동 웨이브 {(autoWave ? "켜짐" : "꺼짐")}");
        }

        public void ToggleGodMode()
        {
            godMode = !godMode;
            // 끌 때 실제 무적도 푼다. 대시 중이면 대시가 끝날 때 PlayerMotor가 푼다
            if (!godMode && !(player.TryGetComponent(out PlayerMotor motor) && motor.IsDashing))
                player.Invincible = false;
        }

        // Kill은 무적을 무시하므로 무적 모드여도 쓰러진다
        public void DownPlayer() => player.Kill();

        public void DownTeammate()
        {
            if (teammate) teammate.Kill();
        }

        public void SpawnWave()
        {
            // 멀티에선 호스트(내 PC)에서만 만든다
            var net = Unity.Netcode.NetworkManager.Singleton;
            if ((net && net.IsListening && !net.IsServer) || monsters.Length == 0) return;
            if (FindObjectsByType<Enemy>(FindObjectsSortMode.None).Length >= maxAlive) return;

            Vector3 center = player.transform.position;
            float offset = Random.Range(0f, 360f);
            for (int i = 0; i < waveSize; i++)
            {
                float ang = offset + 360f * i / waveSize + Random.Range(-10f, 10f);
                Vector3 dir = Quaternion.Euler(0f, ang, 0f) * Vector3.forward;
                Vector3 pos = center + dir * (waveRadius + Random.Range(-1.5f, 1.5f));
                // 웨이브는 스테이지 클리어 조건에 세지 않는다 (디버그용)
                stage.SpawnEnemy(monsters[Random.Range(0, monsters.Length)], pos, Quaternion.LookRotation(-dir), false);
            }
        }

        void Spawn(Enemy prefab)
        {
            Vector3 position = player.transform.position + player.transform.forward * spawnDistance;
            Quaternion rotation = Quaternion.LookRotation(-player.transform.forward);
            // 스테이지를 거쳐야 풀 사용 + 플레이어 목록 주입이 된다
            stage.SpawnEnemy(prefab, position, rotation, countsForClear: false);
        }

#if UNITY_EDITOR
        // 게임 화면 왼쪽 아래 버튼 → 따로 뜨는 디버그 창(DebugPanelWindow). 에디터 창이라 빌드에는 없다.
        void OnGUI()
        {
            if (GUI.Button(new Rect(10f, Screen.height - 40f, 80f, 30f), "디버그"))
                UnityEditor.EditorApplication.ExecuteMenuItem("Tools/BlueReaper/Debug Panel");
        }
#endif

        public void KillAll()
        {
            foreach (var enemy in FindObjectsByType<Enemy>(FindObjectsSortMode.None))
                enemy.GetComponent<Health>().Kill();
        }
    }
}
