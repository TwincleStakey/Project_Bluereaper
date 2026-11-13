using System;
using BlueReaper.Core;
using Unity.Netcode;
using UnityEngine;

namespace BlueReaper.Player
{
    public enum LifeState { Alive, Downed, Dead }

    /// <summary>
    /// 플레이어 생존 상태. 정상 → (HP 0) 빈사 → (동료 구조) 정상 / (시간 초과) 사망.
    /// 규칙: 홍유정 UI 기획 "파티원 실시간 상태창". 수치는 [가안].
    /// 네트워크: 체력·상태·구조 게이지는 호스트가 정하고, 팀원 PC는 받아서 보여 주기만 한다.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class PlayerLife : NetworkBehaviour
    {
        [Tooltip("빈사 후 이 시간 안에 구조 못 받으면 사망 [가안: 기획서 예시 15초]")]
        [SerializeField] float bleedOutTime = 15f;
        [Tooltip("구조 게이지를 다 채우는 데 걸리는 시간 [가안]")]
        [SerializeField] float rescueTime = 3f;
        [Tooltip("구조되면 최대 체력의 이 비율로 일어난다 [가안]")]
        [SerializeField, Range(0.01f, 1f)] float reviveHealthRatio = 0.3f;

        // 호스트만 쓰고 모두가 읽는다 (NetworkVariable 기본 권한)
        readonly NetworkVariable<float> syncedHealth = new();
        readonly NetworkVariable<LifeState> syncedState = new();
        readonly NetworkVariable<float> syncedRescue = new();
        /// <summary>빈사가 사망으로 바뀌는 서버 시각. 늦게 들어온 팀원도 남은 시간을 맞게 본다.</summary>
        readonly NetworkVariable<double> syncedBleedOutEnd = new();

        Health health;

        public LifeState State { get; private set; } = LifeState.Alive;
        /// <summary>사망까지 남은 시간(초). UI 카운트다운용.</summary>
        public float BleedOutLeft { get; private set; }
        /// <summary>빈사 → 사망까지 전체 시간(초). 남은 시간 링 비율 계산용.</summary>
        public float BleedOutTime => bleedOutTime;
        /// <summary>구조 게이지 0~1.</summary>
        public float RescueProgress { get; private set; }

        public event Action<LifeState> StateChanged;

        /// <summary>이 PC가 체력·상태를 정하는지. 네트워크를 안 켰으면(샌드박스) 내가 정한다.</summary>
        bool IsAuthority => !IsSpawned || IsServer;

        void Awake() => health = GetComponent<Health>();

        void OnEnable() => health.Died += OnHealthDepleted;

        void OnDisable() => health.Died -= OnHealthDepleted;

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                // 처음 접속한 팀원도 바로 맞는 값을 받도록 스폰 때 채워 둔다
                syncedHealth.Value = health.Current;
                syncedState.Value = State;
                return;
            }

            health.NetworkDriven = true;
            syncedHealth.OnValueChanged += OnSyncedHealth;
            syncedState.OnValueChanged += OnSyncedState;
            health.ApplyNetworkHealth(syncedHealth.Value);
            OnSyncedState(State, syncedState.Value);
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer) return;
            syncedHealth.OnValueChanged -= OnSyncedHealth;
            syncedState.OnValueChanged -= OnSyncedState;
            health.NetworkDriven = false;
        }

        void Update()
        {
            if (IsSpawned && IsServer)
            {
                // 값이 같으면 보내지 않으므로 매 프레임 넣어도 된다
                syncedHealth.Value = health.Current;
                syncedRescue.Value = RescueProgress;
            }
            else if (!IsAuthority) RescueProgress = syncedRescue.Value;

            if (State != LifeState.Downed) return;

            BleedOutLeft = IsAuthority
                ? Mathf.Max(0f, BleedOutLeft - Time.deltaTime)
                : Mathf.Max(0f, (float)(syncedBleedOutEnd.Value - NetworkManager.ServerTime.Time));
            // 팀원 PC는 카운트다운만 보여 주고, 사망은 호스트가 알려 줄 때까지 기다린다
            if (IsAuthority && BleedOutLeft <= 0f) SetState(LifeState.Dead);
        }

        /// <summary>구조하는 플레이어가 버튼을 누르고 있는 동안 매 프레임 부른다. 다 차면 일어난다. 호스트에서만 효과가 있다.</summary>
        public void AddRescueProgress(float deltaTime)
        {
            if (!IsAuthority || State != LifeState.Downed) return;

            RescueProgress = Mathf.Min(1f, RescueProgress + deltaTime / rescueTime);
            if (RescueProgress < 1f) return;

            // Revive가 Health.Revived를 먼저 쏘고, 그다음 상태를 Alive로
            health.Revive(reviveHealthRatio);
            SetState(LifeState.Alive);
        }

        void OnHealthDepleted()
        {
            // 팀원 PC는 HP 0을 봐도 상태는 호스트가 보낸 값(OnSyncedState)으로 바꾼다
            if (!IsAuthority || State != LifeState.Alive) return;

            EnterDowned();
            SetState(LifeState.Downed);
        }

        void OnSyncedHealth(float _, float value) => health.ApplyNetworkHealth(value);

        void OnSyncedState(LifeState _, LifeState next)
        {
            if (next == State) return;
            if (next == LifeState.Downed) EnterDowned();
            SetState(next);
        }

        void EnterDowned()
        {
            BleedOutLeft = bleedOutTime;
            RescueProgress = 0f;
            if (IsSpawned && IsServer) syncedBleedOutEnd.Value = NetworkManager.ServerTime.Time + bleedOutTime;
        }

        void SetState(LifeState next)
        {
            State = next;
            if (IsSpawned && IsServer) syncedState.Value = next;
            StateChanged?.Invoke(next);
            // 적 추적 목록에서 빼는 건 StageController가 Health.Died로 처리 (구조 후 다시 넣기는 Health.Revived — 성규님과 협의)
            if (next == LifeState.Dead) GameEvents.RaisePlayerDied(transform);
        }
    }
}
