using BlueReaper.Core;
using BlueReaper.Vfx;
using UnityEngine;

namespace BlueReaper.Player
{
    /// <summary>
    /// 플레이어의 연출 담당. SwordAttack의 신호를 듣고 이펙트를 띄운다.
    /// 판정(데미지)에는 관여하지 않는다.
    /// </summary>
    [RequireComponent(typeof(SwordAttack), typeof(HitStop))]
    public class PlayerVfx : MonoBehaviour
    {
        [Tooltip("적을 맞힌 지점에 터지는 이펙트 (VFX_SlashHit)")]
        [SerializeField] GameObject hitPrefab;
        [Tooltip("휘두를 때마다 나오는 칼 궤적 (VFX_SlashTrail)")]
        [SerializeField] GameObject trailPrefab;
        [Tooltip("이펙트를 터뜨릴 높이 (플레이어 발 기준, 몸통쯤)")]
        [SerializeField] float hitHeight = 1f;
        [Tooltip("맞힐 때 화면 흔들림 세기(m)")]
        [SerializeField] float shakeStrength = 0.15f;
        [Tooltip("맞힐 때 화면 흔들림 시간(초)")]
        [SerializeField] float shakeDuration = 0.12f;

        SwordAttack attack;
        HitStop hitStop;
        TopDownCamera cameraRig;

        void Awake()
        {
            attack = GetComponent<SwordAttack>();
            hitStop = GetComponent<HitStop>();
        }

        // 카메라는 씬에 있어 프리팹에 미리 연결할 수 없으니, 모든 Awake가 끝난 뒤 이 PC의 메인 카메라에서 찾는다
        void Start()
        {
            if (Camera.main != null) cameraRig = Camera.main.GetComponent<TopDownCamera>();
        }

        void OnEnable()
        {
            attack.Swung += OnSwung;
            attack.HitLanded += OnHitLanded;
        }

        void OnDisable()
        {
            attack.Swung -= OnSwung;
            attack.HitLanded -= OnHitLanded;
        }

        void OnSwung(Vector3 origin, Vector3 direction)
        {
            var trail = ObjectPool.Spawn(trailPrefab, origin, Quaternion.LookRotation(direction));
            // 궤적의 겹(빛·몸통·심·잔상) 전부를 판정과 같은 사거리·각도로 그린다
            foreach (var layer in trail.GetComponentsInChildren<SlashTrail>())
                layer.Play(attack.Range, attack.ArcAngle);
        }

        void OnHitLanded(Vector3 point)
        {
            // 이펙트는 바깥쪽(나 → 맞은 지점)을 바라본다: 절단선이 공격 방향과 직각이 된다
            Vector3 outward = point - transform.position;
            outward.y = 0f;
            if (outward.sqrMagnitude < 0.0001f) outward = transform.forward;

            // ClosestPoint는 발밑 쪽으로 잡히므로 몸통 높이로 올린다
            point.y = transform.position.y + hitHeight;
            ObjectPool.Spawn(hitPrefab, point, Quaternion.LookRotation(outward));

            hitStop.Play();
            if (cameraRig != null) cameraRig.Shake(shakeStrength, shakeDuration);
        }
    }
}
