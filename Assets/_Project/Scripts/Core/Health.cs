using System;
using UnityEngine;

namespace BlueReaper.Core
{
    /// <summary>체력 컴포넌트. 플레이어·몬스터·보스 공용.</summary>
    public class Health : MonoBehaviour, IDamageable
    {
        [SerializeField, Min(1f)] float maxHealth = 100f;

        public float MaxHealth => maxHealth;
        public float Current { get; private set; }
        public bool IsDead => Current <= 0f;

        /// <summary>켜져 있는 동안 데미지를 무시 (대시 무적, 패턴 중 무적 등).</summary>
        public bool Invincible { get; set; }

        /// <summary>받은 데미지량을 전달.</summary>
        public event Action<float> Damaged;
        public event Action Died;

        // 오브젝트 풀에서 다시 꺼낼 때(재활성화) 체력이 초기화되도록 OnEnable에서 리셋
        void OnEnable() => Current = maxHealth;

        public void TakeDamage(float amount)
        {
            if (IsDead || Invincible || amount <= 0f) return;

            Current = Mathf.Max(0f, Current - amount);
            Damaged?.Invoke(amount);
            if (IsDead) Died?.Invoke();
        }
    }
}
