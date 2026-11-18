using System.Collections.Generic;
using UnityEngine;

namespace BlueReaper.Core
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Health))]
    public sealed class EnemyAggro : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float aggroPerDamage = 1f;

        private readonly Dictionary<Unit, float> scores = new();
        private Health health;
        private Unit owner;

        private void Awake()
        {
            health = GetComponent<Health>();
            owner = GetComponent<Unit>();
        }

        private void OnEnable()
        {
            scores.Clear();
            health.DamageApplied += OnDamageApplied;
        }

        private void OnDisable()
        {
            health.DamageApplied -= OnDamageApplied;
            scores.Clear();
        }

        public float GetAggro(Unit source) => source && scores.TryGetValue(source, out float score) ? score : 0f;

        public void AddAggro(Unit source, float amount)
        {
            if (!isActiveAndEnabled || !source || source == owner || !(amount > 0f) || !float.IsFinite(amount)) return;
            scores[source] = Mathf.Min(float.MaxValue, GetAggro(source) + amount);
        }

        private void OnDamageApplied(DamageAppliedInfo damage) =>
            AddAggro(damage.Source, damage.HealthLost * aggroPerDamage);
    }
}
