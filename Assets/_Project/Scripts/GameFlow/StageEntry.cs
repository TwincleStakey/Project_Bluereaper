using BlueReaper.Core;
using BlueReaper.Stage;
using BlueReaper.UI;
using UnityEngine;

namespace BlueReaper.GameFlow
{
    public sealed class StageEntry : MonoBehaviour
    {
        [SerializeField] private StageController controller;
        [SerializeField] private Health playerPrefab;
        [SerializeField] private HPBarUI playerHp;

        private StageField remoteField;
        private int remoteIndex = -1;

        public StageController Controller => controller;
        public Health PlayerPrefab => playerPrefab;

        public void Bind(Health player)
        {
            if (controller) controller.BindLocalCamera(player);
            if (playerHp) playerHp.Init(player);
        }

        public void ShowRemoteField(StageData data, int index)
        {
            if (!controller || !data || data.Fields == null || index == remoteIndex
                || index < 0 || index >= data.Fields.Length || !data.Fields[index]) return;
            if (remoteField)
            {
                remoteField.gameObject.SetActive(false);
                Destroy(remoteField.gameObject);
            }

            var origin = controller.transform;
            var staging = new GameObject("Remote Field");
            staging.transform.SetParent(origin, false);
            staging.SetActive(false);
            remoteField = Instantiate(data.Fields[index], origin.position, origin.rotation, staging.transform);
            foreach (Unit unit in remoteField.GetComponentsInChildren<Unit>(true))
                if (unit) unit.gameObject.SetActive(false);
            remoteField.transform.SetParent(origin, true);
            Destroy(staging);
            remoteIndex = index;
        }
    }
}
