using System;
using System.Collections.Generic;
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
        [SerializeField] RewardTable rewardTable;

        public EnemyRank Rank => rank;
        public RewardTable RewardTable => rewardTable;

        /// <summary>살아있는 플레이어 목록. 스테이지가 관리하므로 사망 시 자동으로 빠진다.</summary>
        public IReadOnlyList<Transform> Players { get; private set; } = Array.Empty<Transform>();

        /// <summary>스테이지가 스폰 직후 호출. OnEnable보다 늦으므로 Players는 Update부터 읽을 것.</summary>
        public void Init(IReadOnlyList<Transform> players) => Players = players;
    }
}
