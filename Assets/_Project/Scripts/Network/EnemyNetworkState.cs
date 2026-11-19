using System;
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
        private readonly NetworkVariable<float> posturePoints = new();
        private readonly NetworkVariable<bool> groggy = new();
        private Health health;
        private Posture posture;
        internal event Action<float, float> BossHit;
        private Shield shield;
        private StageController stage;

        private void Awake()
        {
            health = GetComponent<Health>();
            shield = GetComponent<Shield>();
            if (GetComponent<Enemy>().Rank == EnemyRank.Boss) posture = GetComponent<Posture>();
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                hp.Value = health.Current;
                shieldPoints.Value = shield ? shield.CurrentShield : 0f;
                UpdatePosture();
                if (GetComponent<Enemy>().Rank == EnemyRank.Boss) health.DamageApplied += PublishBossHit;
                return;
            }

            health.NetworkDriven = true;
            if (TryGetComponent(out EnemyAI ai)) ai.enabled = false;
            if (TryGetComponent(out NavMeshAgent agent)) agent.enabled = false;
            hp.OnValueChanged += ApplyHp;
            shieldPoints.OnValueChanged += ApplyShield;
            ApplyHp(0f, hp.Value);
            ApplyShield(0f, shieldPoints.Value);
            if (posture)
            {
                posture.SetNetworkDriven(true);
                posturePoints.OnValueChanged += OnPostureChanged;
                groggy.OnValueChanged += OnGroggyChanged;
                posture.ApplyNetworkPosture(posturePoints.Value, groggy.Value);
            }
            stage = FindFirstObjectByType<StageController>();
            if (stage) stage.ShowRemoteBoss(GetComponent<Enemy>());
        }

        public override void OnNetworkDespawn()
        {
            health.DamageApplied -= PublishBossHit;
            if (posture)
            {
                posturePoints.OnValueChanged -= OnPostureChanged;
                groggy.OnValueChanged -= OnGroggyChanged;
                posture.SetNetworkDriven(false);
            }
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
            UpdatePosture();
        }

        private void UpdatePosture()
        {
            if (!posture) return;
            posturePoints.Value = posture.CurrentPosture;
            groggy.Value = posture.IsGroggy;
        }

        private void OnPostureChanged(float _, float value) => posture.ApplyNetworkPosture(value, groggy.Value);
        private void OnGroggyChanged(bool _, bool value) => posture.ApplyNetworkPosture(posturePoints.Value, value);

        private void PublishBossHit(DamageAppliedInfo damage) => ShowBossHitRpc(damage.Before, damage.After);

        // Reliable RPCs on this boss NetworkObject preserve the authoritative hit order.
        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        private void ShowBossHitRpc(float before, float after, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId == NetworkManager.ServerClientId)
                BossHit?.Invoke(before, after);
        }

        private void ApplyHp(float _, float value) => health.ApplyNetworkHealth(value);

        private void ApplyShield(float _, float value)
        {
            if (shield) shield.ApplyNetworkShield(value);
        }
    }
}
