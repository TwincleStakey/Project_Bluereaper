using BlueReaper.Core;
using UnityEngine;

namespace BlueReaper.Player
{
    /// <summary>이동·회전·대시(무적 포함). 입력을 직접 읽지 않고 명령만 받는다.</summary>
    [RequireComponent(typeof(CharacterController), typeof(Health), typeof(Unit))]
    public class PlayerMotor : MonoBehaviour
    {
        [Header("이동 (속도는 UnitData.MoveSpeed)")]
        [SerializeField] float turnSpeed = 1080f; // 초당 회전 각도

        [Header("대시")]
        [SerializeField] float dashDistance = 5f;
        [SerializeField] float dashDuration = 0.18f; // 이 시간 동안 무적
        [SerializeField] float dashCooldown = 0.6f;

        CharacterController controller;
        Health health;
        Unit unit;

        Vector3 moveInput;
        float verticalSpeed;
        float facingHoldLeft;

        Vector3 dashDirection;
        float dashTimeLeft;
        float dashCooldownLeft;

        public bool IsDashing => dashTimeLeft > 0f;

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            health = GetComponent<Health>();
            unit = GetComponent<Unit>();
        }

        void OnDisable()
        {
            // 대시 도중 꺼지면 무적이 켜진 채로 남지 않게
            if (IsDashing) health.Invincible = false;
            dashTimeLeft = 0f;
        }

        public void Teleport(Vector3 position, Quaternion rotation)
        {
            if (IsDashing) health.Invincible = false;
            dashTimeLeft = 0f;
            verticalSpeed = 0f;
            facingHoldLeft = 0f;
            moveInput = Vector3.zero;

            bool controllerEnabled = controller.enabled;
            controller.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            controller.enabled = controllerEnabled;
        }

        public void SetMoveInput(Vector3 direction) => moveInput = Vector3.ClampMagnitude(direction, 1f);

        /// <summary>즉시 방향을 돌리고, holdTime 동안은 이동 방향으로 돌아가지 않는다 (공격 방향 유지용).</summary>
        public void FaceInstant(Vector3 direction, float holdTime)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) return;
            transform.rotation = Quaternion.LookRotation(direction);
            facingHoldLeft = holdTime;
        }

        public bool TryDash()
        {
            if (IsDashing || dashCooldownLeft > 0f) return false;

            dashDirection = moveInput.sqrMagnitude > 0.01f ? moveInput.normalized : transform.forward;
            dashTimeLeft = dashDuration;
            dashCooldownLeft = dashCooldown;
            health.Invincible = true;
            FaceInstant(dashDirection, 0f);
            return true;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            dashCooldownLeft -= dt;
            facingHoldLeft -= dt;

            Vector3 horizontal;
            if (IsDashing)
            {
                horizontal = dashDirection * (dashDistance / dashDuration);
                dashTimeLeft -= dt;
                if (dashTimeLeft <= 0f) health.Invincible = false;
            }
            else
            {
                horizontal = moveInput * unit.Data.MoveSpeed;
                if (facingHoldLeft <= 0f && moveInput.sqrMagnitude > 0.01f)
                {
                    var target = Quaternion.LookRotation(moveInput);
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnSpeed * dt);
                }
            }

            // 점프는 없지만 경사·턱에서 바닥에 붙어 있도록 중력은 준다
            verticalSpeed = controller.isGrounded ? -2f : verticalSpeed + Physics.gravity.y * dt;
            controller.Move((horizontal + Vector3.up * verticalSpeed) * dt);
        }
    }
}
