using BlueReaper.Player;
using UnityEngine;

namespace BlueReaper.DebugTools
{
    /// <summary>
    /// Kibaek 씬 전용 임시 아트. 씬에 생기는 플레이어마다 무사 모델을 자식으로 붙이고
    /// 원래 캡슐 몸(Body, Nose)은 숨긴다. Player.prefab은 건드리지 않는다.
    /// </summary>
    public sealed class MusaSkin : MonoBehaviour
    {
        [SerializeField] private MusaAnimator visualPrefab;
        [SerializeField] private string[] hiddenParts = { "Body", "Nose" };

        private float nextScan;

        private void Update()
        {
            if (Time.unscaledTime < nextScan) return;
            nextScan = Time.unscaledTime + 0.5f;

            foreach (PlayerController player in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
            {
                if (player.GetComponentInChildren<MusaAnimator>(true) != null) continue;
                Instantiate(visualPrefab, player.transform, false);
                foreach (string part in hiddenParts)
                {
                    Transform t = player.transform.Find(part);
                    if (t != null && t.TryGetComponent(out MeshRenderer r)) r.enabled = false;
                }
            }
        }
    }
}
