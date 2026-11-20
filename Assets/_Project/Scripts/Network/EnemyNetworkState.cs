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
        private readonly NetworkVariable<Vector2> hp = new();
        private readonly NetworkVariable<Vector2Int> bossPhase = new();
        private Enemy enemy;
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
            enemy = GetComponent<Enemy>();
            shield = GetComponent<Shield>();
            if (enemy.Rank == EnemyRank.Boss) posture = GetComponent<Posture>();
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                hp.Value = new Vector2(health.Current, health.MaxHealth);
                shieldPoints.Value = shield ? shield.CurrentShield : 0f;
                UpdatePosture();
                if (enemy.Rank == EnemyRank.Boss)
                {
                    bossPhase.Value = new Vector2Int((int)enemy.Phase, (int)enemy.PendingPhase);
                    health.DamageApplied += PublishBossHit;
                    enemy.PhaseTransitionStarted += PublishBossPhase;
                    enemy.PhaseChanged += PublishBossPhase;
                }
                return;
            }

            health.NetworkDriven = true;
            if (TryGetComponent(out EnemyAI ai)) ai.enabled = false;
            if (TryGetComponent(out NavMeshAgent agent)) agent.enabled = false;
            hp.OnValueChanged += ApplyHp;
            shieldPoints.OnValueChanged += ApplyShield;
            ApplyHp(default, hp.Value);
            if (enemy.Rank == EnemyRank.Boss)
                enemy.ApplyNetworkPhase((uint)bossPhase.Value.x, (uint)bossPhase.Value.y);
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
            enemy.PhaseTransitionStarted -= PublishBossPhase;
            enemy.PhaseChanged -= PublishBossPhase;
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
            hp.Value = new Vector2(health.Current, health.MaxHealth);
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

        private void PublishBossPhase(uint previous, uint next)
        {
            var snapshot = new Vector2Int((int)enemy.Phase, (int)enemy.PendingPhase);
            var currentHealth = new Vector2(health.Current, health.MaxHealth);
            bossPhase.Value = snapshot;
            hp.Value = currentHealth;
            ShowBossPhaseRpc(snapshot, currentHealth);
        }

        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        private void ShowBossPhaseRpc(Vector2Int phase, Vector2 currentHealth, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != NetworkManager.ServerClientId) return;
            ApplyHp(default, currentHealth);
            enemy.ApplyNetworkPhase((uint)phase.x, (uint)phase.y);
        }

        private void ApplyHp(Vector2 _, Vector2 value) => health.ApplyNetworkHealth(value.x, value.y);

        private void ApplyShield(float _, float value)
        {
            if (shield) shield.ApplyNetworkShield(value);
        }
    }
}
