using System.Collections.Generic;
using BlueReaper.Core;
using UnityEngine;

namespace BlueReaper.Stage
{
    /// <summary>
    /// 스테이지 안의 플레이어와 적을 책임진다.
    /// 살아있는 플레이어 목록을 관리하고, 적을 스폰할 때 그 목록을 넘겨준다(주입).
    /// </summary>
    public class StageController : MonoBehaviour
    {
        [Tooltip("이 스테이지에 참가한 플레이어 (1~3명)")]
        [SerializeField] List<Health> players = new();

        [SerializeField] List<Enemy> enemies = new();

        [SerializeField] Transform spawnPosition = null;

        [SerializeField]
        BossHUD bossHUD = null;


        readonly List<Transform> alivePlayers = new();

        public IReadOnlyList<Transform> AlivePlayers => alivePlayers;

        private List<Enemy> aliveEnemies = new();
        private Health hudBoss;

        void Awake()
        {
            foreach (Health player in players)
            {
                alivePlayers.Add(player.transform);
                // 적들은 이 리스트 자체를 참조하므로, 여기서 빼면 모든 적에게 바로 반영된다
                player.Died += () => alivePlayers.Remove(player.transform);
            }
        }

        private void OnEnable()
        {
            GameEvents.BossSpawned += OnBossSpawned;
            GameEvents.BossDefeated += OnBossDefeated;
            if (hudBoss && !hudBoss.IsDead) OnBossSpawned(hudBoss);
            else if (bossHUD) bossHUD.Hide();
        }

        private void OnDisable()
        {
            GameEvents.BossSpawned -= OnBossSpawned;
            GameEvents.BossDefeated -= OnBossDefeated;
            if (bossHUD) bossHUD.Hide();
        }

        private void Start()
        {
            //test, TODO : 실제로는 스테이지 시작시 스테이지 데이터 (stage, substage, wave)를 읽어서 필드와 스폰을 구성해야함
            for (int i = 0; i < enemies.Count; i++)
                SpawnEnemy(enemies[i], spawnPosition.position, spawnPosition.rotation);
        }

        private void OnBossSpawned(Health boss)
        {
            if (!boss || boss.IsDead) return;
            var enemy = boss.GetComponent<Enemy>();
            if (!enemy || enemy.Rank != EnemyRank.Boss || !aliveEnemies.Contains(enemy)) return;
            hudBoss = boss;
            if (!bossHUD) return;
            bossHUD.Init(enemy);
            bossHUD.Show();
        }

        private void OnBossDefeated(Health boss)
        {
            if (!boss || !boss.IsDead) return;
            var enemy = boss.GetComponent<Enemy>();
            if (enemy) aliveEnemies.Remove(enemy);
            if (hudBoss != boss) return;
            hudBoss = null;
            if (bossHUD) bossHUD.Hide();
        }

        public Enemy SpawnEnemy(Enemy prefab, Vector3 position, Quaternion rotation)
        {
            var enemy = ObjectPool.Spawn(prefab.gameObject, position, rotation).GetComponent<Enemy>();
            enemy.Init(alivePlayers);
            if (!aliveEnemies.Contains(enemy)) aliveEnemies.Add(enemy);
            if (enemy.Rank == EnemyRank.Boss)
                GameEvents.RaiseBossSpawned(enemy.GetComponent<Health>());
            return enemy;
        }
    }
}
