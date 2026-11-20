using System;
using UnityEngine;

namespace BlueReaper.Core
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Health))]
    public sealed class Shield : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float maxShield;
        [SerializeField, Min(0f)] private float initialShield;

        private Health health;
        private float current;
        private double? expiresAt;

        public float MaxShield => float.IsFinite(maxShield) ? Mathf.Max(0f, maxShield) : 0f;
        public float InitialShield => float.IsFinite(initialShield) ? Mathf.Clamp(initialShield, 0f, MaxShield) : 0f;
        public float CurrentShield => HasExpired ? 0f : current;
        /// <summary>현재 실드 전체의 남은 게임 시간. 무기한이면 null.</summary>
        public double? RemainingDuration => expiresAt is double end ? Math.Max(0d, end - Time.timeAsDouble) : null;

        public event Action<float> ShieldChanged;
        public event Action ShieldBroken;

        private bool HasExpired => expiresAt is double end && Time.timeAsDouble >= end;
        private bool CanChange => isActiveAndEnabled && !health.NetworkDriven && !health.IsDead;

        private void Awake() => health = GetComponent<Health>();

        private void OnEnable()
        {
            expiresAt = null;
            current = InitialShield;
            ShieldChanged?.Invoke(current);
        }

        private void OnDisable() => Clear();

        private void Update()
        {
            if (!health.NetworkDriven && HasExpired) Clear();
        }

        /// <summary>최대치까지 더하고 실제로 오른 양을 반환한다. 유효한 타이머는 유지한다.</summary>
        public float Charge(float amount)
        {
            if (!CanChange || !(amount > 0f) || !float.IsFinite(amount)) return 0f;
            if (HasExpired) Clear();
            if (!CanChange) return 0f;
            float before = CurrentShield;
            float next = Mathf.Min(MaxShield, before + amount);
            if (!(next > before)) return 0f;
            current = next;
            ShieldChanged?.Invoke(current);
            return next - before;
        }

        /// <summary>현재 실드 전체의 지속 시간을 설정한다. null은 무기한이다.</summary>
        public bool SetDuration(float? seconds)
        {
            if (!CanChange || !(CurrentShield > 0f)
                || (seconds is float duration && (!(duration > 0f) || !float.IsFinite(duration)))) return false;
            expiresAt = seconds.HasValue ? Time.timeAsDouble + seconds.Value : null;
            return true;
        }

        internal float Absorb(float amount, out bool broken)
        {
            float absorbed = Mathf.Min(CurrentShield, amount);
            broken = false;
            if (!(absorbed > 0f)) return 0f;

            current -= absorbed;
            broken = current <= 0f;
            if (broken) expiresAt = null;
            return absorbed;
        }

        internal void PublishAbsorption(bool broken)
        {
            ShieldChanged?.Invoke(current);
            if (broken) ShieldBroken?.Invoke();
        }

        internal void ApplyNetworkShield(float value)
        {
            if (!health.NetworkDriven) return;
            expiresAt = null;
            float next = Mathf.Clamp(value, 0f, MaxShield);
            if (next == current) return;
            current = next;
            ShieldChanged?.Invoke(current);
        }

        internal void Clear()
        {
            expiresAt = null;
            if (current <= 0f) return;
            current = 0f;
            ShieldChanged?.Invoke(current);
        }
    }
}
