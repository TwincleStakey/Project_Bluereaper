using BlueReaper.Core;
using UnityEngine;
using UnityEngine.AI;

namespace BlueReaper.Stage
{
    public class StageField : MonoBehaviour
    {
        public uint RepeatSpawnCount = 1;
        public float RepeatSpawnDelay = 0f;
        [Range(1, 100)] public int ClearPercent = 85;

        [SerializeField] private GameObject fieldObject;
        [SerializeField] private EnemySpawner dynamicSpawner;
        [SerializeField] private Enemy bossPrefab;
        [SerializeField] private Transform bossSpawnPoint;
        [SerializeField, Min(1)] private int purificationGoal = 30;
        [SerializeField] private StageDialogueLine[] openingDialogue;

        internal StageDialogueLine[] OpeningDialogue => openingDialogue;

        internal EnemySpawner DynamicSpawner => dynamicSpawner;
        internal bool HasBossSpawn => bossPrefab || bossSpawnPoint;
        internal bool UsesManagedFieldFlow => dynamicSpawner || HasBossSpawn;
        internal int PurificationGoal => purificationGoal;
        internal StageReadyPad[] ReadyPads { get; private set; }

        internal EnemySpawnEntry[] SpawnEntries { get; private set; }
        internal PlayerSpawnEntry Entrance { get; private set; }
        internal Unit[] PlacedUnits { get; private set; }
        internal Transform LayoutOrigin => fieldObject ? fieldObject.transform : transform;
        internal Vector3 Center => LayoutOrigin.position;

        private void Awake()
        {
            Transform markers = LayoutOrigin;
            SpawnEntries = markers.GetComponentsInChildren<EnemySpawnEntry>();
            Entrance = markers.GetComponentInChildren<PlayerSpawnEntry>();
            ReadyPads = markers.GetComponentsInChildren<StageReadyPad>(true);
            PlacedUnits = GetComponentsInChildren<Unit>(true);
        }

        internal void SetReadyPadsVisible(bool visible)
        {
            foreach (var pad in ReadyPads) pad.gameObject.SetActive(visible);
        }

        internal bool TryResolveBossSpawn(out Enemy prefab, out Vector3 position, out Quaternion rotation)
        {
            prefab = bossPrefab;
            position = default;
            rotation = default;
            if (dynamicSpawner || !prefab || prefab.Rank != EnemyRank.Boss || prefab.gameObject.scene.IsValid()
                || !bossSpawnPoint || !bossSpawnPoint.IsChildOf(transform)
                || !prefab.TryGetComponent(out NavMeshAgent agent) || !prefab.GetComponent<Health>()
                || !prefab.GetComponent<Unit>())
                return false;

            foreach (var unit in PlacedUnits)
                if (unit.TryGetComponent(out Enemy placed) && placed.Rank == EnemyRank.Boss) return false;

            var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
            if (!NavMesh.SamplePosition(bossSpawnPoint.position, out var hit, .5f, filter)) return false;
            position = hit.position;
            rotation = bossSpawnPoint.rotation;
            return true;
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
