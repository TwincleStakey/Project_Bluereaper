using BlueReaper.BulletPatterns;
using BlueReaper.Core;
using BlueReaper.Data;
using BlueReaper.HitBox;
using BlueReaper.Stage;
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

        private PatternState currentPatternState;
        private Unit unit;
        private Enemy Owner;
        private Health health;
        private Posture posture;
        private NavMeshAgent agent;
        private Animator animator;
        private Coroutine coAIUpdate = null;
        private Coroutine coMeleeHitWindow = null;
        private MeleeHitBox[] meleeHitBoxes;
        private readonly Dictionary<IDamageable, float> nextMeleeHits = new();
        private readonly HashSet<ProjectileHitBox> projectiles = new();
        private MeleeAttackPatternData activeMelee;
        private bool meleeAfterDeath;
        private float meleeStartedAt, meleeDamage, motionUntil;
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

            while (!health.IsDead)
            {
                if (posture && posture.IsGroggy)
                {
                    yield return null;
                    continue;
                }

                Transform target = null;
                foreach (var player in Owner.Players)
                    if (player && player.gameObject.activeInHierarchy) { target = player; break; }

                if(target && (target.position - transform.position).sqrMagnitude > Owner.SightRange * Owner.SightRange)
                {
                    agent.speed = unit.Data.MoveSpeed * Owner.OutSightTrackingPercent;
                    agent.angularSpeed = 360f * Owner.OutSightTrackingPercent;
                }
                else
                {
                    agent.speed = unit.Data.MoveSpeed;
                    agent.angularSpeed = 360f;
                }

                if (spawnPatternPending && spawnPattern)
                {
                    spawnPatternPending = false;
                    Vector3 directionToTarget = target ? target.position - transform.position : transform.forward;
                    directionToTarget.y = 0f;
                    trackingDirection = directionToTarget.normalized;
                    if (trackingDirection != Vector3.zero)
                        transform.rotation = Quaternion.LookRotation(trackingDirection);

                    yield return ExecuteAction(spawnPattern, target);
                    trackingDirection = Vector3.zero;
                    PlayLocomotion(false);
                    yield return wait;
                    continue;
                }

                if (target && !selectedAction) selectedAction = SelectNextPattern();

                EnemyPatternData firstPattern = selectedAction switch
                {
                    EnemyPatternData pattern => pattern,
                    EnemyPatternSequenceData { Steps: { Length: > 0 } steps } => steps[0],
                    _ => null
                };
                if (!target || !firstPattern)
                {
                    if (!firstPattern) selectedAction = null;
                    if (agent.isActiveAndEnabled && agent.isOnNavMesh) agent.isStopped = true;
                    PlayLocomotion(false);
                    yield return wait;
                    continue;
                }

                Vector3 direction = target.position - transform.position;
                direction.y = 0f;
                Vector3 destination = target.position - direction.normalized * firstPattern.ChasingDistance;
                if (!MoveTo(destination) || !IsCompleteDestination())
                {
                    PlayLocomotion(agent.isActiveAndEnabled && agent.isOnNavMesh && agent.hasPath);
                    yield return wait;
                    continue;
                }

                agent.isStopped = true;
                trackingDirection = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.zero;
                if (trackingDirection != Vector3.zero)
                    transform.rotation = Quaternion.LookRotation(trackingDirection);

                yield return ExecuteAction(selectedAction, target);
                trackingDirection = Vector3.zero;
                selectedAction = null;

                PlayLocomotion(false);
                yield return wait;
            }
            coAIUpdate = null;
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

        private EnemyActionData SelectNextPattern()
        {
            Owner.CalcPhase();
            if (patterns == null) return null;

            var candidates = patterns.Where(action => action && action.RequirePhase != null
                && action.RequirePhase.Contains(Owner.Phase)
                && (action is EnemyPatternData
                    || (action is EnemyPatternSequenceData { Steps: { Length: > 0 } steps }
                        && steps.All(step => step)))).ToArray();
            return candidates.Length > 0 ? candidates[UnityEngine.Random.Range(0, candidates.Length)] : null;
        }

        private IEnumerator ExecuteAction(EnemyActionData action, Transform target)
        {
            if (action is EnemyPatternData pattern)
            {
                yield return ExecutePattern(pattern, target, action.CanForwardTracking);
                yield break;
            }

            if (action is EnemyPatternSequenceData { Steps: { Length: > 0 } steps })
            {
                foreach (EnemyPatternData step in steps)
                {
                    if (health.IsDead || !step) yield break;

                    yield return ExecutePattern(step, target, action.CanForwardTracking);
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

        private IEnumerator ExecutePattern(EnemyPatternData pattern, Transform target, bool canForwardTracking)
        {
            StopMeleeHitWindow();
            StopForwardTracking();
            if (!pattern || !unit.Data || health.IsDead) yield break;
            currentPatternState = PatternState.Executing;
            PlayMotion(pattern.MotionClip);
            if (canForwardTracking) StartForwardTracking(pattern);

            int pendingBulletEffects = 0;
            Action onBulletFinished = () => pendingBulletEffects--;
            bool meleeRegistered = false;
            foreach (PatternImplData effect in pattern.Implementions ?? Array.Empty<PatternImplData>())
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

                    case BulletPatternData bulletAttack when bulletAttack:
                        if (!bulletAttack.BulletPrefab)
                        {
                            Debug.LogWarning("Bullet effect requires a bullet prefab.", bulletAttack);
                            break;
                        }

                        pendingBulletEffects++;
                        StartCoroutine(ExecuteBulletEffect(bulletAttack, target, onBulletFinished));
                        break;

                    case SummonPatternData summonAttack when summonAttack:
                        if (Stage) Stage.SummonEnemies(unit, target, summonAttack);
                        else Debug.LogWarning("Summoning requires a StageController.", summonAttack);
                        break;
                }
            }

            while (pendingBulletEffects > 0 || coMeleeHitWindow != null || Time.time < motionUntil)
                yield return null;
            StopForwardTracking();

            if (pattern.RestTime > 0f)
                yield return new WaitForSeconds(pattern.RestTime);

            if(pattern.SelfDestructAfterPattern)
            {
                health.Kill();
                yield break;
            }

            currentPatternState = PatternState.Done;
        }

        private IEnumerator ExecuteBulletEffect(BulletPatternData bulletAttack, Transform target, Action onFinished)
        {
            try
            {
                if (bulletAttack.StartTime > 0f)
                    yield return new WaitForSeconds(bulletAttack.StartTime);

                float startedAt = Time.time;
                Vector3 origin = transform.position;
                Quaternion facing = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
                for (int emission = 0; emission < bulletAttack.RepeatCount; emission++)
                {
                    while (Time.time - startedAt < emission * bulletAttack.RepeatInterval) yield return null;
                    if (health.IsDead) yield break;
                    if (bulletAttack.AimAtTargetEachEmission)
                    {
                        if (!target || !target.gameObject.activeInHierarchy || !Owner.Players.Contains(target)) break;
                        origin = transform.position;
                        Vector3 aim = target.position - origin;
                        aim.y = 0f;
                        if (aim.sqrMagnitude > 0.0001f) facing = Quaternion.LookRotation(aim);
                    }
                    for (int path = 0; path < bulletAttack.MaximumCount; path++)
                    {
                        var projectile = ProjectileHitBox.Spawn(bulletAttack, path, emission, origin, facing);
                        if (!projectile) continue;
                        projectiles.Add(projectile);
                        projectile.Init(unit, bulletAttack, path, emission, origin, facing, OnProjectileReturned);
                    }
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
            if (!pattern || !unit.Data || !(pattern.HitDurationSeconds > 0f)
                || !(pattern.HitStartSeconds + pattern.HitDurationSeconds > Time.time - startedAt)) return;
            activeMelee = pattern;
            meleeAfterDeath = diedAt.HasValue;
            meleeStartedAt = startedAt;
            meleeDamage = DamageFormula.Calculate(unit.Data.Attack, pattern.DamageMultiplier);
            coMeleeHitWindow = StartCoroutine(IEMeleeHitWindow(pattern));
        }

        private IEnumerator IEMeleeHitWindow(MeleeAttackPatternData pattern)
        {
            float end = pattern.HitStartSeconds + pattern.HitDurationSeconds;
            while (Time.time - meleeStartedAt < end)
            {
                float elapsed = Time.time - meleeStartedAt;
                SetMeleeArmed(elapsed >= pattern.HitStartSeconds && elapsed < end);
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
            nextMeleeHits.Clear();
        }

        private void SetMeleeArmed(bool armed)
        {
            if (meleeHitBoxes == null) return;
            foreach (var hitBox in meleeHitBoxes) if (hitBox) hitBox.SetArmed(armed);
        }

        private void OnMeleeContact(IDamageable target)
        {
            if (!activeMelee || health.IsDead != meleeAfterDeath) return;
            if (meleeAfterDeath && (!Stage || !Stage.CanRunDeathPattern(unit))) return;
            float elapsed = Time.time - meleeStartedAt;
            if (elapsed < activeMelee.HitStartSeconds
                || elapsed >= activeMelee.HitStartSeconds + activeMelee.HitDurationSeconds) return;
            if (nextMeleeHits.TryGetValue(target, out float next) && Time.time < next) return;
            nextMeleeHits[target] = activeMelee.RepeatHits
                ? Time.time + Mathf.Max(Time.fixedDeltaTime, activeMelee.HitRepeatIntervalSeconds)
                : float.PositiveInfinity;
            target.TakeDamage(meleeDamage);
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
            bool meleeRegistered = false;
            foreach (PatternImplData effect in pattern.Implementions ?? Array.Empty<PatternImplData>())
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
                        Transform target = Owner.Players.FirstOrDefault(player => player && player.gameObject.activeInHierarchy);
                        Stage.SummonEnemies(unit, target, summon, deathOrigin: origin);
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
