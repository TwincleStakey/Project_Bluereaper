using System;
using System.Collections;
using System.Collections.Generic;
using BlueReaper.BulletPatterns;
using BlueReaper.Core;
using BlueReaper.Data;
using UnityEngine;
using UnityEngine.AI;

namespace BlueReaper.Boss
{
    enum PatternState { None, Start, Executing, Done }

    [RequireComponent(typeof(Unit), typeof(Enemy), typeof(NavMeshAgent))]
    public class EnemyAI : MonoBehaviour
    {
        [SerializeField] EnemyPattern[] patterns;
        [SerializeField] float aiTick = 0.1f;
        [SerializeField] float stopChasingDistance = 3f;

        private int currentPatternIndex;
        private PatternState currentPatternState;
        private Unit unit;
        private Enemy Owner;
        private Health health;
        private NavMeshAgent agent;
        private Animator animator;
        private Coroutine coAIUpdate = null;
        private Coroutine coMeleeHitWindow = null;
        private MeleeHitBox[] meleeHitBoxes;
        private readonly Dictionary<IDamageable, float> nextMeleeHits = new();
        private readonly HashSet<ProjectileHitBox> projectiles = new();
        private MeleeAttackPattern activeMelee;
        private float meleeStartedAt, meleeDamage, motionUntil;
        private Vector3 chaseDestination;
        private bool hasChaseDestination;

        private void Awake()
        {
            unit = GetComponent<Unit>();
            Owner = GetComponent<Enemy>();
            health = GetComponent<Health>();
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
            health.Died += Release;
            currentPatternState = PatternState.None;
            hasChaseDestination = false;
            if (coAIUpdate == null) coAIUpdate = StartCoroutine(AIUpdate());
        }

        private void OnDisable()
        {
            health.Died -= Release;
            Release();
        }

        public void Release()
        {
            StopMeleeHitWindow();
            StopAllCoroutines();
            coAIUpdate = null;
            foreach (var projectile in new List<ProjectileHitBox>(projectiles))
                if (projectile) projectile.ReturnToPool();
            projectiles.Clear();
            hasChaseDestination = false;
            if (agent && agent.isActiveAndEnabled && agent.isOnNavMesh) agent.isStopped = true;
            currentPatternState = PatternState.None;

            //TODO : 우선 보상 테이블은 enemy에 추가, 획득 처리 로직은 ai에서 담당하는데 실제 구현 단계에서는 즉시 처리는 enemy에서, 지연 처리는 stage에서 최종적으로 lazy하게
            if (Owner && Owner.RewardTable)
            {
                (uint id, uint count) reward = Owner.RewardTable.GetReward();
                Debug.Log($"Enemy {Owner.name} died. Reward: {reward.count} of item ID {reward.id}");
            }
        }

        private IEnumerator AIUpdate()
        {
            // ObjectPool activates the boss before StageController injects its players.
            yield return null;
            var wait = new WaitForSeconds(Mathf.Max(0.01f, aiTick));
            while (!health.IsDead)
            {
                Transform target = null;
                foreach (var player in Owner.Players)
                    if (player && player.gameObject.activeInHierarchy) { target = player; break; }

                if (!target || (patterns == null || patterns.Length == 0))
                {
                    if (agent.isActiveAndEnabled && agent.isOnNavMesh) agent.isStopped = true;
                    PlayLocomotion(false);
                    yield return wait;
                    continue;
                }

                Vector3 direction = target.position - transform.position;
                direction.y = 0f;
                currentPatternIndex %= patterns.Length;
                Vector3 destination = target.position - direction.normalized * patterns[currentPatternIndex].ChasingDistance;
                if (!MoveTo(destination) || !IsCompleteDestination())
                {
                    PlayLocomotion(agent.isActiveAndEnabled && agent.isOnNavMesh && agent.hasPath);
                    yield return wait;
                    continue;
                }

                agent.isStopped = true;
                if (direction.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(direction);

                yield return ExecutePattern(patterns[currentPatternIndex]);
                currentPatternIndex = (currentPatternIndex + 1) % patterns.Length;

                PlayLocomotion(false);
                yield return wait;
            }
            coAIUpdate = null;
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

        private IEnumerator ExecutePattern(EnemyPattern pattern)
        {
            StopMeleeHitWindow();
            if (!pattern || !unit.Data || health.IsDead) yield break;
            currentPatternState = PatternState.Executing;
            PlayMotion(pattern.MotionClip);
            StartMeleeHitWindow(pattern.MeleeAttack);

            if (pattern.BulletAttack != null)
            {
                if (pattern.BulletAttack.BulletPrefab == null)
                {
                    StopMeleeHitWindow();
                    currentPatternState = PatternState.Done;
                    yield break;
                }

                if (pattern.BulletAttack.StartTime > 0f)
                    yield return new WaitForSeconds(pattern.BulletAttack.StartTime);

                float startedAt = Time.time;
                Vector3 origin = transform.position;
                Quaternion facing = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
                for (int emission = 0; emission < pattern.BulletAttack.RepeatCount; emission++)
                {
                    while (Time.time - startedAt < emission * pattern.BulletAttack.RepeatInterval) yield return null;
                    if (health.IsDead) yield break;
                    for (int path = 0; path < pattern.BulletAttack.MaximumCount; path++)
                    {
                        if (!pattern.BulletAttack.TryEvaluate(path, emission, emission * pattern.BulletAttack.RepeatInterval,
                            out var position, out var rotation)) continue;
                        var instance = ObjectPool.Spawn(pattern.BulletAttack.BulletPrefab, origin + facing * position, facing * rotation);
                        var projectile = instance.GetComponent<ProjectileHitBox>();
                        if (!projectile)
                        {
                            ObjectPool.Despawn(instance);
                            Debug.LogWarning("Bullet prefab requires ProjectileHitBox.", pattern);
                            continue;
                        }
                        projectiles.Add(projectile);
                        projectile.Init(unit, pattern.BulletAttack, path, emission, origin, facing, OnProjectileReturned);
                    }
                }
                // A volley does not wait for either projectile travel or the parallel melee window.
            }
            else
            {
                // Melee-only patterns retain their action time before advancing to the next pattern.
                while (coMeleeHitWindow != null || Time.time < motionUntil) yield return null;
            }

            if (pattern.RestTime > 0f)
                yield return new WaitForSeconds(pattern.RestTime);

            currentPatternState = PatternState.Done;
        }

        private void StartMeleeHitWindow(MeleeAttackPattern pattern)
        {
            StopMeleeHitWindow();
            if (!pattern || !unit.Data || !(pattern.HitDurationSeconds > 0f)
                || !(pattern.HitStartSeconds + pattern.HitDurationSeconds > 0f)) return;
            activeMelee = pattern;
            meleeStartedAt = Time.time;
            meleeDamage = DamageFormula.Calculate(unit.Data.Attack, pattern.DamageMultiplier);
            coMeleeHitWindow = StartCoroutine(IEMeleeHitWindow(pattern));
        }

        private IEnumerator IEMeleeHitWindow(MeleeAttackPattern pattern)
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
            nextMeleeHits.Clear();
        }

        private void SetMeleeArmed(bool armed)
        {
            if (meleeHitBoxes == null) return;
            foreach (var hitBox in meleeHitBoxes) if (hitBox) hitBox.SetArmed(armed);
        }

        private void OnMeleeContact(IDamageable target)
        {
            if (!activeMelee || health.IsDead) return;
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
    }
}
