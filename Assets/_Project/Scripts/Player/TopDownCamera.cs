using UnityEngine;

namespace BlueReaper.Player
{
    /// <summary>탑다운 75° 고정 시점으로 대상을 따라가는 카메라 (기획서 7장).</summary>
    public class TopDownCamera : MonoBehaviour
    {
        [Tooltip("따라갈 플레이어 (이 PC의 플레이어)")]
        [SerializeField] Transform target;
        [SerializeField, Range(30f, 90f)] float pitch = 75f;
        [SerializeField] float distance = 18f;
        [SerializeField] float smoothTime = 0.08f;

        Vector3 velocity;
        // 흔들림을 뺀 "원래" 위치. 흔들림이 따라가기 계산에 섞이지 않게 따로 둔다
        Vector3 followPosition;
        float shakeLeft, shakeDuration, shakeStrength;

        void Awake() => followPosition = transform.position;

        /// <summary>따라갈 대상을 바꾼다. 네트워크에서 내 캐릭터가 생겼을 때 부른다.</summary>
        public void SetTarget(Transform newTarget) => target = newTarget;

        void LateUpdate()
        {
            if (target == null) return;

            Quaternion rotation = Quaternion.Euler(pitch, 0f, 0f);
            Vector3 desired = target.position - rotation * Vector3.forward * distance;
            followPosition = Vector3.SmoothDamp(followPosition, desired, ref velocity, smoothTime);
            transform.SetPositionAndRotation(followPosition + ShakeOffset(), rotation);
        }

        /// <summary>카메라를 잠깐 흔든다. strength = 최대 흔들림(m), duration = 실제 시간(초)</summary>
        public void Shake(float strength, float duration)
        {
            shakeStrength = strength;
            shakeDuration = duration;
            shakeLeft = duration;
        }

        Vector3 ShakeOffset()
        {
            if (shakeLeft <= 0f) return Vector3.zero;
            // 히트스톱으로 게임 시간이 멈춰도 흔들림은 진행되도록 실제 시간으로 잰다
            shakeLeft -= Time.unscaledDeltaTime;
            float power = shakeStrength * Mathf.Clamp01(shakeLeft / shakeDuration);
            Vector2 r = Random.insideUnitCircle * power;
            return new Vector3(r.x, 0f, r.y);
        }
    }
}
