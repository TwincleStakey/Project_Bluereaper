using System;
using System.Collections.Generic;
using BlueReaper.Core;
using UnityEngine;
using UnityEngine.AI;

namespace BlueReaper.Stage
{
    public class StageField : MonoBehaviour
    {
        [Serializable]
        private struct BossSpawn
        {
            public Enemy Prefab;
            public Transform Point;
        }

        [SerializeField] private GameObject fieldObject;
        [SerializeField] private EnemySpawner dynamicSpawner;
        [SerializeField] private BossSpawn[] bossSpawns;
        [SerializeField, Min(1)] private int purificationGoal = 30;
        [SerializeField, Min(1)] private int huntReferenceKills = 40;
        [SerializeField, Range(1, 99)] private int eveningPercent = 35;
        [SerializeField, Range(2, 100)] private int nightPercent = 70;
        [SerializeField] private StageDialogueLine[] openingDialogue;

        internal StageDialogueLine[] OpeningDialogue => openingDialogue;

        internal EnemySpawner DynamicSpawner => dynamicSpawner;
        internal bool HasBossSpawn => bossSpawns is { Length: > 0 };
        internal StageReadyPad[] ReadyPads { get; private set; }

        internal PlayerSpawnEntry Entrance { get; private set; }
        internal Unit[] PlacedUnits { get; private set; }
        internal Transform LayoutOrigin => fieldObject ? fieldObject.transform : transform;
        internal Vector3 Center => LayoutOrigin.position;

        public bool IsHuntProgressConfigured() =>
            purificationGoal > 0 && huntReferenceKills > 0
            && eveningPercent > 0 && eveningPercent < nightPercent && nightPercent <= 100
            && ((long)huntReferenceKills * eveningPercent + 99) / 100
               < ((long)huntReferenceKills * nightPercent + 99) / 100;

        private void Awake()
        {
            Transform markers = LayoutOrigin;
            Entrance = markers.GetComponentInChildren<PlayerSpawnEntry>();
            ReadyPads = markers.GetComponentsInChildren<StageReadyPad>(true);
            PlacedUnits = GetComponentsInChildren<Unit>(true);
        }

        internal void SetReadyPadsVisible(bool visible)
        {
            foreach (var pad in ReadyPads) pad.gameObject.SetActive(visible);
        }

        internal HuntPhase GetHuntPhase(int kills)
        {
            long progress = kills * 100L;
            if (progress >= (long)huntReferenceKills * nightPercent) return HuntPhase.Night;
            return progress >= (long)huntReferenceKills * eveningPercent ? HuntPhase.Evening : HuntPhase.Afternoon;
        }

        internal bool TryGetClearTarget(int bossSpawns, out int requiredKills, out EnemyRank clearRank)
        {
            requiredKills = 0;
            clearRank = EnemyRank.Normal;
            long bosses = bossSpawns;
            long elites = dynamicSpawner ? dynamicSpawner.RequiredEliteCount : 0;
            foreach (var unit in PlacedUnits)
            {
                if (!unit.TryGetComponent(out Enemy enemy)) continue;
                if (!unit.GetComponent<Health>()) return false;
                if (enemy.Rank == EnemyRank.Boss) bosses++;
                else if (enemy.Rank == EnemyRank.Elite) elites++;
            }

            if (HasBossSpawn && bosses > 0 && bosses <= int.MaxValue)
            {
                clearRank = EnemyRank.Boss;
                requiredKills = (int)bosses;
            }
            else if (!HasBossSpawn && bosses == 0 && elites > 0 && elites <= int.MaxValue)
            {
                clearRank = EnemyRank.Elite;
                requiredKills = (int)elites;
            }
            else if (!HasBossSpawn && bosses == 0 && elites == 0)
                requiredKills = purificationGoal;
            return requiredKills > 0;
        }

        internal bool TryResolveBossSpawns(List<(Enemy Prefab, Vector3 Position, Quaternion Rotation)> spawns)
        {
            spawns.Clear();
            if (dynamicSpawner || !HasBossSpawn) return false;
            foreach (var entry in bossSpawns)
                if (!TryAddBossSpawn(entry.Prefab, entry.Point, spawns)) return false;
            return true;
        }

        private bool TryAddBossSpawn(Enemy prefab, Transform point,
            List<(Enemy Prefab, Vector3 Position, Quaternion Rotation)> spawns)
        {
            if (!prefab || prefab.Rank != EnemyRank.Boss || prefab.gameObject.scene.IsValid()
                || !point || !point.IsChildOf(transform)
                || !prefab.TryGetComponent(out NavMeshAgent agent) || !prefab.GetComponent<Health>()
                || !prefab.GetComponent<Unit>())
                return false;

            var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
            if (!NavMesh.SamplePosition(point.position, out var hit, .5f, filter)) return false;
            spawns.Add((prefab, hit.position, point.rotation));
            return true;
        }
    }
}
