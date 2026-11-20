using BlueReaper.Boss;
using BlueReaper.Core;
using BlueReaper.Stage;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace BlueReaper.Network
{
    [RequireComponent(typeof(Health), typeof(Enemy))]
    public sealed class EnemyNetworkState : NetworkBehaviour
    {
        private readonly NetworkVariable<float> hp = new();
        private readonly NetworkVariable<float> shieldPoints = new();
        private Health health;
        private Shield shield;
        private StageController stage;

        private void Awake()
        {
            health = GetComponent<Health>();
            shield = GetComponent<Shield>();
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                hp.Value = health.Current;
                shieldPoints.Value = shield ? shield.CurrentShield : 0f;
                return;
            }

            health.NetworkDriven = true;
            if (TryGetComponent(out EnemyAI ai)) ai.enabled = false;
            if (TryGetComponent(out NavMeshAgent agent)) agent.enabled = false;
            hp.OnValueChanged += ApplyHp;
            shieldPoints.OnValueChanged += ApplyShield;
            ApplyHp(0f, hp.Value);
            ApplyShield(0f, shieldPoints.Value);
            stage = FindFirstObjectByType<StageController>();
            if (stage) stage.ShowRemoteBoss(GetComponent<Enemy>());
        }

        public override void OnNetworkDespawn()
        {
            hp.OnValueChanged -= ApplyHp;
            shieldPoints.OnValueChanged -= ApplyShield;
            health.NetworkDriven = false;
            if (stage) stage.HideRemoteBoss(health);
        }

        private void Update()
        {
            if (!IsSpawned || !IsServer) return;
            hp.Value = health.Current;
            shieldPoints.Value = shield ? shield.CurrentShield : 0f;
        }

        private void ApplyHp(float _, float value) => health.ApplyNetworkHealth(value);

        private void ApplyShield(float _, float value)
        {
            if (shield) shield.ApplyNetworkShield(value);
        }
    }
}
