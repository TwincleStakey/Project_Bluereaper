using BlueReaper.Core;
using UnityEngine;

namespace BlueReaper.Stage
{
    public class EnemySpawnEntry : MonoBehaviour
    {
        public float SpawnRadius = 1f;
        public Enemy[] EnemyPrefab = null;
        public uint[] SpawnCount = null;
    }
}
