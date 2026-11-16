using System;
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
        float timeLeft, playAt;
        bool waiting;
        Func<bool> canPlay;

        internal static void Play(VfxAutoDespawn prefab, Vector3 position, Quaternion rotation,
            float delay = 0f, Func<bool> canPlay = null)
        {
            if (!prefab || !float.IsFinite(delay) || (canPlay != null && !canPlay())) return;
            ObjectPool.Spawn(prefab.gameObject, position, rotation, delay > 0f
                ? instance => instance.GetComponent<VfxAutoDespawn>().SetDelay(delay, canPlay)
                : null);
        }

        void SetDelay(float seconds, Func<bool> allowed)
        {
            playAt = Time.time + seconds;
            waiting = true;
            canPlay = allowed;
        }

        void Awake()
        {
            particles = GetComponentsInChildren<ParticleSystem>(true);
            foreach (var ps in particles)
            {
                var main = ps.main;
                main.playOnAwake = false;
            }
        }

        void OnEnable()
        {
            if (!waiting) Begin();
        }

        void OnDisable()
        {
            waiting = false;
            canPlay = null;
            foreach (var ps in particles)
                if (ps) ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        void Begin()
        {
            waiting = false;
            canPlay = null;
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
            if (waiting)
            {
                if (canPlay != null && !canPlay())
                {
                    ObjectPool.Despawn(gameObject);
                    return;
                }
                if (Time.time >= playAt) Begin();
                return;
            }

            timeLeft -= Time.deltaTime;
            if (timeLeft > 0f) return;
            ObjectPool.Despawn(gameObject);
        }
    }
}
