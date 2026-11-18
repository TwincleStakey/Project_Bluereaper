using System;
using BlueReaper.Core;
using BlueReaper.HitBox;
using UnityEngine;

namespace BlueReaper.Player
{
    /// <summary>
    /// 무사 평타 3연타 (무사 스킬 기획 v1.1, 11-12): 바라보는 방향 부채꼴 안의 적 전부에게 데미지.
    /// 1·2타 공격력 100%, 3타 120%. 적중한 타마다 마나 +6/+6/+8, 궁 게이지 +2/+2/+3% (적 수와 무관하게 타당 1회).
    /// </summary>
    [RequireComponent(typeof(Unit))]
    public class SwordAttack : MonoBehaviour
    {
        const int ComboLength = 3;

        [Tooltip("공격력(UnitData.Attack)에 곱하는 배율")]
        [SerializeField] float damageMultiplier = 1f;
        [SerializeField] float range = 2.2f;
        [SerializeField, Range(0f, 360f)] float arcAngle = 120f;
        [Tooltip("피격 대상 Unit의 레이어")]
        [SerializeField] LayerMask targetLayers;

        [Header("3연타 [기획 v1.1]")]
        [Tooltip("타별 배율. damageMultiplier에 한 번 더 곱한다")]
        [SerializeField] float[] comboMultipliers = { 1f, 1f, 1.2f };
        [Tooltip("타별 다음 공격까지 대기 시간(초). 3타는 마무리라 길게 [가안]")]
        [SerializeField] float[] comboCooldowns = { 0.3f, 0.3f, 0.55f };
        [Tooltip("타별 적중 시 마나 회복 [기획 v1.1, 재확인 필요]")]
        [SerializeField] float[] comboManaGain = { 6f, 6f, 8f };
        [Tooltip("타별 적중 시 궁극기 게이지 % [기획 v1.1, 재확인 필요]")]
        [SerializeField] float[] comboUltGain = { 2f, 2f, 3f };
        [Tooltip("마지막 공격 후 이 시간 안에 다시 공격해야 다음 타로 이어진다 [가안]")]
        [SerializeField] float comboResetTime = 0.8f;

        /// <summary>휘두를 때마다: (플레이어 위치, 휘두른 방향)</summary>
        public event Action<Vector3, Vector3> Swung;
        /// <summary>적을 맞힐 때마다: 맞은 지점. 한 번 휘두르기에 여러 번 올 수 있다.</summary>
        public event Action<Vector3> HitLanded;

        public float Range => range;
        public float ArcAngle => arcAngle;
        /// <summary>마지막으로 휘두른 타 (0~2). Swung·HitLanded 안에서 읽으면 지금 타다.</summary>
        public int LastCombo { get; private set; }

        readonly AreaHitQuery areaQuery = new();
        float cooldownLeft;
        float sinceLastSwing = float.MaxValue;
        int nextCombo;
        Unit unit;
        PlayerResources resources;

        void Awake()
        {
            unit = GetComponent<Unit>();
            resources = GetComponent<PlayerResources>();
        }

        void Update()
        {
            cooldownLeft -= Time.deltaTime;
            sinceLastSwing += Time.deltaTime;
        }

        public bool TryAttack(Vector3 direction)
        {
            if (cooldownLeft > 0f) return false;
            // 너무 오래 쉬었으면 1타부터 다시
            if (sinceLastSwing > comboResetTime) nextCombo = 0;
            LastCombo = nextCombo;
            nextCombo = (LastCombo + 1) % ComboLength;
            cooldownLeft = comboCooldowns[LastCombo];
            sinceLastSwing = 0f;

            Vector3 origin = transform.position;
            Swung?.Invoke(origin, direction);
            Vector3 forward = Vector3.ProjectOnPlane(direction, Vector3.up);
            Quaternion facing = forward.sqrMagnitude >= 0.0001f
                ? Quaternion.LookRotation(forward, Vector3.up)
                : Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            if (!(range > 0f)) return true;
            var hits = areaQuery.Find(AreaHitShape.Sector, origin, facing,
                new Vector3(range, range * 2f, 0f), arcAngle, unit, targetLayers);
            float damage = DamageFormula.Calculate(unit.Data.Attack, damageMultiplier * comboMultipliers[LastCombo]);

            foreach (var (target, collider) in hits)
            {
                Vector3 point = collider.ClosestPoint(origin);
                target.TakeDamage(damage, unit);
                HitLanded?.Invoke(point);
            }

            // 자원은 맞힌 타마다 1번 (여러 마리를 맞혀도 같음)
            if (hits.Count > 0 && resources != null)
            {
                resources.AddMana(comboManaGain[LastCombo]);
                resources.AddUlt(comboUltGain[LastCombo]);
            }
            return true;
        }

        void OnDrawGizmosSelected()
        {
            // 실제 판정 범위(전방 부채꼴)를 그대로 그린다
            Gizmos.color = Color.red;
            Vector3 o = transform.position + Vector3.up * 0.05f;
            Vector3 f = transform.forward;
            int seg = 24;
            Vector3 prev = o + Quaternion.Euler(0f, -arcAngle * 0.5f, 0f) * f * range;
            Gizmos.DrawLine(o, prev);
            for (int i = 1; i <= seg; i++)
            {
                Vector3 p = o + Quaternion.Euler(0f, -arcAngle * 0.5f + arcAngle * i / seg, 0f) * f * range;
                Gizmos.DrawLine(prev, p);
                prev = p;
            }
            Gizmos.DrawLine(o, prev);
        }
    }
}
