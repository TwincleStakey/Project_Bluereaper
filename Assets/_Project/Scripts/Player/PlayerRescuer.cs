using UnityEngine;

namespace BlueReaper.Player
{
    /// <summary>
    /// 쓰러진 동료 옆에서 상호작용(F)을 꾹 누르면 구조 게이지를 채운다.
    /// 범위를 벗어나거나 손을 떼면 게이지가 멈춘다(줄지는 않음). [가안]
    /// </summary>
    [RequireComponent(typeof(PlayerLife), typeof(PlayerInputReader))]
    public class PlayerRescuer : MonoBehaviour
    {
        [Tooltip("이 반경 안의 쓰러진 동료를 구조할 수 있다 [가안]")]
        [SerializeField] float rescueRadius = 1.8f;
        [SerializeField] LayerMask playerLayers; // Player

        readonly Collider[] hits = new Collider[8];
        PlayerLife self;
        PlayerInputReader input;

        /// <summary>지금 구조할 수 있는 동료 (없으면 null). "F 구조" 안내 UI용.</summary>
        public PlayerLife Target { get; private set; }
        public bool IsRescuing => Target != null && input.InteractHeld;

        void Awake()
        {
            self = GetComponent<PlayerLife>();
            input = GetComponent<PlayerInputReader>();
        }

        void Update()
        {
            Target = self.State == LifeState.Alive ? FindDownedTeammate() : null;
            if (IsRescuing) Target.AddRescueProgress(Time.deltaTime);
        }

        PlayerLife FindDownedTeammate()
        {
            int count = Physics.OverlapSphereNonAlloc(transform.position, rescueRadius, hits, playerLayers);
            PlayerLife nearest = null;
            float nearestSqr = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                if (!hits[i].TryGetComponent(out PlayerLife other)) continue;
                if (other == self || other.State != LifeState.Downed) continue;

                float sqr = (other.transform.position - transform.position).sqrMagnitude;
                if (sqr < nearestSqr)
                {
                    nearestSqr = sqr;
                    nearest = other;
                }
            }
            return nearest;
        }
    }
}
