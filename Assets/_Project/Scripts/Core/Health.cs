using System;
using UnityEngine;

namespace BlueReaper.Core
{
    public readonly struct DamageAppliedInfo
    {
        public readonly Unit Source;
        public readonly float HealthLost;

        public DamageAppliedInfo(Unit source, float healthLost)
        {
            Source = source;
            HealthLost = healthLost;
        }
    }

    /// <summary>체력 컴포넌트. 플레이어·몬스터·보스 공용. 최대 체력은 UnitData에서 읽는다.</summary>
    [RequireComponent(typeof(Unit))]
    public class Health : MonoBehaviour, IDamageable
    {
        Unit unit;

        public float MaxHealth => unit.Data.MaxHealth;
        public float Current { get; private set; }
        public bool IsDead => Current <= 0f;

        /// <summary>켜져 있는 동안 데미지를 무시 (대시 무적, 패턴 중 무적 등).</summary>
        public bool Invincible { get; set; }

        /// <summary>받은 데미지량을 전달.</summary>
        public event Action<float> Damaged;
        public event Action<DamageAppliedInfo> DamageApplied;
        public event Action Died;

        void Awake() => unit = GetComponent<Unit>();

        // 오브젝트 풀에서 다시 꺼낼 때(재활성화) 체력이 초기화되도록 OnEnable에서 리셋
        void OnEnable() => Current = MaxHealth;

        public DamageResult TakeDamage(float amount, Unit source = null)
        {
            if (IsDead || Invincible || !(amount > 0f) || !float.IsFinite(amount)) return DamageResult.Ignored;

            float next = Mathf.Max(0f, Current - amount);
            if (!(next < Current)) return DamageResult.Ignored;
            float healthLost = Current - next;
            Current = next;
            var result = IsDead ? DamageResult.Killed : DamageResult.Applied;
            DamageApplied?.Invoke(new DamageAppliedInfo(source, healthLost));
            Damaged?.Invoke(amount);
            // Capture this damage result before callbacks can apply further damage.
            if (result == DamageResult.Killed) Died?.Invoke();
            return result;
        }

        public void Kill()
        {
            if (IsDead) return;

            Current = 0f;
            Died?.Invoke();
        }
    }
}
