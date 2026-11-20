using System;
using System.Collections;
using System.Collections.Generic;
using BlueReaper.Core;
using BlueReaper.Player;
using UnityEngine;
using UnityEngine.AI;
using Random = UnityEngine.Random;

namespace BlueReaper.Stage
{
    public sealed class EnemySpawner : MonoBehaviour
    {
        [Min(0.1f)] public float SpawnRadius = 25f;
        [Min(1)] public int MaxAliveEnemiesPerPlayer = 8;
        [Min(0.1f)] public float SpawnInterval = 3f;
        public SpawnData[] Waves;

        [Serializable]
        private struct RequiredElite
        {
            public Enemy Prefab;
            [Min(1)] public int Count;
            public HuntPhase StartPhase;
        }

        [SerializeField] private SpawnData[] eveningWaves;
        [SerializeField] private SpawnData[] nightWaves;
        [SerializeField] private RequiredElite[] requiredElites;

        private const int PositionAttempts = 12;
        private const int ClockwiseSteps = 4;
        private const float SpawnViewPadding = 2f;
        private readonly List<Transform> players = new();
        private readonly Plane[] targetView = new Plane[6];

        internal long RequiredEliteCount
        {
            get
            {
                long total = 0;
                if (requiredElites != null)
                    foreach (var elite in requiredElites) total += elite.Count;
                return total;
            }
        }

        public bool IsConfigured()
        {
            if (SpawnRadius <= 0f || !float.IsFinite(SpawnRadius) || MaxAliveEnemiesPerPlayer <= 0
                || SpawnInterval <= 0f || !float.IsFinite(SpawnInterval) || !IsWaveTableConfigured(Waves)
                || HasWaves(eveningWaves) && !IsWaveTableConfigured(eveningWaves)
                || HasWaves(nightWaves) && !IsWaveTableConfigured(nightWaves))
                return false;

            if (requiredElites == null) return true;
            foreach (var elite in requiredElites)
                if (elite.Count <= 0 || !IsSpawnPrefab(elite.Prefab, EnemyRank.Elite)
                    || elite.StartPhase is < HuntPhase.Afternoon or > HuntPhase.Night) return false;
            return RequiredEliteCount <= int.MaxValue;
        }

        private static bool IsWaveTableConfigured(SpawnData[] waves)
        {
            if (!HasWaves(waves)) return false;
            float previousStart = -1f;
            foreach (var wave in waves)
            {
                if (wave == null || wave.StartTime <= previousStart || !float.IsFinite(wave.StartTime)
                    || wave.StartTime < 0f || !Enum.IsDefined(typeof(WaveSpawnType), wave.Type)
                    || wave.Enemies == null || wave.Enemies.Length == 0 || (wave.Type == WaveSpawnType.Clockwise
                        && (wave.StepInterval <= 0f || !float.IsFinite(wave.StepInterval)))) return false;
                foreach (var entry in wave.Enemies)
                    if (!IsSpawnPrefab(entry.Prefab, EnemyRank.Normal) || entry.Count <= 0) return false;
                previousStart = wave.StartTime;
            }
            return true;
        }

        private static bool IsSpawnPrefab(Enemy prefab, EnemyRank rank) =>
            prefab && prefab.Rank == rank && !prefab.gameObject.scene.IsValid()
            && prefab.GetComponent<NavMeshAgent>() && prefab.GetComponent<Health>();

        private static bool HasWaves(SpawnData[] waves) => waves is { Length: > 0 };

        internal IEnumerator Run(StageController stage, StageField field, TopDownCamera view)
        {
            var remainingElites = new int[requiredElites?.Length ?? 0];
            for (int i = 0; i < remainingElites.Length; i++) remainingElites[i] = requiredElites[i].Count;
            float waveTableStarted = Time.time;
            float nextSpawnAt = waveTableStarted;
            float nextEliteAttemptAt = waveTableStarted;
            SpawnData[] activeTable = null;
            int activeWave = -1;
            int step = 0;
            while (stage.CanSpawnWave(field))
            {
                float now = Time.time;
                var huntPhase = stage.CurrentHuntPhase;
                var waves = WavesFor(huntPhase);
                if (waves != activeTable)
                {
                    activeTable = waves;
                    waveTableStarted = now;
                    activeWave = -1;
                }

                if (now >= nextEliteAttemptAt)
                {
                    SpawnRequiredElites(stage, field, view, huntPhase, remainingElites);
                    nextEliteAttemptAt = now + SpawnInterval;
                }

                int waveIndex = WaveAt(waves, now - waveTableStarted);
                if (waveIndex >= 0)
                {
                    var wave = waves[waveIndex];
                    if (waveIndex != activeWave)
                    {
                        activeWave = waveIndex;
                        nextSpawnAt = now;
                        step = 0;
                    }
                    if (now >= nextSpawnAt)
                    {
                        SpawnBatch(stage, field, view, wave, step);
                        step = wave.Type == WaveSpawnType.Clockwise ? (step + 1) % ClockwiseSteps : 0;
                        nextSpawnAt = now + (step > 0 ? wave.StepInterval : SpawnInterval);
                    }
                }
                yield return null;
            }
        }

        private SpawnData[] WavesFor(HuntPhase phase)
        {
            if (phase == HuntPhase.Night && HasWaves(nightWaves)) return nightWaves;
            if (phase >= HuntPhase.Evening && HasWaves(eveningWaves)) return eveningWaves;
            return Waves;
        }

        private static int WaveAt(SpawnData[] waves, float elapsed)
        {
            int index = waves.Length - 1;
            while (index >= 0 && waves[index].StartTime > elapsed) index--;
            return index;
        }

        private void SpawnRequiredElites(StageController stage, StageField field, TopDownCamera view,
            HuntPhase phase, int[] remaining)
        {
            bool anyPending = false;
            for (int i = 0; i < remaining.Length; i++)
                if (remaining[i] > 0 && requiredElites[i].StartPhase <= phase)
                {
                    anyPending = true;
                    break;
                }
            if (!anyPending) return;

            players.Clear();
            players.AddRange(stage.AlivePlayers);
            Physics.SyncTransforms();
            foreach (Transform player in players)
            {
                if (!stage.CanSpawnWave(field)) return;
                if (!player) continue;
                int available = MaxAliveEnemiesPerPlayer - stage.CountEnemiesFor(player);
                if (available <= 0) continue;
                view.GetSpawnViewPlanes(player, targetView);
                for (int index = 0; index < remaining.Length && available > 0; index++)
                {
                    var elite = requiredElites[index];
                    if (elite.StartPhase > phase) continue;
                    while (remaining[index] > 0 && available > 0 && stage.CanSpawnWave(field))
                    {
                        if (!TrySpawn(stage, field, elite.Prefab, player, WaveSpawnType.Scattered, 0, 0, 1, 0f))
                            break;
                        remaining[index]--;
                        available--;
                    }
                }
            }
        }

        private void SpawnBatch(StageController stage, StageField field, TopDownCamera view, SpawnData wave, int step)
        {
            players.Clear();
            players.AddRange(stage.AlivePlayers);
            Physics.SyncTransforms();
            long requested = 0;
            foreach (var entry in wave.Enemies) requested += entry.Count;
            foreach (Transform player in players)
            {
                if (!stage.CanSpawnWave(field)) return;
                if (!player) continue;
                int available = MaxAliveEnemiesPerPlayer - stage.CountEnemiesFor(player);
                if (available <= 0) continue;
                view.GetSpawnViewPlanes(player, targetView);
                long slot = 0;
                float phase = Random.Range(0f, 360f);
                foreach (var entry in wave.Enemies)
                {
                    for (int i = 0; i < entry.Count && available > 0 && i < MaxAliveEnemiesPerPlayer; i++, slot++)
                    {
                        if (!stage.CanSpawnWave(field)) return;
                        if (TrySpawn(stage, field, entry.Prefab, player, wave.Type, step, slot, requested, phase))
                            available--;
                    }
                    if (available <= 0) break;
                }
            }
        }

        private bool TrySpawn(StageController stage, StageField field, Enemy prefab, Transform owner,
            WaveSpawnType type, int step, long slot, long count, float phase)
        {
            for (int attempt = 0; attempt < PositionAttempts; attempt++)
            {
                Vector3 candidate = Candidate(owner.position, field.LayoutOrigin.rotation, type,
                    step, slot, count, phase, attempt);
                if (!TryResolveSpawnPosition(stage, targetView, prefab, candidate, out Vector3 position)) continue;
                Vector3 forward = owner.position - position;
                forward.y = 0f;
                stage.SpawnWaveEnemy(prefab, position,
                    forward.sqrMagnitude > 0.001f ? Quaternion.LookRotation(forward) : Quaternion.identity, owner);
                return true;
            }
            return false;
        }

        private Vector3 Candidate(Vector3 center, Quaternion orientation, WaveSpawnType type, int step,
            long slot, long count, float phase, int attempt)
        {
            float partition = slot / (float)count;
            float centered = (slot + 0.5f) / count - 0.5f;
            if (type is WaveSpawnType.Pincer or WaveSpawnType.Arrowhead)
            {
                Quaternion side = orientation * Quaternion.Euler(0f,
                    (type == WaveSpawnType.Pincer ? -90f : 0f) + (slot % 2 == 0 ? 0f : 180f), 0f);
                int lineIndex = (int)(slot / 2);
                float width = (lineIndex + 1) / 2 * (lineIndex % 2 == 0 ? -1.8f : 1.8f);
                float outward = type == WaveSpawnType.Arrowhead ? Mathf.Abs(width) : 0f;
                return center + side * new Vector3(width + AlternatingOffset(attempt) * 0.3f, 0f, SpawnRadius + outward);
            }
            float angle = type switch
            {
                WaveSpawnType.Scattered => Random.Range(0f, 360f),
                WaveSpawnType.Ring => phase + (partition * 360f) + (attempt * 3f),
                WaveSpawnType.Clockwise => -90f + step * 90f + centered * 48f + AlternatingOffset(attempt),
                _ => phase
            };
            Vector3 direction = orientation * (Quaternion.Euler(0f, angle, 0f) * Vector3.forward);
            direction.y = 0f;
            return center + direction.normalized * SpawnRadius;
        }

        private static float AlternatingOffset(int attempt) =>
            (attempt + 1) / 2 * (attempt % 2 == 0 ? -3f : 3f);

        internal static bool TryResolveSpawnPosition(StageController stage, Plane[] targetView, Enemy prefab,
            Vector3 candidate, out Vector3 position)
        {
            position = default;
            if (!prefab.TryGetComponent(out NavMeshAgent agent)) return false;
            var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 1f, filter)) return false;

            float radius = agent.radius * Mathf.Max(Mathf.Abs(prefab.transform.lossyScale.x), Mathf.Abs(prefab.transform.lossyScale.z));
            float height = Mathf.Max(2f * radius, agent.height * Mathf.Abs(prefab.transform.lossyScale.y));
            var bounds = new Bounds(hit.position + Vector3.up * (height * 0.5f),
                new Vector3(radius * 2f, height, radius * 2f));
            bounds.Expand(SpawnViewPadding * 2f);
            if (GeometryUtility.TestPlanesAABB(targetView, bounds)) return false;

            Vector3 bottom = hit.position + Vector3.up * (radius + 0.03f);
            Vector3 top = bottom + Vector3.up * (height - 2f * radius);
            int layers = LayerMask.GetMask("Default", "Environment", "Player", "Monster", "Boss");
            if (Physics.CheckCapsule(bottom, top, radius, layers, QueryTriggerInteraction.Ignore)
                || !stage.IsSpawnSpaceFree(hit.position, radius, height)) return false;
            position = hit.position;
            return true;
        }
    }
}
