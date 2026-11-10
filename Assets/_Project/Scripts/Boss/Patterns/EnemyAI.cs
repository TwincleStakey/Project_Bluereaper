using BlueReaper.BulletPatterns;
using BlueReaper.Core;
using BlueReaper.Data;
using System.Collections;
using UnityEngine;
using UnityEngine.AI;

namespace BlueReaper.Boss
{
    /// <summary>적 ai. 공격 패턴을 관리한다.</summary>
    /// TODO : 프로토 타입 단계에선 등록된 패턴을 순서대로 실행하지만, 이후에는 상황에 따라 패턴을 선택하도록 변경할 것.
    /// 
    enum PatternState
    {
        None = 0,
        Start,
        Executing,
        Done,
    }

    [RequireComponent(typeof(Unit), typeof(Enemy), typeof(NavMeshAgent))]
    public class EnemyAI : MonoBehaviour
    {
        [SerializeField]
        MeleeAttackPattern[] meleePatterns = null;
        [SerializeField] BulletPattern[] bulletPatterns = null;
        [SerializeField] float aiTick = 0.1f;
        [SerializeField] float stopChasingDistance = 3f;

        private int currentPatternType = 0; // 0 : melee, 1 : bullet
        private int currentMeleePatternIndex = 0;
        private int currentBulletPatternIndex = 0;

        private PatternState currentPatternState = PatternState.None;

        private Unit unit = null;
        private Enemy Owner = null;
        private NavMeshAgent agent = null;

        private Animator animator = null;
        private Coroutine coAIUpdate = null;

        public void Release()
        {
            if (coAIUpdate != null)
            {
                StopCoroutine(coAIUpdate);
                coAIUpdate = null;
            }
        }

        private void Awake()
        {
            unit = GetComponent<Unit>();
            Owner = GetComponent<Enemy>();
            agent = GetComponent<NavMeshAgent>();
            animator = GetComponent<Animator>();

            agent.speed = unit.Data.MoveSpeed;
            agent.stoppingDistance = stopChasingDistance;
        }

        private void Start()
        {
            if (coAIUpdate == null)
            {
                coAIUpdate = StartCoroutine(AIUpdate());
            }
        }

        private void OnDisable()
        {
            Release();
        }


        private IEnumerator AIUpdate()
        {
            while (true)
            {
                if (Owner.Players.Count > 0)
                {
                    //todo : target select, pattern select
                    var target = Owner.Players[0];

                    //chasing target

                    if (currentPatternType == 0)
                    {
                        MoveTo(target.position);
                        animator.SetFloat("MoveSpeed", unit.Data.MoveSpeed);
                    }
                    else if (currentPatternType == 1)
                    {
                        Vector3 dir = (target.position - transform.position).normalized;
                        
                        MoveTo(target.position - dir * 4f);
                        animator.SetFloat("MoveSpeed", unit.Data.MoveSpeed);
                    }
                    else
                    {

                    }

                    if(IsCompleteDestination())
                    {
                        agent.isStopped = true;

                        animator.SetFloat("MoveSpeed", 0f);
                        //start selected pattern
                        currentPatternState = PatternState.Start;
                        
                        if(currentPatternState != PatternState.Executing)
                        {
                            if (currentPatternType == 0)
                            {
                                yield return StartCoroutine(ExecutePattern(meleePatterns[currentMeleePatternIndex]));

                                currentMeleePatternIndex = (currentMeleePatternIndex + 1) % meleePatterns.Length;
                            }
                            else if (currentPatternType == 1)
                            {
                                yield return StartCoroutine(ExecutePattern(bulletPatterns[currentBulletPatternIndex]));

                                currentBulletPatternIndex = (currentBulletPatternIndex + 1) % bulletPatterns.Length;
                            }
                            else
                            {

                            }

                            currentPatternType = currentPatternType == 0 ? 1 : 0;
                        }
                    }
                }

                yield return new WaitForSeconds(aiTick);
            }
        }

        private void MoveTo(Vector3 position)
        {
            if (!agent.isActiveAndEnabled || !agent.isOnNavMesh)
            {
                return;
            }

            agent.isStopped = false;

            if(!agent.SetDestination(position))
            {
                agent.ResetPath();
            }
        }

        private bool IsCompleteDestination()
        {
            return !agent.pathPending && agent.pathStatus == NavMeshPathStatus.PathComplete && agent.remainingDistance <= agent.stoppingDistance + 0.05f;
        }

        private IEnumerator ExecutePattern(MeleeAttackPattern pattern)
        {
            if (pattern == null)
            {
                yield break;
            }
            currentPatternState = PatternState.Executing;

            animator.Play(pattern.AttackAnimation.name);

            yield return new WaitForSeconds(pattern.AttackAnimation.length);

            animator.Play("Idle");

            currentPatternState = PatternState.Done;
        }

        private IEnumerator ExecutePattern(BulletPattern pattern)
        {
            if (pattern == null)
            {
                yield break;
            }
            currentPatternState = PatternState.Executing;

            animator.Play(pattern.AttackAnimation.name);
            //todo : execute pattern
            yield return new WaitForSeconds(pattern.AttackAnimation.length);

            animator.Play("Idle");

            currentPatternState = PatternState.Done;
        }
    }
}