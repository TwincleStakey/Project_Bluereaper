using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BlueReaper.Player
{
    /// <summary>
    /// 키보드·마우스·패드 입력을 "이동 방향 / 조준 방향 / 공격 / 대시"라는 게임 의도로 바꾼다.
    /// 다른 Player 스크립트는 어떤 장치인지 모른다 → 나중에 네트워크 입력으로 갈아끼우기 쉽다.
    /// </summary>
    public class PlayerInputReader : MonoBehaviour
    {
        const float StickDeadzone = 0.2f;

        [SerializeField] InputActionAsset actions;
        [Tooltip("비워두면 Camera.main")]
        [SerializeField] Camera aimCamera;

        InputActionMap map;
        InputAction move, aimPoint, aimStick, attack, dash, interact;
        bool usingStickAim;
        Vector2 lastMousePosition;

        /// <summary>월드 XZ 평면 방향, 길이 0~1.</summary>
        public Vector3 MoveDirection { get; private set; }
        /// <summary>월드 XZ 평면 방향, 길이 1.</summary>
        public Vector3 AimDirection { get; private set; } = Vector3.forward;
        public bool AttackHeld => attack.IsPressed();
        /// <summary>상호작용(F / 패드 ↓)을 누르고 있는지. 구조처럼 꾹 누르는 동작용.</summary>
        public bool InteractHeld => enabled && interact.IsPressed();
        public event Action DashPressed;

        void Awake()
        {
            map = actions.FindActionMap("Player", throwIfNotFound: true);
            move = map.FindAction("Move", true);
            aimPoint = map.FindAction("AimPoint", true);
            aimStick = map.FindAction("AimStick", true);
            attack = map.FindAction("Attack", true);
            dash = map.FindAction("Dash", true);
            interact = map.FindAction("Interact", true);
            if (aimCamera == null) aimCamera = Camera.main;
        }

        void OnEnable()
        {
            map.Enable();
            dash.performed += OnDash;
        }

        void OnDisable()
        {
            dash.performed -= OnDash;
            map.Disable();
            MoveDirection = Vector3.zero;
        }

        void OnDash(InputAction.CallbackContext _) => DashPressed?.Invoke();

        void Update()
        {
            MoveDirection = Vector3.ClampMagnitude(ToWorld(move.ReadValue<Vector2>()), 1f);

            // 마지막으로 만진 장치로 조준 방식을 고른다
            Vector2 stick = aimStick.ReadValue<Vector2>();
            Vector2 mouse = aimPoint.ReadValue<Vector2>();
            if (stick.sqrMagnitude > StickDeadzone * StickDeadzone) usingStickAim = true;
            else if (mouse != lastMousePosition) usingStickAim = false;
            lastMousePosition = mouse;

            if (!usingStickAim)
            {
                if (TryMouseAim(mouse, out Vector3 dir)) AimDirection = dir;
            }
            else if (stick.sqrMagnitude > StickDeadzone * StickDeadzone)
                AimDirection = ToWorld(stick).normalized;
            else if (MoveDirection.sqrMagnitude > 0.01f)
                AimDirection = MoveDirection.normalized; // 패드: 오른스틱을 놓으면 이동 방향을 조준
        }

        /// <summary>화면 기준 입력(위=화면 위쪽)을 카메라 방향에 맞춘 월드 XZ 벡터로.</summary>
        Vector3 ToWorld(Vector2 input)
        {
            Vector3 forward = Vector3.ProjectOnPlane(aimCamera.transform.forward, Vector3.up).normalized;
            Vector3 right = Vector3.ProjectOnPlane(aimCamera.transform.right, Vector3.up).normalized;
            return forward * input.y + right * input.x;
        }

        /// <summary>마우스 위치에서 쏜 광선이 플레이어 발 높이 평면과 만나는 점 쪽 방향.</summary>
        bool TryMouseAim(Vector2 screenPos, out Vector3 dir)
        {
            dir = default;
            Ray ray = aimCamera.ScreenPointToRay(screenPos);
            var ground = new Plane(Vector3.up, transform.position);
            if (!ground.Raycast(ray, out float distance)) return false;

            Vector3 toPoint = ray.GetPoint(distance) - transform.position;
            toPoint.y = 0f;
            if (toPoint.sqrMagnitude < 0.01f) return false;
            dir = toPoint.normalized;
            return true;
        }
    }
}
