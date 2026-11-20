using System;
using System.Collections;
using System.Collections.Generic;
using BlueReaper.Boss;
using BlueReaper.Data;
using BlueReaper.Reward;
using UnityEngine;
using UnityEngine.AI;

namespace BlueReaper.Core
{
    /// <summary>
    /// 모든 적(일반·정예·보스)의 공통 정보: 등급, 스테이지가 넘겨준 플레이어 목록.
    /// 행동은 별도 컴포넌트(몬스터 AI, 보스 패턴)가 이 값을 읽어서 처리한다.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class Enemy : MonoBehaviour
    {
        [SerializeField] EnemyRank rank;
        [SerializeField] RewardTableData rewardTable;
        [SerializeField] EnemyPhaseData phaseInfo;
        [SerializeField] Transform hudAnchor;
        [SerializeField] float sightRange = 5f;
        [SerializeField, Range(0.01f, 1f)] float outSightTrackingPercent = 0.5f; // 시야 밖에서 플레이어를 추적할 때, 시야 안에서보다 얼마나 느리게 추적할지 비율

        internal static event Action<Enemy, bool> PresenceChanged;
        internal IReadOnlyList<uint> PhaseThresholds => phaseInfo && phaseInfo.PhaseTrigger_HPRatio_Permil != null
            ? phaseInfo.PhaseTrigger_HPRatio_Permil : Array.Empty<uint>();
        internal Vector3 HudAnchorPosition => hudAnchor ? hudAnchor.position : transform.position + Vector3.up
            * (TryGetComponent(out NavMeshAgent agent) ? agent.height * Mathf.Abs(transform.lossyScale.y) + .3f : 2f);

        public EnemyRank Rank => rank;
        public RewardTableData RewardTable => rewardTable;
        public float SightRange => sightRange;
        public float OutSightTrackingPercent => outSightTrackingPercent;
        public EnemyAggro Aggro { get; private set; }

        public uint Phase { get; private set; } = 1;
        public bool IsPhaseTransitioning => PendingPhase != 0;
        public event Action<uint, uint> PhaseTransitionStarted;
        public event Action<uint, uint> PhaseChanged;

        internal uint PendingPhase { get; private set; }
        private Health health;
        private EnemyAI ai;
        private Coroutine phaseRoutine;
        private Action restoreParty;

        /// <summary>살아있는 플레이어 목록. 스테이지가 관리하므로 사망 시 자동으로 빠진다.</summary>
        public IReadOnlyList<Transform> Players { get; private set; } = Array.Empty<Transform>();

        private void OnEnable()
        {
            Phase = 1;
            PresenceChanged?.Invoke(this, true);
        }
        private void OnDisable()
        {
            if (phaseRoutine != null) StopCoroutine(phaseRoutine);
            phaseRoutine = null;
            restoreParty = null;
            PendingPhase = 0;
            if (health)
            {
                health.DamageApplied -= OnPhaseBoundaryHit;
                health.LimitHealthAfterDamage = null;
                health.PhaseDamageLocked = false;
            }
            PresenceChanged?.Invoke(this, false);
        }

        private void Awake()
        {
            health = GetComponent<Health>();
            ai = GetComponent<EnemyAI>();
            Aggro = GetComponent<EnemyAggro>();
            if (!Aggro) Aggro = gameObject.AddComponent<EnemyAggro>();
        }

        /// <summary>스테이지가 스폰 직후 호출. OnEnable보다 늦으므로 Players는 Update부터 읽을 것.</summary>
        public void Init(IReadOnlyList<Transform> players)
        {
            Players = players;
            Phase = 1;

            CalcPhase();
        }

        internal void EnableBossPhases(Action restoreHealth)
        {
            if (Rank != EnemyRank.Boss || PhaseThresholds.Count < 2) return;
            var thresholds = PhaseThresholds;
            if (thresholds[0] != 1000)
            {
                Debug.LogError("Boss phase thresholds must start at 1000 permil.", this);
                return;
            }
            for (int i = 1; i < thresholds.Count; i++)
            {
                if (thresholds[i] == 0 || thresholds[i] >= thresholds[i - 1])
                {
                    Debug.LogError("Boss phase thresholds must be positive and strictly descending.", this);
                    return;
                }
            }
            restoreParty = restoreHealth;
            health.LimitHealthAfterDamage = ClampToPhaseBoundary;
            health.DamageApplied += OnPhaseBoundaryHit;
        }

        private float ClampToPhaseBoundary(float nextHealth)
        {
            var thresholds = PhaseThresholds;
            if (Phase >= (uint)thresholds.Count) return nextHealth;

            float boundary = health.MaxHealth * (thresholds[(int)Phase] / 1000f);
            if (nextHealth > boundary) return nextHealth;

            PendingPhase = Phase + 1;
            health.PhaseDamageLocked = true;
            return boundary;
        }

        private void OnPhaseBoundaryHit(DamageAppliedInfo _)
        {
            if (IsPhaseTransitioning && phaseRoutine == null)
                phaseRoutine = StartCoroutine(AdvancePhase());
        }

        private IEnumerator AdvancePhase()
        {
            uint previous = Phase;
            uint next = PendingPhase;
            PhaseTransitionStarted?.Invoke(previous, next);
            yield return null;
            while (ai && ai.IsExecutingStep) yield return null;

            if (phaseInfo && float.IsFinite(phaseInfo.TransitionSeconds) && phaseInfo.TransitionSeconds > 0f)
                yield return new WaitForSeconds(phaseInfo.TransitionSeconds);

            restoreParty?.Invoke();
            Phase = next;
            PendingPhase = 0;
            health.PhaseDamageLocked = false;
            phaseRoutine = null;
            PhaseChanged?.Invoke(previous, next);
        }

        internal void ApplyNetworkPhase(uint phase, uint pending)
        {
            uint previous = Phase;
            uint previousPending = PendingPhase;
            Phase = phase;
            PendingPhase = pending;
            if (pending != 0 && pending != previousPending) PhaseTransitionStarted?.Invoke(phase, pending);
            if (phase != previous) PhaseChanged?.Invoke(previous, phase);
        }

        public void SetPhase(uint phase) => Phase = phase;
        public void CalcPhase()
        {
            if (health.LimitHealthAfterDamage != null || PhaseThresholds.Count <= 1)
                return;
            
            if (!health) 
                return;
            
            float healthRatio = health.Current / health.MaxHealth;

            for(int i = phaseInfo.PhaseTrigger_HPRatio_Permil.Length - 1; i >= 0; i--)
            {
                if (healthRatio <= phaseInfo.PhaseTrigger_HPRatio_Permil[i] / 1000f)
                {
                    Phase = (uint)(i + 1);

                    break;
                }
            }
        }
    }
}
