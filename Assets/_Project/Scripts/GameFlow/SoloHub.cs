using BlueReaper.Core;
using BlueReaper.Player;
using UnityEngine;

namespace BlueReaper.GameFlow
{
    public sealed class SoloHub : MonoBehaviour
    {
        [SerializeField] Health playerPrefab;
        [SerializeField] Transform spawnPoint;
        [SerializeField] TopDownCamera playerCamera;

        Health player;

        public bool TryEnter(string characterName)
        {
            if (!playerPrefab || !spawnPoint || !playerCamera || player) return false;

            player = Instantiate(playerPrefab, spawnPoint.position, spawnPoint.rotation, transform);
            player.name = characterName;
            playerCamera.SetTarget(player.transform);
            return true;
        }
    }
}
