using BlueReaper.Boss;
using BlueReaper.Core;
using BlueReaper.Stage;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace BlueReaper.Network
{
    /// <summary>
    /// 적(일반·정예·보스) 프리팹에 붙는 네트워크 연결부.
    /// 호스트: 평소처럼 AI가 돌고 데미지를 판정한다. 여기서는 HP를 팀원에게 보내기만 한다.
    /// 팀원 PC: AI·길찾기를 끄고 보여 주기만 한다. 위치는 NetworkTransform, 애니메이션은 NetworkAnimator, HP는 여기서 맞춘다.
    /// </summary>
    [RequireComponent(typeof(Health), typeof(Enemy))]
    public class EnemyNetwork : NetworkBehaviour
    {
        // 호스트만 쓰고 모두가 읽는다
        readonly NetworkVariable<float> syncedHealth = new();

        Health health;
        Enemy enemy;
        EnemyAI ai;
        NavMeshAgent agent;
        StageController stage;

        void Awake()
        {
            health = GetComponent<Health>();
            enemy = GetComponent<Enemy>();
            ai = GetComponent<EnemyAI>();
            agent = GetComponent<NavMeshAgent>();
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                // 풀에서 다시 꺼낸 적도 처음 체력부터 보내도록 스폰 때 채운다
                syncedHealth.Value = health.Current;
                return;
            }

            // 팀원 PC: 판단·이동은 호스트 몫. 여기서 AI가 돌면 적이 PC마다 다르게 움직이고 데미지도 두 번 들어간다
            // (팀원 PC의 적은 연결이 끝나면 NGO가 지우므로 다시 켤 일은 없다)
            if (ai) ai.enabled = false;
            if (agent) agent.enabled = false;
            health.NetworkDriven = true;
            syncedHealth.OnValueChanged += OnSyncedHealth;
            health.ApplyNetworkHealth(syncedHealth.Value);

            // 보스면 팀원 화면에도 보스 HP 바를 띄운다 (호스트는 StageController가 직접 띄움)
            if (enemy.Rank == EnemyRank.Boss)
            {
                stage = FindAnyObjectByType<StageController>();
                if (stage) stage.ShowRemoteBoss(enemy);
            }
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer) return;
            syncedHealth.OnValueChanged -= OnSyncedHealth;
            health.NetworkDriven = false;
            if (stage) stage.HideRemoteBoss(health);
            stage = null;
        }

        void Update()
        {
            // 값이 같으면 보내지 않으므로 매 프레임 넣어도 된다
            if (IsSpawned && IsServer) syncedHealth.Value = health.Current;
        }

        void OnSyncedHealth(float _, float value) => health.ApplyNetworkHealth(value);
    }
}
