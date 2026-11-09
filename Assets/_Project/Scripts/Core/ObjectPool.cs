using System.Collections.Generic;
using UnityEngine;
using UnityPool = UnityEngine.Pool.ObjectPool<UnityEngine.GameObject>;

namespace BlueReaper.Core
{
    /// <summary>프리팹별 GameObject 풀. Unity 기본 ObjectPool을 감싼 것.</summary>
    public static class ObjectPool
    {
        static readonly Dictionary<GameObject, UnityPool> poolsByPrefab = new();
        static readonly Dictionary<GameObject, UnityPool> poolsByInstance = new();

        public static GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            if (!poolsByPrefab.TryGetValue(prefab, out var pool))
            {
                pool = new UnityPool(
                    createFunc: () =>
                    {
                        var go = Object.Instantiate(prefab);
                        go.SetActive(false);
                        return go;
                    },
                    actionOnRelease: go => go.SetActive(false),
                    actionOnDestroy: go => Object.Destroy(go));
                poolsByPrefab[prefab] = pool;
            }

            // 씬 전환 등으로 파괴된 인스턴스는 건너뛴다
            GameObject instance;
            do { instance = pool.Get(); } while (instance == null);

            poolsByInstance[instance] = pool;
            // 위치를 먼저 잡은 뒤 활성화해야 OnEnable에서 올바른 위치를 본다
            instance.transform.SetPositionAndRotation(position, rotation);
            instance.SetActive(true);
            return instance;
        }

        /// <summary>풀로 반환. 풀에서 나온 게 아니면 Destroy.</summary>
        public static void Despawn(GameObject instance)
        {
            if (poolsByInstance.TryGetValue(instance, out var pool))
                pool.Release(instance);
            else
                Object.Destroy(instance);
        }

        // Domain Reload를 끈 경우에도 이전 플레이의 풀이 남지 않도록 초기화
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay()
        {
            poolsByPrefab.Clear();
            poolsByInstance.Clear();
        }
    }
}
