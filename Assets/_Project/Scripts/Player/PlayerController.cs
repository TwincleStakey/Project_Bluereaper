using BlueReaper.Core;
using UnityEngine;

namespace BlueReaper.Player
{
    /// <summary>
    /// 플레이어의 "두뇌". 입력(PlayerInputReader)을 읽어 몸(Motor, Attack)에 명령을 내리고,
    /// Core 규칙(사망 이벤트)을 지킨다.
    /// </summary>
    [RequireComponent(typeof(Health), typeof(PlayerMotor), typeof(PlayerInputReader))]
    [RequireComponent(typeof(AutoAim), typeof(SwordAttack), typeof(PlayerLife))]
    public class PlayerController : MonoBehaviour
    {
        [Tooltip("공격 후 이 시간 동안은 이동 방향이 아니라 공격 방향을 바라본다")]
        [SerializeField] float attackFacingHold = 0.25f;

        PlayerLife life;
        PlayerMotor motor;
        PlayerInputReader input;
        AutoAim autoAim;
        SwordAttack attack;

        void Awake()
        {
            life = GetComponent<PlayerLife>();
            motor = GetComponent<PlayerMotor>();
            input = GetComponent<PlayerInputReader>();
            autoAim = GetComponent<AutoAim>();
            attack = GetComponent<SwordAttack>();
        }

        void OnEnable() => input.DashPressed += OnDashPressed;

        void OnDisable() => input.DashPressed -= OnDashPressed;

        void Update()
        {
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
