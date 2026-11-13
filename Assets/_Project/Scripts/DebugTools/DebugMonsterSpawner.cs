using BlueReaper.Core;
using BlueReaper.Stage;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BlueReaper.DebugTools
{
    /// <summary>
    /// 샌드박스 전용 테스트 도구. 숫자키로 몬스터 소환, K로 전부 처치, G로 플레이어 무적.
    /// Kibaek 씬에만 둔다 (Prototype 씬·프리팹에는 넣지 않음).
    /// </summary>
    public class DebugMonsterSpawner : MonoBehaviour
    {
        [Tooltip("숫자키 1, 2, 3...에 대응하는 몬스터 프리팹")]
        [SerializeField] Enemy[] monsters;
        [SerializeField] StageController stage;
        [SerializeField] Health player;
        [Tooltip("플레이어 앞 몇 m에 소환할지")]
        [SerializeField] float spawnDistance = 6f;

        bool godMode;

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            for (int i = 0; i < monsters.Length && i < 9; i++)
                if (keyboard[Key.Digit1 + i].wasPressedThisFrame) Spawn(monsters[i]);

            if (keyboard.kKey.wasPressedThisFrame) KillAll();
            if (keyboard.gKey.wasPressedThisFrame) godMode = !godMode;

            // 대시가 끝날 때 PlayerMotor가 무적을 끄므로, 무적 모드면 매 프레임 다시 켠다
            if (godMode) player.Invincible = true;
        }

        void Spawn(Enemy prefab)
        {
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
