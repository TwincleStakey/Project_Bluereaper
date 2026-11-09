using BlueReaper.Core;
using UnityEngine;

namespace BlueReaper.Player
{
    /// <summary>
    /// 플레이어의 "두뇌". 입력(PlayerInputReader)을 읽어 몸(Motor, Attack)에 명령을 내리고,
    /// Core 규칙(사망 이벤트)을 지킨다.
    /// </summary>
    [RequireComponent(typeof(Health), typeof(PlayerMotor), typeof(PlayerInputReader))]
    [RequireComponent(typeof(AutoAim), typeof(SwordAttack))]
    public class PlayerController : MonoBehaviour
    {
        [Tooltip("공격 후 이 시간 동안은 이동 방향이 아니라 공격 방향을 바라본다")]
        [SerializeField] float attackFacingHold = 0.25f;

        Health health;
        PlayerMotor motor;
        PlayerInputReader input;
        AutoAim autoAim;
        SwordAttack attack;

        void Awake()
        {
            health = GetComponent<Health>();
            motor = GetComponent<PlayerMotor>();
            input = GetComponent<PlayerInputReader>();
            autoAim = GetComponent<AutoAim>();
            attack = GetComponent<SwordAttack>();
        }

        void OnEnable()
        {
            health.Died += OnDied;
            input.DashPressed += OnDashPressed;
        }

        void OnDisable()
        {
            health.Died -= OnDied;
            input.DashPressed -= OnDashPressed;
        }

        void Update()
        {
            motor.SetMoveInput(input.MoveDirection);

            if (input.AttackHeld && !motor.IsDashing)
            {
                Vector3 direction = autoAim.Resolve(input.AimDirection);
                if (attack.TryAttack(direction)) motor.FaceInstant(direction, attackFacingHold);
            }
        }

        void OnDashPressed() => motor.TryDash();

        void OnDied()
        {
            GameEvents.RaisePlayerDied(transform);
            input.enabled = false;
            motor.enabled = false;
            enabled = false; // 적의 추적 목록에서 빼는 건 StageController가 Health.Died로 처리
        }
    }
}
