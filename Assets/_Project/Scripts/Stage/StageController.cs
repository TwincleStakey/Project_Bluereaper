using System;
using System.Collections;
using System.Collections.Generic;
using BlueReaper.Boss;
using BlueReaper.BulletPatterns;
using BlueReaper.Core;
using BlueReaper.Data;
using BlueReaper.HitBox;
using BlueReaper.Network;
using BlueReaper.Player;
using BlueReaper.UI;
using UnityEngine;
using UnityEngine.AI;
using Random = UnityEngine.Random;

namespace BlueReaper.Stage
{
    public class StageController : MonoBehaviour
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
            public bool Dead;
            public Action OnDied;
            public Action OnRevived;
            public Action OnPresentationFinished;
        }

        readonly List<Transform> alivePlayers = new();
        readonly Dictionary<Unit, StageUnitRecord> units = new();
        readonly HashSet<ProjectileHitBox> deathProjectiles = new();
        public IReadOnlyList<Transform> AlivePlayers => alivePlayers;

        private Coroutine stageRoutine;
        private StageField currentField;
        private Health hudBoss;
        private int requiredKills;
        private int fieldKills;
        private bool bossesOnly;
        private bool fieldComplete;
        // [네트워크] 씬에 NetworkManager가 있을 때만 붙는다. 없으면 null → 예전과 똑같이 혼자 진행
        private StageNetwork network;

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
            // [네트워크] NetworkManager는 자기 OnEnable에서 준비되므로, 모든 오브젝트가 깨어난 뒤인 Start에서 확인한다
            if (StageNetwork.HasNetwork) network = gameObject.AddComponent<StageNetwork>();
            foreach (Health player in players)
            {
                if (!player || !player.isActiveAndEnabled || player.IsDead || alivePlayers.Contains(player.transform))
                    continue;
                alivePlayers.Add(player.transform);
                RegisterUnit(player.GetComponent<Unit>(), null);
            }

            if (playerCamera && alivePlayers.Count > 0) playerCamera.SetTarget(alivePlayers[0]);
            if (stageData) stageRoutine = StartCoroutine(network ? RunStageAsHost() : RunStage());
        }

        /// <summary>
        /// [네트워크] 방이 열릴 때까지 기다렸다가 호스트만 스테이지를 진행한다.
        /// 팀원 PC는 진행하지 않고, 호스트가 알려 주는 필드(ShowFieldForClient)와 호스트가 만든 적을 보여 주기만 한다.
        /// </summary>
        private IEnumerator RunStageAsHost()
        {
            while (!StageNetwork.IsHost)
            {
                if (StageNetwork.IsRemoteClient)
                {
                    stageRoutine = null;
                    yield break;
                }
                yield return null;
            }
            yield return RunStage();
        }

        /// <summary>
        /// [네트워크] 실행 중에 들어온 플레이어(팀원)를 적의 추적 대상에 넣는다. 호스트의 PlayerSpawner가 부른다.
        /// 인스펙터의 players 목록은 시작할 때 씬에 있던 플레이어만 담을 수 있어서 따로 필요하다.
        /// </summary>
        public void AddPlayer(Health player)
        {
            // 나간 팀원(파괴된 오브젝트)은 목록에서 정리
            alivePlayers.RemoveAll(alive => !alive);
            if (!player || player.IsDead || alivePlayers.Contains(player.transform)) return;
            alivePlayers.Add(player.transform);
            RegisterUnit(player.GetComponent<Unit>(), null);
        }

        /// <summary>
        /// [네트워크] 팀원 PC 전용: 호스트가 깐 필드를 똑같이 깐다 (index -1이면 치우기만).
        /// 필드에 미리 놓인 적은 지운다 — 적은 호스트가 네트워크로 만들어 보내 준다.
        /// </summary>
        internal void ShowFieldForClient(int index)
        {
            UnloadCurrentField();
            if (!stageData || stageData.Fields == null || index < 0 || index >= stageData.Fields.Length || !stageData.Fields[index])
                return;

            currentField = Instantiate(stageData.Fields[index], transform.position, transform.rotation, transform);
            foreach (Unit unit in currentField.PlacedUnits)
                if (unit && unit.GetComponent<Enemy>()) Destroy(unit.gameObject);

            // 내 캐릭터는 내 PC가 위치를 정하므로(NetworkTransform 주인 권한) 입구로 옮기는 것도 내 PC에서 한다
            var manager = Unity.Netcode.NetworkManager.Singleton;
            var me = manager.LocalClient?.PlayerObject;
            if (me && currentField.Entrance)
                TeleportPlayer(me.transform, currentField.Entrance, (int)(manager.LocalClientId % 3), 3);
        }

        private IEnumerator RunStage()
        {
            yield return null;
            if (stageData.Fields == null || stageData.Fields.Length == 0 || alivePlayers.Count == 0)
            {
                AbortStage("StageData requires at least one field and an active player.");
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
                if (network) network.SetField(index); // [네트워크] 팀원 PC도 같은 필드를 깔게 알림
                if (!currentField.isActiveAndEnabled || !currentField.TryGetClearTarget(out requiredKills, out bossesOnly))
                {
                    AbortStage($"Field {prefab.name} has an invalid entrance or spawn plan.");
                    yield break;
                }

                fieldKills = 0;
                fieldComplete = requiredKills == 0;
                MovePlayers(currentField.Entrance);
                foreach (Unit unit in currentField.PlacedUnits)
                {
                    unit.gameObject.SetActive(true);
                    if (!unit.gameObject.activeInHierarchy)
                    {
                        AbortStage($"Placed unit {unit.name} is under an inactive object.");
                        yield break;
                    }
                    var enemy = unit.GetComponent<Enemy>();
                    if (enemy) StageNetwork.Spawn(unit.gameObject); // [네트워크] 팀원 PC에도 생기게
                    if (enemy) InitEnemy(enemy);
                    else RegisterUnit(unit, currentField);
                    if (!isActiveAndEnabled) yield break;
                }

                for (uint round = 0; round < currentField.RepeatSpawnCount && !fieldComplete; round++)
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
                    while (!fieldComplete && Time.time < nextWaveAt);
                }

                while (!fieldComplete) yield return null;
                FinishField(index + 1 < stageData.Fields.Length);
            }

            while (HasDyingUnits()) yield return null;
            stageRoutine = null;
            GameEvents.RaiseStageCleared();
        }

        private void MovePlayers(PlayerSpawnEntry entrance)
        {
            for (int i = 0; i < alivePlayers.Count; i++)
            {
                // [네트워크] 나간 팀원은 오브젝트가 파괴돼 있을 수 있다
                if (!alivePlayers[i]) continue;
                TeleportPlayer(alivePlayers[i], entrance, i, alivePlayers.Count);
            }
        }

        // (기존 MovePlayers 안의 코드를 그대로 뺀 것. 팀원 PC가 자기 캐릭터만 옮길 때도 같은 배치를 쓰려고 분리)
        private static void TeleportPlayer(Transform player, PlayerSpawnEntry entrance, int i, int count)
        {
            Vector3 position = entrance.transform.position;
            if (i > 0)
            {
                float spacing = Mathf.Max(1f, entrance.SpawnRadius / count);
                float offset = (i + 1) / 2 * (i % 2 == 1 ? 1f : -1f) * spacing;
                position += entrance.transform.right * offset;
            }

            var motor = player.GetComponent<PlayerMotor>();
            if (motor) motor.Teleport(position, entrance.transform.rotation);
            else player.SetPositionAndRotation(position, entrance.transform.rotation);
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
            var enemy = ObjectPool.Spawn(prefab.gameObject, position, rotation).GetComponent<Enemy>();
            StageNetwork.Spawn(enemy.gameObject); // [네트워크] 호스트에서 만든 적을 팀원 PC에도 생기게
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
                // [네트워크·구조] 플레이어는 빈사 때 목록에서 빠지므로, 구조되면 다시 넣는다
                if (field is null && !record.Enemy)
                {
                    record.OnRevived = () => OnPlayerRevived(record);
                    record.Health.Revived += record.OnRevived;
                }
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
            if (!IsRegistered(record) || record.Dead) return;
            record.Dead = true;
            if (record.Field is null && !record.Enemy)
            {
                alivePlayers.Remove(record.Unit.transform);
                return;
            }

            if (record.CountsForClear && record.Enemy && currentField && ReferenceEquals(record.Field, currentField)
                && (!bossesOnly || record.Enemy.Rank == EnemyRank.Boss))
            {
                fieldKills++;
                if (fieldKills >= requiredKills)
                {
                    fieldComplete = true;
                    ReleaseDeathProjectiles();
                }
            }

            if (!record.AI || !record.AI.isActiveAndEnabled) ReleaseUnit(record);
        }

        private void OnPlayerRevived(StageUnitRecord record)
        {
            if (!IsRegistered(record) || !record.Dead) return;
            record.Dead = false;
            if (!alivePlayers.Contains(record.Unit.transform)) alivePlayers.Add(record.Unit.transform);
        }

        private bool IsRegistered(StageUnitRecord record) =>
            units.TryGetValue(record.Unit, out StageUnitRecord registered) && ReferenceEquals(record, registered);

        internal bool CanRunDeathPattern(Unit source) =>
            units.TryGetValue(source, out StageUnitRecord record) && record.Dead && IsFieldActive(record.Field);

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

            float damage = DamageFormula.Calculate(attack, pattern.DamageMultiplier);
            StartCoroutine(EmitDeathBullets(currentField, pattern, diedAt + pattern.StartTime, origin, facing, damage));
        }

        private IEnumerator EmitDeathBullets(StageField field, BulletPatternData pattern, float firstEmissionAt, Vector3 origin,
            Quaternion facing, float damage)
        {
            for (int emission = 0; emission < pattern.RepeatCount; emission++)
            {
                float emissionAt = firstEmissionAt + emission * pattern.RepeatInterval;
                while (IsFieldActive(field) && Time.time < emissionAt) yield return null;
                if (!IsFieldActive(field)) yield break;

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
                    projectile.InitDeathProjectile(damage, pattern, path, emission, origin, facing, OnDeathProjectileReturned);
                }
            }
        }

        private bool IsFieldActive(StageField field) =>
            isActiveAndEnabled && !fieldComplete && field && ReferenceEquals(field, currentField);

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
            if (record.Health) record.Health.Died -= record.OnDied;
            if (record.Health && record.OnRevived != null) record.Health.Revived -= record.OnRevived;
            if (record.AI)
            {
                record.AI.DeathPresentationFinished -= record.OnPresentationFinished;
                record.AI.Stage = null;
            }
            if (record.Field is null && !record.Enemy) return;
            if (hudBoss == record.Health) HideBossHUD();
            if (!record.Unit) return;
            StageNetwork.Despawn(record.Unit.gameObject); // [네트워크] 팀원 PC에서도 사라지게 (호스트는 풀에서 재사용)
            record.Unit.gameObject.SetActive(false);
            ObjectPool.Despawn(record.Unit.gameObject);
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
        }

        private void ReleaseStage()
        {
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
            if (network) network.SetField(-1); // [네트워크] 팀원 PC도 필드를 치우게
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

        /// <summary>[네트워크] 팀원 PC 전용: 호스트가 만든 보스가 내 화면에 생기면 보스 HP 바를 띄운다.</summary>
        internal void ShowRemoteBoss(Enemy boss)
        {
            hudBoss = boss.GetComponent<Health>();
            if (!bossHUD) return;
            bossHUD.Init(boss);
            bossHUD.Show();
        }

        /// <summary>[네트워크] 팀원 PC 전용: 보스가 사라지면 HP 바를 숨긴다.</summary>
        internal void HideRemoteBoss(Health boss)
        {
            if (hudBoss == boss) HideBossHUD();
        }

        private void HideBossHUD()
        {
            hudBoss = null;
            if (bossHUD) bossHUD.Hide();
        }
    }
}
