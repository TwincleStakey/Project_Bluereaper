using BlueReaper.Core;
using BlueReaper.Stage;
using BlueReaper.UI;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace BlueReaper.GameFlow
{
    public sealed class StageEntry : MonoBehaviour
    {
        [SerializeField] private StageController controller;
        [SerializeField] private Health playerPrefab;
        [SerializeField] private HPBarUI playerHp;
        [SerializeField] private StageHudLayout hudLayout = new();
        [SerializeField] private GameObject progressRoot;
        [SerializeField] private Text progressLabel;
        [SerializeField] private Button nextFieldButton;
        [SerializeField] private Image progressFill;
        [SerializeField] private GameObject progressGauge;
        [SerializeField] private CharacterHudPortrait[] characterPortraits;
        [SerializeField] private TMPro.TMP_FontAsset dialogueFont;

        private StageHud hud;
        private bool fieldHudEnded;
        private StageField remoteField;
        private int remoteIndex = -1;
        private RectTransform fallbackProgressFill;
        private bool progressBound;

        private Canvas fallbackCanvas;

        private Canvas GetHudCanvas()
        {
            if (HudWidgets.IsSceneView(hudLayout?.Canvas)) return hudLayout.Canvas;
            var boundCanvas = playerHp ? playerHp.GetComponentInParent<Canvas>(true) : null;
            if (HudWidgets.IsSceneView(boundCanvas)) return boundCanvas;
            if (fallbackCanvas) return fallbackCanvas;

            var root = new GameObject("Stage HUD", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.transform.SetParent(transform, false);
            fallbackCanvas = root.GetComponent<Canvas>();
            fallbackCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scale = root.GetComponent<CanvasScaler>();
            scale.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scale.referenceResolution = new Vector2(1280, 720);
            return fallbackCanvas;
        }

        public StageController Controller => controller;
        public Health PlayerPrefab => playerPrefab;

        public void Bind(Health player)
        {
            if (controller) controller.BindLocalCamera(player);
            if (!HudWidgets.IsSceneView(playerHp)) playerHp = HPBarUI.CreateFallback(GetHudCanvas().transform);
            playerHp.Init(player);
            if (EnsureHud()) hud.Bind(player, playerHp);
        }

        internal void BeginFieldHud()
        {
            fieldHudEnded = false;
            if (hud) hud.BeginField();
        }

        internal void EndFieldHud()
        {
            if (fieldHudEnded) return;
            fieldHudEnded = true;
            if (hud) hud.EndField();
            if (controller) controller.HideBossHUD();
        }

        internal void ShowDialogue(int speakerSlot, int portraitVariant, string dialogue)
        {
            if (!fieldHudEnded && EnsureHud()) hud.ShowDialogue(speakerSlot, portraitVariant, dialogue);
        }

        private bool EnsureHud()
        {
            if (hud) return true;
            var canvas = GetHudCanvas();
            hud = gameObject.AddComponent<StageHud>();
            hud.Init(canvas, hudLayout, characterPortraits, dialogueFont);
            if (fieldHudEnded) hud.EndField();
            return true;
        }

        internal void ShowProgress((StageFieldPhase Phase, int Current, int Goal) progress, bool isHost)
        {
            if (progress.Phase == StageFieldPhase.NextField) EndFieldHud();
            else if (progress.Phase == StageFieldPhase.ExitPortal && controller) controller.HideBossHUD();
            if (remoteField)
                remoteField.SetReadyPadsVisible(remoteField.DynamicSpawner && (progress.Phase is StageFieldPhase.Preparing
                    or StageFieldPhase.Countdown or StageFieldPhase.None));

            if (!progressBound)
            {
                if (progress.Phase == StageFieldPhase.None && !progressRoot) return;
                BindProgressView();
            }
            if (!progressBound) return;

            progressRoot.SetActive(progress.Phase != StageFieldPhase.None);
            progressLabel.text = progress.Phase switch
            {
                StageFieldPhase.Preparing => $"Stand on separate pads  {progress.Current}/{progress.Goal}",
                StageFieldPhase.Countdown => "Battle starting...",
                StageFieldPhase.Combat => $"Purification  {100L * progress.Current / Mathf.Max(1, progress.Goal)}%",
                StageFieldPhase.NextField => isHost ? "Continue to the next field" : "Waiting for the host to continue",
                StageFieldPhase.ExitPortal => "Purification 100%  |  Enter the portal to return",
                _ => ""
            };
            bool canAdvance = isHost && progress.Phase == StageFieldPhase.NextField;
            if (nextFieldButton.enabled != canAdvance && EventSystem.current)
            {
                if (canAdvance) EventSystem.current.SetSelectedGameObject(nextFieldButton.gameObject);
                else if (EventSystem.current.currentSelectedGameObject == nextFieldButton.gameObject)
                    EventSystem.current.SetSelectedGameObject(null);
            }
            nextFieldButton.enabled = canAdvance;
            if (nextFieldButton.targetGraphic == progressLabel) progressLabel.raycastTarget = canAdvance;
            if (progressGauge)
                progressGauge.SetActive(progress.Phase is StageFieldPhase.Combat or StageFieldPhase.NextField or StageFieldPhase.ExitPortal);
            float fill = Mathf.Clamp01(progress.Current / (float)Mathf.Max(1, progress.Goal));
            if (progressFill) progressFill.fillAmount = fill;
            if (fallbackProgressFill) fallbackProgressFill.anchorMax = new Vector2(fill, 1f);
        }

        private void BindProgressView()
        {
            bool sceneRoot = progressRoot && progressRoot.scene.IsValid();
            if (!sceneRoot || !progressLabel || !nextFieldButton
                || !progressLabel.transform.IsChildOf(progressRoot.transform)
                || !nextFieldButton.transform.IsChildOf(progressRoot.transform))
            {
                if (sceneRoot) progressRoot.SetActive(false);
                CreateProgressLabel(GetHudCanvas());
            }
            nextFieldButton.onClick.AddListener(controller.AdvanceClearedField);
            progressBound = true;
        }

        private void OnDestroy()
        {
            if (progressBound && nextFieldButton && controller)
                nextFieldButton.onClick.RemoveListener(controller.AdvanceClearedField);
        }

        private void CreateProgressLabel(Canvas canvas)
        {
            var label = new GameObject("Stage Progress", typeof(RectTransform), typeof(Text), typeof(Outline), typeof(Button));
            progressRoot = label;
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
            var gauge = CreateGaugeImage(rect, "Purification Gauge", new Color(0f, 0f, 0f, 0.65f));
            progressGauge = gauge.gameObject;
            progressFill = null;
            gauge.anchorMin = new Vector2(0.15f, 0f);
            gauge.anchorMax = new Vector2(0.85f, 0f);
            gauge.offsetMin = new Vector2(0f, -6f);
            gauge.offsetMax = Vector2.zero;
            fallbackProgressFill = CreateGaugeImage(gauge, "Purification Fill", new Color(0.25f, 0.79f, 0.94f));
            fallbackProgressFill.anchorMin = Vector2.zero;
            fallbackProgressFill.anchorMax = Vector2.one;
            fallbackProgressFill.offsetMin = fallbackProgressFill.offsetMax = Vector2.zero;
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
            BeginFieldHud();
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
