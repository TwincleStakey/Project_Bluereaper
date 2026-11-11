using BlueReaper.Core;
using UnityEngine;

namespace BlueReaper.Vfx
{
    /// <summary>
    /// 이펙트 프리팹 맨 위에 붙인다. 켜질 때마다 파티클을 처음부터 재생하고,
    /// lifetime 초가 지나면 스스로 풀로 돌아간다.
    /// </summary>
    public class VfxAutoDespawn : MonoBehaviour
    {
        [Tooltip("켜진 뒤 이 시간(초)이 지나면 풀로 반납")]
        [SerializeField] float lifetime = 1f;

        ParticleSystem[] particles;
        float timeLeft;

        void Awake() => particles = GetComponentsInChildren<ParticleSystem>(true);

        void OnEnable()
        {
            timeLeft = lifetime;
            foreach (var ps in particles)
            {
                ps.Clear(false);
                ps.Play(false);
            }
        }

        void Update()
        {
            // 게임 시간 기준: 히트스톱으로 시간이 멈추면 이펙트도 같이 멈춘다
            timeLeft -= Time.deltaTime;
            if (timeLeft > 0f) return;
            ObjectPool.Despawn(gameObject);
        }
    }
}
