using BlueReaper.Core;
using UnityEngine;

namespace BlueReaper.Player
{
    /// <summary>
    /// 조준 방향 부채꼴 안에서 공격할 적을 골라 방향을 보정한다 (락온 버튼 없음, 기획서 6장).
    /// 보스가 우선, 그다음 가까운 순. 대상이 없으면 원래 조준 방향 그대로.
    /// </summary>
    public class AutoAim : MonoBehaviour
    {
        [SerializeField] float range = 6f;
        [SerializeField, Range(0f, 180f)] float coneAngle = 70f; // 부채꼴 전체 각도
        [SerializeField] LayerMask targetLayers; // Monster, Boss

        readonly Collider[] hits = new Collider[32];
        int bossLayer;

        void Awake() => bossLayer = LayerMask.NameToLayer("Boss");

        public Vector3 Resolve(Vector3 aimDirection)
        {
            Vector3 origin = transform.position;
            int count = Physics.OverlapSphereNonAlloc(origin, range, hits, targetLayers);

            Vector3 best = aimDirection;
            bool bestIsBoss = false;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                Collider hit = hits[i];
                var health = hit.GetComponentInParent<Health>();
                if (health != null && health.IsDead) continue;

                Vector3 toTarget = hit.transform.position - origin;
                toTarget.y = 0f;
                if (Vector3.Angle(aimDirection, toTarget) > coneAngle * 0.5f) continue;

                bool isBoss = hit.gameObject.layer == bossLayer;
                float distance = toTarget.magnitude;
                // 보스가 아닌 후보는 보스 후보를 이길 수 없다. 같은 등급끼리는 가까운 쪽.
                if (bestIsBoss && !isBoss) continue;
                if (isBoss == bestIsBoss && distance >= bestDistance) continue;

                best = toTarget.normalized;
                bestIsBoss = isBoss;
                bestDistance = distance;
            }
            return best;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, range);
        }
    }
}
