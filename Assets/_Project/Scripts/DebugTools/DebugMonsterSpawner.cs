using BlueReaper.Core;
using BlueReaper.Player;
using BlueReaper.Stage;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BlueReaper.DebugTools
{
    /// <summary>
    /// 샌드박스 전용 테스트 도구. 숫자키로 몬스터 소환, K로 전부 처치, G로 플레이어 무적,
    /// H로 플레이어 쓰러뜨리기, J로 더미 동료 쓰러뜨리기,
    /// V로 웨이브 자동 소환 켜기/끄기, B로 웨이브 1번 바로 소환 (플레이어 둘레 원형으로 몰려옴).
    /// Kibaek 씬에만 둔다 (Prototype 씬·프리팹에는 넣지 않음).
    /// </summary>
    public class DebugMonsterSpawner : MonoBehaviour
    {
        [Tooltip("숫자키 1, 2, 3...에 대응하는 몬스터 프리팹")]
        [SerializeField] Enemy[] monsters;
        [SerializeField] StageController stage;
        [SerializeField] Health player;
        [Tooltip("구조 테스트용 더미 동료 (조작 컴포넌트를 끈 Player)")]
        [SerializeField] Health teammate;
        [Tooltip("플레이어 앞 몇 m에 소환할지")]
        [SerializeField] float spawnDistance = 6f;

        [Header("웨이브 (V 자동 / B 한 번)")]
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

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            for (int i = 0; i < monsters.Length && i < 9; i++)
                if (keyboard[Key.Digit1 + i].wasPressedThisFrame) Spawn(monsters[i]);

            if (keyboard.kKey.wasPressedThisFrame) KillAll();
            if (keyboard.gKey.wasPressedThisFrame)
            {
                godMode = !godMode;
                // 끌 때 실제 무적도 푼다. 대시 중이면 대시가 끝날 때 PlayerMotor가 푼다
                if (!godMode && !(player.TryGetComponent(out PlayerMotor motor) && motor.IsDashing))
                    player.Invincible = false;
            }
            // Kill은 무적을 무시하므로 G를 켜 둬도 쓰러진다
            if (keyboard.hKey.wasPressedThisFrame) player.Kill();
            if (keyboard.jKey.wasPressedThisFrame && teammate) teammate.Kill();

            // 대시가 끝날 때 PlayerMotor가 무적을 끄므로, 무적 모드면 매 프레임 다시 켠다
            if (godMode) player.Invincible = true;

            if (keyboard.vKey.wasPressedThisFrame)
            {
                autoWave = !autoWave;
                waveTimer = 0f;   // 켜자마자 바로 한 번
                Debug.Log($"[Debug] 자동 웨이브 {(autoWave ? "켜짐" : "꺼짐")}");
            }
            if (keyboard.bKey.wasPressedThisFrame) SpawnWave();
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

        void SpawnWave()
        {
            // 멀티에선 호스트(내 PC)에서만 만든다
            if (Network.StageNetwork.IsRemoteClient || monsters.Length == 0) return;
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
            // 멀티에선 적을 호스트만 만든다 (팀원 PC에서 만들면 나한테만 보이는 가짜 적이 생김)
            if (Network.StageNetwork.IsRemoteClient) return;
            Vector3 position = player.transform.position + player.transform.forward * spawnDistance;
            Quaternion rotation = Quaternion.LookRotation(-player.transform.forward);
            // 스테이지를 거쳐야 풀 사용 + 플레이어 목록 주입이 된다
            stage.SpawnEnemy(prefab, position, rotation);
        }

        void KillAll()
        {
            foreach (var enemy in FindObjectsByType<Enemy>(FindObjectsSortMode.None))
                enemy.GetComponent<Health>().Kill();
        }
    }
}
