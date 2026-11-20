using System;
using BlueReaper.Core;
using BlueReaper.HitBox;
using UnityEngine;

namespace BlueReaper.Player
{
    /// <summary>
    /// 무사 평타 (단타): 바라보는 방향 전방 부채꼴 안의 적 전부에게 데미지.
    /// 1막 기본 평타의 모양·사거리는 기획 미정(디렉션_최종 10-3 "기획에서 할 일") → 지금 값은 가안.
    /// </summary>
    [RequireComponent(typeof(Unit))]
    public class SwordAttack : MonoBehaviour
    {
        [Tooltip("공격력(UnitData.Attack)에 곱하는 배율")]
        [SerializeField] float damageMultiplier = 1f;
        [Tooltip("다음 공격까지 대기 시간(초) [가안]")]
        [SerializeField] float cooldown = 0.3f;
        [Tooltip("사거리(m). 기획서에 수치 없음 → 테스트로 결정 [가안]")]
        [SerializeField] float range = 2.2f;
        [Tooltip("전방 부채꼴 각도 [가안]")]
        [SerializeField, Range(0f, 360f)] float arcAngle = 120f;
        [Tooltip("피격 대상 Unit의 레이어")]
        [SerializeField] LayerMask targetLayers;

        /// <summary>휘두를 때마다: (플레이어 위치, 휘두른 방향)</summary>
        public event Action<Vector3, Vector3> Swung;
        /// <summary>적을 맞힐 때마다: 맞은 지점. 한 번 휘두르기에 여러 번 올 수 있다.</summary>
        public event Action<Vector3> HitLanded;

        // 스킬트리 공격 범위 노드가 사거리를 늘린다 (연출도 이 값을 쓴다)
        public float Range => range * Bonus(SkillStat.AttackRange);
        public float ArcAngle => arcAngle;

        readonly AreaHitQuery areaQuery = new();
        float cooldownLeft;
        Unit unit;
        PlayerSkillTree skills;

        void Awake()
        {
            unit = GetComponent<Unit>();
            skills = GetComponent<PlayerSkillTree>();
        }

        float Bonus(SkillStat stat) => skills ? skills.GetMultiplier(stat) : 1f;

        void Update() => cooldownLeft -= Time.deltaTime;

        /// <summary>연출만 (멀티 팀원 PC). 판정은 호스트가 TryAttack으로 한다.</summary>
        internal bool TrySwing(Vector3 direction)
        {
            if (cooldownLeft > 0f) return false;
            cooldownLeft = cooldown;
            Swung?.Invoke(transform.position, direction);
            return true;
        }

        public bool TryAttack(Vector3 direction)
        {
            if (!TrySwing(direction)) return false;
            Vector3 origin = transform.position;
            Vector3 forward = Vector3.ProjectOnPlane(direction, Vector3.up);
            Quaternion facing = forward.sqrMagnitude >= 0.0001f
                ? Quaternion.LookRotation(forward, Vector3.up)
                : Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            float reach = Range;
            if (!(reach > 0f)) return true;
            var hits = areaQuery.Find(AreaHitShape.Sector, origin, facing,
                new Vector3(reach, reach * 2f, 0f), arcAngle, unit, targetLayers);
            // 공용 스탯표(UnitData)는 그대로 두고, 내 스킬트리 보너스만 곱한다
            var damage = new AttackDamage(unit.Data.Attack * Bonus(SkillStat.Attack), damageMultiplier);

            foreach (var (target, collider) in hits)
            {
                Vector3 point = collider.ClosestPoint(origin);
                HurtboxTarget.TakeDamage(target, collider, damage, unit);
                HitLanded?.Invoke(point);
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
