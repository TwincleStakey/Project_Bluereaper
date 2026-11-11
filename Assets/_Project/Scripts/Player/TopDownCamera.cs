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

        public void SetTarget(Transform player)
        {
            target = player;
            velocity = Vector3.zero;
        }

        void LateUpdate()
        {
            if (target == null) return;

            Quaternion rotation = Quaternion.Euler(pitch, 0f, 0f);
            Vector3 desired = target.position - rotation * Vector3.forward * distance;
            transform.SetPositionAndRotation(
                Vector3.SmoothDamp(transform.position, desired, ref velocity, smoothTime),
                rotation);
        }
    }
}
