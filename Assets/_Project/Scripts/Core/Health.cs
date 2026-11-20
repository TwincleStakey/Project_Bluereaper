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
        Shield shield;

        public float MaxHealth => unit.Data.MaxHealth;
        public float Current { get; private set; }
        public bool IsDead => Current <= 0f;

        /// <summary>켜져 있는 동안 데미지를 무시 (대시 무적, 패턴 중 무적 등).</summary>
        public bool Invincible { get; set; }

        /// <summary>켜져 있으면 체력을 네트워크(호스트)만 바꾼다. 이 PC의 데미지·Kill은 무시하고 ApplyNetworkHealth로만 바뀐다.</summary>
        public bool NetworkDriven { get; set; }
        internal bool CombatLocked { get; set; }

        /// <summary>받은 데미지량을 전달.</summary>
        public event Action<float> Damaged;
        public event Action<DamageAppliedInfo> DamageApplied;
        public event Action Died;
        /// <summary>HP 0에서 다시 일어났을 때 (플레이어 구조).</summary>
        public event Action Revived;

        void Awake()
        {
            unit = GetComponent<Unit>();
            shield = GetComponent<Shield>();
        }

        // 오브젝트 풀에서 다시 꺼낼 때(재활성화) 체력이 초기화되도록 OnEnable에서 리셋
        void OnEnable()
        {
            Current = MaxHealth;
            CombatLocked = false;
        }

        public DamageResult TakeDamage(float amount, Unit source = null)
        {
            if (NetworkDriven || CombatLocked || IsDead || Invincible || !(amount > 0f) || !float.IsFinite(amount))
                return DamageResult.Ignored;

            bool broken = false;
            float absorbed = shield && shield.isActiveAndEnabled ? shield.Absorb(amount, out broken) : 0f;
            float remaining = amount - absorbed;
            float next = Mathf.Max(0f, Current - remaining);
            float healthLost = Current - next;
            Current = next;
            var result = healthLost > 0f ? (IsDead ? DamageResult.Killed : DamageResult.Applied)
                : absorbed > 0f ? DamageResult.Absorbed : DamageResult.Ignored;
            if (absorbed > 0f) shield.PublishAbsorption(broken);
            if (!(healthLost > 0f)) return result;
            DamageApplied?.Invoke(new DamageAppliedInfo(source, healthLost));
            Damaged?.Invoke(remaining);
            // Capture this damage result before callbacks can apply further damage.
            if (result == DamageResult.Killed)
            {
                if (shield) shield.Clear();
                Died?.Invoke();
            }
            return result;
        }

        public void Kill()
        {
            if (NetworkDriven || CombatLocked || IsDead) return;

            Current = 0f;
            if (shield) shield.Clear();
            Died?.Invoke();
        }

        /// <summary>HP 0인 대상을 최대 체력의 ratio만큼 채워 다시 일으킨다.</summary>
        public void Revive(float ratio)
        {
            if (CombatLocked || !IsDead) return;

            Current = MaxHealth * Mathf.Clamp(ratio, 0.01f, 1f);
            Revived?.Invoke();
        }

        /// <summary>호스트가 계산한 체력을 그대로 반영하고, 바뀐 만큼 같은 이벤트를 쏜다 (HP 바·빈사가 똑같이 반응하게).</summary>
        public void ApplyNetworkHealth(float value)
        {
            float before = Current;
            Current = Mathf.Clamp(value, 0f, MaxHealth);
            if (Current < before)
            {
                Damaged?.Invoke(before - Current);
                if (IsDead)
                {
                    if (shield) shield.Clear();
                    Died?.Invoke();
                }
            }
            else if (before <= 0f && Current > 0f) Revived?.Invoke();
        }
    }
}
