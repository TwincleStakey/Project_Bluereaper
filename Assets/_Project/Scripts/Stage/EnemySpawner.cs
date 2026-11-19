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

        private const int PositionAttempts = 12;
        private const int ClockwiseSteps = 4;
        private const float SpawnViewPadding = 2f;
        private readonly List<Transform> players = new();
        private readonly Plane[] targetView = new Plane[6];

        public bool IsConfigured()
        {
            if (SpawnRadius <= 0f || !float.IsFinite(SpawnRadius) || MaxAliveEnemiesPerPlayer <= 0
                || SpawnInterval <= 0f || !float.IsFinite(SpawnInterval) || Waves == null || Waves.Length == 0) return false;

            float previousStart = -1f;
            foreach (var wave in Waves)
            {
                if (wave == null || wave.StartTime <= previousStart || !float.IsFinite(wave.StartTime)
                    || wave.StartTime < 0f || !Enum.IsDefined(typeof(WaveSpawnType), wave.Type)
                    || wave.Enemies == null || wave.Enemies.Length == 0 || (wave.Type == WaveSpawnType.Clockwise
                        && (wave.StepInterval <= 0f || !float.IsFinite(wave.StepInterval)))) return false;
                foreach (var entry in wave.Enemies)
                    if (!entry.Prefab || entry.Prefab.Rank == EnemyRank.Boss
                        || !entry.Prefab.GetComponent<NavMeshAgent>() || !entry.Prefab.GetComponent<Health>()
                        || entry.Count <= 0) return false;
                previousStart = wave.StartTime;
            }
            return true;
        }

        internal IEnumerator Run(StageController stage, StageField field, TopDownCamera view)
        {
            float combatStarted = Time.time;
            float nextSpawnAt = combatStarted;
            int activeWave = -1;
            int step = 0;
            while (stage.IsFieldActive(field))
            {
                float now = Time.time;
                int waveIndex = WaveAt(now - combatStarted);
                if (waveIndex < 0)
                {
                    yield return null;
                    continue;
                }
                var wave = Waves[waveIndex];
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
                yield return null;
            }
        }

        private int WaveAt(float elapsed)
        {
            int index = Waves.Length - 1;
            while (index >= 0 && Waves[index].StartTime > elapsed) index--;
            return index;
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
                if (!stage.IsFieldActive(field)) break;
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
                        if (!stage.IsFieldActive(field)) return;
                        for (int attempt = 0; attempt < PositionAttempts; attempt++)
                        {
                            Vector3 candidate = Candidate(player.position, field.LayoutOrigin.rotation, wave.Type,
                                step, slot, requested, phase, attempt);
                            if (!TryResolveSpawnPosition(stage, targetView, entry.Prefab, candidate, out Vector3 position)) continue;
                            Vector3 forward = player.position - position;
                            forward.y = 0f;
                            stage.SpawnWaveEnemy(entry.Prefab, position,
                                forward.sqrMagnitude > 0.001f ? Quaternion.LookRotation(forward) : Quaternion.identity, player);
                            available--;
                            break;
                        }
                    }
                    if (available <= 0) break;
                }
            }
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
