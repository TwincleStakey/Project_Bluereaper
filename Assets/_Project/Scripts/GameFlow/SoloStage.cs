using BlueReaper.Core;
using BlueReaper.Stage;
using BlueReaper.UI;
using UnityEngine;

namespace BlueReaper.GameFlow
{
    public sealed class SoloStage : MonoBehaviour
    {
        [SerializeField] StageController controller;
        [SerializeField] Health playerPrefab;
        [SerializeField] HPBarUI playerHp;

        Health player;

        public StageController Controller => controller;

        public bool TryBegin(StageData data, string characterName)
        {
            if (!controller || !playerPrefab || !playerHp || player) return false;

            player = Instantiate(playerPrefab);
            player.name = characterName;
            if (!controller.TryStartSoloRun(data, player))
            {
                Destroy(player.gameObject);
                return false;
            }

            playerHp.Init(player);
            return true;
        }

        public void StopRun()
        {
            if (controller) controller.enabled = false;
            if (player) player.gameObject.SetActive(false);
        }
    }
}
