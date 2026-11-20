using BlueReaper.Player;
using UnityEngine;

namespace BlueReaper.DebugTools
{
    /// <summary>
    /// 무사 모델의 Animator를 부모 플레이어 상태로 움직인다.
    /// 속도는 위치 변화로 재서 팀원 PC(네트워크로 위치만 받는 쪽)에서도 똑같이 돈다.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public sealed class MusaAnimator : MonoBehaviour
    {
        private static readonly int SpeedId = Animator.StringToHash("Speed");
        private static readonly int AttackId = Animator.StringToHash("Attack");
        private static readonly int DashId = Animator.StringToHash("Dash");
        private static readonly int DownId = Animator.StringToHash("Down");

        [SerializeField] private float speedSmoothing = 12f;

        private Animator animator;
        private SwordAttack sword;
        private PlayerMotor motor;
        private PlayerLife life;
        private Vector3 lastPosition;
        private float speed;
        private bool wasDashing;

        private void Awake()
        {
            animator = GetComponent<Animator>();
            sword = GetComponentInParent<SwordAttack>();
            motor = GetComponentInParent<PlayerMotor>();
            life = GetComponentInParent<PlayerLife>();
            lastPosition = transform.position;
        }

        private void OnEnable()
        {
            if (sword != null) sword.Swung += OnSwung;
            if (life != null) life.StateChanged += OnLifeChanged;
        }

        private void OnDisable()
        {
            if (sword != null) sword.Swung -= OnSwung;
            if (life != null) life.StateChanged -= OnLifeChanged;
        }

        private void Update()
        {
            Vector3 delta = transform.position - lastPosition;
            lastPosition = transform.position;
            delta.y = 0f;
            float target = Time.deltaTime > 0f ? delta.magnitude / Time.deltaTime : 0f;
            speed = Mathf.Lerp(speed, target, 1f - Mathf.Exp(-speedSmoothing * Time.deltaTime));
            animator.SetFloat(SpeedId, speed);

            bool dashing = motor != null && motor.IsDashing;
            if (dashing && !wasDashing) animator.SetTrigger(DashId);
            wasDashing = dashing;
        }

        // 평타는 단타 하나 (컨트롤러의 Attack 상태 = Sword_Regular_A, 누를 때마다 처음부터 다시)
        private void OnSwung(Vector3 position, Vector3 direction) => animator.SetTrigger(AttackId);

        private void OnLifeChanged(LifeState state)
        {
            animator.SetBool(DownId, state != LifeState.Alive);
        }
    }
}
