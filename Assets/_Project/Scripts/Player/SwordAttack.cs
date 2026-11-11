using System;
using System.Collections.Generic;
using BlueReaper.Core;
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
        [SerializeField] LayerMask targetLayers; // Monster, Boss

        /// <summary>휘두를 때마다: (플레이어 위치, 휘두른 방향)</summary>
        public event Action<Vector3, Vector3> Swung;
        /// <summary>적을 맞힐 때마다: 맞은 지점. 한 번 휘두르기에 여러 번 올 수 있다.</summary>
        public event Action<Vector3> HitLanded;

        public float Range => range;
        public float ArcAngle => arcAngle;

        readonly Collider[] hits = new Collider[32];
        // 콜라이더가 여러 개인 적을 한 번 휘두르기에 두 번 때리지 않도록
        readonly HashSet<IDamageable> hitThisSwing = new();
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
            int count = Physics.OverlapSphereNonAlloc(origin, range, hits, targetLayers);
            hitThisSwing.Clear();
            float damage = DamageFormula.Calculate(unit.Data.Attack, damageMultiplier);

            for (int i = 0; i < count; i++)
            {
                Vector3 toTarget = hits[i].transform.position - origin;
                toTarget.y = 0f;
                if (Vector3.Angle(direction, toTarget) > arcAngle * 0.5f) continue;

                var target = hits[i].GetComponentInParent<IDamageable>();
                if (target == null || !hitThisSwing.Add(target)) continue;
                target.TakeDamage(damage);
                HitLanded?.Invoke(hits[i].ClosestPoint(origin));
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
