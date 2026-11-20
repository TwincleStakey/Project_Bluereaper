using BlueReaper.Core;
using Unity.Netcode;
using UnityEngine;

namespace BlueReaper.Network
{
    internal static class NetworkWorld
    {
        internal static bool Enabled { get; set; }

        internal static GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            if (!Enabled || !NetworkManager.Singleton || !NetworkManager.Singleton.IsServer
                || !prefab.GetComponent<NetworkObject>())
                return ObjectPool.Spawn(prefab, position, rotation);

            var instance = Object.Instantiate(prefab, position, rotation);
            instance.GetComponent<NetworkObject>().Spawn(destroyWithScene: true);
            return instance;
        }

        internal static void SpawnPlaced(GameObject instance)
        {
            if (Enabled && NetworkManager.Singleton && NetworkManager.Singleton.IsServer
                && instance.TryGetComponent(out NetworkObject networkObject) && !networkObject.IsSpawned)
            {
                instance.transform.SetParent(null, true);
                networkObject.Spawn(destroyWithScene: true);
            }
        }

        internal static void Despawn(GameObject instance)
        {
            if (instance.TryGetComponent(out NetworkObject networkObject) && networkObject.IsSpawned)
            {
                if (NetworkManager.Singleton && NetworkManager.Singleton.IsServer) networkObject.Despawn();
                return;
            }

            instance.SetActive(false);
            ObjectPool.Despawn(instance);
        }
    }
}
