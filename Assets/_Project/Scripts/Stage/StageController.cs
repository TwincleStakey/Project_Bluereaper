using System;
using System.Collections;
using System.Collections.Generic;
using BlueReaper.Boss;
using BlueReaper.BulletPatterns;
using BlueReaper.Core;
using BlueReaper.Data;
using BlueReaper.HitBox;
using BlueReaper.Player;
using BlueReaper.Network;
using Unity.Netcode;
using BlueReaper.UI;
using BlueReaper.Vfx;
using UnityEngine;
using UnityEngine.AI;
using Random = UnityEngine.Random;

namespace BlueReaper.Stage
{
    public enum StageRunResult { Cleared, PartyDefeated, Aborted }

    public partial class StageController : MonoBehaviour
    {
        [Tooltip("이 스테이지에 참가한 플레이어 (1~3명)")]
        [SerializeField] List<Health> players = new();
        [SerializeField] BossHUD bossHUD;
        [SerializeField] TopDownCamera playerCamera;
        [SerializeField] StageData stageData;

        private sealed class StageUnitRecord
        {
            public Unit Unit;
            public Health Health;
            public Enemy Enemy;
            public EnemyAI AI;
            public StageField Field;
            public bool CountsForClear;
            public Transform SpawnOwner;
            public bool Dead;
            public Action OnDied;
            public Action OnRevived;
            public Action OnPresentationFinished;
        }

        readonly List<Transform> alivePlayers = new();
        readonly Dictionary<Unit, StageUnitRecord> units = new();
        readonly HashSet<ProjectileHitBox> deathProjectiles = new();
        public IReadOnlyList<Transform> AlivePlayers => alivePlayers;
        public event Action<StageRunResult> RunFinished;
        public event Action<int> FieldChanged;

        private Coroutine stageRoutine;
        private StageField currentField;
        private Health hudBoss;
        private int requiredKills;
        private int fieldKills;
        private bool bossesOnly;
        private bool fieldComplete;
        private bool managedRun;
        private bool runFinished;

        private void OnEnable()
        {
            GameEvents.BossSpawned += OnBossSpawned;
            GameEvents.BossDefeated += OnBossDefeated;
            if (bossHUD) bossHUD.Hide();
        }

        private void OnDisable()
        {
            GameEvents.BossSpawned -= OnBossSpawned;
            GameEvents.BossDefeated -= OnBossDefeated;
            if (stageRoutine != null) StopCoroutine(stageRoutine);
            stageRoutine = null;
            ReleaseStage();
        }

        private void Start()
        {
            if (managedRun) return;

            foreach (Health player in players)
            {
                if (!player || !player.isActiveAndEnabled || player.IsDead || alivePlayers.Contains(player.transform))
                    continue;
                RegisterPlayer(player);
            }

            if (playerCamera && alivePlayers.Count > 0) playerCamera.SetTarget(alivePlayers[0]);
            if (stageData) stageRoutine = StartCoroutine(RunStage());
        }

        public bool TryStartRun(StageData data, IReadOnlyList<Health> party)
        {
            if (!isActiveAndEnabled || managedRun || stageData || players.Count != 0 || !data
                || party == null || party.Count == 0) return false;

            var distinct = new HashSet<Health>();
            foreach (Health player in party)
                if (!player || !player.isActiveAndEnabled || player.IsDead || !distinct.Add(player)) return false;

            managedRun = true;
            stageData = data;
            foreach (Health player in party) RegisterPlayer(player);
            stageRoutine = StartCoroutine(RunStage());
            return true;
        }

        public void BindLocalCamera(Health player)
        {
            if (playerCamera && player) playerCamera.SetTarget(player.transform);
        }

        public void AbortRun() => FinishRun(clearConfirmed ? StageRunResult.Cleared : StageRunResult.Aborted);

        private void RegisterPlayer(Health player)
        {
            alivePlayers.Add(player.transform);
            var unit = player.GetComponent<Unit>();
            RegisterUnit(unit, null);
            StageUnitRecord record = units[unit];
            record.OnRevived = () =>
            {
                if (runFinished || clearConfirmed || !IsRegistered(record) || !record.Dead) return;
                record.Dead = false;
                alivePlayers.Add(player.transform);
            };
            player.Revived += record.OnRevived;
        }

        private IEnumerator RunStage()
        {
            yield return null;
            if (stageData.Fields == null || stageData.Fields.Length == 0 || alivePlayers.Count == 0)
            {
                AbortStage("StageData requires at least one field and an active player.");
                yield break;
            }

            foreach (var field in stageData.Fields)
                if (field && field.DynamicSpawner) usesDynamicFields = true;
            if (usesDynamicFields && (!playerCamera || !exitPortalPrefab))
            {
                AbortStage("Dynamic fields require the stage camera and the exit portal prefab.");
                yield break;
            }

            for (int index = 0; index < stageData.Fields.Length; index++)
            {
                StageField prefab = stageData.Fields[index];
                if (!prefab)
                {
                    AbortStage($"Field {index + 1} is missing from StageData.");
                    yield break;
                }

                currentField = Instantiate(prefab, transform.position, transform.rotation, transform);
                lastField = index + 1 == stageData.Fields.Length;
                var spawner = currentField.DynamicSpawner;
                if (!currentField.isActiveAndEnabled || !currentField.Entrance
                    || (spawner ? !spawner.IsConfigured() || currentField.PurificationGoal <= 0
                        || currentField.ReadyPads.Length < alivePlayers.Count
                        : !currentField.TryGetClearTarget(out requiredKills, out bossesOnly)))
                {
                    AbortStage($"Field {prefab.name} has an invalid entrance or spawn plan.");
                    yield break;
                }

                if (spawner)
                {
                    requiredKills = currentField.PurificationGoal;
                    bossesOnly = false;
                    foreach (var enemy in currentField.PlacedUnits)
                        if (enemy) enemy.gameObject.SetActive(false);
                }
                fieldKills = 0;
                fieldComplete = false;
                readyForNextField = false;
                FieldChanged?.Invoke(index);
                MovePlayers(currentField.Entrance);
                if (spawner)
                {
                    yield return WaitForReadyPads(currentField);
                    if (runFinished) yield break;
                    currentField.SetReadyPadsVisible(false);
                }
                else SetFieldProgress(StageFieldPhase.None);
                if (requiredKills == 0) CompleteField();
                foreach (Unit unit in currentField.PlacedUnits)
                {
                    unit.gameObject.SetActive(true);
                    if (!unit.gameObject.activeInHierarchy)
                    {
                        AbortStage($"Placed unit {unit.name} is under an inactive object.");
                        yield break;
                    }
                    var enemy = unit.GetComponent<Enemy>();
                    if (enemy)
                    {
                        NetworkWorld.SpawnPlaced(unit.gameObject);
                        InitEnemy(enemy);
                    }
                    else RegisterUnit(unit, currentField);
                    if (!isActiveAndEnabled) yield break;
                }

                if (spawner) yield return spawner.Run(this, currentField, playerCamera);
                for (uint round = 0; !spawner && round < currentField.RepeatSpawnCount && !fieldComplete; round++)
                {
                    if (!SpawnWave(currentField))
                    {
                        AbortStage($"Could not spawn a wave in field {prefab.name}. Check its enemy prefabs and NavMesh.");
                        yield break;
                    }

                    if (!isActiveAndEnabled) yield break;
                    if (round + 1 >= currentField.RepeatSpawnCount || fieldComplete) break;
                    float nextWaveAt = Time.time + currentField.RepeatSpawnDelay;
                    do { yield return null; }
                    while (!fieldComplete && !runFinished && Time.time < nextWaveAt);
                    if (runFinished) yield break;
                }

                while (!fieldComplete && !runFinished) yield return null;
                if (runFinished) yield break;
                if (lastField && clearConfirmed)
                    yield return WaitForExitPortal();
                else if (spawner)
                {
                    while (!readyForNextField && !runFinished) yield return null;
                    SetCombatLocked(false);
                }
                if (runFinished) yield break;
                FinishField(!lastField);
            }

            while (HasDyingUnits()) yield return null;
            stageRoutine = null;
            if (runFinished) yield break;
            FinishRun(StageRunResult.Cleared);
            if (!clearConfirmed || !managedRun) GameEvents.RaiseStageCleared();
        }

        private void MovePlayers(PlayerSpawnEntry entrance)
        {
            for (int i = 0; i < alivePlayers.Count; i++)
            {
                Transform player = alivePlayers[i];
                Vector3 position = entrance.transform.position;
                if (i > 0)
                {
                    float spacing = Mathf.Max(1f, entrance.SpawnRadius / alivePlayers.Count);
                    float offset = (i + 1) / 2 * (i % 2 == 1 ? 1f : -1f) * spacing;
                    position += entrance.transform.right * offset;
                }

                var controller = player.GetComponent<PlayerController>();
                var motor = player.GetComponent<PlayerMotor>();
                if (controller && controller.IsSpawned && controller.IsServer)
                    controller.PlaceFromHost(position, entrance.transform.rotation);
                else if (motor) motor.Teleport(position, entrance.transform.rotation);
                else player.SetPositionAndRotation(position, entrance.transform.rotation);
            }
        }

        private bool SpawnWave(StageField field)
        {
            foreach (EnemySpawnEntry entry in field.SpawnEntries)
            {
                for (int i = 0; i < entry.EnemyPrefab.Length; i++)
                {
                    Enemy prefab = entry.EnemyPrefab[i];
                    for (uint count = 0; count < entry.SpawnCount[i]; count++)
                    {
                        if (fieldComplete || !isActiveAndEnabled) return true;
                        Vector2 offset = Random.insideUnitCircle * entry.SpawnRadius;
                        Transform marker = entry.transform;
                        Vector3 position = marker.position + new Vector3(offset.x, 0f, offset.y);
                        if (!TryGetSpawnPosition(prefab, position, marker.position, out Vector3 spawnPosition))
                            return false;
                        SpawnEnemy(prefab, spawnPosition, marker.rotation);
                    }
                }
            }
            return true;
        }

        private static bool TryGetSpawnPosition(Enemy prefab, Vector3 position, Vector3 fallback, out Vector3 spawnPosition)
        {
            spawnPosition = position;
            var agent = prefab.GetComponent<NavMeshAgent>();
            if (!agent) return true;

            var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
            if (!NavMesh.SamplePosition(position, out NavMeshHit hit, 1f, filter)
                && !NavMesh.SamplePosition(fallback, out hit, 1f, filter))
                return false;

            spawnPosition = hit.position;
            return true;
        }

        internal void SummonEnemies(Unit summoner, Transform target, SummonPatternData pattern, Vector3? deathOrigin = null)
        {
            if (!units.TryGetValue(summoner, out StageUnitRecord owner)
                || owner.Dead != deathOrigin.HasValue || !IsFieldActive(owner.Field)) return;

            string error = pattern.GetValidationError();
            if (error != null)
            {
                Debug.LogWarning(error, pattern);
                return;
            }

            if (pattern.SummonPositionType == SummonPositionType.Target
                && (!target || !target.gameObject.activeInHierarchy || !alivePlayers.Contains(target))) return;

            Vector3 center = pattern.SummonPositionType switch
            {
                SummonPositionType.Target => target.position,
                SummonPositionType.FieldCenter => currentField.Center,
                _ => deathOrigin ?? summoner.transform.position
            };

            bool skippedSpawn = false;
            for (int i = 0; i < pattern.SummonPrefab.Length; i++)
            {
                Enemy prefab = pattern.SummonPrefab[i];
                for (uint count = 0; count < pattern.SummonCount[i]; count++)
                {
                    if (!IsFieldActive(owner.Field) || !IsRegistered(owner) || owner.Dead != deathOrigin.HasValue) return;
                    Vector2 offset = Random.insideUnitCircle * pattern.SummonRadius;
                    Vector3 position = center + new Vector3(offset.x, 0f, offset.y);
                    if (TryGetSpawnPosition(prefab, position, center, out Vector3 spawnPosition))
                        SpawnEnemy(prefab, spawnPosition, Quaternion.identity, countsForClear: false);
                    else
                        skippedSpawn = true;
                }
            }

            if (skippedSpawn) Debug.LogWarning("Skipped summon positions outside the NavMesh.", pattern);
        }

        public Enemy SpawnEnemy(Enemy prefab, Vector3 position, Quaternion rotation, bool countsForClear = true)
        {
            var enemy = NetworkWorld.Spawn(prefab.gameObject, position, rotation).GetComponent<Enemy>();
            InitEnemy(enemy, countsForClear);
            return enemy;
        }

        private void InitEnemy(Enemy enemy, bool countsForClear = true)
        {
            enemy.Init(alivePlayers);
            RegisterUnit(enemy.GetComponent<Unit>(), currentField, countsForClear);
            if (countsForClear && enemy.Rank == EnemyRank.Boss)
                GameEvents.RaiseBossSpawned(enemy.GetComponent<Health>());
        }

        private void RegisterUnit(Unit unit, StageField field, bool countsForClear = true)
        {
            if (units.ContainsKey(unit)) return;

            var record = new StageUnitRecord
            {
                Unit = unit,
                Health = unit.GetComponent<Health>(),
                Enemy = unit.GetComponent<Enemy>(),
                AI = unit.GetComponent<EnemyAI>(),
                Field = field,
                CountsForClear = countsForClear,
            };
            units.Add(unit, record);
            if (record.Health)
            {
                record.OnDied = () => OnUnitDied(record);
                record.Health.Died += record.OnDied;
            }
            if (record.AI)
            {
                record.AI.Stage = this;
                record.AI.NotifySpawned();
                record.OnPresentationFinished = () =>
                {
                    if (record.Dead) ReleaseUnit(record);
                };
                record.AI.DeathPresentationFinished += record.OnPresentationFinished;
            }
        }

        private void OnUnitDied(StageUnitRecord record)
        {
            if (!IsRegistered(record) || record.Dead || clearConfirmed) return;
            record.Dead = true;
            if (record.Field is null && !record.Enemy)
            {
                alivePlayers.Remove(record.Unit.transform);
                if (alivePlayers.Count == 0) FinishRun(StageRunResult.PartyDefeated);
                return;
            }

            if (!fieldComplete && record.CountsForClear && record.Enemy && currentField && ReferenceEquals(record.Field, currentField)
                && (!bossesOnly || record.Enemy.Rank == EnemyRank.Boss))
            {
                fieldKills++;
                if (fieldKills >= requiredKills) CompleteField();
                else if (currentField.DynamicSpawner) SetFieldProgress(StageFieldPhase.Combat, fieldKills, requiredKills);
            }

            if (!record.AI || !record.AI.isActiveAndEnabled) ReleaseUnit(record);
        }

        private bool IsRegistered(StageUnitRecord record) =>
            units.TryGetValue(record.Unit, out StageUnitRecord registered) && ReferenceEquals(record, registered);

        internal bool CanRunDeathPattern(Unit source) =>
            units.TryGetValue(source, out StageUnitRecord record) && record.Dead && IsFieldActive(record.Field);

        internal StageField GetActiveField(Unit source, bool afterDeath = false) =>
            units.TryGetValue(source, out StageUnitRecord record) && record.Dead == afterDeath && IsFieldActive(record.Field)
                ? record.Field : null;

        internal Func<bool> GetVfxGuard(StageField field) => () => this && IsFieldActive(field);

        internal void StartDeathBullets(Unit source, BulletPatternData pattern, float diedAt,
            Vector3 origin, Quaternion facing, float attack)
        {
            if (!CanRunDeathPattern(source)) return;
            string error = pattern.GetValidationError();
            if (!pattern.BulletPrefab || error != null)
            {
                Debug.LogWarning(error ?? "Death bullet pattern requires a bullet prefab.", pattern);
                return;
            }

            var damage = new AttackDamage(attack, pattern.DamageMultiplier);
            StartCoroutine(EmitDeathBullets(currentField, pattern, diedAt + pattern.StartTime, origin, facing, damage));
        }

        private IEnumerator EmitDeathBullets(StageField field, BulletPatternData pattern, float firstEmissionAt, Vector3 origin,
            Quaternion facing, AttackDamage damage)
        {
            var vfxGuard = GetVfxGuard(field);
            for (int emission = 0; emission < pattern.RepeatCount; emission++)
            {
                float emissionAt = firstEmissionAt + emission * pattern.RepeatInterval;
                while (IsFieldActive(field) && Time.time < emissionAt) yield return null;
                if (!IsFieldActive(field)) yield break;

                bool launched = false;
                for (int path = 0; path < pattern.MaximumCount; path++)
                {
                    var projectile = ProjectileHitBox.Spawn(pattern, path, emission, origin, facing);
                    if (!projectile) continue;
                    if (!IsFieldActive(field))
                    {
                        projectile.ReturnToPool();
                        yield break;
                    }
                    deathProjectiles.Add(projectile);
                    projectile.InitDeathProjectile(damage, pattern, path, emission, origin, facing,
                        OnDeathProjectileReturned, vfxGuard);
                    launched = true;
                }
                if (launched) VfxAutoDespawn.Play(pattern.LaunchVfxPrefab, origin, facing, canPlay: vfxGuard);
            }
        }

        internal bool IsFieldActive(StageField field) =>
            isActiveAndEnabled && !runFinished && !fieldComplete && field && ReferenceEquals(field, currentField);

        private void OnDeathProjectileReturned(ProjectileHitBox projectile) => deathProjectiles.Remove(projectile);

        private void ReleaseDeathProjectiles()
        {
            foreach (var projectile in new List<ProjectileHitBox>(deathProjectiles))
                if (projectile) projectile.ReturnToPool();
            deathProjectiles.Clear();
        }

        private void ReleaseUnit(StageUnitRecord record)
        {
            if (!IsRegistered(record)) return;
            units.Remove(record.Unit);
            if (record.Health)
            {
                record.Health.Died -= record.OnDied;
                record.Health.Revived -= record.OnRevived;
            }
            if (record.AI)
            {
                record.AI.DeathPresentationFinished -= record.OnPresentationFinished;
                record.AI.Stage = null;
            }
            if (record.Field is null && !record.Enemy) return;
            if (hudBoss == record.Health) HideBossHUD();
            if (!record.Unit) return;
            NetworkWorld.Despawn(record.Unit.gameObject);
        }

        private void FinishField(bool unloadField)
        {
            foreach (StageUnitRecord record in new List<StageUnitRecord>(units.Values))
            {
                if (!ReferenceEquals(record.Field, currentField)) continue;
                if (!record.Dead || !record.Unit || !record.AI || !record.AI.isActiveAndEnabled)
                    ReleaseUnit(record);
                else if (unloadField && record.Unit.transform.IsChildOf(currentField.transform))
                    record.Unit.transform.SetParent(transform, true);
            }

            if (unloadField) UnloadCurrentField();
        }

        private bool HasDyingUnits()
        {
            bool pending = false;
            foreach (StageUnitRecord record in new List<StageUnitRecord>(units.Values))
            {
                if (!record.Dead || (record.Field is null && !record.Enemy)) continue;
                if (record.Unit && record.AI && record.AI.isActiveAndEnabled) pending = true;
                else ReleaseUnit(record);
            }
            return pending;
        }

        private void AbortStage(string reason)
        {
            Debug.LogError(reason, this);
            stageRoutine = null;
            ReleaseStage();
            FinishRun(StageRunResult.Aborted);
        }

        private void FinishRun(StageRunResult result)
        {
            if (!managedRun || runFinished) return;
            runFinished = true;
            RunFinished?.Invoke(result);
        }

        private void ReleaseStage()
        {
            ReleaseExitPortal();
            SetCombatLocked(false);
            ReleaseDeathProjectiles();
            foreach (StageUnitRecord record in new List<StageUnitRecord>(units.Values))
                ReleaseUnit(record);
            alivePlayers.Clear();
            HideBossHUD();
            UnloadCurrentField();
        }

        private void UnloadCurrentField()
        {
            if (!currentField) return;
            currentField.gameObject.SetActive(false);
            Destroy(currentField.gameObject);
            currentField = null;
        }

        private void OnBossSpawned(Health boss)
        {
            if (!boss || boss.IsDead || !units.ContainsKey(boss.GetComponent<Unit>())) return;
            var enemy = boss.GetComponent<Enemy>();
            if (!enemy || enemy.Rank != EnemyRank.Boss) return;
            hudBoss = boss;
            if (!bossHUD) return;
            bossHUD.Init(enemy);
            bossHUD.Show();
        }

        private void OnBossDefeated(Health boss)
        {
            if (hudBoss == boss) HideBossHUD();
        }

        internal void ShowRemoteBoss(Enemy enemy)
        {
            if (!bossHUD || !enemy || enemy.Rank != EnemyRank.Boss) return;
            hudBoss = enemy.GetComponent<Health>();
            bossHUD.Init(enemy);
            bossHUD.Show();
        }

        internal void HideRemoteBoss(Health health)
        {
            if (hudBoss == health) HideBossHUD();
        }

        private void HideBossHUD()
        {
            hudBoss = null;
            if (bossHUD) bossHUD.Hide();
        }
    }
}
