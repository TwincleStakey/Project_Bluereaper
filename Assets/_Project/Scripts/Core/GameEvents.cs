using System;
using UnityEngine;

namespace BlueReaper.Core
{
    /// <summary>시스템 간(플레이어·보스·UI·스테이지) 전역 이벤트.</summary>
    public static class GameEvents
    {
        public static event Action PlayerDied;
        public static event Action<Health> BossSpawned;
        public static event Action BossDefeated;
        public static event Action StageCleared;

        public static void RaisePlayerDied() => PlayerDied?.Invoke();
        public static void RaiseBossSpawned(Health boss) => BossSpawned?.Invoke(boss);
        public static void RaiseBossDefeated() => BossDefeated?.Invoke();
        public static void RaiseStageCleared() => StageCleared?.Invoke();

        // Domain Reload를 끈 경우에도 이전 플레이의 구독자가 남지 않도록 초기화
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay()
        {
            PlayerDied = null;
            BossSpawned = null;
            BossDefeated = null;
            StageCleared = null;
        }
    }
}
