using System.Collections.Generic;
using BlueReaper.Core;
using Unity.Netcode;
using UnityEngine;

namespace BlueReaper.Player
{
    [RequireComponent(typeof(Health), typeof(Unit))]
    public sealed class PlayerLifesteal : NetworkBehaviour
    {
        [SerializeField, Range(0f, 1f)] private float ratio = 0.1f;

        private readonly Dictionary<Enemy, Health> watched = new();
        private Health health;
        private Unit unit;
        private bool listening;

        private void Awake()
        {
            health = GetComponent<Health>();
            unit = GetComponent<Unit>();
        }

        public override void OnNetworkSpawn()
        {
            StopListening();
            if (IsServer) StartListening();
        }

        public override void OnNetworkDespawn() => StopListening();

        private void Start()
        {
            if (!IsSpawned) StartListening();
        }

        public override void OnDestroy()
        {
            StopListening();
            base.OnDestroy();
        }

        private void StartListening()
        {
            if (listening) return;
            listening = true;
            Enemy.PresenceChanged += OnEnemyPresenceChanged;
            foreach (var enemy in FindObjectsByType<Enemy>(FindObjectsSortMode.None))
                Watch(enemy);
        }

        private void StopListening()
        {
            if (!listening) return;
            listening = false;
            Enemy.PresenceChanged -= OnEnemyPresenceChanged;
            foreach (var enemyHealth in watched.Values) enemyHealth.DamageApplied -= OnEnemyDamaged;
            watched.Clear();
        }

        private void OnEnemyPresenceChanged(Enemy enemy, bool present)
        {
            if (present) Watch(enemy);
            else Unwatch(enemy);
        }

        private void Watch(Enemy enemy)
        {
            if (!enemy || watched.ContainsKey(enemy) || !enemy.TryGetComponent(out Health enemyHealth)) return;
            enemyHealth.DamageApplied += OnEnemyDamaged;
            watched.Add(enemy, enemyHealth);
        }

        private void Unwatch(Enemy enemy)
        {
            if (!enemy || !watched.Remove(enemy, out Health enemyHealth)) return;
            enemyHealth.DamageApplied -= OnEnemyDamaged;
        }

        private void OnEnemyDamaged(DamageAppliedInfo damage)
        {
            if (damage.Source != unit || !(damage.HealthLost > 0f)) return;
            health.Heal(damage.HealthLost * ratio);
        }
    }
}
