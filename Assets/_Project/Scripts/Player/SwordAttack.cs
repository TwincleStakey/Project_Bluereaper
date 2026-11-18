using System;
using System.Collections.Generic;
using BlueReaper.Core;
using UnityEngine;

namespace BlueReaper.Player
{
    /// <summary>
    /// 무사 평타 3연타 (무사 스킬 기획 v1.1, 11-12): 바라보는 방향 전방 120° 부채꼴 안의 적 전부에게 데미지.
    /// 1·2타 공격력 100%, 3타 120%. 적중한 타마다 마나 +6/+6/+8, 궁 게이지 +2/+2/+3% (적 수와 무관하게 타당 1회).
    /// </summary>
    [RequireComponent(typeof(Unit))]
    public class SwordAttack : MonoBehaviour
    {
        const int ComboLength = 3;

        [Tooltip("타별 공격력 배율 [기획 v1.1]")]
        [SerializeField] float[] comboMultipliers = { 1f, 1f, 1.2f };
        [Tooltip("타별 다음 공격까지 대기 시간(초). 3타는 마무리라 길게 [가안]")]
        [SerializeField] float[] comboCooldowns = { 0.3f, 0.3f, 0.55f };
        [Tooltip("타별 적중 시 마나 회복 [기획 v1.1, 재확인 필요]")]
        [SerializeField] float[] comboManaGain = { 6f, 6f, 8f };
        [Tooltip("타별 적중 시 궁극기 게이지 % [기획 v1.1, 재확인 필요]")]
        [SerializeField] float[] comboUltGain = { 2f, 2f, 3f };
        [Tooltip("마지막 공격 후 이 시간 안에 다시 공격해야 다음 타로 이어진다 [가안]")]
        [SerializeField] float comboResetTime = 0.8f;
        [Tooltip("사거리(m). 기획서에 수치 없음 → 테스트로 결정 [가안]")]
        [SerializeField] float range = 2.2f;
        [Tooltip("전방 부채꼴 각도 [기획 v1.1: 120°]")]
        [SerializeField, Range(0f, 360f)] float arcAngle = 120f;
        [SerializeField] LayerMask targetLayers; // Monster, Boss

        /// <summary>휘두를 때마다: (플레이어 위치, 휘두른 방향, 몇 번째 타 0~2)</summary>
        public event Action<Vector3, Vector3, int> Swung;
        /// <summary>적을 맞힐 때마다: (맞은 지점, 몇 번째 타). 한 번 휘두르기에 여러 번 올 수 있다.</summary>
        public event Action<Vector3, int> HitLanded;

        public float Range => range;
        public float ArcAngle => arcAngle;

        readonly Collider[] hits = new Collider[32];
        // 콜라이더가 여러 개인 적을 한 번 휘두르기에 두 번 때리지 않도록
        readonly HashSet<IDamageable> hitThisSwing = new();
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
            int combo = nextCombo;
            nextCombo = (combo + 1) % ComboLength;
            cooldownLeft = comboCooldowns[combo];
            sinceLastSwing = 0f;

            Vector3 origin = transform.position;
            Swung?.Invoke(origin, direction, combo);

            int count = Physics.OverlapSphereNonAlloc(origin, range, hits, targetLayers);
            hitThisSwing.Clear();
            float damage = DamageFormula.Calculate(unit.Data.Attack, comboMultipliers[combo]);

            for (int i = 0; i < count; i++)
            {
                if (!InArc(origin, direction, hits[i])) continue;

                var target = hits[i].GetComponentInParent<IDamageable>();
                if (target == null || !hitThisSwing.Add(target)) continue;
                target.TakeDamage(damage);
                HitLanded?.Invoke(hits[i].ClosestPoint(origin), combo);
            }

            // 자원은 맞힌 타마다 1번 (여러 마리를 맞혀도 같음)
            if (hitThisSwing.Count > 0 && resources != null)
            {
                resources.AddMana(comboManaGain[combo]);
                resources.AddUlt(comboUltGain[combo]);
            }
            return true;
        }

        /// <summary>
        /// 적의 몸 일부라도 부채꼴 안에 들어오면 맞는다.
        /// 중심점만 보면 덩치 큰 적(엘리트·보스)은 몸이 부채꼴에 걸쳐 있어도 빠지므로, 가장 가까운 점도 같이 본다.
        /// </summary>
        bool InArc(Vector3 origin, Vector3 direction, Collider col)
        {
            float half = arcAngle * 0.5f;
            Vector3 toCenter = col.bounds.center - origin; toCenter.y = 0f;
            Vector3 toClosest = col.ClosestPoint(origin) - origin; toClosest.y = 0f;
            // 콜라이더 안에 서 있을 정도로 붙어 있으면 무조건 맞음
            if (toClosest.sqrMagnitude < 0.0001f) return true;
            return Vector3.Angle(direction, toCenter) <= half || Vector3.Angle(direction, toClosest) <= half;
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
