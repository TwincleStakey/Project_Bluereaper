using System;
using System.Collections;
using System.Collections.Generic;
using BlueReaper.Boss;
using BlueReaper.Core;
using BlueReaper.Network;
using UnityEngine;
using UnityEngine.AI;

namespace BlueReaper.Stage
{
    internal enum StageFieldPhase { None, Preparing, Countdown, Combat, NextField, ExitPortal }

    public partial class StageController
    {
        [SerializeField] private StageExitPortal exitPortalPrefab;

        private readonly HashSet<Health> readyPlayers = new();
        private StageExitPortal exitPortal;
        private bool lastField, readyForNextField, enteredExitPortal, clearConfirmed;

        public HuntPhase CurrentHuntPhase { get; private set; }
        public event Action<HuntPhase> HuntPhaseChanged;
        /// <summary>Authoritative notification after an enemy has been initialized in a hunting field.</summary>
        public event Action<Enemy, HuntPhase> HuntEnemySpawned;

        internal (StageFieldPhase Phase, int Current, int Goal) FieldProgress { get; private set; }
        internal event Action FieldProgressChanged;

        internal void ApplyRemoteHuntPhase(HuntPhase phase) => SetHuntPhase(phase);

        private bool SetHuntPhase(HuntPhase phase)
        {
            if (CurrentHuntPhase == phase) return false;
            CurrentHuntPhase = phase;
            HuntPhaseChanged?.Invoke(phase);
            return true;
        }

        private void SetFieldProgress(StageFieldPhase phase, int current = 0, int goal = 0, bool forceNotify = false)
        {
            var progress = (phase, current, goal);
            if (FieldProgress == progress && !forceNotify) return;
            FieldProgress = progress;
            FieldProgressChanged?.Invoke();
        }

        private IEnumerator WaitForReadyPads(StageField field)
        {
            int needed = alivePlayers.Count;
            float countdownUntil = 0f;
            SetCombatLocked(true);
            while (!runFinished)
            {
                readyPlayers.Clear();
                foreach (var pad in field.ReadyPads)
                {
                    Health occupant = pad.GetSingleOccupant(alivePlayers);
                    if (occupant) readyPlayers.Add(occupant);
                }
                if (readyPlayers.Count == needed)
                {
                    if (countdownUntil == 0f) countdownUntil = Time.time + 1f;
                    SetFieldProgress(StageFieldPhase.Countdown, needed, needed);
                    if (Time.time >= countdownUntil) break;
                }
                else
                {
                    countdownUntil = 0f;
                    SetFieldProgress(StageFieldPhase.Preparing, readyPlayers.Count, needed);
                }
                yield return null;
            }
            if (runFinished) yield break;
            SetCombatLocked(false);
            SetHuntPhase(HuntPhase.Afternoon);
            SetFieldProgress(StageFieldPhase.Combat, 0, requiredKills);
        }

        internal int CountEnemiesFor(Transform player)
        {
            int count = 0;
            foreach (var record in units.Values)
                if (ReferenceEquals(record.Field, currentField) && !record.Dead && record.SpawnOwner == player)
                    count++;
            return count;
        }

        internal void SpawnWaveEnemy(Enemy prefab, Vector3 position, Quaternion rotation, Transform owner)
        {
            var enemy = SpawnEnemy(prefab, position, rotation);
            units[enemy.GetComponent<Unit>()].SpawnOwner = owner;
            enemy.Aggro?.AddAggro(owner.GetComponent<Unit>(), 1f);
            if (enemy.TryGetComponent(out EnemyAI ai)) ai.SetInitialChaseTarget(owner);
        }

        internal bool IsSpawnSpaceFree(Vector3 point, float radius, float height)
        {
            foreach (var record in units.Values)
            {
                if (!record.Unit || record.Dead || !record.Unit.isActiveAndEnabled) continue;
                var transform = record.Unit.transform;
                float otherRadius, otherHeight;
                Vector3 feet = transform.position;
                if (record.Unit.TryGetComponent(out CharacterController controller))
                {
                    var scale = transform.lossyScale;
                    otherRadius = controller.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
                    otherHeight = Mathf.Max(2f * otherRadius, controller.height * Mathf.Abs(scale.y));
                    feet = transform.TransformPoint(controller.center) - Vector3.up * (otherHeight * 0.5f);
                }
                else if (record.Unit.TryGetComponent(out NavMeshAgent agent))
                {
                    var scale = transform.lossyScale;
                    otherRadius = agent.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
                    otherHeight = Mathf.Max(2f * otherRadius, agent.height * Mathf.Abs(scale.y));
                }
                else continue;

                float x = point.x - feet.x, z = point.z - feet.z;
                float above = point.y + radius - (feet.y + otherHeight - otherRadius);
                float below = feet.y + otherRadius - (point.y + height - radius);
                float gap = Mathf.Max(0f, Mathf.Max(above, below));
                float combinedRadius = radius + otherRadius;
                if (x * x + z * z + gap * gap < combinedRadius * combinedRadius) return false;
            }
            return true;
        }

        private void CompleteField()
        {
            fieldComplete = true;
            HideBossHUD();
            if (lastField)
            {
                clearConfirmed = true;
                OpenExitPortal();
                SetFieldProgress(StageFieldPhase.ExitPortal, requiredKills, requiredKills);
                GameEvents.RaiseStageCleared();
                return;
            }

            ReleaseDeathProjectiles();
            SetCombatLocked(true);
            foreach (var record in units.Values)
                if (!record.Dead && ReferenceEquals(record.Field, currentField) && record.AI) record.AI.Release();
            SetFieldProgress(StageFieldPhase.NextField, requiredKills, requiredKills);
        }

        private void SetCombatLocked(bool locked)
        {
            foreach (var record in units.Values)
                if (record.Health) record.Health.CombatLocked = locked;
        }

        internal void AdvanceClearedField()
        {
            if (FieldProgress.Phase == StageFieldPhase.NextField) readyForNextField = true;
        }

        private void OpenExitPortal()
        {
            Vector3 center = Vector3.zero;
            int count = 0;
            Transform fallback = null;
            foreach (var player in alivePlayers)
            {
                if (!player) continue;
                center += player.position;
                count++;
                fallback = player;
            }
            if (!fallback || !exitPortalPrefab) return;

            center /= count;
            if (NavMesh.SamplePosition(center, out var hit, 1.5f, NavMesh.AllAreas))
                center = hit.position;
            else center = fallback.position;

            exitPortal = NetworkWorld.Spawn(exitPortalPrefab.gameObject, center, Quaternion.identity).GetComponent<StageExitPortal>();
            exitPortal.Arm(this);
        }

        private IEnumerator WaitForExitPortal()
        {
            while (exitPortal && !enteredExitPortal && !runFinished) yield return null;
            ReleaseExitPortal();
        }

        internal void EnterExitPortal(StageExitPortal portal)
        {
            if (clearConfirmed && portal == exitPortal && !runFinished) enteredExitPortal = true;
        }

        private void ReleaseExitPortal()
        {
            if (exitPortal) NetworkWorld.Despawn(exitPortal.gameObject);
            exitPortal = null;
        }
    }
}
