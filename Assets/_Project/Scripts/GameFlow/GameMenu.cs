using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace BlueReaper.GameFlow
{
    public sealed class GameMenu : MonoBehaviour
    {
        static readonly Color Background = new(0.035f, 0.055f, 0.095f);
        static readonly Color Accent = new(0.29f, 0.76f, 0.92f);
        static readonly Color Ink = new(0.06f, 0.1f, 0.16f);
        static readonly Color White = new(0.92f, 0.95f, 0.98f);

        Font font;
        [SerializeField] private Canvas canvas;
        [SerializeField] private Camera backgroundCamera;
        [SerializeField] private EventSystem events;
        [SerializeField] private GameMenuPanel menuPanelPrefab;
        [SerializeField] private GameMenuPanel inWorldPanelPrefab;
        [SerializeField] private Button buttonPrefab;
        [SerializeField] private Text titlePrefab;
        [SerializeField] private Text labelPrefab;
        [SerializeField] private InputField inputPrefab;
        [SerializeField] private Slider volumePrefab;

        private RectTransform column;
        private GameObject activePanel;

        void Awake()
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (!backgroundCamera)
            {
                backgroundCamera = new GameObject("Menu Camera", typeof(Camera)).GetComponent<Camera>();
                backgroundCamera.transform.SetParent(transform);
                backgroundCamera.depth = -100;
                backgroundCamera.cullingMask = 0;
                backgroundCamera.clearFlags = CameraClearFlags.SolidColor;
                backgroundCamera.backgroundColor = Background;
            }

            if (!events)
            {
                var input = new GameObject("Menu Input", typeof(EventSystem), typeof(InputSystemUIInputModule));
                input.transform.SetParent(transform);
                events = input.GetComponent<EventSystem>();
            }

            if (!canvas)
            {
                var overlay = new GameObject("Game Menu", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                overlay.transform.SetParent(transform);
                canvas = overlay.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 10;
                var scaler = overlay.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1280, 720);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            }
        }

        public void Show(string title, string message = null, bool inWorld = false)
        {
            Hide();
            backgroundCamera.enabled = !inWorld;
            canvas.gameObject.SetActive(true);
            events.gameObject.SetActive(true);

            var prefab = inWorld ? inWorldPanelPrefab : menuPanelPrefab;
            if (prefab && prefab.Content)
            {
                var view = Instantiate(prefab, canvas.transform, false);
                activePanel = view.gameObject;
                column = view.Content;
            }
            else
                CreatePanel(inWorld);

            var heading = AddText(title, inWorld ? 30 : 44, Accent, inWorld ? 48 : 66, titlePrefab);
            if (!titlePrefab) heading.fontStyle = FontStyle.Bold;
            if (!string.IsNullOrWhiteSpace(message)) Label(message);
        }

        private void CreatePanel(bool inWorld)
        {
            var panel = new GameObject("Menu Panel", typeof(Image));
            activePanel = panel;
            var rect = (RectTransform)panel.transform;
            rect.SetParent(canvas.transform, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.sizeDelta = Vector2.zero;
            var panelImage = panel.GetComponent<Image>();
            panelImage.color = inWorld ? Color.clear : Background;
            panelImage.raycastTarget = !inWorld;

            var group = new GameObject("Menu Content", typeof(RectTransform), typeof(VerticalLayoutGroup),
                typeof(ContentSizeFitter));
            column = (RectTransform)group.transform;
            column.SetParent(rect, false);
            column.sizeDelta = new Vector2(inWorld ? 300 : 400, 0);
            if (inWorld)
            {
                column.anchorMin = column.anchorMax = Vector2.right;
                column.pivot = Vector2.right;
                column.anchoredPosition = new Vector2(-30, 32);
                group.AddComponent<Image>().color = new Color(Background.r, Background.g, Background.b, 0.92f);
            }
            var layout = group.GetComponent<VerticalLayoutGroup>();
            layout.spacing = inWorld ? 8 : 12;
            if (inWorld) layout.padding = new RectOffset(16, 16, 8, 12);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            group.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        public void Hide(bool keepInput = false)
        {
            if (activePanel)
            {
                activePanel.SetActive(false);
                Destroy(activePanel);
                activePanel = null;
                column = null;
            }
            if (!keepInput) events.SetSelectedGameObject(null);
            events.gameObject.SetActive(keepInput);
            canvas.gameObject.SetActive(false);
            backgroundCamera.enabled = false;
        }

        public void Label(string text) => AddText(text, 22, White, 34, labelPrefab);

        public void Button(string text, Action action)
        {
            if (buttonPrefab && buttonPrefab.GetComponentInChildren<Text>(true))
            {
                var buttonView = Instantiate(buttonPrefab, column, false);
                buttonView.name = text;
                buttonView.GetComponentInChildren<Text>(true).text = text;
                BindButton(buttonView, action);
                return;
            }

            var row = new GameObject(text, typeof(Image), typeof(Button), typeof(LayoutElement));
            row.transform.SetParent(column, false);
            row.GetComponent<LayoutElement>().preferredHeight = 48;
            row.GetComponent<Image>().color = White;
            var button = row.GetComponent<Button>();
            button.targetGraphic = row.GetComponent<Image>();
            var colors = button.colors;
            colors.highlightedColor = Accent;
            colors.selectedColor = Accent;
            button.colors = colors;
            var label = CreateText(text, 22, Ink, row.transform);
            label.name = "Label";
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.sizeDelta = Vector2.zero;
            BindButton(button, action);
        }

        private void BindButton(Button button, Action action)
        {
            button.interactable = action != null;
            if (action != null) button.onClick.AddListener(() => action());
            if (button.interactable && !events.currentSelectedGameObject)
                events.SetSelectedGameObject(button.gameObject);
        }

        public void Input(string placeholder, string value, Action<string> changed, int length)
        {
            InputField field;
            if (inputPrefab && inputPrefab.textComponent && inputPrefab.placeholder is Text)
                field = Instantiate(inputPrefab, column, false);
            else
            {
                var row = DefaultControls.CreateInputField(new DefaultControls.Resources());
                row.transform.SetParent(column, false);
                row.AddComponent<LayoutElement>().preferredHeight = 42f;
                field = row.GetComponent<InputField>();
                field.textComponent.font = font;
                field.textComponent.color = Ink;
                ((Text)field.placeholder).font = font;
            }
            field.name = placeholder;
            ((Text)field.placeholder).text = placeholder;
            field.characterLimit = length;
            field.SetTextWithoutNotify(value);
            field.onValueChanged.AddListener(text => changed(text));
        }

        public void Volume()
        {
            Text value = AddText($"Master Volume {AudioListener.volume:P0}", 22, White, 34, labelPrefab);
            Slider slider;
            if (volumePrefab)
                slider = Instantiate(volumePrefab, column, false);
            else
            {
                var row = DefaultControls.CreateSlider(new DefaultControls.Resources());
                row.transform.SetParent(column, false);
                row.AddComponent<LayoutElement>().preferredHeight = 40;
                slider = row.GetComponent<Slider>();
                row.transform.Find("Background").GetComponent<Image>().color = Ink;
                slider.fillRect.GetComponent<Image>().color = Accent;
                slider.handleRect.GetComponent<Image>().color = White;
            }
            slider.name = "Master Volume";
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.wholeNumbers = false;
            slider.SetValueWithoutNotify(AudioListener.volume);
            slider.onValueChanged.AddListener(volume =>
            {
                AudioListener.volume = volume;
                value.text = $"Master Volume {volume:P0}";
            });
            events.SetSelectedGameObject(slider.gameObject);
        }

        Text AddText(string text, int size, Color color, float height, Text template)
        {
            if (template)
            {
                var authored = Instantiate(template, column, false);
                authored.text = text;
                return authored;
            }

            var label = CreateText(text, size, color, column);
            label.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
            return label;
        }

        Text CreateText(string text, int size, Color color, Transform parent)
        {
            var label = new GameObject(text, typeof(Text)).GetComponent<Text>();
            label.transform.SetParent(parent, false);
            label.font = font;
            label.text = text;
            label.fontSize = size;
            label.color = color;
            label.alignment = TextAnchor.MiddleCenter;
            label.raycastTarget = false;
            return label;
        }
    }
}
