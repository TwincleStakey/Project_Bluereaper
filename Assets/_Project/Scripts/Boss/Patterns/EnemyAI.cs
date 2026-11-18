using BlueReaper.BulletPatterns;
using BlueReaper.Core;
using BlueReaper.Data;
using BlueReaper.HitBox;
using BlueReaper.Network;
using BlueReaper.Stage;
using BlueReaper.Vfx;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

namespace BlueReaper.Boss
{
    enum PatternState { None, Start, Executing, Done }

    [RequireComponent(typeof(Unit), typeof(Enemy), typeof(NavMeshAgent))]
    public class EnemyAI : MonoBehaviour
    {
        [SerializeField] EnemyActionData[] patterns;
        [SerializeField] EnemyActionData spawnPattern;
        [SerializeField] EnemyPatternData deathPattern;
        [SerializeField] float aiTick = 0.1f;
        [SerializeField] float stopChasingDistance = 3f;
        [SerializeField] TargetRule chaseTargetRule = new();

        private readonly struct AreaPlacement
        {
            internal readonly Matrix4x4 Layout;
            internal readonly Quaternion Facing;

            internal AreaPlacement(Transform origin)
            {
                Layout = origin.localToWorldMatrix;
                Facing = Quaternion.Euler(0f, origin.eulerAngles.y, 0f);
            }

            internal AreaPlacement(Vector3 position, Quaternion facing)
            {
                Layout = Matrix4x4.TRS(position, facing, Vector3.one);
                Facing = facing;
            }
        }

        private sealed class PatternExecution
        {
            internal AreaPlacement[] CapturedPlacements;
        }

        private struct AreaCast
        {
            internal AreaAttackPatternData Pattern;
            internal float NextAt, HitAt, LastSample;
            internal AttackDamage Damage;
            internal HitWindow Window;
            internal bool Placed, HasHit;
            internal AreaPlacement Placement;
            internal AreaPlacement[] CapturedPlacements;
        }

        private PatternState currentPatternState;
        private Unit unit;
        private Enemy Owner;
        private Health health;
        private Posture posture;
        private NavMeshAgent agent;
        private Animator animator;
        private readonly List<GameObject> activeTelegraphs = new();
        private Coroutine coAIUpdate = null;
        private Coroutine coMeleeHitWindow = null;
        private MeleeHitBox[] meleeHitBoxes;
        private readonly HitWindow meleeHits = new();
        private readonly AreaHitQuery areaQuery = new();
        private readonly HashSet<ProjectileHitBox> projectiles = new();
        private MeleeAttackPatternData activeMelee;
        private bool meleeAfterDeath;
        private float meleeStartedAt, motionUntil;
        private AttackDamage meleeDamage;
        private Vector3 chaseDestination;
        private bool hasChaseDestination;
        private bool rewardHandled;
        private bool spawnPatternPending;
        private Coroutine coForwardTracking;
        private float speedBeforeTracking;
        private float angularSpeedBeforeTracking;
        private Vector3 trackingDirection;

        internal StageController Stage { private get; set; }
        public event Action DeathPresentationFinished;

        private void Awake()
        {
            unit = GetComponent<Unit>();
            Owner = GetComponent<Enemy>();
            health = GetComponent<Health>();
            posture = GetComponent<Posture>();
            agent = GetComponent<NavMeshAgent>();
            animator = GetComponent<Animator>();
            meleeHitBoxes = GetComponentsInChildren<MeleeHitBox>(true);
            foreach (var hitBox in meleeHitBoxes)
            {
                hitBox.Init(unit, 1f, OnMeleeContact);
                hitBox.SetArmed(false);
            }
            // Attack triggers follow the agent-driven root without physics moving it.
            var body = GetComponent<Rigidbody>();
            if (!body) body = gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            if (unit.Data) agent.speed = unit.Data.MoveSpeed;
            agent.stoppingDistance = stopChasingDistance;
        }

        private void OnEnable()
        {
            rewardHandled = false;
            spawnPatternPending = false;
            health.Died += OnHealthDied;
            if (posture) posture.GroggyStarted += OnGroggyStarted;

            if(Owner.Rank == EnemyRank.Boss)
                GameEvents.BossDefeated += OnBossDefeated;

            currentPatternState = PatternState.None;
            hasChaseDestination = false;
            if (coAIUpdate == null) coAIUpdate = StartCoroutine(AIUpdate());
        }

        internal void NotifySpawned() => spawnPatternPending = true;

        private void OnHealthDied()
        {
            //TODO : 유닛이 죽으면 died 이벤트를 발생 시키고, 각자 애니메이션과 기타 후처리를 수행하도록 변경. 현재는 ai가 죽음 처리까지 담당하고 있음
            Release();
            StartCoroutine(IEDied(deathPattern));
        }

        private void OnBossDefeated(Health boss)
        {
            if (boss != health || !health.IsDead || rewardHandled) return;
            rewardHandled = true;
            //TODO : 우선 보상 테이블은 enemy에 추가, 획득 처리 로직은 ai에서 담당하는데 실제 구현 단계에서는 즉시 처리는 enemy에서, 지연 처리는 stage에서 최종적으로 lazy하게
            if (Owner && Owner.RewardTable)
            {
                (uint id, uint count) reward = Owner.RewardTable.GetReward();
                Debug.Log($"Enemy {Owner.name} died. Reward: {reward.count} of item ID {reward.id}");
            }
        }

        private void OnDisable()
        {
            health.Died -= OnHealthDied;
            if (posture) posture.GroggyStarted -= OnGroggyStarted;

            if (Owner.Rank == EnemyRank.Boss)
                GameEvents.BossDefeated -= OnBossDefeated;

            Release();
        }

        private void OnGroggyStarted()
        {
            if (health.IsDead) return;

            if (agent && agent.isActiveAndEnabled && agent.isOnNavMesh) agent.ResetPath();
            StopActions();
            motionUntil = 0f;
            PlayLocomotion(false);
            coAIUpdate = StartCoroutine(AIUpdate());
        }

        public void Release()
        {
            StopActions();
            foreach (var projectile in new List<ProjectileHitBox>(projectiles))
                if (projectile) projectile.ReturnToPool();
            projectiles.Clear();
        }

        private void StopActions()
        {
            StopMeleeHitWindow();
            StopForwardTracking();
            StopAllCoroutines();
            ReturnTelegraph();
            coAIUpdate = null;
            trackingDirection = Vector3.zero;
            hasChaseDestination = false;
            if (agent && agent.isActiveAndEnabled && agent.isOnNavMesh) agent.isStopped = true;
            currentPatternState = PatternState.None;
        }

        private IEnumerator AIUpdate()
        {
            // ObjectPool activates the boss before StageController injects its players.
            yield return null;
            var wait = new WaitForSeconds(Mathf.Max(0.01f, aiTick));

            EnemyActionData selectedAction = null;
            var chaseTargets = new List<Transform>();
            var actionTargets = new List<Transform>();

            while (!health.IsDead)
            {
                if (posture && posture.IsGroggy)
                {
                    yield return null;
                    continue;
                }

                EnemyTargetSelector.Select(Owner, Owner.Players, chaseTargetRule, chaseTargets);
                if (chaseTargets.Count > 1) chaseTargets.RemoveRange(1, chaseTargets.Count - 1);

                if (spawnPatternPending && spawnPattern)
                {
                    spawnPatternPending = false;
                    bool resolved = ResolveTargets(spawnPattern, chaseTargets, actionTargets);
                    if (resolved || spawnPattern.TargetMode == ActionTargetMode.Inherit)
                    {
                        Transform spawnTarget = FirstTarget(actionTargets);
                        UpdateChaseSpeed(spawnTarget);
                        FaceTarget(spawnTarget, spawnPattern.TargetMode == ActionTargetMode.Inherit);
                        yield return ExecuteAction(spawnPattern, actionTargets);
                        trackingDirection = Vector3.zero;
                        PlayLocomotion(false);
                        yield return wait;
                        continue;
                    }
                }

                if (selectedAction)
                {
                    if (selectedAction.TargetMode == ActionTargetMode.Inherit)
                    {
                        if (!ResolveTargets(selectedAction, chaseTargets, actionTargets)) selectedAction = null;
                    }
                    else if (selectedAction.TargetMode == ActionTargetMode.Select)
                    {
                        actionTargets.RemoveAll(target => !IsCurrentPlayer(target));
                        if (actionTargets.Count == 0) selectedAction = null;
                    }
                }
                if (!selectedAction && (chaseTargets.Count > 0 || Owner.Players.Any(EnemyTargetSelector.IsAlive)))
                    selectedAction = SelectNextPattern(chaseTargets, actionTargets);

                EnemyPatternData firstPattern = selectedAction switch
                {
                    EnemyPatternData pattern => pattern,
                    EnemyPatternSequenceData { Steps: { Length: > 0 } steps } => steps[0],
                    _ => null
                };
                Transform target = FirstTarget(actionTargets);
                UpdateChaseSpeed(target);
                if (!firstPattern || (!target && selectedAction.TargetMode != ActionTargetMode.None))
                {
                    if (!firstPattern) selectedAction = null;
                    if (agent.isActiveAndEnabled && agent.isOnNavMesh) agent.isStopped = true;
                    PlayLocomotion(false);
                    yield return wait;
                    continue;
                }

                if (target)
                {
                    Vector3 direction = target.position - transform.position;
                    direction.y = 0f;
                    Vector3 destination = target.position - direction.normalized * firstPattern.ChasingDistance;
                    if (!MoveTo(destination) || !IsCompleteDestination())
                    {
                        PlayLocomotion(agent.isActiveAndEnabled && agent.isOnNavMesh && agent.hasPath);
                        yield return wait;
                        continue;
                    }
                }
                else
                {
                    hasChaseDestination = false;
                }

                if (agent.isActiveAndEnabled && agent.isOnNavMesh) agent.isStopped = true;
                FaceTarget(target);

                yield return ExecuteAction(selectedAction, actionTargets);
                trackingDirection = Vector3.zero;
                selectedAction = null;

                PlayLocomotion(false);
                yield return wait;
            }
            coAIUpdate = null;
        }

        private static Transform FirstTarget(IReadOnlyList<Transform> targets) => targets.Count > 0 ? targets[0] : null;

        private bool IsCurrentPlayer(Transform target) =>
            EnemyTargetSelector.IsAlive(target) && Owner.Players.Contains(target);

        private void UpdateChaseSpeed(Transform target)
        {
            bool outsideSight = target && (target.position - transform.position).sqrMagnitude > Owner.SightRange * Owner.SightRange;
            float ratio = outsideSight ? Owner.OutSightTrackingPercent : 1f;
            agent.speed = unit.Data.MoveSpeed * ratio;
            agent.angularSpeed = 360f * ratio;
        }

        private void FaceTarget(Transform target, bool useForwardIfMissing = false)
        {
            Vector3 direction = target ? target.position - transform.position
                : useForwardIfMissing ? transform.forward : Vector3.zero;
            direction.y = 0f;
            trackingDirection = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.zero;
            if (trackingDirection != Vector3.zero) transform.rotation = Quaternion.LookRotation(trackingDirection);
        }

        private bool ResolveTargets(EnemyActionData action, IReadOnlyList<Transform> inherited, List<Transform> results)
        {
            results.Clear();
            switch (action.TargetMode)
            {
                case ActionTargetMode.None:
                    return true;
                case ActionTargetMode.Select:
                    EnemyTargetSelector.Select(Owner, Owner.Players, action.TargetRule, results);
                    break;
                default:
                    results.AddRange(inherited);
                    break;
            }
            return results.Count > 0;
        }

        private void StartForwardTracking(EnemyActionData settings)
        {
            float duration = settings.TrackingDuration;
            if (!(duration > 0f) || float.IsInfinity(duration) || trackingDirection == Vector3.zero
                || !agent || !agent.isActiveAndEnabled || !agent.isOnNavMesh) return;

            speedBeforeTracking = agent.speed;
            angularSpeedBeforeTracking = agent.angularSpeed;
            coForwardTracking = StartCoroutine(IEForwardTracking(settings, duration));
        }

        private IEnumerator IEForwardTracking(EnemyActionData settings, float duration)
        {
            agent.ResetPath();
            agent.velocity = Vector3.zero;
            hasChaseDestination = false;
            agent.speed = speedBeforeTracking * settings.TrackingMoveRatio;
            agent.angularSpeed = angularSpeedBeforeTracking * settings.TrackingRotationRatio;
            agent.isStopped = false;

            float elapsed = 0f;
            while (elapsed < duration && agent && agent.isActiveAndEnabled && agent.isOnNavMesh)
            {
                float stepTime = Mathf.Min(Time.deltaTime, duration - elapsed);
                if (stepTime > 0f && agent.speed > 0f)
                    agent.Move(trackingDirection * (agent.speed * stepTime));

                elapsed += stepTime;
                yield return null;
            }

            ClearForwardTracking();
        }

        private void StopForwardTracking()
        {
            if (coForwardTracking == null) return;
            StopCoroutine(coForwardTracking);
            ClearForwardTracking();
        }

        private void ClearForwardTracking()
        {
            coForwardTracking = null;
            if (!agent) return;
            agent.speed = speedBeforeTracking;
            agent.angularSpeed = angularSpeedBeforeTracking;
            if (agent.isActiveAndEnabled && agent.isOnNavMesh) agent.isStopped = true;
        }

        private EnemyActionData SelectNextPattern(IReadOnlyList<Transform> chaseTargets, List<Transform> actionTargets)
        {
            Owner.CalcPhase();
            actionTargets.Clear();
            if (patterns == null) return null;

            var candidates = patterns.Where(action => action && action.RequirePhase != null
                && action.RequirePhase.Contains(Owner.Phase)
                && (action is EnemyPatternData
                    || (action is EnemyPatternSequenceData { Steps: { Length: > 0 } steps }
                        && steps.All(step => step)))).ToList();
            while (candidates.Count > 0)
            {
                int index = UnityEngine.Random.Range(0, candidates.Count);
                var action = candidates[index];
                if (ResolveTargets(action, chaseTargets, actionTargets)) return action;
                candidates.RemoveAt(index);
            }
            return null;
        }

        private IEnumerator ExecuteAction(EnemyActionData action, IReadOnlyList<Transform> targets)
        {
            var execution = new PatternExecution();
            if (action is EnemyPatternData pattern)
            {
                yield return ExecutePattern(pattern, targets, action.CanForwardTracking, execution);
                yield break;
            }

            if (action is EnemyPatternSequenceData { Steps: { Length: > 0 } steps })
            {
                List<Transform> stepTargets = null;
                foreach (EnemyPatternData step in steps)
                {
                    if (health.IsDead || !step) yield break;
                    if (step.TargetMode == ActionTargetMode.Inherit)
                    {
                        yield return ExecutePattern(step, targets, action.CanForwardTracking, execution);
                        continue;
                    }

                    stepTargets ??= new List<Transform>();
                    if (!ResolveTargets(step, targets, stepTargets) && step.PlacementMode == PatternPlacementMode.Default) continue;
                    Quaternion facing = transform.rotation;
                    Vector3 direction = trackingDirection;
                    FaceTarget(FirstTarget(stepTargets));
                    yield return ExecutePattern(step, stepTargets, action.CanForwardTracking, execution);
                    if (health.IsDead) yield break;
                    transform.rotation = facing;
                    trackingDirection = direction;
                }
            }
        }

        private bool MoveTo(Vector3 position)
        {
            if (!agent.isActiveAndEnabled || !agent.isOnNavMesh) return false;
            agent.isStopped = false;
            if (agent.pathPending) return true;
            if (hasChaseDestination && (chaseDestination - position).sqrMagnitude < 0.01f
                && agent.pathStatus == NavMeshPathStatus.PathComplete) return true;
            chaseDestination = position;
            hasChaseDestination = agent.SetDestination(position);
            if (hasChaseDestination) return true;
            agent.ResetPath();
            return false;
        }

        private bool IsCompleteDestination() => agent.isActiveAndEnabled && agent.isOnNavMesh
            && !agent.pathPending && agent.pathStatus == NavMeshPathStatus.PathComplete
            && agent.remainingDistance <= agent.stoppingDistance + 0.05f;

        private void PlayMotion(AnimationClip clip)
        {
            motionUntil = Time.time + (clip ? clip.length : 0f);
            if (!animator || !animator.runtimeAnimatorController) return;
            animator.SetFloat("MoveSpeed", 0f);
            if (clip) animator.Play(clip.name, 0, 0f);
        }

        private void PlayLocomotion(bool moving)
        {
            if (currentPatternState == PatternState.Executing) return;
            if (!animator || !animator.runtimeAnimatorController) return;
            animator.SetFloat("MoveSpeed", moving && unit.Data ? unit.Data.MoveSpeed : 0f);
            // A completed volley need not interrupt a motion until the next action starts.
            if (!moving && Time.time < motionUntil) return;
            string state = moving ? "Move" : "Idle";
            if (!animator.GetCurrentAnimatorStateInfo(0).IsName(state)) animator.Play(state, 0, 0f);
        }

        private IEnumerator ExecutePattern(EnemyPatternData pattern, IReadOnlyList<Transform> targets,
            bool canForwardTracking, PatternExecution execution)
        {
            StopMeleeHitWindow();
            StopForwardTracking();
            if (!pattern || !unit.Data || health.IsDead || !isActiveAndEnabled) yield break;

            StageController stage = Stage;
            StageField field = stage ? stage.GetActiveField(unit) : null;
            bool CanStart() => !health.IsDead && isActiveAndEnabled
                && (ReferenceEquals(stage, null) || (stage && field && stage.GetActiveField(unit) == field));
            if (!CanStart()) yield break;

            AreaPlacement[] capturedPlacements = null;
            if (pattern.PlacementMode != PatternPlacementMode.Default)
            {
                if (pattern.PlacementMode == PatternPlacementMode.CaptureTargets)
                {
                    Quaternion facing = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
                    execution.CapturedPlacements = targets.Where(IsCurrentPlayer)
                        .Select(target => new AreaPlacement(target.position, facing)).ToArray();
                }

                capturedPlacements = execution.CapturedPlacements;
                if (capturedPlacements == null || capturedPlacements.Length == 0)
                {
                    currentPatternState = PatternState.Done;
                    yield break;
                }
            }

            currentPatternState = PatternState.Executing;
            AreaPlacement? telegraphCaster = null;
            AreaPlacement? telegraphField = null;
            if (pattern.TelegraphPrefab && pattern.TelegraphDurationSeconds > 0f
                && float.IsFinite(pattern.TelegraphDurationSeconds))
            {
                if (pattern.TelegraphPrefab.GetComponentInChildren<VfxAutoDespawn>(true))
                {
                    Debug.LogWarning("Telegraph pool return is owned by EnemyAI. Use a telegraph prefab without VfxAutoDespawn.", pattern);
                    currentPatternState = PatternState.Done;
                    yield break;
                }

                var area = (pattern.Implementations ?? Array.Empty<PatternImplData>())
                    .OfType<AreaAttackPatternData>().FirstOrDefault(effect => effect);
                bool fieldRelative = capturedPlacements == null && area && area.PlacementOrigin == AreaPlacementOrigin.Field;
                if (fieldRelative && !field)
                {
                    Debug.LogWarning("Field-relative telegraph requires an active field.", pattern);
                    currentPatternState = PatternState.Done;
                    yield break;
                }
                if (agent && agent.isActiveAndEnabled && agent.isOnNavMesh) agent.isStopped = true;

                telegraphCaster = new AreaPlacement(transform);
                telegraphField = field ? new AreaPlacement(field.LayoutOrigin) : null;
                if (capturedPlacements == null)
                {
                    SpawnTelegraph(pattern.TelegraphPrefab, area,
                        fieldRelative ? telegraphField.GetValueOrDefault() : telegraphCaster.Value);
                }
                else
                {
                    foreach (var placement in capturedPlacements)
                    {
                        if (!CanStart()) break;
                        SpawnTelegraph(pattern.TelegraphPrefab, area, placement);
                    }
                }
                float endsAt = Time.time + pattern.TelegraphDurationSeconds;
                while (Time.time < endsAt && CanStart()) yield return null;
                ReturnTelegraph();
                if (!CanStart())
                {
                    currentPatternState = PatternState.Done;
                    yield break;
                }
            }

            float startedAt = Time.time;
            PlayMotion(pattern.MotionClip);
            Coroutine castVfx = pattern.CastVfxPrefab
                ? StartCoroutine(PlayCastVfx(pattern, startedAt, GetVfxGuard())) : null;
            if (canForwardTracking) StartForwardTracking(pattern);

            int pendingBulletEffects = 0;
            Action onBulletFinished = () => pendingBulletEffects--;
            List<AreaCast> areaCasts = null;
            bool meleeRegistered = false;
            foreach (PatternImplData effect in pattern.Implementations ?? Array.Empty<PatternImplData>())
            {
                if (health.IsDead || !isActiveAndEnabled) yield break;
                switch (effect)
                {
                    case MeleeAttackPatternData meleeAttack when meleeAttack:
                        if (meleeRegistered)
                        {
                            Debug.LogWarning("A pattern can only have one melee effect.", pattern);
                            break;
                        }

                        meleeRegistered = true;
                        StartMeleeHitWindow(meleeAttack);
                        break;

                    case AreaAttackPatternData areaAttack when areaAttack:
                        float placementDelay = areaAttack.PlacementDelaySeconds;
                        float hitDelay = areaAttack.HitStartSeconds;
                        string error = areaAttack.GetValidationError();
                        if (error != null)
                        {
                            Debug.LogWarning(error, areaAttack);
                            break;
                        }

                        (areaCasts ??= new List<AreaCast>()).Add(new AreaCast
                        {
                            Pattern = areaAttack,
                            NextAt = startedAt + placementDelay,
                            HitAt = startedAt + hitDelay,
                            Damage = new AttackDamage(unit.Data.Attack, areaAttack.DamageMultiplier),
                            CapturedPlacements = capturedPlacements,
                        });
                        break;

                    case BulletPatternData bulletAttack when bulletAttack:
                        if (!bulletAttack.BulletPrefab)
                        {
                            Debug.LogWarning("Bullet effect requires a bullet prefab.", bulletAttack);
                            break;
                        }

                        int volleys = capturedPlacements?.Length ??
                            (bulletAttack.AimAtTargetEachEmission ? Mathf.Max(1, targets.Count) : 1);
                        for (int i = 0; i < volleys; i++)
                        {
                            pendingBulletEffects++;
                            Transform target = i < targets.Count ? targets[i] : null;
                            StartCoroutine(ExecuteBulletEffect(bulletAttack, target, onBulletFinished,
                                capturedPlacements?[i], capturedPlacements != null ? CanStart : null));
                        }
                        break;

                    case SummonPatternData summonAttack when summonAttack:
                        Summon(summonAttack, targets);
                        break;
                }
            }

            if (areaCasts != null)
                StartCoroutine(ExecuteAreaEffects(areaCasts, telegraphCaster, telegraphField));

            bool WaitForAreas() => areaCasts != null && (pattern.SelfDestructAfterPattern
                ? areaCasts.Count > 0
                : areaCasts.Exists(cast => !cast.Placed || !cast.Pattern.ContinueAfterPlacement));

            while (pendingBulletEffects > 0 || WaitForAreas() || coMeleeHitWindow != null || Time.time < motionUntil)
                yield return null;
            StopForwardTracking();

            if (pattern.RestTime > 0f)
                yield return new WaitForSeconds(pattern.RestTime);

            if (castVfx != null) StopCoroutine(castVfx);

            if(pattern.SelfDestructAfterPattern)
            {
                health.Kill();
                yield break;
            }

            currentPatternState = PatternState.Done;
        }

        private void Summon(SummonPatternData pattern, IReadOnlyList<Transform> targets, Vector3? deathOrigin = null)
        {
            if (!Stage)
            {
                Debug.LogWarning("Summoning requires a StageController.", pattern);
                return;
            }

            if (pattern.SummonPositionType != SummonPositionType.Target)
            {
                Stage.SummonEnemies(unit, null, pattern, deathOrigin);
                return;
            }

            foreach (var target in targets) Stage.SummonEnemies(unit, target, pattern, deathOrigin);
        }

        private void SpawnTelegraph(GameObject prefab, AreaAttackPatternData area, AreaPlacement placement)
        {
            Vector3 position = placement.Layout.MultiplyPoint3x4(area ? area.LocalCenter : Vector3.zero);
            Quaternion facing = placement.Facing * Quaternion.Euler(0f, area ? area.LocalYaw : 0f, 0f);
            activeTelegraphs.Add(NetworkWorld.Spawn(prefab, position, facing));
        }

        private void ReturnTelegraph()
        {
            while (activeTelegraphs.Count > 0)
            {
                int index = activeTelegraphs.Count - 1;
                var instance = activeTelegraphs[index];
                activeTelegraphs.RemoveAt(index);
                if (instance) NetworkWorld.Despawn(instance);
            }
        }

        private Func<bool> GetVfxGuard(bool afterDeath = false) =>
            Stage ? Stage.GetVfxGuard(Stage.GetActiveField(unit, afterDeath)) : null;

        private IEnumerator PlayCastVfx(EnemyPatternData pattern, float startedAt, Func<bool> canPlay)
        {
            if (!float.IsFinite(pattern.CastVfxDelaySeconds)) yield break;
            float playAt = startedAt + Mathf.Max(0f, pattern.CastVfxDelaySeconds);
            while (Time.time < playAt) yield return null;
            if (!health.IsDead && isActiveAndEnabled)
                VfxAutoDespawn.Play(pattern.CastVfxPrefab, transform.position, transform.rotation, canPlay: canPlay);
        }

        private IEnumerator ExecuteAreaEffects(List<AreaCast> casts, AreaPlacement? telegraphCaster,
            AreaPlacement? telegraphField)
        {
            StageController stage = Stage;
            StageField field = stage ? stage.GetActiveField(unit) : null;
            bool stageBound = !ReferenceEquals(stage, null);
            var vfxGuard = stage ? stage.GetVfxGuard(field) : null;
            bool CanRun() => !health.IsDead && isActiveAndEnabled
                && (!stageBound || (stage && field && stage.GetActiveField(unit) == field));

            try
            {
                LayerMask targets = LayerMask.GetMask("Player");
                while (casts.Count > 0 && CanRun())
                {
                    // Scan the scheduled times so a late frame still fires distinct hits in order.
                    int next = 0;
                    for (int i = 1; i < casts.Count; i++)
                        if (casts[i].NextAt < casts[next].NextAt) next = i;

                    AreaCast cast = casts[next];
                    if (Time.time < cast.NextAt)
                    {
                        yield return null;
                        continue;
                    }

                    if (!cast.Placed)
                    {
                        if (cast.CapturedPlacements == null && cast.Pattern.PlacementOrigin == AreaPlacementOrigin.Field && !field)
                        {
                            Debug.LogWarning("Field-relative area attack requires an active field.", cast.Pattern);
                            casts.RemoveAt(next);
                            continue;
                        }
                        if (cast.CapturedPlacements == null)
                        {
                            cast.Placement = cast.Pattern.PlacementOrigin == AreaPlacementOrigin.Field
                                ? telegraphField ?? new AreaPlacement(field.LayoutOrigin)
                                : telegraphCaster ?? new AreaPlacement(transform);
                        }
                        cast.Placed = true;
                        cast.NextAt = cast.HitAt;
                        casts[next] = cast;
                        continue;
                    }

                    var pattern = cast.Pattern;
                    float now = Time.time;
                    float endsAt = cast.HitAt + pattern.HitDurationSeconds;
                    float elapsed = now - cast.HitAt;
                    int slice = HitWindow.Slice(elapsed, pattern.HitDurationSeconds,
                        pattern.RepeatHits, pattern.HitRepeatIntervalSeconds);
                    if (now >= endsAt || slice < 0)
                    {
                        casts.RemoveAt(next);
                        continue;
                    }
                    cast.Window ??= new HitWindow();
                    var (size, innerRadius) = pattern.EvaluateHitShape(elapsed);
                    // Cover the band crossed since the previous tick, even when it moved farther than its width.
                    if (cast.HasHit && pattern.ExpansionMode == AreaExpansionMode.MovingRing)
                        innerRadius = pattern.EvaluateHitShape(cast.LastSample).InnerRadius;

                    if (cast.CapturedPlacements == null)
                    {
                        ApplyAreaTick(cast, cast.Placement, elapsed, size, innerRadius, targets, CanRun, vfxGuard, slice);
                    }
                    else
                    {
                        foreach (var placement in cast.CapturedPlacements)
                        {
                            if (!CanRun()) yield break;
                            ApplyAreaTick(cast, placement, elapsed, size, innerRadius, targets, CanRun, vfxGuard, slice);
                        }
                    }
                    if (!CanRun()) yield break;

                    cast.HasHit = true;
                    cast.LastSample = elapsed;
                    cast.NextAt = Mathf.Min(now + Time.fixedDeltaTime, endsAt);
                    casts[next] = cast;
                }
            }
            finally
            {
                casts.Clear();
            }
        }

        private void ApplyAreaTick(AreaCast cast, AreaPlacement placement, float sample, Vector3 size,
            float innerRadius, LayerMask targets, Func<bool> canRun, Func<bool> vfxGuard, int slice)
        {
            var pattern = cast.Pattern;
            Vector3 center = placement.Layout.MultiplyPoint3x4(pattern.LocalCenter);
            Quaternion facing = placement.Facing * Quaternion.Euler(0f, pattern.LocalYaw, 0f);
            if (!cast.HasHit) VfxAutoDespawn.Play(pattern.AttackVfxPrefab, center, facing, canPlay: vfxGuard);

            var hits = areaQuery.Find(pattern.Shape, center, facing, size,
                pattern.SectorAngle, unit, targets, innerRadius);
            foreach (AreaAttackShapeData shape in pattern.AdditionalShapes ?? Array.Empty<AreaAttackShapeData>())
            {
                if (shape == null) continue;
                var (additionalSize, additionalInner) = pattern.EvaluateHitShape(sample, shape);
                areaQuery.Append(shape.Shape, placement.Layout.MultiplyPoint3x4(shape.LocalCenter),
                    placement.Facing * Quaternion.Euler(0f, shape.LocalYaw, 0f), additionalSize,
                    shape.SectorAngle, unit, targets, additionalInner);
            }

            foreach (var (target, collider) in hits)
            {
                if (!canRun()) return;
                if (!cast.Window.Claim(HurtboxTarget.Key(target, collider), slice)) continue;
                DamageWithVfx(target, collider, center, facing, cast.Damage,
                    pattern.HitVfxPrefab, pattern.HitVfxDelaySeconds, vfxGuard);
            }
        }

        private IEnumerator ExecuteBulletEffect(BulletPatternData bulletAttack, Transform target, Action onFinished,
            AreaPlacement? capturedPlacement, Func<bool> canFire)
        {
            try
            {
                if (bulletAttack.StartTime > 0f)
                    yield return new WaitForSeconds(bulletAttack.StartTime);

                float startedAt = Time.time;
                Vector3 origin = capturedPlacement is AreaPlacement placement
                    ? placement.Layout.MultiplyPoint3x4(Vector3.zero) : transform.position;
                Quaternion facing = capturedPlacement?.Facing ?? Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
                var vfxGuard = GetVfxGuard();
                for (int emission = 0; emission < bulletAttack.RepeatCount; emission++)
                {
                    while (Time.time - startedAt < emission * bulletAttack.RepeatInterval) yield return null;
                    if (health.IsDead || canFire?.Invoke() == false) yield break;
                    if (capturedPlacement == null && bulletAttack.AimAtTargetEachEmission)
                    {
                        if (!IsCurrentPlayer(target)) break;
                        origin = transform.position;
                        Vector3 aim = target.position - origin;
                        aim.y = 0f;
                        if (aim.sqrMagnitude > 0.0001f) facing = Quaternion.LookRotation(aim);
                    }
                    bool launched = false;
                    for (int path = 0; path < bulletAttack.MaximumCount; path++)
                    {
                        var projectile = ProjectileHitBox.Spawn(bulletAttack, path, emission, origin, facing);
                        if (!projectile) continue;
                        projectiles.Add(projectile);
                        projectile.Init(unit, bulletAttack, path, emission, origin, facing, OnProjectileReturned, vfxGuard);
                        launched = true;
                    }
                    if (launched) VfxAutoDespawn.Play(bulletAttack.LaunchVfxPrefab, origin, facing, canPlay: vfxGuard);
                }
                // Projectile travel remains independent of pattern completion.
            }
            finally
            {
                onFinished();
            }
        }

        private void StartMeleeHitWindow(MeleeAttackPatternData pattern, float? diedAt = null)
        {
            StopMeleeHitWindow();
            float startedAt = diedAt ?? Time.time;
            if (!pattern || !unit.Data || !(pattern.HitStartSeconds >= 0f) || !(pattern.HitDurationSeconds > 0f)
                || !float.IsFinite(pattern.HitStartSeconds + pattern.HitDurationSeconds)
                || !(pattern.HitStartSeconds + pattern.HitDurationSeconds > Time.time - startedAt)
                || (pattern.RepeatHits && (!(pattern.HitRepeatIntervalSeconds > 0f)
                    || !float.IsFinite(pattern.HitRepeatIntervalSeconds)))) return;
            activeMelee = pattern;
            meleeAfterDeath = diedAt.HasValue;
            meleeStartedAt = startedAt;
            meleeDamage = new AttackDamage(unit.Data.Attack, pattern.DamageMultiplier);
            coMeleeHitWindow = StartCoroutine(IEMeleeHitWindow(pattern));
        }

        private IEnumerator IEMeleeHitWindow(MeleeAttackPatternData pattern)
        {
            float end = pattern.HitStartSeconds + pattern.HitDurationSeconds;
            bool vfxPlayed = false;
            while (Time.time - meleeStartedAt < end)
            {
                float elapsed = Time.time - meleeStartedAt;
                bool open = elapsed >= pattern.HitStartSeconds && elapsed < end;
                SetMeleeArmed(open);
                if (open && !vfxPlayed)
                {
                    vfxPlayed = true;
                    if (pattern.AttackVfxPrefab)
                        VfxAutoDespawn.Play(pattern.AttackVfxPrefab, transform.position, transform.rotation,
                            canPlay: GetVfxGuard(meleeAfterDeath));
                }
                yield return null;
            }
            ClearMeleeHitWindow();
        }

        private void StopMeleeHitWindow()
        {
            if (coMeleeHitWindow != null) StopCoroutine(coMeleeHitWindow);
            ClearMeleeHitWindow();
        }

        private void ClearMeleeHitWindow()
        {
            coMeleeHitWindow = null;
            SetMeleeArmed(false);
            activeMelee = null;
            meleeAfterDeath = false;
            meleeHits.Reset();
        }

        private void SetMeleeArmed(bool armed)
        {
            if (meleeHitBoxes == null) return;
            foreach (var hitBox in meleeHitBoxes) if (hitBox) hitBox.SetArmed(armed);
        }

        private void OnMeleeContact(IDamageable target, Collider collider)
        {
            if (!activeMelee || health.IsDead != meleeAfterDeath) return;
            if (meleeAfterDeath && (!Stage || !Stage.CanRunDeathPattern(unit))) return;
            float elapsed = Time.time - meleeStartedAt - activeMelee.HitStartSeconds;
            int slice = HitWindow.Slice(elapsed, activeMelee.HitDurationSeconds,
                activeMelee.RepeatHits, activeMelee.HitRepeatIntervalSeconds);
            if (!meleeHits.Claim(HurtboxTarget.Key(target, collider), slice)) return;
            DamageWithVfx(target, collider, transform.position, transform.rotation, meleeDamage,
                activeMelee.HitVfxPrefab, activeMelee.HitVfxDelaySeconds,
                activeMelee.HitVfxPrefab ? GetVfxGuard(meleeAfterDeath) : null);
        }

        private void DamageWithVfx(IDamageable target, Collider collider, Vector3 origin, Quaternion facing,
            AttackDamage damage, VfxAutoDespawn prefab, float delay, Func<bool> canPlay)
        {
            Vector3 point = prefab && collider ? collider.ClosestPoint(origin) : origin;
            var result = HurtboxTarget.TakeDamage(target, collider, damage, unit);
            if (result is DamageResult.Applied or DamageResult.Killed)
                VfxAutoDespawn.Play(prefab, point, facing, delay, canPlay);
        }

        private void OnProjectileReturned(ProjectileHitBox projectile) => projectiles.Remove(projectile);

        private int PlayDeathMotion(AnimationClip clip)
        {
            if (!animator || !animator.isActiveAndEnabled || !animator.runtimeAnimatorController
                || animator.layerCount == 0) return 0;

            string layer = animator.GetLayerName(0);
            int stateId = Animator.StringToHash(layer + (clip ? "." + clip.name : ".Died"));
            if (!animator.HasState(0, stateId)) stateId = Animator.StringToHash(layer + ".Died");
            if (!animator.HasState(0, stateId)) return 0;
            animator.Play(stateId, 0, 0f);
            return stateId;
        }

        private void RunDeathEffects(EnemyPatternData pattern, float diedAt, Vector3 origin, Quaternion facing, float attack)
        {
            if (pattern.CastVfxPrefab)
                VfxAutoDespawn.Play(pattern.CastVfxPrefab, origin, facing,
                    Mathf.Max(0f, diedAt + pattern.CastVfxDelaySeconds - Time.time), GetVfxGuard(afterDeath: true));
            List<Transform> summonTargets = null;
            bool meleeRegistered = false;
            foreach (PatternImplData effect in pattern.Implementations ?? Array.Empty<PatternImplData>())
            {
                if (!Stage || !Stage.CanRunDeathPattern(unit)) return;
                switch (effect)
                {
                    case MeleeAttackPatternData melee when melee:
                        if (meleeRegistered)
                        {
                            Debug.LogWarning("A pattern can only have one melee effect.", pattern);
                            break;
                        }
                        meleeRegistered = true;
                        StartMeleeHitWindow(melee, diedAt);
                        break;

                    case BulletPatternData bullet when bullet:
                        Stage.StartDeathBullets(unit, bullet, diedAt, origin, facing, attack);
                        break;

                    case SummonPatternData summon when summon:
                        if (summonTargets == null)
                        {
                            summonTargets = new List<Transform>();
                            if (pattern.TargetMode != ActionTargetMode.None)
                            {
                                EnemyTargetSelector.Select(Owner, Owner.Players,
                                    pattern.TargetMode == ActionTargetMode.Select ? pattern.TargetRule : chaseTargetRule, summonTargets);
                                if (pattern.TargetMode == ActionTargetMode.Inherit && summonTargets.Count > 1)
                                    summonTargets.RemoveRange(1, summonTargets.Count - 1);
                            }
                        }
                        Summon(summon, summonTargets, origin);
                        break;
                }
            }
        }

        private IEnumerator IEDied(EnemyPatternData pattern)
        {
            float diedAt = Time.time;
            Vector3 origin = transform.position;
            Quaternion facing = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            float attack = unit.Data ? unit.Data.Attack : 0f;
            int stateId = PlayDeathMotion(pattern ? pattern.MotionClip : null);

            // Resolve the field's kill count before running any death effects.
            yield return null;
            float motionEndsAt = Time.time;
            if (stateId != 0 && animator && animator.isActiveAndEnabled)
            {
                var state = animator.GetCurrentAnimatorStateInfo(0);
                float speed = animator.speed * state.speed * state.speedMultiplier;
                if (state.fullPathHash == stateId && speed > 0f && state.normalizedTime < 1f)
                {
                    float remaining = state.length * (1f - state.normalizedTime) / speed;
                    if (remaining > 0f && !float.IsInfinity(remaining)) motionEndsAt += remaining;
                }
            }

            if (pattern && Stage && Stage.CanRunDeathPattern(unit))
                RunDeathEffects(pattern, diedAt, origin, facing, attack);

            while (coMeleeHitWindow != null)
            {
                if (!Stage || !Stage.CanRunDeathPattern(unit))
                {
                    StopMeleeHitWindow();
                    break;
                }
                yield return null;
            }

            float motionRemaining = motionEndsAt - Time.time;
            if (motionRemaining > 0f) yield return new WaitForSeconds(motionRemaining);

            try
            {
                if (Owner.Rank == EnemyRank.Boss) GameEvents.RaiseBossDefeated(health);
            }
            finally
            {
                DeathPresentationFinished?.Invoke();
            }
        }
    }
}
