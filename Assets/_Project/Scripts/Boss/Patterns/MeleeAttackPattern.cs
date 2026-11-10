using UnityEngine;

namespace BlueReaper.Data
{
    [CreateAssetMenu(
        fileName = "MeleeAttackPattern",
        menuName = "BlueReaper/Melee Attack Pattern")]
    public sealed class MeleeAttackPattern : ScriptableObject
    {
        // 공격 시작 후 판정 시작까지의 지연 시간(초)
        public float HitStartSeconds = 0.2f;

        // 판정이 활성화되는 전체 유지 시간(초)
        public float HitDurationSeconds = 0.15f;

        // 활성 구간 안에서 판정을 반복할지 여부
        public bool RepeatHits;

        // 반복 판정 사이의 간격(초). 반복하지 않으면 무시
        public float HitRepeatIntervalSeconds = 0.2f;

        // 공격자의 공격력에 곱하는 데미지 배율
        public float DamageMultiplier = 1f;
    }
}
