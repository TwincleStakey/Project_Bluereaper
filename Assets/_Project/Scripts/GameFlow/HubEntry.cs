using BlueReaper.Core;
using BlueReaper.Player;
using UnityEngine;

namespace BlueReaper.GameFlow
{
    public sealed class HubEntry : MonoBehaviour
    {
        [SerializeField] private Health playerPrefab;
        [SerializeField] private Transform spawnPoint;
        [SerializeField] private TopDownCamera playerCamera;

        public Health PlayerPrefab => playerPrefab;
        public Transform SpawnPoint => spawnPoint;

        public void Bind(Health player)
        {
            if (playerCamera && player) playerCamera.SetTarget(player.transform);
        }
    }
}
