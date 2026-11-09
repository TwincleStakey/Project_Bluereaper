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

        readonly List<Transform> alivePlayers = new();

        public IReadOnlyList<Transform> AlivePlayers => alivePlayers;

        void Awake()
        {
            foreach (Health player in players)
            {
                alivePlayers.Add(player.transform);
                // 적들은 이 리스트 자체를 참조하므로, 여기서 빼면 모든 적에게 바로 반영된다
                player.Died += () => alivePlayers.Remove(player.transform);
            }
        }

        public Enemy SpawnEnemy(Enemy prefab, Vector3 position, Quaternion rotation)
        {
            var enemy = ObjectPool.Spawn(prefab.gameObject, position, rotation).GetComponent<Enemy>();
            enemy.Init(alivePlayers);
            return enemy;
        }
    }
}
