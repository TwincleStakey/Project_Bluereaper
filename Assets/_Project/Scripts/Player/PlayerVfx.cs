using BlueReaper.Vfx;
using UnityEngine;

namespace BlueReaper.Player
{
    /// <summary>
    /// 플레이어의 연출 담당. SwordAttack의 신호를 듣고 이펙트를 띄운다.
    /// 판정(데미지)에는 관여하지 않는다.
    /// 평타 연출은 VFX 시안 03 수묵 + 광원 (2026-11-14 선택) → InkSlashVfx가 그린다.
    /// </summary>
    [RequireComponent(typeof(SwordAttack), typeof(HitStop), typeof(InkSlashVfx))]
    public class PlayerVfx : MonoBehaviour
    {
        [Tooltip("이펙트를 터뜨릴 높이 (플레이어 발 기준, 몸통쯤)")]
        [SerializeField] float hitHeight = 1f;
        [Tooltip("맞힐 때 화면 흔들림 세기(m)")]
        [SerializeField] float shakeStrength = 0.15f;
        [Tooltip("맞힐 때 화면 흔들림 시간(초)")]
        [SerializeField] float shakeDuration = 0.12f;

        SwordAttack attack;
        HitStop hitStop;
        InkSlashVfx ink;
        TopDownCamera cameraRig;
        int lastHitFrame = -1;

        void Awake()
        {
            attack = GetComponent<SwordAttack>();
            hitStop = GetComponent<HitStop>();
            ink = GetComponent<InkSlashVfx>();
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

        // 먹 획은 판정과 같은 사거리·각도로 그린다 (보이는 범위 = 맞는 범위)
        void OnSwung(Vector3 origin, Vector3 direction) =>
            ink.PlaySwing(origin, direction, attack.Range, attack.ArcAngle);

        void OnHitLanded(Vector3 point)
        {
            Vector3 outward = point - transform.position;
            outward.y = 0f;
            if (outward.sqrMagnitude < 0.0001f) outward = transform.forward;

            // ClosestPoint는 발밑 쪽으로 잡히므로 몸통 높이로 올린다
            point.y = transform.position.y + hitHeight;
            ink.PlayHit(point, outward.normalized, transform.position.y);

            // 여러 마리를 맞혀도 히트스톱·흔들림은 한 번 휘두르기에 한 번만
            if (lastHitFrame == Time.frameCount) return;
            lastHitFrame = Time.frameCount;
            hitStop.Play();
            if (cameraRig != null) cameraRig.Shake(shakeStrength, shakeDuration);
        }
    }
}
