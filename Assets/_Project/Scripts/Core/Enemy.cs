using System;
using System.Collections.Generic;
using BlueReaper.Data;
using BlueReaper.Reward;
using UnityEngine;

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
        [SerializeField] float sightRange = 5f;
        [SerializeField, Range(0.01f, 1f)] float outSightTrackingPercent = 0.5f; // 시야 밖에서 플레이어를 추적할 때, 시야 안에서보다 얼마나 느리게 추적할지 비율

        public EnemyRank Rank => rank;
        public RewardTableData RewardTable => rewardTable;
        public float SightRange => sightRange;
        public float OutSightTrackingPercent => outSightTrackingPercent;

        public uint Phase { get; private set; } = 1;

        /// <summary>살아있는 플레이어 목록. 스테이지가 관리하므로 사망 시 자동으로 빠진다.</summary>
        public IReadOnlyList<Transform> Players { get; private set; } = Array.Empty<Transform>();

        /// <summary>스테이지가 스폰 직후 호출. OnEnable보다 늦으므로 Players는 Update부터 읽을 것.</summary>
        public void Init(IReadOnlyList<Transform> players)
        {
            Players = players;
            Phase = 1;

            CalcPhase();
        }

        public void SetPhase(uint phase) => Phase = phase;
        public void CalcPhase()
        {
            if (phaseInfo == null || phaseInfo.PhaseTrigger_HPRatio_Permil.Length <= 1)
                return;

            var health = GetComponent<Health>();
            
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
