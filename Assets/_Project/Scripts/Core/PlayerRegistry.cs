using UnityEngine;

namespace BlueReaper.Core
{
    /// <summary>현재 플레이어 참조. 플레이어가 OnEnable/OnDisable에서 등록·해제한다.</summary>
    public static class PlayerRegistry
    {
        public static Transform Player { get; private set; }

        public static void Register(Transform player) => Player = player;

        public static void Unregister(Transform player)
        {
            if (Player == player) Player = null;
        }

        // Domain Reload를 끈 경우에도 이전 플레이의 참조가 남지 않도록 초기화
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay() => Player = null;
    }
}
