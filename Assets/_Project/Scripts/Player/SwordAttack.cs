using System;
using BlueReaper.Core;
using BlueReaper.HitBox;
using UnityEngine;

namespace BlueReaper.Player
{
    /// <summary>무사 평타: 바라보는 방향 부채꼴 안의 적 전부에게 데미지.</summary>
    [RequireComponent(typeof(Unit))]
    public class SwordAttack : MonoBehaviour
    {
        [Tooltip("공격력(UnitData.Attack)에 곱하는 배율")]
        [SerializeField] float damageMultiplier = 1f;
        [SerializeField] float range = 2.2f;
        [SerializeField, Range(0f, 360f)] float arcAngle = 120f;
        [SerializeField] float cooldown = 0.35f;
        [Tooltip("피격 대상 Unit의 레이어")]
        [SerializeField] LayerMask targetLayers;

        /// <summary>휘두를 때마다: (플레이어 위치, 휘두른 방향)</summary>
        public event Action<Vector3, Vector3> Swung;
        /// <summary>적을 맞힐 때마다: 맞은 지점. 한 번 휘두르기에 여러 번 올 수 있다.</summary>
        public event Action<Vector3> HitLanded;

        public float Range => range;
        public float ArcAngle => arcAngle;

        readonly AreaHitQuery areaQuery = new();
        float cooldownLeft;
        Unit unit;

        void Awake() => unit = GetComponent<Unit>();

        void Update() => cooldownLeft -= Time.deltaTime;

        public bool TryAttack(Vector3 direction)
        {
            if (cooldownLeft > 0f) return false;
            cooldownLeft = cooldown;

            Vector3 origin = transform.position;
            Swung?.Invoke(origin, direction);
            Vector3 forward = Vector3.ProjectOnPlane(direction, Vector3.up);
            Quaternion facing = forward.sqrMagnitude >= 0.0001f
                ? Quaternion.LookRotation(forward, Vector3.up)
                : Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            if (!(range > 0f)) return true;
            var hits = areaQuery.Find(AreaHitShape.Sector, origin, facing,
                new Vector3(range, range * 2f, 0f), arcAngle, unit, targetLayers);
            float damage = DamageFormula.Calculate(unit.Data.Attack, damageMultiplier);

            foreach (var (target, collider) in hits)
            {
                Vector3 point = collider.ClosestPoint(origin);
                target.TakeDamage(damage);
                HitLanded?.Invoke(point);
            }
            return true;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, range);
        }
    }
}
