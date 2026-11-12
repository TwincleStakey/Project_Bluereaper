using BlueReaper.Core;
using UnityEngine;

namespace BlueReaper.Stage
{
    public class StageField : MonoBehaviour
    {
        public uint RepeatSpawnCount = 1;
        public float RepeatSpawnDelay = 0f;
        [Range(1, 100)] public int ClearPercent = 85;

        [SerializeField] private GameObject fieldObject;

        internal EnemySpawnEntry[] SpawnEntries { get; private set; }
        internal PlayerSpawnEntry Entrance { get; private set; }
        internal Unit[] PlacedUnits { get; private set; }
        internal Vector3 Center => fieldObject ? fieldObject.transform.position : transform.position;

        private void Awake()
        {
            Transform markers = fieldObject ? fieldObject.transform : transform;
            SpawnEntries = markers.GetComponentsInChildren<EnemySpawnEntry>();
            Entrance = markers.GetComponentInChildren<PlayerSpawnEntry>();
            PlacedUnits = GetComponentsInChildren<Unit>(true);
        }

        internal bool TryGetClearTarget(out int requiredKills, out bool bossesOnly)
        {
            requiredKills = 0;
            bossesOnly = false;
            if (!Entrance || ClearPercent < 1 || ClearPercent > 100
                || RepeatSpawnDelay < 0f || float.IsNaN(RepeatSpawnDelay) || float.IsInfinity(RepeatSpawnDelay))
                return false;

            long total = 0;
            long bosses = 0;
            foreach (Unit unit in PlacedUnits)
            {
                var enemy = unit.GetComponent<Enemy>();
                if (!enemy) continue;
                total++;
                if (enemy.Rank == EnemyRank.Boss) bosses++;
            }

            foreach (EnemySpawnEntry entry in SpawnEntries)
            {
                if (entry.EnemyPrefab == null || entry.SpawnCount == null
                    || entry.EnemyPrefab.Length != entry.SpawnCount.Length || !IsValidRadius(entry.SpawnRadius))
                    return false;

                for (int i = 0; i < entry.EnemyPrefab.Length; i++)
                {
                    ulong count = (ulong)entry.SpawnCount[i] * RepeatSpawnCount;
                    if (count == 0) continue;
                    Enemy prefab = entry.EnemyPrefab[i];
                    if (!prefab || count > (ulong)(int.MaxValue - total)) return false;
                    total += (long)count;
                    if (prefab.Rank == EnemyRank.Boss) bosses += (long)count;
                }
            }

            bossesOnly = bosses > 0;
            requiredKills = bossesOnly ? (int)bosses : (int)((total * ClearPercent + 99) / 100);
            return true;
        }

        private static bool IsValidRadius(float radius) =>
            radius >= 0f && !float.IsNaN(radius) && !float.IsInfinity(radius);
    }
}
