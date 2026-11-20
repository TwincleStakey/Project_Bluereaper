using BlueReaper.Core;
using BlueReaper.Stage;
using BlueReaper.UI;
using UnityEngine;
using UnityEngine.UI;

namespace BlueReaper.GameFlow
{
    public sealed class StageEntry : MonoBehaviour
    {
        [SerializeField] private StageController controller;
        [SerializeField] private Health playerPrefab;
        [SerializeField] private HPBarUI playerHp;

        private StageField remoteField;
        private int remoteIndex = -1;
        private Text progressLabel;
        private Button nextFieldButton;
        private RectTransform gaugeBackground, gaugeFill;

        public StageController Controller => controller;
        public Health PlayerPrefab => playerPrefab;

        public void Bind(Health player)
        {
            if (controller) controller.BindLocalCamera(player);
            if (playerHp) playerHp.Init(player);
        }

        internal void ShowProgress((StageFieldPhase Phase, int Current, int Goal) progress, bool isHost)
        {
            if (remoteField)
                remoteField.SetReadyPadsVisible(progress.Phase is StageFieldPhase.Preparing or StageFieldPhase.Countdown
                    or StageFieldPhase.None);

            if (progress.Phase == StageFieldPhase.None && !progressLabel) return;
            if (!progressLabel) CreateProgressLabel();
            if (!progressLabel) return;

            progressLabel.gameObject.SetActive(progress.Phase != StageFieldPhase.None);
            progressLabel.text = progress.Phase switch
            {
                StageFieldPhase.Preparing => $"Stand on separate pads  {progress.Current}/{progress.Goal}",
                StageFieldPhase.Countdown => "Battle starting...",
                StageFieldPhase.Combat => $"Purification  {100L * progress.Current / Mathf.Max(1, progress.Goal)}%",
                StageFieldPhase.NextField => isHost ? "Continue to the next field" : "Waiting for the host to continue",
                StageFieldPhase.ExitPortal => "Purification 100%  |  Enter the portal to return",
                _ => ""
            };
            nextFieldButton.enabled = isHost && progress.Phase == StageFieldPhase.NextField;
            progressLabel.raycastTarget = nextFieldButton.enabled;
            gaugeBackground.gameObject.SetActive(progress.Phase is StageFieldPhase.Combat or StageFieldPhase.NextField or StageFieldPhase.ExitPortal);
            gaugeFill.anchorMax = new Vector2(Mathf.Clamp01(progress.Current / (float)Mathf.Max(1, progress.Goal)), 1f);
        }

        private void CreateProgressLabel()
        {
            var canvas = playerHp ? playerHp.GetComponentInParent<Canvas>() : null;
            if (!canvas) return;
            var label = new GameObject("Stage Progress", typeof(RectTransform), typeof(Text), typeof(Outline), typeof(Button));
            var rect = (RectTransform)label.transform;
            rect.SetParent(canvas.transform, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -46f);
            rect.sizeDelta = new Vector2(640f, 52f);

            progressLabel = label.GetComponent<Text>();
            progressLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            progressLabel.alignment = TextAnchor.MiddleCenter;
            progressLabel.fontSize = 24;
            progressLabel.color = Color.white;
            progressLabel.resizeTextForBestFit = true;
            progressLabel.resizeTextMinSize = 14;
            progressLabel.resizeTextMaxSize = 24;
            nextFieldButton = label.GetComponent<Button>();
            nextFieldButton.targetGraphic = progressLabel;
            nextFieldButton.onClick.AddListener(controller.AdvanceClearedField);

            gaugeBackground = CreateGaugeImage(rect, "Purification Gauge", new Color(0f, 0f, 0f, 0.65f));
            gaugeBackground.anchorMin = new Vector2(0.15f, 0f);
            gaugeBackground.anchorMax = new Vector2(0.85f, 0f);
            gaugeBackground.offsetMin = new Vector2(0f, -6f);
            gaugeBackground.offsetMax = Vector2.zero;
            gaugeFill = CreateGaugeImage(gaugeBackground, "Purification Fill", new Color(0.25f, 0.79f, 0.94f));
            gaugeFill.anchorMin = Vector2.zero;
            gaugeFill.anchorMax = Vector2.one;
            gaugeFill.offsetMin = gaugeFill.offsetMax = Vector2.zero;
        }

        private static RectTransform CreateGaugeImage(RectTransform parent, string name, Color color)
        {
            var image = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            image.rectTransform.SetParent(parent, false);
            image.color = color;
            image.raycastTarget = false;
            return image.rectTransform;
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
