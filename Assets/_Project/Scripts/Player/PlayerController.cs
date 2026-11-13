using BlueReaper.Core;
using Unity.Netcode;
using UnityEngine;

namespace BlueReaper.Player
{
    /// <summary>
    /// 플레이어의 "두뇌". 입력(PlayerInputReader)을 읽어 몸(Motor, Attack)에 명령을 내리고,
    /// Core 규칙(사망 이벤트)을 지킨다.
    /// </summary>
    [RequireComponent(typeof(Health), typeof(PlayerMotor), typeof(PlayerInputReader))]
    [RequireComponent(typeof(AutoAim), typeof(SwordAttack), typeof(PlayerLife))]
    public class PlayerController : NetworkBehaviour
    {
        [Tooltip("공격 후 이 시간 동안은 이동 방향이 아니라 공격 방향을 바라본다")]
        [SerializeField] float attackFacingHold = 0.25f;

        // 대시 무적은 주인 PC만 알고 데미지 판정은 호스트가 하므로, 주인이 호스트에 알려 준다
        readonly NetworkVariable<bool> syncedInvincible =
            new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        Health health;
        PlayerLife life;
        PlayerMotor motor;
        PlayerInputReader input;
        AutoAim autoAim;
        SwordAttack attack;

        // 남의 캐릭터라서 끈 컴포넌트를 네트워크가 끝나면 원래대로 돌려놓기 위해
        bool inputWasEnabled, motorWasEnabled, disabledForRemote;

        /// <summary>이 PC가 조작하는 캐릭터인지. 네트워크를 안 켰으면(샌드박스) 내 캐릭터로 본다.</summary>
        bool IsMine => !IsSpawned || IsOwner;

        void Awake()
        {
            health = GetComponent<Health>();
            life = GetComponent<PlayerLife>();
            motor = GetComponent<PlayerMotor>();
            input = GetComponent<PlayerInputReader>();
            autoAim = GetComponent<AutoAim>();
            attack = GetComponent<SwordAttack>();
        }

        void OnEnable() => input.DashPressed += OnDashPressed;

        void OnDisable() => input.DashPressed -= OnDashPressed;

        public override void OnNetworkSpawn()
        {
            if (IsOwner)
            {
                // 내 캐릭터면 카메라가 나를 따라오게 (조작을 꺼 둔 더미는 제외)
                if (enabled && Camera.main && Camera.main.TryGetComponent(out TopDownCamera cam))
                    cam.SetTarget(transform);
                return;
            }

            // 남의 캐릭터: 내 키보드를 읽지 않고, 위치는 NetworkTransform이 주인 PC 값으로 맞춘다
            inputWasEnabled = input.enabled;
            motorWasEnabled = motor.enabled;
            input.enabled = false;
            motor.enabled = false;
            disabledForRemote = true;
        }

        public override void OnNetworkDespawn()
        {
            if (!disabledForRemote) return;
            input.enabled = inputWasEnabled;
            motor.enabled = motorWasEnabled;
            disabledForRemote = false;
        }

        void Update()
        {
            if (IsSpawned)
            {
                if (IsOwner) syncedInvincible.Value = health.Invincible;
                else if (IsServer) health.Invincible = syncedInvincible.Value;
            }
            if (!IsMine) return;

            // 빈사·사망 중엔 조작 불가 (입력은 계속 읽지만 몸에 전달하지 않음)
            if (life.State != LifeState.Alive)
            {
                motor.SetMoveInput(Vector3.zero);
                return;
            }

            motor.SetMoveInput(input.MoveDirection);

            if (input.AttackHeld && !motor.IsDashing)
            {
                Vector3 direction = autoAim.Resolve(input.AimDirection);
                if (attack.TryAttack(direction)) motor.FaceInstant(direction, attackFacingHold);
            }
        }

        void OnDashPressed()
        {
            if (life.State == LifeState.Alive) motor.TryDash();
        }
    }
}
